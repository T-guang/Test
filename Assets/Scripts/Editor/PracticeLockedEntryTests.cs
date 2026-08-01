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
    /// E3 锁定画布进入练习保护测试。验证画布锁定状态下从图纸集进入练习时：
    /// 立即拒绝、不弹确认框、不读取模板、不清空画布、不建立会话、不切换页面、
    /// 不显示参考图纸、不调用成功回调、状态栏显示明确锁定提示。
    ///
    /// 测试覆盖 A-G 七组：A 锁定+非空、B 锁定+空、C 已有练习A+锁定进入B、
    /// D 确认期间锁定、E 未锁定+空控制组、F 未锁定+非空控制组、G 用户取消确认控制组。
    /// 所有断言失败时抛 InvalidOperationException，batchmode 非零退出。
    /// </summary>
    public static class PracticeLockedEntryTests
    {
        private const string LockedEntryMessage = "画布已锁定，请先解锁后再进入练习模式。";
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string FamilyTemplateIdA = "single_lamp_template";
        private const string FamilyTemplateIdB = "double_control_lamp_template";

        [MenuItem("Tools/Tests/Run Practice Locked Entry Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                if (workspace == null)
                {
                    throw new InvalidOperationException("未找到 WorkspaceController，无法执行 E3 锁定进入练习测试。");
                }

                var referencePanel = UnityEngine.Object.FindObjectOfType<BlueprintReferencePanel>(true);
                var controller = PracticeSessionController.Instance;
                InvokeEnsureReferences(controller);

                var catalogAsset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
                if (catalogAsset == null) throw new InvalidOperationException("未找到模板 catalog 资源。");
                var catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(catalogAsset.text);
                if (catalog == null || catalog.templates == null || catalog.templates.Count == 0)
                    throw new InvalidOperationException("模板 catalog 为空或无效。");
                var itemA = catalog.templates.FirstOrDefault(c => c.templateId == FamilyTemplateIdA);
                var itemB = catalog.templates.FirstOrDefault(c => c.templateId == FamilyTemplateIdB);
                if (itemA == null) throw new InvalidOperationException("catalog 缺少 " + FamilyTemplateIdA);
                if (itemB == null) throw new InvalidOperationException("catalog 缺少 " + FamilyTemplateIdB);

                TestA_LockedNonEmptyCanvas(workspace, controller, referencePanel, itemA, failures);
                TestB_LockedEmptyCanvas(workspace, controller, referencePanel, itemA, failures);
                TestC_ExistingPracticeLockedEntryB(workspace, controller, referencePanel, itemA, itemB, failures);
                TestD_LockedDuringConfirm(workspace, controller, referencePanel, itemA, failures);
                TestE_UnlockedEmptyCanvas(workspace, controller, referencePanel, itemA, failures);
                TestF_UnlockedNonEmptyCanvas(workspace, controller, referencePanel, itemA, failures);
                TestG_UserCancelConfirm(workspace, controller, referencePanel, itemA, failures);
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
                throw new InvalidOperationException("E3 锁定画布进入练习保护测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Practice][E3] 锁定画布进入练习保护：通过");
        }

        // A: 锁定 + 非空画布
        private static void TestA_LockedNonEmptyCanvas(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            var compA = SpawnTestComponent(workspace, "SwitchA", "switch-A-001");
            var compB = SpawnTestComponent(workspace, "SwitchB", "switch-B-002");
            if (compA == null || compB == null) { failures.Add("A: 无法创建测试组件。"); return; }
            var wire = CreateTestWire(workspace, compA, "T1", compB, "T1");
            if (wire == null) { failures.Add("A: 无法创建测试 Wire。"); return; }

            var componentCountBefore = workspace.Components.Count;
            var wireCountBefore = workspace.WireManager.Wires.Count;
            var instanceIdsBefore = workspace.Components.Select(c => c.InstanceId).ToList();
            var wireStartId = wire.StartTerminal != null ? wire.StartTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId : null;
            var wireEndId = wire.EndTerminal != null ? wire.EndTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId : null;
            var refActiveBefore = referencePanel != null && referencePanel.gameObject.activeSelf;

            workspace.ToggleInteractionLock();
            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);

            if (workspace.Components.Count != componentCountBefore) failures.Add("A: 组件数量应不变，before=" + componentCountBefore + " after=" + workspace.Components.Count);
            if (workspace.WireManager.Wires.Count != wireCountBefore) failures.Add("A: Wire 数量应不变，before=" + wireCountBefore + " after=" + workspace.WireManager.Wires.Count);
            var instanceIdsAfter = workspace.Components.Select(c => c.InstanceId).ToList();
            if (!instanceIdsBefore.SequenceEqual(instanceIdsAfter)) failures.Add("A: InstanceId 列表应不变。");
            if (workspace.WireManager.Wires.Count > 0)
            {
                var w = workspace.WireManager.Wires[0];
                var startAfter = w.StartTerminal != null ? w.StartTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId : null;
                var endAfter = w.EndTerminal != null ? w.EndTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId : null;
                if (startAfter != wireStartId || endAfter != wireEndId) failures.Add("A: Wire 端点应不变。");
            }
            if (!workspace.IsInteractionLocked) failures.Add("A: 画布应仍为锁定。");
            if (controller.IsPracticeActive) failures.Add("A: IsPracticeActive 应为 false。");
            if (controller.CurrentTemplateItem != null) failures.Add("A: CurrentTemplateItem 应为 null。");
            if (controller.CurrentTemplateData != null) failures.Add("A: CurrentTemplateData 应为 null。");
            if (referencePanel != null && referencePanel.gameObject.activeSelf != refActiveBefore) failures.Add("A: 参考图纸显示状态应不变。");
            if (PracticeConfirmDialogExists()) failures.Add("A: 不应创建确认框。");
            if (callbackCount != 0) failures.Add("A: 成功回调调用次数应为 0，实际=" + callbackCount);
            var status = GetStatusText(workspace);
            if (status == null || !status.Contains(LockedEntryMessage)) failures.Add("A: 状态栏应包含锁定提示，实际=" + status);
        }

        // B: 锁定 + 空画布
        private static void TestB_LockedEmptyCanvas(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            var refActiveBefore = referencePanel != null && referencePanel.gameObject.activeSelf;
            workspace.ToggleInteractionLock();
            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);

            if (workspace.Components.Count != 0) failures.Add("B: 组件数量应为 0。");
            if (workspace.WireManager.Wires.Count != 0) failures.Add("B: Wire 数量应为 0。");
            if (!workspace.IsInteractionLocked) failures.Add("B: 画布应仍为锁定。");
            if (controller.IsPracticeActive) failures.Add("B: IsPracticeActive 应为 false。");
            if (controller.CurrentTemplateItem != null) failures.Add("B: CurrentTemplateItem 应为 null。");
            if (controller.CurrentTemplateData != null) failures.Add("B: CurrentTemplateData 应为 null。");
            if (referencePanel != null && referencePanel.gameObject.activeSelf != refActiveBefore) failures.Add("B: 参考图纸显示状态应不变。");
            if (PracticeConfirmDialogExists()) failures.Add("B: 不应创建确认框。");
            if (callbackCount != 0) failures.Add("B: 成功回调应为 0。");
            var status = GetStatusText(workspace);
            if (status == null || !status.Contains(LockedEntryMessage)) failures.Add("B: 状态栏应包含锁定提示。");
        }

        // C: 已有练习 A + 锁定后尝试进入 B
        private static void TestC_ExistingPracticeLockedEntryB(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            int callbackA = 0;
            controller.StartPractice(itemA, () => callbackA++);
            if (!controller.IsPracticeActive) { failures.Add("C: 练习 A 应成功进入。"); return; }
            if (controller.CurrentTemplateItem != itemA) { failures.Add("C: CurrentTemplateItem 应为 itemA。"); return; }
            var templateDataA = controller.CurrentTemplateData;
            var refActiveBeforeB = referencePanel != null && referencePanel.gameObject.activeSelf;

            workspace.ToggleInteractionLock();
            int callbackB = 0;
            controller.StartPractice(itemB, () => callbackB++);

            if (!controller.IsPracticeActive) failures.Add("C: IsPracticeActive 应仍为 true（练习 A 仍存在）。");
            if (controller.CurrentTemplateItem != itemA) failures.Add("C: CurrentTemplateItem 应仍为 itemA。");
            if (controller.CurrentTemplateData != templateDataA) failures.Add("C: CurrentTemplateData 应未变。");
            if (referencePanel != null && referencePanel.gameObject.activeSelf != refActiveBeforeB) failures.Add("C: 参考图纸应仍为 A。");
            if (callbackB != 0) failures.Add("C: B 的成功回调应为 0。");
            if (callbackA != 1) failures.Add("C: A 的成功回调应为 1（控制组验证）。");
            var status = GetStatusText(workspace);
            if (status == null || !status.Contains(LockedEntryMessage)) failures.Add("C: 状态栏应包含锁定提示。");
        }

        // D: 确认期间锁定（反射验证 EnterPracticeMode 二次检查）
        private static void TestD_LockedDuringConfirm(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            var comp = SpawnTestComponent(workspace, "SwitchD", "switch-D-001");
            if (comp == null) { failures.Add("D: 无法创建测试组件。"); return; }
            var componentCountBefore = workspace.Components.Count;

            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);
            if (!PracticeConfirmDialogExists()) { failures.Add("D: 应先出现确认框。"); return; }

            // 确认期间锁定
            workspace.ToggleInteractionLock();
            ClickConfirmButton();

            if (workspace.Components.Count != componentCountBefore) failures.Add("D: 原画布应保持，组件数量 before=" + componentCountBefore + " after=" + workspace.Components.Count);
            if (controller.IsPracticeActive) failures.Add("D: IsPracticeActive 应为 false（二次检查拒绝）。");
            if (controller.CurrentTemplateItem != null) failures.Add("D: CurrentTemplateItem 应为 null。");
            if (controller.CurrentTemplateData != null) failures.Add("D: CurrentTemplateData 应为 null。");
            if (referencePanel != null && referencePanel.gameObject.activeSelf) failures.Add("D: 参考图纸不应显示。");
            if (callbackCount != 0) failures.Add("D: 成功回调应为 0。");
            var status = GetStatusText(workspace);
            if (status == null || !status.Contains(LockedEntryMessage)) failures.Add("D: 状态栏应包含锁定提示（二次检查）。");
        }

        // E: 未锁定 + 空画布控制组
        private static void TestE_UnlockedEmptyCanvas(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);

            if (!controller.IsPracticeActive) failures.Add("E: IsPracticeActive 应为 true。");
            if (controller.CurrentTemplateItem != item) failures.Add("E: CurrentTemplateItem 应正确。");
            if (controller.CurrentTemplateData == null) failures.Add("E: CurrentTemplateData 应非空。");
            if (referencePanel != null && !referencePanel.gameObject.activeSelf) failures.Add("E: 参考图纸应显示。");
            if (callbackCount != 1) failures.Add("E: 成功回调应恰好一次，实际=" + callbackCount);
        }

        // F: 未锁定 + 非空画布控制组
        private static void TestF_UnlockedNonEmptyCanvas(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            var comp = SpawnTestComponent(workspace, "SwitchF", "switch-F-001");
            if (comp == null) { failures.Add("F: 无法创建测试组件。"); return; }
            var componentCountBefore = workspace.Components.Count;

            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);
            if (!PracticeConfirmDialogExists()) { failures.Add("F: 应出现确认框。"); return; }
            ClickConfirmButton();

            if (workspace.Components.Count != 0) failures.Add("F: 原画布应被清空，实际 count=" + workspace.Components.Count);
            if (!controller.IsPracticeActive) failures.Add("F: IsPracticeActive 应为 true。");
            if (controller.CurrentTemplateItem != item) failures.Add("F: CurrentTemplateItem 应正确。");
            if (controller.CurrentTemplateData == null) failures.Add("F: CurrentTemplateData 应非空。");
            if (referencePanel != null && !referencePanel.gameObject.activeSelf) failures.Add("F: 参考图纸应显示。");
            if (callbackCount != 1) failures.Add("F: 成功回调应恰好一次，实际=" + callbackCount);
        }

        // G: 用户取消确认控制组
        private static void TestG_UserCancelConfirm(WorkspaceController workspace, PracticeSessionController controller, BlueprintReferencePanel referencePanel, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            CleanupWorkspace(workspace, controller);
            var comp = SpawnTestComponent(workspace, "SwitchG", "switch-G-001");
            if (comp == null) { failures.Add("G: 无法创建测试组件。"); return; }
            var componentCountBefore = workspace.Components.Count;
            var refActiveBefore = referencePanel != null && referencePanel.gameObject.activeSelf;

            int callbackCount = 0;
            controller.StartPractice(item, () => callbackCount++);
            if (!PracticeConfirmDialogExists()) { failures.Add("G: 应出现确认框。"); return; }
            ClickCancelButton();

            if (workspace.Components.Count != componentCountBefore) failures.Add("G: 画布应保持，组件数量 before=" + componentCountBefore + " after=" + workspace.Components.Count);
            if (controller.IsPracticeActive) failures.Add("G: IsPracticeActive 应为 false。");
            if (controller.CurrentTemplateItem != null) failures.Add("G: CurrentTemplateItem 应为 null。");
            if (controller.CurrentTemplateData != null) failures.Add("G: CurrentTemplateData 应为 null。");
            if (referencePanel != null && referencePanel.gameObject.activeSelf != refActiveBefore) failures.Add("G: 参考图纸显示状态应不变。");
            if (PracticeConfirmDialogExists()) failures.Add("G: 确认框应已销毁。");
            if (callbackCount != 0) failures.Add("G: 成功回调应为 0。");
        }

        // --- 辅助方法 ---

        private static void CleanupWorkspace(WorkspaceController workspace, PracticeSessionController controller)
        {
            if (workspace == null) return;
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            workspace.ClearDrawing(true);
            controller.ClearPracticeState();
            var dialog = GameObject.Find("PracticeConfirmDialog");
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static CircuitComponent SpawnTestComponent(WorkspaceController workspace, string displayName, string instanceId)
        {
            var def = ScriptableObject.CreateInstance<ComponentDefinition>();
            def.displayName = displayName;
            def.kind = ComponentKind.Switch;
            def.size = new Vector2(110f, 130f);
            def.terminals = new List<TerminalDefinition>
            {
                new TerminalDefinition { id = "T1", label = "T1", normalizedPosition = new Vector2(0.5f, 0f) },
                new TerminalDefinition { id = "T2", label = "T2", normalizedPosition = new Vector2(0.5f, 1f) }
            };
            return workspace.SpawnComponent(def, Vector2.zero, instanceId, false);
        }

        private static WireView CreateTestWire(WorkspaceController workspace, CircuitComponent compA, string terminalAId, CircuitComponent compB, string terminalBId)
        {
            if (workspace.WireManager == null) return null;
            var termA = compA.GetTerminal(terminalAId);
            var termB = compB.GetTerminal(terminalBId);
            if (termA == null || termB == null) return null;
            return workspace.WireManager.CreateWire(termA, termB, Color.yellow, WireStyle.Straight);
        }

        private static string GetStatusText(WorkspaceController workspace)
        {
            var statusField = typeof(WorkspaceController).GetField("statusText", BindingFlags.NonPublic | BindingFlags.Instance);
            if (statusField == null) return null;
            var statusText = statusField.GetValue(workspace) as Text;
            return statusText != null ? statusText.text : null;
        }

        private static bool PracticeConfirmDialogExists()
        {
            return GameObject.Find("PracticeConfirmDialog") != null;
        }

        private static void ClickConfirmButton()
        {
            var dialog = GameObject.Find("PracticeConfirmDialog");
            if (dialog == null) return;
            var confirmBtn = dialog.transform.Find("Panel/ConfirmButton")?.GetComponent<Button>();
            if (confirmBtn != null) confirmBtn.onClick.Invoke();
            // Destroy 在 Editor 非播放模式下延迟；手动 DestroyImmediate 模拟下一帧销毁
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickCancelButton()
        {
            var dialog = GameObject.Find("PracticeConfirmDialog");
            if (dialog == null) return;
            var cancelBtn = dialog.transform.Find("Panel/CancelButton")?.GetComponent<Button>();
            if (cancelBtn != null) cancelBtn.onClick.Invoke();
            // Destroy 在 Editor 非播放模式下延迟；手动 DestroyImmediate 模拟下一帧销毁
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void InvokeEnsureReferences(PracticeSessionController controller)
        {
            var method = typeof(PracticeSessionController).GetMethod("EnsureReferences", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(controller, null);
        }
    }
}
