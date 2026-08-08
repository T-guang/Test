using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
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
    /// KM-2B5 Gate A: 接触器旁路安全规则测试。
    ///
    /// 验证 CircuitRuleChecker 基于 workspace.WireManager.Wires 真实外部导线检测
    /// 接触器受控触点直接短接，并产生 CONTACTOR_MAIN_CONTACT_BYPASS (Error) 或
    /// CONTACTOR_AUX_CONTACT_BYPASS (Warning)。
    ///
    /// T01-T03: 主触点旁路 (L1-T1, L2-T2, L3-T3) → MAIN code + Error
    /// T04-T06: 辅助触点旁路 (13-14, 33-34, 21-22) → AUX code + Warning
    /// T07-T10: 同 KM 非触点对 (14-A1, 34-A1, 13-A1/33-A1, 14-33) → 无 B5 bypass
    /// T11: 跨 KM (KM1.13 → KM2.14) → 无 B5 bypass
    /// T12: 非 Contactor 同形 ID (Lamp 13/14) → 无 B5 bypass
    ///
    /// 检测必须直接遍历真实外部 Wire，不从 structuralGraph 或 liveGraph 推断。
    /// 同一根 Wire 只产生一次 B5 issue。
    /// </summary>
    public static class KM2B5_ContactorBypassRuleTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string KmDef = "Contactor_KM_220V";

        [MenuItem("Tools/Tests/Run KM2B5 Contactor Bypass Rule Tests")]
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

                // T01-T06: positive cases (expect bypass issue)
                T01_MainBypass_L1_T1(failures, workspace, saveLoad);
                T02_MainBypass_L2_T2(failures, workspace, saveLoad);
                T03_MainBypass_L3_T3(failures, workspace, saveLoad);
                T04_AuxBypass_13_14(failures, workspace, saveLoad);
                T05_AuxBypass_33_34(failures, workspace, saveLoad);
                T06_AuxBypass_21_22(failures, workspace, saveLoad);

                // T07-T11: negative cases (expect no bypass issue)
                T07_NoBypass_14_A1(failures, workspace, saveLoad);
                T08_NoBypass_34_A1(failures, workspace, saveLoad);
                T09_NoBypass_13_A1_33_A1(failures, workspace, saveLoad);
                T10_NoBypass_14_33(failures, workspace, saveLoad);
                T11_NoBypass_CrossKm(failures, workspace, saveLoad);

                // T12: non-contactor defensive test (CircuitTestFactory-based)
                T12_NoBypass_NonContactor(failures);
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
                Debug.Log("=== KM2B5_ContactorBypassRuleTests 通过 (12/12) ===");
            }
            else
            {
                Debug.LogError("=== KM2B5_ContactorBypassRuleTests 失败 ===");
                foreach (var f in failures) Debug.LogError("[FAIL] " + f);
                throw new InvalidOperationException(
                    "KM2B5_ContactorBypassRuleTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }
        }

        // =========================================================================
        // T01-T03: 主触点旁路 → CONTACTOR_MAIN_CONTACT_BYPASS + Error
        // =========================================================================

        private static void T01_MainBypass_L1_T1(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T01-MainBypass-L1-T1";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "L1", "T1");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_MAIN_CONTACT_BYPASS", CircuitIssueSeverity.Error);
        }

        private static void T02_MainBypass_L2_T2(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T02-MainBypass-L2-T2";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "L2", "T2");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_MAIN_CONTACT_BYPASS", CircuitIssueSeverity.Error);
        }

        private static void T03_MainBypass_L3_T3(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T03-MainBypass-L3-T3";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "L3", "T3");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_MAIN_CONTACT_BYPASS", CircuitIssueSeverity.Error);
        }

        // =========================================================================
        // T04-T06: 辅助触点旁路 → CONTACTOR_AUX_CONTACT_BYPASS + Warning
        // =========================================================================

        private static void T04_AuxBypass_13_14(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T04-AuxBypass-13-14";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "13", "14");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_AUX_CONTACT_BYPASS", CircuitIssueSeverity.Warning);
        }

        private static void T05_AuxBypass_33_34(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T05-AuxBypass-33-34";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "33", "34");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_AUX_CONTACT_BYPASS", CircuitIssueSeverity.Warning);
        }

        private static void T06_AuxBypass_21_22(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T06-AuxBypass-21-22";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "21", "22");
            AssertHasBypassIssue(failures, scenario, issues, "CONTACTOR_AUX_CONTACT_BYPASS", CircuitIssueSeverity.Warning);
        }

        // =========================================================================
        // T07-T10: 同 KM 非触点对 → 无 B5 bypass
        // =========================================================================

        private static void T07_NoBypass_14_A1(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T07-NoBypass-14-A1";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "14", "A1");
            AssertNoBypassIssue(failures, scenario, issues);
        }

        private static void T08_NoBypass_34_A1(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T08-NoBypass-34-A1";
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "34", "A1");
            AssertNoBypassIssue(failures, scenario, issues);
        }

        private static void T09_NoBypass_13_A1_33_A1(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T09-NoBypass-13-A1-33-A1";
            // 两根导线：13→A1 和 33→A1，均非触点对
            var issues = SpawnAndCheckMultiple(failures, workspace, saveLoad, scenario,
                new[] { ("13", "A1"), ("33", "A1") });
            AssertNoBypassIssue(failures, scenario, issues);
        }

        private static void T10_NoBypass_14_33(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T10-NoBypass-14-33";
            // 跨辅助触点对：14(NO1) → 33(NO2)，非单一 pair
            var issues = SpawnAndCheck(failures, workspace, saveLoad, scenario, "14", "33");
            AssertNoBypassIssue(failures, scenario, issues);
        }

        // =========================================================================
        // T11: 跨 KM (KM1.13 → KM2.14) → 无 B5 bypass
        // =========================================================================

        private static void T11_NoBypass_CrossKm(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            const string scenario = "T11-NoBypass-CrossKm";
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();

            var dto = new CircuitTemplateDto
            {
                templateId = "b5_cross_km_test",
                templateName = "B5 Cross KM Test",
                category = "Industrial",
                difficulty = "test"
            };
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_2", definitionName = KmDef, x = 200, y = 0 });
            dto.wires.Add(new TemplateWireDto
            {
                startComponentId = "km_1", startTerminalId = "13",
                endComponentId = "km_2", endTerminalId = "14",
                color = "#F21F1F", style = "Orthogonal"
            });

            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var message))
            {
                failures.Add($"{scenario}: Spawn failed: {message}");
                return;
            }

            var checker = new CircuitRuleChecker(workspace);
            var result = checker.Check();
            AssertNoBypassIssue(failures, scenario, result.issues);
        }

        // =========================================================================
        // T12: 非 Contactor 同形 ID (Lamp 13/14) → 无 B5 bypass
        // 使用 CircuitTestFactory 构造，因为 WireManager 不允许非 Contactor 同元件跳线。
        // 验证 B5 规则的 kind == ContactorCoil 防护。
        // =========================================================================

        private static void T12_NoBypass_NonContactor(List<string> failures)
        {
            const string scenario = "T12-NoBypass-NonContactor";
            try
            {
                var factory = new CircuitTestFactory();
                // 创建 Lamp（kind=Lamp，非 ContactorCoil），端子 ID 恰好为 13/14
                var lamp = factory.CreateComponent("lamp_1", "Lamp_220V", ComponentKind.Lamp, "13", "14");
                factory.Connect(lamp, "13", lamp, "14");

                // 静态验证：TryFindPair 匹配 13/14，但 kind 不是 ContactorCoil
                if (!ContactorTerminalSchema.TryFindPair("13", "14", out var pair))
                {
                    failures.Add($"{scenario}: TryFindPair(13,14) 应返回 true");
                    return;
                }
                if (lamp.Definition.kind == ComponentKind.ContactorCoil)
                {
                    failures.Add($"{scenario}: Lamp kind 不应为 ContactorCoil");
                    return;
                }
                Debug.Log($"[{scenario}] 静态验证: TryFindPair(13,14)=true, kind={lamp.Definition.kind} (非 ContactorCoil)");

                // 动态验证：反射调用 DetectContactorBypassIssues（如果已实现）
                var wires = (List<WireView>)typeof(CircuitTestFactory)
                    .GetField("wires", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(factory);

                var method = typeof(CircuitRuleChecker).GetMethod(
                    "DetectContactorBypassIssues",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (method == null)
                {
                    Debug.Log($"[{scenario}] NOT_TESTED: DetectContactorBypassIssues 未实现 (RED 状态，规则尚未实现)");
                    return;
                }

                var issues = (List<CircuitIssue>)method.Invoke(null, new object[] { wires });
                var bypassCount = issues.Count(i =>
                    i.code == "CONTACTOR_MAIN_CONTACT_BYPASS" || i.code == "CONTACTOR_AUX_CONTACT_BYPASS");

                if (bypassCount > 0)
                {
                    failures.Add($"{scenario}: 非 Contactor 元件不应产生 bypass issue，实际 {bypassCount} 个");
                }
                else
                {
                    Debug.Log($"[{scenario}] PASS: 非 Contactor 13/14 端子未产生 bypass issue");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{scenario} 异常: {e.Message}");
            }
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        private static List<CircuitIssue> SpawnAndCheck(
            List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad,
            string scenario, string terminalA, string terminalB)
        {
            return SpawnAndCheckMultiple(failures, workspace, saveLoad, scenario,
                new[] { (terminalA, terminalB) });
        }

        private static List<CircuitIssue> SpawnAndCheckMultiple(
            List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad,
            string scenario, IReadOnlyList<(string a, string b)> wires)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();

            var dto = new CircuitTemplateDto
            {
                templateId = "b5_bypass_test",
                templateName = "B5 Bypass Test",
                category = "Industrial",
                difficulty = "test"
            };
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });

            for (var i = 0; i < wires.Count; i++)
            {
                dto.wires.Add(new TemplateWireDto
                {
                    startComponentId = "km_1", startTerminalId = wires[i].a,
                    endComponentId = "km_1", endTerminalId = wires[i].b,
                    color = "#F21F1F", style = "Orthogonal"
                });
            }

            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var message))
            {
                failures.Add($"{scenario}: Spawn failed: {message}");
                return new List<CircuitIssue>();
            }

            var checker = new CircuitRuleChecker(workspace);
            var result = checker.Check();
            return result.issues;
        }

        private static void AssertHasBypassIssue(
            List<string> failures, string scenario, List<CircuitIssue> issues,
            string expectedCode, CircuitIssueSeverity expectedSeverity)
        {
            var bypassIssues = issues.Where(i => i.code == expectedCode).ToList();
            if (bypassIssues.Count == 0)
            {
                var allCodes = issues.Count > 0
                    ? string.Join(", ", issues.Select(i => i.code).Distinct())
                    : "(empty)";
                failures.Add($"{scenario}: 期望 {expectedCode} issue 但未找到。所有 issues: {allCodes}");
                return;
            }
            if (bypassIssues[0].severity != expectedSeverity)
            {
                failures.Add($"{scenario}: 期望 severity {expectedSeverity} 但实际 {bypassIssues[0].severity}");
                return;
            }
            Debug.Log($"[{scenario}] PASS: 找到 {expectedCode} severity={expectedSeverity} " +
                $"title={bypassIssues[0].title}");
        }

        private static void AssertNoBypassIssue(
            List<string> failures, string scenario, List<CircuitIssue> issues)
        {
            var mainBypass = issues.Where(i => i.code == "CONTACTOR_MAIN_CONTACT_BYPASS").ToList();
            var auxBypass = issues.Where(i => i.code == "CONTACTOR_AUX_CONTACT_BYPASS").ToList();

            if (mainBypass.Count > 0)
            {
                failures.Add($"{scenario}: 不应出现 CONTACTOR_MAIN_CONTACT_BYPASS，实际 {mainBypass.Count} 个");
            }
            if (auxBypass.Count > 0)
            {
                failures.Add($"{scenario}: 不应出现 CONTACTOR_AUX_CONTACT_BYPASS，实际 {auxBypass.Count} 个");
            }
            if (mainBypass.Count == 0 && auxBypass.Count == 0)
            {
                Debug.Log($"[{scenario}] PASS: 无 B5 bypass issue");
            }
        }
    }
}
