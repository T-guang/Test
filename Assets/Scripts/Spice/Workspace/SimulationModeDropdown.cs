using System;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private SimulationModeOptionVisual controlOptionVisual;
        private SimulationModeOptionVisual spiceOptionVisual;

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
            modeController.ConfigurePopupLayer(popupLayer);
            // F2-C：将 modeController 注入 TopNavigationController，使其能查询当前模式和 SPICE 求解状态。
            // 复用 SimulationModeDropdown 已有的序列化引用，不新增场景绑定。
            topNavigation.ConfigureModeController(modeController);
            ApplyMenuLayout();
            dropdownButton.onClick.AddListener(ToggleMenu);
            controlCircuitOption.onClick.AddListener(SelectControlCircuit);
            spiceDcOption.onClick.AddListener(SelectSpiceDc);
            outsideClickBlocker.onClick.AddListener(CloseMenu);
            topNavigation.TabSelected += HandleTabSelected;
            outsideClickBlocker.gameObject.SetActive(false);
            menuPanel.SetActive(false);
            RefreshOptionPresentation();
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
            // F2-C：目标与当前模式相同时不弹窗、不切换。
            if (modeController.CurrentMode == mode)
            {
                CloseMenu();
                return;
            }

            // F2-C：SPICE 正在求解时阻止模式切换，显示单按钮提示。
            if (modeController.IsSpiceSolving)
            {
                ShowSpiceSolvingBlockedForModeSwitch();
                return;
            }

            // F2-C：导航保护弹窗已打开时不允许模式切换绕过遮罩。
            if (topNavigation.IsNavigationGuardDialogOpen)
            {
                return;
            }

            topNavigation.SelectTab(0);

            if (mode == SimulationWorkspaceMode.SpiceDc)
            {
                // F2-C：电工 → SPICE，复用 TopNavigationController 的电工离开保护。
                // 确认后停止电工仿真/退出练习，再执行模式切换。
                topNavigation.RequestLeaveControlWorkspace(() =>
                {
                    modeController.SetMode(mode);
                    RefreshOptionPresentation();
                    CloseMenu();
                });
            }
            else
            {
                // F2-C：SPICE → 电工，SPICE 未求解（已检查），直接切换。
                modeController.SetMode(mode);
                RefreshOptionPresentation();
                CloseMenu();
            }
        }

        // F2-C：SPICE 求解中阻止模式切换的单按钮提示。
        private void ShowSpiceSolvingBlockedForModeSwitch()
        {
            var canvas = popupCanvas;
            if (canvas == null)
            {
                Debug.LogWarning("[F2-C] 找不到 Canvas，SPICE 求解阻止弹窗无法创建，已拒绝模式切换。");
                return;
            }

            ShowModeSwitchSingleButtonDialog(
                canvas,
                "SPICE 正在求解",
                "当前 SPICE 电路正在计算，\n请等待计算完成后再切换模式。",
                "我知道了");
        }

        // F2-C：模式切换单按钮提示弹窗。复用 F2-B.1 视觉样式。
        private void ShowModeSwitchSingleButtonDialog(Canvas canvas, string title, string message, string buttonText)
        {
            var overlay = new GameObject("ModeSwitchGuardDialog", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(canvas.transform, false);
            overlay.transform.SetAsLastSibling();

            var overlayRect = overlay.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayImage = overlay.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.42f);
            overlayImage.raycastTarget = true;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(overlay.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(500f, 270f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(16, 64);
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;

            var outline = panel.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E5E7EB");
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = panel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.05f);
            shadow.effectDistance = new Vector2(0f, -4f);

            var titleObj = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleObj.transform.SetParent(panel.transform, false);
            var titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(20f, -60f);
            titleRect.offsetMax = new Vector2(-20f, -28f);
            var titleLabel = titleObj.GetComponent<Text>();
            titleLabel.text = title;
            titleLabel.font = MainUiTheme.UiFont;
            titleLabel.fontSize = 20;
            titleLabel.fontStyle = FontStyle.Bold;
            titleLabel.alignment = TextAnchor.MiddleCenter;
            titleLabel.color = MainUiTheme.Hex("111827");

            var msgObj = new GameObject("Message", typeof(RectTransform), typeof(Text));
            msgObj.transform.SetParent(panel.transform, false);
            var msgRect = msgObj.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0f, 0f);
            msgRect.anchorMax = new Vector2(1f, 1f);
            msgRect.offsetMin = new Vector2(48f, 85f);
            msgRect.offsetMax = new Vector2(-48f, -90f);
            var msgLabel = msgObj.GetComponent<Text>();
            msgLabel.text = message;
            msgLabel.font = MainUiTheme.UiFont;
            msgLabel.fontSize = 15;
            msgLabel.alignment = TextAnchor.MiddleCenter;
            msgLabel.color = MainUiTheme.Hex("475569");
            msgLabel.lineSpacing = 1.3f;
            msgLabel.supportRichText = false;

            var okBtn = CreateModeSwitchButton(panel.transform, "OkButton", buttonText,
                new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(118f, 38f),
                MainUiTheme.Hex("2563EB"), Color.white);

            var dialog = overlay;
            okBtn.onClick.AddListener(() =>
            {
                DestroyImmediate(dialog);
                CloseMenu();
            });
        }

        private static Button CreateModeSwitchButton(
            Transform parent, string name, string text,
            Vector2 anchor, Vector2 position, Vector2 size,
            Color bgColor, Color textColor)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var img = obj.GetComponent<Image>();
            img.sprite = UiThemeTokens.GetRoundedSprite(8, 64);
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            var labelObj = new GameObject("Text", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(obj.transform, false);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelObj.GetComponent<Text>();
            label.text = text;
            label.font = MainUiTheme.UiFont;
            label.fontSize = 15;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = textColor;

            return obj.GetComponent<Button>();
        }

        private void OpenMenu()
        {
            PositionMenuBelowSimulationTab();
            transform.SetAsLastSibling();
            outsideClickBlocker.gameObject.SetActive(true);
            menuPanel.SetActive(true);
            RefreshOptionPresentation();
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

            // 弹出层拥有自己的 Canvas，因此该转换对任意 CanvasScaler 尺寸都保持正确。
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
            menuRect.sizeDelta = new Vector2(196f, 100f);

            var menuImage = menuPanel.GetComponent<Image>();
            menuImage.color = Color.white;
            menuImage.sprite = UiThemeTokens.GetRoundedSprite(8);
            menuImage.type = Image.Type.Sliced;
            var menuOutline = menuPanel.GetComponent<Outline>();
            if (menuOutline == null) menuOutline = menuPanel.AddComponent<Outline>();
            menuOutline.effectColor = MainUiTheme.Divider;
            menuOutline.effectDistance = new Vector2(1f, -1f);
            var menuShadow = menuPanel.GetComponent<Shadow>();
            if (menuShadow == null) menuShadow = menuPanel.AddComponent<Shadow>();
            menuShadow.effectColor = new Color(0f, 0f, 0f, 0.14f);
            menuShadow.effectDistance = new Vector2(0f, -3f);

            AnchorOption(controlCircuitOption.GetComponent<RectTransform>(), -48f, -8f);
            AnchorOption(spiceDcOption.GetComponent<RectTransform>(), -92f, -52f);
            controlOptionVisual = ConfigureOption(controlCircuitOption);
            spiceOptionVisual = ConfigureOption(spiceDcOption);
        }

        private static void AnchorOption(RectTransform option, float bottom, float top)
        {
            option.anchorMin = new Vector2(0f, 1f);
            option.anchorMax = new Vector2(1f, 1f);
            option.offsetMin = new Vector2(6f, bottom);
            option.offsetMax = new Vector2(-6f, top);
        }

        private static SimulationModeOptionVisual ConfigureOption(Button option)
        {
            var text = option.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = MainUiTheme.DeepText;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);

            option.transition = Selectable.Transition.None;
            var visual = option.GetComponent<SimulationModeOptionVisual>();
            if (visual == null) visual = option.gameObject.AddComponent<SimulationModeOptionVisual>();
            visual.Initialize(option.GetComponent<Image>(), text);
            return visual;
        }

        private void RefreshOptionPresentation()
        {
            if (controlOptionVisual == null || spiceOptionVisual == null)
            {
                return;
            }

            controlOptionVisual.SetSelected(modeController.CurrentMode == SimulationWorkspaceMode.ControlCircuit);
            spiceOptionVisual.SetSelected(modeController.CurrentMode == SimulationWorkspaceMode.SpiceDc);
        }

        private void ValidateBindings()
        {
            if (topNavigation == null || modeController == null || dropdownButton == null || menuPanel == null || controlCircuitOption == null || spiceDcOption == null || outsideClickBlocker == null || popupLayer == null || popupCanvas == null)
            {
                throw new InvalidOperationException("Simulation mode dropdown is missing serialized navigation or menu bindings.");
            }
        }
    }

    /// <summary>为显式绑定的模式选项提供仅影响视觉的交互反馈。</summary>
    internal sealed class SimulationModeOptionVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image background;
        private Image indicator;
        private Text label;
        private bool selected;
        private bool hovered;
        private bool pressed;
        private bool initialized;

        public void Initialize(Image optionBackground, Text optionLabel)
        {
            if (initialized)
            {
                return;
            }

            background = optionBackground;
            label = optionLabel;
            background.sprite = UiThemeTokens.GetRoundedSprite(6);
            background.type = Image.Type.Sliced;
            var outline = background.GetComponent<Outline>();
            if (outline != null) outline.enabled = false;

            var indicatorObject = new GameObject("ActiveIndicator", typeof(RectTransform), typeof(Image));
            indicatorObject.transform.SetParent(transform, false);
            indicator = indicatorObject.GetComponent<Image>();
            indicator.color = MainUiTheme.PrimaryBlue;
            indicator.raycastTarget = false;
            var indicatorRect = indicator.rectTransform;
            indicatorRect.anchorMin = new Vector2(0f, 0.5f);
            indicatorRect.anchorMax = new Vector2(0f, 0.5f);
            indicatorRect.pivot = new Vector2(0f, 0.5f);
            indicatorRect.sizeDelta = new Vector2(4f, 22f);
            indicatorRect.anchoredPosition = new Vector2(5f, 0f);
            initialized = true;
            Refresh();
        }

        public void SetSelected(bool value)
        {
            selected = value;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            Refresh();
        }

        private void Refresh()
        {
            if (!initialized)
            {
                return;
            }

            background.color = selected ? MainUiTheme.Hex("E8F1FF") :
                pressed ? MainUiTheme.Hex("DCEBFF") :
                hovered ? MainUiTheme.Hex("F1F6FF") : Color.white;
            label.color = MainUiTheme.DeepText;
            indicator.gameObject.SetActive(selected);
        }
    }
}
