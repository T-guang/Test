using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Workspace;
using UnityEngine;

namespace ElectricalSim.Spice.T3
{
    [Serializable]
    public sealed class SpiceT3PlayerValidationReport
    {
        public bool success;
        public double firstCurrent;
        public double updatedCurrent;
        public bool d1StaleDiscardPassed;
        public bool d2ImportLimitsPassed;
        public bool d3ClearStatePassed;
        public bool d3NetlistRevisionPassed;
        public bool unexpectedErrorSanitizationPassed;
        public string failure;
    }

    /// <summary>
    /// 独立 T3 Player 验证入口：通过真实工作区建立单电阻回路、修改参数并写出结果，
    /// 不属于正式原型场景的 UI 功能或保存格式。
    /// </summary>
    public sealed class SpiceT3PlayerValidationHarness : MonoBehaviour
    {
        private async void Start()
        {
            var report = new SpiceT3PlayerValidationReport();
            try
            {
                // 场景基础设施已序列化，Bootstrap 在全部 Awake 完成前绑定宿主并初始化 Controller；Start 无需等待一帧。
                var bootstrap = GetComponent<SpiceWorkspacePrototypeBootstrap>();
                if (bootstrap == null)
                    throw new InvalidOperationException("SpiceWorkspacePrototypeBootstrap is missing from the validation host.");
                var workspace = bootstrap.Controller;
                if (workspace == null)
                    throw new InvalidOperationException("Spice workspace was not initialized by Bootstrap.Awake.");
                workspace.ValidateAssistantPanelLayout();
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-160f, 40f));
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(120f, 40f));
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(0f, -140f));
                SpiceT3WorkspaceValidation.ConnectSingleResistor(workspace, source.InstanceId, resistor.InstanceId, ground.InstanceId);
                var first = await workspace.RunCalculationAsync();
                if (first == null || !first.Success || Math.Abs(first.ComponentResults[resistor.InstanceId].Current - 0.01d) > 1e-8d) throw new InvalidOperationException("Single-resistor Player flow failed.");
                report.firstCurrent = first.ComponentResults[resistor.InstanceId].Current;
                if (!workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm") || workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player parameter change did not stale the result.");
                if (workspace.TryGetCopyableNetlistText(out _)) throw new InvalidOperationException("Stale Player netlist remained copyable.");
                var updated = await workspace.RunCalculationAsync();
                if (updated == null || !updated.Success || Math.Abs(updated.ComponentResults[resistor.InstanceId].Current - 0.005d) > 1e-8d) throw new InvalidOperationException("Updated Player result is incorrect.");
                report.updatedCurrent = updated.ComponentResults[resistor.InstanceId].Current;
                if (!workspace.TryGetCopyableNetlistText(out _)) throw new InvalidOperationException("Updated Player netlist was not copyable.");
                report.d3NetlistRevisionPassed = true;

                await ValidateD1StaleResultDiscard(workspace, resistor);
                report.d1StaleDiscardPassed = true;
                report.d2ImportLimitsPassed = ValidateD2PathImportLimit(workspace);

                var currentAfterDiscard = await workspace.RunCalculationAsync();
                if (currentAfterDiscard == null || !currentAfterDiscard.Success ||
                    workspace.ResultState != SpiceWorkspaceResultState.Current ||
                    !workspace.TryGetCopyableOutcomeText(out _) ||
                    !workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("Player flow did not recover a current result after D1 discard.");

                workspace.ClearWorkspace();
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun ||
                    workspace.TryGetCopyableOutcomeText(out _) ||
                    workspace.TryGetCopyableNetlistText(out _) ||
                    workspace.Model.Components.Count != 0 || workspace.Model.Wires.Count != 0)
                    throw new InvalidOperationException("Player ClearWorkspace did not clear result and netlist state.");
                report.d3ClearStatePassed = true;

                source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-160f, 40f));
                resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(120f, 40f));
                ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(0f, -140f));
                SpiceT3WorkspaceValidation.ConnectSingleResistor(workspace, source.InstanceId, resistor.InstanceId, ground.InstanceId);
                var rebuilt = await workspace.RunCalculationAsync();
                if (rebuilt == null || !rebuilt.Success) throw new InvalidOperationException("Rebuilt Player circuit failed before invalid-topology regression.");
                workspace.Model.RemoveWire(workspace.Model.Wires[0]);
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player topology change did not stale the result.");
                var invalid = await workspace.RunCalculationAsync();
                if (invalid == null || invalid.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed) throw new InvalidOperationException("Invalid Player topology was incorrectly marked current.");

                report.unexpectedErrorSanitizationPassed = await ValidateUnexpectedErrorSanitization(workspace);
                if (!report.d1StaleDiscardPassed || !report.d2ImportLimitsPassed ||
                    !report.d3ClearStatePassed || !report.d3NetlistRevisionPassed ||
                    !report.unexpectedErrorSanitizationPassed)
                    throw new InvalidOperationException("One or more stabilization Player checks did not pass.");
                report.success = true;
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogError("[SpiceT3] " + report.failure);
            }

            var path = ResolveResultPath();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("[Spice][T3] Player 验证报告：" + path);
            Application.Quit(report.success ? 0 : 1);
        }

        private static bool ValidateD2PathImportLimit(SpiceWorkspaceController workspace)
        {
            var path = Path.Combine(Application.persistentDataPath, "SpiceT3_D2_ImportLimit_" + Guid.NewGuid().ToString("N") + ".spicejson");
            try
            {
                var oversized = new SpiceWorkspaceModel();
                for (var index = 0; index <= SpiceDrawingLimits.MaxComponents; index++)
                    oversized.AddComponent(SpiceComponentKind.Resistor, new Vector2(index % 20, index / 20));
                File.WriteAllText(path, SpiceDrawingSerializer.ToJson(oversized));
                if (new FileInfo(path).Length >= SpiceDrawingLimits.MaxFileBytes)
                    throw new InvalidOperationException("D2 Player fixture unexpectedly exceeded the file-size limit.");

                var modelBefore = workspace.Model;
                var componentCountBefore = workspace.Model.Components.Count;
                var wireCountBefore = workspace.Model.Wires.Count;
                var pathBefore = workspace.CurrentSpiceFilePath;
                if (workspace.TryImportWorkspaceFromPath(path, out var error))
                    throw new InvalidOperationException("D2 Player path import accepted an over-limit drawing.");
                if (string.IsNullOrEmpty(error) || !error.Contains("器件数量"))
                    throw new InvalidOperationException("D2 Player path import returned an unstable error message: " + error);
                if (!ReferenceEquals(modelBefore, workspace.Model) ||
                    workspace.Model.Components.Count != componentCountBefore ||
                    workspace.Model.Wires.Count != wireCountBefore ||
                    workspace.CurrentSpiceFilePath != pathBefore)
                    throw new InvalidOperationException("D2 Player rejected import changed the workspace or current path.");
                return true;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static async Task<bool> ValidateUnexpectedErrorSanitization(SpiceWorkspaceController workspace)
        {
            const string secretPath = @"C:\Users\TestUser\Secret\solver.tmp";
            workspace.SetSimulationOverrideForTesting((_, __) =>
                Task.FromException<SpiceSimulationResult>(
                    new NullReferenceException("Unexpected Player solver failure at " + secretPath)));
            try
            {
                var result = await workspace.RunCalculationAsync();
                if (result != null || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("Unexpected Player error did not enter Failed state.");
                if (!workspace.TryGetCopyableOutcomeText(out var text) ||
                    !text.Contains("SPICE_RUNTIME_UNEXPECTED") ||
                    text.Contains(secretPath) || text.Contains("NullReferenceException"))
                    throw new InvalidOperationException("Unexpected Player error leaked internal details to the result.");
                return true;
            }
            finally
            {
                workspace.SetSimulationOverrideForTesting(null);
            }
        }

        private static async Task ValidateD1StaleResultDiscard(SpiceWorkspaceController workspace, SpiceWorkspaceComponentData resistor)
        {
            var completion = new TaskCompletionSource<SpiceSimulationResult>();
            workspace.SetSimulationOverrideForTesting((_, __) => completion.Task);
            try
            {
                var calculation = workspace.RunCalculationAsync();
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("D1 delayed calculation did not enter Running state.");
                if (workspace.TrySetParameter(resistor.InstanceId, 3d, "kOhm"))
                    throw new InvalidOperationException("D1 rejected parameter mutation was accepted while Running.");
                if (workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.zero) != null)
                    throw new InvalidOperationException("D1 rejected component creation was accepted while Running.");

                // The production guards reject UI mutations. This controlled model mutation proves
                // the second layer still discards an outdated request if a future path bypasses them.
                if (!workspace.Model.TrySetParameter(resistor.InstanceId, 4000d))
                    throw new InvalidOperationException("D1 controlled model mutation failed.");

                completion.SetResult(new SpiceSimulationResult
                {
                    Success = true,
                    GeneratedNetlistContent = "* D1 delayed result"
                });
                var stale = await calculation;
                if (stale != null || workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("D1 outdated calculation result was not discarded.");
            }
            finally
            {
                workspace.SetSimulationOverrideForTesting(null);
            }
        }

        private static string ResolveResultPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                const string prefix = "--spice-t3-result=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return argument.Substring(prefix.Length).Trim('"');
            }
            return Path.Combine(Application.persistentDataPath, "SpiceT3PlayerResult.json");
        }
    }
}
