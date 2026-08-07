using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Core;

namespace ElectricalSim.AI
{
    public sealed class CircuitTopologyGraph
    {
        public readonly List<ComponentTopologyNode> Nodes = new List<ComponentTopologyNode>();
        public readonly List<TerminalTopologyEdge> Edges = new List<TerminalTopologyEdge>();

        public string SignatureForNode(int nodeIndex)
        {
            var node = Nodes[nodeIndex];
            var terminalDegrees = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var edge in Edges)
            {
                if (edge.NodeA == nodeIndex) AddDegree(terminalDegrees, edge.TerminalA);
                if (edge.NodeB == nodeIndex) AddDegree(terminalDegrees, edge.TerminalB);
            }

            return node.DefinitionName + "|" + string.Join(",", terminalDegrees.Select(pair => pair.Key + ":" + pair.Value));
        }

        public int CountEdges(int nodeA, string terminalA, int nodeB, string terminalB)
        {
            var count = 0;
            for (var i = 0; i < Edges.Count; i++)
            {
                var edge = Edges[i];
                if (edge.Matches(nodeA, terminalA, nodeB, terminalB)) count++;
            }

            return count;
        }

        private static void AddDegree(IDictionary<string, int> degrees, string terminal)
        {
            terminal = terminal ?? string.Empty;
            degrees[terminal] = degrees.TryGetValue(terminal, out var current) ? current + 1 : 1;
        }
    }

    public sealed class ComponentTopologyNode
    {
        public string DefinitionName;
        public ComponentKind Kind;
    }

    public sealed class TerminalTopologyEdge
    {
        public int NodeA;
        public string TerminalA;
        public int NodeB;
        public string TerminalB;

        public bool Matches(int nodeA, string terminalA, int nodeB, string terminalB)
        {
            return NodeA == nodeA && TerminalA == terminalA && NodeB == nodeB && TerminalB == terminalB ||
                NodeA == nodeB && TerminalA == terminalB && NodeB == nodeA && TerminalB == terminalA;
        }
    }
}
