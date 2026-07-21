using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ElectricalSim.AI
{
    public static class CircuitTopologyMatcher
    {
        public static bool IsExactMatch(CircuitTopologyGraph candidate, CircuitTopologyGraph template, out int backtrackSteps)
        {
            backtrackSteps = 0;
            if (!PassesPrefilter(candidate, template)) return false;

            var signatures = Enumerable.Range(0, candidate.Nodes.Count).ToDictionary(index => index, candidate.SignatureForNode);
            var templateSignatures = Enumerable.Range(0, template.Nodes.Count).ToDictionary(index => index, template.SignatureForNode);
            var candidateMap = new Dictionary<int, int>();
            var usedTemplateNodes = new HashSet<int>();
            return MatchNext(candidate, template, signatures, templateSignatures, candidateMap, usedTemplateNodes, ref backtrackSteps);
        }

        public static bool PassesPrefilter(CircuitTopologyGraph candidate, CircuitTopologyGraph template)
        {
            if (candidate == null || template == null || candidate.Nodes.Count != template.Nodes.Count || candidate.Edges.Count != template.Edges.Count) return false;
            return CountDefinitions(candidate).SequenceEqual(CountDefinitions(template));
        }

        private static bool MatchNext(
            CircuitTopologyGraph candidate,
            CircuitTopologyGraph template,
            IReadOnlyDictionary<int, string> candidateSignatures,
            IReadOnlyDictionary<int, string> templateSignatures,
            IDictionary<int, int> map,
            ISet<int> used,
            ref int steps)
        {
            if (map.Count == candidate.Nodes.Count) return true;
            var next = Enumerable.Range(0, candidate.Nodes.Count).Where(index => !map.ContainsKey(index))
                .OrderBy(index => templateSignatures.Count(pair => pair.Value == candidateSignatures[index] && !used.Contains(pair.Key))).First();
            foreach (var target in Enumerable.Range(0, template.Nodes.Count).Where(index => !used.Contains(index) && templateSignatures[index] == candidateSignatures[next]))
            {
                steps++;
                if (!MatchesAssignedEdges(candidate, template, next, target, map)) continue;
                map[next] = target;
                used.Add(target);
                if (MatchNext(candidate, template, candidateSignatures, templateSignatures, map, used, ref steps)) return true;
                used.Remove(target);
                map.Remove(next);
            }
            return false;
        }

        private static bool MatchesAssignedEdges(CircuitTopologyGraph candidate, CircuitTopologyGraph template, int candidateNode, int templateNode, IDictionary<int, int> map)
        {
            foreach (var edge in candidate.Edges)
            {
                var otherCandidate = edge.NodeA == candidateNode ? edge.NodeB : edge.NodeB == candidateNode ? edge.NodeA : -1;
                if (otherCandidate < 0 || !map.TryGetValue(otherCandidate, out var otherTemplate)) continue;
                var terminal = edge.NodeA == candidateNode ? edge.TerminalA : edge.TerminalB;
                var otherTerminal = edge.NodeA == candidateNode ? edge.TerminalB : edge.TerminalA;
                if (candidate.CountEdges(candidateNode, terminal, otherCandidate, otherTerminal) !=
                    template.CountEdges(templateNode, terminal, otherTemplate, otherTerminal)) return false;
            }
            return true;
        }

        private static IEnumerable<KeyValuePair<string, int>> CountDefinitions(CircuitTopologyGraph graph)
        {
            return graph.Nodes.GroupBy(node => node.DefinitionName).Select(group => new KeyValuePair<string, int>(group.Key, group.Count())).OrderBy(pair => pair.Key);
        }
    }
}
