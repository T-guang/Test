using System.Collections.Generic;
using System;
using ElectricalSim.UI.CommonTools;
using ElectricalSim.Practice;
using ElectricalSim.Core;
using ElectricalSim.Spice.Workspace;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 顶部导航的场景绑定控制器：在 Awake 配置 PageRouter、导航页根节点与标签点击事件，
    /// 在 Start 补齐全局退出入口。它只切换页面和维护导航视觉，不负责各页面的内容构建、
    /// 模板加载或退出前保存；这些职责分别留在页面 Controller、TemplateLoadController 与 ExitApplicationDialog。
    /// 当前实现依赖 Demo 场景中序列化的页根节点和按钮列表，运行时仅补建缺失的 PageRouter/退出按钮。
    /// Editor 与 Windows Player 共用此路径；后续若改为预制体化导航，需重新确认重复监听器和单实例退出按钮约束。
    /// </summary>
    public sealed class TopNavigationController : MonoBehaviour
    {
        // 顶部导航只拥有页面选择与离开守卫的 UI 编排，不拥有电路、练习或仿真状态。任何页面切换前的确认
        // 必须委托各会话/Workspace 的正式清理入口，不能通过隐藏页面来绕过停止仿真或退出练习的生命周期。
        [SerializeField] private PageRouter pageRouter;
        [SerializeField] private List<Button> tabButtons = new List<Button>();
        [SerializeField] private List<Text> tabLabels = new List<Text>();
        [SerializeField] private GameObject simulationRoot;
        [SerializeField] private GameObject blueprintRoot;
        [SerializeField] private GameObject encyclopediaRoot;
        [SerializeField] private GameObject toolsRoot;
        [SerializeField] private GameObject emptyPageRoot;
        [SerializeField] private Text emptyPageTitle;

        private Button exitButton;

        /// <summary>在导航标签完成页面切换后通知局部浮层关闭自身，不参与页面内容逻辑。</summary>
        public event Action<int> TabSelected;

        // 导航保护字段：弹窗显示期间锁定新的离开请求，保留第一次用户明确点击的目标页面。
        private GameObject navigationGuardDialog;
        private int pendingNavigationIndex = -1;

        // 模式控制器引用用于查询当前模式（ControlCircuit/SpiceDc）和 SPICE 求解状态。
        // 由 SimulationModeDropdown.Awake 通过 ConfigureModeController 注入，不新增场景绑定。
        private SimulationModeController modeController;

        private void Awake()
        {
            // Awake 只建立持久 UI 引用和基础事件绑定；不要在此处直接切换工作区或清理业务状态，场景加载顺序
            // 在 Editor、Player 和动态页面重建时并不恒定。
            // 路由器必须在任何标签点击前完成配置；这里的空引用兜底仅服务当前场景兼容，不应扩展为页面创建入口。
            if (pageRouter == null)
            {
                pageRouter = FindObjectOfType<PageRouter>();
            }

            if (pageRouter == null)
            {
                pageRouter = gameObject.AddComponent<PageRouter>();
            }

            pageRouter.Configure(
                simulationRoot,
                blueprintRoot,
                null,
                encyclopediaRoot,
                toolsRoot,
                null,
                emptyPageRoot,
                emptyPageTitle);

            if (toolsRoot != null && toolsRoot.GetComponent<CommonToolsPageController>() == null)
            {
                toolsRoot.AddComponent<CommonToolsPageController>();
            }

            if (simulationRoot != null)
            {
                var bg = simulationRoot.GetComponent<Image>();
                if (bg != null) bg.color = UiThemeTokens.Background;
            }

            for (var i = 0; i < tabButtons.Count; i++)
            {
                var index = i;
                tabButtons[i].onClick.AddListener(() => SelectTab(index));
            }

            ApplyThemeToNavBar();
            SelectTab(0);
        }

        private void Start()
        {
            // 在全部 Awake 完成后统一补建或复用全局退出按钮，避免重复创建导航按钮。
            EnsureExitButton();
        }

        private void ApplyThemeToNavBar()
        {
            // 主题应用只改变导航栏表现层。颜色、圆角和图标不是页面激活或模式状态的来源，不能被守卫逻辑读取。
            var navRect = GetComponent<RectTransform>();
            if (navRect != null)
            {
                navRect.sizeDelta = new Vector2(navRect.sizeDelta.x, MainUiTheme.NavBarHeight);
                var bg = navRect.GetComponent<Image>();
                if (bg != null) bg.color = Color.white;
                
                if (navRect.Find("BottomBorder") == null)
                {
                    var border = new GameObject("BottomBorder", typeof(RectTransform), typeof(Image));
                    border.transform.SetParent(navRect, false);
                    var borderRt = border.GetComponent<RectTransform>();
                    borderRt.anchorMin = new Vector2(0, 0);
                    borderRt.anchorMax = new Vector2(1, 0);
                    borderRt.pivot = new Vector2(0.5f, 0f);
                    borderRt.sizeDelta = new Vector2(0, 1f);
                    borderRt.anchoredPosition = Vector2.zero;
                    border.GetComponent<Image>().color = UiThemeTokens.BorderColor;
                }
            }

            ApplyBrandGroupLayout();

            for (int i = 0; i < tabButtons.Count; i++)
            {
                var btnRect = tabButtons[i].GetComponent<RectTransform>();
                if (btnRect != null)
                {
                    btnRect.sizeDelta = new Vector2(btnRect.sizeDelta.x, 40f);
                    var img = tabButtons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = UiThemeTokens.GetRoundedSprite(8);
                        img.type = Image.Type.Sliced;
                    }
                }
                if (i < tabLabels.Count && tabLabels[i] != null)
                {
                    if (i == 5)
                    {
                        tabLabels[i].text = "系统信息";
                    }
                    MainUiTheme.ApplyTextRole(tabLabels[i], MainUiTheme.UiTextRole.NavText);
                }
            }
        }

        private void ApplyBrandGroupLayout()
        {
            // 品牌区布局与 tab 容器解耦，窗口宽度变化时只调整可视排版，不能重新注册 tab 或改变当前页面选择。
            var titleTransform = transform.Find("Logo") ?? transform.Find("Title");
            var brandRect = titleTransform as RectTransform;
            if (brandRect == null)
            {
                return;
            }

            brandRect.anchorMin = new Vector2(0f, 0.5f);
            brandRect.anchorMax = new Vector2(0f, 0.5f);
            brandRect.pivot = new Vector2(0f, 0.5f);
            brandRect.anchoredPosition = new Vector2(24f, 0f);
            brandRect.sizeDelta = new Vector2(404f, 48f);

            var layout = titleTransform.GetComponent<HorizontalLayoutGroup>() ?? titleTransform.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childControlWidth = false;

            var iconWrapper = titleTransform.Find("IconWrapper") as RectTransform;
            if (iconWrapper == null)
            {
                var wrapperObj = new GameObject("IconWrapper", typeof(RectTransform), typeof(LayoutElement));
                wrapperObj.transform.SetParent(titleTransform, false);
                wrapperObj.transform.SetAsFirstSibling();
                iconWrapper = wrapperObj.GetComponent<RectTransform>();
            }

            iconWrapper.sizeDelta = new Vector2(40f, 40f);
            var wrapperLayout = iconWrapper.GetComponent<LayoutElement>() ?? iconWrapper.gameObject.AddComponent<LayoutElement>();
            wrapperLayout.preferredWidth = 40f;
            wrapperLayout.preferredHeight = 40f;
            wrapperLayout.flexibleWidth = 0f;
            wrapperLayout.flexibleHeight = 0f;

            var icon = iconWrapper.Find("Icon") as RectTransform;
            if (icon == null)
            {
                var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(iconWrapper, false);
                icon = iconObj.GetComponent<RectTransform>();
            }

            icon.anchorMin = new Vector2(0.5f, 0.5f);
            icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(40f, 40f);

            var iconImage = icon.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            iconImage.sprite = UiIconLibrary.Load("Logo/ui_logo_main_320");
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;

            var titleText = titleTransform.Find("Text")?.GetComponent<Text>();
            if (titleText == null)
            {
                titleText = titleTransform.GetComponentInChildren<Text>(true);
            }

            if (titleText != null)
            {
                titleText.transform.SetAsLastSibling();
                titleText.color = MainUiTheme.Hex("111827");
                MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.BrandTitle);

                var textLayout = titleText.GetComponent<LayoutElement>() ?? titleText.gameObject.AddComponent<LayoutElement>();
                textLayout.preferredWidth = 300f;
                textLayout.preferredHeight = 40f;
            }
        }

        private void EnsureExitButton()
        {
            // 退出按钮可由运行时 UI 缺失路径补建；必须复用已有同名按钮，避免多个“退出”入口分别绑定不同的确认行为。
            // 退出按钮由导航栏统一持有；重复进入页面时复用已有实例，避免生成多个确认弹窗入口。
            if (exitButton == null)
            {
                var navBar = transform.Find("MainAppRoot/NavBar") as RectTransform;
                if (navBar == null)
                {
                    return;
                }

                var existingButton = navBar.Find("LogoutButton");
                exitButton = existingButton != null ? existingButton.GetComponent<Button>() : null;
                if (exitButton == null)
                {
                    exitButton = CreateExitButton(navBar);
                }
            }

            exitButton.gameObject.SetActive(true);
            ConfigureExitButton(exitButton);
            exitButton.onClick.RemoveListener(ShowExitDialog);
            exitButton.onClick.AddListener(ShowExitDialog);
        }

        private static Button CreateExitButton(RectTransform navBar)
        {
            var buttonObject = new GameObject("ExitApplicationButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(navBar, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            return buttonObject.GetComponent<Button>();
        }

        private static void ConfigureExitButton(Button button)
        {
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-24f, 0f);
            buttonRect.sizeDelta = new Vector2(82f, 36f);

            var image = button.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.DangerBorder;
            outline.effectDistance = new Vector2(1f, -1f);

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            colors.pressedColor = new Color(0.86f, 0.92f, 1f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 0.5019608f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

            var label = button.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                var labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(button.transform, false);
                label = labelObject.GetComponent<Text>();
            }

            label.text = "退出";
            label.font = MainUiTheme.UiFont;
            label.fontSize = 14;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = MainUiTheme.DangerRed;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(28f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);

            UiIconLibrary.EnsureButtonIcon(
                button,
                "ui_exit_practice_20",
                new Vector2(16f, 16f),
                new Vector2(16f, 0f),
                MainUiTheme.DangerRed);
        }

        private void ShowExitDialog()
        {
            ExitApplicationDialog.Show(transform);
        }

        public void SelectTab(int index)
        {
            // SelectTab 是唯一受守卫的导航入口：先判断当前页面是否允许离开，再执行页面激活。不能由按钮监听
            // 直接修改 page active 状态，否则会跳过运行中仿真和练习会话的清理确认。
            // 导航保护只在从模拟电路页离开到其他页面时才需要拦截。
            // 所有外部 SelectTab(0) 调用（进入模拟电路页）和不在模拟电路页时的切换都不会被拦截。
            if (NeedsNavigationGuard(index))
            {
                RequestGuardedNavigation(index);
                return;
            }

            ExecuteNavigation(index);
        }

        // 由 SimulationModeDropdown.Awake 调用，注入模式控制器引用。
        // 不使用 FindObjectOfType 或场景扫描，复用 SimulationModeDropdown 已有序列化引用。
        public void ConfigureModeController(SimulationModeController controller)
        {
            // 模式控制器由外部注入，导航只查询其状态来决定守卫文案和离开条件；不在此处切换分析模式或启动求解。
            modeController = controller;
        }

        // 查询当前是否有导航保护弹窗打开，供 SimulationModeDropdown 防重入使用。
        public bool IsNavigationGuardDialogOpen => navigationGuardDialog != null;

        // 电工离开保护公开入口，供 SimulationModeDropdown 在切换到 SPICE 前复用。
        // 判断电工运行和练习状态，显示与 F2-B 同款弹窗（模式切换专用文案），确认后完成清理并调用 onConfirmed。
        // 无需保护时直接调用 onConfirmed。取消或弹窗创建失败时不调用 onConfirmed。
        public void RequestLeaveControlWorkspace(Action onConfirmed)
        {
            // 外部控制页复用同一离开守卫，确认后的回调只在相关会话已完成正式收口后执行，避免导航方复制清理逻辑。
            if (navigationGuardDialog != null)
            {
                return;
            }

            var practice = PracticeSessionController.Instance;
            var workspace = FindObjectOfType<WorkspaceController>(true);

            var isPracticeActive = practice != null && practice.IsPracticeActive;
            var isSimulationRunning = workspace != null && workspace.IsSimulationRunning;

            if (isPracticeActive)
            {
                ShowPracticeLeaveForModeSwitchDialog(onConfirmed);
            }
            else if (isSimulationRunning)
            {
                ShowSimulationLeaveForModeSwitchDialog(onConfirmed);
            }
            else
            {
                onConfirmed?.Invoke();
            }
        }

        // 判断是否需要导航保护：当前在模拟电路页且目标不是模拟电路页时才拦截。
        private bool NeedsNavigationGuard(int targetIndex)
        {
            // 守卫的判断依据是当前模式/会话的真实状态，不是按钮高亮或目标页名称；视觉状态可能滞后一帧，不能作为事实。
            if (pageRouter == null)
            {
                return false;
            }

            var targetPage = ToPageId(targetIndex);
            return pageRouter.CurrentPage == PageId.Simulation && targetPage != PageId.Simulation;
        }

        // 请求受保护的导航。弹窗显示期间锁定新的离开请求，保留第一次用户明确点击的目标页面。
        private void RequestGuardedNavigation(int targetIndex)
        {
            // 防重入：弹窗已显示时忽略后续点击，不静默更换 pending target。
            if (navigationGuardDialog != null)
            {
                return;
            }

            pendingNavigationIndex = targetIndex;

            // 当前为 SPICE 模式且正在求解时，阻止离开 Simulation 页面。
            if (modeController != null
                && modeController.CurrentMode == SimulationWorkspaceMode.SpiceDc
                && modeController.IsSpiceSolving)
            {
                ShowSpiceSolvingBlockedDialog();
                return;
            }

            var practice = PracticeSessionController.Instance;
            var workspace = FindObjectOfType<WorkspaceController>(true);

            var isPracticeActive = practice != null && practice.IsPracticeActive;
            var isSimulationRunning = workspace != null && workspace.IsSimulationRunning;

            // 当前为 SPICE 模式时，电工 WorkspaceController.IsSimulationRunning 不应触发电工弹窗。
            // 电工 workspace 在 SPICE 模式下虽隐藏但 IsSimulationRunning 可能保持 true，需按当前模式区分。
            if (modeController != null && modeController.CurrentMode == SimulationWorkspaceMode.SpiceDc)
            {
                isSimulationRunning = false;
            }

            if (isPracticeActive)
            {
                ShowPracticeLeaveDialog(targetIndex);
            }
            else if (isSimulationRunning)
            {
                ShowSimulationLeaveDialog(targetIndex);
            }
            else
            {
                // 无需保护，直接切换。
                ExecuteNavigation(targetIndex);
                pendingNavigationIndex = -1;
            }
        }

        // SPICE 求解中阻止导航的单按钮提示。
        private void ShowSpiceSolvingBlockedDialog()
        {
            // SPICE 求解中禁止离开是为了避免 UI 已切页而异步结果仍回写旧工作区；对话框只说明阻止原因，
            // 不取消求解或修改其结果生命周期。
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[F2-C] 找不到 Canvas，SPICE 求解阻止弹窗无法创建，已拒绝导航。");
                pendingNavigationIndex = -1;
                return;
            }

            ShowSingleButtonDialog(
                canvas,
                "SPICE 正在求解",
                "当前 SPICE 电路正在计算，\n请等待计算完成后再切换模式。",
                "我知道了");
        }

        // 模式切换专用的电工运行中确认弹窗。
        private void ShowSimulationLeaveForModeSwitchDialog(Action onConfirmed)
        {
            // 模式切换确认与普通页面离开共享“先完成正式停止、再执行回调”的顺序，避免控制仿真与 SPICE UI 同时拥有画布。
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[F2-C] 找不到 Canvas，模式切换保护弹窗无法创建，已拒绝切换。");
                return;
            }

            ShowNavigationGuardDialog(
                canvas,
                "离开模拟电路",
                "当前电路正在运行。\n切换到 SPICE 将停止仿真，但会保留当前画布。",
                "确认切换",
                "取消",
                () =>
                {
                    var ws = FindObjectOfType<WorkspaceController>(true);
                    ws?.StopSimulation();
                    onConfirmed?.Invoke();
                });
        }

        // 模式切换专用的练习确认弹窗。
        private void ShowPracticeLeaveForModeSwitchDialog(Action onConfirmed)
        {
            // 练习模式切换必须经过 PracticeSessionController 的退出路径，不能只关闭参考面板；模板上下文与画布需同时收口。
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[F2-C] 找不到 Canvas，模式切换保护弹窗无法创建，已拒绝切换。");
                return;
            }

            ShowNavigationGuardDialog(
                canvas,
                "退出当前练习",
                "切换到 SPICE 将停止仿真、退出当前练习，\n并清空练习画布和参考图纸。",
                "确认切换",
                "继续练习",
                () =>
                {
                    var practice = PracticeSessionController.Instance;
                    practice?.EndPracticeSessionAndClearCanvas();
                    onConfirmed?.Invoke();
                });
        }

        // 单按钮提示弹窗用于 SPICE 求解中阻止导航；视觉样式与其他导航守卫保持一致。
        private void ShowSingleButtonDialog(Canvas canvas, string title, string message, string buttonText)
        {
            var overlay = new GameObject("NavigationGuardDialog", typeof(RectTransform), typeof(Image));
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

            var okBtn = CreateGuardButton(panel.transform, "OkButton", buttonText,
                new Vector2(0.5f, 0f), new Vector2(0f, 48f), new Vector2(118f, 38f),
                MainUiTheme.Hex("2563EB"), Color.white);
            var okLabel = okBtn.transform.Find("Text")?.GetComponent<Text>();
            if (okLabel != null)
            {
                okLabel.fontStyle = FontStyle.Bold;
            }

            navigationGuardDialog = overlay;

            okBtn.onClick.AddListener(() =>
            {
                CloseNavigationGuardDialog();
            });
        }

        // 普通电路正在仿真时的确认弹窗。
        private void ShowSimulationLeaveDialog(int targetIndex)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                // 弹窗无法创建时拒绝导航，保持原页面和状态。
                Debug.LogWarning("[F2-B] 找不到 Canvas，导航保护弹窗无法创建，已拒绝离开模拟电路页面。");
                pendingNavigationIndex = -1;
                return;
            }

            ShowNavigationGuardDialog(
                canvas,
                "离开模拟电路",
                "当前电路正在运行。\n离开此页面将停止仿真，但会保留当前画布。",
                "确认离开",
                "取消",
                () =>
                {
                    var ws = FindObjectOfType<WorkspaceController>(true);
                    ws?.StopSimulation();
                    ExecuteNavigation(targetIndex);
                });
        }

        // 练习模式下的确认弹窗。
        private void ShowPracticeLeaveDialog(int targetIndex)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[F2-B] 找不到 Canvas，导航保护弹窗无法创建，已拒绝离开模拟电路页面。");
                pendingNavigationIndex = -1;
                return;
            }

            ShowNavigationGuardDialog(
                canvas,
                "退出当前练习",
                "离开此页面将停止仿真、退出当前练习，\n并清空练习画布和参考图纸。",
                "确认离开",
                "继续练习",
                () =>
                {
                    var practice = PracticeSessionController.Instance;
                    practice?.EndPracticeSessionAndClearCanvas();
                    ExecuteNavigation(targetIndex);
                });
        }

        // 统一创建导航保护弹窗，复用 LoadTemplateConfirmDialog 的样式，不新增平行 Dialog 类型。
        private void ShowNavigationGuardDialog(
            Canvas canvas, string title, string message,
            string confirmText, string cancelText, Action onConfirm)
        {
            // 确认对话框是暂时的 UI 覆盖层，关闭时必须释放其事件和对象。它不锁定或修改画布本身，真正的退出操作
            // 仍由确认回调调用相应 controller 完成。
            var overlay = new GameObject("NavigationGuardDialog", typeof(RectTransform), typeof(Image));
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

            // 容器样式与 PracticeSessionController.ShowPracticeConfirm 对齐（圆角、边框、阴影和尺寸）。
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

            // 按钮尺寸、颜色和位置与 CreateDialogButton 对齐（取消浅灰、确认蓝色、确认文字 Bold）。
            var confirmBtn = CreateGuardButton(panel.transform, "ConfirmButton", confirmText,
                new Vector2(0.5f, 0f), new Vector2(-74f, 48f), new Vector2(118f, 38f),
                MainUiTheme.Hex("2563EB"), Color.white);
            var cancelBtn = CreateGuardButton(panel.transform, "CancelButton", cancelText,
                new Vector2(0.5f, 0f), new Vector2(74f, 48f), new Vector2(118f, 38f),
                MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("334155"));

            var confirmLabel = confirmBtn.transform.Find("Text")?.GetComponent<Text>();
            if (confirmLabel != null)
            {
                confirmLabel.fontStyle = FontStyle.Bold;
            }

            navigationGuardDialog = overlay;

            confirmBtn.onClick.AddListener(() =>
            {
                CloseNavigationGuardDialog();
                onConfirm?.Invoke();
            });

            cancelBtn.onClick.AddListener(() =>
            {
                CloseNavigationGuardDialog();
            });
        }

        private static Button CreateGuardButton(
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
            label.alignment = TextAnchor.MiddleCenter;
            label.color = textColor;

            return obj.GetComponent<Button>();
        }

        // 关闭导航保护弹窗并清空 pending target。
        private void CloseNavigationGuardDialog()
        {
            if (navigationGuardDialog != null)
            {
                DestroyImmediate(navigationGuardDialog);
                navigationGuardDialog = null;
            }
            pendingNavigationIndex = -1;
        }

        // 实际执行页面切换（PageRouter.ShowPage + RefreshTabStates + TabSelected）。
        private void ExecuteNavigation(int index)
        {
            // 执行阶段只激活目标页面并刷新 tab 视觉；所有副作用应已在守卫确认路径完成，使直接导航和确认后导航
            // 共享同一最终行为。
            var page = ToPageId(index);
            if (pageRouter != null)
            {
                pageRouter.ShowPage(page);
            }

            RefreshTabStates(index);
            TabSelected?.Invoke(index);
        }

        private void RefreshTabStates(int activeIndex)
        {
            // tab 的选中颜色是 ExecuteNavigation 的结果投影。不要通过修改高亮来模拟导航成功，否则 pageRouter 与 UI 状态会分叉。
            for (var i = 0; i < tabButtons.Count; i++)
            {
                var active = i == activeIndex;
                var image = tabButtons[i].GetComponent<Image>();
                if (image != null)
                {
                    image.color = active ? MainUiTheme.SelectedBlue : Color.white;
                }

                if (i < tabLabels.Count && tabLabels[i] != null)
                {
                    tabLabels[i].color = active ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("334155");
                    MainUiTheme.ApplyTextRole(
                        tabLabels[i],
                        active ? MainUiTheme.UiTextRole.NavTextSelected : MainUiTheme.UiTextRole.NavText);
                }
            }
        }

        private static PageId ToPageId(int index)
        {
            switch (index)
            {
                case 0:
                    return PageId.Simulation;
                case 1:
                    return PageId.Blueprint;
                case 2:
                    return PageId.Square;
                case 3:
                    return PageId.Encyclopedia;
                case 4:
                    return PageId.Tools;
                case 5:
                    return PageId.Profile;
                default:
                    return PageId.Simulation;
            }
        }
    }

    /// <summary>
    /// 全局 UI 视觉主题规范 (V1.6 升级版)
    /// </summary>
    public static class UiThemeTokens
    {
        public static readonly Color Background = ParseColor("#F7F9FC", new Color(0.97f, 0.98f, 1f));
        public static readonly Color CardBackground = Color.white;
        
        public static readonly Color PrimaryBlue = ParseColor("#3B82F6", new Color(0.23f, 0.51f, 0.96f));
        public static readonly Color PrimaryHover = ParseColor("#2563EB", new Color(0.15f, 0.39f, 0.92f));
        public static readonly Color PrimaryLight = ParseColor("#EFF6FF", new Color(0.94f, 0.96f, 1f));
        
        public static readonly Color TextDark = ParseColor("#1E293B", new Color(0.12f, 0.16f, 0.23f));
        public static readonly Color TextMuted = ParseColor("#64748B", new Color(0.39f, 0.45f, 0.55f));
        
        public static readonly Color BorderColor = ParseColor("#E2E8F0", new Color(0.89f, 0.91f, 0.94f));
        public static readonly Color ErrorColor = ParseColor("#EF4444", new Color(0.94f, 0.27f, 0.27f));

        private static Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// 获取或生成带有边缘抗锯齿的圆角 Sprite，带缓存机制以防内存泄漏
        /// </summary>
        public static Sprite GetRoundedSprite(int radius = 12, int size = 64)
        {
            string key = $"{radius}_{size}";
            if (spriteCache.TryGetValue(key, out var cachedSprite) && cachedSprite != null)
            {
                return cachedSprite;
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - size / 2f) - (size / 2f - radius));
                    float dy = Mathf.Max(0, Mathf.Abs(y - size / 2f) - (size / 2f - radius));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(radius - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            int border = radius + 2;
            Sprite sprite = Sprite.Create(
                tex, 
                new Rect(0, 0, size, size), 
                new Vector2(0.5f, 0.5f), 
                100, 
                0, 
                SpriteMeshType.FullRect, 
                new Vector4(border, border, border, border)
            );

            spriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取标准圆角按钮的 Sprite
        /// </summary>
        public static Sprite GetButtonSprite()
        {
            return GetRoundedSprite(8, 64);
        }
    }
}
