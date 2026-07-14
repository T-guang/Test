using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Core;
using ElectricalSim.Templates;

namespace ElectricalSim.Practice
{
    public class ConnectionCheckResult
    {
        public int MatchScore { get; set; } = 100;
        public List<string> MissingConnections { get; set; } = new List<string>();
        public List<string> WrongConnections { get; set; } = new List<string>();
        public List<string> MissingComponents { get; set; } = new List<string>();
        public List<string> ExtraComponents { get; set; } = new List<string>();
        public bool NeedsReview { get; set; }
    }

    public class UnionFind<T>
    {
        private readonly Dictionary<T, T> parent = new Dictionary<T, T>();

        public void Add(T item)
        {
            if (!parent.ContainsKey(item))
            {
                parent[item] = item;
            }
        }

        public T Find(T item)
        {
            if (!parent.ContainsKey(item)) return default;
            if (EqualityComparer<T>.Default.Equals(parent[item], item))
                return item;
            parent[item] = Find(parent[item]);
            return parent[item];
        }

        public void Union(T item1, T item2)
        {
            Add(item1);
            Add(item2);
            T root1 = Find(item1);
            T root2 = Find(item2);
            if (!EqualityComparer<T>.Default.Equals(root1, root2))
            {
                parent[root1] = root2;
            }
        }

        public IEnumerable<T> GetAllItems() => parent.Keys;

        public List<List<T>> GetGroups()
        {
            var groups = new Dictionary<T, List<T>>();
            var keys = parent.Keys.ToList();
            foreach (var item in keys)
            {
                T root = Find(item);
                if (!groups.ContainsKey(root)) groups[root] = new List<T>();
                groups[root].Add(item);
            }
            return groups.Values.Where(g => g.Count > 1).ToList();
        }
    }

    /// <summary>
    /// 旧练习流程兼容层的连接检查实现，保留旧结果模型、旧评分入口和既有调用方所需的转换行为。
    /// 它与 Netlist/PracticeConnectionChecker 同名但职责不同：后者是当前结构化网表比对层；当前练习主提交流程
    /// 直接调用后者，本类不应被视为重复代码后直接合并或删除。本类不创建 UI，也不维护练习会话生命周期。
    /// 修改实例映射、连通分组或旧结果转换前，必须回归正确、缺失、多余和错误连接的兼容用例。
    /// </summary>
    public static class PracticeConnectionChecker
    {
        /// <summary>
        /// 按旧结果模型执行元件数量、连通分组和实例映射比较。该入口服务仍使用 ConnectionCheckResult 的兼容调用方，
        /// 不替代 Netlist 层的结构化 Issue 输出，也不决定练习会话能否开始或结束。
        /// </summary>
        public static ConnectionCheckResult Check(WorkspaceController workspace, CircuitTemplateDto template)
        {
            var result = new ConnectionCheckResult();
            if (template == null || workspace == null) return result;

            var stdComps = template.components;
            var stuComps = workspace.Components.ToList();

            var stdDefCounts = stdComps.GroupBy(c => c.definitionName).ToDictionary(g => g.Key, g => g.Count());
            var stuDefCounts = stuComps.GroupBy(c => c.Definition.name).ToDictionary(g => g.Key, g => g.Count());

            foreach (var kvp in stdDefCounts)
            {
                int stdCount = kvp.Value;
                stuDefCounts.TryGetValue(kvp.Key, out int stuCount);
                if (stuCount < stdCount)
                {
                    var compName = GetDisplayName(kvp.Key, workspace);
                    result.MissingComponents.Add($"缺少 {stdCount - stuCount} 个 【{compName}】 (所需: {stdCount}, 实际: {stuCount})");
                }
                else if (stuCount > stdCount)
                {
                    var compName = GetDisplayName(kvp.Key, workspace);
                    result.ExtraComponents.Add($"多余 {stuCount - stdCount} 个 【{compName}】 (所需: {stdCount}, 实际: {stuCount})");
                }
            }

            foreach (var kvp in stuDefCounts)
            {
                if (!stdDefCounts.ContainsKey(kvp.Key))
                {
                    var compName = GetDisplayName(kvp.Key, workspace);
                    result.ExtraComponents.Add($"多余 {kvp.Value} 个 【{compName}】");
                }
            }

            if (result.MissingComponents.Count > 0)
            {
                result.MatchScore = 0;
                return result;
            }

            var stdNodes = new UnionFind<string>();
            foreach (var w in template.wires)
            {
                stdNodes.Union($"{w.startComponentId}.{w.startTerminalId}", $"{w.endComponentId}.{w.endTerminalId}");
            }

            var stuNodes = new UnionFind<string>();
            foreach (var w in workspace.WireManager.Wires)
            {
                stuNodes.Union($"{w.StartTerminal.Owner.InstanceId}.{w.StartTerminal.TerminalId}", $"{w.EndTerminal.Owner.InstanceId}.{w.EndTerminal.TerminalId}");
            }

            var mappings = GenerateAllMappings(stdComps, stuComps);
            MappingResult bestMapping = null;
            int maxScore = -9999;
            int tieCount = 0;

            foreach (var map in mappings)
            {
                var r = EvaluateMapping(map, stdNodes, stuNodes, template, workspace);
                int score = - (r.MissingConnections.Count * 25 + r.WrongConnections.Count * 25);
                if (score > maxScore)
                {
                    maxScore = score;
                    bestMapping = r;
                    tieCount = 1;
                }
                else if (score == maxScore)
                {
                    tieCount++;
                }
            }

            if (tieCount > 1 && maxScore < 0)
            {
                result.NeedsReview = true;
                result.MissingConnections.Add("无法唯一判断元件对应关系，请检查是否放置了多余元件或接线过于混乱。");
                result.MatchScore = 0;
                return result;
            }

            if (bestMapping != null)
            {
                result.MissingConnections.AddRange(bestMapping.MissingConnections);
                result.WrongConnections.AddRange(bestMapping.WrongConnections);
                result.MatchScore = 100 - result.MissingConnections.Count * 25 - result.WrongConnections.Count * 25;
                if (result.MatchScore < 0) result.MatchScore = 0;
            }

            return result;
        }

        private static string GetDisplayName(string defName, WorkspaceController workspace)
        {
            var comp = workspace.Components.FirstOrDefault(c => c.Definition.name == defName);
            if (comp != null && !string.IsNullOrWhiteSpace(comp.Definition.displayName))
            {
                return comp.Definition.displayName.Replace("\n", "");
            }
            return defName;
        }

        private static string GetDisplayName(CircuitComponent comp)
        {
            if (comp != null && comp.Definition != null && !string.IsNullOrWhiteSpace(comp.Definition.displayName))
            {
                return comp.Definition.displayName.Replace("\n", "");
            }
            return "未知元件";
        }

        // 旧兼容路径按定义分组枚举实例对应；排列规模会随同类元件数量增长，不能把它误当作当前 Netlist 求解器的实现副本。
        private static List<Dictionary<string, string>> GenerateAllMappings(List<TemplateComponentDto> stdComps, List<CircuitComponent> stuComps)
        {
            var defGroups = stdComps.GroupBy(c => c.definitionName).ToList();
            var allMappings = new List<Dictionary<string, string>> { new Dictionary<string, string>() };

            foreach (var group in defGroups)
            {
                var stdIds = group.Select(c => c.instanceId).ToList();
                var stuIds = stuComps.Where(c => c.Definition.name == group.Key).Select(c => c.InstanceId).ToList();
                var permutations = GetPermutations(stuIds, stdIds.Count);

                var newMappings = new List<Dictionary<string, string>>();
                foreach (var map in allMappings)
                {
                    foreach (var perm in permutations)
                    {
                        var newMap = new Dictionary<string, string>(map);
                        for (int i = 0; i < stdIds.Count; i++)
                        {
                            newMap[stdIds[i]] = perm[i];
                        }
                        newMappings.Add(newMap);
                    }
                }
                allMappings = newMappings;
            }

            return allMappings;
        }

        private static List<List<string>> GetPermutations(List<string> list, int length)
        {
            if (length == 0) return new List<List<string>> { new List<string>() };
            if (length == 1) return list.Select(t => new List<string> { t }).ToList();

            var perms = new List<List<string>>();
            foreach (var t in list)
            {
                var subList = new List<string>(list);
                subList.Remove(t);
                foreach (var subPerm in GetPermutations(subList, length - 1))
                {
                    subPerm.Insert(0, t);
                    perms.Add(subPerm);
                }
            }
            return perms;
        }

        class MappingResult
        {
            public List<string> MissingConnections = new List<string>();
            public List<string> WrongConnections = new List<string>();
        }

        // 在旧 UnionFind 连通组之间做双向映射比较，结果仅回填旧 MissingConnections/WrongConnections 模型。
        private static MappingResult EvaluateMapping(Dictionary<string, string> stdToStuMap, UnionFind<string> stdNodes, UnionFind<string> stuNodes, CircuitTemplateDto template, WorkspaceController workspace)
        {
            var r = new MappingResult();
            var stuToStdMap = stdToStuMap.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            
            var stdGroups = stdNodes.GetGroups();
            foreach (var group in stdGroups)
            {
                var stuMappedTerms = new List<string>();
                foreach (var stdTerm in group)
                {
                    var parts = stdTerm.Split('.');
                    if (stdToStuMap.TryGetValue(parts[0], out string stuId))
                    {
                        stuMappedTerms.Add($"{stuId}.{parts[1]}");
                    }
                }

                if (stuMappedTerms.Count > 1)
                {
                    var firstStuTerm = stuMappedTerms[0];
                    var firstStuRoot = stuNodes.Find(firstStuTerm);
                    
                    for (int i = 1; i < stuMappedTerms.Count; i++)
                    {
                        var currentStuTerm = stuMappedTerms[i];
                        var currentStuRoot = stuNodes.Find(currentStuTerm);

                        if (firstStuRoot == null || currentStuRoot == null || !firstStuRoot.Equals(currentStuRoot))
                        {
                            var parts1 = stuMappedTerms[i - 1].Split('.');
                            var parts2 = currentStuTerm.Split('.');
                            var comp1 = workspace.Components.FirstOrDefault(c => c.InstanceId == parts1[0]);
                            var comp2 = workspace.Components.FirstOrDefault(c => c.InstanceId == parts2[0]);
                            r.MissingConnections.Add($"【{GetDisplayName(comp1)}】的 {parts1[1]} 端子 未连接到 【{GetDisplayName(comp2)}】的 {parts2[1]} 端子");
                            
                            // Re-anchor to prevent redundant errors for same disjoint set
                            firstStuRoot = currentStuRoot;
                        }
                    }
                }
            }

            var stuGroups = stuNodes.GetGroups();
            foreach (var group in stuGroups)
            {
                var stdMappedTerms = new List<string>();
                foreach (var stuTerm in group)
                {
                    var parts = stuTerm.Split('.');
                    if (stuToStdMap.TryGetValue(parts[0], out string stdId))
                    {
                        stdMappedTerms.Add($"{stdId}.{parts[1]}");
                    }
                    else
                    {
                        // Connected an extra component terminal that shouldn't be connected
                        var comp = workspace.Components.FirstOrDefault(c => c.InstanceId == parts[0]);
                        r.WrongConnections.Add($"【{GetDisplayName(comp)}】的 {parts[1]} 端子 存在多余连接");
                    }
                }

                if (stdMappedTerms.Count > 1)
                {
                    var firstStdTerm = stdMappedTerms[0];
                    var firstStdRoot = stdNodes.Find(firstStdTerm);

                    for (int i = 1; i < stdMappedTerms.Count; i++)
                    {
                        var currentStdTerm = stdMappedTerms[i];
                        var currentStdRoot = stdNodes.Find(currentStdTerm);

                        if (firstStdRoot == null || currentStdRoot == null || !firstStdRoot.Equals(currentStdRoot))
                        {
                            var stuParts1 = group[i - 1].Split('.');
                            var stuParts2 = group[i].Split('.');
                            var comp1 = workspace.Components.FirstOrDefault(c => c.InstanceId == stuParts1[0]);
                            var comp2 = workspace.Components.FirstOrDefault(c => c.InstanceId == stuParts2[0]);
                            r.WrongConnections.Add($"【{GetDisplayName(comp1)}】的 {stuParts1[1]} 端子 不应连接到 【{GetDisplayName(comp2)}】的 {stuParts2[1]} 端子");
                            
                            firstStdRoot = currentStdRoot;
                        }
                    }
                }
            }

            return r;
        }
    }
}
