using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Netlist
{
    public enum SpiceAcOutputKind { NodeVoltage, BranchCurrent }

    public sealed class SpiceAcOutputRequest
    {
        public SpiceAcOutputRequest(SpiceAcOutputKind kind, string expression, string resultKey)
        {
            Kind = kind;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            ResultKey = resultKey ?? throw new ArgumentNullException(nameof(resultKey));
        }

        public SpiceAcOutputKind Kind { get; }
        public string Expression { get; }
        public string ResultKey { get; }
    }

    public sealed class SpiceAcNetlistDocument
    {
        public string Content { get; set; }
        public IReadOnlyList<SpiceAcOutputRequest> OutputRequests { get; set; }
    }

    /// <summary>
    /// 构建确定性的单点 ngspice AC 网表；DC 网表生成继续保持独立。
    /// </summary>
    public static class SpiceAcNetlistBuilder
    {
        public const string BeginMarker = "__SPICE_AC_B2_BEGIN__";
        public const string EndMarker = "__SPICE_AC_B2_END__";

        public static SpiceAcNetlistDocument Build(SpiceCircuitModel circuit, SpiceCircuitGraph graph)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            if (circuit.AnalysisSettings == null || circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
                throw new InvalidOperationException("Single-frequency AC analysis settings are required.");
            if (graph == null || !graph.IsValid)
                throw new InvalidOperationException("A valid SpiceCircuitGraph is required before generating an AC netlist.");

            var componentById = circuit.Components.ToDictionary(component => component.InstanceId, StringComparer.Ordinal);
            var ordered = graph.SpiceNameByComponentId.OrderBy(pair => pair.Value, StringComparer.Ordinal).ToList();
            var builder = new StringBuilder("* ElectricalSimulation2D SPICE Single-Frequency AC\n");
            foreach (var pair in ordered)
            {
                var component = componentById[pair.Key];
                if (component.Kind == SpiceComponentKind.IdealOperationalAmplifier)
                {
                    AppendIdealOperationalAmplifierLine(builder, component, pair.Value, graph);
                    continue;
                }
                if (component.Kind == SpiceComponentKind.GenericNpnBjt || component.Kind == SpiceComponentKind.GenericPnpBjt)
                {
                    AppendBjtLines(builder, component, pair.Value, graph);
                    continue;
                }
                var positive = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negative = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                AppendComponentLine(builder, component, pair.Value, positive, negative);
            }

            // 与 DC builder 一致的按需 .model 发射：每种通用 BJT 模型只发一次；
            // 模型卡不含动态参数（TF/CJE/CJC 默认 0），AC 响应纯阻性且与频率无关，教学可接受。
            if (ordered.Any(pair => componentById[pair.Key].Kind == SpiceComponentKind.GenericNpnBjt))
            {
                builder.AppendLine(".model " + SpiceComponentDefaults.NpnGenericModelName + " " + SpiceComponentDefaults.NpnGenericModelParameters);
            }
            if (ordered.Any(pair => componentById[pair.Key].Kind == SpiceComponentKind.GenericPnpBjt))
            {
                builder.AppendLine(".model " + SpiceComponentDefaults.PnpGenericModelName + " " + SpiceComponentDefaults.PnpGenericModelParameters);
            }

            var requests = new List<SpiceAcOutputRequest>();
            var nodes = graph.NodeByTerminal.Values
                .Where(node => node != "0")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(node => node, StringComparer.Ordinal)
                .ToList();
            foreach (var node in nodes)
                requests.Add(new SpiceAcOutputRequest(SpiceAcOutputKind.NodeVoltage, "v(" + node + ")", node));

            var branchNames = ordered
                .Where(pair => componentById[pair.Key].Kind == SpiceComponentKind.AcVoltageSource ||
                               componentById[pair.Key].Kind == SpiceComponentKind.CurrentProbe ||
                               componentById[pair.Key].Kind == SpiceComponentKind.IdealOperationalAmplifier ||
                               componentById[pair.Key].Kind == SpiceComponentKind.DcVoltageSource)
                .Select(pair => pair.Value)
                .ToList();
            // BJT collector 复数电流经内部 0V 探针源读取（@q[ic] 在 .ac 下只给 DC 工作点标量），故把探针源也加入 i() 请求。
            branchNames.AddRange(ordered
                .Where(pair => componentById[pair.Key].Kind == SpiceComponentKind.GenericNpnBjt ||
                               componentById[pair.Key].Kind == SpiceComponentKind.GenericPnpBjt)
                .Select(pair => GetBjtCollectorProbeName(pair.Value)));
            foreach (var branch in branchNames)
                requests.Add(new SpiceAcOutputRequest(SpiceAcOutputKind.BranchCurrent, "i(" + branch + ")", branch));

            var frequency = circuit.AnalysisSettings.FrequencyHz.ToString("R", CultureInfo.InvariantCulture);
            builder.AppendLine().AppendLine(".control").AppendLine("set noaskquit")
                .AppendLine("ac lin 1 " + frequency + " " + frequency)
                .AppendLine("echo " + BeginMarker);
            foreach (var request in requests) builder.AppendLine("print " + request.Expression);
            builder.AppendLine("echo " + EndMarker).AppendLine("quit").AppendLine(".endc").AppendLine().AppendLine(".end");

            return new SpiceAcNetlistDocument { Content = builder.ToString(), OutputRequests = requests };
        }

        private static void AppendComponentLine(StringBuilder builder, SpiceComponentModel component, string graphName, string positive, string negative)
        {
            switch (component.Kind)
            {
                case SpiceComponentKind.AcVoltageSource:
                    builder.Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(" AC ")
                        .Append(component.GetRequiredParameter(SpiceParameterKey.AcMagnitude).ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(component.AcPhaseDegrees.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    return;
                case SpiceComponentKind.DcVoltageSource:
                    // DC 偏置源：显式 DC 关键字且不带 AC 关键字，ngspice 语义下 AC 小信号幅值为 0，仅提供工作点偏置。
                    builder.Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(" DC ")
                        .Append(component.GetRequiredParameter(SpiceParameterKey.DcVoltage).ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    return;
                case SpiceComponentKind.CurrentProbe:
                    builder.Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(" 0").AppendLine();
                    return;
                case SpiceComponentKind.IdealSwitch:
                    builder.Append('R').Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(' ')
                        .Append(SpiceNetlistBuilder.SwitchResistance(component).ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    return;
                case SpiceComponentKind.Resistor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Resistance));
                    return;
                case SpiceComponentKind.Capacitor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Capacitance));
                    return;
                case SpiceComponentKind.Inductor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Inductance));
                    return;
                default:
                    throw new InvalidOperationException("Unsupported component reached the AC netlist builder: " + component.Kind + ".");
            }
        }

        private static void AppendIdealOperationalAmplifierLine(StringBuilder builder, SpiceComponentModel component, string graphName, SpiceCircuitGraph graph)
        {
            // DC 和单频 AC 共用同一线性 VCVS：频率响应完全由外部 R/C/L 决定，固定增益模型不暗含带宽、饱和或电源轨。
            var output = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.OutputTerminalId)];
            var nonInverting = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NonInvertingTerminalId)];
            var inverting = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.InvertingTerminalId)];
            builder.Append(graphName).Append(' ').Append(output).Append(" 0 ").Append(nonInverting).Append(' ').Append(inverting).Append(' ')
                .Append(SpiceComponentDefaults.IdealOperationalAmplifierOpenLoopGain.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        private static void AppendValueLine(StringBuilder builder, string name, string positive, string negative, double value)
        {
            builder.Append(name).Append(' ').Append(positive).Append(' ').Append(negative).Append(' ')
                .Append(value.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        /// <summary>
        /// 发射 BJT 的 Q 行与其 collector 支路内部 0V 探针源。
        /// Q 行端子顺序 collector/base/emitter（与 DC builder 一致）；collector 脚改接内部节点，
        /// 由 <c>V&lt;QName&gt;C &lt;collector_net&gt; &lt;collector_int&gt; DC 0</c> 串联回真实 collector 网络，
        /// 使 i(V&lt;QName&gt;C) 可读取复数小信号 collector 电流（实测 @q[ic] 在 .ac 下只输出 DC 工作点标量）。
        /// </summary>
        private static void AppendBjtLines(StringBuilder builder, SpiceComponentModel component, string graphName, SpiceCircuitGraph graph)
        {
            var collectorNet = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.CollectorTerminalId)];
            var baseNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.BaseTerminalId)];
            var emitter = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.EmitterTerminalId)];
            var collectorInternal = GetBjtCollectorProbeNode(graphName);
            var modelName = component.Kind == SpiceComponentKind.GenericNpnBjt
                ? SpiceComponentDefaults.NpnGenericModelName
                : SpiceComponentDefaults.PnpGenericModelName;
            builder.Append(graphName).Append(' ').Append(collectorInternal).Append(' ').Append(baseNode).Append(' ').Append(emitter).Append(' ').Append(modelName).AppendLine();
            builder.Append(GetBjtCollectorProbeName(graphName)).Append(' ').Append(collectorNet).Append(' ').Append(collectorInternal).Append(" DC 0").AppendLine();
        }

        /// <summary>
        /// BJT collector 探针源的确定性命名：Q 名前置 V、后置 C（如 Q1 -> VQ1C）。
        /// 与既有 SPICE 名（V+数字、VPROBE+数字、EOP+数字等）永不冲突。
        /// </summary>
        public static string GetBjtCollectorProbeName(string qSpiceName)
        {
            return "V" + qSpiceName + "C";
        }

        /// <summary>
        /// BJT collector 探针源内部节点的确定性命名（如 Q1 -> qcq1），不与图节点 n001... 或参考节点 0 冲突。
        /// </summary>
        public static string GetBjtCollectorProbeNode(string qSpiceName)
        {
            return "qc" + qSpiceName.ToLowerInvariant();
        }
    }
}
