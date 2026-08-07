using System.Collections.Generic;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Templates;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 练习网表算法层的连接比对入口：建立标准和学生网表、求解元件映射，再输出缺失、接错与多余连通等结构化结果。
    /// 它不同于外层 Practice/PracticeConnectionChecker，后者负责练习会话、提交动作和 UI 流程；本类不修改画布、
    /// 不负责评分或反馈排版。修改直接连接、等价节点或结果去重方式后，必须回归正确、缺失、多余、无法映射和等价连接用例。
    /// </summary>
    public static class PracticeConnectionChecker
    {
        /// <summary>
        /// 依次构建两侧网表、建立元件映射，并按缺失连接、学生直接接错、学生额外电气合并的顺序收集结果。
        /// 映射成功不等于连接正确，三类比对必须保留，避免只看直接导线而遗漏跨多根导线形成的额外连通。
        /// </summary>
        public static PracticeConnectionCheckResult Check(WorkspaceController workspace, CircuitTemplateDto template)
        {
            var result = new PracticeConnectionCheckResult();
            if (workspace == null || template == null)
            {
                result.Passed = false;
                result.MissingConnections.Add(new PracticeConnectionIssue(PracticeConnectionIssueKind.MissingConnection, "\u5f53\u524d\u6ca1\u6709\u53ef\u7528\u4e8e\u5224\u5b9a\u7684\u7ec3\u4e60\u6a21\u677f\u3002"));
                return result;
            }

            var standard = StandardNetlistBuilder.Build(template);
            var student = StudentNetlistBuilder.Build(workspace);
            var mapping = ComponentMappingSolver.Solve(standard, student);

            foreach (var message in mapping.MissingComponentMessages)
            {
                result.MissingComponents.Add(new PracticeConnectionIssue(PracticeConnectionIssueKind.MissingComponent, message));
            }

            foreach (var message in mapping.ExtraComponentMessages)
            {
                result.ExtraComponents.Add(new PracticeConnectionIssue(PracticeConnectionIssueKind.ExtraComponent, message));
            }

            if (mapping.Ambiguous)
            {
                result.AmbiguousMapping = true;
            }

            // 灯泡端子无极性只影响已确认的普通灯泡实例。先为每个已映射灯泡选定一次 L/N 对应关系，
            // 后续缺失、接错和额外电气节点检查均复用该对应关系，不能按单条导线临时改选方向。
            var terminalMap = CreateStableTerminalMap(workspace, standard, student, mapping);

            AddMissingConnections(result, standard, student, terminalMap);
            AddWrongDirectConnections(result, standard, student, terminalMap);
            AddExtraNodeMerges(result, standard, student, terminalMap);
            AddSuggestions(result, standard);

            result.Passed = !result.HasIssues;
            return result;
        }

        // 标准侧期望连通在映射后以学生侧等价节点判断；无映射端子仍按标准侧描述缺失项，避免丢失关键反馈。
        private static void AddMissingConnections(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, StableTerminalMap terminalMap)
        {
            var reported = new HashSet<string>();
            foreach (var connection in standard.DirectConnections)
            {
                if (!terminalMap.TryMapStandardToStudent(connection.StartKey, out var mappedStart) ||
                    !terminalMap.TryMapStandardToStudent(connection.EndKey, out var mappedEnd))
                {
                    var key = connection.GetUndirectedKey();
                    if (reported.Add(key))
                    {
                        result.MissingConnections.Add(new PracticeConnectionIssue(
                            PracticeConnectionIssueKind.MissingConnection,
                            "\u7f3a\u5c11\u8fde\u63a5\uff1a" + standard.DescribeTerminal(connection.StartKey) + " \u5e94\u8fde\u901a\u5230 " + standard.DescribeTerminal(connection.EndKey) + "\u3002"));
                    }

                    continue;
                }

                if (!student.AreConnected(mappedStart, mappedEnd))
                {
                    var key = mappedStart + "<->" + mappedEnd;
                    if (reported.Add(key))
                    {
                        result.MissingConnections.Add(new PracticeConnectionIssue(
                            PracticeConnectionIssueKind.MissingConnection,
                            "\u7f3a\u5c11\u8fde\u63a5\uff1a" + student.DescribeTerminal(mappedStart) + " \u5e94\u8fde\u901a\u5230 " + student.DescribeTerminal(mappedEnd) + "\u3002"));
                    }
                }
            }
        }

        // 学生直接导线映射回标准侧后再判定，防止仅凭实例 ID 差异把正确同类元件接线误报为接错。
        private static void AddWrongDirectConnections(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, StableTerminalMap terminalMap)
        {
            var reported = new HashSet<string>();
            foreach (var connection in student.DirectConnections)
            {
                if (!terminalMap.TryMapStudentToStandard(connection.StartKey, out var mappedStart) ||
                    !terminalMap.TryMapStudentToStandard(connection.EndKey, out var mappedEnd))
                {
                    var key = connection.GetUndirectedKey();
                    if (reported.Add(key))
                    {
                        result.WrongConnections.Add(new PracticeConnectionIssue(
                            PracticeConnectionIssueKind.WrongConnection,
                            "\u7aef\u5b50\u63a5\u9519\uff1a" + student.DescribeTerminal(connection.StartKey) + " \u4e0e " + student.DescribeTerminal(connection.EndKey) + " \u4e0d\u5c5e\u4e8e\u672c\u7ec3\u4e60\u9700\u8981\u7684\u6807\u51c6\u8fde\u63a5\u3002"));
                    }

                    continue;
                }

                if (!standard.AreConnected(mappedStart, mappedEnd))
                {
                    var key = connection.GetUndirectedKey();
                    if (reported.Add(key))
                    {
                        result.WrongConnections.Add(new PracticeConnectionIssue(
                            PracticeConnectionIssueKind.WrongConnection,
                            "\u7aef\u5b50\u63a5\u9519\uff1a" + student.DescribeTerminal(connection.StartKey) + " \u4e0d\u5e94\u8fde\u901a\u5230 " + student.DescribeTerminal(connection.EndKey) + "\u3002"));
                    }
                }
            }
        }

        // 并查集得到的是学生侧电气等价节点组；同组端子在标准侧不应连通时，才构成额外连接。
        private static void AddExtraNodeMerges(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, StableTerminalMap terminalMap)
        {
            var reported = new HashSet<string>();
            foreach (var group in student.GetEquivalentNodeGroups())
            {
                for (var i = 0; i < group.Count; i++)
                {
                    for (var j = i + 1; j < group.Count; j++)
                    {
                        var studentA = group[i];
                        var studentB = group[j];
                        if (!terminalMap.TryMapStudentToStandard(studentA, out var standardA) ||
                            !terminalMap.TryMapStudentToStandard(studentB, out var standardB))
                        {
                            continue;
                        }

                        if (standard.AreConnected(standardA, standardB))
                        {
                            continue;
                        }

                        var key = string.CompareOrdinal(studentA, studentB) <= 0 ? studentA + "<->" + studentB : studentB + "<->" + studentA;
                        if (reported.Add(key))
                        {
                            result.ExtraConnections.Add(new PracticeConnectionIssue(
                                PracticeConnectionIssueKind.ExtraConnection,
                                "\u591a\u4f59\u8fde\u63a5\uff1a" + student.DescribeTerminal(studentA) + " \u4e0e " + student.DescribeTerminal(studentB) + " \u88ab\u63a5\u5230\u4e86\u540c\u4e00\u7535\u6c14\u8282\u70b9\uff0c\u4f46\u6807\u51c6\u7b54\u6848\u4e2d\u5b83\u4eec\u4e0d\u5e94\u8fde\u901a\u3002"));
                        }
                    }
                }
            }
        }

        private static void AddSuggestions(PracticeConnectionCheckResult result, PracticeNetlist standard)
        {
            foreach (var connection in standard.DirectConnections)
            {
                result.CorrectConnectionSuggestions.Add(new PracticeConnectionIssue(
                    PracticeConnectionIssueKind.Suggestion,
                    standard.DescribeTerminal(connection.StartKey) + " -> " + standard.DescribeTerminal(connection.EndKey)));
            }
        }

        /// <summary>
        /// 从当前 Workspace 的实际 ComponentDefinition 读取 ComponentKind。练习网表只保留 definitionName，
        /// 因此不能通过名称包含 Lamp 推断语义；只有 ComponentKind.Lamp 的实例才可交换 L/N。
        /// </summary>
        private static StableTerminalMap CreateStableTerminalMap(
            WorkspaceController workspace,
            PracticeNetlist standard,
            PracticeNetlist student,
            ComponentMappingResult componentMap)
        {
            var lampStudentIds = new HashSet<string>();
            if (workspace != null)
            {
                foreach (var component in workspace.Components)
                {
                    if (component != null && component.Definition != null && component.Definition.kind == ComponentKind.Lamp)
                    {
                        lampStudentIds.Add(component.InstanceId);
                    }
                }
            }

            var swappedStandardLampIds = new HashSet<string>();
            foreach (var pair in componentMap.StandardToStudent)
            {
                if (!lampStudentIds.Contains(pair.Value) ||
                    !HasLampWorkingTerminals(standard, pair.Key) ||
                    !HasLampWorkingTerminals(student, pair.Value))
                {
                    continue;
                }

                // 对完整直接连接集合比较两种方向。得分相同时保持原方向，避免将混合错误放宽为正确接线。
                var normalScore = ScoreLampOrientation(standard, student, componentMap.StandardToStudent, pair.Key, false);
                var swappedScore = ScoreLampOrientation(standard, student, componentMap.StandardToStudent, pair.Key, true);
                if (swappedScore > normalScore)
                {
                    swappedStandardLampIds.Add(pair.Key);
                }
            }

            // 接触器常开辅助触点对置换：复用 ContactorNoPairPermutation 的同一套 schema 和排列语义，
            // 不在 Practice 层散写 13/14/33/34。仅对 ComponentKind.ContactorCoil 的实例尝试 NO pair remap，
            // 选择使标准侧直接连接匹配数最高的映射；identity 与 swap 同分时保持 identity，避免放宽错误接线。
            var contactorStudentIds = new HashSet<string>();
            if (workspace != null)
            {
                foreach (var component in workspace.Components)
                {
                    if (component != null && component.Definition != null && component.Definition.kind == ComponentKind.ContactorCoil)
                    {
                        contactorStudentIds.Add(component.InstanceId);
                    }
                }
            }

            var contactorRemaps = new Dictionary<string, ContactorTerminalRemap>();
            foreach (var pair in componentMap.StandardToStudent)
            {
                var standardId = pair.Key;
                var studentId = pair.Value;
                if (!contactorStudentIds.Contains(studentId)) continue;

                var usedTerminals = CollectUsedNoPairTerminals(student, studentId);
                if (usedTerminals.Count == 0) continue;

                var options = ContactorNoPairPermutation.GenerateSingleComponentNoPairMappings(usedTerminals);

                ContactorTerminalRemap bestRemap = null;
                var bestScore = int.MinValue;
                foreach (var option in options)
                {
                    var remap = new ContactorTerminalRemap(option);
                    var score = ScoreContactorOrientation(standard, student, componentMap.StandardToStudent, standardId, remap);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRemap = remap;
                    }
                }

                if (bestRemap != null && !bestRemap.IsIdentity)
                {
                    contactorRemaps[standardId] = bestRemap;
                }
            }

            return new StableTerminalMap(componentMap.StandardToStudent, componentMap.StudentToStandard, swappedStandardLampIds, contactorRemaps);
        }

        private static bool HasLampWorkingTerminals(PracticeNetlist netlist, string componentId)
        {
            return netlist != null &&
                netlist.Terminals.ContainsKey(PracticeNetlistTerminal.MakeKey(componentId, "L")) &&
                netlist.Terminals.ContainsKey(PracticeNetlistTerminal.MakeKey(componentId, "N"));
        }

        private static int ScoreLampOrientation(
            PracticeNetlist standard,
            PracticeNetlist student,
            Dictionary<string, string> standardToStudent,
            string standardLampId,
            bool swapLampTerminals)
        {
            var score = 0;
            foreach (var connection in standard.DirectConnections)
            {
                ComponentMappingSolver.SplitTerminalKey(connection.StartKey, out var startComponentId, out _);
                ComponentMappingSolver.SplitTerminalKey(connection.EndKey, out var endComponentId, out _);
                if (startComponentId != standardLampId && endComponentId != standardLampId)
                {
                    continue;
                }

                if (!TryMapStandardTerminalForScore(connection.StartKey, standardToStudent, standardLampId, swapLampTerminals, out var mappedStart) ||
                    !TryMapStandardTerminalForScore(connection.EndKey, standardToStudent, standardLampId, swapLampTerminals, out var mappedEnd))
                {
                    continue;
                }

                if (student.AreConnected(mappedStart, mappedEnd))
                {
                    score++;
                }
            }

            return score;
        }

        private static bool TryMapStandardTerminalForScore(
            string standardTerminalKey,
            Dictionary<string, string> standardToStudent,
            string selectedLampId,
            bool swapSelectedLamp,
            out string studentTerminalKey)
        {
            studentTerminalKey = null;
            ComponentMappingSolver.SplitTerminalKey(standardTerminalKey, out var componentId, out var terminalId);
            if (string.IsNullOrWhiteSpace(componentId) || !standardToStudent.TryGetValue(componentId, out var studentComponentId))
            {
                return false;
            }

            if (swapSelectedLamp && componentId == selectedLampId)
            {
                terminalId = SwapLampTerminalId(terminalId);
            }

            studentTerminalKey = PracticeNetlistTerminal.MakeKey(studentComponentId, terminalId);
            return true;
        }

        private static string SwapLampTerminalId(string terminalId)
        {
            if (terminalId == "L") return "N";
            if (terminalId == "N") return "L";
            return terminalId;
        }

        // 收集学生在某个接触器实例上实际使用的 NO pair 端子。仅依据 ContactorTerminalSchema.NormallyOpenContactPairs，
        // 不散写 13/14/33/34，用于向 ContactorNoPairPermutation 请求同一套置换映射。
        private static HashSet<string> CollectUsedNoPairTerminals(PracticeNetlist student, string studentComponentId)
        {
            var terminals = new HashSet<string>();
            var noPairs = ContactorTerminalSchema.NormallyOpenContactPairs;
            foreach (var connection in student.DirectConnections)
            {
                ComponentMappingSolver.SplitTerminalKey(connection.StartKey, out var startCompId, out var startTermId);
                ComponentMappingSolver.SplitTerminalKey(connection.EndKey, out var endCompId, out var endTermId);

                if (startCompId == studentComponentId)
                {
                    foreach (var pair in noPairs)
                    {
                        if (startTermId == pair.StartTerminalId || startTermId == pair.EndTerminalId)
                            terminals.Add(startTermId);
                    }
                }

                if (endCompId == studentComponentId)
                {
                    foreach (var pair in noPairs)
                    {
                        if (endTermId == pair.StartTerminalId || endTermId == pair.EndTerminalId)
                            terminals.Add(endTermId);
                    }
                }
            }

            return terminals;
        }

        // 对单个接触器尝试一种 NO pair remap，计标准侧直接连接在学生侧匹配的数量。得分越高表示该 remap 越接近正确接线。
        private static int ScoreContactorOrientation(
            PracticeNetlist standard,
            PracticeNetlist student,
            Dictionary<string, string> standardToStudentComponentMap,
            string standardContactorId,
            ContactorTerminalRemap remap)
        {
            var score = 0;
            foreach (var connection in standard.DirectConnections)
            {
                ComponentMappingSolver.SplitTerminalKey(connection.StartKey, out var startCompId, out _);
                ComponentMappingSolver.SplitTerminalKey(connection.EndKey, out var endCompId, out _);
                if (startCompId != standardContactorId && endCompId != standardContactorId) continue;

                if (!TryMapStandardTerminalForContactorScore(connection.StartKey, standardToStudentComponentMap, standardContactorId, remap, out var mappedStart) ||
                    !TryMapStandardTerminalForContactorScore(connection.EndKey, standardToStudentComponentMap, standardContactorId, remap, out var mappedEnd))
                    continue;

                if (student.AreConnected(mappedStart, mappedEnd))
                    score++;
            }

            return score;
        }

        private static bool TryMapStandardTerminalForContactorScore(
            string standardTerminalKey,
            Dictionary<string, string> standardToStudentComponentMap,
            string standardContactorId,
            ContactorTerminalRemap remap,
            out string studentTerminalKey)
        {
            studentTerminalKey = null;
            ComponentMappingSolver.SplitTerminalKey(standardTerminalKey, out var componentId, out var terminalId);
            if (string.IsNullOrWhiteSpace(componentId) || !standardToStudentComponentMap.TryGetValue(componentId, out var studentComponentId))
                return false;

            if (componentId == standardContactorId)
            {
                terminalId = remap.MapStandardToStudent(terminalId);
            }

            studentTerminalKey = PracticeNetlistTerminal.MakeKey(studentComponentId, terminalId);
            return true;
        }

        /// <summary>
        /// 单个接触器实例的 NO pair 端子双向映射。forward remap 来自
        /// <see cref="ContactorNoPairPermutation.GenerateSingleComponentNoPairMappings"/>（student→standard），
        /// inverse 自动反转为 standard→student。不散写 13/14/33/34。
        /// </summary>
        private sealed class ContactorTerminalRemap
        {
            private readonly Dictionary<string, string> studentToStandard;
            private readonly Dictionary<string, string> standardToStudent;

            public ContactorTerminalRemap(Dictionary<string, string> forwardRemap)
            {
                studentToStandard = forwardRemap;
                standardToStudent = new Dictionary<string, string>();
                foreach (var kvp in forwardRemap)
                {
                    standardToStudent[kvp.Value] = kvp.Key;
                }
            }

            public string MapStudentToStandard(string studentTerminal)
            {
                return studentToStandard.TryGetValue(studentTerminal, out var standardTerminal) ? standardTerminal : studentTerminal;
            }

            public string MapStandardToStudent(string standardTerminal)
            {
                return standardToStudent.TryGetValue(standardTerminal, out var studentTerminal) ? studentTerminal : standardTerminal;
            }

            public bool IsIdentity
            {
                get
                {
                    foreach (var kvp in studentToStandard)
                    {
                        if (kvp.Key != kvp.Value) return false;
                    }

                    return true;
                }
            }
        }

        /// <summary>
        /// 保存一次 Check 调用内确定的灯泡端子映射和接触器 NO pair 端子映射。它不改变组件映射规则，
        /// 也不把 L/N 或 NO pair 合并为同一端子，仅确保缺失、接错和额外节点检查都使用同一个端子方向。
        /// </summary>
        private sealed class StableTerminalMap
        {
            private readonly Dictionary<string, string> standardToStudent;
            private readonly Dictionary<string, string> studentToStandard;
            private readonly HashSet<string> swappedStandardLampIds;
            private readonly Dictionary<string, ContactorTerminalRemap> contactorRemaps;

            public StableTerminalMap(
                Dictionary<string, string> standardToStudent,
                Dictionary<string, string> studentToStandard,
                HashSet<string> swappedStandardLampIds,
                Dictionary<string, ContactorTerminalRemap> contactorRemaps)
            {
                this.standardToStudent = standardToStudent;
                this.studentToStandard = studentToStandard;
                this.swappedStandardLampIds = swappedStandardLampIds;
                this.contactorRemaps = contactorRemaps;
            }

            public bool TryMapStandardToStudent(string standardTerminalKey, out string studentTerminalKey)
            {
                studentTerminalKey = null;
                ComponentMappingSolver.SplitTerminalKey(standardTerminalKey, out var standardComponentId, out var terminalId);
                if (string.IsNullOrWhiteSpace(standardComponentId) || !standardToStudent.TryGetValue(standardComponentId, out var studentComponentId))
                {
                    return false;
                }

                if (contactorRemaps.TryGetValue(standardComponentId, out var remap))
                {
                    terminalId = remap.MapStandardToStudent(terminalId);
                }

                if (swappedStandardLampIds.Contains(standardComponentId))
                {
                    terminalId = SwapLampTerminalId(terminalId);
                }

                studentTerminalKey = PracticeNetlistTerminal.MakeKey(studentComponentId, terminalId);
                return true;
            }

            public bool TryMapStudentToStandard(string studentTerminalKey, out string standardTerminalKey)
            {
                standardTerminalKey = null;
                ComponentMappingSolver.SplitTerminalKey(studentTerminalKey, out var studentComponentId, out var terminalId);
                if (string.IsNullOrWhiteSpace(studentComponentId) || !studentToStandard.TryGetValue(studentComponentId, out var standardComponentId))
                {
                    return false;
                }

                if (contactorRemaps.TryGetValue(standardComponentId, out var remap))
                {
                    terminalId = remap.MapStudentToStandard(terminalId);
                }

                if (swappedStandardLampIds.Contains(standardComponentId))
                {
                    terminalId = SwapLampTerminalId(terminalId);
                }

                standardTerminalKey = PracticeNetlistTerminal.MakeKey(standardComponentId, terminalId);
                return true;
            }
        }
    }
}
