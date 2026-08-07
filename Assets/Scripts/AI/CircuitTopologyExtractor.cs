using System.Collections.Generic;
using ElectricalSim.Core;
using ElectricalSim.Templates;

namespace ElectricalSim.AI
{
    public static class CircuitTopologyExtractor
    {
        public static CircuitTopologyGraph FromWorkspace(IReadOnlyList<CircuitComponent> components, IReadOnlyList<WireView> wires)
        {
            var graph = new CircuitTopologyGraph();
            var indices = new Dictionary<CircuitComponent, int>();
            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    if (component == null || component.Definition == null) continue;
                    indices[component] = graph.Nodes.Count;
                    graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = component.Definition.name, Kind = component.Definition.kind });
                }
            }

            if (wires != null)
            {
                for (var i = 0; i < wires.Count; i++)
                {
                    var wire = wires[i];
                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null ||
                        !indices.TryGetValue(wire.StartTerminal.Owner, out var a) || !indices.TryGetValue(wire.EndTerminal.Owner, out var b)) continue;
                    graph.Edges.Add(new TerminalTopologyEdge { NodeA = a, TerminalA = wire.StartTerminal.TerminalId, NodeB = b, TerminalB = wire.EndTerminal.TerminalId });
                }
            }

            return graph;
        }

        public static CircuitTopologyGraph FromTemplate(CircuitTemplateDto template)
        {
            var graph = new CircuitTopologyGraph();
            var indices = new Dictionary<string, int>();
            if (template == null) return graph;
            foreach (var component in template.components)
            {
                if (component == null || string.IsNullOrWhiteSpace(component.instanceId) || string.IsNullOrWhiteSpace(component.definitionName)) continue;
                indices[component.instanceId] = graph.Nodes.Count;
                graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = component.definitionName });
            }
            foreach (var wire in template.wires)
            {
                if (wire == null || !indices.TryGetValue(wire.startComponentId, out var a) || !indices.TryGetValue(wire.endComponentId, out var b)) continue;
                graph.Edges.Add(new TerminalTopologyEdge { NodeA = a, TerminalA = wire.startTerminalId, NodeB = b, TerminalB = wire.endTerminalId });
            }
            return graph;
        }
    }
}
