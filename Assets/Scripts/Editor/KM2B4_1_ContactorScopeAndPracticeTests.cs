using System.Collections.Generic;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Practice.Netlist;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B4.1 专项测试：验证 Contactor 作用域约束和 Practice 路径 NO pair 置换闭环。
    /// T11: 非 Contactor 同 terminalId 不置换（拓扑级）。
    /// T12: Practice 完整 NO pair swap → Passed=true。
    /// T13: Practice 半 pair → Passed=false。
    /// T14: Practice 两 role 共用同一 NO pair → Passed=false。
    /// T15: Practice NO→NC → Passed=false。
    /// T16: Practice 跨 KM role 替代 → Passed=false。
    /// </summary>
    public static class KM2B4_1_ContactorScopeAndPracticeTests
    {
        private const string KmDef = "Contactor_KM_380V";
        private const string StartDef = "Button_Start_NO";
        private const string LampDef = "Lamp_220V";

        [MenuItem("Tools/Tests/Run KM2B4.1 Contactor Scope and Practice Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            T11_NonContactorSameTerminalIdsMustNotPermute(failures);

            EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity", OpenSceneMode.Single);
            var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
            var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
            if (workspace == null || saveLoad == null)
            {
                failures.Add("Setup: Missing WorkspaceController or SaveLoadService — cannot run Practice tests");
                Report(failures);
                return;
            }

            T12_PracticeFullNoPairSwap(failures, workspace, saveLoad);
            T13_PracticeHalfPair(failures, workspace, saveLoad);
            T14_PracticeBothRolesSamePair(failures, workspace, saveLoad);
            T15_PracticeNoToNc(failures, workspace, saveLoad);
            T16_PracticeCrossKmRoleSubstitution(failures, workspace, saveLoad);

            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();

            Report(failures);
        }

        private static void Report(List<string> failures)
        {
            if (failures.Count == 0)
            {
                Debug.Log("KM2B4_1_ContactorScopeAndPracticeTests 通过 (6/6)");
            }
            else
            {
                foreach (var f in failures) Debug.LogError("[FAIL] " + f);
                Debug.LogError($"KM2B4_1_ContactorScopeAndPracticeTests 失败 ({failures.Count} failures)");
            }
        }

        // === T11: 非 Contactor 同 terminalId → NoMatch ===
        // template/candidate 使用同一非 Contactor definition（Lamp_220V），Kind=Lamp，
        // 使负例真正针对"类型作用域"而非 DefinitionName 差异。
        private static void T11_NonContactorSameTerminalIdsMustNotPermute(List<string> failures)
        {
            const string scenario = "T11-NonContactorSameTerminalIds";
            var template = BuildNonContactorGraph("Lamp_220V", "13", "14", "33", "34");
            var candidate = BuildNonContactorGraph("Lamp_220V", "33", "34", "13", "14");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T12: Practice 完整 NO pair swap → Passed=true ===
        // Standard: km_1 NO1(13/14)→start_1, NO2(33/34)→lamp_1
        // Student:  km_1 NO2(33/34)→start_1, NO1(13/14)→lamp_1
        private static void T12_PracticeFullNoPairSwap(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T12-PracticeFullNoPairSwap";
            var standard = BuildNoPairSwapTemplate(
                km13Target: "start_1", km13Terminal: "23",
                km14Target: "start_1", km14Terminal: "24",
                km33Target: "lamp_1", km33Terminal: "L",
                km34Target: "lamp_1", km34Terminal: "N");
            var student = BuildNoPairSwapTemplate(
                km13Target: "lamp_1", km13Terminal: "L",
                km14Target: "lamp_1", km14Terminal: "N",
                km33Target: "start_1", km33Terminal: "23",
                km34Target: "start_1", km34Terminal: "24");

            var passed = RunPracticeCheck(workspace, saveLoad, student, standard, scenario, failures);
            if (!passed)
                failures.Add($"{scenario}: 期望 Passed=true, 实际 false");
        }

        // === T13: Practice 半 pair (33/14) → Passed=false ===
        // Standard: km_1 13/14→start, 33/34→lamp
        // Student:  km_1 33/14→start, 13/34→lamp (半 pair：33 from NO2, 14 from NO1)
        private static void T13_PracticeHalfPair(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T13-PracticeHalfPair";
            var standard = BuildNoPairSwapTemplate(
                km13Target: "start_1", km13Terminal: "23",
                km14Target: "start_1", km14Terminal: "24",
                km33Target: "lamp_1", km33Terminal: "L",
                km34Target: "lamp_1", km34Terminal: "N");
            var student = new CircuitTemplateDto
            {
                templateId = "b4_1_half_pair_test",
                templateName = "B4.1 Half Pair Test",
                category = "Industrial",
                difficulty = "test"
            };
            student.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 200, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "lamp_1", definitionName = LampDef, x = 400, y = 0 });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "33", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = "lamp_1", endTerminalId = "L", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "34", endComponentId = "lamp_1", endTerminalId = "N", color = "#14A640", style = "Orthogonal" });

            var passed = RunPracticeCheck(workspace, saveLoad, student, standard, scenario, failures);
            if (passed)
                failures.Add($"{scenario}: 期望 Passed=false, 实际 true (半 pair 不应通过)");
        }

        // === T14: Practice 两 role 共用同一 NO pair → Passed=false ===
        // Standard: km_1 13/14→start, 33/34→lamp
        // Student:  km_1 13/14→start, 13/14→lamp (两 role 都挂到 NO1)
        private static void T14_PracticeBothRolesSamePair(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T14-PracticeBothRolesSamePair";
            var standard = BuildNoPairSwapTemplate(
                km13Target: "start_1", km13Terminal: "23",
                km14Target: "start_1", km14Terminal: "24",
                km33Target: "lamp_1", km33Terminal: "L",
                km34Target: "lamp_1", km34Terminal: "N");
            var student = new CircuitTemplateDto
            {
                templateId = "b4_1_same_pair_test",
                templateName = "B4.1 Same Pair Test",
                category = "Industrial",
                difficulty = "test"
            };
            student.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 200, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "lamp_1", definitionName = LampDef, x = 400, y = 0 });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = "lamp_1", endTerminalId = "L", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = "lamp_1", endTerminalId = "N", color = "#14A640", style = "Orthogonal" });

            var passed = RunPracticeCheck(workspace, saveLoad, student, standard, scenario, failures);
            if (passed)
                failures.Add($"{scenario}: 期望 Passed=false, 实际 true (两 role 共用同一 NO pair 不应通过)");
        }

        // === T15: Practice NO→NC → Passed=false ===
        // Standard: km_1 13/14→start (NO pair)
        // Student:  km_1 21/22→start (NC pair, NO→NC 替换)
        private static void T15_PracticeNoToNc(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T15-PracticeNoToNc";
            var standard = new CircuitTemplateDto
            {
                templateId = "b4_1_no_to_nc_test",
                templateName = "B4.1 NO to NC Test",
                category = "Industrial",
                difficulty = "test"
            };
            standard.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            standard.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 200, y = 0 });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });

            var student = new CircuitTemplateDto
            {
                templateId = "b4_1_no_to_nc_student",
                templateName = "B4.1 NO to NC Student",
                category = "Industrial",
                difficulty = "test"
            };
            student.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 200, y = 0 });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "21", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "22", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });

            var passed = RunPracticeCheck(workspace, saveLoad, student, standard, scenario, failures);
            if (passed)
                failures.Add($"{scenario}: 期望 Passed=false, 实际 true (NO→NC 不应通过)");
        }

        // === T16: Practice 跨 KM role 替代 → Passed=false ===
        // Standard: KM1 13/14→start, KM2 33/34→lamp
        // Student:  KM1 33/34→lamp (替代 KM2 的 role), KM2 13/14→start (替代 KM1 的 role)
        private static void T16_PracticeCrossKmRoleSubstitution(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T16-PracticeCrossKmRoleSubstitution";
            var standard = new CircuitTemplateDto
            {
                templateId = "b4_1_cross_km_test",
                templateName = "B4.1 Cross KM Role Test",
                category = "Industrial",
                difficulty = "test"
            };
            standard.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            standard.components.Add(new TemplateComponentDto { instanceId = "km_2", definitionName = KmDef, x = 200, y = 0 });
            standard.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 400, y = 0 });
            standard.components.Add(new TemplateComponentDto { instanceId = "lamp_1", definitionName = LampDef, x = 600, y = 0 });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_2", startTerminalId = "33", endComponentId = "lamp_1", endTerminalId = "L", color = "#F21F1F", style = "Orthogonal" });
            standard.wires.Add(new TemplateWireDto { startComponentId = "km_2", startTerminalId = "34", endComponentId = "lamp_1", endTerminalId = "N", color = "#14A640", style = "Orthogonal" });

            var student = new CircuitTemplateDto
            {
                templateId = "b4_1_cross_km_student",
                templateName = "B4.1 Cross KM Role Student",
                category = "Industrial",
                difficulty = "test"
            };
            student.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "km_2", definitionName = KmDef, x = 200, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 400, y = 0 });
            student.components.Add(new TemplateComponentDto { instanceId = "lamp_1", definitionName = LampDef, x = 600, y = 0 });
            // KM1 takes KM2's role (lamp), KM2 takes KM1's role (start)
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "33", endComponentId = "lamp_1", endTerminalId = "L", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "34", endComponentId = "lamp_1", endTerminalId = "N", color = "#14A640", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_2", startTerminalId = "13", endComponentId = "start_1", endTerminalId = "23", color = "#F21F1F", style = "Orthogonal" });
            student.wires.Add(new TemplateWireDto { startComponentId = "km_2", startTerminalId = "14", endComponentId = "start_1", endTerminalId = "24", color = "#14A640", style = "Orthogonal" });

            var passed = RunPracticeCheck(workspace, saveLoad, student, standard, scenario, failures);
            if (passed)
                failures.Add($"{scenario}: 期望 Passed=false, 实际 true (跨 KM role 替代不应通过)");
        }

        // === 判定函数（拓扑级，用于 T11）===
        private static string Judge(CircuitTopologyGraph candidate, CircuitTopologyGraph template)
        {
            if (CircuitTopologyMatcher.IsExactMatch(candidate, template, out _))
                return "ExactMatch";
            if (ContactorNoPairPermutation.IsNoPairEquivalentMatch(candidate, template, out _))
                return "EquivalentMatch";
            return "NoMatch";
        }

        // === Practice 检查执行 ===
        private static bool RunPracticeCheck(
            WorkspaceController workspace,
            SaveLoadService saveLoad,
            CircuitTemplateDto student,
            CircuitTemplateDto standard,
            string scenario,
            List<string> failures)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            if (!CircuitTemplateSpawnService.Spawn(student, workspace, saveLoad.Catalog, out var spawnMessage))
            {
                failures.Add($"{scenario}: Failed to spawn student template: {spawnMessage}");
                return false;
            }

            TemplateEditSession.Clear();
            var result = PracticeConnectionChecker.Check(workspace, standard);
            Debug.Log($"[{scenario}] PracticePassed={result.Passed}");
            if (!result.Passed)
            {
                var issues = new List<string>();
                foreach (var issue in result.MissingConnections) issues.Add("Missing:" + issue.Message);
                foreach (var issue in result.WrongConnections) issues.Add("Wrong:" + issue.Message);
                foreach (var issue in result.ExtraConnections) issues.Add("Extra:" + issue.Message);
                Debug.Log($"[{scenario}] Issues: {string.Join("; ", issues)}");
            }

            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            return result.Passed;
        }

        // === Graph 构造辅助 ===
        // 非 Contactor graph：DefinitionName 和 Kind 都标记为 Lamp，使用 NO pair 端子编号
        private static CircuitTopologyGraph BuildNonContactorGraph(
            string defName,
            string t1, string t2,
            string t3, string t4)
        {
            var graph = new CircuitTopologyGraph();
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = defName, Kind = ComponentKind.Lamp });
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef, Kind = ComponentKind.PushButton });
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = LampDef, Kind = ComponentKind.Lamp });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = t1, NodeB = 1, TerminalB = "23" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = t2, NodeB = 1, TerminalB = "24" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = t3, NodeB = 2, TerminalB = "L" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = t4, NodeB = 2, TerminalB = "N" });
            return graph;
        }

        // Practice 测试模板：km_1 + start_1 + lamp_1，4 根导线由参数指定
        private static CircuitTemplateDto BuildNoPairSwapTemplate(
            string km13Target, string km13Terminal,
            string km14Target, string km14Terminal,
            string km33Target, string km33Terminal,
            string km34Target, string km34Terminal)
        {
            var dto = new CircuitTemplateDto
            {
                templateId = "b4_1_no_pair_swap_test",
                templateName = "B4.1 NO Pair Swap Test",
                category = "Industrial",
                difficulty = "test"
            };

            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 200, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "lamp_1", definitionName = LampDef, x = 400, y = 0 });

            dto.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "13", endComponentId = km13Target, endTerminalId = km13Terminal, color = "#F21F1F", style = "Orthogonal" });
            dto.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "14", endComponentId = km14Target, endTerminalId = km14Terminal, color = "#14A640", style = "Orthogonal" });
            dto.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "33", endComponentId = km33Target, endTerminalId = km33Terminal, color = "#F21F1F", style = "Orthogonal" });
            dto.wires.Add(new TemplateWireDto { startComponentId = "km_1", startTerminalId = "34", endComponentId = km34Target, endTerminalId = km34Terminal, color = "#14A640", style = "Orthogonal" });

            return dto;
        }
    }
}
