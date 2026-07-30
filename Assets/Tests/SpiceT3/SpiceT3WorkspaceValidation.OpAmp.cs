using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// T3 工作区自动验证。partial 文件按功能分组，RunPureChecks 保持唯一且明确的回归执行顺序。
    /// </summary>
    public static partial class SpiceT3WorkspaceValidation
    {
        private static void ValidateInstanceNamingReset()
        {
            var model = new SpiceWorkspaceModel();
            var resistorOne = model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var resistorTwo = model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            if (resistorOne.InstanceId != "resistor-001" || resistorTwo.InstanceId != "resistor-002")
                throw new InvalidOperationException("SPICE instance naming did not start at one per component kind.");

            if (!model.RemoveComponent(resistorOne.InstanceId))
                throw new InvalidOperationException("SPICE instance deletion setup failed.");
            var resistorThree = model.AddComponent(SpiceComponentKind.Resistor, Vector2.up);
            if (resistorThree.InstanceId != "resistor-003")
                throw new InvalidOperationException("Single component deletion unexpectedly reused an instance number.");

            model.Clear();
            model.ResetInstanceNaming();
            var resetResistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var resetDiode = model.AddComponent(SpiceComponentKind.SiliconDiode, Vector2.right);
            var resetSource = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.left);
            var resetVoltageProbe = model.AddComponent(SpiceComponentKind.VoltageProbe, Vector2.up);
            var resetCurrentProbe = model.AddComponent(SpiceComponentKind.CurrentProbe, Vector2.down);
            var resetSwitch = model.AddComponent(SpiceComponentKind.IdealSwitch, Vector2.one);
            if (resetResistor.InstanceId != "resistor-001" || resetDiode.InstanceId != "diode-001" ||
                resetSource.InstanceId != "source-001" || resetVoltageProbe.InstanceId != "voltage-probe-001" ||
                resetCurrentProbe.InstanceId != "current-probe-001" || resetSwitch.InstanceId != "switch-001")
                throw new InvalidOperationException("Clearing the SPICE workspace did not reset per-kind instance naming.");
        }

        private static void ValidateIdealOperationalAmplifierWorkspace()
        {
            var canvasRoot = new GameObject("SpiceOpAmpWorkspaceValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                var initialRevision = workspace.ElectricalRevisionForTesting;
                var opAmp = workspace.CreateComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.zero);
                if (opAmp == null || opAmp.InstanceId != "opamp-001" || workspace.ElectricalRevisionForTesting != initialRevision + 1)
                    throw new InvalidOperationException("Ideal operational amplifier workspace creation did not use the normal model/revision path.");
                var view = workspace.GetComponentViewForTesting(opAmp.InstanceId);
                if (view == null || view.transform.Find("SymbolRoot/Terminal_nonInverting") == null ||
                    view.transform.Find("SymbolRoot/Terminal_inverting") == null || view.transform.Find("SymbolRoot/Terminal_output") == null ||
                    view.transform.Find("SymbolRoot/Symbol/OpLabel") == null)
                    throw new InvalidOperationException("Ideal operational amplifier canvas symbol or terminals are incomplete.");
                workspace.SelectComponent(view);
                if (workspace.GetOpAmpInfoForTesting() == null || !workspace.GetOpAmpInfoForTesting().gameObject.activeSelf ||
                    workspace.GetParameterInputForTesting().gameObject.activeSelf || workspace.GetUnitButtonForTesting().gameObject.activeSelf ||
                    workspace.GetParameterApplyButtonForTesting().gameObject.activeSelf || workspace.GetAcPhaseInputForTesting().gameObject.activeSelf)
                    throw new InvalidOperationException("Ideal operational amplifier parameter panel must be read-only without ordinary apply controls.");
                var card = workspace.GetPaletteCardForTesting(SpiceComponentKind.IdealOperationalAmplifier);
                if (card == null || !card.interactable || !workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) ||
                    !workspace.GetPaletteCardForTesting(SpiceComponentKind.IdealOperationalAmplifier).interactable || workspace.Model.FindComponent(opAmp.InstanceId) == null)
                    throw new InvalidOperationException("Ideal operational amplifier must remain available and retained in both analysis modes.");

                workspace.TrySetAnalysisMode(SpiceAnalysisMode.DcOperatingPoint);
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 100f);
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 100f);
                if (!workspace.Connect(source.InstanceId, SpiceComponentModel.PositiveTerminalId, opAmp.InstanceId, SpiceComponentModel.NonInvertingTerminalId) ||
                    !workspace.Connect(source.InstanceId, SpiceComponentModel.NegativeTerminalId, ground.InstanceId, SpiceComponentModel.GroundTerminalId))
                    throw new InvalidOperationException("Unable to build the formal workspace follower inputs.");
                workspace.HandleTerminalClick(view, SpiceComponentModel.InvertingTerminalId);
                workspace.HandleTerminalHover(view, SpiceComponentModel.OutputTerminalId, true);
                if (view.transform.Find("SymbolRoot/Terminal_output").GetComponent<Image>().color != MainUiTheme.SuccessGreen)
                    throw new InvalidOperationException("IN- to OUT hover target was not marked valid.");
                workspace.HandleTerminalHover(view, SpiceComponentModel.NonInvertingTerminalId, true);
                if (view.transform.Find("SymbolRoot/Terminal_nonInverting").GetComponent<Image>().color != MainUiTheme.DangerRed)
                    throw new InvalidOperationException("IN+ to IN- hover target was not marked invalid.");
                workspace.CancelPendingWire();
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                var beforeFeedbackRevision = workspace.ElectricalRevisionForTesting;
                if (!workspace.Connect(opAmp.InstanceId, SpiceComponentModel.InvertingTerminalId, opAmp.InstanceId, SpiceComponentModel.OutputTerminalId) ||
                    workspace.Model.Wires.Count != 3 || bindings.WireLayer.childCount < 3 || workspace.ElectricalRevisionForTesting != beforeFeedbackRevision + 1 ||
                    workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("Formal IN- to OUT feedback wire did not create exactly one stale workspace change.");
                if (workspace.Connect(opAmp.InstanceId, SpiceComponentModel.NonInvertingTerminalId, opAmp.InstanceId, SpiceComponentModel.OutputTerminalId) ||
                    workspace.Connect(opAmp.InstanceId, SpiceComponentModel.NonInvertingTerminalId, opAmp.InstanceId, SpiceComponentModel.InvertingTerminalId) ||
                    workspace.Connect(source.InstanceId, SpiceComponentModel.PositiveTerminalId, source.InstanceId, SpiceComponentModel.NegativeTerminalId))
                    throw new InvalidOperationException("Invalid same-component wiring was accepted by the formal workspace path.");

                var beforePosition = view.GetTerminalPosition(SpiceComponentModel.OutputTerminalId);
                var beforeDirection = view.GetTerminalDirection(SpiceComponentModel.OutputTerminalId);
                workspace.SelectComponent(view);
                workspace.RotateSelectedComponent();
                if (view.GetTerminalPosition(SpiceComponentModel.OutputTerminalId) == beforePosition ||
                    Vector2.Dot(view.GetTerminalDirection(SpiceComponentModel.OutputTerminalId), beforeDirection) > 0.01f || workspace.Model.Wires.Count != 3)
                    throw new InvalidOperationException("Op-amp rotation did not keep terminal geometry and feedback wire synchronized.");

                var result = new SpiceSimulationService().SimulateAsync(workspace.Model.BuildCircuitModel()).GetAwaiter().GetResult();
                var expectedFollowerVoltage = 10d * SpiceComponentDefaults.IdealOperationalAmplifierOpenLoopGain /
                    (SpiceComponentDefaults.IdealOperationalAmplifierOpenLoopGain + 1d);
                if (!result.Success || Math.Abs(result.ComponentResults[opAmp.InstanceId].Voltage - expectedFollowerVoltage) > 2.1e-5d)
                    throw new InvalidOperationException(
                        "Formal workspace voltage follower did not complete the real SpiceSimulationService path: " +
                        string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)) +
                        "; voltage=" + (result.ComponentResults.TryGetValue(opAmp.InstanceId, out var opAmpResult) ? opAmpResult.Voltage.ToString("G17", CultureInfo.InvariantCulture) : "missing") +
                        "; netlist=" + result.GeneratedNetlistContent +
                        "; stdout=" + (result.RawNgspiceResult?.StandardOutput ?? "<none>") +
                        "; stderr=" + (result.RawNgspiceResult?.StandardError ?? "<none>"));

                var reverse = workspace.CreateComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.right * 180f);
                if (!workspace.Connect(reverse.InstanceId, SpiceComponentModel.OutputTerminalId, reverse.InstanceId, SpiceComponentModel.InvertingTerminalId))
                    throw new InvalidOperationException("Reverse OUT to IN- feedback wire was rejected.");
                workspace.SelectComponent(workspace.GetComponentViewForTesting(reverse.InstanceId));
                workspace.DeleteSelection();
                if (workspace.Model.FindComponent(reverse.InstanceId) != null || workspace.Model.Wires.Any(wire => wire.StartComponentId == reverse.InstanceId || wire.EndComponentId == reverse.InstanceId))
                    throw new InvalidOperationException("Deleting an op-amp left a damaged feedback wire.");
            }
            finally { UnityEngine.Object.DestroyImmediate(canvasRoot); }
        }

        private static void ValidateIdealOperationalAmplifierV1FileBoundary()
        {
            var root = new GameObject("SpiceOpAmpV1FileBoundary", typeof(RectTransform), typeof(Canvas));
            var tempDirectory = Path.Combine(Path.GetTempPath(), "SpiceOpAmpV1FileBoundary_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDirectory);
                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out _);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var normalPath = Path.Combine(tempDirectory, "normal.spicejson");
                if (!workspace.TrySaveWorkspaceToPath(normalPath, out var normalError) || !File.Exists(normalPath))
                    throw new InvalidOperationException("Ordinary DC V2 drawing could not save: " + normalError);
                var originalPath = workspace.CurrentSpiceFilePath;
                workspace.CreateComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.right);
                var missingTarget = Path.Combine(tempDirectory, "opamp-v2.spicejson");
                if (!workspace.TrySaveWorkspaceToPath(missingTarget, out var missingError) || !File.Exists(missingTarget) ||
                    workspace.CurrentSpiceFilePath != missingTarget || missingError != null)
                    throw new InvalidOperationException("Op-amp V2 save did not create the requested target or update the current path.");
                var sentinelPath = Path.Combine(tempDirectory, "sentinel.spicejson");
                const string sentinel = "OPAMP-V2-SENTINEL";
                File.WriteAllText(sentinelPath, sentinel);
                if (!workspace.TrySaveWorkspaceToPath(sentinelPath, out var sentinelError) || File.ReadAllText(sentinelPath) == sentinel ||
                    workspace.CurrentSpiceFilePath != sentinelPath || sentinelError != null ||
                    Directory.GetFiles(tempDirectory, "*.tmp").Length != 0 || Directory.GetFiles(tempDirectory, "*.backup").Length != 0)
                    throw new InvalidOperationException("Op-amp V2 save did not replace the target atomically.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true);
            }
        }
    }
}
