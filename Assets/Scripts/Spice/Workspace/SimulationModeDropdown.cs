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
        [SerializeField] private Button outsideClickBlocker;
        [SerializeField] private RectTransform popupLayer;
        [SerializeField] private Canvas popupCanvas;

        private bool initialized;

        public void Configure(
            TopNavigationController navigation,
            SimulationModeController controller,
            Button toggle,
            GameObject menu,
            Button controlOption,
            Button spiceOption,
            Button blocker,
            RectTransform layer,
            Canvas canvas)
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
            outsideClickBlocker = blocker;
            popupLayer = layer;
            popupCanvas = canvas;
        }

        private void Awake()
        {
            ValidateBindings();
            ApplyMenuLayout();
            dropdownButton.onClick.AddListener(ToggleMenu);
            controlCircuitOption.onClick.AddListener(SelectControlCircuit);
            spiceDcOption.onClick.AddListener(SelectSpiceDc);
            outsideClickBlocker.onClick.AddListener(CloseMenu);
            topNavigation.TabSelected += HandleTabSelected;
            outsideClickBlocker.gameObject.SetActive(false);
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
            outsideClickBlocker.onClick.RemoveListener(CloseMenu);
            topNavigation.TabSelected -= HandleTabSelected;
        }

        private void Update()
        {
            if (menuPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseMenu();
            }
        }

        private void ToggleMenu()
        {
            if (menuPanel.activeSelf)
            {
                CloseMenu();
                return;
            }

            topNavigation.SelectTab(0);
            OpenMenu();
        }

        private void SelectControlCircuit() => SelectMode(SimulationWorkspaceMode.ControlCircuit);
        private void SelectSpiceDc() => SelectMode(SimulationWorkspaceMode.SpiceDc);

        private void SelectMode(SimulationWorkspaceMode mode)
        {
            topNavigation.SelectTab(0);
            modeController.SetMode(mode);
            CloseMenu();
        }

        private void OpenMenu()
        {
            PositionMenuBelowSimulationTab();
            transform.SetAsLastSibling();
            outsideClickBlocker.gameObject.SetActive(true);
            menuPanel.SetActive(true);
        }

        private void CloseMenu()
        {
            if (!initialized)
            {
                return;
            }

            menuPanel.SetActive(false);
            outsideClickBlocker.gameObject.SetActive(false);
        }

        private void HandleTabSelected(int index)
        {
            if (index != 0)
            {
                CloseMenu();
            }
        }

        private void PositionMenuBelowSimulationTab()
        {
            var buttonRect = dropdownButton.GetComponent<RectTransform>();
            var eventCamera = popupCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : popupCanvas.worldCamera;
            var buttonWorldCorners = new Vector3[4];
            buttonRect.GetWorldCorners(buttonWorldCorners);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, buttonWorldCorners[0]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(popupLayer, screenPoint, eventCamera, out var localPoint))
            {
                throw new InvalidOperationException("Simulation mode menu could not convert the navigation button position into the popup layer.");
            }

            var menuRect = menuPanel.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(.5f, .5f);
            menuRect.anchorMax = new Vector2(.5f, .5f);
            menuRect.pivot = new Vector2(0f, 1f);

            // The popup owns its own Canvas, so this conversion remains correct for every CanvasScaler size.
            var desiredPosition = localPoint + new Vector2(0f, -5f);
            var popupRect = popupLayer.rect;
            var menuSize = menuRect.sizeDelta;
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, popupRect.xMin, popupRect.xMax - menuSize.x);
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, popupRect.yMin + menuSize.y, popupRect.yMax);
            menuRect.anchoredPosition = desiredPosition;
        }

        private void ApplyMenuLayout()
        {
            var menuRect = menuPanel.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(.5f, .5f);
            menuRect.anchorMax = new Vector2(.5f, .5f);
            menuRect.pivot = new Vector2(0f, 1f);
            menuRect.sizeDelta = new Vector2(208f, 92f);

            AnchorOption(controlCircuitOption.GetComponent<RectTransform>(), -44f, -6f);
            AnchorOption(spiceDcOption.GetComponent<RectTransform>(), -86f, -48f);
        }

        private static void AnchorOption(RectTransform option, float bottom, float top)
        {
            option.anchorMin = new Vector2(0f, 1f);
            option.anchorMax = new Vector2(1f, 1f);
            option.offsetMin = new Vector2(6f, bottom);
            option.offsetMax = new Vector2(-6f, top);
        }

        private void ValidateBindings()
        {
            if (topNavigation == null || modeController == null || dropdownButton == null || menuPanel == null || controlCircuitOption == null || spiceDcOption == null || outsideClickBlocker == null || popupLayer == null || popupCanvas == null)
            {
                throw new InvalidOperationException("Simulation mode dropdown is missing serialized navigation or menu bindings.");
            }
        }
    }
}
