using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Core
{
    /// <summary>
    /// T2 DC 闭环编排：从当前纯数据模型重建图、生成网表、执行 ngspice 并映射回原 instanceId。
    /// 不保存拓扑或复用上一次节点结果。
    /// </summary>
    public sealed class SpiceDcSimulationService
    {
        private readonly NgspiceProcessRunner processRunner;

        public SpiceDcSimulationService(NgspiceProcessRunner processRunner = null)
        {
            this.processRunner = processRunner ?? new NgspiceProcessRunner();
        }

        public async Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new SpiceSimulationResult();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            result.Diagnostics.AddRange(graph.Diagnostics);
            if (!graph.IsValid) return result;

            var document = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph);
            result.GeneratedNetlistContent = document.Content;
            var raw = await processRunner.RunRawNetlistAsync(document.Content, TimeSpan.FromSeconds(8), "SpiceT2", cancellationToken).ConfigureAwait(false);
            result.RawNgspiceResult = raw;
            result.Duration = raw.Duration;
            if (!raw.Success)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_NGSPICE_" + raw.FailureCode, SpiceDiagnosticSeverity.Error, raw.FailureMessage ?? "ngspice execution failed."));
                return result;
            }

            if (!SpiceDcOutputParser.TryParse(raw.StandardOutput, out var voltages, out var currents, out var parseFailure))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_OUTPUT_PARSE", SpiceDiagnosticSeverity.Error, parseFailure));
                return result;
            }

            if (!ContainsAll(voltages, document.PrintedNodes) || !ContainsAll(currents, document.PrintedBranchNames))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_OUTPUT_INCOMPLETE", SpiceDiagnosticSeverity.Error, "ngspice did not return every vector requested by the generated netlist."));
                return result;
            }

            foreach (var voltage in voltages) result.NodeVoltages[voltage.Key] = voltage.Value;
            BuildComponentResults(circuit, graph, result, currents);
            result.Success = true;
            return result;
        }

        private static void BuildComponentResults(SpiceCircuitModel circuit, SpiceCircuitGraph graph, SpiceSimulationResult result, Dictionary<string, double> currents)
        {
            foreach (var component in circuit.Components.Where(component => component.Kind != SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                var positiveNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negativeNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                var voltage = GetNodeVoltage(result.NodeVoltages, positiveNode) - GetNodeVoltage(result.NodeVoltages, negativeNode);
                var componentResult = new SpiceComponentResult
                {
                    ComponentId = component.InstanceId,
                    ComponentKind = component.Kind.ToString(),
                    Voltage = voltage,
                    VoltageDirection = "positive-to-negative",
                    CurrentDirection = "positive-to-negative",
                    ResultStatus = SpiceResultStatus.Available,
                    Notes = string.Empty
                };

                if (component.Kind == SpiceComponentKind.Resistor)
                {
                    componentResult.Current = voltage / component.GetRequiredParameter(SpiceParameterKey.Resistance);
                }
                else if (component.Kind == SpiceComponentKind.DcCurrentSource)
                {
                    componentResult.Current = component.GetRequiredParameter(SpiceParameterKey.DcCurrent);
                    componentResult.VoltageDirection = "P-to-N";
                    componentResult.CurrentDirection = "P-to-N";
                    componentResult.Notes = "设定电流：P → N";
                }
                else if (component.Kind == SpiceComponentKind.IdealSwitch)
                {
                    componentResult.Current = voltage / SpiceNetlistBuilder.SwitchResistance(component);
                    componentResult.VoltageDirection = "A-to-B";
                    componentResult.CurrentDirection = "A-to-B";
                    componentResult.Notes = component.GetRequiredParameter(SpiceParameterKey.SwitchClosed) > 0.5d ? "状态：闭合" : "状态：断开";
                }
                else if (component.Kind == SpiceComponentKind.SiliconDiode)
                {
                    componentResult.Current = currents[graph.SpiceNameByComponentId[component.InstanceId]];
                    componentResult.VoltageDirection = "A-to-K";
                    componentResult.CurrentDirection = "A-to-K";
                    componentResult.Notes = componentResult.Current > 1e-6d ? "状态：正向导通" : componentResult.Voltage < -0.05d ? "状态：反向偏置/近似截止" : "状态：工作点接近零或不确定";
                }
                else if (component.Kind == SpiceComponentKind.Capacitor)
                {
                    componentResult.Current = 0d;
                    componentResult.ResultStatus = SpiceResultStatus.DcSteadyStateOpenCircuit;
                    componentResult.Notes = "DcSteadyStateOpenCircuit";
                }
                else
                {
                    var spiceName = graph.SpiceNameByComponentId[component.InstanceId];
                    componentResult.Current = currents[spiceName];
                }

                result.ComponentResults[component.InstanceId] = componentResult;
            }
        }

        private static double GetNodeVoltage(Dictionary<string, double> voltages, string node)
        {
            return node == "0" ? 0d : voltages[node];
        }

        private static bool ContainsAll(Dictionary<string, double> actual, IReadOnlyList<string> expected)
        {
            return expected.All(actual.ContainsKey);
        }
    }
}
