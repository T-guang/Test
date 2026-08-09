using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Workspace;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// Command-line-only runtime probe for the actual Demo.unity input route.
    /// It is never created during normal play and leaves no scene asset changes.
    /// </summary>
    public sealed class SpiceBjt4_1_2DemoInputTraceHarness : MonoBehaviour
    {
        private const string ResultPrefix = "--spice-bjt412-result=";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateWhenRequested()
        {
            if (string.IsNullOrEmpty(GetResultPath())) return;
            var root = new GameObject("SpiceBjt4_1_2DemoInputTraceHarness");
            DontDestroyOnLoad(root);
            root.AddComponent<SpiceBjt4_1_2DemoInputTraceHarness>();
        }

        private IEnumerator Start()
        {
            // Demo scene Start methods create the parameter dialogs and settle the first layout.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            SpiceWorkspaceDemoHost host = null;
            string preflightFailure = null;
            try
            {
                var mode = FindObjectOfType<SimulationModeController>(true)
                    ?? throw new InvalidOperationException("Demo SimulationModeController is missing.");
                host = FindObjectOfType<SpiceWorkspaceDemoHost>(true)
                    ?? throw new InvalidOperationException("Demo SpiceWorkspaceDemoHost is missing.");
                mode.SelectSpiceDc();
            }
            catch (Exception exception)
            {
                preflightFailure = exception.ToString();
            }

            // Activating SpiceModeRoot registers its Images with GraphicRegistry on the next frame.
            yield return null;
            yield return new WaitForEndOfFrame();

            var trace = new StringBuilder();
            var success = false;
            RectTransform contentForStability = null;
            float contentYBeforeStabilityWait = 0f;
            try
            {
                if (!string.IsNullOrEmpty(preflightFailure)) throw new InvalidOperationException(preflightFailure);
                Canvas.ForceUpdateCanvases();

                var controller = host.Controller ?? throw new InvalidOperationException("Demo Spice controller is missing.");
                var bindings = controller.GetComponent<SpiceWorkspaceViewBindings>()
                    ?? throw new InvalidOperationException("Demo Spice bindings are missing.");
                var scroll = bindings.PaletteRoot.Find("PaletteScroll")?.GetComponent<ScrollRect>()
                    ?? throw new InvalidOperationException("Demo PaletteScroll is missing.");
                var viewport = scroll.viewport ?? throw new InvalidOperationException("Demo Palette Viewport is missing.");
                var content = scroll.content ?? throw new InvalidOperationException("Demo Palette Content is missing.");
                var workspaceView = bindings.WorkspaceViewport.GetComponent<SpiceWorkspaceViewController>()
                    ?? throw new InvalidOperationException("Demo SpiceWorkspaceViewController is missing.");
                var eventSystem = EventSystem.current ?? throw new InvalidOperationException("Demo runtime EventSystem.current is null.");

                ForceLayout(scroll);
                trace.AppendLine("SCREEN=" + Screen.width + "x" + Screen.height);
                trace.AppendLine("EVENT_SYSTEM=" + PathFor(eventSystem.transform) + ";module=" + (eventSystem.currentInputModule != null ? eventSystem.currentInputModule.GetType().Name : "null"));
                foreach (var raycaster in FindObjectsOfType<GraphicRaycaster>(true))
                    trace.AppendLine("GRAPHIC_RAYCASTER=" + PathFor(raycaster.transform) + ";active=" + raycaster.isActiveAndEnabled + ";canvas=" + (raycaster.GetComponent<Canvas>() != null ? raycaster.GetComponent<Canvas>().renderMode.ToString() : "none"));
                trace.AppendLine("PALETTE_SCROLL_ACTIVE=" + scroll.isActiveAndEnabled + ";image=" + scroll.GetComponent<Image>().raycastTarget);
                trace.AppendLine("NPN_ACTIVE=" + (content.Find(SpiceComponentKind.GenericNpnBjt + "Card")?.gameObject.activeInHierarchy ?? false) + ";NPN_IMAGE_RAYCAST=" + (content.Find(SpiceComponentKind.GenericNpnBjt + "Card")?.GetComponent<Image>()?.raycastTarget ?? false));
                trace.AppendLine("PALETTE_VIEWPORT_HEIGHT=" + viewport.rect.height.ToString("F3"));
                trace.AppendLine("PALETTE_CONTENT_HEIGHT=" + content.rect.height.ToString("F3"));
                trace.AppendLine("PALETTE_MAX_SCROLL=" + Mathf.Max(0f, content.rect.height - viewport.rect.height).ToString("F3"));
                trace.AppendLine("OUTSIDE_CLICK_BLOCKER=" + DescribeOutsideClickBlocker());

                scroll.verticalNormalizedPosition = 0f;
                ForceLayout(scroll);
                var npn = content.Find(SpiceComponentKind.GenericNpnBjt + "Card") as RectTransform
                    ?? throw new InvalidOperationException("Demo NPN card is missing.");
                TraceScroll("R1_NPN", ToScreenPoint(npn), new Vector2(0f, 1f), eventSystem, scroll, content, workspaceView, trace);

                scroll.verticalNormalizedPosition = 1f;
                ForceLayout(scroll);
                var resistor = content.Find(SpiceComponentKind.Resistor + "Card") as RectTransform
                    ?? throw new InvalidOperationException("Demo resistor card is missing.");
                TraceScroll("R2_Resistor", ToScreenPoint(resistor), new Vector2(0f, -1f), eventSystem, scroll, content, workspaceView, trace);

                TraceScroll("R3_PaletteBlank", FindViewportNonCardPoint(eventSystem, viewport, content), new Vector2(0f, -1f), eventSystem, scroll, content, workspaceView, trace);
                TraceRouteOnly("R4_Workspace", ToScreenPoint(bindings.WorkspaceViewport), eventSystem, trace);

                scroll.verticalNormalizedPosition = 1f;
                ForceLayout(scroll);
                var previousY = content.anchoredPosition.y;
                for (var index = 1; index <= 10; index++)
                {
                    var downward = new PointerEventData(eventSystem) { position = ToScreenPoint(viewport), scrollDelta = new Vector2(0f, -1f) };
                    ExecuteEvents.Execute<IScrollHandler>(scroll.gameObject, downward, ExecuteEvents.scrollHandler);
                    ForceLayout(scroll);
                    var currentY = content.anchoredPosition.y;
                    trace.AppendLine("DOWN_SCROLL_" + index + "=" + previousY.ToString("F3") + "->" + currentY.ToString("F3"));
                    if (currentY + 0.01f < previousY)
                        throw new InvalidOperationException("Palette content bounced upward during downward wheel sequence.");
                    previousY = currentY;
                }

                trace.AppendLine("PALETTE_BOTTOM_POSITION=" + content.anchoredPosition.y.ToString("F3"));
                var npnVisible = FullyVisible(npn, viewport);
                var pnp = content.Find(SpiceComponentKind.GenericPnpBjt + "Card") as RectTransform
                    ?? throw new InvalidOperationException("Demo PNP card is missing.");
                var pnpVisible = FullyVisible(pnp, viewport);
                trace.AppendLine("NPN_FULLY_REACHABLE=" + npnVisible);
                trace.AppendLine("PNP_FULLY_REACHABLE=" + pnpVisible);
                success = HasHandler(trace, "R1_NPN", "PaletteScroll") &&
                          HasHandler(trace, "R2_Resistor", "PaletteScroll") &&
                          HasHandler(trace, "R3_PaletteBlank", "PaletteScroll") &&
                          HasHandler(trace, "R4_Workspace", "SpiceWorkspaceViewport") &&
                          npnVisible && pnpVisible;
                contentForStability = content;
                contentYBeforeStabilityWait = content.anchoredPosition.y;
            }
            catch (Exception exception)
            {
                trace.AppendLine("EXCEPTION=" + exception);
            }

            if (success && contentForStability != null)
            {
                for (var index = 0; index < 30; index++)
                    yield return null;

                Canvas.ForceUpdateCanvases();
                var contentYAfterStabilityWait = contentForStability.anchoredPosition.y;
                var stable = Mathf.Approximately(contentYBeforeStabilityWait, contentYAfterStabilityWait);
                trace.AppendLine("PALETTE_CONTENT_Y_AFTER_30_FRAMES=" + contentYAfterStabilityWait.ToString("F3"));
                trace.AppendLine("PALETTE_SCROLL_STABLE_AFTER_30_FRAMES=" + stable);
                success = stable;
            }

            trace.AppendLine("SUCCESS=" + success);
            var path = GetResultPath();
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, trace.ToString(), new UTF8Encoding(false));
            Debug.Log("[BJT4.1.2] Demo input trace written: " + path + "\n" + trace);
            Application.Quit(success ? 0 : 1);
        }

        private static void TraceScroll(string name, Vector2 point, Vector2 delta, EventSystem eventSystem, ScrollRect scroll, RectTransform content, SpiceWorkspaceViewController workspaceView, StringBuilder trace)
        {
            var data = new PointerEventData(eventSystem) { position = point, scrollDelta = delta };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(data, results);
            trace.AppendLine(name + ".position=" + point);
            TraceRaycasts(name, results, trace);
            var handler = results.Count > 0 ? ExecuteEvents.GetEventHandler<IScrollHandler>(results[0].gameObject) : null;
            trace.AppendLine(name + ".handler=" + PathFor(handler != null ? handler.transform : null));
            var beforeY = content.anchoredPosition.y;
            var beforeScale = workspaceView.CurrentScale;
            if (handler != null) ExecuteEvents.Execute(handler, data, ExecuteEvents.scrollHandler);
            Canvas.ForceUpdateCanvases();
            trace.AppendLine(name + ".contentY=" + beforeY.ToString("F3") + "->" + content.anchoredPosition.y.ToString("F3"));
            trace.AppendLine(name + ".workspaceScale=" + beforeScale.ToString("F3") + "->" + workspaceView.CurrentScale.ToString("F3"));
        }

        private static void TraceRouteOnly(string name, Vector2 point, EventSystem eventSystem, StringBuilder trace)
        {
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = point }, results);
            trace.AppendLine(name + ".position=" + point);
            TraceRaycasts(name, results, trace);
            var handler = results.Count > 0 ? ExecuteEvents.GetEventHandler<IScrollHandler>(results[0].gameObject) : null;
            trace.AppendLine(name + ".handler=" + PathFor(handler != null ? handler.transform : null));
        }

        private static void TraceRaycasts(string name, List<RaycastResult> results, StringBuilder trace)
        {
            for (var index = 0; index < Mathf.Min(8, results.Count); index++)
            {
                var result = results[index];
                trace.AppendLine(name + ".raycast[" + index + "]=" + PathFor(result.gameObject != null ? result.gameObject.transform : null) +
                                 ";module=" + (result.module != null ? result.module.GetType().Name : "null") +
                                 ";depth=" + result.depth + ";sorting=" + result.sortingOrder);
            }
        }

        private static Vector2 FindViewportNonCardPoint(EventSystem eventSystem, RectTransform viewport, RectTransform content)
        {
            for (var y = 6f; y < viewport.rect.height - 6f; y += 6f)
            for (var x = 6f; x < viewport.rect.width - 6f; x += 6f)
            {
                var local = new Vector2(viewport.rect.xMin + x, viewport.rect.yMax - y);
                var point = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(local));
                var results = new List<RaycastResult>();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = point }, results);
                if (results.Count > 0 && results[0].gameObject != null && !results[0].gameObject.transform.IsChildOf(content)) return point;
            }
            return ToScreenPoint(viewport);
        }

        private static bool FullyVisible(RectTransform card, RectTransform viewport)
        {
            var corners = new Vector3[4];
            card.GetWorldCorners(corners);
            var bottom = float.MaxValue;
            var top = float.MinValue;
            for (var index = 0; index < corners.Length; index++)
            {
                var point = viewport.InverseTransformPoint(corners[index]);
                bottom = Mathf.Min(bottom, point.y);
                top = Mathf.Max(top, point.y);
            }
            return bottom >= viewport.rect.yMin + 16f && top <= viewport.rect.yMax + 0.1f;
        }

        private static void ForceLayout(ScrollRect scroll)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            scroll.Rebuild(CanvasUpdate.PostLayout);
            Canvas.ForceUpdateCanvases();
        }

        private static Vector2 ToScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));

        private static string DescribeOutsideClickBlocker()
        {
            var blocker = GameObject.Find("OutsideClickBlocker");
            return blocker == null ? "missing" : (blocker.activeSelf ? "active" : "inactive");
        }

        private static bool HasHandler(StringBuilder builder, string probe, string expectedPathSegment)
        {
            var text = builder.ToString();
            var start = text.IndexOf(probe + ".handler=", StringComparison.Ordinal);
            if (start < 0) return false;
            var end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            return text.Substring(start, end - start).Contains(expectedPathSegment);
        }

        private static string PathFor(Transform transform)
        {
            if (transform == null) return "null";
            var path = transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.name + "/" + path; }
            return path;
        }

        private static string GetResultPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith(ResultPrefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(ResultPrefix.Length).Trim('"');
            return null;
        }
    }
}
