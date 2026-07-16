using System.Collections.Generic;
using UnityEngine;
using ElectricalSim.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 管理可编辑电路工作区：元件/导线集合、选择、历史、交互和仿真生命周期。
    /// UI、模板和保存服务必须通过此控制器取得活动图，不能扫描全场景对象，
    /// 因为 Demo.unity 含有活动图之外的历史/静态对象。
    /// 修改会影响接线、撤销重做、保存加载和仿真，之后必须运行模板与安全测试。
    /// </summary>
    public sealed class WorkspaceController : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform workspaceRect;
        [SerializeField] private RectTransform canvasContent;
        [SerializeField] private RectTransform componentLayer;
        [SerializeField] private RectTransform wireLayer;
        [SerializeField] private WireManager wireManager;
        [SerializeField] private Text statusText;
        [SerializeField] private Text actionLogText;
        [SerializeField] private ScrollRect actionLogScrollRect;
        [SerializeField] private MeasurementPanel measurementPanel;
        [SerializeField] private ComponentParameterView componentParameterView;
        [SerializeField] private float gridSize = 24f;
        [SerializeField] private float minCanvasZoom = 0.2f;
        [SerializeField] private float maxCanvasZoom = 2.0f;
        [SerializeField] private float zoomStep = 0.12f;
        [SerializeField] private float simulationRefreshInterval = 0.25f;

        public RectTransform WorkspaceRect => workspaceRect;
        public IReadOnlyList<CircuitComponent> Components => components;
        public WireManager WireManager => wireManager;
        public Color CurrentWireColor { get; set; } = new Color(0.95f, 0.15f, 0.12f);
        public WireStyle CurrentWireStyle { get; set; } = WireStyle.Orthogonal;
        public bool IsInteractionLocked { get; private set; }
        public bool IsSimulationRunning { get; private set; }
        public CircuitComponent SelectedComponent => selectedComponent;
        public bool HasSelectedWire => selectedWire != null;
        public Color ActiveWirePaletteColor => selectedWire != null ? selectedWire.WireColor : Color.clear;

        private readonly List<CircuitComponent> components = new List<CircuitComponent>();
        private readonly List<MeasurementPanel> measurementPanels = new List<MeasurementPanel>();
        private readonly List<Image> previewSegments = new List<Image>();
        private readonly List<DrawingSnapshot> undoStack = new List<DrawingSnapshot>();
        private readonly List<DrawingSnapshot> redoStack = new List<DrawingSnapshot>();
        private readonly List<string> actionLogEntries = new List<string>();
        private TerminalView pendingTerminal;
        private CircuitComponent selectedComponent;
        private CircuitComponent selectedMeasurementTarget;
        private WireView selectedWire;
        private bool restoringHistory;
        private bool simulationDirty = true;
        private bool panningCanvas;
        private float canvasZoom = 1f;
        private float simulationRefreshTimer;
        private Vector2 panStartPointer;
        private Vector2 panStartPosition;
        private const int HistoryLimit = 40;
        private const int ActionLogEntryLimit = 180;
        private const int ActionLogCharacterLimit = 10000;

        private void Awake()
        {
            // 仅在缺少场景注入时补挂 WireManager，随后统一由当前工作区初始化。
            // 不从场景扫描历史对象；Components 与 WireManager.Wires 才是活动电路权威集合。
            if (wireManager == null)
            {
                wireManager = gameObject.AddComponent<WireManager>();
            }

            wireManager.Initialize(wireLayer, this);
            EnsureComponentParameterView();
            ClearActionLog();
        }

        private void Update()
        {
            HandleCanvasZoom();

            var controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (controlPressed && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Home))
            {
                ResetView();
            }

            if (controlPressed && Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
                return;
            }

            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.Delete))
            {
                DeleteSelection();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelPendingWire(null);
                ClearSelection();
                SetStatus("已取消当前选择。");
            }

            if (pendingTerminal != null)
            {
                UpdatePreviewLine();
            }

            if (IsSimulationRunning)
            {
                simulationRefreshTimer += Time.deltaTime;
                if (simulationRefreshTimer >= simulationRefreshInterval)
                {
                    var deltaTime = simulationRefreshTimer;
                    simulationRefreshTimer = 0f;
                    EvaluateSimulation(deltaTime);
                }
            }
        }

        public CircuitComponent SpawnComponent(ComponentDefinition definition, Vector2 anchoredPosition, string instanceId = null, bool recordHistory = true)
        {
            // 所有模板、导入和自由放置都应通过此入口加入活动集合，
            // 以便拓扑脏标记、撤销快照、仿真与保存加载观察到同一份实例数据。
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再添加元件。");
                return null;
            }

            if (recordHistory)
            {
                RecordHistoryCheckpoint();
            }

            var go = new GameObject(definition.displayName, typeof(RectTransform), typeof(Image), typeof(CircuitComponent));
            go.transform.SetParent(componentLayer, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = definition.size;
            rect.anchoredPosition = Snap(anchoredPosition);

            var body = go.GetComponent<Image>();
            if (definition.sprite != null)
            {
                body.sprite = definition.sprite;
                body.color = Color.white;
                body.preserveAspect = true;
            }
            else
            {
                body.color = definition.bodyColor;
            }

            var titleObject = CreateText("Title", go.transform, definition.displayName, 16, TextAnchor.UpperCenter);
            titleObject.rectTransform.anchorMin = new Vector2(0f, 1f);
            titleObject.rectTransform.anchorMax = new Vector2(1f, 1f);
            titleObject.rectTransform.pivot = new Vector2(0.5f, 1f);
            titleObject.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            titleObject.rectTransform.sizeDelta = new Vector2(0f, 28f);

            var stateObject = CreateText("State", go.transform, string.Empty, 18, TextAnchor.MiddleCenter);
            stateObject.rectTransform.anchorMin = new Vector2(0.2f, 0.35f);
            stateObject.rectTransform.anchorMax = new Vector2(0.8f, 0.65f);
            stateObject.rectTransform.offsetMin = Vector2.zero;
            stateObject.rectTransform.offsetMax = Vector2.zero;

            var component = go.GetComponent<CircuitComponent>();
            SetPrivateField(component, "body", body);
            SetPrivateField(component, "title", titleObject);
            SetPrivateField(component, "stateLabel", stateObject);
            component.Initialize(definition, this, instanceId);
            if (definition.kind == ComponentKind.Instrument)
            {
                BuildMeasurementInstrument(component);
            }

            components.Add(component);
            MarkTopologyDirty();
            return component;
        }

        public bool TryScreenToCanvasLocal(Vector2 screenPosition, Camera eventCamera, out Vector2 localPoint)
        {
            var target = canvasContent != null ? canvasContent : workspaceRect;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(target, screenPosition, eventCamera, out localPoint);
        }

        private void BuildMeasurementInstrument(CircuitComponent instrument)
        {
            var definition = instrument.Definition;
            var panel = instrument.gameObject.AddComponent<MeasurementPanel>();
            var isOscilloscope = definition.name.Contains("Oscilloscope");

            if (isOscilloscope)
            {
                var text = CreateText("OscilloscopeReadout", instrument.transform, string.Empty, 15, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.rectTransform.anchorMin = new Vector2(0f, 0.48f);
                text.rectTransform.anchorMax = new Vector2(1f, 0.88f);
                text.rectTransform.offsetMin = new Vector2(14f, 0f);
                text.rectTransform.offsetMax = new Vector2(-14f, 0f);

                var waveformObject = new GameObject("Waveform", typeof(RectTransform), typeof(CanvasRenderer), typeof(OscilloscopeWaveform));
                waveformObject.transform.SetParent(instrument.transform, false);
                var waveformRect = waveformObject.GetComponent<RectTransform>();
                waveformRect.anchorMin = new Vector2(0.08f, 0.08f);
                waveformRect.anchorMax = new Vector2(0.92f, 0.45f);
                waveformRect.offsetMin = Vector2.zero;
                waveformRect.offsetMax = Vector2.zero;
                var waveform = waveformObject.GetComponent<OscilloscopeWaveform>();
                waveform.raycastTarget = false;

                SetPrivateField(panel, "oscilloscopeText", text);
                SetPrivateField(panel, "waveform", waveform);
            }
            else
            {
                var text = CreateText("MultimeterReadout", instrument.transform, string.Empty, 15, TextAnchor.UpperLeft);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.rectTransform.anchorMin = new Vector2(0f, 0f);
                text.rectTransform.anchorMax = new Vector2(1f, 0.88f);
                text.rectTransform.offsetMin = new Vector2(14f, 12f);
                text.rectTransform.offsetMax = new Vector2(-14f, -8f);
                SetPrivateField(panel, "multimeterText", text);
            }

            measurementPanels.Add(panel);
            panel.ShowComponent(selectedMeasurementTarget, IsSimulationRunning);
        }

        public void HandleTerminalClicked(TerminalView terminal)
        {
            // 两次端子点击构成一次接线事务：创建前记录历史，创建后刷新导线并重置运行态。
            // 不要绕过此入口直接向 WireManager 添加导线，否则预览、撤销和状态提示会失同步。
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再接线。");
                return;
            }

            ClearSelection();

            if (pendingTerminal == null)
            {
                pendingTerminal = terminal;
                pendingTerminal.SetSelected(true);
                EnsurePreviewSegments(CurrentWireStyle == WireStyle.Orthogonal ? 3 : 1);
                SetStatus("正在接线：移动鼠标预览线路，点击另一个端子完成接线。");
                return;
            }

            if (pendingTerminal == terminal)
            {
                CancelPendingWire("已取消接线。");
                return;
            }

            if (!wireManager.CanCreateWire(pendingTerminal, terminal, out var rejectionReason))
            {
                pendingTerminal.SetSelected(false);
                pendingTerminal = null;
                HidePreviewLine();
                SetStatus(rejectionReason);
                return;
            }

            RecordHistoryCheckpoint();
            wireManager.CreateWire(pendingTerminal, terminal, ResolveWireColor(pendingTerminal, terminal), CurrentWireStyle);
            pendingTerminal.SetSelected(false);
            pendingTerminal = null;
            HidePreviewLine();
            wireManager.RefreshAll();
            MarkTopologyDirty("已完成接线，点击开始仿真查看结果。");
        }

        public Vector2 Snap(Vector2 position)
        {
            return new Vector2(Mathf.Round(position.x / gridSize) * gridSize, Mathf.Round(position.y / gridSize) * gridSize);
        }

        public void RefreshWiresFor(CircuitComponent component)
        {
            wireManager.RefreshFor(component);
        }

        public void RunSimulation()
        {
            StartSimulation();
        }

        public void ToggleSimulation()
        {
            if (IsSimulationRunning)
            {
                StopSimulation();
            }
            else
            {
                StartSimulation();
            }
        }

        public void StartSimulation()
        {
            IsSimulationRunning = true;
            simulationRefreshTimer = 0f;
            try
            {
                EvaluateSimulation(0f);
            }
            catch (System.Exception exception)
            {
                IsSimulationRunning = false;
                simulationDirty = true;
                ClearSimulationResult();
                SetStatus("仿真启动失败：" + exception.Message);
                Debug.LogException(exception);
            }
        }

        public void StopSimulation()
        {
            IsSimulationRunning = false;
            simulationRefreshTimer = 0f;
            simulationDirty = true;
            SimulationEngine.ResetRuntimeState();
            ClearSimulationResult();
            SetStatus("仿真已结束，当前可继续编辑电路。");
        }

        private void EvaluateSimulation(float deltaTime = 0f)
        {
            // Workspace 控制仿真生命周期，具体状态推导仍委托 SimulationEngine；
            // 任何拓扑、参数或交互变更后都必须先清除旧结果，避免展示过期运行态。
            var result = new SimulationEngine(components, wireManager.Wires, deltaTime).Run();
            simulationDirty = false;
            SetStatus(result);
            RefreshMeasurementPanel();
        }

        public void MarkSimulationDirty(string message = null)
        {
            simulationDirty = true;

            if (IsSimulationRunning)
            {
                EvaluateSimulation();
                return;
            }

            ClearSimulationResult();

            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        public void MarkTopologyDirty(string message = null)
        {
            // 清空活动电路的运行态而不删除场景历史对象。Analyzer/Validation 的输入始终来自
            // Components 与 WireManager.Wires，因此 ClearDrawing 后活动输入应为空。
            simulationDirty = true;
            ClearRuntimeLatchedStates();

            if (IsSimulationRunning)
            {
                EvaluateSimulation();
                return;
            }

            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        public void ClearRuntimeLatchedStates()
        {
            SimulationEngine.ResetRuntimeState();
            ClearSimulationResult();
        }

        public void ClearSimulationResult()
        {
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                component.SetEnergized(false);
                component.ClearMeasurement();
            }

            RefreshMeasurementPanel();
        }

        public void ClearWires()
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再删除导线。");
                return;
            }

            ClearSelection();
            RecordHistoryCheckpoint();
            wireManager.Clear();
            MarkTopologyDirty("已删除所有导线，点击开始仿真重新检查。");
        }

        public void SelectComponent(CircuitComponent component)
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，当前不能选择或移动元件。");
                return;
            }

            CancelPendingWire(null);
            ClearSelection();
            selectedComponent = component;
            selectedComponent?.SetSelected(true);
            if (component.Definition.kind != ComponentKind.Instrument)
            {
                selectedMeasurementTarget = component;
            }

            SetStatus("已选中元件：" + component.Definition.displayName + "。按 D 或 Delete 删除。");
            componentParameterView?.Show(component, this);
            RefreshMeasurementPanel();
        }

        public void RefreshParameterPanelFor(CircuitComponent component)
        {
            if (componentParameterView == null || component == null || component != selectedComponent)
            {
                return;
            }

            componentParameterView.Show(component, this);
        }

        public void SelectWire(WireView wire)
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，当前不能选择导线。");
                return;
            }

            CancelPendingWire(null);
            ClearSelection();
            selectedWire = wire;
            selectedWire?.SetSelected(true);
            SetStatus("已选中导线：" + FormatWireConnection(wire) + "。按 D 或 Delete 删除。");
            RefreshMeasurementPanel();
        }

        private static string FormatWireConnection(WireView wire)
        {
            if (wire == null)
            {
                return "未知导线";
            }

            return FormatTerminalEndpoint(wire.StartTerminal) + " -> " + FormatTerminalEndpoint(wire.EndTerminal);
        }

        public void ApplyWirePaletteColor(Color color)
        {
            color = NormalizeWireColor(color);

            if (selectedWire == null)
            {
                SetStatus("请先选中一条导线再修改颜色。新建导线会继续自动识别线色。");
                return;
            }

            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再修改导线颜色。");
                return;
            }

            if (IsNearColor(selectedWire.WireColor, color))
            {
                SetStatus("选中导线已经是" + FormatWireColorName(color) + "。");
                return;
            }

            var previousColor = selectedWire.WireColor;
            RecordHistoryCheckpoint();
            selectedWire.SetWireColor(color);
            SetStatus("修改导线颜色：" + FormatWireColorName(previousColor) + " -> " + FormatWireColorName(color) + "。");
        }

        private static string FormatTerminalEndpoint(TerminalView terminal)
        {
            if (terminal == null)
            {
                return "未知端子";
            }

            var component = terminal.Owner;
            var componentName = component != null && component.Definition != null ? component.Definition.displayName : null;
            if (string.IsNullOrWhiteSpace(componentName) && component != null)
            {
                componentName = component.InstanceId;
            }

            if (string.IsNullOrWhiteSpace(componentName))
            {
                componentName = "未知元件";
            }

            return componentName + "." + terminal.TerminalId;
        }

        public void DeleteSelection()
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再删除。");
                return;
            }

            if (selectedWire != null)
            {
                RecordHistoryCheckpoint();
                selectedWire.SetSelected(false);
                wireManager.DeleteWire(selectedWire);
                selectedWire = null;
                MarkTopologyDirty("已删除选中导线，点击开始仿真重新检查。");
                return;
            }

            if (selectedComponent != null)
            {
                DeleteSelectedComponent();
            }
        }

        public void DeleteSelectedComponent()
        {
            if (selectedComponent == null)
            {
                return;
            }

            RecordHistoryCheckpoint();
            wireManager.DeleteWiresFor(selectedComponent);
            var instrumentPanel = selectedComponent.GetComponent<MeasurementPanel>();
            if (instrumentPanel != null)
            {
                measurementPanels.Remove(instrumentPanel);
            }

            if (selectedMeasurementTarget == selectedComponent)
            {
                selectedMeasurementTarget = null;
            }

            RuntimeStateManager.Shared.RemoveComponentState(selectedComponent.InstanceId);
            components.Remove(selectedComponent);
            Destroy(selectedComponent.gameObject);
            selectedComponent = null;
            wireManager.RefreshAll();
            MarkTopologyDirty("已删除选中元件及相关导线，点击开始仿真重新检查。");
        }

        public void ClearDrawing()
        {
            ClearDrawing(true);
        }

        public void ClearDrawing(bool recordHistory)
        {
            // 清空画布会同步删除活动导线、元件、测量面板和运行态；
            // 不应以 FindObjectsOfType 替代此集合操作，否则可能误删 Demo 场景中的历史对象。
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再清空画布。");
                return;
            }

            if (recordHistory)
            {
                RecordHistoryCheckpoint();
            }

            // 清空系统模板编辑状态
            TemplateEditSession.Clear();

            foreach (var component in new List<CircuitComponent>(components))
            {
                if (component != null)
                {
                    Destroy(component.gameObject);
                }
            }

            components.Clear();
            measurementPanels.Clear();
            selectedComponent = null;
            selectedMeasurementTarget = null;
            selectedWire = null;
            pendingTerminal = null;
            componentParameterView?.Hide();
            HidePreviewLine();
            wireManager.Clear();
            MarkTopologyDirty();
            SetStatus("画布已清空。");
        }

        public CircuitComponent FindComponent(string instanceId)
        {
            return components.Find(c => c.InstanceId == instanceId);
        }

        public void SetStatus(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (statusText != null)
            {
                statusText.text = message;
            }

            AppendActionLog(message);
        }

        private void AppendActionLog(string message)
        {
            if (actionLogText == null)
            {
                return;
            }

            if (actionLogEntries.Count > 0 && actionLogEntries[actionLogEntries.Count - 1].Contains(message))
            {
                return;
            }

            string timeStr = $"<color=#94A3B8>{System.DateTime.Now.ToString("HH:mm:ss")}</color>";
            string msgColor = "#1F2937";

            if (message.Contains("失败") || message.Contains("错误"))
            {
                msgColor = "#DC2626";
            }
            else if (message.Contains("完成") || message.Contains("成功"))
            {
                msgColor = "#16A34A";
            }
            else if (message.Contains("无法") || message.Contains("没有"))
            {
                msgColor = "#EA580C";
            }

            string formattedMessage = $"{timeStr}  <color={msgColor}>{message}</color>";
            actionLogEntries.Add(formattedMessage);
            TrimActionLogEntries();
            
            actionLogText.supportRichText = true;
            actionLogText.text = string.Join("\n", actionLogEntries);

            if (actionLogScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                actionLogScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        public void ClearActionLog()
        {
            actionLogEntries.Clear();
            if (actionLogText != null)
            {
                actionLogText.supportRichText = true;
                actionLogText.text = "<color=#94A3B8><b>暂无操作记录</b>\n\n拖拽元件、接线、仿真和检查操作会显示在这里。</color>";
            }
        }

        private void TrimActionLogEntries()
        {
            while (actionLogEntries.Count > ActionLogEntryLimit)
            {
                actionLogEntries.RemoveAt(0);
            }

            while (actionLogEntries.Count > 0 && GetActionLogCharacterCount() > ActionLogCharacterLimit)
            {
                actionLogEntries.RemoveAt(0);
            }
        }

        private int GetActionLogCharacterCount()
        {
            var total = 0;
            foreach (var entry in actionLogEntries)
            {
                if (entry != null)
                {
                    total += entry.Length + 1;
                }
            }

            return total;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsInteractionLocked)
            {
                return;
            }

            if (pendingTerminal != null)
            {
                CancelPendingWire("已取消接线。");
                return;
            }

            ClearSelection();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!ShouldPanCanvas(eventData))
            {
                return;
            }

            panningCanvas = true;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(workspaceRect, eventData.position, eventData.pressEventCamera, out panStartPointer);
            panStartPosition = canvasContent != null ? canvasContent.anchoredPosition : Vector2.zero;
            CancelPendingWire(null);
            ClearSelection();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!panningCanvas || canvasContent == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(workspaceRect, eventData.position, eventData.pressEventCamera, out var currentPointer))
            {
                SetCanvasPan(panStartPosition + currentPointer - panStartPointer);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!panningCanvas)
            {
                return;
            }

            panningCanvas = false;
            SetStatus("画布视野已移动。按住鼠标中键或空格+左键可继续拖动画布。");
        }

        private static bool ShouldPanCanvas(PointerEventData eventData)
        {
            return eventData.button == PointerEventData.InputButton.Middle || eventData.button == PointerEventData.InputButton.Left && Input.GetKey(KeyCode.Space);
        }

        private void HandleCanvasZoom()
        {
            if (canvasContent == null) return;
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            // Only block zoom if mouse is on the far left (Palette area) or top (Toolbar)
            if (Input.mousePosition.x < 300f || Input.mousePosition.y > Screen.height - 60f)
            {
                return;
            }

            var oldZoom = canvasZoom;
            var nextZoom = Mathf.Clamp(canvasZoom + scroll * zoomStep, minCanvasZoom, maxCanvasZoom);
            
            if (Mathf.Approximately(oldZoom, nextZoom))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasContent, Input.mousePosition, null, out var contentPointUnderMouse);

            canvasZoom = nextZoom;
            canvasContent.localScale = Vector3.one * canvasZoom;

            var newContentPointUnderMouse = contentPointUnderMouse * (nextZoom / oldZoom);
            var offset = newContentPointUnderMouse - contentPointUnderMouse;
            SetCanvasPan(canvasContent.anchoredPosition - offset * oldZoom);
        }

        public void ResetView()
        {
            if (canvasContent == null) return;
            canvasZoom = 1f;
            canvasContent.localScale = Vector3.one;
            SetCanvasPan(Vector2.zero);
            SetStatus("视图已重置到中心。");
        }

        private bool IsPointerInsideActionLog()
        {
            if (actionLogScrollRect == null)
            {
                return false;
            }

            var logRect = actionLogScrollRect.GetComponent<RectTransform>();
            return logRect != null && RectTransformUtility.RectangleContainsScreenPoint(logRect, Input.mousePosition, null);
        }

        public void SetView(float zoom, Vector2 pan)
        {
            canvasZoom = Mathf.Clamp(zoom, minCanvasZoom, maxCanvasZoom);
            if (canvasContent != null)
            {
                canvasContent.localScale = Vector3.one * canvasZoom;
            }
            SetCanvasPan(pan);
        }

        private void SetCanvasPan(Vector2 targetPosition)
        {
            if (canvasContent == null || workspaceRect == null)
            {
                return;
            }

            var workspaceSize = workspaceRect.rect.size;
            var contentSize = canvasContent.rect.size * canvasZoom;
            var limitX = Mathf.Max(0f, (contentSize.x - workspaceSize.x) * 0.5f);
            var limitY = Mathf.Max(0f, (contentSize.y - workspaceSize.y) * 0.5f);
            canvasContent.anchoredPosition = new Vector2(
                Mathf.Clamp(targetPosition.x, -limitX, limitX),
                Mathf.Clamp(targetPosition.y, -limitY, limitY));
        }

        private void RefreshMeasurementPanel()
        {
            if (selectedMeasurementTarget == null)
            {
                selectedMeasurementTarget = null;
            }

            if (measurementPanel != null)
            {
                TryRefreshMeasurementPanel(measurementPanel);
            }

            for (var i = measurementPanels.Count - 1; i >= 0; i--)
            {
                var panel = measurementPanels[i];
                if (panel == null)
                {
                    measurementPanels.RemoveAt(i);
                    continue;
                }

                TryRefreshMeasurementPanel(panel);
            }
        }

        private void TryRefreshMeasurementPanel(MeasurementPanel panel)
        {
            try
            {
                panel.ShowComponent(selectedMeasurementTarget, IsSimulationRunning);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                SetStatus("测量工具刷新失败，已跳过本次显示。");
            }
        }

        public void ToggleInteractionLock()
        {
            IsInteractionLocked = !IsInteractionLocked;
            if (IsInteractionLocked)
            {
                CancelPendingWire(null);
                ClearSelection();
            }

            SetStatus(IsInteractionLocked ? "画布已锁定：元件、导线和删除操作暂时不可编辑。" : "画布已解锁，可以继续编辑电路。");
        }

        public void RecordHistoryCheckpoint()
        {
            if (restoringHistory)
            {
                return;
            }

            undoStack.Add(CreateSnapshot());
            if (undoStack.Count > HistoryLimit)
            {
                undoStack.RemoveAt(0);
            }

            redoStack.Clear();
        }

        public void ClearHistory()
        {
            undoStack.Clear();
            redoStack.Clear();
        }

        public void Undo()
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再撤销。");
                return;
            }

            if (undoStack.Count == 0)
            {
                SetStatus("没有可撤销的操作。");
                return;
            }

            redoStack.Add(CreateSnapshot());
            var index = undoStack.Count - 1;
            var snapshot = undoStack[index];
            undoStack.RemoveAt(index);
            RestoreSnapshot(snapshot);
            SetStatus("已撤销上一步操作。");
        }

        public void Redo()
        {
            if (IsInteractionLocked)
            {
                SetStatus("画布已锁定，解锁后再重做。");
                return;
            }

            if (redoStack.Count == 0)
            {
                SetStatus("没有可重做的操作。");
                return;
            }

            undoStack.Add(CreateSnapshot());
            var index = redoStack.Count - 1;
            var snapshot = redoStack[index];
            redoStack.RemoveAt(index);
            RestoreSnapshot(snapshot);
            SetStatus("已重做上一步操作。");
        }

        private void ClearSelection()
        {
            selectedComponent?.SetSelected(false);
            selectedWire?.SetSelected(false);
            selectedComponent = null;
            selectedWire = null;
            componentParameterView?.Hide();
            RefreshMeasurementPanel();
        }

        private void EnsureComponentParameterView()
        {
            if (componentParameterView == null)
            {
                componentParameterView = FindObjectOfType<ComponentParameterView>();
            }

            if (componentParameterView == null && workspaceRect != null)
            {
                componentParameterView = ComponentParameterView.Create(workspaceRect);
            }
        }

        private DrawingSnapshot CreateSnapshot()
        {
            var snapshot = new DrawingSnapshot();
            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var rect = component.GetComponent<RectTransform>();
                snapshot.Components.Add(new ComponentSnapshot
                {
                    Definition = component.Definition,
                    InstanceId = component.InstanceId,
                    Position = rect != null ? rect.anchoredPosition : Vector2.zero,
                    IsClosed = component.IsClosed,
                    Parameters = component.CloneParameters()
                });
            }

            foreach (var wire in wireManager.Wires)
            {
                if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                {
                    continue;
                }

                snapshot.Wires.Add(new WireSnapshot
                {
                    StartComponentId = wire.StartTerminal.Owner.InstanceId,
                    StartTerminalId = wire.StartTerminal.TerminalId,
                    EndComponentId = wire.EndTerminal.Owner.InstanceId,
                    EndTerminalId = wire.EndTerminal.TerminalId,
                    Color = wire.WireColor,
                    Style = wire.Style,
                    HasManualRoute = wire.HasManualRoute,
                    ManualRouteHorizontal = wire.ManualRouteHorizontal,
                    ManualRouteAxis = wire.ManualRouteAxis,
                    ManualRoutePoints = new List<Vector2>(wire.ManualRoutePoints)
                });
            }

            return snapshot;
        }

        private void RestoreSnapshot(DrawingSnapshot snapshot)
        {
            // 撤销/重做通过同一生成与接线入口恢复快照，保证实例参数、手工折点和拓扑脏状态一致。
            // 修改快照字段或恢复顺序后必须回归撤销重做、保存导入和真实模板基线。
            restoringHistory = true;
            CancelPendingWire(null);
            ClearSelection();

            foreach (var component in new List<CircuitComponent>(components))
            {
                if (component != null)
                {
                    Destroy(component.gameObject);
                }
            }

            components.Clear();
            measurementPanels.Clear();
            selectedMeasurementTarget = null;
            wireManager.Clear();

            foreach (var componentState in snapshot.Components)
            {
                var component = SpawnComponent(componentState.Definition, componentState.Position, componentState.InstanceId);
                if (component != null)
                {
                    component.SetClosed(componentState.IsClosed);
                    component.SetParameters(componentState.Parameters);
                }
            }

            foreach (var wireState in snapshot.Wires)
            {
                var start = FindComponent(wireState.StartComponentId)?.GetTerminal(wireState.StartTerminalId);
                var end = FindComponent(wireState.EndComponentId)?.GetTerminal(wireState.EndTerminalId);
                var wire = wireManager.CreateWire(start, end, wireState.Color, wireState.Style);
                if (wire != null && wireState.HasManualRoute)
                {
                    if (wireState.ManualRoutePoints != null && wireState.ManualRoutePoints.Count >= 2)
                    {
                        wire.SetManualRoutePoints(wireState.ManualRoutePoints);
                    }
                    else
                    {
                        wire.SetManualRoute(wireState.ManualRouteHorizontal, wireState.ManualRouteAxis);
                    }
                }
            }

            wireManager.RefreshAll();
            restoringHistory = false;
            MarkTopologyDirty();
        }

        private void CancelPendingWire(string status)
        {
            if (pendingTerminal != null)
            {
                pendingTerminal.SetSelected(false);
                pendingTerminal = null;
            }

            HidePreviewLine();
            SetStatus(status);
        }

        private void UpdatePreviewLine()
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(wireLayer, Input.mousePosition, null, out var mouseLocal))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(wireLayer, RectTransformUtility.WorldToScreenPoint(null, pendingTerminal.WorldPosition), null, out var start);

            EnsurePreviewSegments(CurrentWireStyle == WireStyle.Orthogonal ? 3 : 1);

            if (CurrentWireStyle == WireStyle.Orthogonal)
            {
                var midX = (start.x + mouseLocal.x) * 0.5f;
                DrawPreviewSegment(previewSegments[0].rectTransform, start, new Vector2(midX, start.y));
                DrawPreviewSegment(previewSegments[1].rectTransform, new Vector2(midX, start.y), new Vector2(midX, mouseLocal.y));
                DrawPreviewSegment(previewSegments[2].rectTransform, new Vector2(midX, mouseLocal.y), mouseLocal);
            }
            else
            {
                DrawPreviewSegment(previewSegments[0].rectTransform, start, mouseLocal);
            }
        }

        private void EnsurePreviewSegments(int count)
        {
            var previewColor = pendingTerminal != null ? pendingTerminal.TerminalColor : CurrentWireColor;

            while (previewSegments.Count < count)
            {
                var go = new GameObject("WirePreviewSegment", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(wireLayer, false);
                var image = go.GetComponent<Image>();
                image.color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.55f);
                image.raycastTarget = false;
                previewSegments.Add(image);
            }

            for (var i = 0; i < previewSegments.Count; i++)
            {
                previewSegments[i].gameObject.SetActive(i < count);
                previewSegments[i].color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.55f);
            }
        }

        public Color ResolveAutoWireColor(TerminalView start, TerminalView end)
        {
            return ResolveWireColor(start, end);
        }

        private Color ResolveWireColor(TerminalView start, TerminalView end)
        {
            var startScore = ResolveWireColorScore(start, out var startColor);
            var endScore = ResolveWireColorScore(end, out var endColor);

            if (endScore > startScore)
            {
                return endColor;
            }

            if (startScore > endScore)
            {
                return startColor;
            }

            return StableColorRank(startColor) >= StableColorRank(endColor) ? startColor : endColor;
        }

        private static int ResolveWireColorScore(TerminalView terminal, out Color color)
        {
            color = terminal != null ? NormalizeWireColor(terminal.TerminalColor) : WireRed();
            if (terminal == null)
            {
                return 0;
            }

            switch (terminal.Role)
            {
                case TerminalRole.ProtectiveEarth:
                    color = WireGreen();
                    return 100;
                case TerminalRole.Neutral:
                case TerminalRole.CoilA2:
                    color = WireBlue();
                    return 90;
                case TerminalRole.Phase:
                    return 84;
                case TerminalRole.CoilA1:
                    color = WireRed();
                    return 82;
                case TerminalRole.Input:
                case TerminalRole.Output:
                    return 70;
                default:
                    return 40;
            }
        }

        private static Color NormalizeWireColor(Color color)
        {
            if (IsNearColor(color, WireYellow()))
            {
                return WireYellow();
            }

            if (IsNearColor(color, WireGreen()))
            {
                return WireGreen();
            }

            if (IsNearColor(color, WireBlue()))
            {
                return WireBlue();
            }

            return WireRed();
        }

        private static bool IsNearColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.35f;
        }

        private static int StableColorRank(Color color)
        {
            if (IsNearColor(color, WireYellow()))
            {
                return 4;
            }

            if (IsNearColor(color, WireRed()))
            {
                return 3;
            }

            if (IsNearColor(color, WireBlue()))
            {
                return 2;
            }

            return 1;
        }

        private static string FormatWireColorName(Color color)
        {
            if (IsNearColor(color, WireYellow()))
            {
                return "黄色";
            }

            if (IsNearColor(color, WireGreen()))
            {
                return "绿色";
            }

            if (IsNearColor(color, WireBlue()))
            {
                return "蓝色";
            }

            if (IsNearColor(color, WireRed()))
            {
                return "红色";
            }

            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        private static Color WireRed() => new Color(0.95f, 0.12f, 0.12f);
        private static Color WireBlue() => new Color(0.1f, 0.35f, 0.95f);
        private static Color WireGreen() => new Color(0.08f, 0.65f, 0.25f);
        private static Color WireYellow() => new Color(0.95f, 0.78f, 0.12f);

        private void HidePreviewLine()
        {
            foreach (var segment in previewSegments)
            {
                if (segment != null)
                {
                    segment.gameObject.SetActive(false);
                }
            }
        }

        private static void DrawPreviewSegment(RectTransform segment, Vector2 start, Vector2 end)
        {
            var delta = end - start;
            var length = delta.magnitude;
            segment.anchoredPosition = start + delta * 0.5f;
            segment.sizeDelta = new Vector2(Mathf.Max(2f, length), 3f);
            segment.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private static Text CreateText(string name, Transform parent, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.alignment = anchor;
            label.color = new Color(0.08f, 0.1f, 0.16f);
            label.raycastTarget = false;
            return label;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        private sealed class DrawingSnapshot
        {
            // 撤销/重做的内存快照容器；CreateSnapshot 填充，RestoreSnapshot 消费，不是用户图纸 JSON 契约。
            public readonly List<ComponentSnapshot> Components = new List<ComponentSnapshot>();
            public readonly List<WireSnapshot> Wires = new List<WireSnapshot>();
        }

        private sealed class ComponentSnapshot
        {
            // Definition 保留 Unity 资产引用，InstanceId 用于同一快照内导线关联；Parameters 由 CloneParameters 生成独立列表。
            public ComponentDefinition Definition;
            public string InstanceId;
            public Vector2 Position;
            public bool IsClosed;
            public List<ComponentParameter> Parameters = new List<ComponentParameter>();
        }

        private sealed class WireSnapshot
        {
            // 端点实例/端子 ID 用于 RestoreSnapshot 重建导线；手动路径字段仅保存当前画布布局状态。
            public string StartComponentId;
            public string StartTerminalId;
            public string EndComponentId;
            public string EndTerminalId;
            public Color Color;
            public WireStyle Style;
            public bool HasManualRoute;
            public bool ManualRouteHorizontal;
            public float ManualRouteAxis;
            public List<Vector2> ManualRoutePoints = new List<Vector2>();
        }
    }
}
