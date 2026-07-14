using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim.Practice.Netlist
{
    public sealed class ComponentMappingResult
    {
        public Dictionary<string, string> StandardToStudent { get; } = new Dictionary<string, string>();
        public Dictionary<string, string> StudentToStandard { get; } = new Dictionary<string, string>();
        public List<string> MissingComponentMessages { get; } = new List<string>();
        public List<string> ExtraComponentMessages { get; } = new List<string>();
        public bool Ambiguous { get; set; }
    }

    /// <summary>
    /// 在标准网表与学生网表之间建立当前练习模式允许的元件实例映射。
    /// 以 DefinitionName 分组后枚举同类元件的候选对应，并用两侧已建立的电气连通关系为候选打分；
    /// 输出仅供网表层连接比对使用，不负责评分、UI 反馈，也不是可处理任意规模图同构的通用求解器。
    /// 映射不依赖显示名称或画布坐标；修改候选生成、枚举上限、排序或评分时，必须回归多同类元件、映射失败
    /// 与标准/学生连接比对的练习用例。
    /// </summary>
    public static class ComponentMappingSolver
    {
        private const int MaxPermutationCount = 720;

        /// <summary>
        /// 按定义分组组合候选映射，并在全部分组处理完成后依据双方连通关系选出当前实现的最高分候选。
        /// DefinitionName 的排序、候选组合和后续打分共同决定并列候选的稳定表现，不能为了表面简化而调整顺序。
        /// 无法建立完整且可靠映射时保守标记为 Ambiguous，由上层将其作为练习反馈的一部分处理。
        /// </summary>
        public static ComponentMappingResult Solve(PracticeNetlist standard, PracticeNetlist student)
        {
            var result = new ComponentMappingResult();
            if (standard == null || student == null)
            {
                return result;
            }

            var standardGroups = standard.Components.Values.GroupBy(c => c.DefinitionName).ToDictionary(g => g.Key, g => g.ToList());
            var studentGroups = student.Components.Values.GroupBy(c => c.DefinitionName).ToDictionary(g => g.Key, g => g.ToList());
            var allDefinitionNames = new HashSet<string>(standardGroups.Keys);
            allDefinitionNames.UnionWith(studentGroups.Keys);

            var candidates = new List<Dictionary<string, string>> { new Dictionary<string, string>() };

            foreach (var definitionName in allDefinitionNames.OrderBy(n => n))
            {
                standardGroups.TryGetValue(definitionName, out var standardComponents);
                studentGroups.TryGetValue(definitionName, out var studentComponents);
                standardComponents = standardComponents ?? new List<PracticeNetlistComponent>();
                studentComponents = studentComponents ?? new List<PracticeNetlistComponent>();

                if (studentComponents.Count < standardComponents.Count)
                {
                    result.MissingComponentMessages.Add("\u7f3a\u5c11 " + (standardComponents.Count - studentComponents.Count) + " \u4e2a " + GetDefinitionLabel(standardComponents, studentComponents, definitionName) + "\u3002");
                }
                else if (studentComponents.Count > standardComponents.Count)
                {
                    result.ExtraComponentMessages.Add("\u591a\u4f59 " + (studentComponents.Count - standardComponents.Count) + " \u4e2a " + GetDefinitionLabel(standardComponents, studentComponents, definitionName) + "\u3002");
                }

                if (standardComponents.Count == 0 || studentComponents.Count == 0)
                {
                    continue;
                }

                var mappingOptions = BuildMappingOptions(standardComponents, studentComponents);
                var nextCandidates = new List<Dictionary<string, string>>();
                foreach (var candidate in candidates)
                {
                    foreach (var option in mappingOptions)
                    {
                        var copy = new Dictionary<string, string>(candidate);
                        foreach (var kvp in option)
                        {
                            copy[kvp.Key] = kvp.Value;
                        }

                        nextCandidates.Add(copy);
                    }
                }

                candidates = nextCandidates.Count > 0 ? nextCandidates : candidates;
            }

            var bestScore = int.MinValue;
            var bestCandidates = new List<Dictionary<string, string>>();
            foreach (var candidate in candidates)
            {
                var score = ScoreMapping(candidate, standard, student);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidates.Clear();
                    bestCandidates.Add(candidate);
                }
                else if (score == bestScore)
                {
                    bestCandidates.Add(candidate);
                }
            }

            var best = bestCandidates.Count > 0 ? bestCandidates[0] : new Dictionary<string, string>();
            foreach (var kvp in best)
            {
                result.StandardToStudent[kvp.Key] = kvp.Value;
                result.StudentToStandard[kvp.Value] = kvp.Key;
            }

            var hasReliableMapping = result.StandardToStudent.Count == standard.Components.Count && bestScore >= 0;
            result.Ambiguous = bestCandidates.Count > 1 && !hasReliableMapping;
            return result;
        }

        /// <summary>
        /// 为同一 DefinitionName 的实例生成映射候选。排列数量超过安全上限时，当前实现退回到输入顺序中的首组映射，
        /// 以避免练习提交在大量同类元件时无界扩张；这不是全局最优匹配保证。
        /// </summary>
        private static List<Dictionary<string, string>> BuildMappingOptions(List<PracticeNetlistComponent> standardComponents, List<PracticeNetlistComponent> studentComponents)
        {
            var standardIds = standardComponents.Select(c => c.ComponentId).ToList();
            var studentIds = studentComponents.Select(c => c.ComponentId).ToList();
            var length = System.Math.Min(standardIds.Count, studentIds.Count);
            var permutations = GetPermutations(studentIds, length);
            if (permutations.Count > MaxPermutationCount)
            {
                permutations = new List<List<string>> { studentIds.Take(length).ToList() };
            }

            var options = new List<Dictionary<string, string>>();
            foreach (var permutation in permutations)
            {
                var option = new Dictionary<string, string>();
                for (var i = 0; i < length; i++)
                {
                    option[standardIds[i]] = permutation[i];
                }

                options.Add(option);
            }

            return options;
        }

        private static int ScoreMapping(Dictionary<string, string> standardToStudent, PracticeNetlist standard, PracticeNetlist student)
        {
            var score = standardToStudent.Count * 1000;
            foreach (var connection in standard.DirectConnections)
            {
                if (!TryMapTerminal(connection.StartKey, standardToStudent, out var mappedStart) ||
                    !TryMapTerminal(connection.EndKey, standardToStudent, out var mappedEnd))
                {
                    score -= 100;
                    continue;
                }

                score += student.AreConnected(mappedStart, mappedEnd) ? 20 : -50;
            }

            var studentToStandard = standardToStudent.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
            foreach (var connection in student.DirectConnections)
            {
                if (!TryMapTerminal(connection.StartKey, studentToStandard, out var mappedStart) ||
                    !TryMapTerminal(connection.EndKey, studentToStandard, out var mappedEnd))
                {
                    score -= 20;
                    continue;
                }

                score += standard.AreConnected(mappedStart, mappedEnd) ? 8 : -40;
            }

            return score;
        }

        public static bool TryMapTerminal(string sourceTerminalKey, Dictionary<string, string> componentMap, out string mappedTerminalKey)
        {
            mappedTerminalKey = null;
            SplitTerminalKey(sourceTerminalKey, out var componentId, out var terminalId);
            if (string.IsNullOrWhiteSpace(componentId) || !componentMap.TryGetValue(componentId, out var mappedComponentId))
            {
                return false;
            }

            mappedTerminalKey = PracticeNetlistTerminal.MakeKey(mappedComponentId, terminalId);
            return true;
        }

        public static void SplitTerminalKey(string terminalKey, out string componentId, out string terminalId)
        {
            componentId = string.Empty;
            terminalId = string.Empty;
            if (string.IsNullOrWhiteSpace(terminalKey))
            {
                return;
            }

            var index = terminalKey.IndexOf('.');
            if (index < 0)
            {
                componentId = terminalKey;
                return;
            }

            componentId = terminalKey.Substring(0, index);
            terminalId = terminalKey.Substring(index + 1);
        }

        private static List<List<string>> GetPermutations(List<string> source, int length)
        {
            if (length <= 0)
            {
                return new List<List<string>> { new List<string>() };
            }

            var result = new List<List<string>>();
            BuildPermutations(source, length, new List<string>(), result);
            return result;
        }

        // 递归回溯按 source 当前顺序生成候选；顺序会影响超限降级和同分候选的首项，不可随意改写。
        private static void BuildPermutations(List<string> source, int length, List<string> current, List<List<string>> result)
        {
            if (current.Count == length)
            {
                result.Add(new List<string>(current));
                return;
            }

            foreach (var item in source)
            {
                if (current.Contains(item))
                {
                    continue;
                }

                current.Add(item);
                BuildPermutations(source, length, current, result);
                current.RemoveAt(current.Count - 1);
            }
        }

        private static string GetDefinitionLabel(List<PracticeNetlistComponent> standardComponents, List<PracticeNetlistComponent> studentComponents, string fallback)
        {
            var component = studentComponents.FirstOrDefault() ?? standardComponents.FirstOrDefault();
            return component != null ? component.DisplayName : fallback;
        }
    }
}

