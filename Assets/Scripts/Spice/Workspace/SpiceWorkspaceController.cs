using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
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
        private CancellationTokenSource simulationCancellation;
        private bool shuttingDown;
        private bool componentDragInProgress;
        private Func<bool> modalInputGuard;
        private RectTransform viewportRect;
        private RectTransform contentRect;
        private SpiceWorkspaceViewController viewController;
        private Text zoomLabel;
        private Button copyResultButton;
        private readonly SpiceDrawingFileService fileService = new SpiceDrawingFileService();
        // 缓存最近一次正式结果/阻断诊断的权威输出文本（与正式可见 ResultText 一致），
        // 用于复制资格判断和复制输出；不读取隐藏 DiagnosticRoot，不重新格式化结果。
        private string lastOutcomeText;
        private Button zoomOutButton;
        private Button zoomInButton;

        public SpiceWorkspaceModel Model { get; private set; } = new SpiceWorkspaceModel();
        public SpiceWorkspaceResultState ResultState { get; private set; } = SpiceWorkspaceResultState.NeverRun;
        public RectTransform WorkspaceRect { get; private set; }
        public RectTransform ViewportRect => viewportRect;
        public RectTransform ContentRect => contentRect;
        public SpiceWorkspaceViewController ViewController => viewController;
        public RectTransform WireLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }
        public bool HasPendingWire => pendingComponent != null;
        public event Action<SpiceWorkspaceComponentData> ParameterDialogRequested;

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
            shuttingDown = true;
            CancelActiveSimulation();
            Model.Changed -= HandleModelChanged;
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            CancelActiveSimulation();
        }

        private void CancelActiveSimulation()
        {
            if (simulationCancellation != null)
            {
                if (!simulationCancellation.IsCancellationRequested)
                {
                    simulationCancellation.Cancel();
                }
            }
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
            if ((Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)) && CanUseDeletionShortcut()) DeleteSelection();
            RefreshWirePreview();
        }

        public void ConfigureModalInputGuard(Func<bool> guard)
        {
            modalInputGuard = guard;
        }

        public void SetComponentDragInProgress(bool isDragging)
        {
            componentDragInProgress = isDragging;
        }

        private bool CanUseDeletionShortcut()
        {
            if (!isActiveAndEnabled || ResultState == SpiceWorkspaceResultState.Running || HasPendingWire || componentDragInProgress || IsViewNavigationActive)
            {
                return false;
            }

            if (modalInputGuard != null && modalInputGuard())
            {
                return false;
            }

            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected == null || selected.GetComponentInParent<InputField>() == null;
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
            palettePreview.color = IsPointerInsideViewport(screenPosition, eventCamera)
                ? new Color(0.15f, 0.39f, 0.92f, 0.22f)
                : new Color(0.39f, 0.45f, 0.55f, 0.16f);
        }

        public void EndPaletteDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!paletteDragActive) return;
            if (IsPointerInsideViewport(screenPosition, eventCamera) && TryScreenToWorkspace(screenPosition, eventCamera, out var local))
            {
                CreateComponent(paletteKind, local);
            }
            CancelPaletteDrag();
        }

        public bool TryScreenToWorkspace(Vector2 screenPosition, Camera eventCamera, out Vector2 localPosition)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(WorkspaceRect, screenPosition, eventCamera, out localPosition);
        }

        private bool IsPointerInsideViewport(Vector2 screenPosition, Camera eventCamera)
        {
            return viewportRect != null && RectTransformUtility.RectangleContainsScreenPoint(viewportRect, screenPosition, eventCamera);
        }

        public bool IsViewNavigationActive => viewController != null && viewController.IsPanning;

        public bool ConsumeViewNavigationClick(PointerEventData eventData)
        {
            return viewController != null && viewController.ConsumeNavigationClick(eventData);
        }

        public void UpdateZoomControlState(float scale)
        {
            if (zoomOutButton != null) zoomOutButton.interactable = scale > 0.4001f;
            if (zoomInButton != null) zoomInButton.interactable = scale < 1.9999f;
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
            if (selectedComponent != null && string.Equals(selectedComponent.InstanceId, instanceId, StringComparison.Ordinal))
            {
                RefreshParameterPanel();
            }
            return true;
        }

        public bool TrySetSwitchState(string instanceId, bool closed)
        {
            var component = Model.FindComponent(instanceId);
            if (component == null || component.Kind != SpiceComponentKind.IdealSwitch || !Model.TrySetParameter(instanceId, closed ? 1d : 0d))
            {
                return false;
            }

            componentViews[instanceId].RefreshAnnotation();
            if (selectedComponent != null && string.Equals(selectedComponent.InstanceId, instanceId, StringComparison.Ordinal)) RefreshParameterPanel();
            if (statusText != null) statusText.text = "开关状态已更新，请重新运行计算。";
            return true;
        }

        public bool TryApplyParameterText(string instanceId, string rawValue, string unit, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                error = "请输入参数值。";
                return false;
            }

            if (!double.TryParse(rawValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !TrySetParameter(instanceId, value, unit))
            {
                error = "参数无效，请输入当前器件支持范围内的数值。";
                return false;
            }

            if (statusText != null)
            {
                statusText.text = "参数已更新，请重新运行计算。";
            }

            return true;
        }

        public async Task<SpiceSimulationResult> RunCalculationAsync()
        {
            EnsureInitialized();
            if (ResultState == SpiceWorkspaceResultState.Running) return null;
            
            var previousState = ResultState;
            ResultState = SpiceWorkspaceResultState.Running;
            runButton.interactable = false;
            statusText.text = "计算中...";
            lastOutcomeText = null;
            RefreshCopyResultButton();
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            RefreshNetlistUi();

            CancellationTokenSource localCancellation = null;
            try
            {
                localCancellation = new CancellationTokenSource();
                simulationCancellation = localCancellation;

                var result = await simulationService.SimulateAsync(Model.BuildCircuitModel(), localCancellation.Token);

                if (localCancellation.IsCancellationRequested || shuttingDown)
                    return null;

                generatedNetlistContent = result.GeneratedNetlistContent;
                if (result.Success)
                {
                    ResultState = SpiceWorkspaceResultState.Current;
                    statusText.text = "结果有效";
                    var formattedResult = FormatResult(result);
                    lastOutcomeText = string.IsNullOrEmpty(formattedResult) ? null : formattedResult;
                    SetResultText(formattedResult);
                }
                else
                {
                    ResultState = SpiceWorkspaceResultState.Failed;
                    statusText.text = "计算失败";
                    var formattedDiagnostics = FormatDiagnostics(result);
                    lastOutcomeText = string.IsNullOrEmpty(formattedDiagnostics) ? null : formattedDiagnostics;
                    SetDiagnosticText(formattedDiagnostics);
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                if (!shuttingDown)
                {
                    ResultState = previousState;
                    statusText.text = "计算已取消";
                }
                return null;
            }
            catch (Exception exception)
            {
                if (localCancellation != null && localCancellation.IsCancellationRequested || shuttingDown)
                    return null;

                ResultState = SpiceWorkspaceResultState.Failed;
                generatedNetlistContent = null;
                statusText.text = "计算失败";
                lastOutcomeText = exception.ToString();
                SetDiagnosticText(exception.ToString());
                return null;
            }
            finally
            {
                if (ReferenceEquals(simulationCancellation, localCancellation))
                    simulationCancellation = null;

                if (localCancellation != null)
                    localCancellation.Dispose();

                if (!shuttingDown && runButton != null)
                {
                    runButton.interactable = true;
                    RefreshNetlistUi();
                    RefreshCopyResultButton();
                }
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

        public void HandleComponentPointerClick(SpiceWorkspaceComponentView component, PointerEventData eventData)
        {
            if (component == null || eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (HasPendingWire)
            {
                if (statusText != null) statusText.text = "请先完成或取消当前接线。";
                return;
            }

            SelectComponent(component);
            if (eventData.clickCount < 2 || eventData.dragging)
            {
                return;
            }

            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                if (statusText != null) statusText.text = "仿真计算进行中，请稍后编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.IdealSwitch)
            {
                TrySetSwitchState(component.InstanceId, component.Data.SiValue <= 0.5d);
                return;
            }

            if (component.Kind == SpiceComponentKind.SiliconDiode)
            {
                if (statusText != null) statusText.text = "通用硅二极管使用固定模型，无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.VoltageProbe)
            {
                if (statusText != null) statusText.text = "电压探针无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.CurrentProbe)
            {
                if (statusText != null) statusText.text = "电流探针无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.Ground)
            {
                if (statusText != null) statusText.text = "该器件无可编辑参数。";
                return;
            }

            ParameterDialogRequested?.Invoke(component.Data);
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
            if (ConsumeViewNavigationClick(eventData)) return;
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
            Model.ResetInstanceNaming();
            generatedNetlistContent = null;
            lastOutcomeText = null;
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            // 清空 SPICE 画布后重置为 100% 和初始中心
            if (viewController != null) viewController.ResetView();
            // 清空画布成功后清除当前会话文件路径（仅在 ClearWorkspace 末尾调用一次，
            // 不在清空按钮 UI 回调中复制清除路径逻辑）。
            ClearCurrentSpiceFilePath();
        }

        /// <summary>
        /// 事务式导入图纸 JSON。先在临时模型上完整解析和校验，成功后才替换当前工作区。
        /// 失败时不修改任何当前状态（画布、元件、Wire、选择、结果、网表、编号）。
        /// 仿真计算进行中时拒绝导入，以避免旧电路异步结果覆盖刚导入电路的结果和网表。
        /// </summary>
        public bool TryImportDrawingJson(string json, out string error)
        {
            EnsureInitialized();
            if (!CanImportDrawing(out error))
            {
                return false;
            }
            // 阶段一：纯解析+校验，构建临时模型。任何失败都直接返回，不触碰当前状态。
            if (!SpiceDrawingSerializer.TryFromJson(json, out var tempModel, out error))
            {
                return false;
            }
            // 阶段二：只有临时模型完整构建成功后才进入提交阶段。
            CommitImportedModel(tempModel);
            return true;
        }

        /// <summary>
        /// 判定当前是否允许导入图纸。仿真计算进行中时拒绝，保留计算结果完整性。
        /// 不取消当前 ngspice，不等待 Task，不修改任何状态。
        /// </summary>
        internal bool CanImportDrawing(out string reason)
        {
            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                reason = "仿真计算进行中，请稍后导入图纸。";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// 仅供 T3 测试受控设置 ResultState，以验证 Running 等状态下的导入保护。
        /// 不在生产路径调用；不触发 ngspice，不修改视图或结果文本。
        /// </summary>
        internal void SetResultStateForTesting(SpiceWorkspaceResultState state)
        {
            ResultState = state;
        }

        /// <summary>当前会话的图纸文件路径。保存或导入成功后更新；清空画布后清除。</summary>
        public string CurrentSpiceFilePath => fileService.CurrentSpiceFilePath;

        /// <summary>是否已绑定当前会话文件路径。C2 的“保存”按钮据此决定是否改走“另存为”。</summary>
        public bool HasCurrentSpiceFilePath => fileService.HasCurrentSpiceFilePath;

        /// <summary>
        /// 将当前 Workspace 保存到指定路径（原子写入 UTF-8）。
        /// 复用 Batch A 的 SpiceDrawingSerializer.ToJson；不保存结果、网表、诊断、选择、pending Wire、缩放或平移。
        /// 保存成功后 CurrentSpiceFilePath 更新为规范化路径；失败时保持旧值。
        /// 仿真计算进行中拒绝保存。
        /// </summary>
        public bool TrySaveWorkspaceToPath(string path, out string error)
        {
            EnsureInitialized();
            if (!CanImportDrawing(out error))
            {
                // 仿真进行中拒绝保存：不创建目录、不写临时文件、不取消 ngspice。
                error = "仿真计算进行中，请稍后保存图纸。";
                return false;
            }
            var json = SpiceDrawingSerializer.ToJson(Model);
            var normalizedPath = SpiceDrawingFileService.NormalizeExtension(path);
            if (!fileService.TrySaveUtf8Atomically(normalizedPath, json, out error))
            {
                // 保存失败：CurrentSpiceFilePath 保持旧值。
                return false;
            }
            fileService.SetCurrentSpiceFilePath(normalizedPath);
            if (statusText != null) statusText.text = "图纸已保存到：" + normalizedPath;
            return true;
        }

        /// <summary>
        /// 将当前 Workspace 保存到已绑定的 CurrentSpiceFilePath。
        /// 当前路径为空时返回清晰失败，不自行打开对话框。
        /// </summary>
        public bool TrySaveCurrentWorkspace(out string error)
        {
            EnsureInitialized();
            if (!fileService.HasCurrentSpiceFilePath)
            {
                error = "尚未指定保存路径，请使用另存为。";
                return false;
            }
            return TrySaveWorkspaceToPath(fileService.CurrentSpiceFilePath, out error);
        }

        /// <summary>
        /// 从指定路径导入图纸：先进行文件级检查和 UTF-8 读取，
        /// 再将原始 JSON 原封不动交给 Batch B 的 TryImportDrawingJson。
        /// 不在文件层解析器件、坐标、端子或 Wire；不调用 ClearWorkspace；不提前清空当前画布。
        /// Batch B 失败时原样保留当前 Workspace 和当前路径。
        /// 导入成功后 CurrentSpiceFilePath 更新为导入路径。
        /// </summary>
        public bool TryImportWorkspaceFromPath(string path, out string error)
        {
            EnsureInitialized();
            // 第一道防线：运行中拒绝导入（在读取文件之前）。
            // 保持 Batch B 的运行中导入保护作为第二道防线。
            if (!CanImportDrawing(out error))
            {
                error = "仿真计算进行中，请稍后导入图纸。";
                return false;
            }
            if (!fileService.TryReadUtf8File(path, out var json, out error))
            {
                // 读取失败：CurrentSpiceFilePath 保持旧值。
                return false;
            }
            // 保存旧路径，便于 Batch B 失败时恢复。
            var previousPath = fileService.CurrentSpiceFilePath;
            if (!TryImportDrawingJson(json, out error))
            {
                // Batch B 失败：当前 Workspace 和当前路径均不变。
                fileService.SetCurrentSpiceFilePath(previousPath);
                return false;
            }
            // 导入成功：更新当前路径（导入路径不规范化扩展名，保持用户传入的路径）。
            fileService.SetCurrentSpiceFilePath(path);
            if (statusText != null) statusText.text = "图纸已从以下路径导入：" + path;
            return true;
        }

        /// <summary>清除当前会话文件路径。仅在 ClearWorkspace 成功后调用。</summary>
        private void ClearCurrentSpiceFilePath()
        {
            fileService.ClearCurrentSpiceFilePath();
        }

        /// <summary>
        /// 成功导入提交：销毁旧视图、替换模型、重建视图、清除旧结果。
        /// 只在 TryFromJson 成功后调用，失败路径永远不会进入此方法。
        /// 不复用 ClearWorkspace，避免其 ResetInstanceNaming 副作用与导入编号语义冲突。
        /// </summary>
        private void CommitImportedModel(SpiceWorkspaceModel tempModel)
        {
            // 1. 安全取消所有进行中的交互
            CancelPendingWire();
            CancelPaletteDrag();
            componentDragInProgress = false;

            // 2. 清理选中状态（手动 SetSelected(false) 以清除视觉高亮）
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = null;

            // 3. 销毁旧视图
            foreach (var wire in wireViews.ToList()) wire.Destroy();
            wireViews.Clear();
            foreach (var view in componentViews.Values) Destroy(view.gameObject);
            componentViews.Clear();

            // 4. 替换模型：解除旧订阅 → 替换引用 → 订阅新模型（只订阅一次）
            Model.Changed -= HandleModelChanged;
            Model = tempModel;
            Model.Changed += HandleModelChanged;

            // 5. 按新模型重建所有元件视图（CreateComponentView 保留 InstanceId、Position、Rotation、SiValue）
            foreach (var component in Model.Components)
            {
                CreateComponentView(component);
            }

            // 6. 按新模型重建所有 Wire 视图（SpiceWorkspaceWireView 构造时按 VisualState 重建路由）
            foreach (var wire in Model.Wires)
            {
                var startView = componentViews[wire.StartComponentId];
                var endView = componentViews[wire.EndComponentId];
                wireViews.Add(new SpiceWorkspaceWireView(this, wire, startView, endView));
            }

            // 7. 清除旧仿真结果、旧诊断、旧网表，恢复为未运行状态
            generatedNetlistContent = null;
            lastOutcomeText = null;
            ResultState = SpiceWorkspaceResultState.NeverRun;
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            if (statusText != null) statusText.text = StateMessage();

            // 8. 视图适配：有内容时 FitAll，空画布时 ResetView
            if (viewController != null)
            {
                if (Model.Components.Count == 0 && Model.Wires.Count == 0)
                {
                    viewController.ResetView();
                }
                else
                {
                    viewController.FitAll();
                }
            }
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
            SpiceWorkspaceUi.Anchor(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-350f, 0f), new Vector2(-20f, 0f));

            // 工具栏新增视图命令按钮：[－] [100%] [＋] [适配全部] [重置视图]
            zoomOutButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ZoomOut", "－", MainUiTheme.ToolbarButton, null);
            var zoomOut = zoomOutButton;
            SpiceWorkspaceUi.Anchor(zoomOut.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(464f, -20f), new Vector2(498f, 20f));
            zoomLabel = SpiceWorkspaceUi.CreateText(toolbar.transform, "ZoomLabel", "100%", 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(zoomLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(506f, -14f), new Vector2(556f, 14f));
            zoomInButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ZoomIn", "＋", MainUiTheme.ToolbarButton, null);
            var zoomIn = zoomInButton;
            SpiceWorkspaceUi.Anchor(zoomIn.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(564f, -20f), new Vector2(598f, 20f));
            var fitAll = SpiceWorkspaceUi.CreateButton(toolbar.transform, "FitAll", "适配全部", MainUiTheme.ToolbarButton, null);
            SpiceWorkspaceUi.Anchor(fitAll.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(606f, -20f), new Vector2(676f, 20f));
            var resetView = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ResetView", "重置视图", MainUiTheme.ToolbarButton, null);
            SpiceWorkspaceUi.Anchor(resetView.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(684f, -20f), new Vector2(754f, 20f));

            var palette = bindings.PaletteRoot;
            var paletteTitle = SpiceWorkspaceUi.CreateText(palette.transform, "Title", "基础元件", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcCurrentSource, "直流电流源", "1 mA", 1, 2);
            CreatePaletteCard(palette.transform, SpiceComponentKind.IdealSwitch, "理想开关", "断开", 0, 3);
            CreatePaletteCard(palette.transform, SpiceComponentKind.SiliconDiode, "通用硅二极管", "D_GENERIC", 1, 3);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Resistor, "电阻", "1 kOhm", 1, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Capacitor, "电容", "1 uF", 0, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Ground, "接地", "GND", 0, 2);
            CreatePaletteCard(palette.transform, SpiceComponentKind.VoltageProbe, "电压探针", "V+ - V-", 0, 4);
            CreatePaletteCard(palette.transform, SpiceComponentKind.CurrentProbe, "电流探针", "IN → OUT", 1, 4);

            var workspace = bindings.WorkspaceViewport;
            viewportRect = workspace;
            // 为 Viewport 添加 RectMask2D 以裁剪缩放/平移后超出视口的内容
            if (workspace.GetComponent<RectMask2D>() == null) workspace.gameObject.AddComponent<RectMask2D>();
            // 创建统一 Content 容器：缩放/平移只作用于 Content，所有层共享 Content 坐标系
            contentRect = new GameObject("SpiceWorkspaceContent", typeof(RectTransform)).GetComponent<RectTransform>();
            contentRect.SetParent(workspace, false);
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            // 逻辑画布宽高 = Viewport 初始尺寸 × 3
            var viewportSize = workspace.rect.size;
            contentRect.sizeDelta = viewportSize * 3f;
            contentRect.localScale = Vector3.one;
            contentRect.anchoredPosition = Vector2.zero;
            // 将三层重新挂到 Content 下，保持 WireLayer 在最底
            var gridLayer = FindDirectGridLayer(workspace);
            ConfigureWorkspaceLayer(gridLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.WireLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.ComponentLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.OverlayLayer, contentRect);
            if (gridLayer != null) gridLayer.SetSiblingIndex(0);
            bindings.WireLayer.SetSiblingIndex(1);
            bindings.ComponentLayer.SetSiblingIndex(2);
            bindings.OverlayLayer.SetSiblingIndex(3);
            // WorkspaceRect 指向 Content：ComponentView/WireView 的坐标转换无需修改
            WorkspaceRect = contentRect;
            WireLayer = bindings.WireLayer;
            OverlayLayer = bindings.OverlayLayer;
            // BlankClick 仍挂在 Viewport 上，负责空白点击和右键撤点
            workspace.gameObject.AddComponent<SpiceWorkspaceBlankClick>().Initialize(this);
            // ViewController owns view-only zoom and navigation input.
            viewController = workspace.gameObject.AddComponent<SpiceWorkspaceViewController>();

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

            CreatePanelHeader(resultRoot, "ResultHeader", "计算结果", 34f);
            var resultHeader = resultRoot.Find("ResultHeader") as RectTransform;
            copyResultButton = SpiceWorkspaceUi.CreateButton(resultHeader, "CopyResult", "复制结果", MainUiTheme.FilterButton, CopyResult);
            SpiceWorkspaceUi.Anchor(copyResultButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-128f, 2f), new Vector2(-14f, -2f));
            resultView = CreateScrollableTextView(resultRoot, "ResultScrollView", "ResultText", 14, MainUiTheme.NormalText, 38f);
            resultText = resultView.Text;

            var netlistHeader = SpiceWorkspaceUi.CreateImage(netlistRoot, "NetlistHeader", new Color(0.96f, 0.98f, 1f));
            netlistHeader.rectTransform.pivot = new Vector2(0.5f, 1f);
            SpiceWorkspaceUi.Anchor(netlistHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -58f), new Vector2(-10f, 0f));
            var netlistTitle = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Title", "生成网表", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(netlistTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-194f, -4f));
            netlistStatusText = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Status", "尚未生成网表。", 11, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(netlistStatusText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(14f, 4f), new Vector2(-14f, 0f));
            netlistToggleButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Toggle", "展开", MainUiTheme.FilterButton, ToggleNetlist);
            SpiceWorkspaceUi.Anchor(netlistToggleButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-188f, 2f), new Vector2(-104f, -2f));
            copyNetlistButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Copy", "复制", MainUiTheme.FilterButton, CopyNetlist);
            SpiceWorkspaceUi.Anchor(copyNetlistButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-98f, 2f), new Vector2(-14f, -2f));

            netlistView = CreateScrollableTextView(netlistRoot, "NetlistScrollView", "NetlistText", 12, MainUiTheme.NormalText, 62f);
            netlistText = netlistView.Text;

            CreatePanelHeader(diagnosticRoot, "DiagnosticHeader", "诊断信息", 34f);
            diagnosticView = CreateScrollableTextView(diagnosticRoot, "DiagnosticScrollView", "DiagnosticText", 13, MainUiTheme.DangerRed, 38f);
            diagnosticText = diagnosticView.Text;
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            RefreshCopyResultButton();
            ValidateAssistantScrollStructure();

            // 视图控制器初始化（Content 已装配，zoomLabel 已创建）
            viewController.Initialize(this, viewportRect, contentRect, zoomLabel);
            // 视图命令按钮绑定
            zoomOut.onClick.AddListener(viewController.ZoomOut);
            zoomIn.onClick.AddListener(viewController.ZoomIn);
            fitAll.onClick.AddListener(viewController.FitAll);
            resetView.onClick.AddListener(viewController.ResetView);
        }

        private static RectTransform FindDirectGridLayer(RectTransform workspace)
        {
            for (var index = 0; index < workspace.childCount; index++)
            {
                var child = workspace.GetChild(index) as RectTransform;
                if (child != null && child.GetComponent<WorkspaceGrid>() != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static void ConfigureWorkspaceLayer(RectTransform layer, RectTransform content)
        {
            if (layer == null)
            {
                return;
            }

            layer.SetParent(content, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.pivot = new Vector2(0.5f, 0.5f);
            layer.anchoredPosition = Vector2.zero;
            layer.sizeDelta = Vector2.zero;
            layer.localScale = Vector3.one;
            layer.localRotation = Quaternion.identity;
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
            if (ResultState != SpiceWorkspaceResultState.Running && ResultState != SpiceWorkspaceResultState.NeverRun)
            {
                ResultState = SpiceWorkspaceResultState.Stale;
                lastOutcomeText = null;
            }
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
            RefreshNetlistUi();
            RefreshCopyResultButton();
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

        /// <summary>
        /// 获取当前可复制的正式结果/阻断诊断文本。
        /// 文本来源为同一 Presentation 的权威输出（与正式可见 ResultText 一致），
        /// 不读取隐藏 DiagnosticRoot，不重新格式化结果。
        /// 仅在 ResultState 为 Current 或 Failed 且存在实际内容时可复制；
        /// NeverRun、Running、Stale、空文本或占位文本时不可复制。
        /// </summary>
        public bool TryGetCopyableOutcomeText(out string text)
        {
            var isCurrentOutcome = ResultState == SpiceWorkspaceResultState.Current || ResultState == SpiceWorkspaceResultState.Failed;
            text = isCurrentOutcome ? lastOutcomeText : null;
            return !string.IsNullOrEmpty(text);
        }

        private void CopyResult()
        {
            if (!TryGetCopyableOutcomeText(out var text)) return;
            GUIUtility.systemCopyBuffer = text;
            statusText.text = "结果已复制";
        }

        private void RefreshCopyResultButton()
        {
            if (copyResultButton != null) copyResultButton.interactable = TryGetCopyableOutcomeText(out _);
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
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            SpiceWorkspaceUi.Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -height), new Vector2(-10f, 0f));
            var text = SpiceWorkspaceUi.CreateText(header.transform, "Title", title, 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Stretch(text.rectTransform, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        }

        // Each assistant section owns an independent standard UGUI scroll hierarchy.
        private static SpiceScrollableTextView CreateScrollableTextView(RectTransform panel, string scrollName, string textName, int fontSize, Color color, float topInset)
        {
            var scroll = new GameObject(scrollName, typeof(RectTransform), typeof(Image), typeof(ScrollRect)).GetComponent<ScrollRect>();
            scroll.transform.SetParent(panel, false);
            SpiceWorkspaceUi.Anchor(scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -topInset));
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = SpiceScrollableTextLayout.ScrollSensitivity;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scroll.transform, false);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            SpiceWorkspaceUi.Stretch(viewport, Vector2.zero, Vector2.zero);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            SpiceScrollableTextLayout.ConfigureContent(content);
            var text = SpiceWorkspaceUi.CreateText(content, textName, string.Empty, fontSize, FontStyle.Normal, TextAnchor.UpperLeft, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            return new SpiceScrollableTextView(scroll, viewport, content, text);
        }

        private static void RefreshScrollableText(SpiceScrollableTextView view, string value, bool resetToTop = true)
        {
            SpiceScrollableTextLayout.Refresh(view.ScrollRect, view.Text, value, resetToTop);
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
            if (selectedComponent.Kind == SpiceComponentKind.SiliconDiode)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "固定通用硅模型";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.VoltageProbe)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "差分电压测量";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.CurrentProbe)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "串联电流测量";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.IdealSwitch)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = selectedComponent.Data.SiValue > 0.5d ? "闭合（双击器件可切换）" : "断开（双击器件可切换）";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            parameterInput.interactable = true;
            unitButton.interactable = true;
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
            if (selectedComponent == null || currentUnits.Length == 0 || !TryApplyParameterText(selectedComponent.InstanceId, parameterInput.text, currentUnits[unitIndex], out _))
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

        /// <summary>
        /// 计算画布内容的边界（Content 局部坐标），用于"适配全部"。
        /// 边界来源：元件逻辑位置 + 旋转后视觉尺寸、Wire 两端、ManualRoutePoints。
        /// 不包含 pending Wire 预览、鼠标位置、选择装饰或整个 Content Rect。
        /// </summary>
        public Rect? ComputeContentBounds()
        {
            if (componentViews.Count == 0 && wireViews.Count == 0) return null;
            float? minX = null, minY = null, maxX = null, maxY = null;
            foreach (var component in Model.Components)
            {
                var size = SpiceWorkspaceComponentView.SizeFor(component.Kind);
                if (componentViews.TryGetValue(component.InstanceId, out var view))
                {
                    var rot = view.RotationQuarterTurns;
                    var visualSize = rot % 2 != 0 ? new Vector2(size.y, size.x) : size;
                    var half = visualSize * 0.5f;
                    ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, component.Position - half);
                    ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, component.Position + half);
                }
            }
            foreach (var wire in wireViews)
            {
                var startPos = wire.Data.StartComponentId != null && componentViews.TryGetValue(wire.Data.StartComponentId, out var startView)
                    ? startView.GetTerminalPosition(wire.Data.StartTerminalId) : Vector2.zero;
                var endPos = wire.Data.EndComponentId != null && componentViews.TryGetValue(wire.Data.EndComponentId, out var endView)
                    ? endView.GetTerminalPosition(wire.Data.EndTerminalId) : Vector2.zero;
                ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, startPos);
                ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, endPos);
                if (wire.Data.VisualState != null && wire.Data.VisualState.RouteMode == SpiceWireRouteMode.Manual)
                {
                    foreach (var waypoint in wire.Data.VisualState.Waypoints)
                        ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, waypoint);
                }
            }
            if (!minX.HasValue) return null;
            return Rect.MinMaxRect(minX.Value, minY.Value, maxX.Value, maxY.Value);
        }

        private static void ExpandBounds(ref float? minX, ref float? minY, ref float? maxX, ref float? maxY, Vector2 point)
        {
            if (!minX.HasValue || point.x < minX.Value) minX = point.x;
            if (!maxX.HasValue || point.x > maxX.Value) maxX = point.x;
            if (!minY.HasValue || point.y < minY.Value) minY = point.y;
            if (!maxY.HasValue || point.y > maxY.Value) maxY = point.y;
        }

        private string StateMessage()
        {
            return ResultState == SpiceWorkspaceResultState.Stale ? "结果已过期" : ResultState == SpiceWorkspaceResultState.Failed ? "计算失败" : ResultState == SpiceWorkspaceResultState.Current ? "结果有效" : "未计算";
        }

        private static string PaletteLabel(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? "直流电压源" : kind == SpiceComponentKind.DcCurrentSource ? "直流电流源" : kind == SpiceComponentKind.IdealSwitch ? "理想开关" : kind == SpiceComponentKind.SiliconDiode ? "通用硅二极管" : kind == SpiceComponentKind.Resistor ? "电阻" : kind == SpiceComponentKind.Capacitor ? "电容" : kind == SpiceComponentKind.Inductor ? "电感" : kind == SpiceComponentKind.VoltageProbe ? "电压探针" : kind == SpiceComponentKind.CurrentProbe ? "电流探针" : "接地";
        }

        private static string FormatResult(SpiceSimulationResult result)
        {
            return string.Join("\n\n", result.ComponentResults.Values.OrderBy(value => value.ComponentId, StringComparer.Ordinal).Select(value =>
                value.ComponentId + "  " + value.ComponentKind + "\n" + VoltageLabel(value) + "  " + value.Voltage.ToString("G6", CultureInfo.InvariantCulture) + " V\n" + FormatCurrentLine(value) + "参考方向：" + DirectionLabel(value.CurrentDirection) +
                (string.IsNullOrEmpty(value.Notes) ? string.Empty : "\n" + value.Notes)));
        }

        private static string VoltageLabel(SpiceComponentResult value)
        {
            // 二极管电压按 A→K 报告为 VAK，电压探针按 V+→V- 报告为差分电压，避免与“正端 → 负端”通用文案混淆极性。
            return value.ComponentKind == "SiliconDiode" ? "VAK" : value.ComponentKind == "VoltageProbe" ? "差分电压" : "电压";
        }

        private static string FormatCurrentLine(SpiceComponentResult value)
        {
            // 电压探针不注入电流，内部 Current=0 仅为占位值，不得作为测量值展示，故整行省略。
            if (value.ComponentKind == "VoltageProbe") return string.Empty;
            return "电流  " + value.Current.ToString("G6", CultureInfo.InvariantCulture) + " A\n";
        }

        private static string DirectionLabel(string direction)
        {
            return direction == "A-to-K" ? "A → K"
                : direction == "P-to-N" ? "P → N"
                : direction == "A-to-B" ? "A → B"
                : direction == "V-plus-to-V-minus" ? "V+ → V-"
                : direction == "IN-to-OUT" ? "IN → OUT"
                : "正端 → 负端";
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

    /// <summary>
    /// Standard UGUI layout for the assistant's independent text scroll views.
    /// Content height is owned by the layout system, never by result or diagnostic business code.
    /// </summary>
    internal static class SpiceScrollableTextLayout
    {
        public const float ScrollSensitivity = 28f;

        public static void ConfigureContent(RectTransform content)
        {
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public static bool Refresh(ScrollRect scrollRect, Text text, string value, bool resetToTop)
        {
            if (text == null) return false;
            if (text.text == value) return false;

            text.text = value;
            // Result content is authoritative; a temporarily unavailable scroll view must not hide diagnostics.
            // Hidden diagnostic sinks carry data only and must not force UGUI layout work during lifecycle changes.
            if (scrollRect == null || scrollRect.content == null || !scrollRect.isActiveAndEnabled || !scrollRect.content.gameObject.activeInHierarchy) return true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            if (!resetToTop) return true;

            scrollRect.StopMovement();
            scrollRect.horizontalNormalizedPosition = 0f;
            scrollRect.verticalNormalizedPosition = 1f;
            return true;
        }
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
