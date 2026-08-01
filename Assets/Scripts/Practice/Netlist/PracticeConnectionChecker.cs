using System.Collections.Generic;
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

            AddMissingConnections(result, standard, student, mapping.StandardToStudent);
            AddWrongDirectConnections(result, standard, student, mapping.StudentToStandard);
            AddExtraNodeMerges(result, standard, student, mapping.StudentToStandard);
            AddSuggestions(result, standard);

            result.Passed = !result.HasIssues;
            return result;
        }

        // 标准侧期望连通在映射后以学生侧等价节点判断；无映射端子仍按标准侧描述缺失项，避免丢失关键反馈。
        private static void AddMissingConnections(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, Dictionary<string, string> standardToStudent)
        {
            var reported = new HashSet<string>();
            foreach (var connection in standard.DirectConnections)
            {
                if (!ComponentMappingSolver.TryMapTerminal(connection.StartKey, standardToStudent, out var mappedStart) ||
                    !ComponentMappingSolver.TryMapTerminal(connection.EndKey, standardToStudent, out var mappedEnd))
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

                if (!AreConnectedWithLampSwap(student, mappedStart, mappedEnd))
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
        private static void AddWrongDirectConnections(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, Dictionary<string, string> studentToStandard)
        {
            var reported = new HashSet<string>();
            foreach (var connection in student.DirectConnections)
            {
                if (!ComponentMappingSolver.TryMapTerminal(connection.StartKey, studentToStandard, out var mappedStart) ||
                    !ComponentMappingSolver.TryMapTerminal(connection.EndKey, studentToStandard, out var mappedEnd))
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

                if (!AreConnectedWithLampSwap(standard, mappedStart, mappedEnd))
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
        private static void AddExtraNodeMerges(PracticeConnectionCheckResult result, PracticeNetlist standard, PracticeNetlist student, Dictionary<string, string> studentToStandard)
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
                        if (!ComponentMappingSolver.TryMapTerminal(studentA, studentToStandard, out var standardA) ||
                            !ComponentMappingSolver.TryMapTerminal(studentB, studentToStandard, out var standardB))
                        {
                            continue;
                        }

                        if (AreConnectedWithLampSwap(standard, standardA, standardB))
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

        // E4：普通交流灯泡无极性端子等价。仅对 DefinitionName 包含 "Lamp" 的元件的 L/N 端子允许互换，
        // 不影响二极管、直流器件、风扇或其他有极性器件。不建立通用端子等价框架，不全局忽略端子 ID。

        private static bool IsNonPolarizedLamp(PracticeNetlist netlist, string componentId)
        {
            if (string.IsNullOrEmpty(componentId) || !netlist.Components.TryGetValue(componentId, out var component))
            {
                return false;
            }

            var name = component.DefinitionName;
            return name != null && name.Contains("Lamp");
        }

        private static string TryGetLampSwappedTerminalKey(PracticeNetlist netlist, string terminalKey)
        {
            ComponentMappingSolver.SplitTerminalKey(terminalKey, out var componentId, out var terminalId);
            if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(terminalId))
            {
                return null;
            }

            if (!IsNonPolarizedLamp(netlist, componentId))
            {
                return null;
            }

            string swappedId = null;
            if (terminalId == "L") swappedId = "N";
            else if (terminalId == "N") swappedId = "L";
            if (swappedId == null)
            {
                return null;
            }

            return PracticeNetlistTerminal.MakeKey(componentId, swappedId);
        }

        /// <summary>
        /// 检查两端子是否连通，对普通交流灯泡的 L/N 端子允许互换。
        /// 先按原始端子键检查连通；若失败且端子属于灯泡，尝试交换灯泡端子后再次检查。
        /// </summary>
        private static bool AreConnectedWithLampSwap(PracticeNetlist netlist, string firstKey, string secondKey)
        {
            if (netlist.AreConnected(firstKey, secondKey))
            {
                return true;
            }

            var swappedFirst = TryGetLampSwappedTerminalKey(netlist, firstKey);
            if (swappedFirst != null && netlist.AreConnected(swappedFirst, secondKey))
            {
                return true;
            }

            var swappedSecond = TryGetLampSwappedTerminalKey(netlist, secondKey);
            if (swappedSecond != null && netlist.AreConnected(firstKey, swappedSecond))
            {
                return true;
            }

            if (swappedFirst != null && swappedSecond != null && netlist.AreConnected(swappedFirst, swappedSecond))
            {
                return true;
            }

            return false;
        }
    }
}
