using System;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools.SpiceT4
{
    /// <summary>
    /// 只装配 Demo 场景的 T4-A DC 宿主层。它不调用 DemoSceneBuilder，也不创建或修改电工工作区数据。
    /// 每次运行都会精确重建本工具拥有的 SpiceModeRoot 和 SimulationModeSelector，避免手工层级漂移。
    /// </summary>
    public static class SpiceT4DemoIntegrationTools
    {
        private const string DemoScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Spice/T4/Bind DC Workspace to Demo Simulation Page")]
        public static void BindDemoScene()
        {
            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var canvasRoot = RequireRoot(scene, "AppCanvas");
            var appRoot = RequireChild(canvasRoot.transform, "MainAppRoot");
            var simulationRoot = RequireChild(appRoot, "SimulationPage");
            var controlTopBar = RequireChild(simulationRoot, "TopBar").gameObject;
            var controlPalette = RequireChild(simulationRoot, "Palette").gameObject;
            var controlWorkspace = RequireChild(simulationRoot, "Workspace").gameObject;
            var demoUi = canvasRoot.GetComponent<DemoUIController>()
                ?? throw new InvalidOperationException("Demo AppCanvas is missing DemoUIController.");

            RebuildOwnedChild(simulationRoot, "ControlInspectorRoot");
            var inspectorHost = CreatePanel(simulationRoot, "ControlInspectorRoot", Color.clear);
            Anchor(inspectorHost, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            demoUi.ConfigureLocalInspectorHost(inspectorHost);

            RebuildOwnedChild(simulationRoot, "SpiceModeRoot");
            RebuildOwnedChild(simulationRoot, "SimulationModeSelector");

            var spiceRoot = CreatePanel(simulationRoot, "SpiceModeRoot", MainUiTheme.PageBackground);
            Anchor(spiceRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var spiceTopBar = CreatePanel(spiceRoot, "SpiceTopBar", MainUiTheme.PanelBackground);
            Anchor(spiceTopBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -82f), new Vector2(0f, -18f));
            var palette = CreatePanel(spiceRoot, "SpicePaletteRoot", MainUiTheme.PanelBackground);
            Anchor(palette, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(286f, -98f));
            var viewport = CreatePanel(spiceRoot, "SpiceWorkspaceViewport", new Color(0.96f, 0.98f, 1f));
            Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(302f, 0f), new Vector2(-384f, -98f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var wireLayer = CreateLayer(viewport, "SpiceWireLayer");
            var componentLayer = CreateLayer(viewport, "SpiceComponentLayer");
            var overlayLayer = CreateLayer(viewport, "SpiceOverlayLayer");
            wireLayer.SetAsFirstSibling();
            overlayLayer.SetAsLastSibling();

            var assistant = CreatePanel(spiceRoot, "SpiceAssistantRoot", MainUiTheme.PanelBackground);
            Anchor(assistant, new Vector2(1f, 0f), Vector2.one, new Vector2(-368f, 0f), new Vector2(0f, -98f));
            var parameters = CreateLayer(assistant, "ParameterPanel");
            var results = CreateLayer(assistant, "ResultPanel");
            var netlist = CreateLayer(assistant, "NetlistPanel");
            var diagnostics = CreateLayer(assistant, "DiagnosticPanel");

            var run = CreateButton(spiceTopBar, "Run", "运行计算", MainUiTheme.PrimaryBlue, Color.white);
            var rotate = CreateButton(spiceTopBar, "Rotate", "旋转", MainUiTheme.ToolbarButton, MainUiTheme.NormalText);
            var delete = CreateButton(spiceTopBar, "Delete", "删除", MainUiTheme.ToolbarButton, MainUiTheme.NormalText);
            var clear = CreateButton(spiceTopBar, "Clear", "清空", MainUiTheme.ToolbarButton, MainUiTheme.NormalText);
            Anchor(run.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(18f, -20f), new Vector2(130f, 20f));
            Anchor(rotate.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(144f, -20f), new Vector2(238f, 20f));
            Anchor(delete.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(252f, -20f), new Vector2(346f, 20f));
            Anchor(clear.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(360f, -20f), new Vector2(454f, 20f));

            var bindings = spiceRoot.gameObject.AddComponent<SpiceWorkspaceViewBindings>();
            var workspaceController = spiceRoot.gameObject.AddComponent<SpiceWorkspaceController>();
            bindings.Bind(palette, viewport, wireLayer, componentLayer, overlayLayer, assistant, parameters, results, netlist, diagnostics, run, rotate, delete, clear);
            var host = GetOrAddComponent<SpiceWorkspaceDemoHost>(simulationRoot.gameObject);
            host.Configure(bindings, workspaceController);

            var selectorRoot = CreatePanel(simulationRoot, "SimulationModeSelector", Color.clear);
            Anchor(selectorRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-352f, -80f), new Vector2(-18f, -34f));
            var selector = CreateButton(selectorRoot, "ModeButton", "", MainUiTheme.FilterButton, MainUiTheme.NormalText);
            Stretch(selector.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
            var selectorLabel = selector.GetComponentInChildren<Text>();
            var menu = CreatePanel(selectorRoot, "ModeMenu", MainUiTheme.PanelBackground);
            Anchor(menu, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -92f), new Vector2(0f, -4f));
            var controlOption = CreateButton(menu, "ControlCircuitOption", "电工控制仿真", MainUiTheme.FilterButton, MainUiTheme.NormalText);
            var spiceOption = CreateButton(menu, "SpiceDcOption", "基础电路原理仿真", MainUiTheme.FilterButton, MainUiTheme.NormalText);
            Anchor(controlOption.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(.5f, .5f), new Vector2(4f, -18f), new Vector2(-2f, 18f));
            Anchor(spiceOption.GetComponent<RectTransform>(), new Vector2(.5f, .5f), new Vector2(1f, .5f), new Vector2(2f, -18f), new Vector2(-4f, 18f));
            menu.gameObject.SetActive(false);

            var modeController = GetOrAddComponent<SimulationModeController>(simulationRoot.gameObject);
            modeController.Configure(controlTopBar, controlPalette, controlWorkspace, inspectorHost.gameObject, spiceRoot.gameObject, selector, menu.gameObject, controlOption, spiceOption, selectorLabel);
            spiceRoot.gameObject.SetActive(false);

            EditorUtility.SetDirty(demoUi);
            EditorUtility.SetDirty(host);
            EditorUtility.SetDirty(modeController);
            EditorUtility.SetDirty(bindings);
            EditorUtility.SetDirty(workspaceController);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, DemoScenePath))
            {
                throw new InvalidOperationException("Failed to save the T4-A Demo scene bindings.");
            }

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = simulationRoot.gameObject;
            Debug.Log("[SpiceT4] Bound DC workspace to Demo SimulationPage.");
        }

        [MenuItem("Tools/Spice/T4/Validate Demo DC Workspace Binding")]
        public static void ValidateDemoSceneBinding()
        {
            var scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            var canvasRoot = RequireRoot(scene, "AppCanvas");
            var simulationRoot = RequireChild(RequireChild(canvasRoot.transform, "MainAppRoot"), "SimulationPage");
            var spiceRoot = RequireChild(simulationRoot, "SpiceModeRoot");
            var inspectorRoot = RequireChild(simulationRoot, "ControlInspectorRoot");
            var selector = RequireChild(simulationRoot, "SimulationModeSelector");
            var host = simulationRoot.GetComponent<SpiceWorkspaceDemoHost>()
                ?? throw new InvalidOperationException("Demo SimulationPage is missing SpiceWorkspaceDemoHost.");
            var modeController = simulationRoot.GetComponent<SimulationModeController>()
                ?? throw new InvalidOperationException("Demo SimulationPage is missing SimulationModeController.");
            var bindings = spiceRoot.GetComponent<SpiceWorkspaceViewBindings>()
                ?? throw new InvalidOperationException("Demo SpiceModeRoot is missing SpiceWorkspaceViewBindings.");
            var controller = spiceRoot.GetComponent<SpiceWorkspaceController>()
                ?? throw new InvalidOperationException("Demo SpiceModeRoot is missing SpiceWorkspaceController.");
            bindings.Validate();
            if (host.Controller != controller || spiceRoot.gameObject.activeSelf || selector.gameObject.activeSelf == false || inspectorRoot.parent != simulationRoot)
                throw new InvalidOperationException("Demo DC workspace host bindings or default root state are invalid.");
            if (CountComponents<Canvas>(scene) != 1 || CountComponents<UnityEngine.EventSystems.EventSystem>(scene) != 1 || CountComponents<Camera>(scene) != 1)
                throw new InvalidOperationException("Demo DC workspace must reuse the existing single Canvas, EventSystem, and Camera.");
            if (modeController.CurrentMode != SimulationWorkspaceMode.ControlCircuit)
                throw new InvalidOperationException("SimulationModeController default mode is not ControlCircuit.");

            Debug.Log("[SpiceT4] Demo DC workspace binding validation passed.");
        }

        private static GameObject RequireRoot(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            throw new InvalidOperationException("Demo scene is missing root '" + name + "'.");
        }

        private static int CountComponents<T>(Scene scene) where T : Component
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                count += root.GetComponentsInChildren<T>(true).Length;
            }

            return count;
        }

        private static Transform RequireChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                throw new InvalidOperationException("Demo scene is missing required child '" + name + "' below '" + parent.name + "'.");
            }

            return child;
        }

        private static void RebuildOwnedChild(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            panel.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            return panel;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var layer = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            layer.SetParent(parent, false);
            Stretch(layer, Vector2.zero, Vector2.zero);
            return layer;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color background, Color foreground)
        {
            var buttonRoot = CreatePanel(parent, name, background);
            var button = buttonRoot.gameObject.AddComponent<Button>();
            button.targetGraphic = buttonRoot.GetComponent<Image>();
            var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(buttonRoot, false);
            text.font = MainUiTheme.UiFont;
            text.text = label;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = foreground;
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            var outline = buttonRoot.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            return button;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            Anchor(rect, Vector2.zero, Vector2.one, offsetMin, offsetMax);
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
