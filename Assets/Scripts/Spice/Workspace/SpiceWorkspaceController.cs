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
    public sealed partial class SpiceWorkspaceController : MonoBehaviour
    {
        private const string UnexpectedSimulationErrorMessage =
            "仿真运行失败，系统未能完成本次计算。\n请检查电路后重试；若问题持续出现，请将日志交给维护人员。\n错误编号：SPICE_RUNTIME_UNEXPECTED";

        private readonly Dictionary<string, SpiceWorkspaceComponentView> componentViews = new Dictionary<string, SpiceWorkspaceComponentView>(StringComparer.Ordinal);
        private readonly List<SpiceWorkspaceWireView> wireViews = new List<SpiceWorkspaceWireView>();
        private SpiceWorkspaceViewBindings bindings;
        private SpiceSimulationService simulationService;
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
        private Text opAmpInfoText;
        private InputField parameterInput;
        private Text acPhaseLabel;
        private InputField acPhaseInput;
        private Button parameterApplyButton;
        private Button unitButton;
        private Text unitLabel;
        private Button dcAnalysisModeButton;
        private Button acAnalysisModeButton;
        private InputField acFrequencyInput;
        private Button applyAcFrequencyButton;
        private readonly Dictionary<SpiceComponentKind, CanvasGroup> paletteCardGroups = new Dictionary<SpiceComponentKind, CanvasGroup>();
        private Button runButton;
        private Button rotateButton;
        private Button netlistToggleButton;
        private Button copyNetlistButton;
        private SpiceScrollableTextView resultView;
        private SpiceScrollableTextView netlistView;
        private SpiceScrollableTextView diagnosticView;
        private bool netlistExpanded;
        private string generatedNetlistContent;
        private long generatedNetlistRevision = -1;
        private string[] currentUnits = Array.Empty<string>();
        private int unitIndex;
        private bool initialized;
        private CancellationTokenSource simulationCancellation;
        private Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> simulationOverrideForTesting;
        private long electricalRevision;
        private long calculationRequestId;
        private long activeCalculationRequestId;
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
        // 文件工作流 文件操作工具栏按钮：保存 / 另存为 / 导入
        private Button saveFileButton;
        private Button saveAsFileButton;
        private Button importFileButton;
        private bool isDirty;

        public SpiceWorkspaceModel Model { get; private set; } = new SpiceWorkspaceModel();
        public SpiceWorkspaceResultState ResultState { get; private set; } = SpiceWorkspaceResultState.NeverRun;
        public RectTransform WorkspaceRect { get; private set; }
        public RectTransform ViewportRect => viewportRect;
        public RectTransform ContentRect => contentRect;
        public SpiceWorkspaceViewController ViewController => viewController;
        public RectTransform WireLayer { get; private set; }
        public RectTransform OverlayLayer { get; private set; }
        public bool HasPendingWire => pendingComponent != null;
        /// <summary>仅表示尚未写入当前图纸文件的电气编辑；结果和临时 UI 状态不参与保存契约。</summary>
        public bool IsDirty => isDirty;
        internal long ElectricalRevisionForTesting => electricalRevision;
        public event Action<SpiceWorkspaceComponentData> ParameterDialogRequested;

        // 文件工作流 文件操作事件：Host 订阅后负责打开 Windows 文件对话框、替换确认和用户反馈。
        // Controller 只在按钮点击且未运行中时触发，不直接调用 正式工作流 文件 API。
        public event Action SaveRequested;
        public event Action SaveAsRequested;
        public event Action ImportRequested;

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
            simulationService = new SpiceSimulationService();
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
    }
}
