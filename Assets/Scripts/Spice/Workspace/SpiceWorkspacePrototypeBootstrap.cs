using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ElectricalSim.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 仅服务 T3 独立原型场景：装配测试用 Camera、EventSystem、Canvas 和非全屏宿主。
    /// 它不参与电气拓扑、网表构建或 ngspice 调用，正式页面接入时不会使用本类。
    /// </summary>
    public sealed class SpiceWorkspacePrototypeBootstrap : MonoBehaviour
    {
        public SpiceWorkspaceController Controller { get; private set; }

        private CanvasScaler scaler;

        private void Awake()
        {
            var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            scaler = GetComponent<CanvasScaler>();

            // UGUI 附属组件依赖同一对象上的 Canvas；延后到 Start 配置，避免动态添加组件时的 Awake 顺序竞争。
        }

        private void Start()
        {
            scaler = scaler ?? gameObject.AddComponent<CanvasScaler>();
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            new GameObject("SpiceT3PrototypeEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var camera = new GameObject("SpiceT3PrototypeCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = MainUiTheme.PageBackground;
            camera.orthographic = true;

            var host = CreatePanel(transform, "OuterTestFrame", MainUiTheme.PageBackground);
            SpiceWorkspaceUi.Anchor(host, Vector2.zero, Vector2.one, new Vector2(120f, 100f), new Vector2(-160f, -80f));
            var toolbar = CreatePanel(host, "PrototypeToolbarRoot", Color.white);
            SpiceWorkspaceUi.Anchor(toolbar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero);
            var palette = CreatePanel(host, "SpicePaletteRoot", Color.white);
            SpiceWorkspaceUi.Anchor(palette, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(286f, -64f));
            var viewport = CreatePanel(host, "SpiceWorkspaceViewport", new Color(0.96f, 0.98f, 1f));
            SpiceWorkspaceUi.Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(302f, 16f), new Vector2(-384f, -80f));
            var wires = CreateLayer(viewport, "SpiceWireLayer"); wires.SetAsFirstSibling();
            var components = CreateLayer(viewport, "SpiceComponentLayer");
            var overlay = CreateLayer(viewport, "SpiceOverlayLayer");
            var assistant = CreatePanel(host, "SpiceAssistantRoot", Color.white);
            SpiceWorkspaceUi.Anchor(assistant, new Vector2(1f, 0f), Vector2.one, new Vector2(-368f, 0f), new Vector2(0f, -64f));
            var parameters = CreateLayer(assistant, "ParameterRoot");
            var results = CreateLayer(assistant, "ResultRoot");
            var netlist = CreateLayer(assistant, "NetlistRoot");
            var diagnostics = CreateLayer(assistant, "DiagnosticRoot");
            var run = SpiceWorkspaceUi.CreateButton(toolbar, "Run", "运行计算", MainUiTheme.PrimaryBlue, null, true);
            var rotate = SpiceWorkspaceUi.CreateButton(toolbar, "Rotate", "旋转", MainUiTheme.ToolbarButton, null);
            var delete = SpiceWorkspaceUi.CreateButton(toolbar, "Delete", "删除", MainUiTheme.ToolbarButton, null);
            var clear = SpiceWorkspaceUi.CreateButton(toolbar, "Clear", "清空", MainUiTheme.ToolbarButton, null);
            SpiceWorkspaceUi.Anchor(run.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(18f, -20f), new Vector2(130f, 20f));
            SpiceWorkspaceUi.Anchor(rotate.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(144f, -20f), new Vector2(238f, 20f));
            SpiceWorkspaceUi.Anchor(delete.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(252f, -20f), new Vector2(346f, 20f));
            SpiceWorkspaceUi.Anchor(clear.GetComponent<RectTransform>(), new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(360f, -20f), new Vector2(454f, 20f));
            var bindings = GetComponent<SpiceWorkspaceViewBindings>() ?? gameObject.AddComponent<SpiceWorkspaceViewBindings>();
            bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics, run, rotate, delete, clear);
            Controller = GetComponent<SpiceWorkspaceController>() ?? gameObject.AddComponent<SpiceWorkspaceController>();
            Controller.Initialize(bindings);
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color) => SpiceWorkspaceUi.CreateImage(parent, name, color).rectTransform;
        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var layer = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            layer.SetParent(parent, false); SpiceWorkspaceUi.Stretch(layer, Vector2.zero, Vector2.zero); return layer;
        }
    }
}
