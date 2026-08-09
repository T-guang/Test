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
            var result = new SpiceSimulationResult
            {
                AnalysisSettings = circuit == null
                    ? new SpiceAnalysisSettings(SpiceAnalysisMode.DcOperatingPoint, SpiceAnalysisLimits.DefaultFrequencyHz)
                    : circuit.AnalysisSettings.Copy()
            };
            if (circuit == null || circuit.AnalysisSettings.Mode != SpiceAnalysisMode.DcOperatingPoint)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_DC_ANALYSIS_UNSUPPORTED", SpiceDiagnosticSeverity.Error,
                    "直流工作点仿真需要对应的直流分析设置。"));
                return result;
            }
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
                result.Diagnostics.Add(new SpiceDiagnostic(
                    "SPICE_NGSPICE_" + raw.FailureCode,
                    SpiceDiagnosticSeverity.Error,
                    "ngspice 执行失败。" + (string.IsNullOrWhiteSpace(raw.FailureMessage) ? string.Empty : " 原始信息：" + raw.FailureMessage)));
                return result;
            }

            if (!SpiceDcOutputParser.TryParse(raw.StandardOutput, out var voltages, out var currents, out var parseFailure))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_OUTPUT_PARSE", SpiceDiagnosticSeverity.Error, parseFailure));
                return result;
            }

            if (!ContainsAll(voltages, document.PrintedNodes) || !ContainsAll(currents, document.PrintedBranchNames))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_OUTPUT_INCOMPLETE", SpiceDiagnosticSeverity.Error, "ngspice 未返回生成网表请求的全部结果向量。"));
                return result;
            }

            foreach (var voltage in voltages) result.NodeVoltages[voltage.Key] = voltage.Value;
            BuildComponentResults(circuit, graph, result, currents);
            result.Success = true;
            return result;
        }

        /// <summary>
        /// 将 ngspice 解析出的向量映射回各元件的 <see cref="SpiceComponentResult"/>。
        /// BJT 方向约定（实测 ngspice-45.2 确认）：
        /// Voltage = V(C) - V(E)，恒为此方向；
        /// Current = IC = ngspice @q[ic]，符号约定为“流入 collector 端子为正”；
        /// NPN 放大区 IC 为正（电流流入 collector）；PNP 放大区 IC 为负（电流实际流出 collector）；
        /// ngspice 约定下 ic+ib+ie=0（流入为正），本闭环只报告 IC，不打印/不报告 ib、ie。
        /// </summary>
        private static void BuildComponentResults(SpiceCircuitModel circuit, SpiceCircuitGraph graph, SpiceSimulationResult result, Dictionary<string, double> currents)
        {
            foreach (var component in circuit.Components.Where(component => component.Kind != SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.IdealOperationalAmplifier)
                {
                    // V1 的输出定义固定为 OUT 相对 GND；输入端为高阻控制端，不伪造两个输入支路电流。
                    var outputNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.OutputTerminalId)];
                    var opAmpResult = new SpiceComponentResult
                    {
                        ComponentId = component.InstanceId,
                        ComponentKind = component.Kind.ToString(),
                        Voltage = GetNodeVoltage(result.NodeVoltages, outputNode),
                        Current = currents[graph.SpiceNameByComponentId[component.InstanceId]],
                        VoltageDirection = "OUT-to-GND",
                        CurrentDirection = "OUT-to-GND (ngspice branch convention)",
                        ResultStatus = SpiceResultStatus.Available,
                        Notes = "线性 VCVS；固定开环增益 1e6"
                    };
                    result.ComponentResults[component.InstanceId] = opAmpResult;
                    continue;
                }
                if (component.Kind == SpiceComponentKind.GenericNpnBjt || component.Kind == SpiceComponentKind.GenericPnpBjt)
                {
                    var collectorNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.CollectorTerminalId)];
                    var emitterNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.EmitterTerminalId)];
                    var bjtResult = new SpiceComponentResult
                    {
                        ComponentId = component.InstanceId,
                        ComponentKind = component.Kind.ToString(),
                        Voltage = GetNodeVoltage(result.NodeVoltages, collectorNode) - GetNodeVoltage(result.NodeVoltages, emitterNode),
                        Current = currents[graph.SpiceNameByComponentId[component.InstanceId]],
                        VoltageDirection = "C-to-E",
                        CurrentDirection = "collector terminal (ngspice convention: into-terminal positive)",
                        ResultStatus = SpiceResultStatus.Available,
                        Notes = "电压 = V(C) - V(E)；电流 = IC。NPN 有源区 IC > 0，PNP 有源区 IC < 0（流入集电极为正）。"
                    };
                    result.ComponentResults[component.InstanceId] = bjtResult;
                    continue;
                }
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
                    componentResult.Notes = "直流稳态下等效开路";
                }
                else if (component.Kind == SpiceComponentKind.VoltageProbe)
                {
                    // 电压探针不注入电流，Vprobe = V(V+) - V(V-) 已由通用 voltage 计算得到。
                    componentResult.Current = 0d;
                    componentResult.VoltageDirection = "V-plus-to-V-minus";
                    componentResult.CurrentDirection = "V-plus-to-V-minus";
                    componentResult.Notes = "差分电压测量";
                }
                else if (component.Kind == SpiceComponentKind.CurrentProbe)
                {
                    componentResult.Current = currents[graph.SpiceNameByComponentId[component.InstanceId]];
                    componentResult.VoltageDirection = "IN-to-OUT";
                    componentResult.CurrentDirection = "IN-to-OUT";
                    componentResult.Notes = "串联电流测量";
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
