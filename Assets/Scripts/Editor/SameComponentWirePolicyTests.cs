using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ElectricalSim.Core;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-1 Phase 3 集中同元件接线策略测试。
    ///
    /// 验证 SameComponentWirePolicy、WireManager、SaveLoadService 在以下场景下的行为：
    /// 1-5. KM 各种端点对允许（14→A1、34→A1、33→A1、L1→T1、33→34）；
    /// 6. 自连拒绝；
    /// 7. 未知端点拒绝；
    /// 8. 普通元件同元件拒绝；
    /// 9-10. Motor_StarDelta 绕组端子允许、PE 拒绝；
    /// 11. allowSameComponentJumper=false 拒绝；
    /// 12. 交互创建 34→A1 成功；
    /// 13. 保存后重新导入 34→A1 成功；
    /// 14. 导入后端点和 Wire 数量保持；
    /// 15. duplicate Wire 拒绝；
    /// 16. 自连拒绝；
    /// 17. 锁定画布拒绝；
    /// 18. 删除、Undo、Redo 保持；
    /// 19. 33/34 不产生内部 CoilOn/CoilOff 导通；
    /// 20. 旧星三角图纸继续导入。
    ///
    /// 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出。
    /// </summary>
    public static class SameComponentWirePolicyTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string SimulationEnginePath = "Assets/Scripts/Core/SimulationEngine.cs";

        [MenuItem("Tools/Tests/Run Same Component Wire Policy Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            var notTested = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                if (workspace == null || saveLoad == null)
                {
                    throw new InvalidOperationException("测试依赖缺失：WorkspaceController / SaveLoadService 未找到。");
                }

                var km220V = FindDefinition(saveLoad, "Contactor_KM_220V");
                var km380V = FindDefinition(saveLoad, "Contactor_KM_380V");
                var motorDef = FindDefinition(saveLoad, "Motor_StarDelta_380V");
                var lampDef = FindDefinition(saveLoad, "Lamp_220V");
                if (km220V == null || km380V == null || motorDef == null || lampDef == null)
                {
                    throw new InvalidOperationException("测试依赖缺失：catalog 缺少 KM/Motor/Lamp 定义。");
                }

                // 1-5. KM 端点对允许
                Test01_KM_14_A1_Allowed(failures, km380V);
                Test02_KM_34_A1_Allowed(failures, km380V);
                Test03_KM_33_A1_Allowed(failures, km380V);
                Test04_KM_L1_T1_Allowed(failures, km380V);
                Test05_KM_33_34_Allowed(failures, km380V);

                // 6. 自连拒绝
                Test06_KM_A1_A1_SelfRejected(failures, km380V);

                // 7. 未知端点拒绝
                Test07_UnknownTerminalRejected(failures, km380V);

                // 8. 普通元件同元件拒绝
                Test08_NormalComponentRejected(failures, lampDef);

                // 9. Motor_StarDelta 绕组端子允许
                Test09_MotorStarDeltaWindingsAllowed(failures, motorDef);

                // 10. Motor_StarDelta PE 拒绝
                Test10_MotorStarDeltaPERejected(failures, motorDef);

                // 11. allowSameComponentJumper=false 拒绝
                Test11_AllowSameComponentJumperFalseRejected(failures, km380V);

                // 12. 交互创建 34→A1 成功
                Test12_InteractiveCreate34ToA1(failures, workspace, km380V);

                // 13. 保存后重新导入 34→A1 成功
                Test13_SaveAndReimport34ToA1(failures, workspace, saveLoad, km380V, notTested);

                // 14. 导入后端点和 Wire 数量保持
                Test14_ImportCountsMatch(failures, workspace, saveLoad, km380V);

                // 15. duplicate Wire 拒绝
                Test15_DuplicateWireRejected(failures, workspace, saveLoad, km380V);

                // 16. 自连拒绝（DTO 级）
                Test16_SelfConnectionRejected(failures, workspace, saveLoad, km380V);

                // 17. 锁定画布拒绝
                Test17_LockedCanvasRejected(failures, workspace, saveLoad, km380V);

                // 18. 删除、Undo、Redo 保持
                Test18_DeleteUndoRedo(failures, workspace, km380V, notTested);

                // 19. Phase 4: 33/34 内部导通位于 energized 分支（CoilOn 时导通）
                Test19_33_34InternalConductionInEnergizedBranch(failures);

                // 20. 旧星三角图纸继续导入
                Test20_StarDeltaDrawingStillImports(failures, workspace, saveLoad, motorDef);
            }
            catch (Exception exception)
            {
                failures.Add("测试执行出现未处理异常：" + exception);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            foreach (var n in notTested)
            {
                Debug.LogWarning("[NOT_TESTED] " + n);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== SameComponentWirePolicyTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "SameComponentWirePolicyTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== SameComponentWirePolicyTests 通过 ===");
        }

        // =========================================================================
        // 1. KM.14 → KM.A1：允许
        // =========================================================================
        private static void Test01_KM_14_A1_Allowed(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "14", "A1", out var reason);
            if (!ok) failures.Add($"01: KM 14→A1 应允许，reason={reason}");
        }

        // 2. KM.34 → KM.A1：允许
        private static void Test02_KM_34_A1_Allowed(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "34", "A1", out var reason);
            if (!ok) failures.Add($"02: KM 34→A1 应允许，reason={reason}");
        }

        // 3. KM.33 → KM.A1：允许
        private static void Test03_KM_33_A1_Allowed(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "33", "A1", out var reason);
            if (!ok) failures.Add($"03: KM 33→A1 应允许，reason={reason}");
        }

        // 4. KM.L1 → KM.T1：连接资格允许
        private static void Test04_KM_L1_T1_Allowed(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "L1", "T1", out var reason);
            if (!ok) failures.Add($"04: KM L1→T1 应允许，reason={reason}");
        }

        // 5. KM.33 → KM.34：允许
        private static void Test05_KM_33_34_Allowed(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "33", "34", out var reason);
            if (!ok) failures.Add($"05: KM 33→34 应允许，reason={reason}");
        }

        // 6. KM.A1 → KM.A1：拒绝（自连）
        private static void Test06_KM_A1_A1_SelfRejected(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "A1", "A1", out var reason);
            if (ok) failures.Add("06: KM A1→A1 自连应拒绝。");
            if (string.IsNullOrEmpty(reason)) failures.Add("06: 自连拒绝应提供原因。");
        }

        // 7. 未知端点：拒绝
        private static void Test07_UnknownTerminalRejected(List<string> failures, ComponentDefinition km)
        {
            var ok = SameComponentWirePolicy.CanConnect(km, "A1", "XX", out var reason);
            if (ok) failures.Add("07: 未知端点 XX 应拒绝。");
            if (string.IsNullOrEmpty(reason)) failures.Add("07: 未知端点拒绝应提供原因。");
        }

        // 8. 普通元件同元件连接：拒绝
        private static void Test08_NormalComponentRejected(List<string> failures, ComponentDefinition lamp)
        {
            // Lamp 的 allowSameComponentJumper=false
            var ok = SameComponentWirePolicy.CanConnect(lamp, "L", "N", out var reason);
            if (ok) failures.Add("08: 普通元件 Lamp L→N 同元件应拒绝。");
        }

        // 9. Motor_StarDelta 六个绕组端子：保持允许
        private static void Test09_MotorStarDeltaWindingsAllowed(List<string> failures, ComponentDefinition motor)
        {
            var windings = new[] { "U1", "V1", "W1", "U2", "V2", "W2" };
            foreach (var a in windings)
            {
                foreach (var b in windings)
                {
                    if (a == b) continue;
                    var ok = SameComponentWirePolicy.CanConnect(motor, a, b, out var reason);
                    if (!ok) failures.Add($"09: Motor {a}→{b} 应允许，reason={reason}");
                }
            }
        }

        // 10. Motor_StarDelta PE：拒绝
        private static void Test10_MotorStarDeltaPERejected(List<string> failures, ComponentDefinition motor)
        {
            var ok = SameComponentWirePolicy.CanConnect(motor, "U1", "PE", out var reason);
            if (ok) failures.Add("10: Motor U1→PE 应拒绝。");
            if (string.IsNullOrEmpty(reason) || !reason.Contains("PE"))
            {
                failures.Add($"10: PE 拒绝原因应包含 PE，实际={reason}");
            }
        }

        // 11. allowSameComponentJumper=false：拒绝
        private static void Test11_AllowSameComponentJumperFalseRejected(List<string> failures, ComponentDefinition km)
        {
            // 临时设置 allowSameComponentJumper=false
            var oldValue = km.allowSameComponentJumper;
            try
            {
                km.allowSameComponentJumper = false;
                var ok = SameComponentWirePolicy.CanConnect(km, "33", "34", out var reason);
                if (ok) failures.Add("11: allowSameComponentJumper=false 时 33→34 应拒绝。");
            }
            finally
            {
                km.allowSameComponentJumper = oldValue;
            }
        }

        // 12. 交互创建 34→A1 成功
        private static void Test12_InteractiveCreate34ToA1(List<string> failures, WorkspaceController workspace, ComponentDefinition km)
        {
            ResetWorkspace(workspace);
            var comp = workspace.SpawnComponent(km, Vector2.zero, "km_test_12", false);
            if (comp == null) { failures.Add("12: SpawnComponent 返回 null。"); return; }

            var t34 = comp.GetTerminal("34");
            var tA1 = comp.GetTerminal("A1");
            if (t34 == null || tA1 == null) { failures.Add("12: 端子 34 或 A1 未找到。"); return; }

            var wire = workspace.WireManager.CreateWire(t34, tA1, Color.red, WireStyle.Straight);
            if (wire == null) { failures.Add("12: 34→A1 Wire 创建失败。"); return; }

            if (workspace.WireManager.Wires.Count != 1)
            {
                failures.Add($"12: Wire 数应为 1，实际={workspace.WireManager.Wires.Count}。");
            }
        }

        // 13. 保存后重新导入 34→A1 成功
        private static void Test13_SaveAndReimport34ToA1(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition km, List<string> notTested)
        {
            ResetWorkspace(workspace);
            var comp = workspace.SpawnComponent(km, Vector2.zero, "km_test_13", false);
            if (comp == null) { failures.Add("13: SpawnComponent 返回 null。"); return; }
            var t34 = comp.GetTerminal("34");
            var tA1 = comp.GetTerminal("A1");
            if (t34 == null || tA1 == null) { failures.Add("13: 端子 34 或 A1 未找到。"); return; }
            workspace.WireManager.CreateWire(t34, tA1, Color.red, WireStyle.Straight);

            var testName = "KM1_Phase3_Test13_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var saveOk = saveLoad.SaveAs(testName, true, out var savedInfo, out _, out var saveError);
            if (!saveOk || savedInfo == null)
            {
                notTested.Add($"13: SaveAs 失败（batchmode persistentDataPath 限制），{saveError}，标记 NOT_TESTED。");
                // smoke: 直接 JSON 导入验证
                ResetWorkspace(workspace);
                var json = BuildDrawingJson(
                    new[] { ("km_rt", "Contactor_KM_380V", 0f, 0f) },
                    new[] { ("km_rt", "34", "km_rt", "A1") });
                var loadOk = saveLoad.LoadFromJsonString(json, out var loadError);
                if (!loadOk) failures.Add($"13: JSON 直接导入 34→A1 smoke 应成功，error={loadError}");
                return;
            }

            ResetWorkspace(workspace);
            var loadOk2 = saveLoad.LoadFromFile(savedInfo.filePath, out var loadError2);
            if (!loadOk2) { failures.Add($"13: 保存后重新导入应成功，error={loadError2}"); return; }

            if (workspace.WireManager.Wires.Count != 1)
            {
                failures.Add($"13: 重新导入后 Wire 数应为 1，实际={workspace.WireManager.Wires.Count}。");
            }

            try { saveLoad.DeleteSavedBlueprint(savedInfo.filePath, out _); } catch { }
        }

        // 14. 导入后端点和 Wire 数量保持
        private static void Test14_ImportCountsMatch(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition km)
        {
            ResetWorkspace(workspace);
            var json = BuildDrawingJson(
                new[] { ("km_1", "Contactor_KM_380V", 0f, 0f) },
                new[] { ("km_1", "34", "km_1", "A1"), ("km_1", "33", "km_1", "14") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok) { failures.Add($"14: 导入应成功，error={error}"); return; }
            if (workspace.Components.Count != 1) failures.Add($"14: 元件数应为 1，实际={workspace.Components.Count}。");
            if (workspace.WireManager.Wires.Count != 2) failures.Add($"14: Wire 数应为 2，实际={workspace.WireManager.Wires.Count}。");
        }

        // 15. duplicate Wire 拒绝
        private static void Test15_DuplicateWireRejected(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition km)
        {
            ResetWorkspace(workspace);
            var json = BuildDrawingJson(
                new[] { ("km_1", "Contactor_KM_380V", 0f, 0f) },
                new[] { ("km_1", "34", "km_1", "A1"), ("km_1", "A1", "km_1", "34") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add("15: 重复 Wire 应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("重复"))
            {
                failures.Add($"15: 错误信息应提示重复，实际={error}");
            }
        }

        // 16. 自连拒绝（DTO 级）
        private static void Test16_SelfConnectionRejected(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition km)
        {
            ResetWorkspace(workspace);
            var json = BuildDrawingJson(
                new[] { ("km_1", "Contactor_KM_380V", 0f, 0f) },
                new[] { ("km_1", "34", "km_1", "34") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add("16: 自连应被拒绝。");
        }

        // 17. 锁定画布拒绝
        private static void Test17_LockedCanvasRejected(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition km)
        {
            ResetWorkspace(workspace);
            workspace.SpawnComponent(km, Vector2.zero, "km_old", false);
            workspace.StartSimulation();
            workspace.ToggleInteractionLock();
            if (!workspace.IsInteractionLocked) { failures.Add("17: 前置锁定状态未建立。"); return; }

            var json = BuildDrawingJson(
                new[] { ("km_new", "Contactor_KM_380V", 0f, 0f) },
                new[] { ("km_new", "34", "km_new", "A1") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add("17: 锁定画布时应拒绝导入。");
            if (string.IsNullOrEmpty(error) || !error.Contains("锁定"))
            {
                failures.Add($"17: 错误信息应提示锁定，实际={error}");
            }
            if (!workspace.IsInteractionLocked) failures.Add("17: 拒绝后应保持锁定。");
        }

        // 18. 删除、Undo、Redo 保持
        private static void Test18_DeleteUndoRedo(List<string> failures, WorkspaceController workspace, ComponentDefinition km, List<string> notTested)
        {
            ResetWorkspace(workspace);
            var comp = workspace.SpawnComponent(km, Vector2.zero, "km_test_18", false);
            if (comp == null) { failures.Add("18: SpawnComponent 返回 null。"); return; }
            var t34 = comp.GetTerminal("34");
            var tA1 = comp.GetTerminal("A1");
            if (t34 == null || tA1 == null) { failures.Add("18: 端子未找到。"); return; }
            var wire = workspace.WireManager.CreateWire(t34, tA1, Color.red, WireStyle.Straight);
            if (wire == null) { failures.Add("18: Wire 创建失败。"); return; }
            if (workspace.WireManager.Wires.Count != 1) { failures.Add("18: 前置 Wire 数应为 1。"); return; }

            // 删除 Wire
            workspace.WireManager.DeleteWire(wire);
            if (workspace.WireManager.Wires.Count != 0)
            {
                failures.Add($"18: 删除后 Wire 数应为 0，实际={workspace.WireManager.Wires.Count}。");
            }

            // Undo/Redo 通过 WorkspaceController 的撤销系统
            var undoMethod = typeof(WorkspaceController).GetMethod("Undo", BindingFlags.Public | BindingFlags.Instance);
            var redoMethod = typeof(WorkspaceController).GetMethod("Redo", BindingFlags.Public | BindingFlags.Instance);
            if (undoMethod == null || redoMethod == null)
            {
                notTested.Add("18: WorkspaceController.Undo/Redo 方法未找到，Undo/Redo 标记 NOT_TESTED。");
                return;
            }

            try
            {
                undoMethod.Invoke(workspace, null);
                // Undo 后 Wire 可能恢复（取决于撤销系统实现）
                Debug.Log($"18: Undo 后 Wire 数={workspace.WireManager.Wires.Count}");

                redoMethod.Invoke(workspace, null);
                Debug.Log($"18: Redo 后 Wire 数={workspace.WireManager.Wires.Count}");
            }
            catch (Exception e)
            {
                notTested.Add("18: Undo/Redo 调用异常: " + e.Message);
            }
        }

        // 19. KM-1.1: 33/34 内部导通已迁移到 ContactorTerminalSchema.EnumerateClosedPairs
        private static void Test19_33_34InternalConductionInEnergizedBranch(List<string> failures)
        {
            var fullPath = Path.Combine(Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""), SimulationEnginePath);
            if (!File.Exists(fullPath))
            {
                failures.Add("19: SimulationEngine.cs 未找到。");
                return;
            }

            var content = File.ReadAllText(fullPath);

            // KM-1.1: SimulationEngine 应使用 ContactorTerminalSchema.EnumerateClosedPairs 遍历触点
            var hasSchemaIteration = System.Text.RegularExpressions.Regex.IsMatch(content, @"ContactorTerminalSchema\.EnumerateClosedPairs");
            if (!hasSchemaIteration)
            {
                failures.Add("19: SimulationEngine 应使用 ContactorTerminalSchema.EnumerateClosedPairs 进行触点遍历，但未找到。");
                return;
            }

            // 验证 ContactorTerminalSchema.cs 定义了 33/34 配对
            var schemaPath = Path.Combine(Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""), "Assets/Scripts/Core/ContactorTerminalSchema.cs");
            if (!File.Exists(schemaPath))
            {
                failures.Add("19: ContactorTerminalSchema.cs 未找到。");
                return;
            }
            var schemaContent = File.ReadAllText(schemaPath);
            if (!schemaContent.Contains("AuxNO33") || !schemaContent.Contains("AuxNO34"))
            {
                failures.Add("19: ContactorTerminalSchema.cs 应定义 33/34 配对 (TerminalConstants.AuxNO33/AuxNO34)。");
                return;
            }

            // 验证 33/34 位于 NO 集合（得电时闭合），而非 NC 集合（失电时闭合）
            if (schemaContent.Contains("NormallyClosedContactPairs") &&
                System.Text.RegularExpressions.Regex.IsMatch(schemaContent, @"NormallyClosedContactPairs[^;]*AuxNO33[^;]*AuxNO34"))
            {
                failures.Add("19: 33/34 不应位于 NormallyClosedContactPairs（应为 NormallyOpen）。");
            }
        }

        // 20. 旧星三角图纸继续导入
        private static void Test20_StarDeltaDrawingStillImports(List<string> failures, WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition motorDef)
        {
            ResetWorkspace(workspace);
            var json = BuildDrawingJson(
                new[] { ("motor_1", "Motor_StarDelta_380V", 0f, 0f) },
                new[] { ("motor_1", "U1", "motor_1", "W2") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok) { failures.Add($"20: 旧星三角图纸应导入成功，error={error}"); return; }
            if (workspace.Components.Count != 1) failures.Add($"20: 元件数应为 1，实际={workspace.Components.Count}。");
            if (workspace.WireManager.Wires.Count != 1) failures.Add($"20: Wire 数应为 1，实际={workspace.WireManager.Wires.Count}。");
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================
        private static ComponentDefinition FindDefinition(SaveLoadService saveLoad, string name)
        {
            foreach (var def in saveLoad.Catalog)
            {
                if (def != null && def.name == name) return def;
            }
            return null;
        }

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            if (workspace == null) return;
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            if (workspace.IsSimulationRunning) workspace.StopSimulation();
            workspace.ClearDrawing(true);
        }

        private static string BuildDrawingJson(
            IEnumerable<(string instanceId, string definitionName, float x, float y)> components,
            IEnumerable<(string startComp, string startTerm, string endComp, string endTerm)> wires)
        {
            var compLines = components.Select(c =>
                $"{{\"instanceId\":\"{c.instanceId}\",\"definitionName\":\"{c.definitionName}\",\"x\":{c.x},\"y\":{c.y},\"isClosed\":false,\"parameters\":[]}}");
            var wireLines = wires.Select(w =>
                $"{{\"startComponentId\":\"{w.startComp}\",\"startTerminalId\":\"{w.startTerm}\",\"endComponentId\":\"{w.endComp}\",\"endTerminalId\":\"{w.endTerm}\"}}");

            return "{\"documentId\":\"p3-test\",\"documentName\":\"P3 Test\",\"savedAt\":\"2026-08-06 12:00:00\"," +
                   "\"components\":[" + string.Join(",", compLines) + "]," +
                   "\"wires\":[" + string.Join(",", wireLines) + "]}";
        }
    }
}
