using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
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
        private SpiceDcSimulationService simulationService;
        private SpiceWorkspaceComponentView selectedComponent;
        private SpiceWorkspaceWireView selectedWire;
        private SpiceWorkspaceComponentView pendingComponent;
        private string pendingTerminalId;
        private SpiceWorkspaceComponentView highlightedComponent;
        private string highlightedTerminalId;
        private SpiceWorkspaceWirePreview wirePreview;
        private Image palettePreview;
        private SpiceComponentKind paletteKind;
        private bool paletteDragActive;
        private Text statusText;
        private Text resultText;
        private Text diagnosticText;
        private Text parameterTitle;
        private InputField parameterInput;
        private Button unitButton;
        private Text unitLabel;
        private Button runButton;
        private string[] currentUnits = Array.Empty<string>();
        private int unitIndex;
        private bool initialized;

        public SpiceWorkspaceModel Model { get; } = new SpiceWorkspaceModel();
        public SpiceWorkspaceResultState ResultState { get; private set; } = SpiceWorkspaceResultState.NeverRun;
        public RectTransform WorkspaceRect { get; private set; }
        public RectTransform WireLayer { get; private set; }

        private void Awake() => EnsureInitialized();

        private void EnsureInitialized()
        {
            if (initialized) return;
            simulationService = new SpiceDcSimulationService();
            BuildUi();
            Model.Changed += HandleModelChanged;
            initialized = true;
        }

        private void OnDestroy()
        {
            Model.Changed -= HandleModelChanged;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPendingWire();
                CancelPaletteDrag();
            }
            if (pendingComponent != null && TryScreenToWorkspace(Input.mousePosition, null, out var pointer))
            {
                wirePreview.Refresh(pendingComponent.GetTerminalPosition(pendingTerminalId), pointer);
            }
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

        public bool Connect(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            EnsureInitialized();
            if (!Model.AddWire(startComponentId, startTerminalId, endComponentId, endTerminalId)) return false;
            var wire = Model.Wires[Model.Wires.Count - 1];
            wireViews.Add(new SpiceWorkspaceWireView(this, wire, componentViews[startComponentId], componentViews[endComponentId]));
            return true;
        }

        public bool TrySetParameter(string instanceId, double displayValue, string unit)
        {
            EnsureInitialized();
            var component = Model.FindComponent(instanceId);
            if (component == null || !SpiceParameterUnits.TryToSi(component.Kind, displayValue, unit, out var siValue)) return false;
            return Model.TrySetParameter(instanceId, siValue);
        }

        public async Task<SpiceSimulationResult> RunCalculationAsync()
        {
            EnsureInitialized();
            if (ResultState == SpiceWorkspaceResultState.Running) return null;
            ResultState = SpiceWorkspaceResultState.Running;
            runButton.interactable = false;
            statusText.text = "计算中...";
            resultText.text = string.Empty;
            diagnosticText.text = string.Empty;
            try
            {
                var result = await simulationService.SimulateAsync(Model.BuildCircuitModel());
                if (result.Success)
                {
                    ResultState = SpiceWorkspaceResultState.Current;
                    statusText.text = "结果有效";
                    resultText.text = FormatResult(result);
                }
                else
                {
                    ResultState = SpiceWorkspaceResultState.Failed;
                    statusText.text = "计算失败";
                    diagnosticText.text = FormatDiagnostics(result);
                }
                return result;
            }
            catch (Exception exception)
            {
                ResultState = SpiceWorkspaceResultState.Failed;
                statusText.text = "计算失败";
                diagnosticText.text = exception.ToString();
                return null;
            }
            finally
            {
                runButton.interactable = true;
            }
        }

        public void SelectComponent(SpiceWorkspaceComponentView component)
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedWire = null;
            selectedComponent = component;
            if (selectedComponent != null)
            {
                selectedComponent.SetSelected(true);
                RefreshParameterPanel();
            }
        }

        public void SelectWire(SpiceWorkspaceWireView wire)
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = wire;
            wire.SetSelected(true);
            parameterTitle.text = "已选择导线";
            parameterInput.interactable = false;
            unitButton.interactable = false;
        }

        public void HandleTerminalClick(SpiceWorkspaceComponentView component, string terminalId)
        {
            if (pendingComponent == null)
            {
                pendingComponent = component;
                pendingTerminalId = terminalId;
                wirePreview = new SpiceWorkspaceWirePreview(this);
                statusText.text = "请选择第二个端子，Esc 或点击空白取消";
                return;
            }
            if (pendingComponent == component && pendingTerminalId == terminalId) return;
            var connected = Connect(pendingComponent.InstanceId, pendingTerminalId, component.InstanceId, terminalId);
            if (!connected) statusText.text = "无法建立该导线";
            CancelPendingWire();
        }

        public void HandleTerminalHover(SpiceWorkspaceComponentView component, string terminalId, bool entered)
        {
            if (highlightedComponent != null)
            {
                highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, false);
                highlightedComponent = null;
                highlightedTerminalId = null;
            }
            if (!entered || pendingComponent == null || (pendingComponent == component && pendingTerminalId == terminalId)) return;
            highlightedComponent = component;
            highlightedTerminalId = terminalId;
            component.SetTerminalHighlighted(terminalId, true);
        }

        public void MoveComponent(string instanceId, Vector2 position)
        {
            Model.MoveComponent(instanceId, position);
            foreach (var wire in wireViews) wire.Refresh();
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
                return;
            }
            if (selectedWire != null)
            {
                RemoveWireView(selectedWire);
                selectedWire = null;
                ClearParameterPanel();
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
            ClearParameterPanel();
        }

        public void CancelPendingWire()
        {
            if (highlightedComponent != null) highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, false);
            highlightedComponent = null;
            highlightedTerminalId = null;
            pendingComponent = null;
            pendingTerminalId = null;
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

        private void BuildUi()
        {
            var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            var root = SpiceWorkspaceUi.CreateImage(transform, "SpiceT3Root", MainUiTheme.PageBackground);
            SpiceWorkspaceUi.Stretch(root.rectTransform, Vector2.zero, Vector2.zero);
            var toolbar = SpiceWorkspaceUi.CreateImage(root.transform, "Toolbar", Color.white);
            SpiceWorkspaceUi.Anchor(toolbar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero);
            var toolbarOutline = toolbar.gameObject.AddComponent<Outline>();
            toolbarOutline.effectColor = MainUiTheme.Divider;
            toolbarOutline.effectDistance = new Vector2(0f, -1f);
            runButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Run", "运行计算", MainUiTheme.PrimaryBlue, RunFromButton, true);
            SpiceWorkspaceUi.Anchor(runButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, -20f), new Vector2(130f, 20f));
            var deleteButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Delete", "删除", MainUiTheme.ToolbarButton, DeleteSelection);
            SpiceWorkspaceUi.Anchor(deleteButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(144f, -20f), new Vector2(238f, 20f));
            var clearButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Clear", "清空", MainUiTheme.ToolbarButton, ClearWorkspace);
            SpiceWorkspaceUi.Anchor(clearButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(252f, -20f), new Vector2(346f, 20f));
            statusText = SpiceWorkspaceUi.CreateText(toolbar.transform, "Status", "未计算", 15, FontStyle.Normal, TextAnchor.MiddleRight, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 0f), new Vector2(-20f, 0f));

            var palette = SpiceWorkspaceUi.CreateImage(root.transform, "Palette", Color.white);
            SpiceWorkspaceUi.Anchor(palette.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(286f, -64f));
            var paletteOutline = palette.gameObject.AddComponent<Outline>();
            paletteOutline.effectColor = MainUiTheme.Divider;
            paletteOutline.effectDistance = new Vector2(1f, 0f);
            var paletteTitle = SpiceWorkspaceUi.CreateText(palette.transform, "Title", "基础元件", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Resistor, "电阻", "1 kOhm", 1, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Capacitor, "电容", "1 uF", 0, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Ground, "接地", "GND", 0, 2);

            var workspace = SpiceWorkspaceUi.CreateImage(root.transform, "Workspace", new Color(0.96f, 0.98f, 1f));
            SpiceWorkspaceUi.Anchor(workspace.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(302f, 16f), new Vector2(-384f, -80f));
            WorkspaceRect = workspace.rectTransform;
            workspace.gameObject.AddComponent<SpiceWorkspaceBlankClick>().Initialize(this);
            var wireLayer = new GameObject("WireLayer", typeof(RectTransform));
            wireLayer.transform.SetParent(workspace.transform, false);
            WireLayer = wireLayer.GetComponent<RectTransform>();
            SpiceWorkspaceUi.Stretch(WireLayer, Vector2.zero, Vector2.zero);
            WireLayer.SetAsFirstSibling();

            palettePreview = SpiceWorkspaceUi.CreateImage(workspace.transform, "PaletteDragPreview", new Color(0.15f, 0.39f, 0.92f, 0.22f));
            palettePreview.rectTransform.sizeDelta = new Vector2(130f, 72f);
            palettePreview.raycastTarget = false;
            var previewLabel = SpiceWorkspaceUi.CreateText(palettePreview.transform, "Label", string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.PrimaryBlue);
            SpiceWorkspaceUi.Stretch(previewLabel.rectTransform, Vector2.zero, Vector2.zero);
            palettePreview.gameObject.SetActive(false);

            var side = SpiceWorkspaceUi.CreateImage(root.transform, "Inspector", Color.white);
            SpiceWorkspaceUi.Anchor(side.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-368f, 0f), new Vector2(0f, -64f));
            var sideOutline = side.gameObject.AddComponent<Outline>();
            sideOutline.effectColor = MainUiTheme.Divider;
            sideOutline.effectDistance = new Vector2(-1f, 0f);
            var assistantTitle = SpiceWorkspaceUi.CreateText(side.transform, "AssistantTitle", "仿真助手", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(assistantTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -46f), new Vector2(-18f, -8f));
            parameterTitle = SpiceWorkspaceUi.CreateText(side.transform, "ParameterTitle", "参数设置", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(parameterTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -86f), new Vector2(-18f, -54f));
            parameterInput = SpiceWorkspaceUi.CreateInput(side.transform, "ParameterInput");
            SpiceWorkspaceUi.Anchor(parameterInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(18f, -132f), new Vector2(-4f, -94f));
            unitButton = SpiceWorkspaceUi.CreateButton(side.transform, "Unit", "V", MainUiTheme.FilterButton, CycleUnit);
            SpiceWorkspaceUi.Anchor(unitButton.GetComponent<RectTransform>(), new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -132f), new Vector2(-18f, -94f));
            unitLabel = unitButton.GetComponentInChildren<Text>();
            var apply = SpiceWorkspaceUi.CreateButton(side.transform, "Apply", "应用参数", MainUiTheme.PrimaryBlue, ApplyParameter, true);
            SpiceWorkspaceUi.Anchor(apply.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -178f), new Vector2(-18f, -140f));
            var resultTitle = SpiceWorkspaceUi.CreateText(side.transform, "ResultTitle", "计算结果", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(resultTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -222f), new Vector2(-18f, -190f));
            resultText = SpiceWorkspaceUi.CreateText(side.transform, "Results", "尚无结果", 14, FontStyle.Normal, TextAnchor.UpperLeft, MainUiTheme.NormalText);
            resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            resultText.verticalOverflow = VerticalWrapMode.Overflow;
            SpiceWorkspaceUi.Anchor(resultText.rectTransform, new Vector2(0f, 0.38f), new Vector2(1f, 1f), new Vector2(18f, 0f), new Vector2(-18f, -228f));
            var diagnosticTitle = SpiceWorkspaceUi.CreateText(side.transform, "DiagnosticTitle", "诊断信息", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(diagnosticTitle.rectTransform, new Vector2(0f, 0.34f), new Vector2(1f, 0.4f), new Vector2(18f, 0f), new Vector2(-18f, 0f));
            diagnosticText = SpiceWorkspaceUi.CreateText(side.transform, "Diagnostics", "", 13, FontStyle.Normal, TextAnchor.UpperLeft, MainUiTheme.DangerRed);
            diagnosticText.horizontalOverflow = HorizontalWrapMode.Wrap;
            diagnosticText.verticalOverflow = VerticalWrapMode.Overflow;
            SpiceWorkspaceUi.Anchor(diagnosticText.rectTransform, Vector2.zero, new Vector2(1f, 0.34f), new Vector2(18f, 16f), new Vector2(-18f, -6f));
            ClearParameterPanel();
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
            view.transform.SetParent(WorkspaceRect, false);
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
            if (ResultState == SpiceWorkspaceResultState.Current) ResultState = SpiceWorkspaceResultState.Stale;
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
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
            var size = kind == SpiceComponentKind.Ground ? new Vector2(108f, 80f) : new Vector2(156f, 96f);
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
                value.ComponentId + "  " + value.ComponentKind + "\n电压  " + value.Voltage.ToString("G6", CultureInfo.InvariantCulture) + " V\n电流  " + value.Current.ToString("G6", CultureInfo.InvariantCulture) + " A (positive -> negative)" +
                (string.IsNullOrEmpty(value.Notes) ? string.Empty : "\n" + value.Notes)));
        }

        private static string FormatDiagnostics(SpiceSimulationResult result)
        {
            return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + (string.IsNullOrEmpty(diagnostic.ComponentId) ? string.Empty : " [" + diagnostic.ComponentId + "]")));
        }
    }

    public sealed class SpiceWorkspaceBlankClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        public void Initialize(SpiceWorkspaceController workspace) { owner = workspace; }
        public void OnPointerClick(PointerEventData eventData) { owner.CancelPendingWire(); }
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
            button.onClick.AddListener(action);
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
