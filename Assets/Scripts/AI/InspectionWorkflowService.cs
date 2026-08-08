using System;
using System.Collections.Generic;
using ElectricalSim.Core;
using ElectricalSim.Rules;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 在不依赖 UGUI 的条件下编排 Check 和 Explain 报告生成流程。
    /// 它保持既有 Analyzer、规则、格式化器和 Composer 的调用顺序，并通过窄适配器取得
    /// 尚未抽离的运行态显示信息。只返回数据，面板负责 UI 与状态文字。
    /// 改动流程顺序后必须通过 Inspector 模型测试和 18 张模板基线。
    /// </summary>
    public sealed class InspectionWorkflowService
    {
        private readonly WorkspaceController workspace;
        private readonly IInspectionWorkflowRuntimeAdapter runtimeAdapter;
        private readonly bool showDeveloperDebugInfo;
        private readonly CircuitSummaryBuilder summaryBuilder;
        private readonly CircuitTopologyRecognitionService recognitionService;

        public InspectionWorkflowService(
            WorkspaceController workspace,
            IInspectionWorkflowRuntimeAdapter runtimeAdapter,
            bool showDeveloperDebugInfo)
        {
            this.workspace = workspace;
            this.runtimeAdapter = runtimeAdapter;
            this.showDeveloperDebugInfo = showDeveloperDebugInfo;
            summaryBuilder = workspace == null ? null : new CircuitSummaryBuilder(workspace);
            recognitionService = new CircuitTopologyRecognitionService();
        }

        public InspectionWorkflowResult CreateCheckReport()
        {
            // 工业与普通电路路径必须保留分支：两者的分析器、摘要和状态文案均属于既定学习者体验。
            if (workspace == null || runtimeAdapter == null)
            {
                return InspectionWorkflowResult.Failure("电路检查失败：未能读取当前画布。");
            }

            var recognition = recognitionService.Recognize(workspace);

            if (IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var industrialResult) && industrialResult.IsIndustrial)
            {
                // B5: 工业电路也需执行接触器旁路安全规则；复用 CircuitRuleChecker 共享窄 detector，不运行完整 Check()。
                // bypass issues 必须在 summary/debug/formatter 构建之前合入 industrialResult，保证 ErrorCount/WarningCount 一致。
                MergeContactorBypassIssues(industrialResult, CircuitRuleChecker.DetectContactorBypassIssues(workspace));

                var currentCircuitName = runtimeAdapter.ResolveCurrentCircuitName();
                if (!string.IsNullOrWhiteSpace(currentCircuitName))
                {
                    industrialResult.CircuitType = currentCircuitName;
                }

                var industrialStateResult = runtimeAdapter.AnalyzeCircuitState();
                runtimeAdapter.ApplyRuntimeDisplayOverrides(industrialStateResult);
                var industrialDebugDetails = runtimeAdapter.BuildRuntimeDisplaySummary(industrialStateResult) +
                    "\n\n" + industrialResult.FormatForAssistant() +
                    "\n\n" + industrialStateResult.ToReadableText();
                var industrialSummaryReport = InspectionReportComposer.CreateSummary(
                    "最新检查报告",
                    "接线检查",
                    workspace.IsSimulationRunning,
                    ResolveRecognitionDisplayName(recognition),
                    InspectionReportComposer.BuildCheckSummaryConclusion(industrialStateResult, industrialResult.ErrorCount, industrialResult.WarningCount),
                    InspectionReportComposer.ResolveRiskLevel(industrialStateResult, industrialResult.ErrorCount, industrialResult.WarningCount));
                var industrialNotices = runtimeAdapter.PrependCheckPanelRuntimeNotices(
                    TeachingCheckReportFormatter.Format(industrialStateResult, industrialResult, industrialDebugDetails, showDeveloperDebugInfo),
                    industrialStateResult,
                    out var industrialValidationIssues);
                var industrialStatus = "工业电路检查完成：";
                if (industrialResult.ErrorCount > 0)
                {
                    industrialStatus += "发现 " + industrialResult.ErrorCount + " 个严重问题。";
                }
                else if (industrialResult.WarningCount > 0)
                {
                    industrialStatus += "发现 " + industrialResult.WarningCount + " 个提醒。";
                }
                else
                {
                    industrialStatus += "未发现严重错误。";
                }

                var industrialReport = InspectionReportComposer.ComposeCheckReport(industrialSummaryReport, industrialNotices, industrialValidationIssues);
                industrialReport.AddRange(BuildRecognitionReport(recognition));
                return InspectionWorkflowResult.Success(
                    industrialReport,
                    industrialStatus);
            }

            var checker = new CircuitRuleChecker(workspace);
            var result = checker.Check();
            var stateResult = runtimeAdapter.AnalyzeCircuitState();
            runtimeAdapter.ApplyRuntimeDisplayOverrides(stateResult);
            var displayResult = runtimeAdapter.FilterCheckPanelFalsePositives(result, stateResult);
            var debugDetails = runtimeAdapter.BuildRuntimeDisplaySummary(stateResult) +
                "\n\n" + CircuitRuleCheckTeacherFormatter.FormatForTeaching(displayResult) +
                "\n\n" + stateResult.ToReadableText();
            var summaryReport = InspectionReportComposer.CreateSummary(
                "最新检查报告",
                "接线检查",
                workspace.IsSimulationRunning,
                ResolveRecognitionDisplayName(recognition),
                InspectionReportComposer.BuildCheckSummaryConclusion(stateResult, displayResult.ErrorCount, displayResult.WarningCount),
                InspectionReportComposer.ResolveRiskLevel(stateResult, displayResult.ErrorCount, displayResult.WarningCount));
            var notices = runtimeAdapter.PrependCheckPanelRuntimeNotices(
                TeachingCheckReportFormatter.Format(stateResult, displayResult, debugDetails, showDeveloperDebugInfo),
                stateResult,
                out var validationIssues);
            var status = "电路检查完成：";
            if (displayResult.ErrorCount > 0 || displayResult.WarningCount > 0)
            {
                status += "发现 " + displayResult.ErrorCount + " 个严重问题，" + displayResult.WarningCount + " 个提醒。";
            }
            else
            {
                status += "未发现明显接线错误。";
            }

            var report = InspectionReportComposer.ComposeCheckReport(summaryReport, notices, validationIssues);
            report.AddRange(BuildRecognitionReport(recognition));
            return InspectionWorkflowResult.Success(
                report,
                status);
        }

        public InspectionWorkflowResult CreateExplanationReport()
        {
            if (workspace == null || runtimeAdapter == null)
            {
                return InspectionWorkflowResult.Failure("电路解释失败：未能读取当前画布。");
            }

            var stateResult = runtimeAdapter.AnalyzeCircuitState();
            runtimeAdapter.ApplyRuntimeDisplayOverrides(stateResult);
            var report = InspectionReportComposer.CreateSummary(
                "当前电路解释",
                "电路解释",
                workspace.IsSimulationRunning,
                ResolveRecognitionDisplayName(recognitionService.Recognize(workspace)),
                "以下内容基于当前元件状态和接线拓扑生成。",
                string.Empty);
            report.AddRange(runtimeAdapter.BuildCurrentCircuitExplanationReportData(stateResult));

            if (IndustrialCircuitExplainer.TryExplain(workspace, out var industrialExplanation))
            {
                report.AddRange(InspectionReportComposer.CreateTeaching(runtimeAdapter.ApplyCurrentCircuitName(industrialExplanation)));
                return InspectionWorkflowResult.Success(report);
            }

            var summary = summaryBuilder != null ? summaryBuilder.BuildDetailedSummary() : string.Empty;
            if (string.IsNullOrWhiteSpace(summary))
            {
                report.AddRange(InspectionReportComposer.CreateTeaching("当前画布为空，请先搭建电路或加载标准图纸。"));
                return InspectionWorkflowResult.Success(report);
            }

            report.AddRange(InspectionReportData.FromLegacyText("【教学说明】\n" + summary));
            return InspectionWorkflowResult.Success(report);
        }

        private string ResolveCurrentCircuitDisplayName()
        {
            var name = runtimeAdapter.ResolveCurrentCircuitName();
            return string.IsNullOrWhiteSpace(name) ? "未识别模板" : name;
        }

        private string ResolveRecognitionDisplayName(CircuitRecognitionResult recognition)
        {
            if (recognition != null && (recognition.Status == CircuitRecognitionStatus.ExactMatch || recognition.Status == CircuitRecognitionStatus.EquivalentMatch))
            {
                return recognition.MatchedTemplateName;
            }

            return ResolveCurrentCircuitDisplayName();
        }

        private static InspectionReportData BuildRecognitionReport(CircuitRecognitionResult recognition)
        {
            if (recognition == null) return new InspectionReportData();
            var source = recognition.Source == CircuitRecognitionSource.LoadedTemplate ? "系统模板" :
                recognition.Source == CircuitRecognitionSource.TopologyMatch ? "自由搭建（拓扑匹配）" : "自由搭建";
            if (recognition.Status == CircuitRecognitionStatus.ExactMatch || recognition.Status == CircuitRecognitionStatus.EquivalentMatch)
            {
                var matchMethod = recognition.Status == CircuitRecognitionStatus.ExactMatch
                    ? "精确静态拓扑"
                    : "电气节点等价";
                return InspectionReportComposer.CreateTeaching(
                    "识别电路：" + recognition.MatchedTemplateName + "\n搭建来源：" + source + "\n匹配方式：" + matchMethod + "\n" + recognition.Reason);
            }
            var text = "搭建来源：" + source + "\n模板匹配：" + recognition.Reason;
            return InspectionReportComposer.CreateTeaching(text);
        }

        /// <summary>
        /// 将 B5 接触器旁路 issues 按 severity 合入 industrialResult.Errors/Warnings。
        /// 合入时执行 Trim 级唯一去重，保证 ErrorCount/WarningCount 与最终可见内容不因重复产生偏差。
        /// learner-facing 文本至少包含 issue.title + "：" + issue.message，不能只取通用 message。
        /// </summary>
        private static void MergeContactorBypassIssues(
            CircuitAnalysisResult industrialResult,
            IReadOnlyList<CircuitIssue> bypassIssues)
        {
            if (industrialResult == null || bypassIssues == null || bypassIssues.Count == 0)
            {
                return;
            }

            for (var i = 0; i < bypassIssues.Count; i++)
            {
                var issue = bypassIssues[i];
                if (issue == null)
                {
                    continue;
                }

                var title = string.IsNullOrWhiteSpace(issue.title) ? string.Empty : issue.title.Trim();
                var message = string.IsNullOrWhiteSpace(issue.message) ? string.Empty : issue.message.Trim();
                var text = string.IsNullOrEmpty(title)
                    ? message
                    : string.IsNullOrEmpty(message) ? title : title + "：" + message;

                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (issue.severity == CircuitIssueSeverity.Error)
                {
                    AddUniqueTrim(industrialResult.Errors, text);
                }
                else if (issue.severity == CircuitIssueSeverity.Warning)
                {
                    AddUniqueTrim(industrialResult.Warnings, text);
                }
            }
        }

        /// <summary>
        /// Trim 级唯一合并：比较时对已有条目和新条目都做 Trim，避免尾部空格导致重复计数。
        /// </summary>
        private static void AddUniqueTrim(List<string> target, string text)
        {
            if (target == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var normalized = text.Trim();
            for (var i = 0; i < target.Count; i++)
            {
                if (target[i] != null && target[i].Trim() == normalized)
                {
                    return;
                }
            }

            target.Add(normalized);
        }
    }
}
