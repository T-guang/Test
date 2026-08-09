using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 左侧元件池的显示控制器：从已加载的 ComponentDefinition 与现有场景项补齐卡片，提供搜索、分类过滤、
    /// 收起状态以及元件池相关的 ActionLog 布局对齐。它不创建 CircuitComponent、不决定元件定义或接线规则，
    /// 拖入画布后的真实生成仍由 Workspace 负责。
    /// 图标优先从 ComponentVisualRuntimeCatalog 获取，使 Editor 与 Windows Player 使用同一运行时资源链；
    /// Catalog 缺项时仅在 Editor 保留原有 AssetDatabase 兼容回退，Player 才使用占位图。
    /// 卡片可由 Awake 动态补建，ActionLog 在 Start 对齐；后续重构前需保留过滤监听器和 actionLogLayoutApplied 的一次性布局边界。
    /// </summary>
    public sealed class PaletteController : MonoBehaviour
    {
        // Palette 是创建入口而不是元件数据权威：卡片展示可创建的 ComponentDefinition，并把用户的点击/拖拽意图
        // 交给 Workspace。卡片、筛选、图标和布局坐标都不等于 CircuitComponent 实例、terminal identity 或电气连接。
        // 静态 Definition、runtime visual catalog 与 prefab registry 各有职责；本类只选择展示资源和创建入口，不能在
        // 图标 fallback、搜索或卡片补建路径中篡改元件规格、保存数据或仿真状态。
        [SerializeField] private InputField searchInput;
        [SerializeField] private SaveLoadService saveLoadService;
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button allFilterButton;
        [SerializeField] private Button householdFilterButton;
        [SerializeField] private Button industrialFilterButton;
        [SerializeField] private List<RectTransform> sectionTitles = new List<RectTransform>();
        [SerializeField] private List<RectTransform> itemRects = new List<RectTransform>();
        [SerializeField] private List<string> itemNames = new List<string>();
        [SerializeField] private List<int> itemCategories = new List<int>();

        private enum PaletteFilter
        {
            All,
            Household,
            Industrial
        }

        private const float PaletteWidth = 380f;
        private const float CollapsedPaletteWidth = 0f;
        private const float PalettePadding = 16f;
        private const float PaletteVerticalOffset = -60f;
        private const float CollapseHandleSize = 36f;
        private const float CardWidth = 96f;
        private const float CardHeight = 132f;
        private const float CardGapX = 10f;
        private const float CardGapY = 12f;
        private const float ContentLeft = 14f;
        private const float SectionTitleHeight = 30f;
        private const float SectionGap = 18f;
        private const float OperationLogHeight = 390f;
        private const float OperationLogMargin = 16f;
        private const float OperationLogWidth = PaletteWidth - PalettePadding * 2f;

        private readonly ComponentCategory[] categoryOrder =
        {
            ComponentCategory.Household,
            ComponentCategory.Industrial,
            ComponentCategory.Measurement
        };

        private readonly Dictionary<ComponentCategory, string> sectionDisplayNames = new Dictionary<ComponentCategory, string>
        {
            { ComponentCategory.Household, "家庭电路组件" },
            { ComponentCategory.Industrial, "工业电路组件" },
            { ComponentCategory.Measurement, "测量工具" }
        };

        private PaletteFilter currentFilter = PaletteFilter.All;
        private Sprite fallbackIcon;
        private Sprite collapseHandleSprite;
        private Button collapseHandleButton;
        private Image collapseHandleIcon;
        private Text collapseHandleLabel;
        private bool isLeftPanelCollapsed;
        private bool isCollapseHandleVisible = true;


        private void Awake()
        {
            // 初始化先建立 Palette 壳和筛选监听，再补齐 catalog 卡片，使运行时新卡片立即服从同一搜索/分类状态。
            // listener 只绑定一次；重复补建不得使同一筛选按钮触发多次 ApplyFilter。
            // 过滤监听器在补齐卡片前建立，保证运行时新增卡片进入同一搜索与分类状态。
            EnsureCardPaletteShell();
            searchInput?.onValueChanged.AddListener(_ => ApplyFilter());
            allFilterButton?.onClick.AddListener(() => SetFilter(PaletteFilter.All));
            householdFilterButton?.onClick.AddListener(() => SetFilter(PaletteFilter.Household));
            industrialFilterButton?.onClick.AddListener(() => SetFilter(PaletteFilter.Industrial));
            AddMissingCatalogItems();
            UpgradeExistingItems();
            ApplyFilter();
        }

        private void Start()
        {
            // ActionLog 和 Workspace 可能由其他场景控制器稍后准备，因此首帧后只做几何对齐。对齐不会移动元件、
            // 改变 Workspace view transform 或重置用户已选中的 Palette item。
            // Workspace 与 ActionLog 可能由其他场景控制器稍后准备，因此布局对齐放在 Start。
            if (workspace == null)
            {
                workspace = FindObjectOfType<WorkspaceController>();
            }

            EnsureActionLogLayout();
            AlignActionLogToPalette();
            EnsureViewportPosition();

        }

        private void EnsureCardPaletteShell()
        {
            // Shell 建立固定标题、Viewport、Content 和折叠手柄层级。标题与手柄留在外层，只有 cards/content 可滚动；
            // 不要把整个 PaletteRoot 放进 ScrollRect，否则折叠和筛选控件会随内容移动或重复创建。
            var root = transform as RectTransform;
            if (root == null)
            {
                return;
            }

            root.sizeDelta = new Vector2(PaletteWidth, root.sizeDelta.y);
            if (root.GetComponent<RectMask2D>() == null)
            {
                root.gameObject.AddComponent<RectMask2D>();
            }

            var rootImage = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            rootImage.color = MainUiTheme.PanelBackground;
            rootImage.raycastTarget = true;

            var rootOutline = root.GetComponent<Outline>() ?? root.gameObject.AddComponent<Outline>();
            rootOutline.effectColor = MainUiTheme.Divider;
            rootOutline.effectDistance = new Vector2(1f, 0f);

            EnsureCollapseHandle(root);

            var title = transform.Find("PaletteTitle") as RectTransform;
            if (title == null)
            {
                var titleGo = new GameObject("PaletteTitle", typeof(RectTransform));
                titleGo.transform.SetParent(root, false);
                title = titleGo.GetComponent<RectTransform>();

                var textGo = new GameObject("TitleText", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(title, false);
                var textRt = textGo.GetComponent<RectTransform>();
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = new Vector2(18f, 0f);
                textRt.offsetMax = Vector2.zero;
            }

            title.anchorMin = new Vector2(0f, 1f);
            title.anchorMax = new Vector2(1f, 1f);
            title.pivot = new Vector2(0.5f, 1f);
            title.anchoredPosition = new Vector2(12f, -16f);
            title.sizeDelta = new Vector2(-PalettePadding * 2f - 12f, 38f);
            title.gameObject.SetActive(true);
            
            var titleText = title.GetComponent<Text>();
            if (titleText == null)
            {
                var titleTextGo = title.Find("TitleText");
                if (titleTextGo != null)
                {
                    titleText = titleTextGo.GetComponent<Text>();
                }
            }

            if (titleText != null)
            {
                titleText.text = "电工控件池";
                MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.SectionTitle);
                titleText.color = MainUiTheme.Hex("111827");
            }

            EnsureTitleAccent(title);

            EnsureFilterButtons(root);
            HideLegacySearchBox();
            EnsureViewportPosition();
            EnsureActionLogLayout();
            EnsureSectionTitleObjects();
            ApplyLeftPanelLayout(PaletteWidth);
        }

        private void EnsureCollapseHandle(RectTransform root)
        {
            // 手柄在外层按名称复用，防止 UI 重建后存在多个点击目标。折叠仅改变可视宽度，不清空 catalog 或 Workspace。
            var parent = root.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            var handle = parent.Find("LeftPaletteCollapseHandle") as RectTransform;
            if (handle == null)
            {
                handle = CreateRect("LeftPaletteCollapseHandle", parent);
                handle.gameObject.AddComponent<Image>();
                handle.gameObject.AddComponent<Button>();

                var labelRect = CreateRect("Arrow", handle);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                var label = labelRect.gameObject.AddComponent<Text>();
                MainUiTheme.ApplyText(label, 21, FontStyle.Bold, MainUiTheme.MutedText, TextAnchor.MiddleCenter, false);
                label.raycastTarget = false;
            }

            handle.SetAsLastSibling();
            handle.anchorMin = new Vector2(0f, 0.5f);
            handle.anchorMax = new Vector2(0f, 0.5f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.sizeDelta = new Vector2(CollapseHandleSize, CollapseHandleSize);

            var image = handle.GetComponent<Image>() ?? handle.gameObject.AddComponent<Image>();
            image.sprite = GetCollapseHandleSprite();
            image.type = Image.Type.Simple;
            image.color = Color.white;
            image.raycastTarget = true;

            var outline = handle.GetComponent<Outline>() ?? handle.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = handle.GetComponent<Shadow>() ?? handle.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.14f);
            shadow.effectDistance = new Vector2(0f, -2f);

            collapseHandleButton = handle.GetComponent<Button>() ?? handle.gameObject.AddComponent<Button>();
            collapseHandleButton.onClick.RemoveListener(ToggleLeftPanelCollapsed);
            collapseHandleButton.onClick.AddListener(ToggleLeftPanelCollapsed);
            var colors = collapseHandleButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.97f, 0.98f, 1f, 1f);
            colors.pressedColor = new Color(0.93f, 0.96f, 1f, 1f);
            colors.selectedColor = Color.white;
            collapseHandleButton.colors = colors;
            collapseHandleButton.gameObject.SetActive(isCollapseHandleVisible);

            collapseHandleLabel = handle.GetComponentInChildren<Text>(true);
            if (collapseHandleLabel != null)
            {
                collapseHandleLabel.color = MainUiTheme.MutedText;
                collapseHandleLabel.gameObject.SetActive(false);
            }

            collapseHandleIcon = UiIconLibrary.EnsureCenteredIcon(handle, "ui_sidebar_collapse_left_32", new Vector2(24f, 24f), MainUiTheme.MutedText);
        }

        private static void EnsureTitleAccent(RectTransform title)
        {
            var accent = title.Find("Accent") as RectTransform;
            if (accent == null)
            {
                accent = CreateRect("Accent", title);
                accent.gameObject.AddComponent<Image>();
            }

            accent.anchorMin = new Vector2(0f, 0.5f);
            accent.anchorMax = new Vector2(0f, 0.5f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = new Vector2(-12f, 0f);
            accent.sizeDelta = new Vector2(4f, 18f);

            var image = accent.GetComponent<Image>() ?? accent.gameObject.AddComponent<Image>();
            image.color = MainUiTheme.PrimaryBlue;
            image.raycastTarget = false;
            accent.SetAsFirstSibling();
        }

        private void ToggleLeftPanelCollapsed()
        {
            isLeftPanelCollapsed = !isLeftPanelCollapsed;
            ApplyLeftPanelLayout(isLeftPanelCollapsed ? CollapsedPaletteWidth : PaletteWidth);
        }

        /// <summary>
        /// Controls visibility of the control-circuit palette handle without changing its collapsed state or layout.
        /// </summary>
        public void SetCollapseHandleVisible(bool visible)
        {
            isCollapseHandleVisible = visible;
            if (collapseHandleButton != null)
            {
                collapseHandleButton.gameObject.SetActive(visible);
            }
        }

        private void ApplyLeftPanelLayout(float width)
        {
            // 左侧面板宽度与 Workspace/ActionLog 可用区域同步，但仅是表现层预留。不能把 width 或 collapsed 状态作为
            // 元件可放置、接线合法或仿真可运行的业务条件。
            var root = transform as RectTransform;
            if (root != null)
            {
                root.sizeDelta = new Vector2(width, root.sizeDelta.y);
            }

            SetPaletteContentVisible(!isLeftPanelCollapsed);
            AlignWorkspaceToPalette(width);
            AlignActionLogToPalette();
            AlignCollapseHandle(width);
        }

        private void SetPaletteContentVisible(bool visible)
        {
            var title = transform.Find("PaletteTitle");
            if (title != null)
            {
                title.gameObject.SetActive(visible);
            }

            var filterRow = transform.Find("PaletteFilterRow");
            if (filterRow != null)
            {
                filterRow.gameObject.SetActive(visible);
            }

            var viewport = transform.Find("PaletteViewport");
            if (viewport != null)
            {
                viewport.gameObject.SetActive(visible);
            }

            var parent = transform.parent;
            var logPanel = parent != null ? parent.Find("ActionLogPanel") : null;
            if (logPanel != null)
            {
                logPanel.gameObject.SetActive(visible);
            }
        }

        private void AlignWorkspaceToPalette(float width)
        {
            // 元件池折叠只改变 Workspace 的可视边界；组件实例位置、端子坐标和 Wire endpoint 都保持在各自的画布坐标系中。
            var parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            var workspace = parent.Find("Workspace") as RectTransform;
            if (workspace == null)
            {
                return;
            }

            workspace.offsetMin = new Vector2(width, workspace.offsetMin.y);
        }

        private void AlignActionLogToPalette()
        {
            // 操作记录跟随元件池宽度是界面布局约束，不能据此推断 Workspace 当前是否锁定、是否运行或是否存在选中对象。
            var parent = transform.parent;
            var logPanel = parent != null ? parent.Find("ActionLogPanel") as RectTransform : null;
            if (logPanel == null)
            {
                return;
            }

            logPanel.anchoredPosition = new Vector2(0f, 0f);
            logPanel.sizeDelta = new Vector2(PaletteWidth, OperationLogHeight);
        }

        private void AlignCollapseHandle(float width)
        {
            if (collapseHandleButton == null)
            {
                return;
            }

            var handle = collapseHandleButton.transform as RectTransform;
            if (handle != null)
            {
                handle.SetAsLastSibling();
                var x = isLeftPanelCollapsed ? CollapseHandleSize * 0.5f : width;
                handle.anchoredPosition = new Vector2(x, PaletteVerticalOffset);
            }

            if (collapseHandleLabel != null)
            {
                collapseHandleLabel.text = isLeftPanelCollapsed ? ">" : "<";
            }

            if (collapseHandleIcon != null)
            {
                collapseHandleIcon.sprite = UiIconLibrary.Load(isLeftPanelCollapsed ? "ui_sidebar_expand_right_32" : "ui_sidebar_collapse_left_32");
            }
        }

        private void EnsureFilterButtons(RectTransform root)
        {
            // 筛选按钮是 presentation 控件，按固定名称复用。按钮是否高亮不能替代 currentFilter，也不能成为 catalog
            // 是否包含某个 Definition 的判断依据。
            var row = transform.Find("PaletteFilterRow") as RectTransform;
            if (row == null)
            {
                row = CreateRect("PaletteFilterRow", root);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.anchoredPosition = new Vector2(0f, -58f);
                row.sizeDelta = new Vector2(-PalettePadding * 2f, 34f);

                var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 8f;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;
                layout.childAlignment = TextAnchor.MiddleLeft;
            }

            allFilterButton = allFilterButton != null ? allFilterButton : EnsureFilterButton(row, "Filter_All", "全部", 72f);
            householdFilterButton = householdFilterButton != null ? householdFilterButton : EnsureFilterButton(row, "Filter_Household", "家庭电路组件", 136f);
            industrialFilterButton = industrialFilterButton != null ? industrialFilterButton : EnsureFilterButton(row, "Filter_Industrial", "工业电路组件", 136f);
        }

        private Button EnsureFilterButton(RectTransform row, string name, string label, float width)
        {
            var rect = row.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = CreateRect(name, row);
                rect.gameObject.AddComponent<Image>();
                rect.gameObject.AddComponent<Button>();

                var textRect = CreateRect("Text", rect);
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(8f, 2f);
                textRect.offsetMax = new Vector2(-8f, -2f);

                var text = textRect.gameObject.AddComponent<Text>();
                MainUiTheme.ApplyText(text, 12, FontStyle.Normal, MainUiTheme.SecondaryText, TextAnchor.MiddleCenter, false);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.raycastTarget = false;
            }

            rect.sizeDelta = new Vector2(width, 32f);

            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(16, 32);
            image.type = Image.Type.Sliced;
            image.color = MainUiTheme.FilterButton;
            var outline = rect.GetComponent<Outline>() ?? rect.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);

            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            ApplyFilterIcon(rect, name);
            var labelText = rect.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                labelText.text = label;
                MainUiTheme.ApplyTextRole(labelText, MainUiTheme.UiTextRole.FilterText);
                labelText.color = MainUiTheme.SecondaryText;
                labelText.rectTransform.offsetMin = new Vector2(24f, 2f);
                labelText.rectTransform.offsetMax = new Vector2(-8f, -2f);
            }

            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 32f;
            return button;
        }

        private void ApplyFilterIcon(RectTransform rect, string buttonName)
        {
            string iconPath = null;
            if (buttonName.Contains("All"))
            {
                iconPath = "ui_sidebar_filter_all_20";
            }
            else if (buttonName.Contains("Household"))
            {
                iconPath = "ui_sidebar_filter_home_20";
            }
            else if (buttonName.Contains("Industrial"))
            {
                iconPath = "ui_sidebar_filter_industry_20_";
            }

            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return;
            }

            var sprite = UiIconLibrary.Load(iconPath);
            if (sprite == null)
            {
                return;
            }

            var iconRect = rect.Find("Icon") as RectTransform;
            if (iconRect == null)
            {
                iconRect = CreateRect("Icon", rect);
                iconRect.gameObject.AddComponent<Image>();
            }

            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.anchoredPosition = new Vector2(15f, 0f);
            iconRect.sizeDelta = new Vector2(18f, 18f);
            var image = iconRect.GetComponent<Image>() ?? iconRect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = Color.white;
        }

        private void HideLegacySearchBox()
        {
            // 历史搜索控件仅为兼容场景层级而隐藏；新的筛选路径必须集中使用当前输入框，避免两个输入同时修改可见性。
            if (searchInput == null)
            {
                searchInput = GetComponentInChildren<InputField>(true);
            }

            if (searchInput == null)
            {
                return;
            }

            searchInput.text = string.Empty;
            searchInput.gameObject.SetActive(false);
        }

        private void EnsureViewportPosition()
        {
            // Viewport 几何负责可达性和裁剪。卡片的 anchoredPosition 只描述 UI 布局，绝不能参与元件 definition、
            // terminal anchor 或 Wire topology 的计算。
            var viewport = transform.Find("PaletteViewport") as RectTransform;
            if (viewport == null)
            {
                return;
            }

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(PalettePadding, OperationLogHeight + 12f);
            viewport.offsetMax = new Vector2(-PalettePadding, -104f);

            var mask = viewport.GetComponent<RectMask2D>() ?? viewport.gameObject.AddComponent<RectMask2D>();

            var viewportImage = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
            viewportImage.color = MainUiTheme.PanelBackground;
            viewportImage.raycastTarget = true;
        }

        private void EnsureActionLogLayout()
        {
            // ActionLog 不是元件池数据的一部分；这里只调整其与左侧面板共存时的宿主布局。
            if (workspace == null)
            {
                workspace = FindObjectOfType<WorkspaceController>();
            }

            RectTransform logPanel = null;

            if (workspace != null)
            {
                var field = typeof(WorkspaceController).GetField("actionLogScrollRect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var scrollRect = field?.GetValue(workspace) as ScrollRect;
                logPanel = scrollRect != null ? scrollRect.transform as RectTransform : null;
            }

            if (logPanel == null)
            {
                var parent = transform.parent;
                logPanel = parent != null ? parent.Find("ActionLogPanel") as RectTransform : null;
            }

            if (logPanel == null)
            {
                return;
            }

            // --- Panel position & size ---
            logPanel.anchorMin = new Vector2(0f, 0f);
            logPanel.anchorMax = new Vector2(0f, 0f);
            logPanel.pivot = new Vector2(0f, 0f);
            logPanel.anchoredPosition = new Vector2(0f, 0f);
            logPanel.sizeDelta = new Vector2(PaletteWidth, OperationLogHeight);
            logPanel.SetAsLastSibling();

            // --- Panel background ---
            var panelImage = logPanel.GetComponent<Image>() ?? logPanel.gameObject.AddComponent<Image>();
            panelImage.enabled = true;
            panelImage.sprite = null;
            panelImage.color = Color.white;
            panelImage.raycastTarget = true;

            // --- Panel border ---
            var outline = logPanel.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }

            // --- Panel shadow ---
            var shadow = logPanel.GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.enabled = false;
            }

            // --- PanelTopBorder (1px top divider) ---
            var topBorder = logPanel.Find("PanelTopBorder") as RectTransform;
            if (topBorder == null)
            {
                var borderGo = new GameObject("PanelTopBorder", typeof(RectTransform), typeof(Image));
                borderGo.transform.SetParent(logPanel, false);
                topBorder = borderGo.GetComponent<RectTransform>();
            }
            topBorder.anchorMin = new Vector2(0f, 1f);
            topBorder.anchorMax = new Vector2(1f, 1f);
            topBorder.pivot = new Vector2(0.5f, 1f);
            topBorder.anchoredPosition = new Vector2(0f, 0f);
            topBorder.sizeDelta = new Vector2(0f, 1f);
            {
                var borderImage = topBorder.GetComponent<Image>();
                borderImage.color = MainUiTheme.Hex("E2E8F0");
                borderImage.raycastTarget = false;
            }

            // --- ActionLogHeader (title bar background) ---
            var header = logPanel.Find("ActionLogHeader") as RectTransform;
            if (header == null)
            {
                header = logPanel.Find("TitleBarBackground") as RectTransform;
                if (header != null)
                {
                    header.gameObject.name = "ActionLogHeader";
                }
            }
            if (header == null)
            {
                var headerGo = new GameObject("ActionLogHeader", typeof(RectTransform), typeof(Image));
                headerGo.transform.SetParent(logPanel, false);
                header = headerGo.GetComponent<RectTransform>();
            }
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.anchoredPosition = new Vector2(0f, -1f);
            header.sizeDelta = new Vector2(0f, 36f);
            {
                var headerImg = header.GetComponent<Image>();
                headerImg.sprite = null;
                headerImg.color = MainUiTheme.Hex("F8FAFC");
                headerImg.raycastTarget = false;
            }

            // --- HeaderBottomLine (1px divider) ---
            var bottomLine = logPanel.Find("HeaderBottomLine") as RectTransform;
            if (bottomLine == null)
            {
                bottomLine = logPanel.Find("TopBorder") as RectTransform;
                if (bottomLine != null)
                {
                    bottomLine.gameObject.name = "HeaderBottomLine";
                }
            }
            if (bottomLine == null)
            {
                var lineGo = new GameObject("HeaderBottomLine", typeof(RectTransform), typeof(Image));
                lineGo.transform.SetParent(logPanel, false);
                bottomLine = lineGo.GetComponent<RectTransform>();
            }
            bottomLine.anchorMin = new Vector2(0f, 1f);
            bottomLine.anchorMax = new Vector2(1f, 1f);
            bottomLine.pivot = new Vector2(0.5f, 1f);
            bottomLine.anchoredPosition = new Vector2(0f, -37f);
            bottomLine.sizeDelta = new Vector2(0f, 1f);
            {
                var lineImage = bottomLine.GetComponent<Image>() ?? bottomLine.gameObject.AddComponent<Image>();
                lineImage.color = MainUiTheme.Hex("E2E8F0");
                lineImage.raycastTarget = false;
            }

            // --- ActionLogTitle ---
            var title = logPanel.Find("ActionLogTitle") as RectTransform;
            if (title != null)
            {
                title.anchorMin = new Vector2(0f, 1f);
                title.anchorMax = new Vector2(1f, 1f);
                title.pivot = new Vector2(0.5f, 1f);
                var titleText = title.GetComponent<Text>();
                if (titleText != null)
                {
                    MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.LogTitle);
                    titleText.color = MainUiTheme.Hex("111827");
                    titleText.text = "操作记录";
                    titleText.rectTransform.offsetMin = new Vector2(20f, -37f);
                    titleText.rectTransform.offsetMax = new Vector2(-52f, -1f);
                }
            }

            // --- ClearButton (trash icon) ---
            var clearButtonRect = logPanel.Find("ClearButton") as RectTransform;
            if (clearButtonRect == null && title != null)
            {
                clearButtonRect = title.Find("ClearButton") as RectTransform;
                if (clearButtonRect != null)
                {
                    clearButtonRect.SetParent(logPanel, false);
                }
            }
            if (clearButtonRect == null)
            {
                var clearGo = new GameObject("ClearButton", typeof(RectTransform), typeof(Image), typeof(Button));
                clearGo.transform.SetParent(logPanel, false);
                clearButtonRect = clearGo.GetComponent<RectTransform>();
            }
            clearButtonRect.anchorMin = new Vector2(1f, 1f);
            clearButtonRect.anchorMax = new Vector2(1f, 1f);
            clearButtonRect.pivot = new Vector2(1f, 1f);
            clearButtonRect.anchoredPosition = new Vector2(-20f, -5f);
            clearButtonRect.sizeDelta = new Vector2(28f, 28f);
            {
                var clearImage = clearButtonRect.GetComponent<Image>();
                clearImage.sprite = UiThemeTokens.GetRoundedSprite(6, 32);
                clearImage.type = Image.Type.Sliced;
                clearImage.color = MainUiTheme.Hex("F8FAFC");
                clearImage.raycastTarget = true;

                var clearButton = clearButtonRect.GetComponent<Button>();
                clearButton.onClick.RemoveAllListeners();

                if (workspace == null)
                {
                    workspace = FindObjectOfType<WorkspaceController>();
                }

                if (workspace != null)
                {
                    clearButton.onClick.AddListener(() => workspace.ClearActionLog());
                }

                UiIconLibrary.EnsureCenteredIcon(clearButtonRect, "ui_toolbar_clear_all_24", new Vector2(18f, 18f), MainUiTheme.Hex("64748B"));
            }

            // --- Hide old CurrentStatus ---
            var status = logPanel.Find("CurrentStatus") as RectTransform;
            if (status != null)
            {
                status.gameObject.SetActive(false);
            }

            // --- ActionLogViewport ---
            var viewport = logPanel.Find("ActionLogViewport") as RectTransform;
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.pivot = new Vector2(0.5f, 0.5f);
                viewport.offsetMin = new Vector2(20f, 12f);
                viewport.offsetMax = new Vector2(-20f, -48f);

                var mask = viewport.GetComponent<RectMask2D>() ?? viewport.gameObject.AddComponent<RectMask2D>();

                var viewportImage = viewport.GetComponent<Image>() ?? viewport.gameObject.AddComponent<Image>();
                viewportImage.enabled = true;
                viewportImage.color = Color.white;
                viewportImage.raycastTarget = true;

                var actionLogText = viewport.Find("ActionLogText") as RectTransform;
                if (actionLogText != null)
                {
                    var text = actionLogText.GetComponent<Text>();
                    if (text != null)
                    {
                        MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.LogBody);
                        text.color = MainUiTheme.Hex("475569");
                        text.supportRichText = true;
                    }
                }
            }

            // --- Enforce hierarchy order ---
            header.SetAsFirstSibling();
            if (topBorder != null)
            {
                topBorder.SetAsLastSibling();
            }
            if (viewport != null)
            {
                viewport.SetAsLastSibling();
            }
            bottomLine.SetAsLastSibling();
            if (title != null)
            {
                title.SetAsLastSibling();
            }
            if (clearButtonRect != null)
            {
                clearButtonRect.SetAsLastSibling();
            }

            Debug.Log("[PaletteController] ActionLog layout applied.");
        }

        private void EnsureSectionTitleObjects()
        {
            // 分区标题与 itemRects 是同一内容树的展示索引。补建时保持 categoryOrder 的稳定顺序，不能根据当前搜索
            // 结果重新定义分类或改变 catalog 本身的排列。
            if (content == null)
            {
                return;
            }

            while (sectionTitles.Count < categoryOrder.Length)
            {
                var category = categoryOrder[sectionTitles.Count];
                var titleRect = CreateRect("Section_" + category, content);
                titleRect.anchorMin = new Vector2(0f, 1f);
                titleRect.anchorMax = new Vector2(0f, 1f);
                titleRect.pivot = new Vector2(0f, 1f);
                titleRect.sizeDelta = new Vector2(OperationLogWidth, 34f);

                var title = titleRect.gameObject.AddComponent<Text>();
                MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.SubsectionTitle);
                title.color = MainUiTheme.DeepText;
                title.raycastTarget = false;
                title.text = sectionDisplayNames.TryGetValue(category, out var displayName) ? displayName : category.ToString();

                sectionTitles.Add(titleRect);
            }
        }

        private void AddMissingCatalogItems()
        {
            // catalog 补齐只创建缺失卡片，不删除运行时已有的合法卡片。Definition 仍来自 SaveLoadService 的目录，
            // 因此不能通过卡片名称猜测 kind 或手写一份平行 catalog。
            // 仅为当前 Catalog 中可见且场景未预置的定义创建展示卡，不修改 ComponentDefinition 或 Workspace。
            if (content == null)
            {
                return;
            }

            if (saveLoadService == null)
            {
                saveLoadService = FindObjectOfType<SaveLoadService>();
            }

            if (workspace == null)
            {
                workspace = FindObjectOfType<WorkspaceController>();
            }

            var catalog = saveLoadService != null ? saveLoadService.Catalog : null;
            if (catalog == null || workspace == null)
            {
                return;
            }

            foreach (var definition in catalog)
            {
                if (definition == null || !definition.showInPalette || HasPaletteItem(definition))
                {
                    continue;
                }

                CreateRuntimePaletteItem(definition);
            }
        }

        private bool HasPaletteItem(ComponentDefinition definition)
        {
            for (var i = 0; i < itemRects.Count; i++)
            {
                var rect = itemRects[i];
                if (rect == null)
                {
                    continue;
                }

                var item = rect.GetComponent<PaletteItem>();
                if (item != null && item.Definition == definition)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateRuntimePaletteItem(ComponentDefinition definition)
        {
            // 创建的 item 是 Definition 的一次展示投影；点击/拖放应由 item 的正式 Workspace 入口创建新实例，
            // 不得复用卡片对象作为 Canvas 上的 CircuitComponent。
            // 动态卡片只承载展示和交互入口；元件定义由 SaveLoadService.Catalog 提供，
            // 实际拖入画布和元件生成继续由 PaletteItem 与 Workspace 的既有流程负责。
            var itemObject = new GameObject("Palette_" + definition.name, typeof(RectTransform), typeof(Image), typeof(PaletteItem));
            itemObject.transform.SetParent(content, false);

            var rect = itemObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var item = itemObject.GetComponent<PaletteItem>();
            item.Initialize(definition, workspace);
            ConfigureCard(rect, definition);

            itemRects.Add(rect);
            itemNames.Add(definition.displayName);
            itemCategories.Add((int)definition.category);
        }

        private void UpgradeExistingItems()
        {
            // 兼容升级只补齐旧场景卡片的视觉/交互外壳，保留其已有引用和排序。不要在此处重写定义、Wire 或选择状态。
            for (var i = 0; i < itemRects.Count; i++)
            {
                var rect = itemRects[i];
                if (rect == null)
                {
                    continue;
                }

                var item = rect.GetComponent<PaletteItem>();
                var definition = item != null ? item.Definition : null;
                if (definition != null)
                {
                    ConfigureCard(rect, definition);
                }
            }
        }

        private void ConfigureCard(RectTransform rect, ComponentDefinition definition)
        {
            // Card 配置可更新标题、图标、尺寸和拖拽入口；definition 的 stable asset name、terminalId 与参数不因显示名
            // 或图标 fallback 而变化。资源缺失时只降级视觉，不应阻断已支持元件的创建。
            if (rect == null || definition == null)
            {
                return;
            }

            rect.sizeDelta = new Vector2(CardWidth, CardHeight);

            var background = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            background.sprite = UiThemeTokens.GetRoundedSprite(8, 32);
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var iconImage = EnsureChildImage(rect, "Icon");
            var iconRect = iconImage.rectTransform;
            iconRect.anchorMin = new Vector2(0.5f, 1f);
            iconRect.anchorMax = new Vector2(0.5f, 1f);
            iconRect.pivot = new Vector2(0.5f, 1f);
            iconRect.anchoredPosition = new Vector2(0f, -10f);
            iconRect.sizeDelta = GetPaletteIconSize(definition);
            var paletteSprite = ResolvePaletteIcon(definition);
            iconImage.sprite = paletteSprite != null ? paletteSprite : GetFallbackIcon();
            iconImage.color = paletteSprite != null ? Color.white : GetCategoryIconColor(definition.category);
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            var label = EnsureChildText(rect, "Label");
            var displayName = GetPaletteDisplayName(definition);
            label.text = displayName;
            label.lineSpacing = 1.02f;
            MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.PaletteCardTitle);
            // 允许长名称换行到第二行，超出两行时截断，防止文本溢出到相邻卡片
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = MainUiTheme.DeepText;
            label.raycastTarget = false;

            // 对两行长名称启用有限自动字号（minSize=11, maxSize=15），短名称保持固定字号 15
            var isMultiLineName = displayName != null && displayName.IndexOf('\n') >= 0;
            if (isMultiLineName)
            {
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 11;
                label.resizeTextMaxSize = 15;
            }
            else
            {
                label.resizeTextForBestFit = false;
            }

            var labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 9f);
            // 宽度 -2 留 1px 左右边距，高度 36 容纳两行 14~15px 文本
            labelRect.sizeDelta = new Vector2(-2f, 36f);

            var outline = rect.GetComponent<Outline>() ?? rect.gameObject.AddComponent<Outline>();
            var normalOutline = MainUiTheme.Hex("EEF2F7");
            var hoverOutline = MainUiTheme.Hex("93C5FD");
            outline.effectColor = normalOutline;
            outline.effectDistance = new Vector2(1f, -1f);

            var paletteItem = rect.GetComponent<PaletteItem>();
            if (paletteItem != null)
            {
                paletteItem.ConfigureCardVisual(
                    background,
                    Color.white,
                    MainUiTheme.Hex("F8FBFF"),
                    outline,
                    normalOutline,
                    hoverOutline);
            }
        }

        private static Vector2 GetPaletteIconSize(ComponentDefinition definition)
        {
            var name = definition != null ? definition.name : string.Empty;

            if (ContainsName(name, "AC_ThreePhase_Power") ||
                ContainsName(name, "TerminalBlock") ||
                ContainsName(name, "Fuse_1P") ||
                ContainsName(name, "Fuse_3P"))
            {
                return new Vector2(76f, 40f);
            }

            // 刀开关 QS 原图为纵向 sprite（宽高比≈0.62），原 76×40 宽扁框导致 preserveAspect 下渲染宽仅 ~25px。
            // 改为 56×80 竖框，受宽度约束，渲染高度 ≈ 56/0.62 ≈ 90px，视觉高度提升约 125%，主体明显增大且不裁切。
            // 此修改仅影响元件池预览，不影响画布 SpawnComponent 实际尺寸（画布尺寸由 ComponentDefinition.size + prefab root sizeDelta 决定）。
            if (ContainsName(name, "KnifeSwitch"))
            {
                return new Vector2(56f, 80f);
            }

            if (ContainsName(name, "AC_220V_Power") ||
                ContainsName(name, "Single_Phase_Meter") ||
                ContainsName(name, "Breaker_1P"))
            {
                return new Vector2(52f, 72f);
            }

            if (ContainsName(name, "Contactor_KM") ||
                ContainsName(name, "ThermalRelay") ||
                ContainsName(name, "Timer_") ||
                ContainsName(name, "TimerRelay") ||
                ContainsName(name, "Motor_") ||
                ContainsName(name, "LimitSwitch") ||
                ContainsName(name, "Breaker_2P") ||
                ContainsName(name, "Breaker_3P") ||
                ContainsName(name, "Breaker_4P"))
            {
                return new Vector2(72f, 72f);
            }

            return new Vector2(64f, 64f);
        }

        private static bool ContainsName(string value, string pattern)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Sprite ResolvePaletteIcon(ComponentDefinition definition)
        {
            // 图标解析优先走 runtime catalog，使 Editor/Player 使用一致资源；Editor 专用的 AssetDatabase 回退不得泄漏
            // 为 Player 依赖。任何 fallback 都是视觉占位，不声明该组件存在 prefab 或仿真支持。
            // Player 必须先走 Runtime Catalog 的序列化 Sprite 引用，不能依赖 Assets 路径或 AssetDatabase。
            if (definition == null)
            {
                return null;
            }

            var catalog = ComponentVisualRuntimeCatalog.Load();
            if (catalog != null && catalog.TryGetDefaultSprite(definition.name, out var sprite))
            {
                return sprite;
            }

#if UNITY_EDITOR
            var specialIcon = LoadSpecialPaletteIcon(definition.name);
            if (specialIcon != null)
            {
                return specialIcon;
            }

            if (VisualPrefabRegistry.TryGetConfig(definition.name, out var config) && config != null)
            {
                sprite = LoadSpriteAtPath(config.DefaultSpritePath);
                if (sprite != null)
                {
                    return sprite;
                }

                sprite = LoadSpriteFromVisualPrefab(config.PrefabPath);
                if (sprite != null)
                {
                    return sprite;
                }
            }
#endif

            return definition.sprite;
        }

#if UNITY_EDITOR
        private static Sprite LoadSpecialPaletteIcon(string definitionName)
        {
            if (string.IsNullOrWhiteSpace(definitionName))
            {
                return null;
            }

            if (definitionName.IndexOf("Contactor_KM", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return LoadSpriteAtPath("Assets/Art/Components/Contactor_KM_380V_Default.png");
            }

            return null;
        }

        private static Sprite LoadSpriteAtPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite LoadSpriteFromVisualPrefab(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return null;
            }

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return null;
            }

            var body = prefab.transform.Find("Body");
            var image = body != null ? body.GetComponent<Image>() : prefab.GetComponentInChildren<Image>(true);
            return image != null ? image.sprite : null;
        }
#endif

        private static Image EnsureChildImage(RectTransform parent, string name)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null)
            {
                child = CreateRect(name, parent);
                child.gameObject.AddComponent<Image>();
            }

            return child.GetComponent<Image>() ?? child.gameObject.AddComponent<Image>();
        }

        private static Text EnsureChildText(RectTransform parent, string name)
        {
            var child = parent.Find(name) as RectTransform;
            if (child == null)
            {
                child = CreateRect(name, parent);
                child.gameObject.AddComponent<Text>();
            }

            var text = child.GetComponent<Text>() ?? child.gameObject.AddComponent<Text>();
            if (text.font == null)
            {
                text.font = MainUiTheme.UiFont;
            }

            return text;
        }

        private Sprite GetFallbackIcon()
        {
            if (fallbackIcon != null)
            {
                return fallbackIcon;
            }

            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var fill = new Color(0.64f, 0.72f, 0.84f, 1f);
            var dark = new Color(0.34f, 0.42f, 0.55f, 1f);
            var center = new Vector2(31.5f, 31.5f);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center);
                    if (distance < 22f)
                    {
                        texture.SetPixel(x, y, fill);
                    }
                    else if (distance < 25f)
                    {
                        texture.SetPixel(x, y, dark);
                    }
                    else
                    {
                        texture.SetPixel(x, y, clear);
                    }
                }
            }

            texture.Apply();
            fallbackIcon = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            return fallbackIcon;
        }

        private Sprite GetCollapseHandleSprite()
        {
            if (collapseHandleSprite != null)
            {
                return collapseHandleSprite;
            }

            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var fill = Color.white;
            var center = new Vector2(31.5f, 31.5f);
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x, y), center);
                    texture.SetPixel(x, y, distance <= 30f ? fill : clear);
                }
            }

            texture.Apply();
            collapseHandleSprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f);
            return collapseHandleSprite;
        }

        private static Color GetCategoryIconColor(ComponentCategory category)
        {
            switch (category)
            {
                case ComponentCategory.Household:
                    return new Color(0.35f, 0.58f, 0.95f, 1f);
                case ComponentCategory.Industrial:
                    return new Color(0.94f, 0.58f, 0.24f, 1f);
                case ComponentCategory.Measurement:
                    return new Color(0.37f, 0.68f, 0.48f, 1f);
                default:
                    return new Color(0.64f, 0.72f, 0.84f, 1f);
            }
        }

        private void ApplyFilter()
        {
            // 搜索与类别筛选只切换卡片/标题可见性，保留完整 catalog 和 item 索引。过滤后不能重排或删除 Definition，
            // 也不能让隐藏卡片失去被模板、保存图纸或已放置实例引用的可能性。
            // 搜索和分类只改变卡片可见性，不重建 Catalog、不改变定义排序，也不影响已在画布上的元件。
            var query = searchInput != null ? searchInput.text.Trim() : string.Empty;
            var y = -14f;
            UpdateFilterButtonState();

            for (var sectionIndex = 0; sectionIndex < categoryOrder.Length; sectionIndex++)
            {
                var categoryEnum = categoryOrder[sectionIndex];
                if (!ShouldShowCategory(categoryEnum))
                {
                    HideCategory(categoryEnum);
                    var hiddenTitle = sectionIndex < sectionTitles.Count ? sectionTitles[sectionIndex] : null;
                    if (hiddenTitle != null)
                    {
                        hiddenTitle.gameObject.SetActive(false);
                    }

                    continue;
                }

                var category = (int)categoryEnum;
                var visibleCount = 0;
                for (var i = 0; i < itemRects.Count; i++)
                {
                    if (i >= itemCategories.Count || itemCategories[i] != category)
                    {
                        continue;
                    }

                    if (Matches(i, query))
                    {
                        visibleCount++;
                    }
                }

                var title = sectionIndex < sectionTitles.Count ? sectionTitles[sectionIndex] : null;
                if (title != null)
                {
                    title.gameObject.SetActive(visibleCount > 0);
                    title.anchoredPosition = new Vector2(18f, y);
                    var titleText = title.GetComponent<Text>();
                    if (titleText != null && sectionDisplayNames.TryGetValue(categoryEnum, out var sectionName))
                    {
                        titleText.text = sectionName;
                        MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.SubsectionTitle);
                        titleText.color = MainUiTheme.DeepText;
                    }
                }

                if (visibleCount == 0)
                {
                    HideCategory(categoryEnum);
                    continue;
                }

                y -= SectionTitleHeight + 12f;
                var visibleIndex = 0;
                for (var i = 0; i < itemRects.Count; i++)
                {
                    if (i >= itemCategories.Count || itemCategories[i] != category)
                    {
                        continue;
                    }

                    var visible = Matches(i, query);
                    itemRects[i].gameObject.SetActive(visible);
                    if (!visible)
                    {
                        continue;
                    }

                    var row = visibleIndex / 3;
                    var col = visibleIndex % 3;
                    itemRects[i].anchoredPosition = new Vector2(ContentLeft + col * (CardWidth + CardGapX), y - row * (CardHeight + CardGapY));
                    visibleIndex++;
                }

                var rows = Mathf.CeilToInt(visibleCount / 3f);
                y -= rows * (CardHeight + CardGapY) + SectionGap;
            }

            for (var i = 0; i < itemRects.Count; i++)
            {
                var categoryKnown = i < itemCategories.Count;
                if (!categoryKnown)
                {
                    itemRects[i].gameObject.SetActive(false);
                }
            }

            if (content != null)
            {
                content.sizeDelta = new Vector2(0f, Mathf.Max(900f, Mathf.Abs(y) + 24f));
                content.anchoredPosition = Vector2.zero;
            }
        }

        private bool ShouldShowCategory(ComponentCategory category)
        {
            switch (currentFilter)
            {
                case PaletteFilter.Household:
                    return category == ComponentCategory.Household;
                case PaletteFilter.Industrial:
                    return category == ComponentCategory.Industrial;
                default:
                    return category == ComponentCategory.Household || category == ComponentCategory.Industrial;
            }
        }

        private void HideCategory(ComponentCategory category)
        {
            var categoryValue = (int)category;
            for (var i = 0; i < itemRects.Count; i++)
            {
                if (i < itemCategories.Count && itemCategories[i] == categoryValue && itemRects[i] != null)
                {
                    itemRects[i].gameObject.SetActive(false);
                }
            }
        }

        private void SetFilter(PaletteFilter filter)
        {
            // currentFilter 是唯一筛选状态来源；按钮视觉在 ApplyFilter 后同步，避免通过 UI active 状态反推筛选语义。
            currentFilter = filter;
            ApplyFilter();
        }

        private void UpdateFilterButtonState()
        {
            SetFilterButtonVisual(allFilterButton, currentFilter == PaletteFilter.All);
            SetFilterButtonVisual(householdFilterButton, currentFilter == PaletteFilter.Household);
            SetFilterButtonVisual(industrialFilterButton, currentFilter == PaletteFilter.Industrial);
        }

        private static void SetFilterButtonVisual(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected ? MainUiTheme.PrimaryBlue : MainUiTheme.FilterButton;
                image.sprite = UiThemeTokens.GetRoundedSprite(8, 32);
                image.type = Image.Type.Sliced;
            }

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? MainUiTheme.PrimaryBlue : MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = selected ? Color.white : MainUiTheme.Hex("F8FBFF");
            colors.pressedColor = selected ? Color.white : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = Color.white;
            button.colors = colors;

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                MainUiTheme.ApplyTextRole(label, selected ? MainUiTheme.UiTextRole.FilterTextSelected : MainUiTheme.UiTextRole.FilterText);
                label.color = selected ? Color.white : MainUiTheme.SecondaryText;
            }

            var iconRect = button.transform.Find("Icon");
            if (iconRect != null)
            {
                var iconImg = iconRect.GetComponent<Image>();
                if (iconImg != null)
                {
                    iconImg.color = selected ? Color.white : MainUiTheme.SecondaryText;
                }
            }
        }

        private bool Matches(int index, string query)
        {
            if (!IsVisibleInPalette(index))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            var name = index >= 0 && index < itemNames.Count ? itemNames[index] : string.Empty;
            return name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsVisibleInPalette(int index)
        {
            if (index < 0 || index >= itemRects.Count || itemRects[index] == null)
            {
                return false;
            }

            var item = itemRects[index].GetComponent<PaletteItem>();
            return item == null || item.Definition == null || item.Definition.showInPalette;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }
        public static string GetPaletteDisplayName(ComponentDefinition definition)
        {
            if (definition == null) return string.Empty;

            var id = definition.name;
            var original = definition.displayName;

            // UI-only 定向换行：仅对以下 7 个真实长名称在原 displayName 基础上插入换行，
            // 使括号内容独占第二行，避免括号单独成行或与主名称挤在同一行。
            // 不修改 ComponentDefinition.displayName 的术语、型号、缩写或含义。
            // 其他元件默认直接返回 definition.displayName。
            if (id == "Button_Compound_SB")
            {
                return "复合按钮SB\n(红)";
            }
            if (id == "Button_Compound_Green_SB")
            {
                return "复合按钮SB\n(绿)";
            }
            if (id == "Button_SelfLock_SB")
            {
                return "自锁开关SB\n(红)";
            }
            if (id == "Button_SelfLock_Green_SB")
            {
                return "自锁开关SB\n(绿)";
            }
            if (id == "LimitSwitch_Compound")
            {
                return "行程开关 SQ\n（限位开关）";
            }
            if (id == "LimitSwitch_SelfLock")
            {
                return "限位开关\n(自锁)";
            }
            if (id == "KnifeSwitch_QS")
            {
                return "刀开关\n(QS)";
            }

            return original;
        }
    }
}
