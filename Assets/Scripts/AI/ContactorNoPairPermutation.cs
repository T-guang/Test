using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 接触器常开辅助触点对（NO1/NO2）的 per-component 双射置换工具。
    /// 仅在同一 Contactor 实例内部允许完整 NO pair 互换（NO1&lt;-&gt;NO2），
    /// 不跨元件、不跨触点类型（NO&lt;-&gt;NC/Main/A1A2 禁止）、不半 pair 替换。
    /// 置换以 <see cref="ContactorTerminalSchema.NormallyOpenContactPairs"/> 为唯一依据，
    /// 不在业务代码中散写 13/14/33/34。
    /// </summary>
    public static class ContactorNoPairPermutation
    {
        /// <summary>
        /// 判定 candidate 是否可通过合法的 per-component NO pair 置换后与 template 精确匹配。
        /// 不改变 ExactMatch 语义：仅当原 IsExactMatch 为 false 且置换后为 true 时返回 true。
        /// 置换作用域严格限定为单个 graph node（即单个元件实例），不跨元件。
        /// </summary>
        public static bool IsNoPairEquivalentMatch(CircuitTopologyGraph candidate, CircuitTopologyGraph template, out int backtrackSteps)
        {
            backtrackSteps = 0;
            if (candidate == null || template == null) return false;
            if (!CircuitTopologyMatcher.PassesPrefilter(candidate, template)) return false;

            // 先尝试 identity（不置换），如果已经是 ExactMatch 则不算 EquivalentMatch。
            // 调用方应先检查 IsExactMatch，此处仍做一次防御性跳过。
            if (CircuitTopologyMatcher.IsExactMatch(candidate, template, out var identitySteps))
            {
                backtrackSteps += identitySteps;
                return false;
            }

            // 收集 candidate 中每个 node 使用的 NO pair 端子，确定哪些 node 是接触器且可置换。
            var contactorNodes = FindContactorNodesWithNoPairs(candidate);
            if (contactorNodes.Count == 0) return false;

            // 只要 schema 有 > 1 个 NO pair 且 candidate 有接触器使用 NO pair，就有置换空间。
            // 即使 candidate 只用了 1 个 NO pair，也可映射到 schema 中的另一个 NO pair。
            if (ContactorTerminalSchema.NormallyOpenContactPairs.Count <= 1) return false;

            // 枚举 per-component NO pair 置换组合，跳过全 identity（已由上方 IsExactMatch 覆盖）。
            foreach (var permutation in EnumeratePermutations(contactorNodes))
            {
                if (IsAllIdentity(permutation, contactorNodes)) continue;
                var permuted = ApplyPermutation(candidate, permutation);
                backtrackSteps++;
                if (CircuitTopologyMatcher.IsExactMatch(permuted, template, out var steps))
                {
                    backtrackSteps += steps;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 识别 graph 中使用了 NO pair 端子的 node，并记录每个 node 实际使用的 NO pair 列表。
        /// 判断依据完全来自 <see cref="ContactorTerminalSchema.NormallyOpenContactPairs"/>，
        /// 不散写端子编号。
        /// </summary>
        private static List<ContactorNodeInfo> FindContactorNodesWithNoPairs(CircuitTopologyGraph graph)
        {
            var nodeTerminals = new Dictionary<int, HashSet<string>>();
            foreach (var edge in graph.Edges)
            {
                CollectTerminal(nodeTerminals, edge.NodeA, edge.TerminalA);
                CollectTerminal(nodeTerminals, edge.NodeB, edge.TerminalB);
            }

            var result = new List<ContactorNodeInfo>();
            foreach (var kvp in nodeTerminals)
            {
                var usedPairs = GetUsedNoPairs(kvp.Value);
                if (usedPairs.Count > 0)
                {
                    result.Add(new ContactorNodeInfo { NodeIndex = kvp.Key, UsedPairs = usedPairs });
                }
            }

            return result;
        }

        private static void CollectTerminal(Dictionary<int, HashSet<string>> map, int node, string terminal)
        {
            if (!map.TryGetValue(node, out var set))
            {
                set = new HashSet<string>();
                map[node] = set;
            }
            if (!string.IsNullOrEmpty(terminal))
            {
                set.Add(terminal);
            }
        }

        /// <summary>
        /// 从端子集合中找出该元件实际使用的 NO pair 列表。
        /// 只要 pair 的任一端子出现在端子集合中，即视为该 pair 被使用。
        /// </summary>
        private static List<ContactorContactPair> GetUsedNoPairs(HashSet<string> terminals)
        {
            var used = new List<ContactorContactPair>();
            var noPairs = ContactorTerminalSchema.NormallyOpenContactPairs;
            for (var i = 0; i < noPairs.Count; i++)
            {
                var pair = noPairs[i];
                if (terminals.Contains(pair.StartTerminalId) || terminals.Contains(pair.EndTerminalId))
                {
                    used.Add(pair);
                }
            }

            return used;
        }

        /// <summary>
        /// 枚举 per-component NO pair 置换的笛卡尔积。
        /// 每个接触器独立选择 identity 或 swap（仅当使用了 >= 2 个 NO pair 时才有 swap 选项）。
        /// </summary>
        private static IEnumerable<Dictionary<int, Dictionary<string, string>>> EnumeratePermutations(
            List<ContactorNodeInfo> contactorNodes)
        {
            var nodeOptions = new List<List<Dictionary<string, string>>>();
            foreach (var info in contactorNodes)
            {
                var options = GeneratePairOptions(info.UsedPairs);
                nodeOptions.Add(options);
            }

            // 笛卡尔积枚举
            var indices = new int[nodeOptions.Count];
            while (true)
            {
                var permutation = new Dictionary<int, Dictionary<string, string>>();
                for (var i = 0; i < contactorNodes.Count; i++)
                {
                    permutation[contactorNodes[i].NodeIndex] = nodeOptions[i][indices[i]];
                }
                yield return permutation;

                // 进位
                var carry = 0;
                while (carry < nodeOptions.Count)
                {
                    indices[carry]++;
                    if (indices[carry] < nodeOptions[carry].Count) break;
                    indices[carry] = 0;
                    carry++;
                }
                if (carry == nodeOptions.Count) yield break;
            }
        }

        /// <summary>
        /// 为单个接触器生成所有合法的 NO pair 双射映射。
        /// candidate 使用了 N 个 NO pair，从 schema 全部 NO pair 中选 N 个做排列（P(schema, N)）。
        /// 每种排列是完整 pair 到完整 pair 的映射，保证 pair 完整性和双射性。
        /// </summary>
        private static List<Dictionary<string, string>> GeneratePairOptions(List<ContactorContactPair> usedPairs)
        {
            var options = new List<Dictionary<string, string>>();
            var allNoPairs = ContactorTerminalSchema.NormallyOpenContactPairs;
            var permutations = GetKPermutations(allNoPairs, usedPairs.Count);

            foreach (var perm in permutations)
            {
                var mapping = new Dictionary<string, string>();
                for (var i = 0; i < usedPairs.Count; i++)
                {
                    var source = usedPairs[i];
                    var target = perm[i];
                    mapping[source.StartTerminalId] = target.StartTerminalId;
                    mapping[source.EndTerminalId] = target.EndTerminalId;
                }
                options.Add(mapping);
            }

            return options;
        }

        /// <summary>
        /// 从 source 中选 k 个元素的所有排列（P(n, k)），用于 NO pair 的双射映射。
        /// </summary>
        private static List<List<T>> GetKPermutations<T>(IReadOnlyList<T> source, int k)
        {
            var result = new List<List<T>>();
            var used = new bool[source.Count];
            var current = new List<T>();
            PermuteK(source, k, used, current, result);
            return result;
        }

        private static void PermuteK<T>(IReadOnlyList<T> source, int k, bool[] used, List<T> current, List<List<T>> result)
        {
            if (current.Count == k)
            {
                result.Add(new List<T>(current));
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current.Add(source[i]);
                PermuteK(source, k, used, current, result);
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }

        /// <summary>
        /// 判断置换是否全部为 identity（无实际交换）。
        /// </summary>
        private static bool IsAllIdentity(
            Dictionary<int, Dictionary<string, string>> permutation,
            List<ContactorNodeInfo> contactorNodes)
        {
            foreach (var info in contactorNodes)
            {
                if (!permutation.TryGetValue(info.NodeIndex, out var mapping)) continue;
                foreach (var pair in info.UsedPairs)
                {
                    if (mapping.TryGetValue(pair.StartTerminalId, out var mapped) &&
                        mapped != pair.StartTerminalId)
                        return false;
                    if (mapping.TryGetValue(pair.EndTerminalId, out var mappedEnd) &&
                        mappedEnd != pair.EndTerminalId)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 应用 per-component 端子置换，生成新的 graph。
        /// 仅 remap NO pair 端子，其他端子（NC/Main/A1A2 等）保持不变。
        /// </summary>
        private static CircuitTopologyGraph ApplyPermutation(
            CircuitTopologyGraph graph,
            Dictionary<int, Dictionary<string, string>> permutation)
        {
            var result = new CircuitTopologyGraph();
            foreach (var node in graph.Nodes)
            {
                result.Nodes.Add(new ComponentTopologyNode { DefinitionName = node.DefinitionName });
            }

            foreach (var edge in graph.Edges)
            {
                var terminalA = edge.TerminalA;
                var terminalB = edge.TerminalB;

                if (permutation.TryGetValue(edge.NodeA, out var remapA))
                {
                    terminalA = RemapTerminal(remapA, terminalA);
                }

                if (permutation.TryGetValue(edge.NodeB, out var remapB))
                {
                    terminalB = RemapTerminal(remapB, terminalB);
                }

                result.Edges.Add(new TerminalTopologyEdge
                {
                    NodeA = edge.NodeA,
                    TerminalA = terminalA,
                    NodeB = edge.NodeB,
                    TerminalB = terminalB
                });
            }

            return result;
        }

        private static string RemapTerminal(Dictionary<string, string> mapping, string terminal)
        {
            if (terminal != null && mapping.TryGetValue(terminal, out var mapped))
            {
                return mapped;
            }

            return terminal;
        }

        private struct ContactorNodeInfo
        {
            public int NodeIndex;
            public List<ContactorContactPair> UsedPairs;
        }
    }
}
