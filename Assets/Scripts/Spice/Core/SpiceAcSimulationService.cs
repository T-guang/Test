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
    public sealed class SpiceAcSimulationService
    {
        private readonly Func<string, TimeSpan, string, CancellationToken, Task<NgspiceRunResult>> runRawNetlistAsync;

        public SpiceAcSimulationService(Func<string, TimeSpan, string, CancellationToken, Task<NgspiceRunResult>> runRawNetlistAsync = null)
        {
            if (runRawNetlistAsync != null)
            {
                this.runRawNetlistAsync = runRawNetlistAsync;
                return;
            }

            var runner = new NgspiceProcessRunner();
            this.runRawNetlistAsync = runner.RunRawNetlistAsync;
        }

        public async Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = CreateResult(circuit);
            if (circuit == null || circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_ANALYSIS_UNSUPPORTED", SpiceDiagnosticSeverity.Error,
                    "Single-frequency AC analysis requires AC analysis settings."));
                return result;
            }

            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            result.Diagnostics.AddRange(graph.Diagnostics);
            if (!graph.IsValid) return result;

            var document = SpiceAcNetlistBuilder.Build(circuit, graph);
            result.GeneratedNetlistContent = document.Content;
            var raw = await runRawNetlistAsync(document.Content, TimeSpan.FromSeconds(8), "SpiceAcB2", cancellationToken).ConfigureAwait(false);
            result.RawNgspiceResult = raw;
            result.Duration = raw.Duration;
            if (!raw.Success)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_NGSPICE_" + raw.FailureCode, SpiceDiagnosticSeverity.Error,
                    raw.FailureMessage ?? "ngspice execution failed."));
                return result;
            }

            if (SpiceNgspiceErrorClassifier.TryGetSevereErrorLine(raw.StandardError, out _))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_NGSPICE_ERROR", SpiceDiagnosticSeverity.Error,
                    "ngspice reported an error while executing the AC analysis."));
                return result;
            }

            if (!SpiceAcOutputParser.TryParse(raw.StandardOutput, document.OutputRequests, out var values, out var failure, out var message))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_OUTPUT_" + failure.ToString().ToUpperInvariant(), SpiceDiagnosticSeverity.Error, message));
                return result;
            }

            result.AcNodeVoltages["0"] = SpicePhasor.Zero;
            foreach (var request in document.OutputRequests.Where(request => request.Kind == SpiceAcOutputKind.NodeVoltage))
                result.AcNodeVoltages[request.ResultKey] = values[request.ResultKey];
            BuildComponentResults(circuit, graph, result, values);
            result.Success = true;
            return result;
        }

        private static SpiceSimulationResult CreateResult(SpiceCircuitModel circuit)
        {
            var settings = circuit == null
                ? new SpiceAnalysisSettings(SpiceAnalysisMode.DcOperatingPoint, SpiceAnalysisLimits.DefaultFrequencyHz)
                : circuit.AnalysisSettings.Copy();
            return new SpiceSimulationResult { AnalysisSettings = settings };
        }

        private static void BuildComponentResults(SpiceCircuitModel circuit, SpiceCircuitGraph graph, SpiceSimulationResult result,
            IReadOnlyDictionary<string, SpicePhasor> branchValues)
        {
            var omega = 2d * Math.PI * circuit.AnalysisSettings.FrequencyHz;
            foreach (var component in circuit.Components.Where(component => component.Kind != SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.IdealOperationalAmplifier)
                {
                    // ngspice 已通过真实 DC/AC fixture 验证 i(E...) 可读取，因此只报告 VCVS 输出支路，不把高阻输入端误报为普通元件电流。
                    var outputNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.OutputTerminalId)];
                    result.AcComponentResults[component.InstanceId] = new SpiceAcComponentResult
                    {
                        ComponentId = component.InstanceId,
                        ComponentKind = component.Kind.ToString(),
                        Voltage = GetNodeVoltage(result.AcNodeVoltages, outputNode),
                        Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]],
                        VoltageDirection = "OUT-to-GND",
                        CurrentDirection = "OUT-to-GND (ngspice branch convention)",
                        ResultStatus = SpiceResultStatus.Available,
                        Notes = "线性 VCVS；固定开环增益 1e6"
                    };
                    continue;
                }
                if (component.Kind == SpiceComponentKind.GenericNpnBjt || component.Kind == SpiceComponentKind.GenericPnpBjt)
                {
                    // BJT 三端器件：Voltage = V(C)-V(E) 相量；Current 经内部 0V 探针源 i() 支路读取复数小信号 collector 电流。
                    // 方向约定：C-to-E；流入 collector 为正（探针源正极接 collector 网络、负极接 Q collector 脚）。
                    var collectorNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.CollectorTerminalId)];
                    var emitterNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.EmitterTerminalId)];
                    var probeName = SpiceAcNetlistBuilder.GetBjtCollectorProbeName(graph.SpiceNameByComponentId[component.InstanceId]);
                    result.AcComponentResults[component.InstanceId] = new SpiceAcComponentResult
                    {
                        ComponentId = component.InstanceId,
                        ComponentKind = component.Kind.ToString(),
                        Voltage = GetNodeVoltage(result.AcNodeVoltages, collectorNode).Subtract(GetNodeVoltage(result.AcNodeVoltages, emitterNode)),
                        Current = branchValues[probeName],
                        VoltageDirection = "C-to-E",
                        CurrentDirection = "collector small-signal current via internal 0V probe, positive flowing into collector",
                        ResultStatus = SpiceResultStatus.Available,
                        Notes = "小信号 AC collector 电流（经内部 0V 探针源 i() 读取），非 DC 工作点电流；@q[ic] 在 .ac 下只输出 DC 标量，故采用探针 fallback。模型卡无动态参数，响应纯阻性、虚部≈0。"
                    };
                    continue;
                }
                var positiveNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negativeNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                var voltage = GetNodeVoltage(result.AcNodeVoltages, positiveNode).Subtract(GetNodeVoltage(result.AcNodeVoltages, negativeNode));
                var componentResult = new SpiceAcComponentResult
                {
                    ComponentId = component.InstanceId,
                    ComponentKind = component.Kind.ToString(),
                    Voltage = voltage,
                    VoltageDirection = "positive-to-negative",
                    CurrentDirection = "positive-to-negative",
                    ResultStatus = SpiceResultStatus.Available,
                    Notes = string.Empty
                };

                switch (component.Kind)
                {
                    case SpiceComponentKind.Resistor:
                        componentResult.Current = voltage.Divide(component.GetRequiredParameter(SpiceParameterKey.Resistance));
                        break;
                    case SpiceComponentKind.Capacitor:
                        componentResult.Current = voltage.MultiplyByJ(omega * component.GetRequiredParameter(SpiceParameterKey.Capacitance));
                        break;
                    case SpiceComponentKind.Inductor:
                        componentResult.Current = voltage.DivideByJ(omega * component.GetRequiredParameter(SpiceParameterKey.Inductance));
                        break;
                    case SpiceComponentKind.IdealSwitch:
                        componentResult.Current = voltage.Divide(SpiceNetlistBuilder.SwitchResistance(component));
                        componentResult.VoltageDirection = "A-to-B";
                        componentResult.CurrentDirection = "A-to-B";
                        componentResult.Notes = component.GetRequiredParameter(SpiceParameterKey.SwitchClosed) > 0.5d
                            ? "closed static-resistance equivalent"
                            : "open static-resistance equivalent";
                        break;
                    case SpiceComponentKind.AcVoltageSource:
                        componentResult.Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]];
                        componentResult.CurrentDirection = "positive-to-negative (ngspice branch convention)";
                        break;
                    case SpiceComponentKind.DcVoltageSource:
                        // DC 偏置源：AC 小信号激励为 0，仅提供工作点偏置；支路电流为小信号流经偏置源的电流。
                        componentResult.Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]];
                        componentResult.CurrentDirection = "positive-to-negative (ngspice branch convention)";
                        componentResult.Notes = "DC bias source; AC small-signal excitation is 0; current is the small-signal current through the bias source";
                        break;
                    case SpiceComponentKind.VoltageProbe:
                        componentResult.Current = SpicePhasor.Zero;
                        componentResult.VoltageDirection = "V-plus-to-V-minus";
                        componentResult.CurrentDirection = "V-plus-to-V-minus";
                        componentResult.Notes = "differential voltage measurement";
                        break;
                    case SpiceComponentKind.CurrentProbe:
                        componentResult.Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]];
                        componentResult.VoltageDirection = "IN-to-OUT";
                        componentResult.CurrentDirection = "IN-to-OUT";
                        componentResult.Notes = "series current measurement";
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported component reached AC result mapping: " + component.Kind + ".");
                }

                result.AcComponentResults[component.InstanceId] = componentResult;
            }
        }

        private static SpicePhasor GetNodeVoltage(IReadOnlyDictionary<string, SpicePhasor> nodeVoltages, string node)
        {
            return node == "0" ? SpicePhasor.Zero : nodeVoltages[node];
        }
    }
}
