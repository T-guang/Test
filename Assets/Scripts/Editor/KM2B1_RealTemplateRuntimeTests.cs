using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B1.1: SimulationEngine 泛化提交前真实模板补验。
    ///
    /// 加载真实模板 motor_jog_continuous 和 motor_forward_reverse_double_interlock，
    /// 通过正式生产入口 Spawn 后执行真实 SimulationEngine 行为序列，
    /// 验证泛化不破坏真实模板的运行行为。
    ///
    /// 约束：
    /// - 不修改任何生产代码；
    /// - 只通过 CircuitTemplateLoader + CircuitTemplateSpawnService 加载真实模板；
    /// - 通过 SetClosed 模拟按钮操作，通过 SimulationEngine.Run 驱动仿真；
    /// - selfHoldEligibleContactors 是静态字典，只在场景开始时 ResetRuntimeState 一次，
    ///   后续步骤不重置，以模拟真实连续仿真行为。
    /// </summary>
    public static class KM2B1_RealTemplateRuntimeTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Tests/Run KM2B1 Real Template Runtime Tests")]
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
                    throw new InvalidOperationException("测试依赖缺失：WorkspaceController 或 SaveLoadService 未找到。");
                }

                // 测试 1: motor_jog_continuous 真实运行验证
                Test_MotorJogContinuous_Runtime(workspace, saveLoad, failures);

                // 测试 2: motor_forward_reverse_double_interlock 真实运行验证
                Test_MotorForwardReverseDoubleInterlock_Runtime(workspace, saveLoad, failures);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM2B1_RealTemplateRuntimeTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B1_RealTemplateRuntimeTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B1_RealTemplateRuntimeTests 通过 (2/2) ===");
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        private static CircuitTemplateCatalogDto LoadCatalog()
        {
            var asset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            if (asset == null) throw new InvalidOperationException("未找到模板 catalog 资源。");
            var catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text);
            if (catalog == null || catalog.templates == null || catalog.templates.Count == 0)
            {
                throw new InvalidOperationException("模板 catalog 为空或无效。");
            }
            return catalog;
        }

        private static CircuitTemplateCatalogItemDto GetItem(CircuitTemplateCatalogDto catalog, string templateId)
        {
            var item = catalog.templates.FirstOrDefault(c => c.templateId == templateId);
            if (item == null) throw new InvalidOperationException("catalog 缺少 " + templateId);
            return item;
        }

        private static bool LoadTemplateViaProductionApi(
            WorkspaceController workspace, SaveLoadService saveLoad, string templateId, out string error)
        {
            error = null;
            var catalog = LoadCatalog();
            var item = GetItem(catalog, templateId);

            if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var template, out error))
            {
                return false;
            }

            if (!CircuitTemplateSpawnService.TryValidate(template, saveLoad.Catalog, out error))
            {
                return false;
            }

            if (workspace.IsSimulationRunning) workspace.StopSimulation();
            workspace.ClearDrawing(true);
            SimulationEngine.ResetRuntimeState();

            if (!CircuitTemplateSpawnService.Spawn(template, workspace, saveLoad.Catalog, out error))
            {
                return false;
            }

            return true;
        }

        private static void CleanupWorkspace(WorkspaceController workspace)
        {
            if (workspace == null) return;
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            if (workspace.IsSimulationRunning) workspace.StopSimulation();
            workspace.ClearDrawing(true);
            SimulationEngine.ResetRuntimeState();
        }

        private static CircuitComponent FindComponent(WorkspaceController workspace, string instanceId)
        {
            return workspace.Components.FirstOrDefault(c => c.InstanceId == instanceId);
        }

        private static List<CircuitComponent> GetKMComponents(WorkspaceController workspace)
        {
            return workspace.Components
                .Where(c => c.Definition != null &&
                       (c.Definition.name == "Contactor_KM_220V" || c.Definition.name == "Contactor_KM_380V"))
                .ToList();
        }

        /// <summary>
        /// 运行一步仿真，不重置静态状态。
        /// </summary>
        private static void RunSimulationStep(WorkspaceController workspace)
        {
            var wires = workspace.WireManager?.Wires ?? new List<WireView>();
            var engine = new SimulationEngine(workspace.Components.ToList(), wires, 0f);
            engine.Run();
        }

        // =========================================================================
        // 测试 1: motor_jog_continuous 真实运行验证
        // =========================================================================

        private static void Test_MotorJogContinuous_Runtime(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "REAL-JOG-CONTINUOUS";
            const string templateId = "motor_jog_continuous";

            try
            {
                CleanupWorkspace(workspace);
                string error;
                if (!LoadTemplateViaProductionApi(workspace, saveLoad, templateId, out error))
                {
                    failures.Add($"{scenario}: 加载模板 {templateId} 失败: {error}");
                    return;
                }

                var km = FindComponent(workspace, "km_1");
                var stop = FindComponent(workspace, "stop_1");
                var jog = FindComponent(workspace, "jog_1");
                var start = FindComponent(workspace, "start_1");

                if (km == null || stop == null || jog == null || start == null)
                {
                    failures.Add($"{scenario}: 缺少必要元件 (km/stop/jog/start)");
                    return;
                }

                // 确认模板结构：jog_1 instanceId 包含 "jog"，走 instanceId 快速路径
                bool jogUsesInstanceIdFastPath = jog.InstanceId != null &&
                    jog.InstanceId.IndexOf("jog", StringComparison.OrdinalIgnoreCase) >= 0;

                Debug.Log($"[{scenario}] 模板结构: jog_1 instanceId='{jog.InstanceId}', " +
                    $"使用 instanceId 快速路径={jogUsesInstanceIdFastPath}");

                // 初始状态：所有按钮未按下，KM 应失电
                // stop_1 是 NC 按钮，初始 IsClosed=true (闭合)
                // jog_1 是复合按钮，初始 IsClosed=false (11/12 NC 闭合)
                // start_1 是 NO 按钮，初始 IsClosed=false (23/24 断开)
                RunSimulationStep(workspace);

                var kmInitialEnergized = km.IsEnergized;
                Debug.Log($"[{scenario}] step0 (initial): KM.IsEnergized={kmInitialEnergized}, " +
                    $"stop.IsClosed={stop.IsClosed}, jog.IsClosed={jog.IsClosed}, start.IsClosed={start.IsClosed}");

                if (kmInitialEnergized)
                {
                    failures.Add($"{scenario} step0: 初始状态 KM 不应得电");
                    return;
                }

                // 步骤 1: 按下连续启动按钮 start_1
                start.SetClosed(true);
                RunSimulationStep(workspace);
                var kmAfterStart = km.IsEnergized;
                Debug.Log($"[{scenario}] step1 (start pressed): KM.IsEnergized={kmAfterStart}");

                if (!kmAfterStart)
                {
                    failures.Add($"{scenario} step1: 按下启动后 KM 应得电");
                    return;
                }

                // 步骤 2: 释放启动按钮，KM 应通过 13/14 自锁保持
                start.SetClosed(false);
                RunSimulationStep(workspace);
                var kmAfterRelease = km.IsEnergized;
                Debug.Log($"[{scenario}] step2 (start released): KM.IsEnergized={kmAfterRelease} (应通过 13/14 自锁保持)");

                if (!kmAfterRelease)
                {
                    failures.Add($"{scenario} step2: 释放启动后 KM 应通过 13/14 自锁保持得电");
                    return;
                }

                // 步骤 3: 按下停止按钮，KM 应失电
                stop.SetClosed(false);
                RunSimulationStep(workspace);
                var kmAfterStop = km.IsEnergized;
                Debug.Log($"[{scenario}] step3 (stop pressed): KM.IsEnergized={kmAfterStop} (应失电)");

                if (kmAfterStop)
                {
                    failures.Add($"{scenario} step3: 按下停止后 KM 应失电");
                    return;
                }

                // 步骤 4: 恢复停止按钮，按下点动按钮 jog_1
                stop.SetClosed(true);
                jog.SetClosed(true);
                RunSimulationStep(workspace);
                var kmAfterJogPress = km.IsEnergized;
                Debug.Log($"[{scenario}] step4 (jog pressed): KM.IsEnergized={kmAfterJogPress} (点动按下应得电)");

                if (!kmAfterJogPress)
                {
                    failures.Add($"{scenario} step4: 按下点动后 KM 应得电");
                    return;
                }

                // 步骤 5: 释放点动按钮，KM 应失电（NC 11/12 断开自锁路径）
                jog.SetClosed(false);
                RunSimulationStep(workspace);
                var kmAfterJogRelease = km.IsEnergized;
                Debug.Log($"[{scenario}] step5 (jog released): KM.IsEnergized={kmAfterJogRelease} (点动释放应失电)");

                if (kmAfterJogRelease)
                {
                    failures.Add($"{scenario} step5: 释放点动后 KM 应失电，不应因 13/14 自锁保持");
                    return;
                }

                // 记录最终状态
                Debug.Log($"[{scenario}] 最终状态: KM.IsEnergized={km.IsEnergized}");
                Debug.Log($"[{scenario}] 13/14 自锁路径验证: 连续启动→释放保持→停止失电 = PASS");
                Debug.Log($"[{scenario}] 点动路径验证: 点动按下→得电, 释放→失电 = PASS");

                // 说明：该模板使用 13/14 自锁和 instanceId="jog_1" 快速路径识别点动，
                // 不经过 IsJogStartButtonForContactor 的 NO pair 遍历路径。
                // 因此 33/34 泛化对该模板无影响，记录 NOT_APPLICABLE。
                Debug.Log($"[{scenario}] IsJogStartButtonForContactor NO pair 泛化影响: NOT_APPLICABLE " +
                    "(jog_1 instanceId 包含 'jog'，走快速路径，不经过 NO pair 遍历)");

                Debug.Log($"[PASS] {scenario}: 真实模板 motor_jog_continuous 运行行为验证通过");
            }
            catch (Exception e)
            {
                failures.Add($"{scenario} 异常: {e.Message}");
            }
        }

        // =========================================================================
        // 测试 2: motor_forward_reverse_double_interlock 真实运行验证
        // =========================================================================

        private static void Test_MotorForwardReverseDoubleInterlock_Runtime(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "REAL-DOUBLE-INTERLOCK";
            const string templateId = "motor_forward_reverse_double_interlock";

            try
            {
                CleanupWorkspace(workspace);
                string error;
                if (!LoadTemplateViaProductionApi(workspace, saveLoad, templateId, out error))
                {
                    failures.Add($"{scenario}: 加载模板 {templateId} 失败: {error}");
                    return;
                }

                var kmForward = FindComponent(workspace, "km_forward");
                var kmReverse = FindComponent(workspace, "km_reverse");
                var stop = FindComponent(workspace, "stop_1");
                var forwardButton = FindComponent(workspace, "forward_button_1");
                var reverseButton = FindComponent(workspace, "reverse_button_1");

                if (kmForward == null || kmReverse == null || stop == null ||
                    forwardButton == null || reverseButton == null)
                {
                    failures.Add($"{scenario}: 缺少必要元件 (km_forward/km_reverse/stop/forward_button/reverse_button)");
                    return;
                }

                // 初始状态：KM_F=off, KM_R=off
                RunSimulationStep(workspace);
                Debug.Log($"[{scenario}] step0 (initial): KM_F.IsEnergized={kmForward.IsEnergized}, " +
                    $"KM_R.IsEnergized={kmReverse.IsEnergized}");

                if (kmForward.IsEnergized || kmReverse.IsEnergized)
                {
                    failures.Add($"{scenario} step0: 初始状态 KM_F 和 KM_R 均不应得电");
                    return;
                }

                // 步骤 1: 正转启动
                forwardButton.SetClosed(true);
                RunSimulationStep(workspace);
                var kmFAfterForward = kmForward.IsEnergized;
                var kmRAfterForward = kmReverse.IsEnergized;
                Debug.Log($"[{scenario}] step1 (forward pressed): " +
                    $"KM_F.IsEnergized={kmFAfterForward}, KM_R.IsEnergized={kmRAfterForward}");

                if (!kmFAfterForward)
                {
                    failures.Add($"{scenario} step1: 正转启动后 KM_F 应得电");
                    return;
                }
                if (kmRAfterForward)
                {
                    failures.Add($"{scenario} step1: 正转启动后 KM_R 不应得电 (互锁)");
                    return;
                }

                // 步骤 2: 释放正转按钮，KM_F 应通过 13/14 自锁保持
                forwardButton.SetClosed(false);
                RunSimulationStep(workspace);
                var kmFAfterRelease = kmForward.IsEnergized;
                Debug.Log($"[{scenario}] step2 (forward released): " +
                    $"KM_F.IsEnergized={kmFAfterRelease} (应通过 13/14 自锁保持)");

                if (!kmFAfterRelease)
                {
                    failures.Add($"{scenario} step2: 释放正转后 KM_F 应通过 13/14 自锁保持");
                    return;
                }

                // 步骤 3: 按下停止，KM_F 应失电
                stop.SetClosed(false);
                RunSimulationStep(workspace);
                var kmFAfterStop = kmForward.IsEnergized;
                Debug.Log($"[{scenario}] step3 (stop pressed): KM_F.IsEnergized={kmFAfterStop} (应失电)");

                if (kmFAfterStop)
                {
                    failures.Add($"{scenario} step3: 停止后 KM_F 应失电");
                    return;
                }

                // 步骤 4: 恢复停止，反转启动
                stop.SetClosed(true);
                reverseButton.SetClosed(true);
                RunSimulationStep(workspace);
                var kmFAfterReverse = kmForward.IsEnergized;
                var kmRAfterReverse = kmReverse.IsEnergized;
                Debug.Log($"[{scenario}] step4 (reverse pressed): " +
                    $"KM_F.IsEnergized={kmFAfterReverse}, KM_R.IsEnergized={kmRAfterReverse}");

                if (!kmRAfterReverse)
                {
                    failures.Add($"{scenario} step4: 反转启动后 KM_R 应得电");
                    return;
                }
                if (kmFAfterReverse)
                {
                    failures.Add($"{scenario} step4: 反转启动后 KM_F 不应得电 (互锁)");
                    return;
                }

                // 步骤 5: 释放反转按钮，KM_R 应通过 13/14 自锁保持
                reverseButton.SetClosed(false);
                RunSimulationStep(workspace);
                var kmRAfterReverseRelease = kmReverse.IsEnergized;
                Debug.Log($"[{scenario}] step5 (reverse released): " +
                    $"KM_R.IsEnergized={kmRAfterReverseRelease} (应通过 13/14 自锁保持)");

                if (!kmRAfterReverseRelease)
                {
                    failures.Add($"{scenario} step5: 释放反转后 KM_R 应通过 13/14 自锁保持");
                    return;
                }

                // 步骤 6: 停止
                stop.SetClosed(false);
                RunSimulationStep(workspace);
                var kmRAfterStop = kmReverse.IsEnergized;
                Debug.Log($"[{scenario}] step6 (stop pressed): KM_R.IsEnergized={kmRAfterStop} (应失电)");

                if (kmRAfterStop)
                {
                    failures.Add($"{scenario} step6: 停止后 KM_R 应失电");
                    return;
                }

                // 步骤 7: 冲突场景 - 同时按下正转和反转
                stop.SetClosed(true);
                forwardButton.SetClosed(true);
                reverseButton.SetClosed(true);
                RunSimulationStep(workspace);
                var kmFConflict = kmForward.IsEnergized;
                var kmRConflict = kmReverse.IsEnergized;
                Debug.Log($"[{scenario}] step7 (conflict - both pressed): " +
                    $"KM_F.IsEnergized={kmFConflict}, KM_R.IsEnergized={kmRConflict}");

                if (kmFConflict && kmRConflict)
                {
                    failures.Add($"{scenario} step7: 冲突场景 KM_F 和 KM_R 不应同时得电 (双重互锁保护失败)");
                    return;
                }

                // 记录最终状态
                Debug.Log($"[{scenario}] 最终状态: KM_F.IsEnergized={kmForward.IsEnergized}, " +
                    $"KM_R.IsEnergized={kmReverse.IsEnergized}");
                Debug.Log($"[{scenario}] 正转启动→自锁→停止 = PASS");
                Debug.Log($"[{scenario}] 反转启动→自锁→停止 = PASS");
                Debug.Log($"[{scenario}] 冲突场景互锁保护 = PASS (不同时得电)");
                Debug.Log($"[{scenario}] NC 21/22 互锁泛化影响: 行为零变化 (Schema 只有 21/22 一组 NC)");

                Debug.Log($"[PASS] {scenario}: 真实模板 motor_forward_reverse_double_interlock 运行行为验证通过");
            }
            catch (Exception e)
            {
                failures.Add($"{scenario} 异常: {e.Message}");
            }
        }
    }
}
