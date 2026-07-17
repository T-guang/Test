using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Topology
{
    public static class SpiceCircuitGraphBuilder
    {
        public static SpiceCircuitGraph Build(SpiceCircuitModel circuit)
        {
            var graph = new SpiceCircuitGraph();
            if (circuit == null || circuit.Components.Count == 0)
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_EMPTY_CIRCUIT", SpiceDiagnosticSeverity.Error, "The circuit contains no components."));
                return graph;
            }

            var components = new Dictionary<string, SpiceComponentModel>(StringComparer.Ordinal);
            for (var i = 0; i < circuit.Components.Count; i++)
            {
                var component = circuit.Components[i];
                if (component == null || string.IsNullOrWhiteSpace(component.InstanceId))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_COMPONENT", SpiceDiagnosticSeverity.Error, "A component is missing its instanceId."));
                    continue;
                }

                if (components.ContainsKey(component.InstanceId))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_DUPLICATE_INSTANCE", SpiceDiagnosticSeverity.Error, "Duplicate component instanceId.", component.InstanceId));
                    continue;
                }

                components.Add(component.InstanceId, component);
            }

            var terminals = new List<SpiceTerminalRef>();
            foreach (var component in components.Values.OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.Ground)
                {
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.GroundTerminalId));
                }
                else
                {
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId));
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId));
                }
            }

            var indexByTerminal = new Dictionary<SpiceTerminalRef, int>();
            for (var i = 0; i < terminals.Count; i++) indexByTerminal[terminals[i]] = i;
            var unionFind = new UnionFind(terminals.Count);
            var connectionCount = terminals.ToDictionary(terminal => terminal, terminal => 0);

            var grounds = components.Values.Where(component => component.Kind == SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal).ToList();
            if (grounds.Count == 0)
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_GROUND_MISSING", SpiceDiagnosticSeverity.Error, "The circuit requires at least one ground component."));
            }
            else
            {
                var firstGround = indexByTerminal[new SpiceTerminalRef(grounds[0].InstanceId, SpiceComponentModel.GroundTerminalId)];
                for (var i = 1; i < grounds.Count; i++) unionFind.Union(firstGround, indexByTerminal[new SpiceTerminalRef(grounds[i].InstanceId, SpiceComponentModel.GroundTerminalId)]);
            }

            foreach (var wire in circuit.Wires)
            {
                if (wire == null || wire.Start == null || wire.End == null)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_WIRE", SpiceDiagnosticSeverity.Error, "A wire endpoint is missing."));
                    continue;
                }

                if (wire.Start.Equals(wire.End))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SELF_CONNECTION", SpiceDiagnosticSeverity.Error, "A terminal cannot connect to itself.", wire.Start.ComponentInstanceId, wire.Start.TerminalId));
                    continue;
                }

                if (!TryResolveTerminal(components, indexByTerminal, wire.Start, graph) || !TryResolveTerminal(components, indexByTerminal, wire.End, graph)) continue;
                unionFind.Union(indexByTerminal[wire.Start], indexByTerminal[wire.End]);
                connectionCount[wire.Start]++;
                connectionCount[wire.End]++;
            }

            ValidateComponents(components, graph);
            foreach (var terminal in terminals)
            {
                if (connectionCount[terminal] == 0 && components[terminal.ComponentInstanceId].Kind != SpiceComponentKind.Ground)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_FLOATING_TERMINAL", SpiceDiagnosticSeverity.Error, "A component terminal has no wire connection.", terminal.ComponentInstanceId, terminal.TerminalId));
                }
            }
            if (!graph.IsValid) return graph;

            var groundedRoot = grounds.Count > 0 ? unionFind.Find(indexByTerminal[new SpiceTerminalRef(grounds[0].InstanceId, SpiceComponentModel.GroundTerminalId)]) : -1;
            var roots = terminals.Select((terminal, index) => new { terminal, root = unionFind.Find(index) })
                .GroupBy(item => item.root).OrderBy(group => group.Key).ToList();
            var nodeIndex = 1;
            foreach (var root in roots)
            {
                var node = root.Key == groundedRoot ? "0" : "n" + nodeIndex++.ToString("D3");
                foreach (var item in root) graph.NodeByTerminal[item.terminal] = node;
            }

            foreach (var component in components.Values.OrderBy(component => component.Kind).ThenBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.Ground) continue;
                var prefix = GetPrefix(component.Kind);
                var number = graph.SpiceNameByComponentId.Count(existing => existing.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) + 1;
                var name = prefix + number;
                graph.SpiceNameByComponentId[component.InstanceId] = name;
                graph.ComponentIdBySpiceName[name] = component.InstanceId;

                var positive = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negative = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                if (positive == negative && component.Kind == SpiceComponentKind.DcVoltageSource)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SOURCE_SHORTED", SpiceDiagnosticSeverity.Error, "A DC voltage source cannot have both terminals on the same node.", component.InstanceId));
                }

                if (positive == negative && component.Kind != SpiceComponentKind.DcVoltageSource)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_COMPONENT_SHORTED", SpiceDiagnosticSeverity.Warning, "Both component terminals resolve to the same node.", component.InstanceId));
                }
            }

            if (!components.Values.Any(component => component.Kind == SpiceComponentKind.DcVoltageSource))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SOURCE_MISSING", SpiceDiagnosticSeverity.Error, "The circuit contains no DC voltage source."));
            }

            foreach (var component in components.Values.Where(component => component.Kind != SpiceComponentKind.Ground && component.Kind != SpiceComponentKind.DcVoltageSource))
            {
                var a = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var b = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                if (a != "0" && b != "0" && a == b)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_FLOATING_COMPONENT", SpiceDiagnosticSeverity.Error, "Component has no path to the ground reference.", component.InstanceId));
                }
            }
            return graph;
        }

        private static bool TryResolveTerminal(Dictionary<string, SpiceComponentModel> components, Dictionary<SpiceTerminalRef, int> indexes, SpiceTerminalRef terminal, SpiceCircuitGraph graph)
        {
            if (!components.TryGetValue(terminal.ComponentInstanceId ?? string.Empty, out var component))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_WIRE_COMPONENT_MISSING", SpiceDiagnosticSeverity.Error, "Wire references a missing component.", terminal.ComponentInstanceId, terminal.TerminalId));
                return false;
            }
            if (!component.HasTerminal(terminal.TerminalId) || !indexes.ContainsKey(terminal))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_WIRE_TERMINAL_MISSING", SpiceDiagnosticSeverity.Error, "Wire references a missing terminal.", terminal.ComponentInstanceId, terminal.TerminalId));
                return false;
            }
            return true;
        }

        private static void ValidateComponents(Dictionary<string, SpiceComponentModel> components, SpiceCircuitGraph graph)
        {
            foreach (var component in components.Values)
            {
                SpiceParameterKey? key = component.Kind == SpiceComponentKind.DcVoltageSource ? SpiceParameterKey.DcVoltage :
                    component.Kind == SpiceComponentKind.Resistor ? SpiceParameterKey.Resistance :
                    component.Kind == SpiceComponentKind.Capacitor ? SpiceParameterKey.Capacitance :
                    component.Kind == SpiceComponentKind.Inductor ? SpiceParameterKey.Inductance : (SpiceParameterKey?)null;
                if (!key.HasValue) continue;
                if (!component.TryGetParameter(key.Value, out var value) || double.IsNaN(value) || double.IsInfinity(value) || (key.Value != SpiceParameterKey.DcVoltage && value <= 0d))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_PARAMETER", SpiceDiagnosticSeverity.Error, "Component parameter is missing or outside the supported DC range.", component.InstanceId));
                }
            }
        }

        private static string GetPrefix(SpiceComponentKind kind)
        {
            switch (kind)
            {
                case SpiceComponentKind.DcVoltageSource: return "V";
                case SpiceComponentKind.Resistor: return "R";
                case SpiceComponentKind.Capacitor: return "C";
                case SpiceComponentKind.Inductor: return "L";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private sealed class UnionFind
        {
            private readonly int[] parent;
            public UnionFind(int count) { parent = Enumerable.Range(0, count).ToArray(); }
            public int Find(int value) { while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; } return value; }
            public void Union(int left, int right) { left = Find(left); right = Find(right); if (left != right) parent[right] = left; }
        }
    }
}
