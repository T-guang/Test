using System;
using UnityEngine;

namespace ElectricalSim.Spice.Workspace
{
    public enum SimulationWorkspaceMode
    {
        ControlCircuit,
        SpiceDc
    }

    /// <summary>
    /// 仅管理模拟电路页面中两套局部 Root 的显隐。
    /// 它不访问任一电路模型、不销毁页面状态，也不执行任何仿真计算。
    /// </summary>
    public sealed class SimulationModeController : MonoBehaviour
    {
        [SerializeField] private GameObject controlTopBar;
        [SerializeField] private GameObject controlPalette;
        [SerializeField] private GameObject controlWorkspace;
        [SerializeField] private GameObject localInspectorPanel;
        [SerializeField] private GameObject spiceModeRoot;
        private bool initialized;

        public SimulationWorkspaceMode CurrentMode { get; private set; } = SimulationWorkspaceMode.ControlCircuit;

        /// <summary>仅供场景装配器写入显式内容 Root；不会查询或创建页面对象。</summary>
        public void Configure(
            GameObject topBar,
            GameObject palette,
            GameObject workspace,
            GameObject inspector,
            GameObject spiceRoot)
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
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// 场景内容 Root 由序列化引用提供；重复调用仅保留首个初始化。
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            ValidateBindings();
            initialized = true;
            ApplyMode(SimulationWorkspaceMode.ControlCircuit, true);
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
            if (controlTopBar == null || controlPalette == null || controlWorkspace == null || localInspectorPanel == null || spiceModeRoot == null)
            {
                throw new InvalidOperationException(
                    "Simulation mode controller is missing serialized control or SPICE content bindings.");
            }
        }
    }
}
