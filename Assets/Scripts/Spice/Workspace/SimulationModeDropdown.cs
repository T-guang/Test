using System;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 绑定在正式 NavBar“模拟电路”项下的二级模式菜单。
    /// 它只协调已序列化的导航和模式引用，不创建页面对象，也不参与电路状态。
    /// </summary>
    public sealed class SimulationModeDropdown : MonoBehaviour
    {
        [SerializeField] private TopNavigationController topNavigation;
        [SerializeField] private SimulationModeController modeController;
        [SerializeField] private Button dropdownButton;
        [SerializeField] private GameObject menuPanel;
        [SerializeField] private Button controlCircuitOption;
        [SerializeField] private Button spiceDcOption;

        private bool initialized;

        public void Configure(TopNavigationController navigation, SimulationModeController controller, Button toggle, GameObject menu, Button controlOption, Button spiceOption)
        {
            if (initialized)
            {
                throw new InvalidOperationException("Simulation mode dropdown cannot be configured after initialization.");
            }

            topNavigation = navigation;
            modeController = controller;
            dropdownButton = toggle;
            menuPanel = menu;
            controlCircuitOption = controlOption;
            spiceDcOption = spiceOption;
        }

        private void Awake()
        {
            ValidateBindings();
            dropdownButton.onClick.AddListener(ToggleMenu);
            controlCircuitOption.onClick.AddListener(SelectControlCircuit);
            spiceDcOption.onClick.AddListener(SelectSpiceDc);
            menuPanel.SetActive(false);
            initialized = true;
        }

        private void OnDestroy()
        {
            if (!initialized)
            {
                return;
            }

            dropdownButton.onClick.RemoveListener(ToggleMenu);
            controlCircuitOption.onClick.RemoveListener(SelectControlCircuit);
            spiceDcOption.onClick.RemoveListener(SelectSpiceDc);
        }

        private void ToggleMenu()
        {
            topNavigation.SelectTab(0);
            menuPanel.SetActive(!menuPanel.activeSelf);
        }

        private void SelectControlCircuit() => SelectMode(SimulationWorkspaceMode.ControlCircuit);
        private void SelectSpiceDc() => SelectMode(SimulationWorkspaceMode.SpiceDc);

        private void SelectMode(SimulationWorkspaceMode mode)
        {
            topNavigation.SelectTab(0);
            modeController.SetMode(mode);
            menuPanel.SetActive(false);
        }

        private void ValidateBindings()
        {
            if (topNavigation == null || modeController == null || dropdownButton == null || menuPanel == null || controlCircuitOption == null || spiceDcOption == null)
            {
                throw new InvalidOperationException("Simulation mode dropdown is missing serialized navigation or menu bindings.");
            }
        }
    }
}
