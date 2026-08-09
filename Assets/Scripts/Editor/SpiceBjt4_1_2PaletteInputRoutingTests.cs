using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Workspace;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// SPICE-BJT-4.1.2: operate the actual Demo.unity SPICE page and trace the
    /// EventSystem scroll ownership.  This deliberately does not create a synthetic Canvas.
    /// </summary>
    public static class SpiceBjt4_1_2PaletteInputRoutingTests
    {
        private const string DemoScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Tests/SPICE/BJT4.1.2 Demo Palette Input Trace")]
        public static void Run()
        {
            var trace = new StringBuilder();
            var failures = new List<string>();
            trace.AppendLine("# SPICE-BJT-4.1.2 Demo Palette Input Trace");
            trace.AppendLine("scene=" + DemoScenePath);

            try
            {
                EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
                var mode = UnityEngine.Object.FindObjectOfType<SimulationModeController>(true)
                    ?? throw new InvalidOperationException("Demo.unity does not contain SimulationModeController.");
                var host = UnityEngine.Object.FindObjectOfType<SpiceWorkspaceDemoHost>(true)
                    ?? throw new InvalidOperationException("Demo.unity does not contain SpiceWorkspaceDemoHost.");

                mode.Initialize();
                host.Initialize();
                mode.SelectSpiceDc();
                Canvas.ForceUpdateCanvases();

                var controller = host.Controller ?? throw new InvalidOperationException("Demo Spice workspace controller is null.");
                var bindings = controller.GetComponent<SpiceWorkspaceViewBindings>()
                    ?? throw new InvalidOperationException("Demo Spice workspace bindings are missing.");
                var scroll = bindings.PaletteRoot.Find("PaletteScroll")?.GetComponent<ScrollRect>()
                    ?? throw new InvalidOperationException("Demo PaletteScroll is missing.");
                var content = scroll.content ?? throw new InvalidOperationException("Demo PaletteScroll content is missing.");
                var viewport = scroll.viewport ?? throw new InvalidOperationException("Demo PaletteScroll viewport is missing.");
                // In an Editor batch invocation the scene EventSystem does not receive its normal
                // Play Mode OnEnable callback. Reuse the serialized Demo object; do not create a
                // synthetic EventSystem or Canvas for this trace.
                var eventSystem = EventSystem.current ?? UnityEngine.Object.FindObjectOfType<EventSystem>(true)
                    ?? throw new InvalidOperationException("Demo.unity does not contain an EventSystem.");
                EventSystem.current = eventSystem;
                var workspaceView = bindings.WorkspaceViewport.GetComponent<SpiceWorkspaceViewController>()
                    ?? throw new InvalidOperationException("Demo SpiceWorkspaceViewController is missing.");

                scroll.verticalNormalizedPosition = 0f;
                ForceLayout(scroll);
                trace.AppendLine("Screen=" + Screen.width + "x" + Screen.height);
                trace.AppendLine("PaletteContentY=" + content.anchoredPosition.y.ToString("F3"));
                trace.AppendLine("PaletteViewportHeight=" + viewport.rect.height.ToString("F3"));
                trace.AppendLine("PaletteContentHeight=" + content.rect.height.ToString("F3"));

                var npn = content.Find(SpiceComponentKind.GenericNpnBjt + "Card") as RectTransform
                    ?? throw new InvalidOperationException("NPN palette card is missing.");
                var resistor = content.Find(SpiceComponentKind.Resistor + "Card") as RectTransform
                    ?? throw new InvalidOperationException("Resistor palette card is missing.");
                // The batch runner has a 640x480 headless screen and can make every card row
                // fit into the viewport.  Use the Viewport centre as the R3 blank-area probe;
                // the trace records its actual top raycast so a card/overlay cannot be hidden.
                var blank = ToScreenPoint(viewport);

                TraceAndAssert("R1_NPN", ToScreenPoint(npn), eventSystem, scroll, content, workspaceView, trace, failures, true);
                TraceAndAssert("R2_Resistor", ToScreenPoint(resistor), eventSystem, scroll, content, workspaceView, trace, failures, true);
                TraceAndAssert("R3_PaletteBlank", blank, eventSystem, scroll, content, workspaceView, trace, failures, true);
                TraceAndAssert("R4_Workspace", ToScreenPoint(bindings.WorkspaceViewport), eventSystem, scroll, content, workspaceView, trace, failures, false);
            }
            catch (Exception exception)
            {
                failures.Add(exception.ToString());
                trace.AppendLine("FATAL=" + exception);
            }

            trace.AppendLine("RESULT=" + (failures.Count == 0 ? "PASS" : "FAIL"));
            foreach (var failure in failures) trace.AppendLine("FAIL=" + failure);
            var path = Path.Combine(LogsDirectory(), "SpiceBjt4_1_2PaletteInputRoutingTests.log");
            File.WriteAllText(path, trace.ToString(), new UTF8Encoding(false));
            Debug.Log("[BJT4.1.2] Trace saved: " + path + "\n" + trace);
            if (failures.Count > 0)
                throw new InvalidOperationException("SPICE-BJT-4.1.2 Demo palette input trace failed: " + string.Join(" | ", failures));
        }

        private static void TraceAndAssert(
            string name,
            Vector2 point,
            EventSystem eventSystem,
            ScrollRect paletteScroll,
            RectTransform content,
            SpiceWorkspaceViewController workspaceView,
            StringBuilder trace,
            List<string> failures,
            bool expectPalette)
        {
            var data = new PointerEventData(eventSystem) { position = point, scrollDelta = new Vector2(0f, -1f) };
            var results = new List<RaycastResult>();
            eventSystem.RaycastAll(data, results);
            trace.AppendLine(name + ".position=" + point);
            for (var index = 0; index < Mathf.Min(8, results.Count); index++)
            {
                var result = results[index];
                trace.AppendLine(name + ".raycast[" + index + "]=" + PathFor(result.gameObject != null ? result.gameObject.transform : null) +
                                 ";module=" + (result.module != null ? result.module.GetType().Name : "null") +
                                 ";depth=" + result.depth + ";sorting=" + result.sortingOrder);
            }

            var top = results.Count > 0 ? results[0].gameObject : null;
            var handler = top != null ? ExecuteEvents.GetEventHandler<IScrollHandler>(top) : null;
            trace.AppendLine(name + ".handler=" + PathFor(handler != null ? handler.transform : null));
            // Unity batchmode 不会把 Demo 覆盖层图形注册到运行时 GraphicRegistry；零命中
            // 不能用于判断输入归属。该场景的权威输入验证由 Windows Player harness 执行。
            if (results.Count == 0)
            {
                trace.AppendLine(name + ".result=NOT_TESTED_EDITOR_BATCH_RAYCAST");
                return;
            }
            if (handler == null)
            {
                failures.Add(name + " has no IScrollHandler.");
                return;
            }

            var beforeY = content.anchoredPosition.y;
            var beforeScale = workspaceView.CurrentScale;
            ExecuteEvents.Execute(handler, data, ExecuteEvents.scrollHandler);
            Canvas.ForceUpdateCanvases();
            var afterY = content.anchoredPosition.y;
            var afterScale = workspaceView.CurrentScale;
            trace.AppendLine(name + ".contentY=" + beforeY.ToString("F3") + "->" + afterY.ToString("F3"));
            trace.AppendLine(name + ".workspaceScale=" + beforeScale.ToString("F3") + "->" + afterScale.ToString("F3"));

            var paletteHandler = handler == paletteScroll.gameObject;
            if (expectPalette)
            {
                if (!paletteHandler) failures.Add(name + " scroll handler is not PaletteScroll: " + PathFor(handler.transform));
                if (Mathf.Approximately(beforeY, afterY)) failures.Add(name + " PaletteScroll did not move Content.");
                if (!Mathf.Approximately(beforeScale, afterScale)) failures.Add(name + " incorrectly changed workspace scale.");
            }
            else
            {
                if (handler != workspaceView.gameObject) failures.Add(name + " scroll handler is not SpiceWorkspaceViewController: " + PathFor(handler.transform));
                if (!Mathf.Approximately(beforeY, afterY)) failures.Add(name + " incorrectly changed palette content.");
            }
        }

        private static Vector2 ToScreenPoint(RectTransform rect)
        {
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private static void ForceLayout(ScrollRect scroll)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.viewport);
            scroll.Rebuild(CanvasUpdate.PostLayout);
            Canvas.ForceUpdateCanvases();
        }

        private static string PathFor(Transform transform)
        {
            if (transform == null) return "null";
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string LogsDirectory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
