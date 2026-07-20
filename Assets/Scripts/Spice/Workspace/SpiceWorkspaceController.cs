using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    public enum SpiceWorkspaceResultState { NeverRun, Running, Current, Stale, Failed }

    /// <summary>
    /// 独立 DC 原型的 UGUI 宿主。SpiceWorkspaceModel 是本原型唯一的电路事实来源；
    /// 本类不读取或写入正式 WorkspaceController、WireManager、模板或检查助手。
    /// 参数和拓扑变更会使计算结果过期，纯画布移动仅刷新视图位置。
    /// </summary>
    public sealed class SpiceWorkspaceController : MonoBehaviour
    {
        private readonly Dictionary<string, SpiceWorkspaceComponentView> componentViews = new Dictionary<string, SpiceWorkspaceComponentView>(StringComparer.Ordinal);
        private readonly List<SpiceWorkspaceWireView> wireViews = new List<SpiceWorkspaceWireView>();
        private SpiceWorkspaceViewBindings bindings;
        private SpiceDcSimulationService simulationService;
        private SpiceWorkspaceComponentView selectedComponent;
        private SpiceWorkspaceWireView selectedWire;
        private SpiceWorkspaceComponentView pendingComponent;
        private string pendingTerminalId;
        private readonly List<Vector2> pendingWaypoints = new List<Vector2>();
        private bool pendingNextSegmentHorizontal;
        private SpiceWorkspaceComponentView highlightedComponent;
        private string highlightedTerminalId;
        private SpiceWorkspaceWirePreview wirePreview;
        private Image palettePreview;
        private SpiceComponentKind paletteKind;
        private bool paletteDragActive;
        private Text statusText;
        private Text resultText;
        private Text diagnosticText;
        private Text netlistText;
        private Text netlistStatusText;
        private Text parameterTitle;
        private InputField parameterInput;
        private Button unitButton;
        private Text unitLabel;
        private Button runButton;
        private Button rotateButton;
        private Button netlistToggleButton;
        private Button copyNetlistButton;
        private SpiceScrollableTextView resultView;
        private SpiceScrollableTextView netlistView;
        private SpiceScrollableTextView diagnosticView;
        private bool netlistExpanded;
        private string generatedNetlistContent;
        private string[] currentUnits = Array.Empty<string>();
        private int unitIndex;
        private bool initialized;

        public SpiceWorkspaceModel Model { get; } = new SpiceWorkspaceModel();
        public SpiceWorkspaceResultState ResultState { get; private set; } = SpiceWorkspaceResultState.NeverRun;
        public RectTransform WorkspaceRect { get; private set; }
        public RectTransform WireLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }

        /// <summary>绑定外部宿主后初始化。本控制器不创建 Canvas、EventSystem 或 Camera。</summary>
        public void Initialize(SpiceWorkspaceViewBindings hostBindings)
        {
            if (initialized) throw new InvalidOperationException("Spice workspace is already initialized.");
            bindings = hostBindings ?? throw new ArgumentNullException(nameof(hostBindings));
            bindings.Validate();
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            if (bindings == null) throw new InvalidOperationException("SpiceWorkspaceController requires explicit host bindings.");
            simulationService = new SpiceDcSimulationService();
            BuildUi();
            Model.Changed += HandleModelChanged;
            initialized = true;
        }

        private void OnDestroy()
        {
            Model.Changed -= HandleModelChanged;
        }

        private void OnDisable()
        {
            // 正式电路和结果属于控制器状态；仅清理与当前指针交互相关的瞬态 Overlay。
            CancelPendingWire();
            CancelPaletteDrag();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPendingWire();
                CancelPaletteDrag();
            }
            if (Input.GetKeyDown(KeyCode.R)) RotateSelectedComponent();
            RefreshWirePreview();
        }

        /// <summary>保留给验证 Harness 的固定位置创建入口；元件池交互改由拖放入口使用。</summary>
        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind)
        {
            EnsureInitialized();
            var offset = new Vector2(-120f + componentViews.Count * 32f, 90f - componentViews.Count * 24f);
            return CreateComponent(kind, offset);
        }

        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind, Vector2 position)
        {
            EnsureInitialized();
            var data = Model.AddComponent(kind, ClampToWorkspace(kind, position));
            CreateComponentView(data);
            SelectComponent(componentViews[data.InstanceId]);
            return data;
        }

        public void SelectPaletteKind(SpiceComponentKind kind)
        {
            paletteKind = kind;
            statusText.text = "拖动元件到画布以放置";
        }

        public void BeginPaletteDrag(SpiceComponentKind kind, Vector2 screenPosition, Camera eventCamera)
        {
            EnsureInitialized();
            CancelPendingWire();
            paletteKind = kind;
            paletteDragActive = true;
            palettePreview.gameObject.SetActive(true);
            palettePreview.GetComponentInChildren<Text>().text = PaletteLabel(kind);
            UpdatePaletteDrag(screenPosition, eventCamera);
        }

        public void UpdatePaletteDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!paletteDragActive) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(WorkspaceRect, screenPosition, eventCamera, out var local);
            palettePreview.rectTransform.anchoredPosition = local;
            palettePreview.color = RectTransformUtility.RectangleContainsScreenPoint(WorkspaceRect, screenPosition, eventCamera)
                ? new Color(0.15f, 0.39f, 0.92f, 0.22f)
                : new Color(0.39f, 0.45f, 0.55f, 0.16f);
        }

        public void EndPaletteDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!paletteDragActive) return;
            if (RectTransformUtility.RectangleContainsScreenPoint(WorkspaceRect, screenPosition, eventCamera) && TryScreenToWorkspace(screenPosition, eventCamera, out var local))
            {
                CreateComponent(paletteKind, local);
            }
            CancelPaletteDrag();
        }

        public bool TryScreenToWorkspace(Vector2 screenPosition, Camera eventCamera, out Vector2 localPosition)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(WorkspaceRect, screenPosition, eventCamera, out localPosition);
        }

        public bool Connect(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId, SpiceWireVisualState visualState = null)
        {
            EnsureInitialized();
            if (!Model.AddWire(startComponentId, startTerminalId, endComponentId, endTerminalId, visualState)) return false;
            var wire = Model.Wires[Model.Wires.Count - 1];
            wireViews.Add(new SpiceWorkspaceWireView(this, wire, componentViews[startComponentId], componentViews[endComponentId]));
            return true;
        }

        public bool TrySetParameter(string instanceId, double displayValue, string unit)
        {
            EnsureInitialized();
            var component = Model.FindComponent(instanceId);
            if (component == null || !SpiceParameterUnits.TryToSi(component.Kind, displayValue, unit, out var siValue)) return false;
            if (!Model.TrySetParameter(instanceId, siValue)) return false;
            componentViews[instanceId].RefreshAnnotation();
            return true;
        }

        public async Task<SpiceSimulationResult> RunCalculationAsync()
        {
            EnsureInitialized();
            if (ResultState == SpiceWorkspaceResultState.Running) return null;
            ResultState = SpiceWorkspaceResultState.Running;
            runButton.interactable = false;
            statusText.text = "计算中...";
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            RefreshNetlistUi();
            try
            {
                var result = await simulationService.SimulateAsync(Model.BuildCircuitModel());
                generatedNetlistContent = result.GeneratedNetlistContent;
                if (result.Success)
                {
                    ResultState = SpiceWorkspaceResultState.Current;
                    statusText.text = "结果有效";
                    SetResultText(FormatResult(result));
                }
                else
                {
                    ResultState = SpiceWorkspaceResultState.Failed;
                    statusText.text = "计算失败";
                    SetDiagnosticText(FormatDiagnostics(result));
                }
                return result;
            }
            catch (Exception exception)
            {
                ResultState = SpiceWorkspaceResultState.Failed;
                generatedNetlistContent = null;
                statusText.text = "计算失败";
                SetDiagnosticText(exception.ToString());
                return null;
            }
            finally
            {
                runButton.interactable = true;
                RefreshNetlistUi();
            }
        }

        public void RunCalculation() => RunFromButton();
        public void RotateSelection() => RotateSelectedComponent();
        public void ClearAll() => ClearWorkspace();

        public void SelectComponent(SpiceWorkspaceComponentView component)
        {
            if (pendingComponent != null && pendingComponent != component) CancelPendingWire();
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedWire = null;
            selectedComponent = component;
            if (selectedComponent != null)
            {
                selectedComponent.SetSelected(true);
                RefreshParameterPanel();
            }
            UpdateRotateAvailability();
        }

        public void SelectWire(SpiceWorkspaceWireView wire)
        {
            if (pendingComponent != null) CancelPendingWire();
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = wire;
            wire.SetSelected(true);
            parameterTitle.text = "已选择导线";
            parameterInput.interactable = false;
            unitButton.interactable = false;
            UpdateRotateAvailability();
        }

        public void ClearSelection()
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = null;
            ClearParameterPanel();
            UpdateRotateAvailability();
        }

        public void HandleTerminalClick(SpiceWorkspaceComponentView component, string terminalId)
        {
            if (pendingComponent == null)
            {
                if (ResultState == SpiceWorkspaceResultState.Running) return;
                pendingComponent = component;
                pendingTerminalId = terminalId;
                pendingNextSegmentHorizontal = SpiceWorkspaceOrthogonalRoute.IsHorizontal(component.GetTerminalDirection(terminalId));
                wirePreview = new SpiceWorkspaceWirePreview(this);
                statusText.text = "请选择第二个端子，Esc 或点击空白取消";
                return;
            }
            if (!IsPendingTargetValid(component, terminalId))
            {
                statusText.text = "该端子不能与起始元件直接连接";
                return;
            }
            var route = pendingWaypoints.Count == 0 ? SpiceWireVisualState.Auto() : SpiceWireVisualState.Manual(pendingWaypoints);
            if (!Connect(pendingComponent.InstanceId, pendingTerminalId, component.InstanceId, terminalId, route))
            {
                statusText.text = "无法建立该导线";
                return;
            }
            CancelPendingWire();
        }

        public void HandleTerminalHover(SpiceWorkspaceComponentView component, string terminalId, bool entered)
        {
            if (highlightedComponent != null)
            {
                highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, SpiceTerminalHighlightState.None);
                highlightedComponent = null;
                highlightedTerminalId = null;
            }
            if (!entered || pendingComponent == null) return;
            highlightedComponent = component;
            highlightedTerminalId = terminalId;
            component.SetTerminalHighlighted(terminalId, IsPendingTargetValid(component, terminalId) ? SpiceTerminalHighlightState.Valid : SpiceTerminalHighlightState.Invalid);
        }

        public void HandleWorkspacePointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                UndoPendingWaypoint();
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (pendingComponent == null)
            {
                ClearSelection();
                return;
            }
            if (!TryScreenToWorkspace(eventData.position, eventData.pressEventCamera, out var pointer))
            {
                CancelPendingWire();
                return;
            }
            AddPendingWaypoint(pointer);
        }

        public void MoveComponent(string instanceId, Vector2 position)
        {
            Model.MoveComponent(instanceId, position);
            foreach (var wire in wireViews) wire.Refresh();
            RefreshWirePreview();
        }

        public void RotateSelectedComponent()
        {
            if (selectedComponent == null) return;
            selectedComponent.RotateClockwise();
            foreach (var wire in wireViews) wire.Refresh();
            RefreshWirePreview();
        }

        public void DeleteSelection()
        {
            if (selectedComponent != null)
            {
                var id = selectedComponent.InstanceId;
                if (pendingComponent == selectedComponent) CancelPendingWire();
                Destroy(selectedComponent.gameObject);
                componentViews.Remove(id);
                wireViews.Where(wire => wire.Data.StartComponentId == id || wire.Data.EndComponentId == id).ToList().ForEach(RemoveWireView);
                Model.RemoveComponent(id);
                selectedComponent = null;
                ClearParameterPanel();
                UpdateRotateAvailability();
                return;
            }
            if (selectedWire != null)
            {
                RemoveWireView(selectedWire);
                selectedWire = null;
                ClearParameterPanel();
                UpdateRotateAvailability();
            }
        }

        public void ClearWorkspace()
        {
            CancelPendingWire();
            foreach (var wire in wireViews.ToList()) wire.Destroy();
            wireViews.Clear();
            foreach (var view in componentViews.Values) Destroy(view.gameObject);
            componentViews.Clear();
            selectedComponent = null;
            selectedWire = null;
            Model.Clear();
            generatedNetlistContent = null;
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
        }

        public void CancelPendingWire()
        {
            if (highlightedComponent != null) highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, SpiceTerminalHighlightState.None);
            highlightedComponent = null;
            highlightedTerminalId = null;
            pendingComponent = null;
            pendingTerminalId = null;
            pendingWaypoints.Clear();
            if (wirePreview != null)
            {
                wirePreview.Destroy();
                wirePreview = null;
            }
            if (initialized && ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
        }

        private void CancelPaletteDrag()
        {
            paletteDragActive = false;
            if (palettePreview != null) palettePreview.gameObject.SetActive(false);
        }

        private void RefreshWirePreview()
        {
            if (pendingComponent != null && wirePreview != null && TryScreenToWorkspace(Input.mousePosition, null, out var pointer))
            {
                var visualState = pendingWaypoints.Count == 0 ? SpiceWireVisualState.Auto() : SpiceWireVisualState.Manual(pendingWaypoints);
                wirePreview.Refresh(SpiceWorkspaceOrthogonalRoute.Build(
                    pendingComponent.GetTerminalPosition(pendingTerminalId),
                    pointer,
                    pendingComponent.GetTerminalDirection(pendingTerminalId),
                    visualState));
            }
        }

        private bool IsPendingTargetValid(SpiceWorkspaceComponentView component, string terminalId)
        {
            return ResultState != SpiceWorkspaceResultState.Running &&
                pendingComponent != null &&
                component != null &&
                component.Data.HasTerminal(terminalId) &&
                !string.Equals(pendingComponent.InstanceId, component.InstanceId, StringComparison.Ordinal);
        }

        private void AddPendingWaypoint(Vector2 pointer)
        {
            var anchor = pendingWaypoints.Count == 0 ? pendingComponent.GetTerminalPosition(pendingTerminalId) : pendingWaypoints[pendingWaypoints.Count - 1];
            var waypoint = SpiceWorkspaceOrthogonalRoute.ConstrainToAxis(anchor, pointer, pendingNextSegmentHorizontal);
            if ((waypoint - anchor).sqrMagnitude <= 0.0001f) return;
            pendingWaypoints.Add(waypoint);
            pendingNextSegmentHorizontal = !pendingNextSegmentHorizontal;
            RefreshWirePreview();
        }

        private void UndoPendingWaypoint()
        {
            if (pendingComponent == null) return;
            if (pendingWaypoints.Count == 0)
            {
                CancelPendingWire();
                return;
            }
            pendingWaypoints.RemoveAt(pendingWaypoints.Count - 1);
            pendingNextSegmentHorizontal = SpiceWorkspaceOrthogonalRoute.IsHorizontal(pendingComponent.GetTerminalDirection(pendingTerminalId));
            if (pendingWaypoints.Count % 2 != 0) pendingNextSegmentHorizontal = !pendingNextSegmentHorizontal;
            RefreshWirePreview();
        }

        private void UpdateRotateAvailability()
        {
            if (rotateButton != null) rotateButton.interactable = selectedComponent != null;
        }

        private void BuildUi()
        {
            var toolbar = bindings.RunButton.transform.parent;
            runButton = bindings.RunButton;
            rotateButton = bindings.RotateButton;
            runButton.onClick.AddListener(RunFromButton);
            rotateButton.onClick.AddListener(RotateSelectedComponent);
            bindings.DeleteButton.onClick.AddListener(DeleteSelection);
            bindings.ClearButton.onClick.AddListener(ClearWorkspace);
            statusText = SpiceWorkspaceUi.CreateText(toolbar.transform, "Status", "未计算", 15, FontStyle.Normal, TextAnchor.MiddleRight, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 0f), new Vector2(-20f, 0f));

            var palette = bindings.PaletteRoot;
            var paletteTitle = SpiceWorkspaceUi.CreateText(palette.transform, "Title", "基础元件", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Resistor, "电阻", "1 kOhm", 1, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Capacitor, "电容", "1 uF", 0, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Ground, "接地", "GND", 0, 2);

            var workspace = bindings.WorkspaceViewport;
            WorkspaceRect = workspace;
            workspace.gameObject.AddComponent<SpiceWorkspaceBlankClick>().Initialize(this);
            WireLayer = bindings.WireLayer;
            OverlayLayer = bindings.OverlayLayer;

            palettePreview = SpiceWorkspaceUi.CreateImage(OverlayLayer, "PaletteDragPreview", new Color(0.15f, 0.39f, 0.92f, 0.22f));
            palettePreview.rectTransform.sizeDelta = new Vector2(130f, 72f);
            palettePreview.raycastTarget = false;
            var previewLabel = SpiceWorkspaceUi.CreateText(palettePreview.transform, "Label", string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.PrimaryBlue);
            SpiceWorkspaceUi.Stretch(previewLabel.rectTransform, Vector2.zero, Vector2.zero);
            palettePreview.gameObject.SetActive(false);

            var side = bindings.AssistantRoot;
            var parameterRoot = bindings.ParameterRoot;
            var resultRoot = bindings.ResultRoot;
            var netlistRoot = bindings.NetlistRoot;
            var diagnosticRoot = bindings.DiagnosticRoot;
            var assistantTitle = SpiceWorkspaceUi.CreateText(side.transform, "AssistantTitle", "仿真助手", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(assistantTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -46f), new Vector2(-18f, -8f));
            ConfigureAssistantPanel(parameterRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -198f), new Vector2(-12f, -54f));
            ConfigureAssistantPanel(resultRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -396f), new Vector2(-12f, -206f));
            ConfigureAssistantPanel(netlistRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -564f), new Vector2(-12f, -404f));
            ConfigureAssistantPanel(diagnosticRoot, Vector2.zero, Vector2.one, new Vector2(12f, 18f), new Vector2(-12f, -572f));

            parameterTitle = SpiceWorkspaceUi.CreateText(parameterRoot, "ParameterTitle", "参数设置", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(parameterTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -38f), new Vector2(-14f, -8f));
            parameterInput = SpiceWorkspaceUi.CreateInput(parameterRoot, "ParameterInput");
            SpiceWorkspaceUi.Anchor(parameterInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(14f, -82f), new Vector2(-4f, -44f));
            unitButton = SpiceWorkspaceUi.CreateButton(parameterRoot, "Unit", "V", MainUiTheme.FilterButton, CycleUnit);
            SpiceWorkspaceUi.Anchor(unitButton.GetComponent<RectTransform>(), new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -82f), new Vector2(-14f, -44f));
            unitLabel = unitButton.GetComponentInChildren<Text>();
            var apply = SpiceWorkspaceUi.CreateButton(parameterRoot, "Apply", "应用参数", MainUiTheme.PrimaryBlue, ApplyParameter, true);
            SpiceWorkspaceUi.Anchor(apply.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f), new Vector2(14f, 10f), new Vector2(-14f, 42f));

            CreatePanelHeader(resultRoot, "ResultHeader", "计算结果", 40f);
            resultView = CreateScrollableTextView(resultRoot, "ResultScrollView", "ResultText", 14, MainUiTheme.NormalText, 48f);
            resultText = resultView.Text;

            var netlistHeader = SpiceWorkspaceUi.CreateImage(netlistRoot, "NetlistHeader", new Color(0.96f, 0.98f, 1f));
            SpiceWorkspaceUi.Anchor(netlistHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -64f));
            var netlistTitle = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Title", "生成网表", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(netlistTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-142f, -4f));
            netlistStatusText = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Status", "尚未生成网表。", 11, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(netlistStatusText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(14f, 4f), new Vector2(-14f, 0f));
            netlistToggleButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Toggle", "展开", MainUiTheme.FilterButton, ToggleNetlist);
            SpiceWorkspaceUi.Anchor(netlistToggleButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-132f, -32f), new Vector2(-76f, -6f));
            copyNetlistButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Copy", "复制", MainUiTheme.FilterButton, CopyNetlist);
            SpiceWorkspaceUi.Anchor(copyNetlistButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-70f, -32f), new Vector2(-14f, -6f));

            netlistView = CreateScrollableTextView(netlistRoot, "NetlistScrollView", "NetlistText", 12, MainUiTheme.NormalText, 72f);
            netlistText = netlistView.Text;

            CreatePanelHeader(diagnosticRoot, "DiagnosticHeader", "诊断信息", 40f);
            diagnosticView = CreateScrollableTextView(diagnosticRoot, "DiagnosticScrollView", "DiagnosticText", 13, MainUiTheme.DangerRed, 48f);
            diagnosticText = diagnosticView.Text;
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            ValidateAssistantScrollStructure();
        }

        private void CreatePaletteCard(Transform parent, SpiceComponentKind kind, string title, string summary, int column, int row)
        {
            var card = SpiceWorkspaceUi.CreateImage(parent, kind + "Card", MainUiTheme.SelectedBlue);
            var x = 16f + column * 132f;
            var y = -66f - row * 100f;
            SpiceWorkspaceUi.Anchor(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y - 82f), new Vector2(x + 116f, y));
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            card.gameObject.AddComponent<SpiceWorkspacePaletteDragItem>().Initialize(this, kind);
            var label = SpiceWorkspaceUi.CreateText(card.transform, "Title", title, 14, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(label.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-6f, -8f));
            var meta = SpiceWorkspaceUi.CreateText(card.transform, "Summary", summary, 12, FontStyle.Normal, TextAnchor.MiddleCenter, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(meta.rectTransform, Vector2.zero, new Vector2(1f, 0.45f), new Vector2(6f, 6f), new Vector2(-6f, -2f));
        }

        private void CreateComponentView(SpiceWorkspaceComponentData data)
        {
            var view = new GameObject(data.InstanceId, typeof(RectTransform), typeof(SpiceWorkspaceComponentView)).GetComponent<SpiceWorkspaceComponentView>();
            view.transform.SetParent(bindings.ComponentLayer, false);
            view.Initialize(this, data);
            componentViews.Add(data.InstanceId, view);
        }

        private void RemoveWireView(SpiceWorkspaceWireView wire)
        {
            wire.Destroy();
            wireViews.Remove(wire);
            Model.RemoveWire(wire.Data);
        }

        private void HandleModelChanged(SpiceWorkspaceChange change)
        {
            if (ResultState != SpiceWorkspaceResultState.Running && (ResultState == SpiceWorkspaceResultState.Current || !string.IsNullOrEmpty(generatedNetlistContent))) ResultState = SpiceWorkspaceResultState.Stale;
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
            RefreshNetlistUi();
        }

        // 网表仅显示 SpiceNetlistBuilder 已生成的原始文本，UI 不自行重建近似内容。
        private void ToggleNetlist()
        {
            netlistExpanded = !netlistExpanded;
            RefreshNetlistUi();
        }

        private void CopyNetlist()
        {
            if (string.IsNullOrEmpty(generatedNetlistContent)) return;
            GUIUtility.systemCopyBuffer = generatedNetlistContent;
            statusText.text = "已复制网表";
        }

        private void RefreshNetlistUi()
        {
            if (netlistView == null) return;
            var hasNetlist = !string.IsNullOrEmpty(generatedNetlistContent);
            netlistView.ScrollRect.gameObject.SetActive(netlistExpanded);
            if (netlistExpanded) RefreshScrollableText(netlistView, generatedNetlistContent ?? string.Empty);
            else netlistText.text = generatedNetlistContent ?? string.Empty;
            netlistToggleButton.GetComponentInChildren<Text>().text = netlistExpanded ? "收起" : "展开";
            copyNetlistButton.interactable = hasNetlist;
            netlistStatusText.text = NetlistStatusMessage(hasNetlist);
        }

        private static void ConfigureAssistantPanel(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            SpiceWorkspaceUi.Anchor(panel, anchorMin, anchorMax, offsetMin, offsetMax);
            var image = panel.GetComponent<Image>() ?? panel.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var outline = panel.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static void CreatePanelHeader(RectTransform panel, string name, string title, float height)
        {
            var header = SpiceWorkspaceUi.CreateImage(panel, name, new Color(0.96f, 0.98f, 1f));
            SpiceWorkspaceUi.Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, -height));
            var text = SpiceWorkspaceUi.CreateText(header.transform, "Title", title, 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Stretch(text.rectTransform, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        }

        // 每个助手信息区各自裁剪并滚动，长文本不会越过相邻 Panel 的边界。
        private static SpiceScrollableTextView CreateScrollableTextView(RectTransform panel, string scrollName, string textName, int fontSize, Color color, float topInset)
        {
            var scroll = new GameObject(scrollName, typeof(RectTransform), typeof(Image), typeof(ScrollRect)).GetComponent<ScrollRect>();
            scroll.transform.SetParent(panel, false);
            SpiceWorkspaceUi.Stretch(scroll.GetComponent<RectTransform>(), new Vector2(12f, 12f), new Vector2(-12f, -topInset));
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            scroll.horizontal = false;
            scroll.vertical = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scroll.transform, false);
            SpiceWorkspaceUi.Stretch(viewport, Vector2.zero, Vector2.zero);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            var text = SpiceWorkspaceUi.CreateText(content, textName, string.Empty, fontSize, FontStyle.Normal, TextAnchor.UpperLeft, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            SpiceWorkspaceUi.Anchor(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
            scroll.viewport = viewport;
            scroll.content = content;
            return new SpiceScrollableTextView(scroll, viewport, content, text);
        }

        // Content 高度只在文本或布局发生实际变化时按 preferredHeight 刷新，不在 Update 中轮询。
        private static void RefreshScrollableText(SpiceScrollableTextView view, string value)
        {
            view.Text.text = value;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(view.Text.rectTransform);
            var height = Mathf.Max(view.Viewport.rect.height, view.Text.preferredHeight + 12f);
            view.Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            view.ScrollRect.verticalNormalizedPosition = 1f;
        }

        private void SetResultText(string value) => RefreshScrollableText(resultView, string.IsNullOrEmpty(value) ? "尚无结果" : value);
        private void SetDiagnosticText(string value) => RefreshScrollableText(diagnosticView, value ?? string.Empty);

        /// <summary>由宿主在 Start 或更晚阶段调用，以验证 Canvas 完成布局后的三块固定信息区。</summary>
        public void ValidateAssistantPanelLayout()
        {
            Canvas.ForceUpdateCanvases();
            var resultPanel = bindings.ResultRoot;
            var netlistPanel = bindings.NetlistRoot;
            var diagnosticPanel = bindings.DiagnosticRoot;
            if (resultPanel.rect.height <= 0f || netlistPanel.rect.height <= 0f || diagnosticPanel.rect.height <= 0f)
                throw new InvalidOperationException("Spice assistant panels require positive layout height.");
            if (RectsOverlap(resultPanel, netlistPanel) || RectsOverlap(netlistPanel, diagnosticPanel)) throw new InvalidOperationException("Spice assistant panels overlap.");
        }

        private void ValidateAssistantScrollStructure()
        {
            ValidateScrollableTextView(resultView, bindings.ResultRoot);
            ValidateScrollableTextView(netlistView, bindings.NetlistRoot);
            ValidateScrollableTextView(diagnosticView, bindings.DiagnosticRoot);
            if (resultView.ScrollRect.content == netlistView.ScrollRect.content || netlistView.ScrollRect.content == diagnosticView.ScrollRect.content || resultView.ScrollRect.content == diagnosticView.ScrollRect.content)
                throw new InvalidOperationException("Spice assistant panels must not share scroll content.");
        }

        private static void ValidateScrollableTextView(SpiceScrollableTextView view, RectTransform panel)
        {
            if (view.ScrollRect.viewport != view.Viewport || view.ScrollRect.content != view.Content || view.Viewport.GetComponent<RectMask2D>() == null || !view.Viewport.IsChildOf(panel) || !view.Text.transform.IsChildOf(view.Content))
                throw new InvalidOperationException("Spice assistant scroll view bindings are incomplete.");
            if (!RectContains(panel, view.Viewport)) throw new InvalidOperationException("Spice assistant viewport extends outside its panel.");
        }

        private static bool RectsOverlap(RectTransform left, RectTransform right)
        {
            GetWorldBounds(left, out var leftMin, out var leftMax);
            GetWorldBounds(right, out var rightMin, out var rightMax);
            return leftMin.x < rightMax.x && leftMax.x > rightMin.x && leftMin.y < rightMax.y && leftMax.y > rightMin.y;
        }

        private static bool RectContains(RectTransform outer, RectTransform inner)
        {
            GetWorldBounds(outer, out var outerMin, out var outerMax);
            GetWorldBounds(inner, out var innerMin, out var innerMax);
            return innerMin.x >= outerMin.x && innerMax.x <= outerMax.x && innerMin.y >= outerMin.y && innerMax.y <= outerMax.y;
        }

        private static void GetWorldBounds(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            min = corners[0];
            max = corners[2];
        }

        private string NetlistStatusMessage(bool hasNetlist)
        {
            if (ResultState == SpiceWorkspaceResultState.NeverRun) return "尚未生成网表。";
            if (ResultState == SpiceWorkspaceResultState.Running) return "正在生成本次网表。";
            if (ResultState == SpiceWorkspaceResultState.Stale && hasNetlist) return "该网表对应修改前的电路，已过期。";
            if (ResultState == SpiceWorkspaceResultState.Failed) return hasNetlist ? "本次网表已生成，但执行或解析失败。" : "当前电路未通过校验，尚未生成新网表。";
            return hasNetlist ? "当前计算使用的网表。" : "尚未生成网表。";
        }

        private void RefreshParameterPanel()
        {
            if (selectedComponent == null || selectedComponent.Kind == SpiceComponentKind.Ground)
            {
                ClearParameterPanel();
                return;
            }
            currentUnits = SpiceParameterUnits.UnitsFor(selectedComponent.Kind);
            unitIndex = 0;
            parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
            parameterInput.interactable = true;
            unitButton.interactable = true;
            parameterInput.text = SpiceParameterUnits.FromSi(selectedComponent.Kind, selectedComponent.Data.SiValue, currentUnits[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
            unitLabel.text = currentUnits[unitIndex];
        }

        private void ClearParameterPanel()
        {
            parameterTitle.text = "参数设置";
            parameterInput.text = string.Empty;
            parameterInput.interactable = false;
            unitButton.interactable = false;
            unitLabel.text = "-";
        }

        private void CycleUnit()
        {
            if (selectedComponent == null || currentUnits.Length == 0) return;
            unitIndex = (unitIndex + 1) % currentUnits.Length;
            unitLabel.text = currentUnits[unitIndex];
            parameterInput.text = SpiceParameterUnits.FromSi(selectedComponent.Kind, selectedComponent.Data.SiValue, currentUnits[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
        }

        private void ApplyParameter()
        {
            if (selectedComponent == null || !double.TryParse(parameterInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !TrySetParameter(selectedComponent.InstanceId, value, currentUnits[unitIndex]))
            {
                statusText.text = "参数无效";
                return;
            }
            RefreshParameterPanel();
        }

        private async void RunFromButton() => await RunCalculationAsync();

        private Vector2 ClampToWorkspace(SpiceComponentKind kind, Vector2 position)
        {
            var size = SpiceWorkspaceComponentView.SizeFor(kind);
            var half = size * 0.5f;
            var bounds = WorkspaceRect.rect;
            return new Vector2(Mathf.Clamp(position.x, bounds.xMin + half.x, bounds.xMax - half.x), Mathf.Clamp(position.y, bounds.yMin + half.y, bounds.yMax - half.y));
        }

        private string StateMessage()
        {
            return ResultState == SpiceWorkspaceResultState.Stale ? "结果已过期" : ResultState == SpiceWorkspaceResultState.Failed ? "计算失败" : ResultState == SpiceWorkspaceResultState.Current ? "结果有效" : "未计算";
        }

        private static string PaletteLabel(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? "直流电压源" : kind == SpiceComponentKind.Resistor ? "电阻" : kind == SpiceComponentKind.Capacitor ? "电容" : kind == SpiceComponentKind.Inductor ? "电感" : "接地";
        }

        private static string FormatResult(SpiceSimulationResult result)
        {
            return string.Join("\n\n", result.ComponentResults.Values.OrderBy(value => value.ComponentId, StringComparer.Ordinal).Select(value =>
                value.ComponentId + "  " + value.ComponentKind + "\n电压  " + value.Voltage.ToString("G6", CultureInfo.InvariantCulture) + " V\n电流  " + value.Current.ToString("G6", CultureInfo.InvariantCulture) + " A\n参考方向：正端 → 负端" +
                (string.IsNullOrEmpty(value.Notes) ? string.Empty : "\n" + value.Notes)));
        }

        private static string FormatDiagnostics(SpiceSimulationResult result)
        {
            return string.Join("\n\n", result.Diagnostics.Select(FormatDiagnostic));
        }

        private static string FormatDiagnostic(SpiceDiagnostic diagnostic)
        {
            var title = diagnostic.Code == "SPICE_GROUND_MISSING" ? "缺少接地参考" :
                diagnostic.Code == "SPICE_FLOATING_TERMINAL" ? "存在悬空端子" :
                diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT" ? "存在未接地子电路" :
                diagnostic.Code == "SPICE_INVALID_PARAMETER" ? "元件参数无效" :
                diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION" ? "同一元件端子不能直接连接" :
                diagnostic.Code == "SPICE_SOURCE_MISSING" ? "缺少直流电压源" :
                diagnostic.Code == "SPICE_SOURCE_SHORTED" ? "电压源两端短接" :
                diagnostic.Code == "SPICE_COMPONENT_SHORTED" ? "元件两端短接" : "SPICE 计算诊断";
            var detail = diagnostic.Code == "SPICE_GROUND_MISSING" ? "电路至少需要一个 GND 作为 0 V 参考。" :
                diagnostic.Code == "SPICE_FLOATING_TERMINAL" ? "该端子尚未通过导线连接。" :
                diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT" ? "该子电路无法通过元件与导线到达 GND。" :
                diagnostic.Code == "SPICE_INVALID_PARAMETER" ? "请检查数值是否为支持范围内的 SI 参数。" :
                diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION" ? "请改为连接不同元件的端子。" :
                diagnostic.Code == "SPICE_SOURCE_MISSING" ? "当前直流工作点计算需要一个直流电压源。" :
                diagnostic.Code == "SPICE_SOURCE_SHORTED" ? "请断开电压源两端的直接短接。" :
                diagnostic.Code == "SPICE_COMPONENT_SHORTED" ? "请检查该元件两端是否被同一电气节点直接连接。" : diagnostic.Message;
            var related = string.IsNullOrEmpty(diagnostic.ComponentId) ? string.Empty : "\n关联元件：" + diagnostic.ComponentId + (string.IsNullOrEmpty(diagnostic.TerminalId) ? string.Empty : " / 端子：" + diagnostic.TerminalId);
            return title + "\n" + detail + related + "\n错误码：" + diagnostic.Code;
        }
    }

    /// <summary>固定助手 Panel 内的独立滚动文本引用；文字永远裁剪在自己的 Viewport 中。</summary>
    internal sealed class SpiceScrollableTextView
    {
        public SpiceScrollableTextView(ScrollRect scrollRect, RectTransform viewport, RectTransform content, Text text)
        {
            ScrollRect = scrollRect;
            Viewport = viewport;
            Content = content;
            Text = text;
        }

        public ScrollRect ScrollRect { get; }
        public RectTransform Viewport { get; }
        public RectTransform Content { get; }
        public Text Text { get; }
    }

    public sealed class SpiceWorkspaceBlankClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        public void Initialize(SpiceWorkspaceController workspace) { owner = workspace; }
        public void OnPointerClick(PointerEventData eventData) => owner.HandleWorkspacePointerClick(eventData);
    }

    internal static class SpiceWorkspaceUi
    {
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string value, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.UiFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var image = CreateImage(parent, name, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            var text = CreateText(image.transform, "Text", label, 14, FontStyle.Bold, TextAnchor.MiddleCenter, primary ? Color.white : MainUiTheme.NormalText);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            return button;
        }

        public static InputField CreateInput(Transform parent, string name)
        {
            var image = CreateImage(parent, name, Color.white);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            var input = image.gameObject.AddComponent<InputField>();
            var text = CreateText(image.transform, "Text", string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.NormalText);
            Stretch(text.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));
            input.textComponent = text;
            return input;
        }

        public static void Stretch(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
        public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) { rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
