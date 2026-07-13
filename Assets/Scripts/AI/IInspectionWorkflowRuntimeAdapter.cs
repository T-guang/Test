using System.Collections.Generic;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using ElectricalSim.Rules;

namespace ElectricalSim.AI
{
    public interface IInspectionWorkflowRuntimeAdapter
    {
        CircuitStateResult AnalyzeCircuitState();
        void ApplyRuntimeDisplayOverrides(CircuitStateResult stateResult);
        string BuildRuntimeDisplaySummary(CircuitStateResult stateResult);
        InspectionReportData BuildCurrentCircuitExplanationReportData(CircuitStateResult stateResult);
        CircuitCheckResult FilterCheckPanelFalsePositives(CircuitCheckResult ruleResult, CircuitStateResult stateResult);
        string PrependCheckPanelRuntimeNotices(
            string report,
            CircuitStateResult stateResult,
            out IReadOnlyList<CircuitValidationIssue> validationIssues);
        string ResolveCurrentCircuitName();
        string ApplyCurrentCircuitName(string text);
    }
}
