using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Practice;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// F2-C 模式切换保护测试：电工与 SPICE 模式切换的保护逻辑。
    ///
    /// 验证：
    /// - 电工普通画布未运行 → SPICE：直接切换，画布保留；
    /// - 电工运行中 → SPICE：弹窗确认/取消，确认后停止仿真+保留画布+切换；取消后状态不变；
    /// - 电工练习模式 → SPICE：弹窗确认/取消，确认后退出练习+清空画布+切换；取消后状态不变；
    /// - SPICE 空闲 → 电工：直接切换，SPICE 数据保持；
    /// - SPICE Running → 电工：阻止切换，显示单按钮提示；
    /// - SPICE Running → 其他主页面：阻止导航，显示单按钮提示；
    /// - SPICE 由 Running 变为 Current/Failed 后可以切换；
    /// - 连续点击模式选项只显示一个弹窗；
    /// - F2-B 弹窗打开时模式请求不能绕过；
    /// - 当前模式选项重复点击不弹窗；
    /// - 缺失 Canvas 时拒绝破坏性切换；
    /// - 两个工作区数据保持；
    /// - 模式显示文字与实际 CurrentMode 始终一致。
    ///
    /// 约束：
    /// - 使用真实生产入口 SimulationModeDropdown.SelectMode 和 TopNavigationController.SelectTab；
    /// - 使用 SpiceWorkspaceController.SetResultStateForTesting 模拟 SPICE 求解状态，不修改 SPICE 生产代码；
    /// - 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出。
    /// </summary>
    public static class ModeSwitchNavigationGuardTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string TemplateIdA = "single_lamp_template";

        [MenuItem("Tools/Tests/Run Mode Switch Navigation Guard Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var nav = UnityEngine.Object.FindObjectOfType<TopNavigationController>(true);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var practice = PracticeSessionController.Instance;
                var modeController = UnityEngine.Object.FindObjectOfType<SimulationModeController>(true);
                var modeDropdown = UnityEngine.Object.FindObjectOfType<SimulationModeDropdown>(true);
                var spiceHost = UnityEngine.Object.FindObjectOfType<SpiceWorkspaceDemoHost>(true);

                if (nav == null) throw new InvalidOperationException("F2-C 测试依赖缺失：TopNavigationController 未找到。");
                if (workspace == null) throw new InvalidOperationException("F2-C 测试依赖缺失：WorkspaceController 未找到。");
                if (modeController == null) throw new InvalidOperationException("F2-C 测试依赖缺失：SimulationModeController 未找到。");
                if (modeDropdown == null) throw new InvalidOperationException("F2-C 测试依赖缺失：SimulationModeDropdown 未找到。");

                InvokePrivate(practice, "EnsureReferences");
                InvokePrivate(nav, "Awake");

                // 确保 SimulationModeController 已初始化（batchmode 下 Awake 可能未执行）
                if (!IsModeControllerInitialized(modeController))
                {
                    InvokePrivate(modeController, "Initialize");
                }

                // 注入 modeController 引用到 TopNavigationController（复用 SimulationModeDropdown 的序列化引用）
                nav.ConfigureModeController(modeController);

                var router = GetPrivateField(nav, "pageRouter") as PageRouter;
                if (router == null)
                {
                    router = UnityEngine.Object.FindObjectOfType<PageRouter>(true);
                }
                if (router == null) throw new InvalidOperationException("F2-C 测试依赖缺失：PageRouter 未找到。");

                var spiceController = spiceHost != null ? spiceHost.Controller : null;
                var controlWorkspace = GetPrivateField(modeController, "controlWorkspace") as GameObject;
                var spiceModeRoot = GetPrivateField(modeController, "spiceModeRoot") as GameObject;

                if (spiceController == null) Debug.LogWarning("[F2-C] SpiceWorkspaceController 未找到，SPICE 相关测试将使用模拟状态。");
                if (controlWorkspace == null) throw new InvalidOperationException("F2-C 测试依赖缺失：controlWorkspace 未找到。");
                if (spiceModeRoot == null) throw new InvalidOperationException("F2-C 测试依赖缺失：spiceModeRoot 未找到。");

                Test01_ControlNotRunningToSpice(nav, modeDropdown, modeController, workspace, controlWorkspace, spiceModeRoot, failures);
                Test02_ControlRunningToSpiceConfirm(nav, modeDropdown, modeController, workspace, controlWorkspace, spiceModeRoot, failures);
                Test03_ControlRunningToSpiceCancel(nav, modeDropdown, modeController, workspace, controlWorkspace, spiceModeRoot, failures);
                Test04_PracticeNotRunningToSpiceConfirm(nav, modeDropdown, modeController, workspace, practice, spiceModeRoot, failures);
                Test05_PracticeRunningToSpiceConfirm(nav, modeDropdown, modeController, workspace, practice, spiceModeRoot, failures);
                Test06_PracticeRunningToSpiceCancel(nav, modeDropdown, modeController, workspace, practice, controlWorkspace, failures);
                Test07_SpiceIdleToControl(nav, modeDropdown, modeController, workspace, controlWorkspace, spiceModeRoot, failures);
                Test08_SpiceRunningToControlBlocked(nav, modeDropdown, modeController, spiceController, spiceModeRoot, failures);
                Test09_SpiceRunningToBlueprintBlocked(nav, router, modeController, spiceController, failures);
                Test10_SpiceRunningToSquareBlocked(nav, router, modeController, spiceController, failures);
                Test11_SpiceRunningToOtherPagesBlocked(nav, router, modeController, spiceController, failures);
                Test12_SpiceRunningToCurrentCanSwitch(nav, modeDropdown, modeController, spiceController, controlWorkspace, failures);
                Test13_SpiceRunningToFailedCanSwitch(nav, modeDropdown, modeController, spiceController, controlWorkspace, failures);
                Test14_ConsecutiveModeClicksSingleDialog(nav, modeDropdown, modeController, workspace, failures);
                Test15_F2BDialogOpenModeRequestBlocked(nav, modeDropdown, modeController, workspace, router, failures);
                Test16_SameModeClickNoDialog(nav, modeDropdown, modeController, failures);
                Test17_MissingCanvasRejectsSwitch(nav, modeDropdown, modeController, workspace, failures);
                Test18_BothWorkspaceDataPreserved(nav, modeDropdown, modeController, workspace, spiceController, failures);
                Test19_ModeDisplayConsistentWithCurrentMode(nav, modeDropdown, modeController, workspace, spiceController, controlWorkspace, spiceModeRoot, failures);
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
                throw new InvalidOperationException("F2-C 模式切换保护测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][F2-C] 模式切换保护：通过");
        }

        // 1. 电工未运行 → SPICE：直接切换，画布保留
        private static void Test01_ControlNotRunningToSpice(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, GameObject controlWorkspace, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "01";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T01-Comp", "t01-comp");

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);

            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": CurrentMode 应为 SpiceDc。", failures);
            Assert.IsFalse(controlWorkspace.activeSelf, s + ": 电工 Root 应隐藏。", failures);
            Assert.IsTrue(spiceModeRoot.activeSelf, s + ": SPICE Root 应显示。", failures);
            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 仿真不应运行。", failures);
            Assert.IsTrue(workspace.Components.Count > 0, s + ": 画布组件应保留。", failures);
            AssertNoGuardDialog(s, failures);
            AssertNoModeSwitchDialog(s, failures);
        }

        // 2. 电工运行中 → SPICE → 确认：停止仿真、画布保留、模式切换
        private static void Test02_ControlRunningToSpiceConfirm(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, GameObject controlWorkspace, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "02";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T02-Comp", "t02-comp");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(s + ": 前置仿真未启动。"); return; }

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": CurrentMode 应为 SpiceDc。", failures);
            Assert.IsTrue(spiceModeRoot.activeSelf, s + ": SPICE Root 应显示。", failures);
            Assert.IsTrue(workspace.Components.Count > 0, s + ": 画布组件应保留。", failures);
        }

        // 3. 电工运行中 → SPICE → 取消：仿真继续、模式不变
        private static void Test03_ControlRunningToSpiceCancel(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, GameObject controlWorkspace, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "03";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T03-Comp", "t03-comp");
            workspace.StartSimulation();

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertGuardDialogShown(s, failures);
            ClickGuardCancelButton();

            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 取消后仿真应继续。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": CurrentMode 应仍为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 电工 Root 应仍显示。", failures);
            Assert.IsFalse(spiceModeRoot.activeSelf, s + ": SPICE Root 应仍隐藏。", failures);
        }

        // 4. 练习未运行 → SPICE → 确认：退出练习、清空画布、切换
        private static void Test04_PracticeNotRunningToSpiceConfirm(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, PracticeSessionController practice, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "04";
            Cleanup(nav, workspace, practice, modeController, null);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();

            Assert.IsFalse(practice.IsPracticeActive, s + ": 确认后练习应退出。", failures);
            Assert.IsTrue(workspace.Components.Count == 0, s + ": 画布应清空。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": CurrentMode 应为 SpiceDc。", failures);
            Assert.IsTrue(spiceModeRoot.activeSelf, s + ": SPICE Root 应显示。", failures);
        }

        // 5. 练习运行中 → SPICE → 确认：停止仿真、退出练习、清空画布、切换
        private static void Test05_PracticeRunningToSpiceConfirm(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, PracticeSessionController practice, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "05";
            Cleanup(nav, workspace, practice, modeController, null);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }
            SpawnTestComponent(workspace, "T05-Comp", "t05-comp");
            workspace.StartSimulation();

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.IsFalse(practice.IsPracticeActive, s + ": 确认后练习应退出。", failures);
            Assert.IsTrue(workspace.Components.Count == 0, s + ": 画布应清空。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": CurrentMode 应为 SpiceDc。", failures);
        }

        // 6. 练习运行中 → SPICE → 取消：所有状态保持
        private static void Test06_PracticeRunningToSpiceCancel(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, PracticeSessionController practice, GameObject controlWorkspace, List<string> failures)
        {
            const string s = "06";
            Cleanup(nav, workspace, practice, modeController, null);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }
            SpawnTestComponent(workspace, "T06-Comp", "t06-comp");
            workspace.StartSimulation();
            var compCountBefore = workspace.Components.Count;

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertGuardDialogShown(s, failures);
            ClickGuardCancelButton();

            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 取消后仿真应继续。", failures);
            Assert.IsTrue(practice.IsPracticeActive, s + ": 取消后练习应保持。", failures);
            Assert.AreEqual(compCountBefore, workspace.Components.Count, s + ": 取消后画布应不变。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": CurrentMode 应仍为 ControlCircuit。", failures);
        }

        // 7. SPICE 空闲 → 电工：直接切换，SPICE 数据保持
        private static void Test07_SpiceIdleToControl(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, GameObject controlWorkspace, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "07";
            Cleanup(nav, workspace, null, modeController, null);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            if (modeController.CurrentMode != SimulationWorkspaceMode.SpiceDc) { failures.Add(s + ": 前置 SPICE 切换失败。"); return; }

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);

            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": CurrentMode 应为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 电工 Root 应显示。", failures);
            Assert.IsFalse(spiceModeRoot.activeSelf, s + ": SPICE Root 应隐藏。", failures);
            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 电工不应自动运行。", failures);
            AssertNoGuardDialog(s, failures);
            AssertNoModeSwitchDialog(s, failures);
        }

        // 8. SPICE Running → 电工：切换被拒绝
        private static void Test08_SpiceRunningToControlBlocked(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "08";
            Cleanup(nav, null, null, modeController, spiceController);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.Running);
            if (!modeController.IsSpiceSolving) { failures.Add(s + ": 前置 SPICE Running 未生效。"); return; }

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);
            AssertModeSwitchDialogShown(s, failures);
            ClickModeSwitchOkButton();

            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": CurrentMode 应仍为 SpiceDc。", failures);
            Assert.IsTrue(spiceModeRoot.activeSelf, s + ": SPICE Root 应仍显示。", failures);
            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.NeverRun);
        }

        // 9. SPICE Running → 图纸集：导航被拒绝
        private static void Test09_SpiceRunningToBlueprintBlocked(
            TopNavigationController nav, PageRouter router, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, List<string> failures)
        {
            const string s = "09";
            Cleanup(nav, null, null, modeController, spiceController);
            EnsureSpiceMode(nav, modeController, spiceController, SpiceWorkspaceResultState.Running);

            nav.SelectTab(1); // Blueprint → SPICE 求解阻止弹窗
            AssertGuardDialogShown(s, failures);
            ClickGuardOkButton();

            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 页面应仍为 Simulation。", failures);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": 模式应仍为 SpiceDc。", failures);
            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.NeverRun);
        }

        // 10. SPICE Running → 仿真广场：导航被拒绝
        private static void Test10_SpiceRunningToSquareBlocked(
            TopNavigationController nav, PageRouter router, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, List<string> failures)
        {
            const string s = "10";
            Cleanup(nav, null, null, modeController, spiceController);
            EnsureSpiceMode(nav, modeController, spiceController, SpiceWorkspaceResultState.Running);

            nav.SelectTab(2); // Square → SPICE 求解阻止弹窗
            AssertGuardDialogShown(s, failures);
            ClickGuardOkButton();

            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 页面应仍为 Simulation。", failures);
            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.NeverRun);
        }

        // 11. SPICE Running → 百科/工具/个人中心：导航被拒绝
        private static void Test11_SpiceRunningToOtherPagesBlocked(
            TopNavigationController nav, PageRouter router, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, List<string> failures)
        {
            var targets = new[] { 3, 4, 5 };
            var names = new[] { "Encyclopedia", "Tools", "Profile" };

            for (int i = 0; i < targets.Length; i++)
            {
                var s = "11-" + names[i];
                Cleanup(nav, null, null, modeController, spiceController);
                EnsureSpiceMode(nav, modeController, spiceController, SpiceWorkspaceResultState.Running);

                nav.SelectTab(targets[i]);
                AssertGuardDialogShown(s, failures);
                ClickGuardOkButton();

                Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 页面应仍为 Simulation。", failures);
                SetSpiceResultState(spiceController, SpiceWorkspaceResultState.NeverRun);
            }
        }

        // 12. SPICE 由 Running 变为 Current 后可以切换
        private static void Test12_SpiceRunningToCurrentCanSwitch(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, GameObject controlWorkspace, List<string> failures)
        {
            const string s = "12";
            Cleanup(nav, null, null, modeController, spiceController);
            EnsureSpiceMode(nav, modeController, spiceController, SpiceWorkspaceResultState.Running);

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);
            AssertModeSwitchDialogShown(s, failures);
            ClickModeSwitchOkButton();
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": Running 时不应切换。", failures);

            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.Current);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);

            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": Current 后应切换为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 电工 Root 应显示。", failures);
        }

        // 13. SPICE 由 Running 变为 Failed 后可以切换
        private static void Test13_SpiceRunningToFailedCanSwitch(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, GameObject controlWorkspace, List<string> failures)
        {
            const string s = "13";
            Cleanup(nav, null, null, modeController, spiceController);
            EnsureSpiceMode(nav, modeController, spiceController, SpiceWorkspaceResultState.Running);

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);
            AssertModeSwitchDialogShown(s, failures);
            ClickModeSwitchOkButton();

            SetSpiceResultState(spiceController, SpiceWorkspaceResultState.Failed);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);

            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": Failed 后应切换为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 电工 Root 应显示。", failures);
        }

        // 14. 连续点击模式选项只显示一个弹窗
        private static void Test14_ConsecutiveModeClicksSingleDialog(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, List<string> failures)
        {
            const string s = "14";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T14-Comp", "t14-comp");
            workspace.StartSimulation();

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);

            var dialogCount = CountDialogs("NavigationGuardDialog");
            Assert.IsTrue(dialogCount <= 1, s + ": 最多只能有一个弹窗，实际=" + dialogCount, failures);

            ClickGuardConfirmButton();
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": 确认后应切换为 SpiceDc。", failures);
            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
        }

        // 15. F2-B 弹窗打开时模式请求不能绕过
        private static void Test15_F2BDialogOpenModeRequestBlocked(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, PageRouter router, List<string> failures)
        {
            const string s = "15";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T15-Comp", "t15-comp");
            workspace.StartSimulation();

            nav.SelectTab(1); // F2-B 弹窗（NavigationGuardDialog）
            AssertGuardDialogShown(s, failures);

            // 尝试模式切换 → 应被阻止（IsNavigationGuardDialogOpen 为 true）
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            AssertNoModeSwitchDialog(s, failures);
            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": 模式不应改变。", failures);

            ClickGuardCancelButton(); // 取消 F2-B 弹窗
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 取消后应仍在 Simulation。", failures);
            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 取消后仿真应继续。", failures);
        }

        // 16. 当前模式选项重复点击不弹窗
        private static void Test16_SameModeClickNoDialog(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            List<string> failures)
        {
            const string s = "16";
            Cleanup(nav, null, null, modeController, null);
            // 当前为 ControlCircuit，重复点击 ControlCircuit
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);

            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": 模式应不变。", failures);
            AssertNoGuardDialog(s, failures);
            AssertNoModeSwitchDialog(s, failures);

            // 切换到 SPICE 后重复点击 SPICE
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);

            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": 模式应不变。", failures);
            AssertNoGuardDialog(s, failures);
            AssertNoModeSwitchDialog(s, failures);
        }

        // 17. 缺失 Canvas/弹窗创建失败时拒绝破坏性切换
        private static void Test17_MissingCanvasRejectsSwitch(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, List<string> failures)
        {
            const string s = "17";
            Cleanup(nav, workspace, null, modeController, null);
            SpawnTestComponent(workspace, "T17-Comp", "t17-comp");
            workspace.StartSimulation();

            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
            GameObject canvasObj = null;
            bool canvasWasActive = false;
            if (canvas != null)
            {
                canvasObj = canvas.gameObject;
                canvasWasActive = canvasObj.activeSelf;
                canvasObj.SetActive(false);
            }

            try
            {
                InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            }
            finally
            {
                if (canvasObj != null) canvasObj.SetActive(canvasWasActive);
            }

            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": Canvas 缺失时应拒绝切换，模式不变。", failures);
            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 仿真应继续。", failures);
        }

        // 18. 两个工作区数据保持
        private static void Test18_BothWorkspaceDataPreserved(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, SpiceWorkspaceController spiceController, List<string> failures)
        {
            const string s = "18";
            Cleanup(nav, workspace, null, modeController, spiceController);
            SpawnTestComponent(workspace, "T18-Elec", "t18-elec");
            var elecCountBefore = workspace.Components.Count;

            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);

            // 在 SPICE 画布添加组件
            if (spiceController != null && spiceController.Model != null)
            {
                spiceController.Model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
            }
            var spiceCountBefore = spiceController != null ? spiceController.Model.Components.Count : -1;

            // 切回电工
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);

            Assert.AreEqual(elecCountBefore, workspace.Components.Count, s + ": 电工画布组件数应保持。", failures);
            if (spiceController != null)
            {
                Assert.AreEqual(spiceCountBefore, spiceController.Model.Components.Count, s + ": SPICE 画布组件数应保持。", failures);
            }
        }

        // 19. 模式显示文字与实际 CurrentMode 始终一致
        private static void Test19_ModeDisplayConsistentWithCurrentMode(
            TopNavigationController nav, SimulationModeDropdown dropdown, SimulationModeController modeController,
            WorkspaceController workspace, SpiceWorkspaceController spiceController,
            GameObject controlWorkspace, GameObject spiceModeRoot, List<string> failures)
        {
            const string s = "19";
            Cleanup(nav, workspace, null, modeController, spiceController);

            // 初始应为 ControlCircuit
            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": 初始应为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 初始电工 Root 应显示。", failures);
            Assert.IsFalse(spiceModeRoot.activeSelf, s + ": 初始 SPICE Root 应隐藏。", failures);

            // 切换到 SPICE
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": 切换后应为 SpiceDc。", failures);
            Assert.IsFalse(controlWorkspace.activeSelf, s + ": SPICE 模式下电工 Root 应隐藏。", failures);
            Assert.IsTrue(spiceModeRoot.activeSelf, s + ": SPICE 模式下 SPICE Root 应显示。", failures);

            // 重复点击 SPICE（无操作）
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.SpiceDc);
            Assert.AreEqual(SimulationWorkspaceMode.SpiceDc, modeController.CurrentMode, s + ": 重复点击后仍应为 SpiceDc。", failures);

            // 切回电工
            InvokeSelectMode(dropdown, SimulationWorkspaceMode.ControlCircuit);
            Assert.AreEqual(SimulationWorkspaceMode.ControlCircuit, modeController.CurrentMode, s + ": 切回后应为 ControlCircuit。", failures);
            Assert.IsTrue(controlWorkspace.activeSelf, s + ": 切回后电工 Root 应显示。", failures);
            Assert.IsFalse(spiceModeRoot.activeSelf, s + ": 切回后 SPICE Root 应隐藏。", failures);
        }

        // --- 辅助方法 ---

        private static void Cleanup(
            TopNavigationController nav, WorkspaceController workspace,
            PracticeSessionController practice, SimulationModeController modeController,
            SpiceWorkspaceController spiceController)
        {
            // 即使 workspace 参数为 null，也尝试从场景查找，确保上一个测试的仿真状态被清理。
            if (workspace == null)
            {
                workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
            }
            if (workspace != null)
            {
                if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
                if (workspace.IsSimulationRunning) workspace.StopSimulation();
                workspace.ClearDrawing(true);
            }
            PracticeSessionController.Instance.ClearPracticeState();
            DestroyDialog("NavigationGuardDialog");
            DestroyDialog("ModeSwitchGuardDialog");
            DestroyDialog("LoadTemplateConfirmDialog");
            DestroyDialog("PracticeConfirmDialog");
            DestroyDialog("LockedCanvasLoadDialog");
            RuntimeStateManager.Shared.ResetAll("F2-C test cleanup");

            // 重置 SPICE ResultState
            if (spiceController != null)
            {
                SetSpiceResultState(spiceController, SpiceWorkspaceResultState.NeverRun);
            }

            // 重置模式为 ControlCircuit
            if (modeController != null && modeController.CurrentMode != SimulationWorkspaceMode.ControlCircuit)
            {
                modeController.SetMode(SimulationWorkspaceMode.ControlCircuit);
            }

            // 回到模拟电路页（绕过保护）
            if (nav != null)
            {
                var method = typeof(TopNavigationController).GetMethod("ExecuteNavigation", BindingFlags.NonPublic | BindingFlags.Instance);
                method?.Invoke(nav, new object[] { 0 });
            }
        }

        private static void EnsureSpiceMode(
            TopNavigationController nav, SimulationModeController modeController,
            SpiceWorkspaceController spiceController, SpiceWorkspaceResultState state)
        {
            if (modeController.CurrentMode != SimulationWorkspaceMode.SpiceDc)
            {
                modeController.SetMode(SimulationWorkspaceMode.SpiceDc);
            }
            SetSpiceResultState(spiceController, state);
        }

        private static void InvokeSelectMode(SimulationModeDropdown dropdown, SimulationWorkspaceMode mode)
        {
            var method = typeof(SimulationModeDropdown).GetMethod("SelectMode", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(dropdown, new object[] { mode });
        }

        private static void SetSpiceResultState(SpiceWorkspaceController controller, SpiceWorkspaceResultState state)
        {
            if (controller == null) return;
            // 直接通过反射设置 ResultState 属性，不调用 SetResultStateForTesting。
            // SetResultStateForTesting 会触发 RefreshResultStateDependentControls → RefreshParameterPanel → ClearParameterPanel，
            // 后者在 batchmode 下因 UI 未完整构建而抛 NullReferenceException。
            // 测试只需要 IsSpiceSolving 返回正确的布尔值，不需要刷新 UI 控件。
            var property = typeof(SpiceWorkspaceController).GetProperty("ResultState");
            property?.SetValue(controller, state);
        }

        private static bool IsModeControllerInitialized(SimulationModeController controller)
        {
            var field = typeof(SimulationModeController).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance);
            return field != null && (bool)field.GetValue(controller);
        }

        private static void SpawnTestComponent(WorkspaceController workspace, string displayName, string instanceId)
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

        private static void AssertGuardDialogShown(string s, List<string> failures)
        {
            var dialog = GameObject.Find("NavigationGuardDialog");
            if (dialog == null)
            {
                failures.Add(s + ": 应显示 NavigationGuardDialog，但未找到。");
            }
        }

        private static void AssertNoGuardDialog(string s, List<string> failures)
        {
            var dialog = GameObject.Find("NavigationGuardDialog");
            if (dialog != null)
            {
                failures.Add(s + ": 不应显示 NavigationGuardDialog，但已出现。");
                UnityEngine.Object.DestroyImmediate(dialog);
            }
        }

        private static void AssertModeSwitchDialogShown(string s, List<string> failures)
        {
            var dialog = GameObject.Find("ModeSwitchGuardDialog");
            if (dialog == null)
            {
                failures.Add(s + ": 应显示 ModeSwitchGuardDialog，但未找到。");
            }
        }

        private static void AssertNoModeSwitchDialog(string s, List<string> failures)
        {
            var dialog = GameObject.Find("ModeSwitchGuardDialog");
            if (dialog != null)
            {
                failures.Add(s + ": 不应显示 ModeSwitchGuardDialog，但已出现。");
                UnityEngine.Object.DestroyImmediate(dialog);
            }
        }

        private static void ClickGuardConfirmButton()
        {
            var dialog = GameObject.Find("NavigationGuardDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("Panel/ConfirmButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickGuardCancelButton()
        {
            var dialog = GameObject.Find("NavigationGuardDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("Panel/CancelButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickGuardOkButton()
        {
            var dialog = GameObject.Find("NavigationGuardDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("Panel/OkButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void ClickModeSwitchOkButton()
        {
            var dialog = GameObject.Find("ModeSwitchGuardDialog");
            if (dialog == null) return;
            var btn = dialog.transform.Find("Panel/OkButton")?.GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static int CountDialogs(string name)
        {
            return GameObject.FindObjectsOfType<GameObject>()
                .Where(go => go.name == name && go.transform.parent == null)
                .Count();
        }

        private static void DestroyDialog(string name)
        {
            var dialog = GameObject.Find(name);
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            if (target == null) return;
            var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(target, null);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(target);
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
    }
}
