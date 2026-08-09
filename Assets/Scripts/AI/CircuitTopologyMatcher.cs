using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 对规范化后的元件节点与外部 Wire 边执行精确结构匹配。它比较的是元件定义、端子身份及节点间
    /// 的边关系，不比较运行时触点导通、对象引用或 Wire 的创建顺序；因此拓扑相同不等于当前运行态相同。
    /// EquivalentMatch 和 Ambiguous 的产品语义由上层识别流程决定，本类仅提供 ExactMatch 的确定性基础。
    /// </summary>
    public static class CircuitTopologyMatcher
    {
        // 签名先缩小候选，再以已分配边做回溯验证。不能只比较元件数量或 Wire 数量：相同数量的
        // 元件也可能接在不同 terminal/node 上。backtrackSteps 用于诊断搜索成本，不参与匹配语义。
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

        // 预筛是必要条件而非匹配结论：节点、边和定义计数相同仍可能因 terminal identity 或节点连接关系不同
        // 而 NoMatch。保持它廉价且保守，不能在此处引入运行态或模板名称特判。
        public static bool PassesPrefilter(CircuitTopologyGraph candidate, CircuitTopologyGraph template)
        {
            if (candidate == null || template == null || candidate.Nodes.Count != template.Nodes.Count || candidate.Edges.Count != template.Edges.Count) return false;
            return CountDefinitions(candidate).SequenceEqual(CountDefinitions(template));
        }

        // 优先选择剩余同签名候选最少的节点，降低同类元件较多时的组合爆炸；每次映射只在与已映射
        // 邻居的端子边一致时继续，回溯不得留下 map/used 的临时状态。
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

        // 只检查已映射邻居可让回溯逐步收紧约束；边数按两端 terminal identity / 对应关系比较，避免多个相同元件节点因
        // 粗粒度连通计数相等而被错误视作 ExactMatch。
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
