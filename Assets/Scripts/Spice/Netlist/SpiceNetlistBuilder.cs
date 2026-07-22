using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Netlist
{
    /// <summary>
    /// 已生成网表及其明确要求 ngspice 输出的向量列表。该列表也是结果解析的完整性契约。
    /// </summary>
    public sealed class SpiceNetlistDocument
    {
        public string Content { get; set; }
        public IReadOnlyList<string> PrintedNodes { get; set; }
        public IReadOnlyList<string> PrintedBranchNames { get; set; }
    }

    /// <summary>
    /// 将已校验的 T2 图转换为确定性 DC 工作点网表；不从用户 instanceId 直接生成 SPICE 名称。
    /// </summary>
    public static class SpiceNetlistBuilder
    {
        public const string BeginMarker = "__SPICE_T2_BEGIN__";
        public const string EndMarker = "__SPICE_T2_END__";

        public static SpiceNetlistDocument BuildDcOperatingPoint(SpiceCircuitModel circuit, SpiceCircuitGraph graph)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            if (graph == null || !graph.IsValid) throw new InvalidOperationException("A valid SpiceCircuitGraph is required before generating a netlist.");

            var componentById = circuit.Components.ToDictionary(component => component.InstanceId, StringComparer.Ordinal);
            var ordered = graph.SpiceNameByComponentId.OrderBy(pair => pair.Value, StringComparer.Ordinal).ToList();
            var builder = new StringBuilder("* ElectricalSimulation2D SPICE T2 DC\n");
            foreach (var pair in ordered)
            {
                var component = componentById[pair.Key];
                var a = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var b = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                builder.Append(GetElementName(component, pair.Value)).Append(' ').Append(a).Append(' ').Append(b).Append(' ')
                    .Append(GetValue(component).ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }

            var nodes = graph.NodeByTerminal.Values.Where(node => node != "0").Distinct(StringComparer.Ordinal).OrderBy(node => node, StringComparer.Ordinal).ToList();
            var branches = ordered.Where(pair => componentById[pair.Key].Kind == SpiceComponentKind.DcVoltageSource || componentById[pair.Key].Kind == SpiceComponentKind.Inductor)
                .Select(pair => pair.Value).ToList();
            // 只打印后续结果层需要的向量，并用唯一标记隔离 ngspice 自身日志。
            builder.AppendLine().AppendLine(".control").AppendLine("set noaskquit").AppendLine("op").AppendLine("echo " + BeginMarker);
            foreach (var node in nodes) builder.AppendLine("print v(" + node + ")");
            foreach (var branch in branches) builder.AppendLine("print i(" + branch + ")");
            builder.AppendLine("echo " + EndMarker).AppendLine("quit").AppendLine(".endc").AppendLine().AppendLine(".end");
            return new SpiceNetlistDocument { Content = builder.ToString(), PrintedNodes = nodes, PrintedBranchNames = branches };
        }

        private static double GetValue(SpiceComponentModel component)
        {
            switch (component.Kind)
            {
                case SpiceComponentKind.DcVoltageSource: return component.GetRequiredParameter(SpiceParameterKey.DcVoltage);
                case SpiceComponentKind.DcCurrentSource: return component.GetRequiredParameter(SpiceParameterKey.DcCurrent);
                case SpiceComponentKind.IdealSwitch: return SwitchResistance(component);
                case SpiceComponentKind.Resistor: return component.GetRequiredParameter(SpiceParameterKey.Resistance);
                case SpiceComponentKind.Capacitor: return component.GetRequiredParameter(SpiceParameterKey.Capacitance);
                case SpiceComponentKind.Inductor: return component.GetRequiredParameter(SpiceParameterKey.Inductance);
                default: throw new InvalidOperationException("Ground has no SPICE element line.");
            }
        }

        private static string GetElementName(SpiceComponentModel component, string graphName)
        {
            // The manual switch is represented by a deterministic RON/ROFF resistor.
            return component.Kind == SpiceComponentKind.IdealSwitch ? "R" + graphName : graphName;
        }

        public static double SwitchResistance(SpiceComponentModel component)
        {
            return component.GetRequiredParameter(SpiceParameterKey.SwitchClosed) > 0.5d ? 1e-3d : 1e12d;
        }
    }
}
