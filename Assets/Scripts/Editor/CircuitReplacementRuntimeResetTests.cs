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
    /// F2-A 失败关闭测试：加载新电路时重置旧仿真状态。
    ///
    /// 通过真实生产加载入口（TemplateLoadController.RequestLoadTemplateFromGallery、
    /// PracticeSessionController.StartPractice、BlueprintController.EnterConfiguration、
    /// SimulationGalleryPageController.LoadEntry）验证：替换画布时正确停止旧仿真、
    /// 清理旧运行态缓存、结束旧练习会话，并保持新模板序列化初始状态；
    /// 取消、锁定、资源读取失败等路径必须保持旧画布、旧仿真与旧运行缓存不变。
    ///
    /// 约束：
    /// - 使用真实生产加载入口，不反射调用 LoadTemplateNow；
    /// - 不在测试体中手工 StopSimulation 后声称生产代码完成重置（StartSimulation 仅用于构造“旧仿真运行”前置）；
    /// - 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出；
    /// - finally 仅负责清理测试资源，不吞异常。
    /// </summary>
    public static class CircuitReplacementRuntimeResetTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string TemplateIdA = "single_lamp_template";
        private const string TemplateIdB = "double_control_lamp_template";
        private const string TemplateIdC = "breaker_lamp_template";

        // 运行态哨兵键，用于验证旧 KT / 电机 / 自动往返 / 热继缓存不继承到新电路。
        private const string SentinelTimer = "f2a-sentinel-kt";
        private const string SentinelMotion = "f2a-sentinel-motion";
        private const string SentinelMotor = "f2a-sentinel-motor";
        private const string SentinelProtection = "f2a-sentinel-fr";

        [MenuItem("Tools/Tests/Run Circuit Replacement Runtime Reset Tests")]
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
                    throw new InvalidOperationException("F2-A 测试依赖缺失：WorkspaceController / SaveLoadService / Canvas 未找到。");
                }

                var loader = EnsureTemplateLoader(workspace, saveLoad);
                var practice = PracticeSessionController.Instance;
                InvokePrivate(practice, "EnsureReferences");

                var catalog = LoadCatalog();
                var itemA = GetItem(catalog, TemplateIdA);
                var itemB = GetItem(catalog, TemplateIdB);
                var itemC = GetItem(catalog, TemplateIdC);

                TestA_RunningOldCircuitLoadNewTemplate(workspace, loader, itemA, itemB, failures);
                TestB_BlueprintRealLoad(workspace, practice, itemA, itemB, failures);
                TestC_GalleryRealLoad(workspace, loader, itemA, itemB, failures);
                TestD_PracticeAToTemplateB(workspace, loader, practice, itemA, itemB, failures);
                TestE_PracticeAToPracticeB(workspace, practice, itemA, itemB, failures);
                TestF_UserCancelReplacement(workspace, loader, itemA, itemB, failures);
                TestG_LockedCanvasReject(workspace, loader, itemA, itemB, failures);
                TestH_TemplateResourceMissing(workspace, loader, itemA, failures);
                TestI_NewTemplateInitialStatePreserved(workspace, loader, itemA, itemB, failures);
                TestJ_OldTimerStateNotInherited(workspace, loader, itemA, itemB, failures);
                TestK_OldMotorMotionStateNotInherited(workspace, loader, itemA, itemB, failures);
                TestL_ConsecutiveReplacement(workspace, loader, itemA, itemB, itemC, failures);
                TestM_ButtonLabelSyncAfterReplacement(workspace, loader, itemA, itemB, failures);
                TestN_PracticeEmptyCanvasLoadRequiresConfirm(workspace, loader, practice, itemA, itemB, failures);
                TestO_PracticeNonEmptyCanvasLoadConfirm(workspace, loader, practice, itemA, itemB, failures);
                TestP_CancelReplacementKeepsSimulationRunning(workspace, loader, itemA, itemB, failures);
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
                throw new InvalidOperationException("F2-A 电路替换运行态重置测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][F2-A] 电路替换运行态重置：通过");
        }

        // A. 运行中的旧电路加载新模板（TemplateLoadController 真实入口）
        private static void TestA_RunningOldCircuitLoadNewTemplate(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "A";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);

            var runtimeBefore = RuntimeStateManager.Shared;
            if (runtimeBefore.TimerStateCount == 0 && runtimeBefore.MotionStateCount == 0
                && runtimeBefore.MotorStateCount == 0 && runtimeBefore.ProtectionStateCount == 0)
            {
                failures.Add(scenario + ": 前置运行态播种失败，旧缓存为空。");
            }

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateConfirmButton();

            AssertReplacementSuccess(scenario, workspace, failures, itemB, callbacks);
            AssertRuntimeStateCleared(scenario, workspace, failures);
            AssertNoEnergizedComponents(scenario, workspace, failures);
        }

        // B. 图纸集真实加载（BlueprintController.EnterConfiguration → StartPractice）
        private static void TestB_BlueprintRealLoad(
            WorkspaceController workspace, PracticeSessionController practice,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "B";
            SetupRunningOldCircuit(workspace, null, itemA, failures, scenario);
            // 图纸集入口需要画布有内容才会触发确认框；itemA 已加载。
            var blueprintObject = new GameObject("F2ABlueprintEntry");
            BlueprintController blueprint = null;
            try
            {
                blueprint = blueprintObject.AddComponent<BlueprintController>();
                SetPrivateField(blueprint, "dynamicTemplates", new List<CircuitTemplateCatalogItemDto> { itemB });
                SetPrivateField(blueprint, "selectedIndex", 0);
                InvokePrivate(blueprint, "EnterConfiguration");
                ClickPracticeConfirmButton();

                if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
                if (!practice.IsPracticeActive) failures.Add(scenario + ": IsPracticeActive 应为 true（练习 B 已建立）。");
                if (practice.CurrentTemplateItem != itemB) failures.Add(scenario + ": CurrentTemplateItem 应为 itemB。");
                if (practice.CurrentTemplateData == null) failures.Add(scenario + ": CurrentTemplateData 应非空。");
                AssertRuntimeStateCleared(scenario, workspace, failures);
                AssertNoEnergizedComponents(scenario, workspace, failures);
            }
            finally
            {
                if (blueprintObject != null) UnityEngine.Object.DestroyImmediate(blueprintObject);
            }
        }

        // C. 仿真广场真实加载（SimulationGalleryPageController.LoadEntry → TemplateLoadController）
        // LoadEntry 内部 callback 仅做 navigation.SelectTab，不是加载成功指示器；
        // 因此 TestC 不跟踪 callback 计数，仅通过真实副作用验证（IsSimulationRunning、Components、TemplateEditSession）。
        private static void TestC_GalleryRealLoad(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "C";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);

            var gallery = UnityEngine.Object.FindObjectOfType<SimulationGalleryPageController>(true);
            var temporaryGallery = false;
            if (gallery == null)
            {
                var go = new GameObject("F2AGalleryEntry", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                gallery = go.AddComponent<SimulationGalleryPageController>();
                temporaryGallery = true;
            }

            try
            {
                InvokeGalleryLoadEntry(gallery, itemB);
                ClickLoadTemplateConfirmButton();
            }
            finally
            {
                if (temporaryGallery && gallery != null) UnityEngine.Object.DestroyImmediate(gallery.gameObject);
            }

            // 仅校验真实生产副作用，不校验 callback 计数（LoadEntry 的 callback 是 navigation 切换）。
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
            if (workspace.Components.Count == 0) failures.Add(scenario + ": 新模板元件应已生成。");
            if (TemplateEditSession.CurrentTemplateId != itemB.templateId) failures.Add(scenario + ": TemplateEditSession 应记录新模板。");
            AssertRuntimeStateCleared(scenario, workspace, failures);
            AssertNoEnergizedComponents(scenario, workspace, failures);
        }

        // D. 练习 A → 普通模板 B（工具栏加载模板应结束练习 A 并停止旧仿真）
        private static void TestD_PracticeAToTemplateB(
            WorkspaceController workspace, TemplateLoadController loader, PracticeSessionController practice,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "D";
            CleanupWorkspace(workspace, practice);
            // 进入练习 A（空画布，无确认框）
            int practiceCb = 0;
            practice.StartPractice(itemA, () => practiceCb++);
            if (!practice.IsPracticeActive) { failures.Add(scenario + ": 练习 A 前置未建立。"); return; }
            // 在练习画布上放置练习元件并启动仿真，构造“练习 A 仿真运行”
            SpawnPracticeComponent(workspace, "PracticeD-Switch", "practice-d-sw");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(scenario + ": 旧仿真未启动。"); return; }
            SeedRuntimeState();

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateConfirmButton();

            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
            if (practice.IsPracticeActive) failures.Add(scenario + ": IsPracticeActive 应为 false（练习 A 已结束）。");
            if (practice.CurrentTemplateItem != null) failures.Add(scenario + ": CurrentTemplateItem 应为 null。");
            if (practice.CurrentTemplateData != null) failures.Add(scenario + ": CurrentTemplateData 应为 null。");
            if (callbacks != 1) failures.Add(scenario + ": onLoaded 应调用一次，实际=" + callbacks);
            if (TemplateEditSession.CurrentTemplateId != itemB.templateId) failures.Add(scenario + ": TemplateEditSession 应记录 B。");
            if (workspace.Components.Count == 0) failures.Add(scenario + ": 新模板元件应已生成。");
            AssertRuntimeStateCleared(scenario, workspace, failures);
            AssertNoEnergizedComponents(scenario, workspace, failures);
        }

        // E. 练习 A → 练习 B（PracticeSessionController.StartPractice 真实入口）
        private static void TestE_PracticeAToPracticeB(
            WorkspaceController workspace, PracticeSessionController practice,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "E";
            CleanupWorkspace(workspace, practice);
            int practiceCbA = 0;
            practice.StartPractice(itemA, () => practiceCbA++);
            if (!practice.IsPracticeActive) { failures.Add(scenario + ": 练习 A 前置未建立。"); return; }
            SpawnPracticeComponent(workspace, "PracticeE-Switch", "practice-e-sw");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(scenario + ": 旧仿真未启动。"); return; }
            SeedRuntimeState();

            int practiceCbB = 0;
            practice.StartPractice(itemB, () => practiceCbB++);
            ClickPracticeConfirmButton();

            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
            if (!practice.IsPracticeActive) failures.Add(scenario + ": IsPracticeActive 应为 true（练习 B）。");
            if (practice.CurrentTemplateItem != itemB) failures.Add(scenario + ": CurrentTemplateItem 应为 itemB。");
            if (practice.CurrentTemplateData == null) failures.Add(scenario + ": CurrentTemplateData 应非空。");
            if (practiceCbB != 1) failures.Add(scenario + ": 练习 B onEntered 应调用一次，实际=" + practiceCbB);
            AssertRuntimeStateCleared(scenario, workspace, failures);
            AssertNoEnergizedComponents(scenario, workspace, failures);
        }

        // F. 用户取消替换（确认框取消，旧状态全部保持）
        private static void TestF_UserCancelReplacement(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "F";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var componentCountBefore = workspace.Components.Count;
            var wireCountBefore = workspace.WireManager.Wires.Count;
            var instanceIdsBefore = workspace.Components.Select(c => c.InstanceId).ToList();
            SeedRuntimeState();

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateCancelButton();

            if (workspace.IsSimulationRunning != true) failures.Add(scenario + ": 取消后旧仿真应仍在运行。");
            if (workspace.Components.Count != componentCountBefore) failures.Add(scenario + ": 取消后组件数量应不变。");
            if (workspace.WireManager.Wires.Count != wireCountBefore) failures.Add(scenario + ": 取消后 Wire 数量应不变。");
            var instanceIdsAfter = workspace.Components.Select(c => c.InstanceId).ToList();
            if (!instanceIdsBefore.SequenceEqual(instanceIdsAfter)) failures.Add(scenario + ": 取消后 InstanceId 列表应不变。");
            if (callbacks != 0) failures.Add(scenario + ": 取消时 onLoaded 不应调用，实际=" + callbacks);
            if (TemplateEditSession.CurrentTemplateId != itemA.templateId) failures.Add(scenario + ": 取消后 TemplateEditSession 应仍为 A。");
            AssertRuntimeStatePreserved(scenario, failures);
        }

        // G. 画布锁定拒绝（旧状态全部保持）
        private static void TestG_LockedCanvasReject(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "G";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var componentCountBefore = workspace.Components.Count;
            var wireCountBefore = workspace.WireManager.Wires.Count;
            SeedRuntimeState();
            workspace.ToggleInteractionLock();
            if (!workspace.IsInteractionLocked) { failures.Add(scenario + ": 画布锁定前置失败。"); return; }

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            CloseLockedDialog();

            if (!workspace.IsInteractionLocked) failures.Add(scenario + ": 锁定状态应保持。");
            if (workspace.IsSimulationRunning != true) failures.Add(scenario + ": 锁定拒绝后旧仿真应仍在运行。");
            if (workspace.Components.Count != componentCountBefore) failures.Add(scenario + ": 锁定拒绝后组件数量应不变。");
            if (workspace.WireManager.Wires.Count != wireCountBefore) failures.Add(scenario + ": 锁定拒绝后 Wire 数量应不变。");
            if (callbacks != 0) failures.Add(scenario + ": 锁定拒绝时 onLoaded 不应调用。");
            AssertRuntimeStatePreserved(scenario, failures);
        }

        // H. 模板资源不存在或读取失败（旧状态全部保持）
        private static void TestH_TemplateResourceMissing(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, List<string> failures)
        {
            const string scenario = "H";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var componentCountBefore = workspace.Components.Count;
            var wireCountBefore = workspace.WireManager.Wires.Count;
            SeedRuntimeState();

            var badItem = new CircuitTemplateCatalogItemDto
            {
                templateId = "f2a_nonexistent_template",
                templateName = "F2A不存在",
                resourcePath = "Blueprints/Templates/f2a_does_not_exist"
            };

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(badItem, () => callbacks++);
            // 画布有内容 → 弹确认框；点击确认后 LoadTemplateNow 调用 TryLoad 失败
            ClickLoadTemplateConfirmButton();

            if (workspace.IsSimulationRunning != true) failures.Add(scenario + ": 读取失败后旧仿真应仍在运行。");
            if (workspace.Components.Count != componentCountBefore) failures.Add(scenario + ": 读取失败后组件数量应不变。");
            if (workspace.WireManager.Wires.Count != wireCountBefore) failures.Add(scenario + ": 读取失败后 Wire 数量应不变。");
            if (callbacks != 0) failures.Add(scenario + ": 读取失败时 onLoaded 不应调用。");
            if (TemplateEditSession.CurrentTemplateId != itemA.templateId) failures.Add(scenario + ": 读取失败后 TemplateEditSession 应仍为 A。");
            AssertRuntimeStatePreserved(scenario, failures);
        }

        // I. 新模板序列化初始状态保持（isClosed / instanceId / 数量与模板 JSON 一致）
        private static void TestI_NewTemplateInitialStatePreserved(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "I";
            CleanupWorkspace(workspace, PracticeSessionController.Instance);
            // 空画布加载 itemB，不触发确认框
            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            if (callbacks != 1) { failures.Add(scenario + ": 空画布加载应成功回调一次，实际=" + callbacks); return; }

            if (!CircuitTemplateLoader.TryLoad(itemB.resourcePath, out var dto, out var error))
            {
                failures.Add(scenario + ": 无法读取模板 DTO 用于初始状态比对：" + error);
                return;
            }

            if (workspace.Components.Count != dto.components.Count)
            {
                failures.Add(scenario + ": 元件数量应与模板一致，模板=" + dto.components.Count + " 实际=" + workspace.Components.Count);
            }

            foreach (var templateComponent in dto.components)
            {
                var spawned = workspace.FindComponent(templateComponent.instanceId);
                if (spawned == null)
                {
                    failures.Add(scenario + ": 缺少模板元件 " + templateComponent.instanceId);
                    continue;
                }
                if (spawned.IsClosed != templateComponent.isClosed)
                {
                    failures.Add(scenario + ": 元件 " + templateComponent.instanceId + " isClosed 应为 " + templateComponent.isClosed + " 实际 " + spawned.IsClosed);
                }
            }

            // 新模板加载后不应自动运行（IsSimulationRunning 应为 false）
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 新模板加载后不应自动运行。");
        }

        // J. 旧时间继电器计时不继承
        private static void TestJ_OldTimerStateNotInherited(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "J";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var timer = RuntimeStateManager.Shared.GetOrCreateTimerState(SentinelTimer);
            timer.DelaySeconds = 5f;
            timer.ElapsedSeconds = 3.2f;
            timer.Phase = TimerRuntimePhase.Timing;
            timer.IsCoilEnergized = true;
            if (RuntimeStateManager.Shared.TimerStateCount < 1) { failures.Add(scenario + ": 旧 KT 状态播种失败。"); return; }

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateConfirmButton();

            if (callbacks != 1) failures.Add(scenario + ": onLoaded 应调用一次，实际=" + callbacks);
            if (RuntimeStateManager.Shared.TryGetTimerState(SentinelTimer, out var leftover))
            {
                failures.Add(scenario + ": 旧 KT 计时状态不应继承，仍存在 sentinel=" + SentinelTimer + " elapsed=" + leftover.ElapsedSeconds);
            }
            if (RuntimeStateManager.Shared.TimerStateCount != 0)
            {
                failures.Add(scenario + ": 替换后 TimerStateCount 应为 0，实际=" + RuntimeStateManager.Shared.TimerStateCount);
            }
        }

        // K. 旧电机 / 自动往返位置不继承
        private static void TestK_OldMotorMotionStateNotInherited(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "K";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var motion = RuntimeStateManager.Shared.GetOrCreateMotionState(SentinelMotion);
            motion.Position = 82.5f;
            motion.Direction = MotionDirection.Forward;
            motion.RightLimitTriggered = true;
            var motor = RuntimeStateManager.Shared.GetOrCreateMotorState(SentinelMotor);
            motor.IsRunning = true;
            var protection = RuntimeStateManager.Shared.GetOrCreateProtectionState(SentinelProtection);
            protection.IsTripped = true;
            protection.OverloadSeconds = 14f;
            if (RuntimeStateManager.Shared.MotionStateCount < 1 || RuntimeStateManager.Shared.MotorStateCount < 1)
            {
                failures.Add(scenario + ": 旧电机/往返状态播种失败。"); return;
            }

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateConfirmButton();

            if (callbacks != 1) failures.Add(scenario + ": onLoaded 应调用一次，实际=" + callbacks);
            if (RuntimeStateManager.Shared.TryGetMotionState(SentinelMotion, out var leftoverMotion))
            {
                failures.Add(scenario + ": 旧自动往返位置不应继承，仍存在 sentinel pos=" + leftoverMotion.Position);
            }
            if (RuntimeStateManager.Shared.TryGetMotorState(SentinelMotor, out var leftoverMotor))
            {
                failures.Add(scenario + ": 旧电机方向状态不应继承，仍存在 sentinel running=" + leftoverMotor.IsRunning);
            }
            if (RuntimeStateManager.Shared.TryGetProtectionState(SentinelProtection, out var leftoverProtection))
            {
                failures.Add(scenario + ": 旧热继脱扣状态不应继承，仍存在 tripped=" + leftoverProtection.IsTripped);
            }
            if (RuntimeStateManager.Shared.MotionStateCount != 0) failures.Add(scenario + ": 替换后 MotionStateCount 应为 0，实际=" + RuntimeStateManager.Shared.MotionStateCount);
            if (RuntimeStateManager.Shared.MotorStateCount != 0) failures.Add(scenario + ": 替换后 MotorStateCount 应为 0，实际=" + RuntimeStateManager.Shared.MotorStateCount);
            if (RuntimeStateManager.Shared.ProtectionStateCount != 0) failures.Add(scenario + ": 替换后 ProtectionStateCount 应为 0，实际=" + RuntimeStateManager.Shared.ProtectionStateCount);
        }

        // L. A → B → 手动启动 B → C 连续替换
        private static void TestL_ConsecutiveReplacement(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, CircuitTemplateCatalogItemDto itemC, List<string> failures)
        {
            const string scenario = "L";
            CleanupWorkspace(workspace, PracticeSessionController.Instance);

            // 加载 A（空画布，无确认）
            int cbA = 0;
            loader.RequestLoadTemplateFromGallery(itemA, () => cbA++);
            if (cbA != 1) { failures.Add(scenario + ": 加载 A 回调应为 1，实际=" + cbA); return; }
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 加载 A 后不应自动运行。");
            if (TemplateEditSession.CurrentTemplateId != itemA.templateId) failures.Add(scenario + ": 加载 A 后 TemplateEditSession 应为 A。");

            // 加载 B（A 有内容 → 确认）
            int cbB = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => cbB++);
            ClickLoadTemplateConfirmButton();
            if (cbB != 1) { failures.Add(scenario + ": 加载 B 回调应为 1，实际=" + cbB); return; }
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 加载 B 后不应自动运行。");
            if (TemplateEditSession.CurrentTemplateId != itemB.templateId) failures.Add(scenario + ": 加载 B 后 TemplateEditSession 应为 B。");

            // 手动启动 B 仿真
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(scenario + ": 手动启动 B 仿真失败。"); return; }
            SeedRuntimeState();

            // 加载 C（B 运行中 + 有内容 → 确认）→ 必须停止 B 仿真
            int cbC = 0;
            loader.RequestLoadTemplateFromGallery(itemC, () => cbC++);
            ClickLoadTemplateConfirmButton();

            if (cbC != 1) failures.Add(scenario + ": 加载 C 回调应为 1，实际=" + cbC);
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 加载 C 后 IsSimulationRunning 应为 false（B 仿真已停止）。");
            if (TemplateEditSession.CurrentTemplateId != itemC.templateId) failures.Add(scenario + ": 加载 C 后 TemplateEditSession 应为 C。");
            if (workspace.Components.Count == 0) failures.Add(scenario + ": 加载 C 后元件应已生成。");
            AssertRuntimeStateCleared(scenario, workspace, failures);
            AssertNoEnergizedComponents(scenario, workspace, failures);
        }

        // M. 按钮状态同步：运行模板 A 后加载 B，按钮应恢复"开始仿真"
        private static void TestM_ButtonLabelSyncAfterReplacement(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "M";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);

            // batchmode 下无 LateUpdate 循环，先手动调用 RefreshSimulationButtonLabel 同步按钮到"结束仿真"
            var demoUi = UnityEngine.Object.FindObjectOfType<DemoUIController>(true);
            if (demoUi != null)
            {
                InvokePrivate(demoUi, "RefreshSimulationButtonLabel");
            }

            // 找到开始仿真按钮并验证旧状态显示"结束仿真"
            var startButton = FindStartSimulationButton();
            if (startButton == null) { failures.Add(scenario + ": 未找到开始仿真按钮。"); return; }
            var labelBefore = startButton.GetComponentInChildren<Text>();
            if (labelBefore == null) { failures.Add(scenario + ": 开始仿真按钮无 Text。"); return; }
            if (labelBefore.text != "结束仿真") failures.Add(scenario + ": 旧仿真运行时按钮应显示'结束仿真'，实际='" + labelBefore.text + "'。");

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            ClickLoadTemplateConfirmButton();

            // 模拟 LateUpdate 调用 RefreshSimulationButtonLabel
            if (demoUi != null)
            {
                InvokePrivate(demoUi, "RefreshSimulationButtonLabel");
            }

            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
            var labelAfter = startButton != null ? startButton.GetComponentInChildren<Text>() : null;
            if (labelAfter != null && labelAfter.text != "开始仿真")
            {
                failures.Add(scenario + ": 替换后按钮应显示'开始仿真'，实际='" + labelAfter.text + "'。");
            }
            AssertRuntimeStateCleared(scenario, workspace, failures);
        }

        // N. 练习 A + 空画布加载模板 B：必须出现确认框，取消保持 A，确认加载 B
        private static void TestN_PracticeEmptyCanvasLoadRequiresConfirm(
            WorkspaceController workspace, TemplateLoadController loader, PracticeSessionController practice,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "N";
            CleanupWorkspace(workspace, practice);
            int practiceCb = 0;
            practice.StartPractice(itemA, () => practiceCb++);
            if (!practice.IsPracticeActive) { failures.Add(scenario + ": 练习 A 前置未建立。"); return; }
            // 空画布（练习 A 不放置任何元件），验证确认框仍出现
            if (workspace.Components.Count != 0) { failures.Add(scenario + ": 前置画布应为空。"); return; }

            // 先测试取消路径
            var callbacksCancel = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacksCancel++);
            var dialogAfterRequest = GameObject.Find("LoadTemplateConfirmDialog");
            if (dialogAfterRequest == null) { failures.Add(scenario + ": 空画布+练习活动时必须显示确认框。"); return; }
            ClickLoadTemplateCancelButton();

            if (!practice.IsPracticeActive) failures.Add(scenario + ": 取消后练习 A 应保持。");
            if (practice.CurrentTemplateItem != itemA) failures.Add(scenario + ": 取消后 CurrentTemplateItem 应仍为 A。");
            if (callbacksCancel != 0) failures.Add(scenario + ": 取消时 onLoaded 不应调用。");
            if (workspace.Components.Count != 0) failures.Add(scenario + ": 取消后画布应仍为空。");

            // 再测试确认路径
            var callbacksConfirm = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacksConfirm++);
            ClickLoadTemplateConfirmButton();

            if (practice.IsPracticeActive) failures.Add(scenario + ": 确认后练习 A 应结束。");
            if (practice.CurrentTemplateItem != null) failures.Add(scenario + ": 确认后 CurrentTemplateItem 应为 null。");
            if (callbacksConfirm != 1) failures.Add(scenario + ": 确认后 onLoaded 应调用一次，实际=" + callbacksConfirm);
            if (TemplateEditSession.CurrentTemplateId != itemB.templateId) failures.Add(scenario + ": 确认后 TemplateEditSession 应为 B。");
            if (workspace.Components.Count == 0) failures.Add(scenario + ": 确认后新模板元件应已生成。");
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 确认后 IsSimulationRunning 应为 false。");
        }

        // O. 练习 A + 非空画布加载模板 B：必须出现确认框，取消保持 A，确认加载 B
        private static void TestO_PracticeNonEmptyCanvasLoadConfirm(
            WorkspaceController workspace, TemplateLoadController loader, PracticeSessionController practice,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "O";
            CleanupWorkspace(workspace, practice);
            int practiceCb = 0;
            practice.StartPractice(itemA, () => practiceCb++);
            if (!practice.IsPracticeActive) { failures.Add(scenario + ": 练习 A 前置未建立。"); return; }
            SpawnPracticeComponent(workspace, "PracticeO-Switch", "practice-o-sw");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(scenario + ": 旧仿真未启动。"); return; }
            SeedRuntimeState();
            var componentCountBefore = workspace.Components.Count;

            // 先测试取消路径
            var callbacksCancel = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacksCancel++);
            var dialogAfterRequest = GameObject.Find("LoadTemplateConfirmDialog");
            if (dialogAfterRequest == null) { failures.Add(scenario + ": 非空画布+练习活动时必须显示确认框。"); return; }
            ClickLoadTemplateCancelButton();

            if (!practice.IsPracticeActive) failures.Add(scenario + ": 取消后练习 A 应保持。");
            if (practice.CurrentTemplateItem != itemA) failures.Add(scenario + ": 取消后 CurrentTemplateItem 应仍为 A。");
            if (workspace.IsSimulationRunning != true) failures.Add(scenario + ": 取消后旧仿真应仍在运行。");
            if (workspace.Components.Count != componentCountBefore) failures.Add(scenario + ": 取消后组件数量应不变。");
            if (callbacksCancel != 0) failures.Add(scenario + ": 取消时 onLoaded 不应调用。");
            AssertRuntimeStatePreserved(scenario, failures);

            // 再测试确认路径
            var callbacksConfirm = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacksConfirm++);
            ClickLoadTemplateConfirmButton();

            if (practice.IsPracticeActive) failures.Add(scenario + ": 确认后练习 A 应结束。");
            if (practice.CurrentTemplateItem != null) failures.Add(scenario + ": 确认后 CurrentTemplateItem 应为 null。");
            if (callbacksConfirm != 1) failures.Add(scenario + ": 确认后 onLoaded 应调用一次，实际=" + callbacksConfirm);
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 确认后 IsSimulationRunning 应为 false。");
            if (TemplateEditSession.CurrentTemplateId != itemB.templateId) failures.Add(scenario + ": 确认后 TemplateEditSession 应为 B。");
            AssertRuntimeStateCleared(scenario, workspace, failures);
        }

        // P. 取消替换：旧仿真继续运行，旧画布和参考图纸不变
        private static void TestP_CancelReplacementKeepsSimulationRunning(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, CircuitTemplateCatalogItemDto itemB, List<string> failures)
        {
            const string scenario = "P";
            SetupRunningOldCircuit(workspace, loader, itemA, failures, scenario);
            var componentCountBefore = workspace.Components.Count;
            var wireCountBefore = workspace.WireManager.Wires.Count;
            var templateIdBefore = TemplateEditSession.CurrentTemplateId;
            SeedRuntimeState();

            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(itemB, () => callbacks++);
            var dialog = GameObject.Find("LoadTemplateConfirmDialog");
            if (dialog == null) { failures.Add(scenario + ": 确认框应出现。"); return; }
            ClickLoadTemplateCancelButton();

            if (workspace.IsSimulationRunning != true) failures.Add(scenario + ": 取消后旧仿真应仍在运行。");
            if (workspace.Components.Count != componentCountBefore) failures.Add(scenario + ": 取消后组件数量应不变。");
            if (workspace.WireManager.Wires.Count != wireCountBefore) failures.Add(scenario + ": 取消后 Wire 数量应不变。");
            if (callbacks != 0) failures.Add(scenario + ": 取消时 onLoaded 不应调用。");
            if (TemplateEditSession.CurrentTemplateId != templateIdBefore) failures.Add(scenario + ": 取消后 TemplateEditSession 应不变。");
            AssertRuntimeStatePreserved(scenario, failures);
        }

        // --- 通用断言 ---

        private static void AssertReplacementSuccess(
            string scenario, WorkspaceController workspace, List<string> failures,
            CircuitTemplateCatalogItemDto expectedItem, int callbacks)
        {
            if (workspace.IsSimulationRunning) failures.Add(scenario + ": 替换后 IsSimulationRunning 应为 false。");
            if (callbacks != 1) failures.Add(scenario + ": onLoaded 应调用一次，实际=" + callbacks);
            if (workspace.Components.Count == 0) failures.Add(scenario + ": 新模板元件应已生成。");
            if (TemplateEditSession.CurrentTemplateId != expectedItem.templateId) failures.Add(scenario + ": TemplateEditSession 应记录新模板。");
            var timerField = typeof(WorkspaceController).GetField("simulationRefreshTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (timerField != null)
            {
                var timerValue = (float)timerField.GetValue(workspace);
                if (!Mathf.Approximately(timerValue, 0f)) failures.Add(scenario + ": simulationRefreshTimer 应为 0，实际=" + timerValue);
            }
        }

        private static void AssertRuntimeStateCleared(string scenario, WorkspaceController workspace, List<string> failures)
        {
            var mgr = RuntimeStateManager.Shared;
            if (mgr.TimerStateCount != 0) failures.Add(scenario + ": 替换后 TimerStateCount 应为 0，实际=" + mgr.TimerStateCount);
            if (mgr.MotionStateCount != 0) failures.Add(scenario + ": 替换后 MotionStateCount 应为 0，实际=" + mgr.MotionStateCount);
            if (mgr.MotorStateCount != 0) failures.Add(scenario + ": 替换后 MotorStateCount 应为 0，实际=" + mgr.MotorStateCount);
            if (mgr.ProtectionStateCount != 0) failures.Add(scenario + ": 替换后 ProtectionStateCount 应为 0，实际=" + mgr.ProtectionStateCount);
        }

        private static void AssertRuntimeStatePreserved(string scenario, List<string> failures)
        {
            var mgr = RuntimeStateManager.Shared;
            if (!mgr.TryGetTimerState(SentinelTimer, out _)) failures.Add(scenario + ": 旧 KT 状态应保持，但 sentinel 丢失。");
            if (!mgr.TryGetMotionState(SentinelMotion, out _)) failures.Add(scenario + ": 旧往返状态应保持，但 sentinel 丢失。");
            if (!mgr.TryGetMotorState(SentinelMotor, out _)) failures.Add(scenario + ": 旧电机状态应保持，但 sentinel 丢失。");
            if (!mgr.TryGetProtectionState(SentinelProtection, out _)) failures.Add(scenario + ": 旧热继状态应保持，但 sentinel 丢失。");
        }

        private static void AssertNoEnergizedComponents(string scenario, WorkspaceController workspace, List<string> failures)
        {
            // 新模板加载后不应自动运行，因此新元件不应被 EvaluateSimulation 标记为带电。
            // 旧 Inspector 运行快照（IsEnergized / Measurement）应已清除。
            foreach (var component in workspace.Components)
            {
                if (component == null) continue;
                if (component.IsEnergized)
                {
                    failures.Add(scenario + ": 新模板元件 " + component.InstanceId + " 不应自动带电（新模板未开始仿真）。");
                }
            }
        }

        // --- 前置与播种 ---

        private static void SetupRunningOldCircuit(
            WorkspaceController workspace, TemplateLoadController loader,
            CircuitTemplateCatalogItemDto itemA, List<string> failures, string scenario)
        {
            var practice = PracticeSessionController.Instance;
            CleanupWorkspace(workspace, practice);
            // 空画布加载 itemA，不触发确认框
            var loaderToUse = loader ?? EnsureTemplateLoader(workspace, UnityEngine.Object.FindObjectOfType<SaveLoadService>(true));
            int cb = 0;
            loaderToUse.RequestLoadTemplateFromGallery(itemA, () => cb++);
            if (cb != 1 || workspace.Components.Count == 0)
            {
                failures.Add(scenario + ": 旧电路前置加载失败 cb=" + cb + " components=" + workspace.Components.Count);
                return;
            }
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning)
            {
                failures.Add(scenario + ": 旧仿真未启动。");
                return;
            }
            SeedRuntimeState();
        }

        private static void SeedRuntimeState()
        {
            var timer = RuntimeStateManager.Shared.GetOrCreateTimerState(SentinelTimer);
            timer.DelaySeconds = 5f;
            timer.ElapsedSeconds = 3.2f;
            timer.Phase = TimerRuntimePhase.Timing;
            timer.IsCoilEnergized = true;

            var motion = RuntimeStateManager.Shared.GetOrCreateMotionState(SentinelMotion);
            motion.Position = 75f;
            motion.Direction = MotionDirection.Forward;
            motion.RightLimitTriggered = true;

            var motor = RuntimeStateManager.Shared.GetOrCreateMotorState(SentinelMotor);
            motor.IsRunning = true;

            var protection = RuntimeStateManager.Shared.GetOrCreateProtectionState(SentinelProtection);
            protection.IsTripped = true;
            protection.OverloadSeconds = 12f;
        }

        private static void SpawnPracticeComponent(WorkspaceController workspace, string displayName, string instanceId)
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
            workspace.SpawnComponent(def, Vector2.zero, instanceId, false);
        }

        // --- 对话框与入口辅助 ---

        private static void ClickLoadTemplateConfirmButton()
        {
            var dialog = GameObject.Find("LoadTemplateConfirmDialog");
            if (dialog == null) return;
            foreach (var btn in dialog.GetComponentsInChildren<Button>(true))
            {
                var label = btn.GetComponentInChildren<Text>();
                if (label != null && label.text == "确认加载")
                {
                    btn.onClick.Invoke();
                    break;
                }
            }
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickLoadTemplateCancelButton()
        {
            var dialog = GameObject.Find("LoadTemplateConfirmDialog");
            if (dialog == null) return;
            foreach (var btn in dialog.GetComponentsInChildren<Button>(true))
            {
                var label = btn.GetComponentInChildren<Text>();
                if (label != null && label.text == "取消")
                {
                    btn.onClick.Invoke();
                    break;
                }
            }
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickPracticeConfirmButton()
        {
            var dialog = GameObject.Find("PracticeConfirmDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("Panel/ConfirmButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void CloseLockedDialog()
        {
            var dialog = GameObject.Find("LockedCanvasLoadDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("DialogPanel/AcknowledgeButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void InvokeGalleryLoadEntry(SimulationGalleryPageController gallery, CircuitTemplateCatalogItemDto item)
        {
            // 真实生产入口：SimulationGalleryPageController.LoadEntry 内部 FindObjectOfType<TemplateLoadController>()
            // 再调用 RequestLoadTemplateFromGallery(item, navCallback)。navCallback 仅做 navigation.SelectTab，
            // 不是加载成功指示器，因此本方法不传入测试侧 callback；加载成功由 TestC 的副作用断言验证
            //（IsSimulationRunning、Components.Count、TemplateEditSession.CurrentTemplateId）。
            var entryType = typeof(SimulationGalleryPageController).GetNestedType("GalleryEntry", BindingFlags.NonPublic);
            if (entryType == null) throw new InvalidOperationException("未找到 SimulationGalleryPageController.GalleryEntry 嵌套类型。");
            var entry = Activator.CreateInstance(entryType, true);
            entryType.GetField("CatalogItem", BindingFlags.Public | BindingFlags.Instance).SetValue(entry, item);

            var method = typeof(SimulationGalleryPageController).GetMethod("LoadEntry", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) throw new InvalidOperationException("未找到 SimulationGalleryPageController.LoadEntry 方法。");
            method.Invoke(gallery, new[] { entry });
        }

        // --- 反射与基础辅助 ---

        private static TemplateLoadController EnsureTemplateLoader(WorkspaceController workspace, SaveLoadService saveLoad)
        {
            var loader = UnityEngine.Object.FindObjectOfType<TemplateLoadController>(true);
            if (loader == null)
            {
                loader = new GameObject("F2ATemplateLoadController").AddComponent<TemplateLoadController>();
            }
            SetPrivateField(loader, "workspace", workspace);
            SetPrivateField(loader, "saveLoadService", saveLoad);
            return loader;
        }

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

        private static void CleanupWorkspace(WorkspaceController workspace, PracticeSessionController practice)
        {
            if (workspace == null) return;
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            if (workspace.IsSimulationRunning) workspace.StopSimulation();
            workspace.ClearDrawing(true);
            if (practice != null) practice.ClearPracticeState();
            DestroyDialog("LoadTemplateConfirmDialog");
            DestroyDialog("PracticeConfirmDialog");
            DestroyDialog("LockedCanvasLoadDialog");
            RuntimeStateManager.Shared.ResetAll("F2-A test cleanup");
        }

        private static void DestroyDialog(string name)
        {
            var dialog = GameObject.Find(name);
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        // 查找开始仿真按钮：DemoUIController.startButton 是 private，通过反射获取；
        // 若反射失败则回退到按文字查找（"开始仿真"/"结束仿真"）。
        private static Button FindStartSimulationButton()
        {
            var demoUi = UnityEngine.Object.FindObjectOfType<DemoUIController>(true);
            if (demoUi != null)
            {
                var field = typeof(DemoUIController).GetField("startButton", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var btn = field.GetValue(demoUi) as Button;
                    if (btn != null) return btn;
                }
            }

            // 回退：遍历所有 Button 查找文字匹配
            foreach (var btn in UnityEngine.Object.FindObjectsOfType<Button>(true))
            {
                var label = btn.GetComponentInChildren<Text>();
                if (label != null && (label.text == "开始仿真" || label.text == "结束仿真"))
                {
                    return btn;
                }
            }
            return null;
        }
    }
}
