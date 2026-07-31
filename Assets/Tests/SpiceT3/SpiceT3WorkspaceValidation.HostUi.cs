using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// T3 工作区自动验证。partial 文件按功能分组，RunPureChecks 保持唯一且明确的回归执行顺序。
    /// </summary>
    public static partial class SpiceT3WorkspaceValidation
    {
        private static void ValidateHostBindings()
        {
            var host = new GameObject("SpiceT3BindingValidation");
            try
            {
                var incomplete = host.AddComponent<SpiceWorkspaceViewBindings>();
                var rejected = false;
                try { incomplete.Validate(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Incomplete host bindings were accepted.");

                var palette = CreateRect(host.transform);
                var viewport = CreateRect(host.transform);
                var wires = CreateRect(host.transform);
                var components = CreateRect(host.transform);
                var overlay = CreateRect(host.transform);
                var assistant = CreateRect(host.transform);
                var parameters = CreateRect(host.transform);
                var results = CreateRect(host.transform);
                var netlist = CreateRect(host.transform);
                var diagnostics = CreateRect(host.transform);
                incomplete.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                    CreateButton(host.transform), CreateButton(host.transform), CreateButton(host.transform), CreateButton(host.transform));
                incomplete.Validate();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateSimulationModeSwitching()
        {
            var host = new GameObject("SimulationModeValidation");
            host.SetActive(false);
            try
            {
                var controller = host.AddComponent<SimulationModeController>();
                var controlTopBar = CreateRoot(host.transform);
                var controlPalette = CreateRoot(host.transform);
                var controlPaletteController = controlPalette.AddComponent<PaletteController>();
                var controlWorkspace = CreateRoot(host.transform);
                var inspector = CreateRoot(host.transform);
                var spiceRoot = CreateRoot(host.transform);
                controller.Configure(controlTopBar, controlPalette, controlPaletteController, controlWorkspace, inspector, spiceRoot);
                controller.Initialize();
                host.SetActive(true);

                if (controller.CurrentMode != SimulationWorkspaceMode.ControlCircuit || !controlTopBar.activeSelf || spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not initialize the control mode.");
                controller.SelectSpiceDc();
                if (controller.CurrentMode != SimulationWorkspaceMode.SpiceDc || controlPalette.activeSelf || !spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not preserve mutually exclusive roots.");
                controller.SelectControlCircuit();
                if (controller.CurrentMode != SimulationWorkspaceMode.ControlCircuit || !controlWorkspace.activeSelf || spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not restore the control roots.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateHiddenDemoHostInitialization()
        {
            var canvasRoot = new GameObject("SpiceDemoHostValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var spiceRoot = CreateRoot(canvasRoot.transform);
                spiceRoot.SetActive(false);
                var bindings = spiceRoot.AddComponent<SpiceWorkspaceViewBindings>();
                var workspace = spiceRoot.AddComponent<SpiceWorkspaceController>();
                var palette = CreateRect(spiceRoot.transform);
                var viewport = CreateRect(spiceRoot.transform);
                var wires = CreateRect(viewport);
                var components = CreateRect(viewport);
                var overlay = CreateRect(viewport);
                var assistant = CreateRect(spiceRoot.transform);
                var parameters = CreateRect(assistant);
                var results = CreateRect(assistant);
                var netlist = CreateRect(assistant);
                var diagnostics = CreateRect(assistant);
                bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                    CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform));

                var hostRoot = CreateRoot(canvasRoot.transform);
                hostRoot.SetActive(false);
                var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
                host.Configure(bindings, workspace);
                host.Initialize();
                host.Initialize();
                if (!host.IsInitialized || host.Controller != workspace)
                    throw new InvalidOperationException("Hidden SPICE Demo host did not initialize exactly once.");
                if (workspace.ViewController == null || workspace.ContentRect == null || workspace.WorkspaceRect != workspace.ContentRect)
                    throw new InvalidOperationException("SPICE workspace did not create one explicit view Content root.");
                if (workspace.ContentRect.parent != viewport || wires.parent != workspace.ContentRect || components.parent != workspace.ContentRect || overlay.parent != workspace.ContentRect)
                    throw new InvalidOperationException("SPICE workspace layers are not sharing the Content coordinate system.");
                workspace.ViewController.ZoomIn();
                if (Math.Abs(workspace.ViewController.CurrentScale - 1.1f) > 0.001f)
                    throw new InvalidOperationException("SPICE workspace zoom-in step is not 10 percent.");
                workspace.ViewController.ResetView();
                if (Math.Abs(workspace.ViewController.CurrentScale - 1f) > 0.001f || workspace.ContentRect.anchoredPosition.sqrMagnitude > 0.001f)
                    throw new InvalidOperationException("SPICE workspace reset view did not restore the default view state.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static RectTransform CreateRect(Transform parent)
        {
            var rect = new GameObject("BindingRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            // 编辑器纯验证没有正式场景的布局系统；提供有效尺寸后再激活根节点，
            // 以覆盖生产路径的“激活后提交工作区几何”而不接受零尺寸伪边界。
            rect.sizeDelta = new Vector2(900f, 600f);
            return rect;
        }

        private static GameObject CreateRoot(Transform parent)
        {
            var root = new GameObject("ModeRoot", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static Button CreateButton(Transform parent)
        {
            var button = new GameObject("BindingButton", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            return button;
        }

        private static Text CreateText(Transform parent)
        {
            var text = new GameObject("ModeLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private static void ValidateScrollableTextLayout()
        {
            var canvasRoot = new GameObject("SpiceScrollableTextValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var result = CreateScrollableTextForValidation(canvasRoot.transform, "Result", new Vector2(300f, 180f));
                var netlist = CreateScrollableTextForValidation(canvasRoot.transform, "Netlist", new Vector2(300f, 180f));
                var longText = string.Join("\n", new string[40].Select((_, index) => "诊断 " + index + "：该端子尚未通过导线连接。"));

                if (!SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, longText, true))
                    throw new InvalidOperationException("Long SPICE diagnostics did not update the result text.");
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(result.Content);
                if (result.Content.rect.height <= result.Viewport.rect.height)
                    throw new InvalidOperationException("Long SPICE diagnostics did not expand the actual ResultScrollView content height.");
                if (result.Viewport.GetComponent<RectMask2D>() == null || result.Content.GetComponent<VerticalLayoutGroup>() == null ||
                    result.Content.GetComponent<ContentSizeFitter>() == null || result.ScrollRect.content != result.Content || result.ScrollRect.viewport != result.Viewport)
                    throw new InvalidOperationException("SPICE scroll layout is missing its required Viewport, Content, or standard UGUI layout components.");

                result.ScrollRect.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                if (result.Content.anchoredPosition.y <= 0.01f)
                    throw new InvalidOperationException("ResultScrollView could not move its long diagnostic content to the bottom.");
                var userPosition = result.ScrollRect.verticalNormalizedPosition;
                if (SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, longText, true))
                    throw new InvalidOperationException("Unchanged SPICE diagnostics unexpectedly refreshed their scroll layout.");
                if (Math.Abs(result.ScrollRect.verticalNormalizedPosition - userPosition) > 0.001f)
                    throw new InvalidOperationException("Unchanged SPICE diagnostics reset the user's scroll position.");

                if (!SpiceScrollableTextLayout.Refresh(netlist.ScrollRect, netlist.Text, longText, true))
                    throw new InvalidOperationException("Long SPICE netlist did not update independently.");
                netlist.ScrollRect.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                if (result.ScrollRect.verticalNormalizedPosition != userPosition || result.ScrollRect.content == netlist.ScrollRect.content)
                    throw new InvalidOperationException("Result and netlist scroll views are not independent.");

                SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, "短结果", true);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(result.Content);
                if (result.Content.rect.height > result.Viewport.rect.height + 0.01f)
                    throw new InvalidOperationException("Short SPICE results left an unnecessary vertical scroll range.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceScrollableTextView CreateScrollableTextForValidation(Transform parent, string name, Vector2 size)
        {
            var scroll = new GameObject(name + "Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect)).GetComponent<ScrollRect>();
            scroll.transform.SetParent(parent, false);
            var scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.sizeDelta = size;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = SpiceScrollableTextLayout.ScrollSensitivity;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scroll.transform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            SpiceScrollableTextLayout.ConfigureContent(content);

            var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(content, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            return new SpiceScrollableTextView(scroll, viewport, content, text);
        }
    }
}
