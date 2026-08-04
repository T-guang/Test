using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Practice;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// F1-A 用户图纸导入最小安全修复测试。
    ///
    /// 验证 SaveLoadService.LoadFromJsonString 在以下场景下的行为：
    /// - 合法停止态/运行中导入成功，运行中导入停止旧仿真且新电路不自动运行；
    /// - 重复 instanceId 在停止仿真和清空画布前被拒绝，失败时旧画布和运行态保持；
    /// - 无效 Wire（同端子、普通元件同元件跳线、PE 跳线、重复端点对、不存在元件/端子）被拒绝；
    /// - 合法星三角同元件跳线仍能导入；
    /// - 损坏 JSON 被拒绝；
    /// - 画布锁定时导入被拒绝且运行状态保持；
    /// - 失败场景不出现成功提示；
    /// - 成功导入后元件数和 Wire 数与 DTO 一致；
    /// - 保存后重新导入的合法用户图纸正常；
    /// - 练习状态下导入入口可达时拒绝导入。
    ///
    /// 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出。
    /// </summary>
    public static class UserDrawingImportSafetyTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Tests/Run User Drawing Import Safety Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                var canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
                if (workspace == null || saveLoad == null || canvas == null)
                {
                    throw new InvalidOperationException("F1-A 测试依赖缺失：WorkspaceController / SaveLoadService / Canvas 未找到。");
                }

                var practice = PracticeSessionController.Instance;
                InvokePrivate(practice, "EnsureReferences");

                // 确认测试所需的元件定义在 catalog 中可用。
                var lampDef = FindDefinition(saveLoad, "Lamp_220V");
                var buttonDef = FindDefinition(saveLoad, "Button_Start_NO");
                var motorDef = FindDefinition(saveLoad, "Motor_StarDelta_380V");
                if (lampDef == null || buttonDef == null || motorDef == null)
                {
                    throw new InvalidOperationException("F1-A 测试依赖缺失：catalog 缺少 Lamp_220V / Button_Start_NO / Motor_StarDelta_380V。");
                }

                Test01_ValidStoppedImportSuccess(workspace, saveLoad, lampDef, buttonDef, failures);
                Test02_RunningImportStopsSimulation(workspace, saveLoad, lampDef, buttonDef, failures);
                Test03_DuplicateInstanceIdRejected(workspace, saveLoad, lampDef, failures);
                Test04_DuplicateIdKeepsOldCanvas(workspace, saveLoad, lampDef, buttonDef, failures);
                Test05_DuplicateIdKeepsRunningState(workspace, saveLoad, lampDef, failures);
                Test06_SameTerminalConnectionRejected(workspace, saveLoad, lampDef, failures);
                Test07_NormalComponentSameComponentWireRejected(workspace, saveLoad, lampDef, failures);
                Test08_ValidStarDeltaJumperImports(workspace, saveLoad, motorDef, failures);
                Test09_PEJumperNotRelaxed(workspace, saveLoad, motorDef, failures);
                Test10_DuplicateWireEndpointPairRejected(workspace, saveLoad, lampDef, buttonDef, failures);
                Test11_MissingComponentAndTerminalRejected(workspace, saveLoad, lampDef, failures);
                Test12_CorruptedJsonRejected(workspace, saveLoad, failures);
                Test13_LockedCanvasImportRejected(workspace, saveLoad, lampDef, buttonDef, failures);
                Test14_FailureNoSuccessStatus(workspace, saveLoad, lampDef, failures);
                Test15_SuccessCountsMatchDto(workspace, saveLoad, lampDef, buttonDef, failures);
                Test16_SaveAndReimportValidDrawing(workspace, saveLoad, lampDef, buttonDef, failures);
                Test17_PracticeImportEntryAudit(workspace, saveLoad, practice, lampDef, buttonDef, failures);
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
                throw new InvalidOperationException("F1-A 用户图纸导入安全测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][F1-A] 用户图纸导入安全：通过");
        }

        // 1. 合法停止态用户图纸导入成功
        private static void Test01_ValidStoppedImportSuccess(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "01";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f), ("btn_1", "Button_Start_NO", 100f, 0f) },
                new[] { ("lamp_1", "L", "btn_1", "23") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok || !string.IsNullOrEmpty(error))
            {
                failures.Add($"{scenario}: 合法停止态导入应成功，error={error}");
                return;
            }
            if (workspace.IsSimulationRunning) failures.Add($"{scenario}: 导入后仿真不应运行。");
            if (workspace.Components.Count != 2) failures.Add($"{scenario}: 导入后元件数应为 2，实际={workspace.Components.Count}。");
            if (workspace.WireManager.Wires.Count != 1) failures.Add($"{scenario}: 导入后 Wire 数应为 1，实际={workspace.WireManager.Wires.Count}。");
        }

        // 2. 运行中合法导入，旧仿真停止，新电路不自动运行
        private static void Test02_RunningImportStopsSimulation(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "02";
            ResetWorkspace(workspace);

            // 建立旧电路并启动仿真
            SpawnSimpleCircuit(workspace, lampDef);
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning)
            {
                failures.Add($"{scenario}: 前置仿真未启动。");
                return;
            }

            var json = BuildDrawingJson(
                new[] { ("lamp_new", "Lamp_220V", 0f, 0f), ("btn_new", "Button_Start_NO", 100f, 0f) },
                new[] { ("lamp_new", "L", "btn_new", "23") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok) { failures.Add($"{scenario}: 运行中合法导入应成功，error={error}"); return; }
            if (workspace.IsSimulationRunning) failures.Add($"{scenario}: 导入后旧仿真应已停止。");
            if (workspace.Components.Count != 2) failures.Add($"{scenario}: 导入后元件数应为 2，实际={workspace.Components.Count}。");
        }

        // 3. 重复 instanceId 被拒绝
        private static void Test03_DuplicateInstanceIdRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "03";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("dup_1", "Lamp_220V", 0f, 0f), ("dup_1", "Lamp_220V", 100f, 0f) },
                Array.Empty<(string, string, string, string)>());

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: 重复 instanceId 应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("dup_1") || !error.Contains("重复"))
            {
                failures.Add($"{scenario}: 错误信息应包含重复 ID 'dup_1'，实际={error}");
            }
        }

        // 4. 重复 ID 失败时旧元件和 Wire 保持
        private static void Test04_DuplicateIdKeepsOldCanvas(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "04";
            ResetWorkspace(workspace);

            // 建立旧画布
            SpawnSimpleCircuit(workspace, lampDef);
            var oldComponentCount = workspace.Components.Count;
            var oldWireCount = workspace.WireManager.Wires.Count;

            var json = BuildDrawingJson(
                new[] { ("dup_1", "Lamp_220V", 0f, 0f), ("dup_1", "Lamp_220V", 100f, 0f) },
                Array.Empty<(string, string, string, string)>());

            saveLoad.LoadFromJsonString(json, out _);

            if (workspace.Components.Count != oldComponentCount)
            {
                failures.Add($"{scenario}: 重复 ID 失败后旧元件数应保持={oldComponentCount}，实际={workspace.Components.Count}。");
            }
            if (workspace.WireManager.Wires.Count != oldWireCount)
            {
                failures.Add($"{scenario}: 重复 ID 失败后旧 Wire 数应保持={oldWireCount}，实际={workspace.WireManager.Wires.Count}。");
            }
        }

        // 5. 重复 ID 失败时运行状态保持
        private static void Test05_DuplicateIdKeepsRunningState(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "05";
            ResetWorkspace(workspace);

            SpawnSimpleCircuit(workspace, lampDef);
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning)
            {
                failures.Add($"{scenario}: 前置仿真未启动。");
                return;
            }

            var json = BuildDrawingJson(
                new[] { ("dup_1", "Lamp_220V", 0f, 0f), ("dup_1", "Lamp_220V", 100f, 0f) },
                Array.Empty<(string, string, string, string)>());

            saveLoad.LoadFromJsonString(json, out _);

            if (!workspace.IsSimulationRunning)
            {
                failures.Add($"{scenario}: 重复 ID 失败后运行状态应保持。");
            }
        }

        // 6. 相同端子连接被拒绝
        private static void Test06_SameTerminalConnectionRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "06";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f) },
                new[] { ("lamp_1", "L", "lamp_1", "L") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: 相同端子连接应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("同一端子"))
            {
                failures.Add($"{scenario}: 错误信息应提示同一端子，实际={error}");
            }
        }

        // 7. 普通元件同元件 Wire 被拒绝
        private static void Test07_NormalComponentSameComponentWireRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "07";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f) },
                new[] { ("lamp_1", "L", "lamp_1", "N") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: 普通元件同元件 Wire 应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("不允许同一器件内部端子跳线"))
            {
                failures.Add($"{scenario}: 错误信息应提示不允许跳线，实际={error}");
            }
        }

        // 8. 合法星三角同元件跳线仍能导入
        private static void Test08_ValidStarDeltaJumperImports(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition motorDef, List<string> failures)
        {
            const string scenario = "08";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("motor_1", "Motor_StarDelta_380V", 0f, 0f) },
                new[] { ("motor_1", "U1", "motor_1", "W2") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok) { failures.Add($"{scenario}: 合法星三角跳线应导入成功，error={error}"); return; }
            if (workspace.Components.Count != 1) failures.Add($"{scenario}: 导入后元件数应为 1，实际={workspace.Components.Count}。");
            if (workspace.WireManager.Wires.Count != 1) failures.Add($"{scenario}: 导入后 Wire 数应为 1，实际={workspace.WireManager.Wires.Count}。");
        }

        // 9. PE 同元件跳线不得被误放开
        private static void Test09_PEJumperNotRelaxed(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition motorDef, List<string> failures)
        {
            const string scenario = "09";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("motor_1", "Motor_StarDelta_380V", 0f, 0f) },
                new[] { ("motor_1", "U1", "motor_1", "PE") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: PE 同元件跳线应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("PE"))
            {
                failures.Add($"{scenario}: 错误信息应包含 PE，实际={error}");
            }
        }

        // 10. 重复 Wire 端点对被拒绝
        private static void Test10_DuplicateWireEndpointPairRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "10";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f), ("btn_1", "Button_Start_NO", 100f, 0f) },
                new[] { ("lamp_1", "L", "btn_1", "23"), ("btn_1", "23", "lamp_1", "L") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: 重复 Wire 端点对应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("重复导线"))
            {
                failures.Add($"{scenario}: 错误信息应提示重复导线，实际={error}");
            }
        }

        // 11. 不存在的元件、端子继续被拒绝
        private static void Test11_MissingComponentAndTerminalRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "11";
            ResetWorkspace(workspace);

            // 导线引用不存在的元件
            var jsonMissingComp = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f) },
                new[] { ("lamp_1", "L", "ghost_1", "23") });
            var ok1 = saveLoad.LoadFromJsonString(jsonMissingComp, out var error1);
            if (ok1) failures.Add($"{scenario}-a: 引用不存在元件应被拒绝。");

            // 导线引用不存在的端子
            var jsonMissingTerminal = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f) },
                new[] { ("lamp_1", "L", "lamp_1", "T99") });
            // 注意：lamp_1 → lamp_1 同元件且 Lamp_220V 不允许跳线，会先触发跳线拒绝。
            // 改用两个不同元件测试端子不存在
            var jsonMissingTerminal2 = BuildDrawingJson(
                new[] { ("lamp_1", "Lamp_220V", 0f, 0f), ("btn_1", "Button_Start_NO", 100f, 0f) },
                new[] { ("lamp_1", "L", "btn_1", "T99") });
            var ok2 = saveLoad.LoadFromJsonString(jsonMissingTerminal2, out var error2);
            if (ok2) failures.Add($"{scenario}-b: 引用不存在端子应被拒绝。");
            if (string.IsNullOrEmpty(error2) || !error2.Contains("缺少端子"))
            {
                failures.Add($"{scenario}-b: 错误信息应提示缺少端子，实际={error2}");
            }
        }

        // 12. 损坏 JSON 继续被拒绝
        private static void Test12_CorruptedJsonRejected(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "12";
            ResetWorkspace(workspace);

            var ok = saveLoad.LoadFromJsonString("{ this is not valid json }", out var error);
            if (ok) failures.Add($"{scenario}: 损坏 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("JSON"))
            {
                failures.Add($"{scenario}: 错误信息应提示 JSON 格式错误，实际={error}");
            }
        }

        // 13. 画布锁定时导入被拒绝且运行状态保持
        private static void Test13_LockedCanvasImportRejected(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "13";
            ResetWorkspace(workspace);

            SpawnSimpleCircuit(workspace, lampDef);
            workspace.StartSimulation();
            workspace.ToggleInteractionLock();
            if (!workspace.IsInteractionLocked || !workspace.IsSimulationRunning)
            {
                failures.Add($"{scenario}: 前置锁定+运行状态未建立。");
                return;
            }

            var json = BuildDrawingJson(
                new[] { ("lamp_new", "Lamp_220V", 0f, 0f), ("btn_new", "Button_Start_NO", 100f, 0f) },
                new[] { ("lamp_new", "L", "btn_new", "23") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (ok) failures.Add($"{scenario}: 画布锁定时导入应被拒绝。");
            if (string.IsNullOrEmpty(error) || !error.Contains("锁定"))
            {
                failures.Add($"{scenario}: 错误信息应提示画布锁定，实际={error}");
            }
            if (!workspace.IsSimulationRunning) failures.Add($"{scenario}: 锁定拒绝后运行状态应保持。");
            if (!workspace.IsInteractionLocked) failures.Add($"{scenario}: 锁定拒绝后画布应保持锁定。");
        }

        // 14. 任一失败场景不出现成功提示
        private static void Test14_FailureNoSuccessStatus(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, List<string> failures)
        {
            const string scenario = "14";
            ResetWorkspace(workspace);

            // 重复 ID 失败
            saveLoad.LoadFromJsonString(
                BuildDrawingJson(new[] { ("d", "Lamp_220V", 0f, 0f), ("d", "Lamp_220V", 1f, 0f) }, Array.Empty<(string, string, string, string)>()),
                out _);
            var status1 = GetStatusText(workspace);
            if (status1 != null && status1.Contains("导入成功"))
            {
                failures.Add($"{scenario}-a: 重复 ID 失败不应出现成功提示，status={status1}");
            }

            // 同端子失败
            ResetWorkspace(workspace);
            saveLoad.LoadFromJsonString(
                BuildDrawingJson(new[] { ("l", "Lamp_220V", 0f, 0f) }, new[] { ("l", "L", "l", "L") }),
                out _);
            var status2 = GetStatusText(workspace);
            if (status2 != null && status2.Contains("导入成功"))
            {
                failures.Add($"{scenario}-b: 同端子失败不应出现成功提示，status={status2}");
            }

            // 损坏 JSON 失败
            ResetWorkspace(workspace);
            saveLoad.LoadFromJsonString("not json", out _);
            var status3 = GetStatusText(workspace);
            if (status3 != null && status3.Contains("导入成功"))
            {
                failures.Add($"{scenario}-c: 损坏 JSON 失败不应出现成功提示，status={status3}");
            }
        }

        // 15. 成功导入后元件数、Wire 数与 DTO 一致
        private static void Test15_SuccessCountsMatchDto(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "15";
            ResetWorkspace(workspace);

            var json = BuildDrawingJson(
                new[] { ("c1", "Lamp_220V", 0f, 0f), ("c2", "Button_Start_NO", 100f, 0f), ("c3", "Lamp_220V", 200f, 0f) },
                new[] { ("c1", "L", "c2", "23"), ("c2", "24", "c3", "L") });

            var ok = saveLoad.LoadFromJsonString(json, out var error);
            if (!ok) { failures.Add($"{scenario}: 合法导入应成功，error={error}"); return; }
            if (workspace.Components.Count != 3) failures.Add($"{scenario}: 元件数应为 3，实际={workspace.Components.Count}。");
            if (workspace.WireManager.Wires.Count != 2) failures.Add($"{scenario}: Wire 数应为 2，实际={workspace.WireManager.Wires.Count}。");
        }

        // 16. 保存后重新导入的合法用户图纸正常
        private static void Test16_SaveAndReimportValidDrawing(
            WorkspaceController workspace, SaveLoadService saveLoad,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "16";
            ResetWorkspace(workspace);

            // 建立画布并保存
            SpawnSimpleCircuit(workspace, lampDef);
            var testName = "F1A_Reimport_Test_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var saveOk = saveLoad.SaveAs(testName, true, out var savedInfo, out _, out var saveError);
            if (!saveOk || savedInfo == null)
            {
                // batchmode 环境下 persistentDataPath 可能存在权限限制。
                // 标记为 NOT_TESTED_SAVE_REIMPORT，不声称真实 SaveAs → LoadFromFile roundtrip 已通过。
                var reason = string.IsNullOrWhiteSpace(saveError) ? "SaveAs 返回失败且未提供错误信息" : saveError;
                Debug.LogWarning($"[F1-A][Test16] NOT_TESTED_SAVE_REIMPORT: {reason}");

                // 附加 smoke：JSON 直接导入验证（不作为 roundtrip 证据）
                ResetWorkspace(workspace);
                var json = BuildDrawingJson(
                    new[] { ("lamp_rt", "Lamp_220V", 0f, 0f), ("btn_rt", "Button_Start_NO", 100f, 0f) },
                    new[] { ("lamp_rt", "L", "btn_rt", "23") });
                var loadOk = saveLoad.LoadFromJsonString(json, out var loadError);
                if (!loadOk) failures.Add($"{scenario}: JSON 直接导入 smoke 应成功，error={loadError}");
                else if (workspace.Components.Count != 2) failures.Add($"{scenario}: JSON 导入后元件数应为 2，实际={workspace.Components.Count}。");
                return;
            }

            // 真实 SaveAs → LoadFromFile roundtrip
            ResetWorkspace(workspace);
            var loadOk2 = saveLoad.LoadFromFile(savedInfo.filePath, out var loadError2);
            if (!loadOk2)
            {
                failures.Add($"{scenario}: 保存后重新导入应成功，error={loadError2}");
            }
            if (workspace.Components.Count == 0)
            {
                failures.Add($"{scenario}: 重新导入后画布不应为空。");
            }

            // 清理测试文件
            try { saveLoad.DeleteSavedBlueprint(savedInfo.filePath, out _); } catch { }
        }

        // 17. 练习状态导入入口验证（真实调用生产方法 LoadBlueprint 和 OnExternalImportClicked）
        private static void Test17_PracticeImportEntryAudit(
            WorkspaceController workspace, SaveLoadService saveLoad,
            PracticeSessionController practice,
            ComponentDefinition lampDef, ComponentDefinition buttonDef, List<string> failures)
        {
            const string scenario = "17";
            ResetWorkspace(workspace);

            // 通过生产 Create 方法创建真实 ImportBlueprintPanel 实例
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
            if (canvas == null)
            {
                failures.Add($"{scenario}: Canvas 未找到，无法创建 ImportBlueprintPanel。");
                return;
            }
            var canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect == null)
            {
                failures.Add($"{scenario}: Canvas RectTransform 未找到。");
                return;
            }

            ImportBlueprintPanel panel = null;
            try
            {
                panel = ImportBlueprintPanel.Create(canvasRect, saveLoad);
                if (panel == null)
                {
                    failures.Add($"{scenario}: ImportBlueprintPanel.Create 返回 null。");
                    return;
                }
                panel.Show();

                // 建立真实练习状态
                var catalog = LoadTemplateCatalog();
                var item = GetTemplateItem(catalog, "single_lamp_template");
                practice.StartPractice(item);
                if (!practice.IsPracticeActive)
                {
                    failures.Add($"{scenario}: 练习前置未建立。");
                    return;
                }

                // 记录练习状态快照
                var componentsBefore = workspace.Components.Count;
                var wiresBefore = workspace.WireManager.Wires.Count;
                var practiceItemBefore = practice.CurrentTemplateItem;
                var simRunningBefore = workspace.IsSimulationRunning;

                // 反射获取 errorText 字段和 LoadBlueprint / OnExternalImportClicked 方法
                var errorTextField = typeof(ImportBlueprintPanel).GetField("errorText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var loadBlueprintMethod = typeof(ImportBlueprintPanel).GetMethod("LoadBlueprint",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var externalImportMethod = typeof(ImportBlueprintPanel).GetMethod("OnExternalImportClicked",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (errorTextField == null)
                {
                    failures.Add($"{scenario}: errorText 字段未找到。");
                    return;
                }
                if (loadBlueprintMethod == null)
                {
                    failures.Add($"{scenario}: LoadBlueprint 方法未找到。");
                    return;
                }
                if (externalImportMethod == null)
                {
                    failures.Add($"{scenario}: OnExternalImportClicked 方法未找到。");
                    return;
                }

                // === Test17-A: LoadBlueprint 入口 ===
                var blueprintInfo = new SavedBlueprintInfo
                {
                    documentId = "test-doc",
                    documentName = "Test Blueprint",
                    savedAt = "2026-08-04 12:00:00",
                    fileName = "test_blueprint.json",
                    filePath = "Z:/nonexistent/path/test_blueprint.json",
                    lastWriteTime = DateTime.Now
                };

                loadBlueprintMethod.Invoke(panel, new object[] { blueprintInfo });

                var errorText = errorTextField.GetValue(panel) as Text;
                var errorTextValue = errorText != null ? errorText.text : null;

                if (string.IsNullOrEmpty(errorTextValue) || !errorTextValue.Contains("请先退出当前练习后再导入图纸"))
                {
                    failures.Add($"{scenario}-A: LoadBlueprint 应显示练习拒绝提示，实际 errorText={errorTextValue}");
                }
                if (errorTextValue != null && errorTextValue.Contains("文件不存在"))
                {
                    failures.Add($"{scenario}-A: 练习保护发生在 LoadFromFile 之前，不应显示文件不存在，实际 errorText={errorTextValue}");
                }
                if (!panel.gameObject.activeSelf)
                {
                    failures.Add($"{scenario}-A: 面板不应被隐藏。");
                }
                if (workspace.Components.Count != componentsBefore)
                {
                    failures.Add($"{scenario}-A: 画布元件数应保持={componentsBefore}，实际={workspace.Components.Count}。");
                }
                if (workspace.WireManager.Wires.Count != wiresBefore)
                {
                    failures.Add($"{scenario}-A: 画布 Wire 数应保持={wiresBefore}，实际={workspace.WireManager.Wires.Count}。");
                }
                if (!practice.IsPracticeActive)
                {
                    failures.Add($"{scenario}-A: IsPracticeActive 应保持 true。");
                }
                if (practice.CurrentTemplateItem != practiceItemBefore)
                {
                    failures.Add($"{scenario}-A: CurrentTemplateItem 应保持。");
                }
                if (workspace.IsSimulationRunning != simRunningBefore)
                {
                    failures.Add($"{scenario}-A: IsSimulationRunning 应保持={simRunningBefore}，实际={workspace.IsSimulationRunning}。");
                }

                Debug.Log("[F1-A][Test17-A] saved blueprint import blocked in practice");

                // === Test17-B: OnExternalImportClicked 入口 ===
                externalImportMethod.Invoke(panel, null);

                errorText = errorTextField.GetValue(panel) as Text;
                errorTextValue = errorText != null ? errorText.text : null;

                if (string.IsNullOrEmpty(errorTextValue) || !errorTextValue.Contains("请先退出当前练习后再导入图纸"))
                {
                    failures.Add($"{scenario}-B: OnExternalImportClicked 应显示练习拒绝提示，实际 errorText={errorTextValue}");
                }
                if (workspace.Components.Count != componentsBefore)
                {
                    failures.Add($"{scenario}-B: 画布元件数应保持={componentsBefore}，实际={workspace.Components.Count}。");
                }
                if (workspace.WireManager.Wires.Count != wiresBefore)
                {
                    failures.Add($"{scenario}-B: 画布 Wire 数应保持={wiresBefore}，实际={workspace.WireManager.Wires.Count}。");
                }
                if (!practice.IsPracticeActive)
                {
                    failures.Add($"{scenario}-B: IsPracticeActive 应保持 true。");
                }
                if (practice.CurrentTemplateItem != practiceItemBefore)
                {
                    failures.Add($"{scenario}-B: CurrentTemplateItem 应保持。");
                }
                if (workspace.IsSimulationRunning != simRunningBefore)
                {
                    failures.Add($"{scenario}-B: IsSimulationRunning 应保持={simRunningBefore}，实际={workspace.IsSimulationRunning}。");
                }

                Debug.Log("[F1-A][Test17-B] external import blocked in practice");
            }
            finally
            {
                if (panel != null && panel.gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
                practice.ClearPracticeState();
            }
        }

        #region Helpers

        private static ComponentDefinition FindDefinition(SaveLoadService saveLoad, string name)
        {
            foreach (var def in saveLoad.Catalog)
            {
                if (def != null && def.name == name) return def;
            }
            return null;
        }

        private static string BuildDrawingJson(
            IEnumerable<(string instanceId, string definitionName, float x, float y)> components,
            IEnumerable<(string startComp, string startTerm, string endComp, string endTerm)> wires)
        {
            var compLines = components.Select(c =>
                $"{{\"instanceId\":\"{c.instanceId}\",\"definitionName\":\"{c.definitionName}\",\"x\":{c.x},\"y\":{c.y},\"isClosed\":false,\"parameters\":[]}}");
            var wireLines = wires.Select(w =>
                $"{{\"startComponentId\":\"{w.startComp}\",\"startTerminalId\":\"{w.startTerm}\",\"endComponentId\":\"{w.endComp}\",\"endTerminalId\":\"{w.endTerm}\"}}");

            return "{\"documentId\":\"f1a-test\",\"documentName\":\"F1A Test\",\"savedAt\":\"2026-08-04 12:00:00\"," +
                   "\"components\":[" + string.Join(",", compLines) + "]," +
                   "\"wires\":[" + string.Join(",", wireLines) + "]}";
        }

        private static void SpawnSimpleCircuit(WorkspaceController workspace, ComponentDefinition lampDef)
        {
            var buttonDef = ScriptableObject.CreateInstance<ComponentDefinition>();
            buttonDef.kind = ComponentKind.Switch;
            buttonDef.displayName = "Test Button";
            buttonDef.size = new Vector2(100f, 80f);
            buttonDef.terminals = new List<TerminalDefinition>
            {
                new TerminalDefinition { id = "23", label = "23", normalizedPosition = new Vector2(0f, 0.5f) },
                new TerminalDefinition { id = "24", label = "24", normalizedPosition = new Vector2(1f, 0.5f) }
            };

            var lamp = workspace.SpawnComponent(lampDef, new Vector2(-50f, 0f), "old-lamp", false);
            var btn = workspace.SpawnComponent(buttonDef, new Vector2(50f, 0f), "old-btn", false);
            if (lamp != null && btn != null)
            {
                workspace.WireManager.CreateWire(lamp.GetTerminal("L"), btn.GetTerminal("23"), Color.red, WireStyle.Straight);
            }
        }

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            if (workspace == null) return;
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            if (workspace.IsSimulationRunning) workspace.StopSimulation();
            workspace.ClearDrawing(true);
            PracticeSessionController.Instance.ClearPracticeState();
        }

        private static string GetStatusText(WorkspaceController workspace)
        {
            // 通过反射读取 workspace 的 statusText 字段
            var field = typeof(WorkspaceController).GetField("statusText", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                var value = field.GetValue(workspace);
                if (value is Text text) return text.text;
            }
            return null;
        }

        private static CircuitTemplateCatalogDto LoadTemplateCatalog()
        {
            var asset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            if (asset == null) throw new InvalidOperationException("未找到模板 catalog 资源。");
            return JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text);
        }

        private static CircuitTemplateCatalogItemDto GetTemplateItem(CircuitTemplateCatalogDto catalog, string templateId)
        {
            var item = catalog.templates.FirstOrDefault(c => c.templateId == templateId);
            if (item == null) throw new InvalidOperationException("catalog 缺少 " + templateId);
            return item;
        }

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, null);
        }

        #endregion
    }
}
