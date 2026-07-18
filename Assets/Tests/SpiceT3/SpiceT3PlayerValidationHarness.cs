using System;
using System.IO;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
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
                await Task.Yield();
                var workspace = GetComponent<SpiceWorkspaceController>();
                if (workspace == null) throw new InvalidOperationException("SpiceWorkspaceController was not created.");
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-160f, 40f));
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(120f, 40f));
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(0f, -140f));
                SpiceT3WorkspaceValidation.ConnectSingleResistor(workspace, source.InstanceId, resistor.InstanceId, ground.InstanceId);
                var first = await workspace.RunCalculationAsync();
                if (first == null || !first.Success || Math.Abs(first.ComponentResults[resistor.InstanceId].Current - 0.01d) > 1e-8d) throw new InvalidOperationException("Single-resistor Player flow failed.");
                report.firstCurrent = first.ComponentResults[resistor.InstanceId].Current;
                if (!workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm") || workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player parameter change did not stale the result.");
                var updated = await workspace.RunCalculationAsync();
                if (updated == null || !updated.Success || Math.Abs(updated.ComponentResults[resistor.InstanceId].Current - 0.005d) > 1e-8d) throw new InvalidOperationException("Updated Player result is incorrect.");
                report.updatedCurrent = updated.ComponentResults[resistor.InstanceId].Current;
                workspace.Model.RemoveWire(workspace.Model.Wires[0]);
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player topology change did not stale the result.");
                var invalid = await workspace.RunCalculationAsync();
                if (invalid == null || invalid.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed) throw new InvalidOperationException("Invalid Player topology was incorrectly marked current.");
                report.success = true;
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogError("[SpiceT3] " + report.failure);
            }

            var path = ResolveResultPath();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("[SpiceT3] Player validation report: " + path);
            Application.Quit(report.success ? 0 : 1);
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
