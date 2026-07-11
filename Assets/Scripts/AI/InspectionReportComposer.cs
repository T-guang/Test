using System;
using System.Collections.Generic;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using UnityEngine;

namespace ElectricalSim.AI
{
    public static class InspectionReportComposer
    {
        public static InspectionReportData CreateSummary(
            string title,
            string reportType,
            bool simulationRunning,
            string circuitDisplayName,
            string conclusion,
            string riskLevel)
        {
            var body = new StringBuilder();
            body.AppendLine("报告类型：" + reportType);
            body.AppendLine("生成时间：" + DateTime.Now.ToString("HH:mm:ss"));
            body.AppendLine("当前状态：" + (simulationRunning ? "仿真运行中" : "仿真停止"));
            body.AppendLine("识别电路：" + circuitDisplayName);
            if (string.Equals(reportType, "接线检查", StringComparison.Ordinal))
            {
                body.AppendLine("检查结论：" + conclusion);
                body.AppendLine("风险等级：" + riskLevel);
            }
            else
            {
                body.AppendLine("说明：" + conclusion);
            }

            var data = new InspectionReportData();
            data.Add(new InspectionReportBlock(
                title,
                body.ToString().TrimEnd(),
                InspectionReportBlockKind.Summary,
                InspectionReportSeverity.Information));
            return data;
        }

        public static string BuildCheckSummaryConclusion(CircuitStateResult stateResult, int errorCount, int warningCount)
        {
            if (stateResult != null && (stateResult.HasShortCircuit || stateResult.HasPowerConflict))
            {
                return "当前电路存在短路或电源冲突风险，建议先停止仿真并检查电源与主回路。";
            }

            if (errorCount > 0)
            {
                return "当前电路存在接线风险或逻辑异常，建议先处理“问题与风险”中的错误项。";
            }

            if (warningCount > 0)
            {
                return "当前电路存在需要关注的提醒项，建议按图纸继续核对控制回路和保护回路。";
            }

            return "当前未发现已支持规则范围内的严重接线错误。";
        }

        public static string ResolveRiskLevel(CircuitStateResult stateResult, int errorCount, int warningCount)
        {
            if (stateResult != null && (stateResult.HasShortCircuit || stateResult.HasPowerConflict)) return "错误";
            if (errorCount > 0) return "错误";
            if (warningCount > 0) return "提醒";
            return "正常";
        }

        public static InspectionReportData CreateExplanation(
            string composition,
            string mainCircuit,
            string controlCircuit,
            string actionRelation,
            string runtimeSummary,
            string parameterSummary)
        {
            var data = new InspectionReportData();
            AddSection(data, "电路组成", composition, InspectionReportBlockKind.General);
            AddSection(data, "主回路路径", mainCircuit, InspectionReportBlockKind.General);
            AddSection(data, "控制回路路径", controlCircuit, InspectionReportBlockKind.General);
            AddSection(data, "元件动作关系", actionRelation, InspectionReportBlockKind.General);
            // The legacy runtime summary already contains its own titled sections
            // (for example, the current canvas and auto-reciprocating state).
            // Preserve the former report-block boundaries instead of nesting them
            // inside the "current runtime state" body.
            data.Add(new InspectionReportBlock(
                "当前运行状态",
                string.Empty,
                InspectionReportBlockKind.Runtime,
                InspectionReportSeverity.Information));
            data.AddRange(ParseLegacyText(runtimeSummary));
            data.AddRange(ParseLegacyText(parameterSummary));
            return data;
        }

        public static InspectionReportData CreateTeaching(string text)
        {
            var data = new InspectionReportData();
            AddSection(data, "教学说明", StripLeadingSection(text), InspectionReportBlockKind.Teaching);
            return data;
        }

        public static InspectionReportData ComposeCheckReport(
            InspectionReportData summary,
            string noticesAndFormattedReport,
            IReadOnlyList<CircuitValidationIssue> validationIssues)
        {
            var data = new InspectionReportData();
            data.AddRange(summary);
            data.AddRange(ParseLegacyText(noticesAndFormattedReport, validationIssues));
            return data;
        }

        public static InspectionReportData ParseLegacyText(
            string text,
            IReadOnlyList<CircuitValidationIssue> validationIssues = null)
        {
            var data = new InspectionReportData();
            if (string.IsNullOrWhiteSpace(text))
            {
                return data;
            }

            foreach (var message in SplitLegacySections(text))
            {
                var title = ResolveTitle(message);
                var body = StripLeadingSection(message);
                var kind = ResolveKind(title);
                data.Add(new InspectionReportBlock(
                    title,
                    body,
                    kind,
                    ResolveSeverity(message),
                    kind == InspectionReportBlockKind.Validation ? CollectRuleIds(validationIssues) : null));
            }

            return data;
        }

        private static void AddSection(InspectionReportData data, string title, string body, InspectionReportBlockKind kind)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return;
            }

            data.Add(new InspectionReportBlock(title, body, kind, InspectionReportSeverity.Information));
        }

        public static string StripLeadingSection(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var firstLineEnd = normalized.IndexOf('\n');
            var firstLine = firstLineEnd >= 0 ? normalized.Substring(0, firstLineEnd).Trim() : normalized.Trim();
            return IsSectionTitle(firstLine)
                ? (firstLineEnd >= 0 ? normalized.Substring(firstLineEnd + 1).Trim() : string.Empty)
                : normalized.Trim();
        }

        private static List<string> SplitLegacySections(string text)
        {
            var sections = new List<string>();
            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            var current = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsSectionTitle(line.Trim()) && current.Length > 0)
                {
                    sections.Add(current.ToString().Trim());
                    current.Length = 0;
                }
                current.AppendLine(line);
            }

            if (current.Length > 0)
            {
                sections.Add(current.ToString().Trim());
            }

            if (sections.Count == 0)
            {
                sections.Add(text.Trim());
            }

            return sections;
        }

        private static string ResolveTitle(string message)
        {
            var normalized = message ?? string.Empty;
            var firstLineEnd = normalized.IndexOf('\n');
            var firstLine = firstLineEnd >= 0 ? normalized.Substring(0, firstLineEnd).Trim() : normalized.Trim();
            return IsSectionTitle(firstLine) ? firstLine.Trim('【', '】', ' ') : "检查报告";
        }

        private static bool IsSectionTitle(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length >= 2 && value[0] == '【' && value[value.Length - 1] == '】';
        }

        private static InspectionReportBlockKind ResolveKind(string title)
        {
            if (title.IndexOf("参数", StringComparison.Ordinal) >= 0 || title.IndexOf("估算", StringComparison.Ordinal) >= 0) return InspectionReportBlockKind.Parameter;
            if (title.IndexOf("教学", StringComparison.Ordinal) >= 0 || title.IndexOf("说明", StringComparison.Ordinal) >= 0) return InspectionReportBlockKind.Teaching;
            if (title.IndexOf("接线校验", StringComparison.Ordinal) >= 0) return InspectionReportBlockKind.Validation;
            if (title.IndexOf("状态", StringComparison.Ordinal) >= 0) return InspectionReportBlockKind.Runtime;
            return InspectionReportBlockKind.General;
        }

        private static InspectionReportSeverity ResolveSeverity(string message)
        {
            if (ContainsAny(message, "错误", "失败", "短路", "未形成有效")) return InspectionReportSeverity.Error;
            if (ContainsAny(message, "警告", "提醒", "建议")) return InspectionReportSeverity.Warning;
            if (ContainsAny(message, "通过", "正常", "完成")) return InspectionReportSeverity.Success;
            return InspectionReportSeverity.Information;
        }

        private static IReadOnlyList<string> CollectRuleIds(IReadOnlyList<CircuitValidationIssue> issues)
        {
            var result = new List<string>();
            if (issues == null) return result;
            for (var i = 0; i < issues.Count; i++)
            {
                var ruleId = issues[i] == null ? string.Empty : issues[i].RuleId;
                if (!string.IsNullOrWhiteSpace(ruleId) && !result.Contains(ruleId)) result.Add(ruleId);
            }
            return result;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            for (var i = 0; i < values.Length; i++)
            {
                if (text.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
