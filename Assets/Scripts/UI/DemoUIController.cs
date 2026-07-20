using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.Core;
using ElectricalSim.AI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// Demo 主场景工具栏与运行时页面辅助控制器。Awake 先整理主框架、工具栏和必需的图纸/检查面板，
    /// 再绑定现有按钮到 Workspace、保存导入和模拟操作；Start 在首帧后完成右侧操作组布局。
    /// 它不拥有 Workspace、保存数据或检查规则，也不应复制各页面 Controller 的内容生成逻辑。
    /// 当前实现直接依赖 Demo 场景序列化的按钮、Workspace 与对话框引用，并会按兼容需要动态补建局部 UI。
    /// Editor 与 Windows Player 共用主体初始化路径；更新布局等开发入口仅在 Editor 或 Development Build 中显示。
    /// 后续拆分工具栏时需保留 RemoveAllListeners 后再绑定的单次监听器约束。
    /// </summary>
    public sealed class DemoUIController : MonoBehaviour
    {
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private SaveLoadService saveLoadService;
        [SerializeField] private Button startButton;
        [SerializeField] private Button clearWiresButton;
        [SerializeField] private Button clearAllButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button importButton;
        [SerializeField] private SaveBlueprintDialog saveDialog;
        [SerializeField] private ImportBlueprintPanel importPanel;
        [SerializeField] private LocalInspectorPanel localInspectorPanel;
        [SerializeField] private RectTransform localInspectorHostRoot;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;
        [SerializeField] private Button quickDeleteButton;
        [SerializeField] private Button quickClearWiresButton;
        [SerializeField] private Button quickClearAllButton;
        [SerializeField] private Button lockButton;
        [SerializeField] private Button updateLayoutButton;
        [SerializeField] private Dropdown wireStyleDropdown;
        [SerializeField] private List<Button> colorButtons = new List<Button>();

        private readonly List<Outline> colorButtonOutlines = new List<Outline>();
        private Color[] wirePaletteColors;
        private Color lastActiveWireColor;
        private bool lastActiveWireSelectionState;
        private RectTransform fileActionGroup;
        private Coroutine rightActionGroupStartupRoutine;

        private void Awake()
        {
            // 顺序是当前场景兼容契约：先保证容器存在，再绑定会访问这些容器或 Workspace 的操作入口。
            ApplyMainFrameLayout();
            EnsureToolbarLayout();
            EnsureMainLogo();
            EnsureBlueprintPanels();
            EnsureLocalInspectorPanel();
            BindButton(startButton, ToggleSimulation);
            BindButton(clearWiresButton, workspace.ClearWires);
            BindButton(clearAllButton, workspace.ClearDrawing);
            BindButton(saveButton, OpenSaveDialog);
            BindButton(loadButton, OpenImportPanel);
            BindButton(undoButton, workspace.Undo);
            BindButton(redoButton, workspace.Redo);
            BindButton(quickDeleteButton, workspace.DeleteSelection);
            BindButton(lockButton, ToggleLock);

            if (wireStyleDropdown != null)
            {
                wireStyleDropdown.onValueChanged.AddListener(value => workspace.CurrentWireStyle = value == 0 ? WireStyle.Orthogonal : WireStyle.Straight);
            }

            wirePaletteColors = new[]
            {
                MainUiTheme.Hex("EF4444"),
                MainUiTheme.PrimaryBlue,
                MainUiTheme.SuccessGreen,
                MainUiTheme.Hex("FACC15")
            };

            colorButtonOutlines.Clear();
            for (var i = 0; i < colorButtons.Count && i < wirePaletteColors.Length; i++)
            {
                var color = wirePaletteColors[i];
                ConfigureWireColorButton(colorButtons[i], color);
                colorButtons[i].onClick.AddListener(() =>
                {
                    workspace.ApplyWirePaletteColor(color);
                    RefreshWireColorButtons(true);
                });
            }

            RefreshSimulationButtonLabel();
            RefreshLockButtonLabel();
            RefreshWireColorButtons(true);
        }

        private void Start()
        {
            if (rightActionGroupStartupRoutine != null)
            {
                StopCoroutine(rightActionGroupStartupRoutine);
            }

            rightActionGroupStartupRoutine = StartCoroutine(FinalizeRightActionGroupAfterStartup());
        }

        private void LateUpdate()
        {
            RefreshWireColorButtons(false);
        }

        private void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            // 运行时 UI 可能复用场景按钮；先清理旧监听器以避免页面重建后一次点击重复执行。
            if (button == null || action == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void EnsureToolbarLayout()
        {
            // 仅整理已有工具栏及其兼容补件；不要把页面级布局迁移到这里。
            var toolbar = startButton != null ? startButton.transform.parent as RectTransform : null;
            if (toolbar == null)
            {
                toolbar = transform as RectTransform;
            }

            if (toolbar == null)
            {
                return;
            }

            toolbar.sizeDelta = new Vector2(toolbar.sizeDelta.x, MainUiTheme.ToolbarHeight);

            var toolbarImage = toolbar.GetComponent<Image>() ?? toolbar.gameObject.AddComponent<Image>();
            toolbarImage.color = MainUiTheme.PanelBackground;
            toolbarImage.raycastTarget = true;
            var toolbarOutline = toolbar.GetComponent<Outline>() ?? toolbar.gameObject.AddComponent<Outline>();
            toolbarOutline.effectColor = MainUiTheme.Divider;
            toolbarOutline.effectDistance = new Vector2(0f, -1f);

            var quickRoot = FindQuickToolRoot();

            undoButton = EnsureButton(toolbar, undoButton, "UndoButton", "撤销");
            redoButton = EnsureButton(toolbar, redoButton, "RedoButton", "重做");
            quickDeleteButton = EnsureButton(toolbar, quickDeleteButton, "DeleteSelectionButton", "删除");
            lockButton = EnsureButton(toolbar, lockButton, "InteractionLockButton", "锁定");

            var leftGroup = EnsureGroup(toolbar, "LeftActionGroup", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(790f, MainUiTheme.ToolbarHeight), TextAnchor.MiddleLeft, 14f);
            var colorGroup = EnsureGroup(toolbar, "WireColorGroup", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(830f, 0f), new Vector2(260f, MainUiTheme.ToolbarHeight), TextAnchor.MiddleLeft, 14f);
            fileActionGroup = EnsureGroup(toolbar, "FileActionGroup", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(520f, MainUiTheme.ToolbarHeight), TextAnchor.MiddleRight, 10f);

            MoveButtonToGroup(startButton, leftGroup, new Vector2(128f, 40f), "开始仿真");
            MoveButtonToGroup(undoButton, leftGroup, new Vector2(92f, 40f), "撤销");
            MoveButtonToGroup(redoButton, leftGroup, new Vector2(92f, 40f), "重做");
            MoveButtonToGroup(quickDeleteButton, leftGroup, new Vector2(92f, 40f), "删除");
            MoveButtonToGroup(clearWiresButton, leftGroup, new Vector2(92f, 40f), "清线");
            MoveButtonToGroup(clearAllButton, leftGroup, new Vector2(92f, 40f), "清空");
            MoveButtonToGroup(lockButton, leftGroup, new Vector2(92f, 40f), "锁定");

            StyleToolbarButton(startButton, true, false, MainUiTheme.UiTextRole.ToolbarPrimaryButton);
            StyleToolbarButton(undoButton, false, false, MainUiTheme.UiTextRole.ToolbarButton);
            StyleToolbarButton(redoButton, false, false, MainUiTheme.UiTextRole.ToolbarButton);
            StyleToolbarButton(quickDeleteButton, false, true, MainUiTheme.UiTextRole.ToolbarDangerButton);
            StyleToolbarButton(clearWiresButton, false, true, MainUiTheme.UiTextRole.ToolbarDangerButton);
            StyleToolbarButton(clearAllButton, false, true, MainUiTheme.UiTextRole.ToolbarDangerButton);
            StyleToolbarButton(lockButton, false, false, MainUiTheme.UiTextRole.ToolbarButton);

            SetToolbarButtonSize(startButton, 128f);
            SetToolbarButtonSize(undoButton, 92f);
            SetToolbarButtonSize(redoButton, 92f);
            SetToolbarButtonSize(quickDeleteButton, 92f);
            SetToolbarButtonSize(clearWiresButton, 92f);
            SetToolbarButtonSize(clearAllButton, 92f);
            SetToolbarButtonSize(lockButton, 92f);
            SetToolbarButtonSize(saveButton, 118f);
            SetToolbarButtonSize(loadButton, 118f);

            ApplyToolbarIcon(startButton, "ui_toolbar_start_32", 24f, false, true);
            ApplyToolbarIcon(undoButton, "ui_toolbar_undo_24", 24f);
            ApplyToolbarIcon(redoButton, "ui_toolbar_redo_24", 24f);
            ApplyToolbarIcon(quickDeleteButton, "ui_toolbar_delete_24", 24f, true);
            ApplyToolbarIcon(clearWiresButton, "ui_toolbar_clear_wire_24", 24f, true);
            ApplyToolbarIcon(clearAllButton, "ui_toolbar_clear_all_24", 24f, true);
            ApplyToolbarIcon(lockButton, "ui_toolbar_lock_24", 24f);
            
            if (saveButton != null)
            {
                MoveButtonToGroup(saveButton, fileActionGroup, new Vector2(118f, 40f), null);
                StyleToolbarButton(saveButton, false, false, MainUiTheme.UiTextRole.ToolbarFileButton);
                ApplyToolbarIcon(saveButton, "ui_toolbar_save_blueprint_24", 24f);
                SetToolbarButtonSize(saveButton, 118f);
                saveButton.GetComponent<Image>().sprite = UiThemeTokens.GetButtonSprite();
            }

            if (importButton == null && loadButton != null && loadButton.name != "LoadBlueprintButton")
            {
                importButton = loadButton;
            }

            if (importButton == null)
            {
                var importGo = GameObject.Find("ImportButton")
                    ?? GameObject.Find("ImportBlueprintButton")
                    ?? GameObject.Find("ImportDrawingButton")
                    ?? GameObject.Find("导入图纸")
                    ?? GameObject.Find("Load");
                if (importGo != null) importButton = importGo.GetComponent<Button>();
            }

            if (importButton != null)
            {
                MoveButtonToGroup(importButton, fileActionGroup, new Vector2(118f, 40f), "导入图纸");
                StyleToolbarButton(importButton, false, false, MainUiTheme.UiTextRole.ToolbarFileButton);
                ApplyToolbarIcon(importButton, "ui_toolbar_import_blueprint_24", 24f);
                SetToolbarButtonSize(importButton, 118f);
                importButton.GetComponent<Image>().sprite = UiThemeTokens.GetButtonSprite();
            }

            if (colorGroup.Find("WireColorLabel") == null)
            {
                var labelGo = new GameObject("WireColorLabel", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(colorGroup, false);
                labelGo.transform.SetAsFirstSibling();
                var labelText = labelGo.GetComponent<Text>();
                labelText.text = "导线颜色";
                labelText.color = MainUiTheme.Hex("111827");
                MainUiTheme.ApplyTextRole(labelText, MainUiTheme.UiTextRole.ToolbarLabel);
                labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
                var rt = labelText.rectTransform;
                rt.sizeDelta = new Vector2(64f, 28f);
            }

            for (var i = 0; i < colorButtons.Count; i++)
            {
                var button = colorButtons[i];
                if (button == null)
                {
                    continue;
                }

                MoveButtonToGroup(button, colorGroup, new Vector2(28f, 28f), null);
                var img = button.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = UiThemeTokens.GetRoundedSprite(6, 32);
                    img.type = Image.Type.Sliced;
                }
            }

            EnsureVerticalDivider(toolbar, "LeftToolbarDivider", 814f);
            EnsureVerticalDivider(toolbar, "ColorToolbarDivider", 1100f);
            SyncFileActionGroupLayout();

            if (quickRoot != null)
            {
                quickRoot.gameObject.SetActive(false);
            }

            if (quickClearWiresButton != null)
            {
                quickClearWiresButton.gameObject.SetActive(false);
            }

            if (quickClearAllButton != null)
            {
                quickClearAllButton.gameObject.SetActive(false);
            }
        }

        private void ApplyMainFrameLayout()
        {
            var navBar = GameObject.Find("NavBar")?.GetComponent<RectTransform>();
            if (navBar != null)
            {
                navBar.anchorMin = new Vector2(0f, 1f);
                navBar.anchorMax = new Vector2(1f, 1f);
                navBar.pivot = new Vector2(0.5f, 1f);
                navBar.anchoredPosition = Vector2.zero;
                navBar.sizeDelta = new Vector2(0f, MainUiTheme.NavBarHeight);
                var navImage = navBar.GetComponent<Image>() ?? navBar.gameObject.AddComponent<Image>();
                navImage.color = MainUiTheme.PanelBackground;
                var navOutline = navBar.GetComponent<Outline>() ?? navBar.gameObject.AddComponent<Outline>();
                navOutline.effectColor = MainUiTheme.Divider;
                navOutline.effectDistance = new Vector2(0f, -1f);
                ApplyFontsToChildren(navBar, 14);
                ApplyNavigationVisuals(navBar);
            }

            var topBar = GameObject.Find("TopBar")?.GetComponent<RectTransform>();
            if (topBar != null)
            {
                topBar.anchorMin = new Vector2(0f, 1f);
                topBar.anchorMax = new Vector2(1f, 1f);
                topBar.pivot = new Vector2(0.5f, 1f);
                topBar.anchoredPosition = new Vector2(0f, -MainUiTheme.NavBarHeight);
                topBar.sizeDelta = new Vector2(0f, MainUiTheme.ToolbarHeight);
            }

            var workspaceRect = workspace != null ? workspace.WorkspaceRect : null;
            if (workspaceRect != null)
            {
                workspaceRect.offsetMax = new Vector2(workspaceRect.offsetMax.x, -MainUiTheme.MainContentTop);
                workspaceRect.offsetMin = new Vector2(workspaceRect.offsetMin.x, 0f);
                var workspaceImage = workspaceRect.GetComponent<Image>() ?? workspaceRect.gameObject.AddComponent<Image>();
                workspaceImage.color = MainUiTheme.PageBackground;
            }

            var palette = GameObject.Find("Palette")?.GetComponent<RectTransform>();
            if (palette != null)
            {
                palette.anchorMin = new Vector2(0f, 0f);
                palette.anchorMax = new Vector2(0f, 1f);
                palette.pivot = new Vector2(0f, 0.5f);
                palette.offsetMin = new Vector2(0f, 0f);
                palette.offsetMax = new Vector2(MainUiTheme.LeftPanelWidth, -MainUiTheme.MainContentTop);
            }
        }

        private void EnsureMainLogo()
        {
            var logoText = GameObject.Find("Logo")?.GetComponent<Text>();
            if (logoText == null)
            {
                return;
            }

            var sprite = UiIconLibrary.Load("Logo/ui_sidebar_yalong_logo_320") ?? UiIconLibrary.Load("ui_sidebar_yalong_logo_320");
            if (sprite == null)
            {
                return;
            }

            var titleValue = string.IsNullOrWhiteSpace(logoText.text) ? "电工数字学生仿真系统" : logoText.text;
            logoText.text = string.Empty;
            logoText.raycastTarget = false;

            var iconRect = logoText.transform.Find("LogoIcon") as RectTransform;
            if (iconRect == null)
            {
                var iconObject = new GameObject("LogoIcon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(logoText.transform, false);
                iconRect = iconObject.GetComponent<RectTransform>();
            }

            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(18f, 0f);
            iconRect.sizeDelta = new Vector2(40f, 40f);

            var image = iconRect.GetComponent<Image>() ?? iconRect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;

            var titleRect = logoText.transform.Find("LogoTitle") as RectTransform;
            if (titleRect == null)
            {
                var titleObject = new GameObject("LogoTitle", typeof(RectTransform), typeof(Text));
                titleObject.transform.SetParent(logoText.transform, false);
                titleRect = titleObject.GetComponent<RectTransform>();
            }

            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(68f, 0f);
            titleRect.offsetMax = new Vector2(0f, 0f);

            var titleText = titleRect.GetComponent<Text>() ?? titleRect.gameObject.AddComponent<Text>();
            titleText.text = titleValue;
            titleText.color = MainUiTheme.DeepText;
            MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.BrandTitle);
            titleText.raycastTarget = false;

            iconRect.SetAsFirstSibling();
        }

        private RectTransform FindQuickToolRoot()
        {
            var candidates = new[]
            {
                undoButton,
                redoButton,
                quickDeleteButton,
                quickClearWiresButton,
                quickClearAllButton,
                lockButton
            };

            foreach (var candidate in candidates)
            {
                var parent = candidate != null ? candidate.transform.parent as RectTransform : null;
                if (parent != null && parent.name.Contains("Quick"))
                {
                    return parent;
                }
            }

            return null;
        }

        private void ApplyToolbarIcon(Button button, string iconPath, float iconSize, bool danger = false, bool primary = false)
        {
            if (button == null)
            {
                return;
            }

            Color? color = null;
            if (primary) color = Color.white;
            else color = MainUiTheme.Hex("64748B");
            
            var icon = UiIconLibrary.EnsureButtonIcon(button, iconPath, new Vector2(iconSize, iconSize), new Vector2(18f, 0f), color);
            if (icon == null)
            {
                return;
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(40f, 0f);
                label.rectTransform.offsetMax = new Vector2(-8f, 0f);
                label.alignment = TextAnchor.MiddleCenter;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private static void StyleToolbarButton(Button button, bool primary, bool danger, MainUiTheme.UiTextRole role)
        {
            if (button == null) return;

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = UiThemeTokens.GetRoundedSprite(10);
                image.type = Image.Type.Sliced;
                
                if (primary) image.color = MainUiTheme.Hex("2563EB");
                else if (danger) image.color = Color.white;
                else image.color = Color.white;
            }

            var outline = button.GetComponent<Outline>();
            if (outline == null) outline = button.gameObject.AddComponent<Outline>();
            
            if (primary) outline.effectColor = MainUiTheme.Hex("2563EB");
            else if (danger) outline.effectColor = MainUiTheme.Hex("FCA5A5");
            else outline.effectColor = MainUiTheme.Hex("D8DEE8");
            
            outline.effectDistance = new Vector2(1f, -1f);

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                MainUiTheme.ApplyTextRole(label, role);
                label.resizeTextForBestFit = false;
                label.rectTransform.localScale = Vector3.one;

                if (primary)
                {
                    label.color = Color.white;
                }
                else if (danger)
                {
                    label.color = MainUiTheme.Hex("DC2626");
                }
                else
                {
                    label.color = MainUiTheme.Hex("1F2937");
                }
            }
        }

        private RectTransform EnsureGroup(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, TextAnchor childAlignment, float spacing)
        {
            var existing = parent.Find(name) as RectTransform;
            var rect = existing != null ? existing : CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            var layout = rect.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            }

            layout.childAlignment = childAlignment;
            layout.spacing = spacing;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            return rect;
        }

        private Button EnsureButton(RectTransform fallbackParent, Button button, string name, string label)
        {
            if (button != null)
            {
                return button;
            }

            var rect = CreateRect(name, fallbackParent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText("Text", rect, label);
            text.color = new Color(0.05f, 0.08f, 0.14f);
            return button;
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private Text CreateText(string name, Transform parent, string value)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = MainUiTheme.UiFont;
            text.fontSize = 14;
            text.fontStyle = FontStyle.Normal;
            text.color = MainUiTheme.SecondaryText;
            text.alignment = TextAnchor.MiddleCenter;
            text.resizeTextForBestFit = false;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = 14;
            text.raycastTarget = false;

            var rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(-12f, -4f);
            return text;
        }

        private void MoveButtonToGroup(Button button, RectTransform group, Vector2 size, string label)
        {
            if (button == null || group == null)
            {
                return;
            }

            button.transform.SetParent(group, false);
            button.gameObject.SetActive(true);

            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            var layout = button.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = button.gameObject.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;

            if (!string.IsNullOrEmpty(label))
            {
                var text = button.GetComponentInChildren<Text>();
                if (text == null)
                {
                    text = CreateText("Text", button.transform, label);
                }

                text.text = label;
                text.alignment = TextAnchor.MiddleCenter;
                text.resizeTextForBestFit = false;
                text.resizeTextMinSize = 0;
                text.resizeTextMaxSize = 0;
            }
        }

        private static void SetToolbarButtonSize(Button button, float width)
        {
            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(width, 40f);
            }

            var layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredWidth = width;
                layout.preferredHeight = 40f;
                layout.flexibleWidth = 0f;
                layout.flexibleHeight = 0f;
            }
        }

        private void ConfigureWireColorButton(Button button, Color color)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            colors.pressedColor = new Color(0.86f, 0.92f, 1f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            var outline = button.GetComponent<Outline>();
            if (outline == null)
            {
                outline = button.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = new Color(0f, 0f, 0f, 0.16f);
            outline.effectDistance = new Vector2(1f, -1f);
            if (!colorButtonOutlines.Contains(outline))
            {
                colorButtonOutlines.Add(outline);
            }
        }

        private void RefreshWireColorButtons(bool force)
        {
            if (workspace == null || wirePaletteColors == null)
            {
                return;
            }

            var activeColor = workspace.ActiveWirePaletteColor;
            var hasSelectedWire = workspace.HasSelectedWire;
            if (!force && hasSelectedWire == lastActiveWireSelectionState && IsSamePaletteColor(activeColor, lastActiveWireColor))
            {
                return;
            }

            lastActiveWireColor = activeColor;
            lastActiveWireSelectionState = hasSelectedWire;

            for (var i = 0; i < colorButtons.Count && i < wirePaletteColors.Length; i++)
            {
                var button = colorButtons[i];
                if (button == null)
                {
                    continue;
                }

                var outline = button.GetComponent<Outline>();
                var active = IsSamePaletteColor(activeColor, wirePaletteColors[i]);
                if (outline != null)
                {
                    outline.effectColor = active ? MainUiTheme.Hex("1D4ED8") : new Color(0f, 0f, 0f, 0.16f);
                    outline.effectDistance = active ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
                }
            }
        }

        private static void ApplyFontsToChildren(RectTransform root, int defaultSize)
        {
            if (root == null)
            {
                return;
            }

            var texts = root.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null)
                {
                    continue;
                }

                text.font = MainUiTheme.BodyFont;
                if (text.fontSize <= 0 || text.fontSize == 16)
                {
                    text.fontSize = defaultSize;
                }
            }
        }

        private static void ApplyNavigationVisuals(RectTransform navBar)
        {
            if (navBar == null)
            {
                return;
            }

            var buttons = navBar.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                var label = button.GetComponentInChildren<Text>(true);
                var labelText = label != null ? label.text : string.Empty;
                var selected = labelText.Contains("模拟电路");

                var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
                image.color = selected ? MainUiTheme.SelectedBlue : Color.white;

                var outline = button.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = selected ? MainUiTheme.SelectedBlue : Color.clear;
                    outline.effectDistance = Vector2.zero;
                }

                if (label != null)
                {
                    label.color = selected ? MainUiTheme.PrimaryBlue : MainUiTheme.NormalText;
                    MainUiTheme.ApplyTextRole(label, selected ? MainUiTheme.UiTextRole.NavTextSelected : MainUiTheme.UiTextRole.NavText);
                }
            }
        }

        private void SyncFileActionGroupLayout()
        {
            var rightGroup = ResolveRightActionGroup();
            if (rightGroup == null)
            {
                return;
            }

            ConfigureRightActionGroup(rightGroup);

            var loadBlueprintButton = ResolveLoadBlueprintButton();
            var saveBlueprintButton = ResolveSaveBlueprintButton();
            var importBlueprintButton = ResolveImportBlueprintButton();

            PrepareToolbarFileButton(loadBlueprintButton, 118f, "加载模板", "ui_toolbar_load_blueprint_24", MainUiTheme.UiTextRole.ToolbarFileButton);
            PrepareToolbarFileButton(saveBlueprintButton, 118f, "保存图纸", "ui_toolbar_save_blueprint_24", MainUiTheme.UiTextRole.ToolbarFileButton);
            PrepareToolbarFileButton(importBlueprintButton, 118f, "导入图纸", "ui_toolbar_import_blueprint_24", MainUiTheme.UiTextRole.ToolbarFileButton);

            updateLayoutButton = updateLayoutButton ?? GameObject.Find("UpdateTemplateLayoutButton")?.GetComponent<Button>();
            if (updateLayoutButton != null)
            {
                PrepareToolbarFileButton(updateLayoutButton, 112f, "更新布局", "ui_toolbar_load_blueprint_24", MainUiTheme.UiTextRole.DeveloperToolButton);
                StyleToolbarButton(updateLayoutButton, false, false, MainUiTheme.UiTextRole.DeveloperToolButton);
                updateLayoutButton.gameObject.name = "UpdateTemplateLayoutButton";
            }

            var showDevelopmentButton = ShouldShowUpdateLayoutButton() && updateLayoutButton != null;
            var divider = EnsureDevelopmentDivider(showDevelopmentButton);

            if (updateLayoutButton != null)
            {
                updateLayoutButton.gameObject.SetActive(showDevelopmentButton);
            }

            RemoveUnexpectedRightActionChildren(rightGroup, updateLayoutButton, divider, loadBlueprintButton, saveBlueprintButton, importBlueprintButton);
            ReorderFileActionButtons(rightGroup, updateLayoutButton, divider, loadBlueprintButton, saveBlueprintButton, importBlueprintButton, showDevelopmentButton);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rightGroup);
        }

        private RectTransform ResolveRightActionGroup()
        {
            if (fileActionGroup == null)
            {
                fileActionGroup = GameObject.Find("FileActionGroup")?.GetComponent<RectTransform>();
            }

            if (fileActionGroup == null)
            {
                var topBar = GameObject.Find("TopBar")?.GetComponent<RectTransform>();
                if (topBar != null)
                {
                    fileActionGroup = EnsureGroup(
                        topBar,
                        "FileActionGroup",
                        new Vector2(1f, 0.5f),
                        new Vector2(1f, 0.5f),
                        new Vector2(1f, 0.5f),
                        new Vector2(-24f, 0f),
                        new Vector2(520f, MainUiTheme.ToolbarHeight),
                        TextAnchor.MiddleRight,
                        10f);
                }
            }

            return fileActionGroup;
        }

        private static void ConfigureRightActionGroup(RectTransform group)
        {
            if (group == null)
            {
                return;
            }

            group.anchorMin = new Vector2(1f, 0.5f);
            group.anchorMax = new Vector2(1f, 0.5f);
            group.pivot = new Vector2(1f, 0.5f);
            group.anchoredPosition = new Vector2(-24f, 0f);
            group.sizeDelta = new Vector2(520f, MainUiTheme.ToolbarHeight);

            var layout = group.GetComponent<HorizontalLayoutGroup>() ?? group.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = group.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                DestroyImmediate(fitter);
            }

            var mask = group.GetComponent<Mask>();
            if (mask != null)
            {
                DestroyImmediate(mask);
            }

            var rectMask = group.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                DestroyImmediate(rectMask);
            }
        }

        private Button ResolveLoadBlueprintButton()
        {
            var templateButton = GameObject.Find("LoadBlueprintButton")?.GetComponent<Button>();
            if (templateButton != null)
            {
                return templateButton;
            }

            return loadButton != null && loadButton.name == "LoadBlueprintButton" ? loadButton : null;
        }

        private Button ResolveSaveBlueprintButton()
        {
            if (saveButton == null)
            {
                saveButton = GameObject.Find("SaveButton")?.GetComponent<Button>();
            }

            return saveButton;
        }

        private Button ResolveImportBlueprintButton()
        {
            if (importButton == null)
            {
                importButton = GameObject.Find("ImportButton")?.GetComponent<Button>()
                    ?? GameObject.Find("ImportBlueprintButton")?.GetComponent<Button>()
                    ?? GameObject.Find("ImportDrawingButton")?.GetComponent<Button>()
                    ?? GameObject.Find("Load")?.GetComponent<Button>();
            }

            if (importButton == null && loadButton != null && loadButton.name != "LoadBlueprintButton")
            {
                importButton = loadButton;
            }

            return importButton;
        }

        private void PrepareToolbarFileButton(Button button, float width, string label, string iconPath, MainUiTheme.UiTextRole role)
        {
            if (button == null)
            {
                return;
            }

            var rightGroup = ResolveRightActionGroup();
            if (rightGroup == null)
            {
                return;
            }

            button.gameObject.SetActive(true);
            button.transform.SetParent(rightGroup, false);
            MoveButtonToGroup(button, rightGroup, new Vector2(width, 40f), label);
            StyleToolbarButton(button, false, false, role);
            SetToolbarButtonSize(button, width);
            EnsureFixedLayoutElement(button, width, 40f);

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = UiThemeTokens.GetButtonSprite();
                image.type = Image.Type.Sliced;
            }

            ApplyToolbarIcon(button, iconPath, 24f);
        }

        private GameObject EnsureDevelopmentDivider(bool visible)
        {
            var rightGroup = ResolveRightActionGroup();
            if (rightGroup == null)
            {
                return null;
            }

            var dividerTransform = rightGroup.Find("DevelopmentDivider") as RectTransform;
            if (dividerTransform == null)
            {
                var legacyDivider = rightGroup.Find("UpdateLayoutDivider") as RectTransform;
                dividerTransform = legacyDivider;
                if (dividerTransform == null)
                {
                    var dividerObject = new GameObject("DevelopmentDivider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                    dividerObject.transform.SetParent(rightGroup, false);
                    dividerTransform = dividerObject.GetComponent<RectTransform>();
                }

                dividerTransform.gameObject.name = "DevelopmentDivider";
            }

            dividerTransform.gameObject.SetActive(visible);
            dividerTransform.sizeDelta = new Vector2(1f, 24f);

            var image = dividerTransform.GetComponent<Image>() ?? dividerTransform.gameObject.AddComponent<Image>();
            image.color = MainUiTheme.Divider;
            image.raycastTarget = false;

            var layout = dividerTransform.GetComponent<LayoutElement>() ?? dividerTransform.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = 1f;
            layout.preferredWidth = 1f;
            layout.flexibleWidth = 0f;
            layout.minHeight = 24f;
            layout.preferredHeight = 24f;
            layout.flexibleHeight = 0f;

            return dividerTransform.gameObject;
        }

        private static void RemoveUnexpectedRightActionChildren(
            RectTransform rightGroup,
            Button updateButton,
            GameObject divider,
            Button loadBlueprintButton,
            Button saveBlueprintButton,
            Button importBlueprintButton)
        {
            if (rightGroup == null)
            {
                return;
            }

            var allowed = new HashSet<GameObject>();
            if (updateButton != null) allowed.Add(updateButton.gameObject);
            if (divider != null) allowed.Add(divider);
            if (loadBlueprintButton != null) allowed.Add(loadBlueprintButton.gameObject);
            if (saveBlueprintButton != null) allowed.Add(saveBlueprintButton.gameObject);
            if (importBlueprintButton != null) allowed.Add(importBlueprintButton.gameObject);

            for (var i = rightGroup.childCount - 1; i >= 0; i--)
            {
                var child = rightGroup.GetChild(i).gameObject;
                if (allowed.Contains(child))
                {
                    continue;
                }

                var name = child.name;
                if (name.Contains("Spacer") || name.Contains("Divider") || name.Contains("Gap"))
                {
                    DestroyImmediate(child);
                    continue;
                }

                child.SetActive(false);
            }
        }

        private static void ReorderFileActionButtons(
            RectTransform rightGroup,
            Button updateButton,
            GameObject divider,
            Button loadBlueprintButton,
            Button saveBlueprintButton,
            Button importBlueprintButton,
            bool includeDevelopmentButton)
        {
            if (rightGroup == null)
            {
                return;
            }

            var order = new List<Transform>();
            if (includeDevelopmentButton && updateButton != null)
            {
                order.Add(updateButton.transform);
            }

            if (includeDevelopmentButton && divider != null)
            {
                order.Add(divider.transform);
            }

            if (loadBlueprintButton != null) order.Add(loadBlueprintButton.transform);
            if (saveBlueprintButton != null) order.Add(saveBlueprintButton.transform);
            if (importBlueprintButton != null) order.Add(importBlueprintButton.transform);

            for (var i = 0; i < order.Count; i++)
            {
                order[i].SetSiblingIndex(i);
            }
        }

        private static void EnsureFixedLayoutElement(Button button, float width, float height)
        {
            if (button == null)
            {
                return;
            }

            var layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private IEnumerator FinalizeRightActionGroupAfterStartup()
        {
            yield return null;
            yield return null;
            SyncFileActionGroupLayout();
            yield return null;
            SyncFileActionGroupLayout();
            rightActionGroupStartupRoutine = null;
        }

        private static bool ShouldShowUpdateLayoutButton()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return true;
#else
            return false;
#endif
        }

        private static bool IsSamePaletteColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.35f;
        }

        private void EnsureLocalInspectorPanel()
        {
            // 检查助手由 LocalInspectorPanel 自行建立内部 UI；此处只确保主场景存在一个宿主实例。
            if (workspace == null)
            {
                return;
            }

            var parent = localInspectorHostRoot != null
                ? localInspectorHostRoot
                : workspace.WorkspaceRect != null ? workspace.WorkspaceRect.parent as RectTransform : null;
            if (parent == null)
            {
                parent = startButton != null && startButton.transform.parent != null ? startButton.transform.parent.parent as RectTransform : null;
            }

            if (parent == null)
            {
                return;
            }

            localInspectorPanel = LocalInspectorPanel.Create(parent, workspace);
        }

        /// <summary>
        /// 由 Demo 场景装配工具指定检查助手的控制模式宿主，使模式切换能够显隐整个助手及折叠把手。
        /// </summary>
        public void ConfigureLocalInspectorHost(RectTransform hostRoot)
        {
            localInspectorHostRoot = hostRoot;
        }
        private void EnsureBlueprintPanels()
        {
            // 图纸预览与练习参考面板的实际内容由各自 Controller 管理，此处不参与模板或图片加载。
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindObjectOfType<Canvas>();
            }

            var parent = canvas != null ? canvas.transform as RectTransform : transform.root as RectTransform;
            if (parent == null)
            {
                return;
            }

            if (saveDialog == null)
            {
                saveDialog = FindObjectOfType<SaveBlueprintDialog>();
            }

            if (saveDialog == null)
            {
                saveDialog = SaveBlueprintDialog.Create(parent, saveLoadService);
            }
            else
            {
                saveDialog.Initialize(saveLoadService);
            }

            if (importPanel == null)
            {
                importPanel = ImportBlueprintPanel.Create(parent, saveLoadService);
            }
            else
            {
                importPanel.Initialize(saveLoadService);
            }
        }

        private void EnsureVerticalDivider(RectTransform parent, string name, float x)
        {
            var divider = parent.Find(name) as RectTransform;
            if (divider == null)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                divider = go.GetComponent<RectTransform>();
            }

            divider.anchorMin = new Vector2(0f, 0.5f);
            divider.anchorMax = new Vector2(0f, 0.5f);
            divider.pivot = new Vector2(0.5f, 0.5f);
            divider.anchoredPosition = new Vector2(x, 0f);
            divider.sizeDelta = new Vector2(1f, 32f);

            var img = divider.GetComponent<Image>();
            img.color = MainUiTheme.Hex("E5E7EB");
            img.raycastTarget = false;
        }

        private void OpenSaveDialog()
        {
            if (saveDialog != null)
            {
                saveDialog.Show();
                return;
            }

            saveLoadService.Save();
        }

        private void OpenImportPanel()
        {
            if (importPanel != null)
            {
                importPanel.Show();
                return;
            }

            saveLoadService.Load();
        }

        private void ToggleSimulation()
        {
            workspace.ToggleSimulation();
            RefreshSimulationButtonLabel();
        }

        private void RefreshSimulationButtonLabel()
        {
            var label = startButton != null ? startButton.GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                label.color = Color.white;
                MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.ToolbarPrimaryButton);
                label.text = workspace != null && workspace.IsSimulationRunning ? "结束仿真" : "开始仿真";
            }
        }

        private void ToggleLock()
        {
            workspace.ToggleInteractionLock();
            RefreshLockButtonLabel();
        }

        private void RefreshLockButtonLabel()
        {
            var label = lockButton != null ? lockButton.GetComponentInChildren<Text>() : null;
            if (label != null)
            {
                label.color = MainUiTheme.Hex("1F2937");
                MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.ToolbarButton);
                label.text = workspace != null && workspace.IsInteractionLocked ? "解锁" : "锁定";
            }
        }
    }
}
