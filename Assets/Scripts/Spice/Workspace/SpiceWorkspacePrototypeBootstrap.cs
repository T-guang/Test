using System;
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

        [SerializeField] private Canvas prototypeCanvas;
        [SerializeField] private CanvasScaler prototypeCanvasScaler;
        [SerializeField] private GraphicRaycaster prototypeGraphicRaycaster;
        [SerializeField] private EventSystem prototypeEventSystem;
        [SerializeField] private StandaloneInputModule prototypeInputModule;
        [SerializeField] private Camera prototypeCamera;
        [SerializeField] private SpiceWorkspaceViewBindings viewBindings;
        [SerializeField] private SpiceWorkspaceController workspaceController;
        private bool initialized;

        /// <summary>
        /// 仅由 Editor 场景生成器写入原型基础设施引用；本方法不创建对象或初始化工作区。
        /// </summary>
        public void ConfigurePrototypeInfrastructure(
            Canvas canvas,
            CanvasScaler canvasScaler,
            GraphicRaycaster graphicRaycaster,
            EventSystem eventSystem,
            StandaloneInputModule inputModule,
            Camera camera,
            SpiceWorkspaceViewBindings bindings,
            SpiceWorkspaceController controller)
        {
            prototypeCanvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            prototypeCanvasScaler = canvasScaler ?? throw new ArgumentNullException(nameof(canvasScaler));
            prototypeGraphicRaycaster = graphicRaycaster ?? throw new ArgumentNullException(nameof(graphicRaycaster));
            prototypeEventSystem = eventSystem ?? throw new ArgumentNullException(nameof(eventSystem));
            prototypeInputModule = inputModule ?? throw new ArgumentNullException(nameof(inputModule));
            prototypeCamera = camera ?? throw new ArgumentNullException(nameof(camera));
            viewBindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            workspaceController = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        private void Awake()
        {
            ValidatePrototypeInfrastructure();

            // 只在已序列化的 Canvas 下创建原型局部页面，不补齐全局 UGUI 基础设施。
            var host = CreatePanel(prototypeCanvas.transform, "OuterTestFrame", MainUiTheme.PageBackground);
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
            viewBindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics, run, rotate, delete, clear);
            workspaceController.Initialize(viewBindings);
            Controller = workspaceController;
            initialized = true;
        }

        private void ValidatePrototypeInfrastructure()
        {
            if (initialized || Controller != null) throw new InvalidOperationException("Spice T3 prototype bootstrap was initialized more than once.");
            if (prototypeCanvas == null || prototypeCanvasScaler == null || prototypeGraphicRaycaster == null || prototypeEventSystem == null || prototypeInputModule == null || prototypeCamera == null || viewBindings == null || workspaceController == null)
                throw new InvalidOperationException("Spice T3 prototype scene is missing its serialized Canvas infrastructure. Regenerate the scene through Tools > Spice > T3 > Open Workspace Prototype.");
            if (prototypeCanvas.gameObject != prototypeCanvasScaler.gameObject || prototypeCanvas.gameObject != prototypeGraphicRaycaster.gameObject)
                throw new InvalidOperationException("Spice T3 prototype Canvas, CanvasScaler, and GraphicRaycaster must share one GameObject.");
            if (prototypeEventSystem.gameObject != prototypeInputModule.gameObject)
                throw new InvalidOperationException("Spice T3 prototype EventSystem and input module must share one GameObject.");
            if (prototypeCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                throw new InvalidOperationException("Spice T3 prototype Canvas must use ScreenSpaceOverlay.");
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color) => SpiceWorkspaceUi.CreateImage(parent, name, color).rectTransform;
        private static RectTransform CreateLayer(Transform parent, string name)
        {
            var layer = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            layer.SetParent(parent, false); SpiceWorkspaceUi.Stretch(layer, Vector2.zero, Vector2.zero); return layer;
        }
    }
}
