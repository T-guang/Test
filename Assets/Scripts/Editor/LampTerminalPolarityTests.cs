using System;
using System.Collections.Generic;
using System.Linq;
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
    /// E4：普通交流灯泡无极性接线正式测试。验证灯泡两个工作端子（L/N）无论哪个接火线、
    /// 哪个接零线，只要两端分别落在有效火线节点和零线节点上就应正常点亮，
    /// 不产生"火线和零线接反"错误，并能通过模板识别和练习检查。
    ///
    /// 测试覆盖：
    /// A. 正向四种 L/N 组合均点亮
    /// B. 交换灯泡端子四种 L/N 组合均点亮
    /// C. 非法或不完整接线不亮
    /// D. 三张家庭模板原接线通过 + 交换灯泡端子等价识别 + 练习检查通过
    ///
    /// 使用生产元件定义和生产运行链（SimulationEngine + CircuitStateAnalyzer + PracticeConnectionChecker），
    /// 不重新实现电压传播。所有断言失败时抛 InvalidOperationException，batchmode 非零退出。
    /// </summary>
    public static class LampTerminalPolarityTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string LockedEntryMessage = "画布已锁定";

        [MenuItem("Tools/Tests/Run Lamp Terminal Polarity Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                if (workspace == null)
                    throw new InvalidOperationException("未找到 WorkspaceController，无法执行 E4 灯泡极性测试。");

                var powerDef = LoadDefinition("AC_220V_Power");
                var switchDef = LoadDefinition("Single_Control_Switch");
                var lampDef = LoadDefinition("Lamp_220V");
                if (powerDef == null) throw new InvalidOperationException("未找到 AC_220V_Power 定义。");
                if (switchDef == null) throw new InvalidOperationException("未找到 Single_Control_Switch 定义。");
                if (lampDef == null) throw new InvalidOperationException("未找到 Lamp_220V 定义。");

                // A. 正向四种 L/N 组合
                TestA_NormalDirection(workspace, powerDef, switchDef, lampDef, failures, "L", "N");
                TestA_NormalDirection(workspace, powerDef, switchDef, lampDef, failures, "L2", "N");
                TestA_NormalDirection(workspace, powerDef, switchDef, lampDef, failures, "L", "N2");
                TestA_NormalDirection(workspace, powerDef, switchDef, lampDef, failures, "L2", "N2");

                // B. 交换灯泡端子四种 L/N 组合
                TestB_SwappedTerminals(workspace, powerDef, switchDef, lampDef, failures, "L", "N");
                TestB_SwappedTerminals(workspace, powerDef, switchDef, lampDef, failures, "L2", "N");
                TestB_SwappedTerminals(workspace, powerDef, switchDef, lampDef, failures, "L", "N2");
                TestB_SwappedTerminals(workspace, powerDef, switchDef, lampDef, failures, "L2", "N2");

                // C. 非法或不完整接线
                TestC_IllegalWiring(workspace, powerDef, switchDef, lampDef, failures);

                // D. 模板与练习
                TestD_TemplateAndPractice(workspace, failures);

                // E. 练习检查中的灯泡端子映射必须为每个灯泡保持一致。
                TestE_StableLampTerminalMapping(workspace, lampDef, failures);
            }
            catch (Exception exception)
            {
                failures.Add("测试执行出现未处理异常：" + exception);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("E4 灯泡无极性接线测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Lamp][E4] 普通交流灯泡无极性接线：通过");
        }

        // A: 正向 — 火线接 Lamp.L，零线接 Lamp.N
        private static void TestA_NormalDirection(WorkspaceController workspace, ComponentDefinition powerDef, ComponentDefinition switchDef, ComponentDefinition lampDef, List<string> failures, string powerPhaseTerminal, string powerNeutralTerminal)
        {
            var label = "A(" + powerPhaseTerminal + "/" + powerNeutralTerminal + ")";
            ResetWorkspace(workspace);
            var power = workspace.SpawnComponent(powerDef, new Vector2(-360, 120), "power_1", false);
            var sw = workspace.SpawnComponent(switchDef, new Vector2(-48, 120), "switch_1", false);
            var lamp = workspace.SpawnComponent(lampDef, new Vector2(288, 120), "lamp_1", false);
            if (power == null || sw == null || lamp == null) { failures.Add(label + ": 无法创建测试元件。"); return; }

            power.SetClosed(true);
            sw.SetClosed(true);

            CreateWire(workspace, power, powerPhaseTerminal, sw, "L");
            CreateWire(workspace, sw, "L1", lamp, "L");
            CreateWire(workspace, power, powerNeutralTerminal, lamp, "N");

            RunSimulation(workspace);
            if (!lamp.IsEnergized) failures.Add(label + ": 灯泡应点亮（正向接线）。");

            var analysis = new CircuitStateAnalyzer().Analyze(workspace.Components, workspace.WireManager.Wires);
            var lampInfo = analysis.FindComponent("lamp_1");
            if (lampInfo != null && lampInfo.State != "On") failures.Add(label + ": 分析器应报告灯泡 On，实际=" + lampInfo.State);
            AssertNoReverseWarning(analysis, label, failures);
        }

        // B: 交换灯泡端子 — 火线接 Lamp.N，零线接 Lamp.L
        private static void TestB_SwappedTerminals(WorkspaceController workspace, ComponentDefinition powerDef, ComponentDefinition switchDef, ComponentDefinition lampDef, List<string> failures, string powerPhaseTerminal, string powerNeutralTerminal)
        {
            var label = "B(" + powerPhaseTerminal + "/" + powerNeutralTerminal + ")";
            ResetWorkspace(workspace);
            var power = workspace.SpawnComponent(powerDef, new Vector2(-360, 120), "power_1", false);
            var sw = workspace.SpawnComponent(switchDef, new Vector2(-48, 120), "switch_1", false);
            var lamp = workspace.SpawnComponent(lampDef, new Vector2(288, 120), "lamp_1", false);
            if (power == null || sw == null || lamp == null) { failures.Add(label + ": 无法创建测试元件。"); return; }

            power.SetClosed(true);
            sw.SetClosed(true);

            // 交换灯泡端子：火线接到 Lamp.N，零线接到 Lamp.L
            CreateWire(workspace, power, powerPhaseTerminal, sw, "L");
            CreateWire(workspace, sw, "L1", lamp, "N");
            CreateWire(workspace, power, powerNeutralTerminal, lamp, "L");

            RunSimulation(workspace);
            if (!lamp.IsEnergized) failures.Add(label + ": 灯泡应点亮（交换端子）。");

            var analysis = new CircuitStateAnalyzer().Analyze(workspace.Components, workspace.WireManager.Wires);
            var lampInfo = analysis.FindComponent("lamp_1");
            if (lampInfo != null && lampInfo.State != "On") failures.Add(label + ": 分析器应报告灯泡 On，实际=" + lampInfo.State);
            AssertNoReverseWarning(analysis, label, failures);
        }

        // C: 非法或不完整接线
        private static void TestC_IllegalWiring(WorkspaceController workspace, ComponentDefinition powerDef, ComponentDefinition switchDef, ComponentDefinition lampDef, List<string> failures)
        {
            // C1: 仅 Lamp.L 接火线（N 悬空）
            ResetWorkspace(workspace);
            var power1 = workspace.SpawnComponent(powerDef, Vector2.zero, "power_1", false);
            var lamp1 = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            power1.SetClosed(true);
            CreateWire(workspace, power1, "L", lamp1, "L");
            RunSimulation(workspace);
            if (lamp1.IsEnergized) failures.Add("C1: 仅 Lamp.L 接火线，灯泡不应点亮。");

            // C2: 仅 Lamp.N 接火线（L 悬空）
            ResetWorkspace(workspace);
            var power2 = workspace.SpawnComponent(powerDef, Vector2.zero, "power_1", false);
            var lamp2 = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            power2.SetClosed(true);
            CreateWire(workspace, power2, "L", lamp2, "N");
            RunSimulation(workspace);
            if (lamp2.IsEnergized) failures.Add("C2: 仅 Lamp.N 接火线，灯泡不应点亮。");

            // C3: 两端都接同一零线节点
            ResetWorkspace(workspace);
            var power3 = workspace.SpawnComponent(powerDef, Vector2.zero, "power_1", false);
            var lamp3 = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            power3.SetClosed(true);
            CreateWire(workspace, power3, "N", lamp3, "L");
            CreateWire(workspace, power3, "N", lamp3, "N");
            RunSimulation(workspace);
            if (lamp3.IsEnergized) failures.Add("C3: 两端都接零线，灯泡不应点亮。");

            // C4: 两端都接同一火线节点
            ResetWorkspace(workspace);
            var power4 = workspace.SpawnComponent(powerDef, Vector2.zero, "power_1", false);
            var lamp4 = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            power4.SetClosed(true);
            CreateWire(workspace, power4, "L", lamp4, "L");
            CreateWire(workspace, power4, "L", lamp4, "N");
            RunSimulation(workspace);
            if (lamp4.IsEnergized) failures.Add("C4: 两端都接火线，灯泡不应点亮。");

            // C5: Lamp.L 和 Lamp.N 通过 Wire 直接短接（不接电源）
            // 注意：CanCreateWire 不允许同一器件内部端子跳线，通过第二只灯泡的 L 端子作跳线点。
            ResetWorkspace(workspace);
            var lamp5a = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            var lamp5b = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_2", false);
            CreateWire(workspace, lamp5a, "L", lamp5b, "L");
            CreateWire(workspace, lamp5a, "N", lamp5b, "L"); // lamp_1 的 L/N 通过 lamp_2.L 短接
            RunSimulation(workspace);
            if (lamp5a.IsEnergized) failures.Add("C5: 灯泡两端直接短接无电源，不应点亮。");

            // C6: 火线与零线直接短接 — 检查短路规则仍然发现
            // 注意：CanCreateWire 不允许同一器件内部端子跳线，所以通过灯泡 L 端子短路 L/N。
            ResetWorkspace(workspace);
            var power6 = workspace.SpawnComponent(powerDef, Vector2.zero, "power_1", false);
            var lamp6 = workspace.SpawnComponent(lampDef, Vector2.zero, "lamp_1", false);
            power6.SetClosed(true);
            CreateWire(workspace, power6, "L", lamp6, "L");
            CreateWire(workspace, power6, "N", lamp6, "L"); // 火线和零线同时接到 lamp.L → 短路
            RunSimulation(workspace);
            var analysis6 = new CircuitStateAnalyzer().Analyze(workspace.Components, workspace.WireManager.Wires);
            if (!analysis6.HasShortCircuit) failures.Add("C6: 火线零线短接应被短路规则发现。");
            if (lamp6.IsEnergized) failures.Add("C6: 火零短路时灯泡不应保持 energized。");

            // C7: 开关断开导致回路不完整
            ResetWorkspace(workspace);
            var power7 = workspace.SpawnComponent(powerDef, new Vector2(-360, 120), "power_1", false);
            var sw7 = workspace.SpawnComponent(switchDef, new Vector2(-48, 120), "switch_1", false);
            var lamp7 = workspace.SpawnComponent(lampDef, new Vector2(288, 120), "lamp_1", false);
            power7.SetClosed(true);
            sw7.SetClosed(false); // 开关断开
            CreateWire(workspace, power7, "L", sw7, "L");
            CreateWire(workspace, sw7, "L1", lamp7, "L");
            CreateWire(workspace, power7, "N", lamp7, "N");
            RunSimulation(workspace);
            if (lamp7.IsEnergized) failures.Add("C7: 开关断开时灯泡不应点亮。");
        }

        // D: 模板与练习
        private static void TestD_TemplateAndPractice(WorkspaceController workspace, List<string> failures)
        {
            var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
            if (saveLoad == null) { failures.Add("D: 未找到 SaveLoadService。"); return; }

            var catalogAsset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            if (catalogAsset == null) { failures.Add("D: 未找到模板 catalog。"); return; }
            var catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(catalogAsset.text);
            if (catalog == null) { failures.Add("D: catalog 解析失败。"); return; }

            var templateIds = new[]
            {
                "single_lamp_template",
                "double_control_lamp_template",
                "breaker_lamp_template",
                "meter_lamp_template",
                "lamp_fan_parallel_template",
                "double_lamp_single_switch",
                "two_switch_two_lamp",
                "breaker_lamp_fan_parallel"
            };
            foreach (var templateId in templateIds)
            {
                var item = catalog.templates.FirstOrDefault(c => c.templateId == templateId);
                if (item == null) { failures.Add("D: catalog 缺少 " + templateId); continue; }

                CircuitTemplateDto template;
                string loadError;
                if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out template, out loadError))
                {
                    failures.Add("D: 加载模板 " + templateId + " 失败：" + loadError);
                    continue;
                }

                // D1: 原模板接线通过
                ResetWorkspace(workspace);
                var spawned = CircuitTemplateSpawnService.Spawn(template, workspace, saveLoad.Catalog, out var spawnMsg);
                if (!spawned) { failures.Add("D1(" + templateId + "): 生成失败：" + spawnMsg); continue; }
                CloseAllSwitches(workspace);
                RunSimulation(workspace);
                var lamp = workspace.Components.FirstOrDefault(c => c.Definition != null && c.Definition.kind == ComponentKind.Lamp);
                if (lamp == null) { failures.Add("D1(" + templateId + "): 未找到灯泡元件。"); continue; }
                if (!lamp.IsEnergized) failures.Add("D1(" + templateId + "): 原接线灯泡应点亮。");
                var practiceOriginal = PracticeConnectionChecker.Check(workspace, template);
                if (!practiceOriginal.Passed) failures.Add("D1(" + templateId + "): 原接线练习检查应通过。");
                var originalRecognition = new CircuitTopologyRecognitionService().Recognize(workspace);
                if (originalRecognition.Status != CircuitRecognitionStatus.ExactMatch || originalRecognition.MatchedTemplateId != templateId)
                    failures.Add("D1(" + templateId + "): 原模板应精确识别自身，实际=" + originalRecognition.Status + "/" + originalRecognition.MatchedTemplateId);
                var originalAnalysis = new CircuitStateAnalyzer().Analyze(workspace.Components, workspace.WireManager.Wires);
                if (originalAnalysis == null) failures.Add("D1(" + templateId + "): CircuitStateAnalyzer 未返回结果。");

                // D2: 交换灯泡端子的等价图
                var swappedTemplate = CloneTemplateWithSwappedLampTerminals(template);
                if (swappedTemplate == null) { failures.Add("D2(" + templateId + "): 无法构造交换端子模板。"); continue; }

                ResetWorkspace(workspace);
                spawned = CircuitTemplateSpawnService.Spawn(swappedTemplate, workspace, saveLoad.Catalog, out spawnMsg);
                if (!spawned) { failures.Add("D2(" + templateId + "): 交换端子生成失败：" + spawnMsg); continue; }
                CloseAllSwitches(workspace);
                RunSimulation(workspace);
                lamp = workspace.Components.FirstOrDefault(c => c.Definition != null && c.Definition.kind == ComponentKind.Lamp);
                if (lamp == null) { failures.Add("D2(" + templateId + "): 交换端子后未找到灯泡。"); continue; }
                if (!lamp.IsEnergized) failures.Add("D2(" + templateId + "): 交换端子后灯泡应点亮。");

                // D3: 自由接线识别 — 用原始模板做等价识别
                var recognition = new CircuitTopologyRecognitionService().Recognize(workspace);
                if (recognition.Status != CircuitRecognitionStatus.EquivalentMatch)
                    failures.Add("D3(" + templateId + "): 交换端子后应等价识别，实际=" + recognition.Status);

                // D4: PracticeConnectionChecker — 交换端子后应通过
                var practiceSwapped = PracticeConnectionChecker.Check(workspace, template);
                if (!practiceSwapped.Passed)
                    failures.Add("D4(" + templateId + "): 交换端子练习检查应通过，issues=" + practiceSwapped.WrongConnections.Count + "+" + practiceSwapped.MissingConnections.Count + "+" + practiceSwapped.ExtraConnections.Count);
            }
        }

        // E: 稳定灯泡端子映射。测试比对仍走正式 PracticeConnectionChecker，不复制网表或端子比对算法。
        private static void TestE_StableLampTerminalMapping(WorkspaceController workspace, ComponentDefinition lampDefinition, List<string> failures)
        {
            TestSingleLampStableMapping(workspace, lampDefinition, false, failures);
            TestSingleLampStableMapping(workspace, lampDefinition, true, failures);
            TestTwoLampStableMapping(workspace, lampDefinition, false, failures);
            TestTwoLampStableMapping(workspace, lampDefinition, true, failures);
            TestNonLampNameDoesNotGainSwapPrivilege(workspace, failures);
        }

        private static void TestSingleLampStableMapping(WorkspaceController workspace, ComponentDefinition lampDefinition, bool mixedError, List<string> failures)
        {
            ResetWorkspace(workspace);
            var nodeA = CreateTestNodeDefinition("E4_NodeA");
            var nodeB = CreateTestNodeDefinition("E4_NodeB");
            var nodeC = CreateTestNodeDefinition("E4_NodeC");
            var template = CreatePracticeTemplate(
                mixedError ? "e4_single_mixed" : "e4_single_swapped",
                new[] { lampDefinition, nodeA, nodeB, nodeC },
                new[] { "lamp", "a", "b", "c" },
                new[]
                {
                    CreateTemplateWire("lamp", "L", "a", "X"),
                    CreateTemplateWire("lamp", "L", "c", "X"),
                    CreateTemplateWire("lamp", "N", "b", "X")
                });

            var lamp = workspace.SpawnComponent(lampDefinition, Vector2.zero, "lamp", false);
            var a = workspace.SpawnComponent(nodeA, Vector2.zero, "a", false);
            var b = workspace.SpawnComponent(nodeB, Vector2.zero, "b", false);
            var c = workspace.SpawnComponent(nodeC, Vector2.zero, "c", false);
            if (lamp == null || a == null || b == null || c == null)
            {
                failures.Add("E1: 无法创建单灯端子映射测试元件。");
                return;
            }

            if (mixedError)
            {
                CreateWire(workspace, lamp, "L", a, "X");
                CreateWire(workspace, lamp, "N", b, "X");
                CreateWire(workspace, lamp, "N", c, "X");
            }
            else
            {
                CreateWire(workspace, lamp, "N", a, "X");
                CreateWire(workspace, lamp, "N", c, "X");
                CreateWire(workspace, lamp, "L", b, "X");
            }

            var result = PracticeConnectionChecker.Check(workspace, template);
            if (mixedError)
            {
                if (result.Passed) failures.Add("E2: 单灯混合错误不存在统一 L/N 映射，Practice 检查应失败。");
                var recognition = new CircuitTopologyRecognitionService().Recognize(workspace);
                if (recognition.Status == CircuitRecognitionStatus.EquivalentMatch)
                    failures.Add("E2: 单灯混合错误不得被识别为 EquivalentMatch。");
            }
            else if (!result.Passed)
            {
                failures.Add("E1: 单灯整体交换 L/N 后 Practice 检查应通过。");
            }
        }

        private static void TestTwoLampStableMapping(WorkspaceController workspace, ComponentDefinition lampDefinition, bool secondLampMixedError, List<string> failures)
        {
            ResetWorkspace(workspace);
            var nodeA1 = CreateTestNodeDefinition("E4_NodeA1");
            var nodeB1 = CreateTestNodeDefinition("E4_NodeB1");
            var nodeA2 = CreateTestNodeDefinition("E4_NodeA2");
            var nodeB2 = CreateTestNodeDefinition("E4_NodeB2");
            var template = CreatePracticeTemplate(
                secondLampMixedError ? "e4_two_lamp_mixed" : "e4_two_lamp_independent",
                new[] { lampDefinition, lampDefinition, nodeA1, nodeB1, nodeA2, nodeB2 },
                new[] { "lamp1", "lamp2", "a1", "b1", "a2", "b2" },
                new[]
                {
                    CreateTemplateWire("lamp1", "L", "a1", "X"),
                    CreateTemplateWire("lamp1", "N", "b1", "X"),
                    CreateTemplateWire("lamp2", "L", "a2", "X"),
                    CreateTemplateWire("lamp2", "N", "b2", "X")
                });

            var lamp1 = workspace.SpawnComponent(lampDefinition, Vector2.zero, "lamp1", false);
            var lamp2 = workspace.SpawnComponent(lampDefinition, Vector2.zero, "lamp2", false);
            var a1 = workspace.SpawnComponent(nodeA1, Vector2.zero, "a1", false);
            var b1 = workspace.SpawnComponent(nodeB1, Vector2.zero, "b1", false);
            var a2 = workspace.SpawnComponent(nodeA2, Vector2.zero, "a2", false);
            var b2 = workspace.SpawnComponent(nodeB2, Vector2.zero, "b2", false);
            if (lamp1 == null || lamp2 == null || a1 == null || b1 == null || a2 == null || b2 == null)
            {
                failures.Add("E3: 无法创建双灯端子映射测试元件。");
                return;
            }

            CreateWire(workspace, lamp1, "L", a1, "X");
            CreateWire(workspace, lamp1, "N", b1, "X");
            if (secondLampMixedError)
            {
                CreateWire(workspace, lamp2, "L", a2, "X");
                CreateWire(workspace, lamp2, "L", b2, "X");
            }
            else
            {
                CreateWire(workspace, lamp2, "N", a2, "X");
                CreateWire(workspace, lamp2, "L", b2, "X");
            }

            var result = PracticeConnectionChecker.Check(workspace, template);
            if (secondLampMixedError && result.Passed)
            {
                failures.Add("E4: 双灯中一盏混合错误不能被正确灯泡掩盖。");
            }
            if (!secondLampMixedError && !result.Passed)
            {
                failures.Add("E3: 两盏灯应可分别使用原始与交换端子映射。");
            }
        }

        private static void TestNonLampNameDoesNotGainSwapPrivilege(WorkspaceController workspace, List<string> failures)
        {
            ResetWorkspace(workspace);
            var namedLampButSwitch = CreateTestNodeDefinition("LampNamedSwitch");
            namedLampButSwitch.kind = ComponentKind.Switch;
            namedLampButSwitch.terminals = new List<TerminalDefinition>
            {
                CreateTerminal("L"),
                CreateTerminal("N")
            };
            var nodeA = CreateTestNodeDefinition("E4_NonLampNodeA");
            var nodeB = CreateTestNodeDefinition("E4_NonLampNodeB");
            var template = CreatePracticeTemplate(
                "e4_nonlamp_name",
                new[] { namedLampButSwitch, nodeA, nodeB },
                new[] { "device", "a", "b" },
                new[]
                {
                    CreateTemplateWire("device", "L", "a", "X"),
                    CreateTemplateWire("device", "N", "b", "X")
                });

            var device = workspace.SpawnComponent(namedLampButSwitch, Vector2.zero, "device", false);
            var a = workspace.SpawnComponent(nodeA, Vector2.zero, "a", false);
            var b = workspace.SpawnComponent(nodeB, Vector2.zero, "b", false);
            CreateWire(workspace, device, "N", a, "X");
            CreateWire(workspace, device, "L", b, "X");
            var result = PracticeConnectionChecker.Check(workspace, template);
            if (result.Passed)
            {
                failures.Add("E5: 名称含 Lamp 但 ComponentKind 不是 Lamp 的元件不得获得端子交换特权。");
            }
        }

        private static ComponentDefinition CreateTestNodeDefinition(string definitionName)
        {
            var definition = ScriptableObject.CreateInstance<ComponentDefinition>();
            definition.name = definitionName;
            definition.displayName = definitionName;
            definition.kind = ComponentKind.TerminalBlock;
            definition.size = new Vector2(60f, 60f);
            definition.terminals = new List<TerminalDefinition> { CreateTerminal("X") };
            return definition;
        }

        private static TerminalDefinition CreateTerminal(string terminalId)
        {
            return new TerminalDefinition { id = terminalId, label = terminalId, normalizedPosition = new Vector2(0.5f, 0.5f) };
        }

        private static CircuitTemplateDto CreatePracticeTemplate(
            string templateId,
            IReadOnlyList<ComponentDefinition> definitions,
            IReadOnlyList<string> instanceIds,
            IReadOnlyList<TemplateWireDto> wires)
        {
            var template = new CircuitTemplateDto { templateId = templateId, templateName = templateId };
            for (var i = 0; i < definitions.Count; i++)
            {
                template.components.Add(new TemplateComponentDto
                {
                    instanceId = instanceIds[i],
                    definitionName = definitions[i].name,
                    parameters = new List<ComponentParameter>()
                });
            }
            template.wires.AddRange(wires);
            return template;
        }

        private static TemplateWireDto CreateTemplateWire(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            return new TemplateWireDto
            {
                startComponentId = startComponentId,
                startTerminalId = startTerminalId,
                endComponentId = endComponentId,
                endTerminalId = endTerminalId,
                manualRoutePoints = new List<Vector2>()
            };
        }

        private static bool IsLampDefinition(string definitionName)
        {
            var definition = LoadDefinition(definitionName);
            return definition != null && definition.kind == ComponentKind.Lamp;
        }

        // --- 辅助方法 ---

        private static ComponentDefinition LoadDefinition(string name)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def != null && string.Equals(def.name, name, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            SimulationEngine.ResetRuntimeState();
            workspace.ClearDrawing(false);
        }

        private static void CreateWire(WorkspaceController workspace, CircuitComponent a, string termA, CircuitComponent b, string termB)
        {
            if (workspace.WireManager == null) return;
            var ta = a.GetTerminal(termA);
            var tb = b.GetTerminal(termB);
            if (ta == null || tb == null) return;
            workspace.WireManager.CreateWire(ta, tb, Color.yellow, WireStyle.Straight);
        }

        private static void RunSimulation(WorkspaceController workspace)
        {
            if (workspace.WireManager == null) return;
            new SimulationEngine(workspace.Components.ToList(), workspace.WireManager.Wires, 0f).Run();
        }

        private static void CloseAllSwitches(WorkspaceController workspace)
        {
            foreach (var c in workspace.Components)
            {
                if (c == null || c.Definition == null) continue;
                if (c.Definition.kind == ComponentKind.Switch || c.Definition.kind == ComponentKind.TwoWaySwitch ||
                    c.Definition.kind == ComponentKind.PushButton || c.Definition.kind == ComponentKind.Breaker ||
                    c.Definition.kind == ComponentKind.Fuse)
                {
                    c.SetClosed(true);
                }
            }
        }

        private static void AssertNoReverseWarning(CircuitStateResult result, string label, List<string> failures)
        {
            foreach (var comp in result.Components)
            {
                if (comp.Judgement != null && comp.Judgement.Contains("接反"))
                    failures.Add(label + ": 不应出现接反提示，实际=" + comp.Judgement);
            }
            foreach (var w in result.Warnings)
            {
                if (w.Contains("接反"))
                    failures.Add(label + ": 不应出现接反警告，实际=" + w);
            }
        }

        /// <summary>
        /// 克隆模板 DTO 并交换灯泡元件的 L/N 端子引用（在 wires 中把灯泡端点的 L 改为 N、N 改为 L）。
        /// 不修改原始模板，不改变元件数量或拓扑结构。
        /// </summary>
        private static CircuitTemplateDto CloneTemplateWithSwappedLampTerminals(CircuitTemplateDto original)
        {
            if (original == null || original.components == null || original.wires == null) return null;

            var json = JsonUtility.ToJson(original);
            var clone = JsonUtility.FromJson<CircuitTemplateDto>(json);
            if (clone == null) return null;

            // 找到灯泡元件的 instanceId
            var lampIds = new HashSet<string>();
            foreach (var comp in clone.components)
            {
                if (comp != null && IsLampDefinition(comp.definitionName))
                {
                    lampIds.Add(comp.instanceId);
                }
            }

            // 交换灯泡端子的 L↔N
            foreach (var wire in clone.wires)
            {
                if (wire == null) continue;
                if (lampIds.Contains(wire.startComponentId) && (wire.startTerminalId == "L" || wire.startTerminalId == "N"))
                {
                    wire.startTerminalId = wire.startTerminalId == "L" ? "N" : "L";
                }
                if (lampIds.Contains(wire.endComponentId) && (wire.endTerminalId == "L" || wire.endTerminalId == "N"))
                {
                    wire.endTerminalId = wire.endTerminalId == "L" ? "N" : "L";
                }
            }

            return clone;
        }
    }
}
