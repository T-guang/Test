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
    /// F2-B 失败关闭测试：离开模拟电路页面的导航保护。
    ///
    /// 验证：
    /// - 普通自由画布未运行时直接切换，画布保留；
    /// - 普通自由画布运行中离开时弹出"离开模拟电路"确认框，确认后停止仿真+保留画布+切换页面；
    /// - 练习模式下离开时弹出"退出当前练习"确认框，确认后停止仿真+退出练习+清空画布+隐藏参考图纸+切换页面；
    /// - 取消时页面、仿真、画布、练习会话和参考图纸全部保持不变；
    /// - 连续快速点击多个页签时最多显示一个弹窗；
    /// - 弹窗无法创建时拒绝导航。
    ///
    /// 约束：
    /// - 使用真实生产导航入口 TopNavigationController.SelectTab；
    /// - 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出。
    /// </summary>
    public static class SimulationPageNavigationGuardTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string TemplateIdA = "single_lamp_template";

        [MenuItem("Tools/Tests/Run Simulation Page Navigation Guard Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var nav = UnityEngine.Object.FindObjectOfType<TopNavigationController>(true);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var practice = PracticeSessionController.Instance;
                InvokePrivate(practice, "EnsureReferences");

                if (nav == null)
                {
                    throw new InvalidOperationException("F2-B 测试依赖缺失：TopNavigationController 未找到。");
                }
                if (workspace == null)
                {
                    throw new InvalidOperationException("F2-B 测试依赖缺失：WorkspaceController 未找到。");
                }

                // batchmode 下 Awake 可能未执行，手动触发 TopNavigationController 的初始化
                InvokePrivate(nav, "Awake");

                var router = UnityEngine.Object.FindObjectOfType<PageRouter>(true);
                if (router == null)
                {
                    // 通过反射获取 pageRouter 字段
                    var routerField = typeof(TopNavigationController).GetField("pageRouter", BindingFlags.NonPublic | BindingFlags.Instance);
                    router = routerField?.GetValue(nav) as PageRouter;
                }
                if (router == null)
                {
                    throw new InvalidOperationException("F2-B 测试依赖缺失：PageRouter 未找到且无法通过反射获取。");
                }

                Test01_NormalNotRunningToBlueprint(nav, router, workspace, failures);
                Test02_NormalNotRunningToSquare(nav, router, workspace, failures);
                Test03_NormalRunningToBlueprintConfirm(nav, router, workspace, failures);
                Test04_NormalRunningToBlueprintCancel(nav, router, workspace, failures);
                Test05_NormalRunningToSquareConfirm(nav, router, workspace, failures);
                Test06_NormalRunningToOtherPages(nav, router, workspace, failures);
                Test07_NormalRunningLockedCanvasConfirm(nav, router, workspace, failures);
                Test08_PracticeNotRunningLeaveConfirm(nav, router, workspace, practice, failures);
                Test09_PracticeRunningLeaveConfirm(nav, router, workspace, practice, failures);
                Test10_PracticeRunningLeaveCancel(nav, router, workspace, practice, failures);
                Test11_PracticeToBlueprintClearsTemplate(nav, router, workspace, practice, failures);
                Test12_PracticeToSquareConfirm(nav, router, workspace, practice, failures);
                Test13_ConsecutiveClicksSingleDialog(nav, router, workspace, failures);
                Test14_CancelThenLeaveAgain(nav, router, workspace, failures);
                Test15_NonSimulationPageSwitchNoGuard(nav, router, workspace, failures);
                Test16_ClickCurrentSimulationTabNoGuard(nav, router, workspace, failures);
                Test17_TemplateLoadThenSelectSimulation(nav, router, workspace, failures);
                Test18_PracticeEnterThenSelectSimulation(nav, router, workspace, practice, failures);
                Test19_DialogCannotCreateRejectsNav(nav, router, workspace, failures);
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
                throw new InvalidOperationException("F2-B 导航保护测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][F2-B] 导航保护：通过");
        }

        // 1. 普通画布未运行 → 图纸集：直接切换，画布保留
        private static void Test01_NormalNotRunningToBlueprint(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "01";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T01-Comp", "t01-comp");

            nav.SelectTab(1); // Blueprint

            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 当前页应为 Blueprint。", failures);
            Assert.IsTrue(workspace.Components.Count > 0, s + ": 画布组件应保留。", failures);
            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 仿真不应运行。", failures);
        }

        // 2. 普通画布未运行 → 仿真广场：直接切换
        private static void Test02_NormalNotRunningToSquare(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "02";
            Cleanup(nav, router, workspace, null);

            nav.SelectTab(2); // Square

            Assert.AreEqual(PageId.Square, router.CurrentPage, s + ": 当前页应为 Square。", failures);
        }

        // 3. 普通画布运行中 → 图纸集 → 确认：仿真停止、画布保留、页面切换
        private static void Test03_NormalRunningToBlueprintConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "03";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T03-Comp", "t03-comp");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(s + ": 前置仿真未启动。"); return; }

            nav.SelectTab(1); // Blueprint → 弹窗
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.IsTrue(workspace.Components.Count > 0, s + ": 画布组件应保留。", failures);
            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 当前页应为 Blueprint。", failures);
        }

        // 4. 普通画布运行中 → 图纸集 → 取消：仿真继续、页面不变
        private static void Test04_NormalRunningToBlueprintCancel(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "04";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T04-Comp", "t04-comp");
            workspace.StartSimulation();
            if (!workspace.IsSimulationRunning) { failures.Add(s + ": 前置仿真未启动。"); return; }

            nav.SelectTab(1); // Blueprint → 弹窗
            AssertGuardDialogShown(s, failures);
            ClickGuardCancelButton();

            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 取消后仿真应继续。", failures);
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 取消后页面应仍为 Simulation。", failures);
        }

        // 5. 普通画布运行中 → 仿真广场 → 确认
        private static void Test05_NormalRunningToSquareConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "05";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T05-Comp", "t05-comp");
            workspace.StartSimulation();

            nav.SelectTab(2); // Square → 弹窗
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.AreEqual(PageId.Square, router.CurrentPage, s + ": 当前页应为 Square。", failures);
        }

        // 6. 普通画布运行中 → 百科/工具/系统信息，各至少验证一次
        private static void Test06_NormalRunningToOtherPages(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            var targets = new[] { 3, 4, 5 }; // Encyclopedia, Tools, Profile
            var names = new[] { "Encyclopedia", "Tools", "Profile" };
            var pageIds = new[] { PageId.Encyclopedia, PageId.Tools, PageId.Profile };

            for (int i = 0; i < targets.Length; i++)
            {
                var s = "06-" + names[i];
                Cleanup(nav, router, workspace, null);
                SpawnTestComponent(workspace, "T06-Comp-" + i, "t06-comp-" + i);
                workspace.StartSimulation();

                nav.SelectTab(targets[i]);
                ClickGuardConfirmButton();

                Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
                Assert.AreEqual(pageIds[i], router.CurrentPage, s + ": 当前页应为 " + names[i] + "。", failures);
            }
        }

        // 7. 普通运行态 + 锁定画布 → 确认：停止仿真、画布和锁定状态保持
        private static void Test07_NormalRunningLockedCanvasConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "07";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T07-Comp", "t07-comp");
            workspace.ToggleInteractionLock();
            if (!workspace.IsInteractionLocked) { failures.Add(s + ": 前置锁定失败。"); return; }
            workspace.StartSimulation();

            nav.SelectTab(1); // Blueprint → 弹窗
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.IsTrue(workspace.IsInteractionLocked, s + ": 锁定状态应保持。", failures);
            Assert.IsTrue(workspace.Components.Count > 0, s + ": 画布组件应保留。", failures);
            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 当前页应为 Blueprint。", failures);
        }

        // 8. 练习未运行 → 离开 → 确认：退出练习并清空
        private static void Test08_PracticeNotRunningLeaveConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "08";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }

            nav.SelectTab(1); // Blueprint → 弹窗（练习模式）
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();

            Assert.IsFalse(practice.IsPracticeActive, s + ": 确认后练习应退出。", failures);
            Assert.IsTrue(workspace.Components.Count == 0, s + ": 画布应清空。", failures);
            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 当前页应为 Blueprint。", failures);
        }

        // 9. 练习运行中 → 离开 → 确认：停止、退出、清空、隐藏参考图
        private static void Test09_PracticeRunningLeaveConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "09";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }
            SpawnTestComponent(workspace, "T09-Comp", "t09-comp");
            workspace.StartSimulation();

            nav.SelectTab(3); // Encyclopedia → 弹窗（练习模式）
            ClickGuardConfirmButton();

            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
            Assert.IsFalse(practice.IsPracticeActive, s + ": 确认后练习应退出。", failures);
            Assert.IsTrue(workspace.Components.Count == 0, s + ": 画布应清空。", failures);
            Assert.IsNull(practice.CurrentTemplateItem, s + ": CurrentTemplateItem 应为 null。", failures);
            Assert.IsNull(practice.CurrentTemplateData, s + ": CurrentTemplateData 应为 null。", failures);
            AssertReferencePanelHidden(s, failures);
            Assert.AreEqual(PageId.Encyclopedia, router.CurrentPage, s + ": 当前页应为 Encyclopedia。", failures);
        }

        // 10. 练习运行中 → 离开 → 取消：所有状态保持
        private static void Test10_PracticeRunningLeaveCancel(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "10";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }
            SpawnTestComponent(workspace, "T10-Comp", "t10-comp");
            workspace.StartSimulation();
            var compCountBefore = workspace.Components.Count;

            nav.SelectTab(1); // Blueprint → 弹窗（练习模式）
            ClickGuardCancelButton();

            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 取消后仿真应继续。", failures);
            Assert.IsTrue(practice.IsPracticeActive, s + ": 取消后练习应保持。", failures);
            Assert.AreEqual(compCountBefore, workspace.Components.Count, s + ": 取消后画布应不变。", failures);
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 取消后页面应仍为 Simulation。", failures);
        }

        // 11. 练习 A → 图纸集确认离开：CurrentTemplateItem/Data 清空
        private static void Test11_PracticeToBlueprintClearsTemplate(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "11";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }

            nav.SelectTab(1); // Blueprint → 弹窗
            ClickGuardConfirmButton();

            Assert.IsNull(practice.CurrentTemplateItem, s + ": CurrentTemplateItem 应为 null。", failures);
            Assert.IsNull(practice.CurrentTemplateData, s + ": CurrentTemplateData 应为 null。", failures);
        }

        // 12. 练习 A → 仿真广场确认离开
        private static void Test12_PracticeToSquareConfirm(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "12";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);
            practice.StartPractice(itemA);
            if (!practice.IsPracticeActive) { failures.Add(s + ": 练习前置未建立。"); return; }

            nav.SelectTab(2); // Square → 弹窗
            ClickGuardConfirmButton();

            Assert.IsFalse(practice.IsPracticeActive, s + ": 确认后练习应退出。", failures);
            Assert.AreEqual(PageId.Square, router.CurrentPage, s + ": 当前页应为 Square。", failures);
        }

        // 13. 连续点击五个页签：只有一个弹窗、最多执行一次清理
        private static void Test13_ConsecutiveClicksSingleDialog(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "13";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T13-Comp", "t13-comp");
            workspace.StartSimulation();

            // 连续点击 5 个页签（1,2,3,4,5）
            nav.SelectTab(1);
            nav.SelectTab(2);
            nav.SelectTab(3);
            nav.SelectTab(4);
            nav.SelectTab(5);

            var dialogCount = GameObject.FindObjectsOfType<Image>()
                .Where(img => img.transform.parent == null && img.name == "NavigationGuardDialog")
                .Count();
            Assert.IsTrue(dialogCount <= 1, s + ": 最多只能有一个弹窗，实际=" + dialogCount, failures);

            ClickGuardConfirmButton();

            // 确认后应到达第一个点击的目标页（Blueprint=1）
            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 应到达第一个点击的目标页 Blueprint。", failures);
            Assert.IsFalse(workspace.IsSimulationRunning, s + ": 确认后仿真应停止。", failures);
        }

        // 14. 弹窗取消后再次离开：可以重新显示
        private static void Test14_CancelThenLeaveAgain(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "14";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T14-Comp", "t14-comp");
            workspace.StartSimulation();

            nav.SelectTab(1); // 弹窗
            ClickGuardCancelButton();
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 取消后应仍在 Simulation。", failures);

            // 再次离开
            nav.SelectTab(2); // 弹窗
            AssertGuardDialogShown(s, failures);
            ClickGuardConfirmButton();
            Assert.AreEqual(PageId.Square, router.CurrentPage, s + ": 确认后应到达 Square。", failures);
        }

        // 15. 当前已经不在模拟电路页时，在其他页面之间切换：不弹窗
        private static void Test15_NonSimulationPageSwitchNoGuard(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "15";
            Cleanup(nav, router, workspace, null);
            // 先到达 Encyclopedia（未运行，不弹窗）
            nav.SelectTab(3);
            Assert.AreEqual(PageId.Encyclopedia, router.CurrentPage, s + ": 应到达 Encyclopedia。", failures);

            // 从 Encyclopedia 切到 Tools，不应弹窗
            nav.SelectTab(4);
            Assert.AreEqual(PageId.Tools, router.CurrentPage, s + ": 应到达 Tools。", failures);
            AssertNoGuardDialog(s, failures);
        }

        // 16. 点击当前模拟电路页签：不弹窗
        private static void Test16_ClickCurrentSimulationTabNoGuard(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "16";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T16-Comp", "t16-comp");
            workspace.StartSimulation();

            // 当前在模拟电路页，点击模拟电路页签（index=0），不应弹窗
            nav.SelectTab(0);
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 应仍在 Simulation。", failures);
            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 仿真应继续。", failures);
            AssertNoGuardDialog(s, failures);
        }

        // 17. TemplateLoadController 加载成功后选择模拟电路页：不被 F2-B 阻断
        private static void Test17_TemplateLoadThenSelectSimulation(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "17";
            Cleanup(nav, router, workspace, null);
            var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
            var loader = EnsureTemplateLoader(workspace, saveLoad);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);

            // 先到达图纸集页（未运行，不弹窗）
            nav.SelectTab(1);
            Assert.AreEqual(PageId.Blueprint, router.CurrentPage, s + ": 应到达 Blueprint。", failures);
            // 从图纸集页加载模板，onLoaded 回调中调用 SelectTab(0) 回到模拟电路页
            loader.RequestLoadTemplateFromGallery(itemA, () => nav.SelectTab(0));
            // 空画布加载不弹确认框，直接加载并通过回调 SelectTab(0)
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 加载后应回到 Simulation。", failures);
            AssertNoGuardDialog(s, failures);
        }

        // 18. PracticeSessionController 成功进入练习后选择模拟电路页：不被 F2-B 阻断
        private static void Test18_PracticeEnterThenSelectSimulation(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice, List<string> failures)
        {
            const string s = "18";
            Cleanup(nav, router, workspace, practice);
            var catalog = LoadCatalog();
            var itemA = GetItem(catalog, TemplateIdA);

            // 先到达图纸集页
            nav.SelectTab(1);
            // 从图纸集页进入练习（EnterPracticeMode 会调用 SelectTab(0)）
            practice.StartPractice(itemA);
            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 进入练习后应回到 Simulation。", failures);
            Assert.IsTrue(practice.IsPracticeActive, s + ": 练习应已建立。", failures);
            AssertNoGuardDialog(s, failures);
        }

        // 19. 弹窗无法创建：导航被拒绝、当前状态不变
        private static void Test19_DialogCannotCreateRejectsNav(
            TopNavigationController nav, PageRouter router, WorkspaceController workspace, List<string> failures)
        {
            const string s = "19";
            Cleanup(nav, router, workspace, null);
            SpawnTestComponent(workspace, "T19-Comp", "t19-comp");
            workspace.StartSimulation();

            // 临时禁用 Canvas 使 FindObjectOfType<Canvas>() 返回 null
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
                nav.SelectTab(1); // 尝试离开 → 弹窗无法创建 → 拒绝导航
            }
            finally
            {
                if (canvasObj != null) canvasObj.SetActive(canvasWasActive);
            }

            Assert.AreEqual(PageId.Simulation, router.CurrentPage, s + ": 弹窗无法创建时应拒绝导航，页面不变。", failures);
            Assert.IsTrue(workspace.IsSimulationRunning, s + ": 仿真应继续。", failures);
        }

        // --- 辅助方法 ---

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

        private static void AssertReferencePanelHidden(string s, List<string> failures)
        {
            var panel = UnityEngine.Object.FindObjectOfType<BlueprintReferencePanel>(true);
            if (panel != null && panel.gameObject.activeSelf)
            {
                failures.Add(s + ": 参考图纸应已隐藏。");
            }
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

        private static void Cleanup(TopNavigationController nav, PageRouter router, WorkspaceController workspace, PracticeSessionController practice)
        {
            if (workspace != null)
            {
                if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
                if (workspace.IsSimulationRunning) workspace.StopSimulation();
                workspace.ClearDrawing(true);
            }
            // 始终清理练习状态，避免上一个测试的练习残留影响下一个测试
            PracticeSessionController.Instance.ClearPracticeState();
            DestroyDialog("NavigationGuardDialog");
            DestroyDialog("LoadTemplateConfirmDialog");
            DestroyDialog("PracticeConfirmDialog");
            DestroyDialog("LockedCanvasLoadDialog");
            RuntimeStateManager.Shared.ResetAll("F2-B test cleanup");

            // 回到模拟电路页
            if (router != null)
            {
                // 直接通过 ExecuteNavigation 重置页面，绕过保护
                var method = typeof(TopNavigationController).GetMethod("ExecuteNavigation", BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null && nav != null)
                {
                    method.Invoke(nav, new object[] { 0 });
                }
            }
        }

        private static void DestroyDialog(string name)
        {
            var dialog = GameObject.Find(name);
            if (dialog != null) UnityEngine.Object.DestroyImmediate(dialog);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            if (target == null) return;
            var method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null) method.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) field.SetValue(target, value);
        }

        private static TemplateLoadController EnsureTemplateLoader(WorkspaceController workspace, SaveLoadService saveLoad)
        {
            var loader = UnityEngine.Object.FindObjectOfType<TemplateLoadController>(true);
            if (loader == null)
            {
                loader = new GameObject("F2BTemplateLoadController").AddComponent<TemplateLoadController>();
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
    }

    // 简化断言辅助
    internal static class Assert
    {
        public static void AreEqual<T>(T expected, T actual, string message, List<string> failures)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                failures.Add(message + " 期望=" + expected + " 实际=" + actual);
            }
        }

        public static void IsTrue(bool condition, string message, List<string> failures)
        {
            if (!condition) failures.Add(message);
        }

        public static void IsFalse(bool condition, string message, List<string> failures)
        {
            if (condition) failures.Add(message);
        }

        public static void IsNull<T>(T value, string message, List<string> failures) where T : class
        {
            if (value != null) failures.Add(message);
        }
    }
}
