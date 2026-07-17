using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Netlist
{
    public sealed class SpiceNetlistDocument
    {
        public string Content { get; set; }
        public IReadOnlyList<string> PrintedNodes { get; set; }
        public IReadOnlyList<string> PrintedBranchNames { get; set; }
    }

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
                builder.Append(pair.Value).Append(' ').Append(a).Append(' ').Append(b).Append(' ')
                    .Append(GetValue(component).ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            }

            var nodes = graph.NodeByTerminal.Values.Where(node => node != "0").Distinct(StringComparer.Ordinal).OrderBy(node => node, StringComparer.Ordinal).ToList();
            var branches = ordered.Where(pair => componentById[pair.Key].Kind == SpiceComponentKind.DcVoltageSource || componentById[pair.Key].Kind == SpiceComponentKind.Inductor)
                .Select(pair => pair.Value).ToList();
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
                case SpiceComponentKind.DcVoltageSource: component.TryGetParameter(SpiceParameterKey.DcVoltage, out var volts); return volts;
                case SpiceComponentKind.Resistor: component.TryGetParameter(SpiceParameterKey.Resistance, out var ohms); return ohms;
                case SpiceComponentKind.Capacitor: component.TryGetParameter(SpiceParameterKey.Capacitance, out var farads); return farads;
                case SpiceComponentKind.Inductor: component.TryGetParameter(SpiceParameterKey.Inductance, out var henries); return henries;
                default: throw new InvalidOperationException("Ground has no SPICE element line.");
            }
        }
    }
}
