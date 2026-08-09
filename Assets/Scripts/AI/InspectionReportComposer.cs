using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using UnityEngine;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 将既有检查文本段落和校验问题转换为结构化报告 Block。
    /// 不执行分析、不校验接线，也不渲染 UGUI。Block 的 Kind、Severity 和 RuleIds 是受回归保护的模型数据；
    /// 旧文本解析只用于兼容，不能再作为新 UI 的数据来源。
    /// </summary>
    public static class InspectionReportComposer
    {
        // Composer 的输入是已经计算完成的状态、校验问题和兼容文本；输出是供 Inspector 渲染的稳定 block 模型。
        // 它可以决定区块、严重度和去重的呈现规则，但不能重新运行 Analyzer、升级规则严重度或修改 Workspace。
        public static InspectionReportData CreateSummary(
            string title,
            string reportType,
            bool simulationRunning,
            string circuitDisplayName,
            string conclusion,
            string riskLevel)
        {
            // 摘要是所有检查报告的固定入口块。报告类型、风险级别和运行标记来自调用方，保持它们的语义可让
            // UI、快照测试和后续导出使用同一份结构化数据，而不是各自解析显示文本。
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
                ResolveSummarySeverity(reportType, riskLevel)));
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
            // 解释报告由确定的教学章节和历史文本兼容章节组成。历史运行摘要可能已带标题，因此保留原 block
            // 边界而非嵌套拼接，避免读者和 Inspector 都无法区分“当前运行态”与其子说明。
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
            // 检查摘要与问题正文在模型层合流：summary 保留总览，legacy 文本经同一解析路径取得 block 和
            // severity。不要在 Panel 层再次拼接，否则顶部计数与“问题与风险”会失去一致的去重语义。
            var data = new InspectionReportData();
            data.AddRange(summary);
            data.AddRange(ParseLegacyText(noticesAndFormattedReport, validationIssues));
            return data;
        }

        public static InspectionReportData ParseLegacyText(
            string text,
            IReadOnlyList<CircuitValidationIssue> validationIssues = null)
        {
            // 旧文本仅是向结构化报告迁移的兼容输入。分段时不能据字符串内容推导新的电气事实；Validation
            // block 的严重度优先消费结构化 issues，文本关键字只保留给没有 issue 的历史路径。
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
                    kind == InspectionReportBlockKind.Validation
                        ? ResolveValidationSeverity(message, validationIssues)
                        : ResolveSeverity(message),
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
            // 使用标题边界而不是固定换行数分段，兼容不同平台换行符和正文中的空行；标题格式仍是报告协议的一部分。
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
            if (title.IndexOf("状态", StringComparison.Ordinal) >= 0 || title.IndexOf("运行态", StringComparison.Ordinal) >= 0) return InspectionReportBlockKind.Runtime;
            return InspectionReportBlockKind.General;
        }

        private static InspectionReportSeverity ResolveSummarySeverity(string reportType, string riskLevel)
        {
            if (!string.Equals(reportType, "接线检查", StringComparison.Ordinal)) return InspectionReportSeverity.Information;
            if (string.Equals(riskLevel, "错误", StringComparison.Ordinal)) return InspectionReportSeverity.Error;
            if (string.Equals(riskLevel, "提醒", StringComparison.Ordinal)) return InspectionReportSeverity.Warning;
            if (string.Equals(riskLevel, "正常", StringComparison.Ordinal)) return InspectionReportSeverity.Success;
            return InspectionReportSeverity.Information;
        }

        private static InspectionReportSeverity ResolveValidationSeverity(
            string message,
            IReadOnlyList<CircuitValidationIssue> validationIssues)
        {
            if (validationIssues == null) return ResolveSeverity(message);
            if (validationIssues.Any(issue => issue != null && issue.Severity == CircuitValidationSeverity.Error)) return InspectionReportSeverity.Error;
            if (validationIssues.Any(issue => issue != null && issue.Severity == CircuitValidationSeverity.Warning)) return InspectionReportSeverity.Warning;
            if (validationIssues.Any(issue => issue != null && issue.Severity == CircuitValidationSeverity.Info)) return InspectionReportSeverity.Information;
            return InspectionReportSeverity.Success;
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
            // 一个 Validation block 可承载多个 issue；RuleId 集合按稳定顺序去重，供 UI 定位和快照对比使用，
            // 但不把内部 RuleId 强制暴露给普通学习者。
            return issues == null
                ? Array.Empty<string>()
                : issues.Where(issue => issue != null && !string.IsNullOrWhiteSpace(issue.RuleId))
                    .Select(issue => issue.RuleId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(ruleId => ruleId, StringComparer.Ordinal)
                    .ToList();
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
