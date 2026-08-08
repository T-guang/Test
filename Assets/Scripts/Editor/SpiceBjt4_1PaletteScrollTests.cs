using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// SPICE-BJT-4.1 元件池滚动结构回归。
    /// 验证真实 Workspace 初始化后的层级、卡片归属、动态 Content 高度和重复展示稳定性。
    /// </summary>
    public static class SpiceBjt4_1PaletteScrollTests
    {
        private static readonly SpiceComponentKind[] PaletteOrder =
        {
            SpiceComponentKind.DcVoltageSource,
            SpiceComponentKind.DcCurrentSource,
            SpiceComponentKind.IdealSwitch,
            SpiceComponentKind.SiliconDiode,
            SpiceComponentKind.Resistor,
            SpiceComponentKind.Capacitor,
            SpiceComponentKind.Inductor,
            SpiceComponentKind.Ground,
            SpiceComponentKind.VoltageProbe,
            SpiceComponentKind.CurrentProbe,
            SpiceComponentKind.AcVoltageSource,
            SpiceComponentKind.IdealOperationalAmplifier,
            SpiceComponentKind.GenericNpnBjt,
            SpiceComponentKind.GenericPnpBjt
        };

        private static int passed;
        private static int failed;
        private static readonly StringBuilder Summary = new StringBuilder();

        [MenuItem("Tools/Tests/SPICE/BJT4.1 Palette Scroll Tests")]
        public static void RunAllTests()
        {
            passed = 0;
            failed = 0;
            Summary.Clear();
            Summary.AppendLine("# SPICE-BJT-4.1 Palette Scroll Tests (S01-S08)");

            Run("S01_HierarchyAndScrollSettings", S01_HierarchyAndScrollSettings);
            Run("S02_TitleRemainsFixed", S02_TitleRemainsFixed);
            Run("S03_AllCardsAreContentChildren", S03_AllCardsAreContentChildren);
            Run("S04_BottomBjtCardsReachable", S04_BottomBjtCardsReachable);
            Run("S05_ContentHeightTracksActualRows", S05_ContentHeightTracksActualRows);
            Run("S06_ReapplyDoesNotDuplicateHierarchy", S06_ReapplyDoesNotDuplicateHierarchy);
            Run("S07_AdapterKeepsCardsInContentAndOrder", S07_AdapterKeepsCardsInContentAndOrder);
            Run("S08_PaletteCardsRetainDragAndClickComponents", S08_PaletteCardsRetainDragAndClickComponents);

            var total = passed + failed;
            Summary.AppendLine();
            Summary.AppendLine($"RESULT: {passed}/{total} passed, {failed} failed.");
            var path = Path.Combine(LogsDirectory(), "SpiceBjt4_1PaletteScrollTests.log");
            File.WriteAllText(path, Summary.ToString());
            Debug.Log($"[BJT4.1] Summary saved: {path}");
            if (failed > 0)
                throw new InvalidOperationException($"SPICE-BJT-4.1 Palette Scroll Tests: {failed} of {total} FAILED.");
        }

        private static void S01_HierarchyAndScrollSettings()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                var scroll = GetPaletteScroll(bindings);
                var viewport = scroll.viewport;
                var content = scroll.content;
                CheckTrue(scroll.vertical, "PaletteScroll 必须启用垂直滚动。");
                CheckFalse(scroll.horizontal, "PaletteScroll 不得启用横向滚动。");
                CheckEqual(ScrollRect.MovementType.Clamped, scroll.movementType, "PaletteScroll 必须使用 Clamped。");
                CheckFalse(scroll.inertia, "PaletteScroll 必须关闭惯性，避免回弹和不可控滑动。");
                CheckTrue(viewport != null && viewport.name == "Viewport", "PaletteScroll 缺少 Viewport。");
                CheckTrue(viewport.GetComponent<RectMask2D>() != null, "Palette Viewport 必须具有 RectMask2D。");
                CheckTrue(content != null && content.name == "Content", "PaletteScroll 缺少 Content。");
                CheckTrue(content.parent == viewport, "Content 必须直属 Viewport。");
                CheckTrue(scroll.transform.parent == bindings.PaletteRoot, "PaletteScroll 必须直属 PaletteRoot。");
            });
        }

        private static void S02_TitleRemainsFixed()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                var title = bindings.PaletteRoot.Find("Title");
                var scroll = GetPaletteScroll(bindings);
                CheckTrue(title != null, "PaletteRoot 缺少标题。");
                CheckTrue(title.parent == bindings.PaletteRoot, "标题必须固定在 PaletteRoot，不能成为 Content 子节点。");
                CheckFalse(title.IsChildOf(scroll.content), "标题不得随元件卡片滚动。");
            });
        }

        private static void S03_AllCardsAreContentChildren()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                var content = GetPaletteScroll(bindings).content;
                foreach (var kind in PaletteOrder)
                {
                    var card = content.Find(kind + "Card");
                    CheckTrue(card != null, kind + " 卡片缺失。");
                    CheckTrue(card.parent == content, kind + " 卡片必须直属 Content。");
                    CheckTrue(bindings.PaletteRoot.Find(kind + "Card") == null, kind + " 卡片不得继续直属 PaletteRoot。");
                }

                CheckTrue(content.Find(SpiceComponentKind.IdealOperationalAmplifier + "Card") != null, "旧运放卡片必须仍在 Content 中。");
                CheckTrue(content.Find(SpiceComponentKind.GenericNpnBjt + "Card") != null, "NPN 卡片必须仍在 Content 中。");
                CheckTrue(content.Find(SpiceComponentKind.GenericPnpBjt + "Card") != null, "PNP 卡片必须仍在 Content 中。");
            });
        }

        private static void S04_BottomBjtCardsReachable()
        {
            foreach (var size in new[] { new Vector2(1366f, 768f), new Vector2(1920f, 1080f), new Vector2(2560f, 1440f) })
            {
                WithWorkspace(size, (bindings, _) =>
                {
                    var scroll = GetPaletteScroll(bindings);
                    Canvas.ForceUpdateCanvases();
                    scroll.verticalNormalizedPosition = 0f;
                    scroll.Rebuild(CanvasUpdate.PostLayout);
                    Canvas.ForceUpdateCanvases();

                    var pnp = scroll.content.Find(SpiceComponentKind.GenericPnpBjt + "Card") as RectTransform;
                    var npn = scroll.content.Find(SpiceComponentKind.GenericNpnBjt + "Card") as RectTransform;
                    CheckTrue(IsFullyInside(pnp, scroll.viewport), size + " 下 PNP 卡片必须完整进入 Viewport。");
                    CheckTrue(IsFullyInside(npn, scroll.viewport), size + " 下 NPN 卡片必须完整进入 Viewport。");
                });
            }
        }

        private static void S05_ContentHeightTracksActualRows()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                var scroll = GetPaletteScroll(bindings);
                var expected = 10f + 7f * 116f + 6f * 12f + 16f;
                CheckNear(expected, scroll.content.rect.height, 0.1f, "Content 高度必须按当前实际七行卡片动态计算。");
                CheckTrue(scroll.content.rect.height > scroll.viewport.rect.height, "1366×768 下 Content 必须高于 Viewport，滚动才有意义。");
            });
        }

        private static void S06_ReapplyDoesNotDuplicateHierarchy()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                InvokePresentationAdapter(bindings);
                InvokePresentationAdapter(bindings);
                var scroll = GetPaletteScroll(bindings);
                CheckEqual(1, CountNamed(bindings.PaletteRoot, "PaletteScroll"), "重复 Apply 后 PaletteScroll 不得重复。");
                CheckEqual(1, CountNamed(scroll.transform, "Viewport"), "重复 Apply 后 Viewport 不得重复。");
                CheckEqual(1, CountNamed(scroll.viewport, "Content"), "重复 Apply 后 Content 不得重复。");
                CheckEqual(PaletteOrder.Length, CountPaletteCards(scroll.content), "重复 Apply 后卡片数量不得增加。");
            });
        }

        private static void S07_AdapterKeepsCardsInContentAndOrder()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                InvokePresentationAdapter(bindings);
                var content = GetPaletteScroll(bindings).content;
                var positions = PaletteOrder.Select(kind => content.Find(kind + "Card").GetSiblingIndex()).ToArray();
                for (var index = 1; index < positions.Length; index++)
                    CheckTrue(positions[index - 1] < positions[index], "Adapter 重刷后卡片顺序不得改变。");
                CheckTrue(content.GetChild(content.childCount - 2).name == SpiceComponentKind.GenericNpnBjt + "Card", "NPN 必须保持在计划顺序的倒数第二张。");
                CheckTrue(content.GetChild(content.childCount - 1).name == SpiceComponentKind.GenericPnpBjt + "Card", "PNP 必须保持在计划顺序的最后一张。");
            });
        }

        private static void S08_PaletteCardsRetainDragAndClickComponents()
        {
            WithWorkspace(new Vector2(1366f, 768f), (bindings, _) =>
            {
                var content = GetPaletteScroll(bindings).content;
                foreach (var kind in new[] { SpiceComponentKind.Resistor, SpiceComponentKind.GenericNpnBjt, SpiceComponentKind.GenericPnpBjt })
                {
                    var card = content.Find(kind + "Card");
                    CheckTrue(card.GetComponent<SpiceWorkspacePaletteDragItem>() != null, kind + " 卡片必须保留原有拖拽/点击入口。");
                    CheckTrue(card.GetComponent<CanvasGroup>() != null, kind + " 卡片必须保留交互 CanvasGroup。");
                    CheckTrue(card.GetComponent<Image>().raycastTarget, kind + " 卡片必须保持可接收指针事件。");
                }
            });
        }

        private static void WithWorkspace(Vector2 rootSize, Action<SpiceWorkspaceViewBindings, SpiceWorkspaceController> action)
        {
            var canvasRoot = new GameObject("Bjt4_1Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, rootSize, out var bindings);
                action(bindings, workspace);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceWorkspaceController CreateInitializedWorkspace(Transform parent, Vector2 rootSize, out SpiceWorkspaceViewBindings bindings)
        {
            var spiceRoot = new GameObject("SpiceBjt4_1Workspace", typeof(RectTransform));
            spiceRoot.transform.SetParent(parent, false);
            var spiceRootRect = spiceRoot.GetComponent<RectTransform>();
            spiceRootRect.anchorMin = new Vector2(0.5f, 0.5f);
            spiceRootRect.anchorMax = new Vector2(0.5f, 0.5f);
            spiceRootRect.sizeDelta = rootSize;
            spiceRoot.SetActive(false);

            bindings = spiceRoot.AddComponent<SpiceWorkspaceViewBindings>();
            var workspace = spiceRoot.AddComponent<SpiceWorkspaceController>();
            var toolbar = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(toolbar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero);
            var palette = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(palette, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(286f, -64f));
            var viewport = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(302f, 16f), new Vector2(-384f, -80f));
            var grid = new GameObject("WorkspaceGrid", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
            grid.SetParent(viewport, false);
            SpiceWorkspaceUi.Stretch(grid, Vector2.zero, Vector2.zero);
            grid.gameObject.AddComponent<WorkspaceGrid>().raycastTarget = false;
            var wires = CreateRect(viewport);
            var components = CreateRect(viewport);
            var overlay = CreateRect(viewport);
            var assistant = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(assistant, new Vector2(1f, 0f), Vector2.one, new Vector2(-368f, 0f), new Vector2(0f, -64f));
            var parameters = CreateRect(assistant);
            var results = CreateRect(assistant);
            var netlist = CreateRect(assistant);
            var diagnostics = CreateRect(assistant);
            bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                CreateButton(toolbar), CreateButton(toolbar), CreateButton(toolbar), CreateButton(toolbar));

            var hostRoot = new GameObject("SpiceBjt4_1Host", typeof(RectTransform));
            hostRoot.transform.SetParent(parent, false);
            hostRoot.SetActive(false);
            var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
            host.Configure(bindings, workspace);
            host.Initialize();
            spiceRoot.SetActive(true);
            hostRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (!workspace.SynchronizeWorkspaceGeometryForTesting())
                throw new InvalidOperationException("BJT4.1 测试工作区未能完成激活后的几何同步。");
            return workspace;
        }

        private static ScrollRect GetPaletteScroll(SpiceWorkspaceViewBindings bindings)
        {
            var scroll = bindings.PaletteRoot.Find("PaletteScroll")?.GetComponent<ScrollRect>();
            CheckTrue(scroll != null, "PaletteScroll 未创建。");
            return scroll;
        }

        private static void InvokePresentationAdapter(SpiceWorkspaceViewBindings bindings)
        {
            var adapterType = typeof(SpiceWorkspaceController).Assembly.GetType("ElectricalSim.Spice.Workspace.SpiceWorkspacePresentationAdapter");
            var apply = adapterType?.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            CheckTrue(apply != null, "未找到 SpiceWorkspacePresentationAdapter.Apply。");
            apply.Invoke(null, new object[] { bindings });
        }

        private static bool IsFullyInside(RectTransform child, RectTransform container)
        {
            if (child == null || container == null) return false;
            var childCorners = new Vector3[4];
            var containerCorners = new Vector3[4];
            child.GetWorldCorners(childCorners);
            container.GetWorldCorners(containerCorners);
            const float tolerance = 0.1f;
            return childCorners.All(corner =>
                corner.x >= containerCorners[0].x - tolerance && corner.x <= containerCorners[2].x + tolerance &&
                corner.y >= containerCorners[0].y - tolerance && corner.y <= containerCorners[2].y + tolerance);
        }

        private static int CountNamed(Transform root, string name)
        {
            var count = root.name == name ? 1 : 0;
            for (var index = 0; index < root.childCount; index++)
                count += CountNamed(root.GetChild(index), name);
            return count;
        }

        private static int CountPaletteCards(Transform content)
        {
            var count = 0;
            for (var index = 0; index < content.childCount; index++)
                if (content.GetChild(index).name.EndsWith("Card", StringComparison.Ordinal)) count++;
            return count;
        }

        private static RectTransform CreateRect(Transform parent)
        {
            var rect = new GameObject("BindingRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(900f, 600f);
            return rect;
        }

        private static Button CreateButton(Transform parent)
        {
            var button = new GameObject("BindingButton", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            return button;
        }

        private static string LogsDirectory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Summary.AppendLine("[PASS] " + name);
                Debug.Log("[BJT4.1] PASS: " + name);
            }
            catch (Exception exception)
            {
                failed++;
                Summary.AppendLine("[FAIL] " + name + " — " + exception.Message);
                Debug.LogError("[BJT4.1] FAIL: " + name + " — " + exception.Message);
            }
        }

        private static void CheckTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFalse(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException(message);
        }

        private static void CheckEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual)) throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }

        private static void CheckNear(float expected, float actual, float tolerance, string message)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException(message + $" Expected {expected}, got {actual}.");
        }
    }
}
