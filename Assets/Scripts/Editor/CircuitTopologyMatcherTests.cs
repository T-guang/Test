using System;
using ElectricalSim.AI;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    public static class CircuitTopologyMatcherTests
    {
        [MenuItem("Tools/Tests/Run Circuit Topology Matcher Tests")]
        public static void RunTests()
        {
            AssertMatch(BuildChain("Power", "Switch", "Lamp"), BuildChain("Power", "Switch", "Lamp"), true);
            AssertMatch(BuildChain("Power", "Switch", "Lamp"), BuildChain("Power", "Switch", "Fan"), false);
            var extra = BuildChain("Power", "Switch", "Lamp");
            extra.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "N", NodeB = 2, TerminalB = "N" });
            AssertMatch(extra, BuildChain("Power", "Switch", "Lamp"), false);
            if (!CircuitTemplateLoader.TryLoad("Blueprints/Templates/single_lamp_template", out var singleLamp, out var error))
            {
                throw new InvalidOperationException("Unable to load single lamp template: " + error);
            }
            AssertMatch(CircuitTopologyExtractor.FromTemplate(singleLamp), CircuitTopologyExtractor.FromTemplate(singleLamp), true);
            Debug.Log("Circuit topology matcher tests: 4/4 passed.");
        }

        private static CircuitTopologyGraph BuildChain(string first, string second, string third)
        {
            var graph = new CircuitTopologyGraph();
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = first });
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = second });
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = third });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "L", NodeB = 1, TerminalB = "1" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = "2", NodeB = 2, TerminalB = "L" });
            return graph;
        }

        private static void AssertMatch(CircuitTopologyGraph candidate, CircuitTopologyGraph template, bool expected)
        {
            var actual = CircuitTopologyMatcher.IsExactMatch(candidate, template, out _);
            if (actual != expected) throw new InvalidOperationException("Unexpected topology match result.");
        }
    }
}
