using System;
using System.Collections.Generic;
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
            var templates = LoadCatalogTemplates();
            if (templates.Count != 18)
            {
                throw new InvalidOperationException("Expected 18 catalog templates, got " + templates.Count + ".");
            }

            for (var i = 0; i < templates.Count; i++)
            {
                var item = templates[i];
                if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var template, out var error))
                {
                    throw new InvalidOperationException("Unable to load template " + item.templateId + ": " + error);
                }

                var graph = CircuitTopologyExtractor.FromTemplate(template);
                AssertMatch(graph, graph, true);
            }

            Debug.Log("Circuit topology matcher tests: 3 generic + 18 template self-match checks passed.");
        }

        private static List<CircuitTemplateCatalogItemDto> LoadCatalogTemplates()
        {
            var asset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            var catalog = asset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text) : null;
            if (catalog == null || catalog.templates == null)
            {
                throw new InvalidOperationException("Unable to load template catalog.");
            }

            return new List<CircuitTemplateCatalogItemDto>(catalog.templates);
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
