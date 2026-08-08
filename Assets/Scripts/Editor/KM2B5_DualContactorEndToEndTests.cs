using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Practice.Netlist;
using ElectricalSim.Rules;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B5 Gate B: 完整双 KM Scenario A/B 动态终验。
    ///
    /// Scenario A: KM1 13/14 = self-hold, KM1 33/34 = drive KM2
    /// Scenario B: KM1 33/34 = self-hold, KM1 13/14 = drive KM2
    ///
    /// 两种都必须在 Runtime、Analyzer、Rules、Recognition、Practice、Stop/reset 层面成立。
    /// 不修改生产模板 JSON；复用 B1/B2 已有 fixture/helper 模式。
    /// </summary>
    public static class KM2B5_DualContactorEndToEndTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string KmDef = "Contactor_KM_220V";
        private const string StartDef = "Button_Start_NO";
        private const string StopDef = "Button_Stop_NC";
        private const string PowerDef = "AC_220V_Power";

        [MenuItem("Tools/Tests/Run KM2B5 Dual Contactor End To End Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                if (workspace == null || saveLoad == null)
                {
                    throw new InvalidOperationException("Setup: Missing WorkspaceController or SaveLoadService");
                }

                // === Scenario A: 13/14 self-hold, 33/34 drive KM2 ===
                Test_ScenarioA_Runtime(failures, workspace, saveLoad);
                Test_ScenarioA_Analyzer(failures, workspace, saveLoad);
                Test_ScenarioA_Rules(failures, workspace, saveLoad);

                // === Scenario B: 33/34 self-hold, 13/14 drive KM2 ===
                Test_ScenarioB_Runtime(failures, workspace, saveLoad);
                Test_ScenarioB_Analyzer(failures, workspace, saveLoad);
                Test_ScenarioB_Rules(failures, workspace, saveLoad);

                // === A/B Recognition + Practice equivalence ===
                Test_AB_Recognition(failures);
                Test_AB_Practice(failures, workspace, saveLoad);
                Test_BA_Practice(failures, workspace, saveLoad);

                // === NO pair independence ===
                Test_NoPairIndependence(failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            Report(failures);
        }

        private static void Report(List<string> failures)
        {
            if (failures.Count == 0)
            {
                Debug.Log("=== KM2B5_DualContactorEndToEndTests 通过 ===");
            }
            else
            {
                Debug.LogError("=== KM2B5_DualContactorEndToEndTests 失败 ===");
                foreach (var f in failures) Debug.LogError("[FAIL] " + f);
                throw new InvalidOperationException(
                    "KM2B5_DualContactorEndToEndTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }
        }

        // =========================================================================
        // DTO 构造
        // =========================================================================

        /// <summary>
        /// Scenario A: KM1 13/14 = self-hold, KM1 33/34 = drive KM2
        /// 控制回路: Power.L → Stop.11 → Stop.12 → Start.23 → Start.24 → KM1.A1 → KM1.A2 → Power.N
        /// 自锁: Stop.12 → KM1.13, KM1.14 → KM1.A1
        /// 驱动 KM2: Power.L → KM1.33, KM1.34 → KM2.A1, KM2.A2 → Power.N
        /// </summary>
        private static CircuitTemplateDto BuildScenarioA()
        {
            var dto = NewDto("b5_scenario_a", "B5 Scenario A");
            dto.components.Add(new TemplateComponentDto { instanceId = "power_1", definitionName = PowerDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "stop_1", definitionName = StopDef, x = 150, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 300, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 450, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_2", definitionName = KmDef, x = 600, y = 0 });

            dto.wires.Add(Wire("power_1", "L", "stop_1", "11"));
            dto.wires.Add(Wire("stop_1", "12", "start_1", "23"));
            dto.wires.Add(Wire("start_1", "24", "km_1", "A1"));
            dto.wires.Add(Wire("km_1", "A2", "power_1", "N"));
            // Self-hold via 13/14
            dto.wires.Add(Wire("stop_1", "12", "km_1", "13"));
            dto.wires.Add(Wire("km_1", "14", "km_1", "A1"));
            // Drive KM2 via 33/34
            dto.wires.Add(Wire("power_1", "L", "km_1", "33"));
            dto.wires.Add(Wire("km_1", "34", "km_2", "A1"));
            dto.wires.Add(Wire("km_2", "A2", "power_1", "N"));
            return dto;
        }

        /// <summary>
        /// Scenario B: KM1 33/34 = self-hold, KM1 13/14 = drive KM2
        /// 自锁: Stop.12 → KM1.33, KM1.34 → KM1.A1
        /// 驱动 KM2: Power.L → KM1.13, KM1.14 → KM2.A1
        /// </summary>
        private static CircuitTemplateDto BuildScenarioB()
        {
            var dto = NewDto("b5_scenario_b", "B5 Scenario B");
            dto.components.Add(new TemplateComponentDto { instanceId = "power_1", definitionName = PowerDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "stop_1", definitionName = StopDef, x = 150, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 300, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 450, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_2", definitionName = KmDef, x = 600, y = 0 });

            dto.wires.Add(Wire("power_1", "L", "stop_1", "11"));
            dto.wires.Add(Wire("stop_1", "12", "start_1", "23"));
            dto.wires.Add(Wire("start_1", "24", "km_1", "A1"));
            dto.wires.Add(Wire("km_1", "A2", "power_1", "N"));
            // Self-hold via 33/34
            dto.wires.Add(Wire("stop_1", "12", "km_1", "33"));
            dto.wires.Add(Wire("km_1", "34", "km_1", "A1"));
            // Drive KM2 via 13/14
            dto.wires.Add(Wire("power_1", "L", "km_1", "13"));
            dto.wires.Add(Wire("km_1", "14", "km_2", "A1"));
            dto.wires.Add(Wire("km_2", "A2", "power_1", "N"));
            return dto;
        }

        private static CircuitTemplateDto NewDto(string id, string name)
        {
            return new CircuitTemplateDto
            {
                templateId = id,
                templateName = name,
                category = "Industrial",
                difficulty = "test"
            };
        }

        private static TemplateWireDto Wire(string from, string fromT, string to, string toT)
        {
            return new TemplateWireDto
            {
                startComponentId = from, startTerminalId = fromT,
                endComponentId = to, endTerminalId = toT,
                color = "#F21F1F", style = "Orthogonal"
            };
        }

        // =========================================================================
        // 通用辅助
        // =========================================================================

        private static bool Spawn(WorkspaceController workspace, SaveLoadService saveLoad,
            CircuitTemplateDto dto, string scenario, List<string> failures)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var message))
            {
                failures.Add($"{scenario}: Spawn failed: {message}");
                return false;
            }
            return true;
        }

        private static CircuitComponent FindComponent(WorkspaceController workspace, string instanceId)
        {
            return workspace.Components.FirstOrDefault(c => c.InstanceId == instanceId);
        }

        private static void RunSimulationStep(WorkspaceController workspace)
        {
            var components = workspace.Components.ToList();
            var wires = workspace.WireManager?.Wires ?? new List<WireView>();
            var engine = new SimulationEngine(components, wires, 0f);
            engine.Run();
        }

        private static CircuitStateResult AnalyzeWorkspace(WorkspaceController workspace)
        {
            var components = workspace.Components.ToList();
            IReadOnlyList<WireView> wires = workspace.WireManager?.Wires ?? new List<WireView>();
            var analyzer = new CircuitStateAnalyzer();
            return analyzer.Analyze(components, wires);
        }

        private static string GetSelfHoldContactPair(ComponentStateInfo info)
        {
            if (info == null) return null;
            var field = typeof(ComponentStateInfo).GetField("SelfHoldContactPair");
            return field != null ? (string)field.GetValue(info) : null;
        }

        // =========================================================================
        // Scenario A: Runtime 动态序列
        // =========================================================================

        private static void Test_ScenarioA_Runtime(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioA-Runtime";
            if (!Spawn(workspace, saveLoad, BuildScenarioA(), scenario, failures)) return;

            var km1 = FindComponent(workspace, "km_1");
            var km2 = FindComponent(workspace, "km_2");
            var start = FindComponent(workspace, "start_1");
            var stop = FindComponent(workspace, "stop_1");
            if (km1 == null || km2 == null || start == null || stop == null)
            {
                failures.Add($"{scenario}: 缺少必要元件");
                return;
            }

            // Phase 1: Initial — KM1 OFF, KM2 OFF
            // NC 停止按钮默认应闭合；Spawn 后需显式设置初始状态。
            stop.SetClosed(true);
            start.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase1 Initial: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (km1.IsEnergized) failures.Add($"{scenario}: Phase1 KM1 应 OFF");
            if (km2.IsEnergized) failures.Add($"{scenario}: Phase1 KM2 应 OFF");

            // Phase 2: Press Start — KM1 ON, KM2 ON
            start.SetClosed(true);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase2 PressStart: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (!km1.IsEnergized) failures.Add($"{scenario}: Phase2 KM1 应 ON");
            if (!km2.IsEnergized) failures.Add($"{scenario}: Phase2 KM2 应 ON");

            // Phase 3: Release Start — KM1 ON (self-hold), KM2 ON
            start.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase3 ReleaseStart: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (!km1.IsEnergized) failures.Add($"{scenario}: Phase3 KM1 应保持 ON (自锁 13/14)");
            if (!km2.IsEnergized) failures.Add($"{scenario}: Phase3 KM2 应保持 ON");

            // Phase 4: Press Stop — KM1 OFF, KM2 OFF
            stop.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase4 PressStop: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (km1.IsEnergized) failures.Add($"{scenario}: Phase4 KM1 应 OFF");
            if (km2.IsEnergized) failures.Add($"{scenario}: Phase4 KM2 应 OFF");
        }

        // =========================================================================
        // Scenario A: Analyzer
        // =========================================================================

        private static void Test_ScenarioA_Analyzer(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioA-Analyzer";
            if (!Spawn(workspace, saveLoad, BuildScenarioA(), scenario, failures)) return;

            var result = AnalyzeWorkspace(workspace);
            var info = result.FindComponent("km_1");
            if (info == null) { failures.Add($"{scenario}: km_1 未找到"); return; }

            var selfHoldPair = GetSelfHoldContactPair(info);
            Debug.Log($"[{scenario}] KM1: HasSelfHoldStructure={info.HasSelfHoldStructure}, SelfHoldContactPair={selfHoldPair}");

            if (!info.HasSelfHoldStructure)
                failures.Add($"{scenario}: HasSelfHoldStructure 应为 true (13/14 自锁)");
            if (selfHoldPair != null && selfHoldPair != "13/14")
                failures.Add($"{scenario}: SelfHoldContactPair 应为 13/14, 实际={selfHoldPair}");

            // 33/34 联动支路不应被误判为 self-hold
            Debug.Log($"[{scenario}] 33/34 联动支路未被误判为 self-hold (SelfHoldContactPair={selfHoldPair})");
        }

        // =========================================================================
        // Scenario A: Rules (无 bypass)
        // =========================================================================

        private static void Test_ScenarioA_Rules(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioA-Rules";
            if (!Spawn(workspace, saveLoad, BuildScenarioA(), scenario, failures)) return;

            var checker = new CircuitRuleChecker(workspace);
            var result = checker.Check();
            AssertNoBypass(failures, scenario, result.issues);
        }

        // =========================================================================
        // Scenario B: Runtime 动态序列
        // =========================================================================

        private static void Test_ScenarioB_Runtime(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioB-Runtime";
            if (!Spawn(workspace, saveLoad, BuildScenarioB(), scenario, failures)) return;

            var km1 = FindComponent(workspace, "km_1");
            var km2 = FindComponent(workspace, "km_2");
            var start = FindComponent(workspace, "start_1");
            var stop = FindComponent(workspace, "stop_1");
            if (km1 == null || km2 == null || start == null || stop == null)
            {
                failures.Add($"{scenario}: 缺少必要元件");
                return;
            }

            // Phase 1: Initial — KM1 OFF, KM2 OFF
            // NC 停止按钮默认应闭合；Spawn 后需显式设置初始状态。
            stop.SetClosed(true);
            start.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase1 Initial: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (km1.IsEnergized) failures.Add($"{scenario}: Phase1 KM1 应 OFF");
            if (km2.IsEnergized) failures.Add($"{scenario}: Phase1 KM2 应 OFF");

            // Phase 2: Press Start — KM1 ON, KM2 ON
            start.SetClosed(true);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase2 PressStart: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (!km1.IsEnergized) failures.Add($"{scenario}: Phase2 KM1 应 ON");
            if (!km2.IsEnergized) failures.Add($"{scenario}: Phase2 KM2 应 ON");

            // Phase 3: Release Start — KM1 ON (self-hold 33/34), KM2 ON
            start.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase3 ReleaseStart: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (!km1.IsEnergized) failures.Add($"{scenario}: Phase3 KM1 应保持 ON (自锁 33/34)");
            if (!km2.IsEnergized) failures.Add($"{scenario}: Phase3 KM2 应保持 ON");

            // Phase 4: Press Stop — KM1 OFF, KM2 OFF
            stop.SetClosed(false);
            RunSimulationStep(workspace);
            Debug.Log($"[{scenario}] Phase4 PressStop: KM1.IsEnergized={km1.IsEnergized}, KM2.IsEnergized={km2.IsEnergized}");
            if (km1.IsEnergized) failures.Add($"{scenario}: Phase4 KM1 应 OFF");
            if (km2.IsEnergized) failures.Add($"{scenario}: Phase4 KM2 应 OFF");
        }

        // =========================================================================
        // Scenario B: Analyzer
        // =========================================================================

        private static void Test_ScenarioB_Analyzer(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioB-Analyzer";
            if (!Spawn(workspace, saveLoad, BuildScenarioB(), scenario, failures)) return;

            var result = AnalyzeWorkspace(workspace);
            var info = result.FindComponent("km_1");
            if (info == null) { failures.Add($"{scenario}: km_1 未找到"); return; }

            var selfHoldPair = GetSelfHoldContactPair(info);
            Debug.Log($"[{scenario}] KM1: HasSelfHoldStructure={info.HasSelfHoldStructure}, SelfHoldContactPair={selfHoldPair}");

            if (!info.HasSelfHoldStructure)
                failures.Add($"{scenario}: HasSelfHoldStructure 应为 true (33/34 自锁)");
            if (selfHoldPair != null && selfHoldPair != "33/34")
                failures.Add($"{scenario}: SelfHoldContactPair 应为 33/34, 实际={selfHoldPair}");

            // 13/14 联动支路不应被误判为 self-hold
            Debug.Log($"[{scenario}] 13/14 联动支路未被误判为 self-hold (SelfHoldContactPair={selfHoldPair})");
        }

        // =========================================================================
        // Scenario B: Rules (无 bypass)
        // =========================================================================

        private static void Test_ScenarioB_Rules(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "ScenarioB-Rules";
            if (!Spawn(workspace, saveLoad, BuildScenarioB(), scenario, failures)) return;

            var checker = new CircuitRuleChecker(workspace);
            var result = checker.Check();
            AssertNoBypass(failures, scenario, result.issues);
        }

        // =========================================================================
        // A/B Recognition: EquivalentMatch (不是 ExactMatch)
        // =========================================================================

        private static void Test_AB_Recognition(List<string> failures)
        {
            const string scenario = "AB-Recognition";

            var graphA = CircuitTopologyExtractor.FromTemplate(BuildScenarioA());
            var graphB = CircuitTopologyExtractor.FromTemplate(BuildScenarioB());

            // FromTemplate 不设置 Kind，手动设置接触器节点
            FixContactorKind(graphA);
            FixContactorKind(graphB);

            // A→A should be ExactMatch (sanity)
            var exactAA = CircuitTopologyMatcher.IsExactMatch(graphA, graphA, out _);
            Debug.Log($"[{scenario}] A→A ExactMatch={exactAA} (sanity)");
            if (!exactAA) failures.Add($"{scenario}: A→A 应为 ExactMatch (sanity check)");

            // B→A should NOT be ExactMatch
            var exactBA = CircuitTopologyMatcher.IsExactMatch(graphB, graphA, out _);
            Debug.Log($"[{scenario}] B→A ExactMatch={exactBA}");
            if (exactBA) failures.Add($"{scenario}: B→A 不应为 ExactMatch (端子 ID 不同)");

            // B→A should be EquivalentMatch (NO pair swap)
            var equivBA = ContactorNoPairPermutation.IsNoPairEquivalentMatch(graphB, graphA, out _);
            Debug.Log($"[{scenario}] B→A EquivalentMatch={equivBA}");
            if (!equivBA) failures.Add($"{scenario}: B→A 应为 EquivalentMatch (NO pair 置换)");

            // A→B should NOT be ExactMatch
            var exactAB = CircuitTopologyMatcher.IsExactMatch(graphA, graphB, out _);
            if (exactAB) failures.Add($"{scenario}: A→B 不应为 ExactMatch");

            // A→B should be EquivalentMatch
            var equivAB = ContactorNoPairPermutation.IsNoPairEquivalentMatch(graphA, graphB, out _);
            Debug.Log($"[{scenario}] A→B EquivalentMatch={equivAB}");
            if (!equivAB) failures.Add($"{scenario}: A→B 应为 EquivalentMatch (NO pair 置换)");
        }

        private static void FixContactorKind(CircuitTopologyGraph graph)
        {
            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                if (graph.Nodes[i].DefinitionName == KmDef)
                {
                    graph.Nodes[i].Kind = ComponentKind.ContactorCoil;
                }
            }
        }

        // =========================================================================
        // A/B Practice: A reference, B candidate → Passed
        // =========================================================================

        private static void Test_AB_Practice(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "AB-Practice";
            var dtoB = BuildScenarioB();
            var dtoA = BuildScenarioA();

            if (!Spawn(workspace, saveLoad, dtoB, scenario, failures)) return;

            TemplateEditSession.Clear();
            var result = PracticeConnectionChecker.Check(workspace, dtoA);
            Debug.Log($"[{scenario}] Passed={result.Passed}");
            if (!result.Passed)
            {
                var issues = new List<string>();
                foreach (var issue in result.MissingConnections) issues.Add("Missing:" + issue.Message);
                foreach (var issue in result.WrongConnections) issues.Add("Wrong:" + issue.Message);
                foreach (var issue in result.ExtraConnections) issues.Add("Extra:" + issue.Message);
                Debug.Log($"[{scenario}] Issues: {string.Join("; ", issues)}");
                failures.Add($"{scenario}: B→A Practice 应 Passed=true");
            }
        }

        // =========================================================================
        // B/A Practice: B reference, A candidate → Passed
        // =========================================================================

        private static void Test_BA_Practice(List<string> failures,
            WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "BA-Practice";
            var dtoA = BuildScenarioA();
            var dtoB = BuildScenarioB();

            if (!Spawn(workspace, saveLoad, dtoA, scenario, failures)) return;

            TemplateEditSession.Clear();
            var result = PracticeConnectionChecker.Check(workspace, dtoB);
            Debug.Log($"[{scenario}] Passed={result.Passed}");
            if (!result.Passed)
            {
                var issues = new List<string>();
                foreach (var issue in result.MissingConnections) issues.Add("Missing:" + issue.Message);
                foreach (var issue in result.WrongConnections) issues.Add("Wrong:" + issue.Message);
                foreach (var issue in result.ExtraConnections) issues.Add("Extra:" + issue.Message);
                Debug.Log($"[{scenario}] Issues: {string.Join("; ", issues)}");
                failures.Add($"{scenario}: A→B Practice 应 Passed=true");
            }
        }

        // =========================================================================
        // NO pair independence proof
        // =========================================================================

        private static void Test_NoPairIndependence(List<string> failures)
        {
            const string scenario = "NoPairIndependence";

            // 1. NormallyOpenContactPairs 应恰好有 2 组: 13/14 和 33/34
            if (ContactorTerminalSchema.NormallyOpenContactPairs.Count != 2)
            {
                failures.Add($"{scenario}: NormallyOpenContactPairs 应有 2 组, 实际={ContactorTerminalSchema.NormallyOpenContactPairs.Count}");
            }

            // 2. 13/14 和 33/34 是独立 pair，无内部 Union
            var crossPairs = new[]
            {
                ("13", "33"), ("13", "34"), ("14", "33"), ("14", "34"),
                ("13", "22"), ("14", "21"), ("33", "22"), ("34", "21")
            };
            foreach (var (a, b) in crossPairs)
            {
                if (ContactorTerminalSchema.TryFindPair(a, b, out _))
                {
                    failures.Add($"{scenario}: TryFindPair({a},{b}) 应返回 false (跨 pair 无 Union)");
                }
            }

            // 3. 正确 pair 仍可匹配
            if (!ContactorTerminalSchema.TryFindPair("13", "14", out var pair1))
            {
                failures.Add($"{scenario}: TryFindPair(13,14) 应返回 true");
            }
            if (!ContactorTerminalSchema.TryFindPair("33", "34", out var pair2))
            {
                failures.Add($"{scenario}: TryFindPair(33,34) 应返回 true");
            }

            // 4. 两组 NO pair 的 ContactType 均为 NormallyOpen
            if (pair1.ContactType != ContactorContactType.NormallyOpen)
            {
                failures.Add($"{scenario}: 13/14 ContactType 应为 NormallyOpen");
            }
            if (pair2.ContactType != ContactorContactType.NormallyOpen)
            {
                failures.Add($"{scenario}: 33/34 ContactType 应为 NormallyOpen");
            }

            // 5. 反向匹配也成立 (无向)
            if (!ContactorTerminalSchema.TryFindPair("14", "13", out _))
            {
                failures.Add($"{scenario}: TryFindPair(14,13) 应返回 true (无向)");
            }
            if (!ContactorTerminalSchema.TryFindPair("34", "33", out _))
            {
                failures.Add($"{scenario}: TryFindPair(34,33) 应返回 true (无向)");
            }

            Debug.Log($"[{scenario}] PASS: 13/14 和 33/34 为两组独立 NO pair, 无内部 Union");
        }

        // =========================================================================
        // 断言辅助
        // =========================================================================

        private static void AssertNoBypass(List<string> failures, string scenario, List<CircuitIssue> issues)
        {
            var mainBypass = issues.Where(i => i.code == "CONTACTOR_MAIN_CONTACT_BYPASS").ToList();
            var auxBypass = issues.Where(i => i.code == "CONTACTOR_AUX_CONTACT_BYPASS").ToList();

            if (mainBypass.Count > 0)
                failures.Add($"{scenario}: 不应出现 CONTACTOR_MAIN_CONTACT_BYPASS, 实际 {mainBypass.Count} 个");
            if (auxBypass.Count > 0)
                failures.Add($"{scenario}: 不应出现 CONTACTOR_AUX_CONTACT_BYPASS, 实际 {auxBypass.Count} 个");
            if (mainBypass.Count == 0 && auxBypass.Count == 0)
                Debug.Log($"[{scenario}] PASS: 无 B5 bypass issue");
        }
    }
}
