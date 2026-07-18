using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 宿主提供的 SPICE 局部视图引用。它不保存电路、路由或计算结果；缺少任何必要 Root 时，
    /// 控制器拒绝初始化，避免把原型 Canvas 当作正式页面的隐式回退。
    /// </summary>
    public sealed class SpiceWorkspaceViewBindings : MonoBehaviour
    {
        [SerializeField] private RectTransform paletteRoot;
        [SerializeField] private RectTransform workspaceViewport;
        [SerializeField] private RectTransform wireLayer;
        [SerializeField] private RectTransform componentLayer;
        [SerializeField] private RectTransform overlayLayer;
        [SerializeField] private RectTransform assistantRoot;
        [SerializeField] private RectTransform parameterRoot;
        [SerializeField] private RectTransform resultRoot;
        [SerializeField] private RectTransform netlistRoot;
        [SerializeField] private RectTransform diagnosticRoot;
        [SerializeField] private Button runButton;
        [SerializeField] private Button rotateButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button clearButton;

        public RectTransform PaletteRoot => paletteRoot;
        public RectTransform WorkspaceViewport => workspaceViewport;
        public RectTransform WireLayer => wireLayer;
        public RectTransform ComponentLayer => componentLayer;
        public RectTransform OverlayLayer => overlayLayer;
        public RectTransform AssistantRoot => assistantRoot;
        public RectTransform ParameterRoot => parameterRoot;
        public RectTransform ResultRoot => resultRoot;
        public RectTransform NetlistRoot => netlistRoot;
        public RectTransform DiagnosticRoot => diagnosticRoot;
        public Button RunButton => runButton;
        public Button RotateButton => rotateButton;
        public Button DeleteButton => deleteButton;
        public Button ClearButton => clearButton;

        public void Bind(RectTransform palette, RectTransform viewport, RectTransform wires, RectTransform components, RectTransform overlay, RectTransform assistant, RectTransform parameters, RectTransform results, RectTransform netlist, RectTransform diagnostics, Button run, Button rotate, Button delete, Button clear)
        {
            paletteRoot = palette; workspaceViewport = viewport; wireLayer = wires; componentLayer = components; overlayLayer = overlay;
            assistantRoot = assistant; parameterRoot = parameters; resultRoot = results; netlistRoot = netlist; diagnosticRoot = diagnostics;
            runButton = run; rotateButton = rotate; deleteButton = delete; clearButton = clear;
        }

        public void Validate()
        {
            if (paletteRoot == null || workspaceViewport == null || wireLayer == null || componentLayer == null || overlayLayer == null || assistantRoot == null || parameterRoot == null || resultRoot == null || netlistRoot == null || diagnosticRoot == null || runButton == null || rotateButton == null || deleteButton == null || clearButton == null)
                throw new InvalidOperationException("Spice workspace host bindings are incomplete.");
        }
    }
}
