using System;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    public enum SimulationWorkspaceMode
    {
        ControlCircuit,
        SpiceDc
    }

    /// <summary>
    /// 仅管理模拟电路页面中两套局部 Root 的显隐和模式选择器显示。
    /// 它不访问任一电路模型、不销毁页面状态，也不执行任何仿真计算。
    /// </summary>
    public sealed class SimulationModeController : MonoBehaviour
    {
        [SerializeField] private GameObject controlTopBar;
        [SerializeField] private GameObject controlPalette;
        [SerializeField] private GameObject controlWorkspace;
        [SerializeField] private GameObject localInspectorPanel;
        [SerializeField] private GameObject spiceModeRoot;
        [SerializeField] private Button modeSelectorButton;
        [SerializeField] private GameObject modeMenuPanel;
        [SerializeField] private Button controlCircuitOption;
        [SerializeField] private Button spiceDcOption;
        [SerializeField] private Text modeSelectorLabel;

        private bool initialized;

        public SimulationWorkspaceMode CurrentMode { get; private set; } = SimulationWorkspaceMode.ControlCircuit;

        /// <summary>仅供场景装配器写入显式 Root 和选择器引用；不会查询或创建页面对象。</summary>
        public void Configure(
            GameObject topBar,
            GameObject palette,
            GameObject workspace,
            GameObject inspector,
            GameObject spiceRoot,
            Button selector,
            GameObject menu,
            Button controlOption,
            Button spiceOption,
            Text label)
        {
            if (initialized)
            {
                throw new InvalidOperationException("Simulation mode controller cannot be configured after initialization.");
            }

            controlTopBar = topBar;
            controlPalette = palette;
            controlWorkspace = workspace;
            localInspectorPanel = inspector;
            spiceModeRoot = spiceRoot;
            modeSelectorButton = selector;
            modeMenuPanel = menu;
            controlCircuitOption = controlOption;
            spiceDcOption = spiceOption;
            modeSelectorLabel = label;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (!initialized)
            {
                return;
            }

            modeSelectorButton.onClick.RemoveListener(ToggleModeMenu);
            controlCircuitOption.onClick.RemoveListener(SelectControlCircuit);
            spiceDcOption.onClick.RemoveListener(SelectSpiceDc);
        }

        /// <summary>
        /// 场景基础设施由序列化引用提供；重复调用仅保留首个初始化，避免重复注册按钮监听。
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            ValidateBindings();
            modeSelectorButton.onClick.AddListener(ToggleModeMenu);
            controlCircuitOption.onClick.AddListener(SelectControlCircuit);
            spiceDcOption.onClick.AddListener(SelectSpiceDc);
            initialized = true;
            ApplyMode(SimulationWorkspaceMode.ControlCircuit, true);
        }

        public void ToggleModeMenu()
        {
            EnsureInitialized();
            modeMenuPanel.SetActive(!modeMenuPanel.activeSelf);
        }

        public void SelectControlCircuit()
        {
            SetMode(SimulationWorkspaceMode.ControlCircuit);
        }

        public void SelectSpiceDc()
        {
            SetMode(SimulationWorkspaceMode.SpiceDc);
        }

        public void SetMode(SimulationWorkspaceMode mode)
        {
            EnsureInitialized();
            if (CurrentMode == mode)
            {
                modeMenuPanel.SetActive(false);
                return;
            }

            ApplyMode(mode, false);
        }

        private void ApplyMode(SimulationWorkspaceMode mode, bool force)
        {
            if (!force && CurrentMode == mode)
            {
                return;
            }

            var useSpice = mode == SimulationWorkspaceMode.SpiceDc;
            controlTopBar.SetActive(!useSpice);
            controlPalette.SetActive(!useSpice);
            controlWorkspace.SetActive(!useSpice);
            localInspectorPanel.SetActive(!useSpice);
            spiceModeRoot.SetActive(useSpice);
            modeMenuPanel.SetActive(false);
            modeSelectorLabel.text = useSpice ? "基础电路原理仿真" : "电工控制仿真";
            CurrentMode = mode;
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                throw new InvalidOperationException("Simulation mode controller has not been initialized.");
            }
        }

        private void ValidateBindings()
        {
            if (controlTopBar == null || controlPalette == null || controlWorkspace == null || localInspectorPanel == null || spiceModeRoot == null ||
                modeSelectorButton == null || modeMenuPanel == null || controlCircuitOption == null || spiceDcOption == null || modeSelectorLabel == null)
            {
                throw new InvalidOperationException(
                    "Simulation mode controller is missing serialized control, SPICE, or selector bindings.");
            }
        }
    }
}
