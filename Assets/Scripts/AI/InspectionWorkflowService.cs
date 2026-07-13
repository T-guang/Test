using System;
using ElectricalSim.Core;
using ElectricalSim.Rules;

namespace ElectricalSim.AI
{
    public sealed class InspectionWorkflowService
    {
        private readonly WorkspaceController workspace;
        private readonly IInspectionWorkflowRuntimeAdapter runtimeAdapter;
        private readonly bool showDeveloperDebugInfo;
        private readonly CircuitSummaryBuilder summaryBuilder;

        public InspectionWorkflowService(
            WorkspaceController workspace,
            IInspectionWorkflowRuntimeAdapter runtimeAdapter,
            bool showDeveloperDebugInfo)
        {
            this.workspace = workspace;
            this.runtimeAdapter = runtimeAdapter;
            this.showDeveloperDebugInfo = showDeveloperDebugInfo;
            summaryBuilder = workspace == null ? null : new CircuitSummaryBuilder(workspace);
        }

        public InspectionWorkflowResult CreateCheckReport()
        {
            if (workspace == null || runtimeAdapter == null)
            {
                return InspectionWorkflowResult.Failure("电路检查失败：未能读取当前画布。");
            }

            if (IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var industrialResult) && industrialResult.IsIndustrial)
            {
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
                    ResolveCurrentCircuitDisplayName(),
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

                return InspectionWorkflowResult.Success(
                    InspectionReportComposer.ComposeCheckReport(industrialSummaryReport, industrialNotices, industrialValidationIssues),
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
                ResolveCurrentCircuitDisplayName(),
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

            return InspectionWorkflowResult.Success(
                InspectionReportComposer.ComposeCheckReport(summaryReport, notices, validationIssues),
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
                ResolveCurrentCircuitDisplayName(),
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
    }
}
