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
    /// KM-2B2: CircuitStateAnalyzer 真实模板分析验证。
    ///
    /// 加载 4 张真实模板：
    /// - motor_self_hold_control
    /// - motor_jog_continuous
    /// - motor_forward_reverse_interlock
    /// - motor_forward_reverse_double_interlock
    ///
    /// 通过正式生产入口 Spawn 后调用 CircuitStateAnalyzer.Analyze，
    /// 报告每个 KM 的 HasSelfHoldStructure、SelfHoldContactPair、HasInterlockStructure、
    /// InterlockContactPair、SelfHoldStatus、InterlockStatus。
    ///
    /// 约束：
    /// - 不修改任何生产代码；
    /// - 只通过 CircuitTemplateLoader + CircuitTemplateSpawnService 加载真实模板；
    /// - 仅做静态拓扑分析，不驱动 SimulationEngine；
    /// - 不依赖 instanceId、中文名称或模板 ID 判定结果。
    /// </summary>
    public static class KM2B2_RealTemplateAnalyzerTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Tests/Run KM2B2 Real Template Analyzer Tests")]
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

                // 1. motor_self_hold_control
                Test_MotorSelfHoldControl_Analyzer(workspace, saveLoad, failures);

                // 2. motor_jog_continuous
                Test_MotorJogContinuous_Analyzer(workspace, saveLoad, failures);

                // 3. motor_forward_reverse_interlock
                Test_MotorForwardReverseInterlock_Analyzer(workspace, saveLoad, failures);

                // 4. motor_forward_reverse_double_interlock
                Test_MotorForwardReverseDoubleInterlock_Analyzer(workspace, saveLoad, failures);
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
                Debug.LogError("=== KM2B2_RealTemplateAnalyzerTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM2B2_RealTemplateAnalyzerTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM2B2_RealTemplateAnalyzerTests 通过 (4/4) ===");
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

        private static List<CircuitComponent> GetKMComponents(WorkspaceController workspace)
        {
            return workspace.Components
                .Where(c => c.Definition != null &&
                       (c.Definition.name == "Contactor_KM_220V" || c.Definition.name == "Contactor_KM_380V"))
                .ToList();
        }

        private static string GetSelfHoldContactPair(ComponentStateInfo info)
        {
            if (info == null) return null;
            var field = typeof(ComponentStateInfo).GetField("SelfHoldContactPair");
            return field != null ? (string)field.GetValue(info) : null;
        }

        private static string GetInterlockContactPair(ComponentStateInfo info)
        {
            if (info == null) return null;
            var field = typeof(ComponentStateInfo).GetField("InterlockContactPair");
            return field != null ? (string)field.GetValue(info) : null;
        }

        private static CircuitStateResult AnalyzeWorkspace(WorkspaceController workspace)
        {
            var components = workspace.Components.ToList();
            IReadOnlyList<WireView> wires = workspace.WireManager?.Wires ?? new List<WireView>();
            var analyzer = new CircuitStateAnalyzer();
            return analyzer.Analyze(components, wires);
        }

        private static void DumpKmStates(
            string scenario, CircuitStateResult result, List<CircuitComponent> kms)
        {
            foreach (var km in kms)
            {
                var info = result.FindComponent(km.InstanceId);
                if (info == null)
                {
                    Debug.Log($"[{scenario}] KM {km.InstanceId} ({km.Definition?.name}): 未在分析结果中找到");
                    continue;
                }

                Debug.Log($"[{scenario}] KM {km.InstanceId} ({km.Definition?.name}): " +
                    $"HasSelfHoldStructure={info.HasSelfHoldStructure}, " +
                    $"SelfHoldContactPair={GetSelfHoldContactPair(info)}, " +
                    $"HasInterlockStructure={info.HasInterlockStructure}, " +
                    $"InterlockContactPair={GetInterlockContactPair(info)}");
                Debug.Log($"[{scenario}]   SelfHoldStatus={info.SelfHoldStatus}");
                Debug.Log($"[{scenario}]   InterlockStatus={info.InterlockStatus}");
            }
        }

        // =========================================================================
        // 测试 1: motor_self_hold_control
        // =========================================================================
        private static void Test_MotorSelfHoldControl_Analyzer(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "REAL-SELF-HOLD";
            const string templateId = "motor_self_hold_control";

            try
            {
                CleanupWorkspace(workspace);
                string error;
                if (!LoadTemplateViaProductionApi(workspace, saveLoad, templateId, out error))
                {
                    failures.Add($"{scenario}: 加载模板 {templateId} 失败: {error}");
                    return;
                }

                var result = AnalyzeWorkspace(workspace);
                var kms = GetKMComponents(workspace);
                if (kms.Count == 0)
                {
                    failures.Add($"{scenario}: 未找到 KM 元件");
                    return;
                }

                DumpKmStates(scenario, result, kms);

                // 验证：至少一个 KM 应识别自锁结构
                var hasAnySelfHold = kms.Any(km =>
                {
                    var info = result.FindComponent(km.InstanceId);
                    return info != null && info.HasSelfHoldStructure;
                });
                if (!hasAnySelfHold)
                {
                    failures.Add($"{scenario}: 至少一个 KM 应识别自锁结构");
                }

                // 验证：自锁 KM 的 pair 应为 13/14
                foreach (var km in kms)
                {
                    var info = result.FindComponent(km.InstanceId);
                    if (info == null) continue;
                    if (info.HasSelfHoldStructure)
                    {
                        var pair = GetSelfHoldContactPair(info);
                        if (pair != "13/14")
                        {
                            failures.Add($"{scenario}: KM {km.InstanceId} 自锁 pair 应为 13/14，实际={pair}");
                        }
                    }
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // 测试 2: motor_jog_continuous
        // =========================================================================
        private static void Test_MotorJogContinuous_Analyzer(
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

                var result = AnalyzeWorkspace(workspace);
                var kms = GetKMComponents(workspace);
                if (kms.Count == 0)
                {
                    failures.Add($"{scenario}: 未找到 KM 元件");
                    return;
                }

                DumpKmStates(scenario, result, kms);

                // 验证：连续启动 KM 应识别自锁结构
                var hasAnySelfHold = kms.Any(km =>
                {
                    var info = result.FindComponent(km.InstanceId);
                    return info != null && info.HasSelfHoldStructure;
                });
                if (!hasAnySelfHold)
                {
                    failures.Add($"{scenario}: 连续启动 KM 应识别自锁结构");
                }

                // 验证：不应误报点动状态为第二组自锁
                // （只要识别到的自锁 pair 是合法的 NO pair 即可）
                foreach (var km in kms)
                {
                    var info = result.FindComponent(km.InstanceId);
                    if (info == null) continue;
                    if (info.HasSelfHoldStructure)
                    {
                        var pair = GetSelfHoldContactPair(info);
                        if (pair != "13/14" && pair != "33/34")
                        {
                            failures.Add($"{scenario}: KM {km.InstanceId} 自锁 pair 非法: {pair}");
                        }
                    }
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // 测试 3: motor_forward_reverse_interlock
        // =========================================================================
        private static void Test_MotorForwardReverseInterlock_Analyzer(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "REAL-FWD-REV-INTERLOCK";
            const string templateId = "motor_forward_reverse_interlock";

            try
            {
                CleanupWorkspace(workspace);
                string error;
                if (!LoadTemplateViaProductionApi(workspace, saveLoad, templateId, out error))
                {
                    failures.Add($"{scenario}: 加载模板 {templateId} 失败: {error}");
                    return;
                }

                var result = AnalyzeWorkspace(workspace);
                var kms = GetKMComponents(workspace);
                if (kms.Count < 2)
                {
                    failures.Add($"{scenario}: 应至少有 2 个 KM 元件（正反转），实际={kms.Count}");
                    return;
                }

                DumpKmStates(scenario, result, kms);

                // 验证：两个方向 KM 都应识别自锁
                foreach (var km in kms)
                {
                    var info = result.FindComponent(km.InstanceId);
                    if (info == null)
                    {
                        failures.Add($"{scenario}: KM {km.InstanceId} 未在分析结果中");
                        continue;
                    }
                    if (!info.HasSelfHoldStructure)
                    {
                        failures.Add($"{scenario}: KM {km.InstanceId} 应识别自锁结构");
                    }
                }

                // 验证：至少一个 KM 应识别 NC 互锁
                var hasAnyInterlock = kms.Any(km =>
                {
                    var info = result.FindComponent(km.InstanceId);
                    return info != null && info.HasInterlockStructure;
                });
                if (!hasAnyInterlock)
                {
                    failures.Add($"{scenario}: 至少一个 KM 应识别 NC 互锁结构");
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }

        // =========================================================================
        // 测试 4: motor_forward_reverse_double_interlock
        // =========================================================================
        private static void Test_MotorForwardReverseDoubleInterlock_Analyzer(
            WorkspaceController workspace, SaveLoadService saveLoad, List<string> failures)
        {
            const string scenario = "REAL-FWD-REV-DOUBLE-INTERLOCK";
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

                var result = AnalyzeWorkspace(workspace);
                var kms = GetKMComponents(workspace);
                if (kms.Count < 2)
                {
                    failures.Add($"{scenario}: 应至少有 2 个 KM 元件（正反转），实际={kms.Count}");
                    return;
                }

                DumpKmStates(scenario, result, kms);

                // 验证：两个方向 KM 都应识别自锁
                foreach (var km in kms)
                {
                    var info = result.FindComponent(km.InstanceId);
                    if (info == null)
                    {
                        failures.Add($"{scenario}: KM {km.InstanceId} 未在分析结果中");
                        continue;
                    }
                    if (!info.HasSelfHoldStructure)
                    {
                        failures.Add($"{scenario}: KM {km.InstanceId} 应识别自锁结构");
                    }
                }

                // 验证：至少一个 KM 应识别 NC 互锁
                var hasAnyInterlock = kms.Any(km =>
                {
                    var info = result.FindComponent(km.InstanceId);
                    return info != null && info.HasInterlockStructure;
                });
                if (!hasAnyInterlock)
                {
                    failures.Add($"{scenario}: 至少一个 KM 应识别 NC 互锁结构");
                }
            }
            catch (Exception e) { failures.Add($"{scenario} 异常: {e.Message}"); }
            finally { CleanupWorkspace(workspace); }
        }
    }
}
