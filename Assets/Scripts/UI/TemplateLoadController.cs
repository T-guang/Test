using ElectricalSim.Core;
using ElectricalSim.Templates;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 系统模板加载的 UI 协调入口。
    /// 负责读取目录、响应选择动作、在已有画布时请求确认，并依次调用单模板 Loader 与 SpawnService；不复制 JSON 解析或元件、导线生成算法。
    /// 系统模板加载会记录 TemplateEditSession，和用户保存图纸的导入入口保持边界。修改后必须回归图纸集、仿真广场以及家庭和工业模板加载。
    /// </summary>
    public sealed class TemplateLoadController : MonoBehaviour
    {
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private SaveLoadService saveLoadService;
        [SerializeField] private string catalogPath = "Blueprints/Templates/template_catalog";

        private TemplateSelectionPanel selectionPanel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallAfterSceneLoad()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureController();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureController();
        }

        private static void EnsureController()
        {
            // 场景未预置该控制器时只补挂一份；重复控制器会造成工具栏按钮和加载监听器重复创建。
            if (FindObjectOfType<WorkspaceController>() == null || FindObjectOfType<TemplateLoadController>() != null)
            {
                return;
            }

            new GameObject("TemplateLoadController").AddComponent<TemplateLoadController>();
        }

        private void Start()
        {
            if (workspace == null)
            {
                workspace = FindObjectOfType<WorkspaceController>();
            }

            if (saveLoadService == null)
            {
                saveLoadService = FindObjectOfType<SaveLoadService>();
            }

            CreateButtonIfNeeded();

            if (FindObjectOfType<UpdateTemplateLayoutController>() == null)
            {
                UpdateTemplateLayoutController.Create(workspace);
            }
        }

        public void ShowTemplateSelection()
        {
            if (workspace == null)
            {
                return;
            }

            // Catalog 只提供可展示、可选择的目录项；单张模板内容在确认选择后才读取。
            if (!CircuitTemplateCatalogLoader.TryLoad(catalogPath, out var catalog, out var error))
            {
                workspace.SetStatus(error ?? "标准图纸目录读取失败。");
                return;
            }

            EnsureSelectionPanel();
            if (selectionPanel == null)
            {
                workspace.SetStatus("标准图纸面板创建失败。");
                return;
            }

            selectionPanel.Show(catalog.templates);
        }

        public void RequestLoadTemplateFromGallery(ElectricalSim.Templates.CircuitTemplateCatalogItemDto item)
        {
            RequestLoadTemplateFromGallery(item, null);
        }

        public void RequestLoadTemplateFromGallery(ElectricalSim.Templates.CircuitTemplateCatalogItemDto item, System.Action onLoaded)
        {
            if (item != null)
            {
                LoadTemplate(item, onLoaded);
            }
        }

        private void LoadTemplate(ElectricalSim.Templates.CircuitTemplateCatalogItemDto item)
        {
            LoadTemplate(item, null);
        }

        private void LoadTemplate(ElectricalSim.Templates.CircuitTemplateCatalogItemDto item, System.Action onLoaded)
        {
            if (workspace == null || item == null)
            {
                return;
            }

            if (HasWorkspaceContent())
            {
                ShowLoadConfirm(item, () =>
                {
                    if (LoadTemplateNow(item))
                    {
                        onLoaded?.Invoke();
                    }
                });
                return;
            }

            if (LoadTemplateNow(item))
            {
                onLoaded?.Invoke();
            }
        }

        private bool LoadTemplateNow(CircuitTemplateCatalogItemDto item)
        {
            if (workspace == null || item == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.resourcePath))
            {
                workspace.SetStatus("模板路径为空：" + item.templateId);
                return false;
            }

            // 顺序不能调整：先读取 DTO，再由 SpawnService 完整校验并决定何时清空当前画布。
            if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var template, out var error))
            {
                workspace.SetStatus(string.IsNullOrWhiteSpace(error) ? "模板读取失败：" + item.templateId : error);
                return false;
            }

            var catalog = saveLoadService != null ? saveLoadService.Catalog : null;
            if (!CircuitTemplateSpawnService.Spawn(template, workspace, catalog, out var message))
            {
                workspace.SetStatus(string.IsNullOrWhiteSpace(message) ? "模板生成失败：" + item.templateId : message);
                return false;
            }

            // 仅成功生成后记录当前系统模板身份，供 Editor 布局更新等维护功能使用。
            TemplateEditSession.RecordTemplate(item.templateId, template.templateName, item.resourcePath);
            workspace.SetStatus("已加载标准图纸：" + template.templateName);
            return true;
        }

        private bool HasWorkspaceContent()
        {
            var hasComponents = workspace != null && workspace.Components != null && workspace.Components.Count > 0;
            var hasWires = workspace != null && workspace.WireManager != null && workspace.WireManager.Wires != null && workspace.WireManager.Wires.Count > 0;
            return hasComponents || hasWires;
        }

        private void ShowLoadConfirm(CircuitTemplateCatalogItemDto item, System.Action onConfirm)
        {
            var canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                onConfirm?.Invoke();
                return;
            }

            var overlay = new GameObject("LoadTemplateConfirmDialog", typeof(RectTransform), typeof(Image));
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

            var title = CreateText("Title", panel.transform, "加载标准图纸", 20, FontStyle.Bold, MainUiTheme.Hex("111827"));
            title.alignment = TextAnchor.MiddleCenter;
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.offsetMin = new Vector2(20f, -60f);
            title.rectTransform.offsetMax = new Vector2(-20f, -28f);

            var templateName = string.IsNullOrWhiteSpace(item.templateName) ? item.templateId : item.templateName;
            var message = CreateText("Message", panel.transform, $"当前画布将被清空并加载标准图纸“{templateName}”，是否继续？", 15, FontStyle.Normal, MainUiTheme.Hex("475569"));
            message.alignment = TextAnchor.MiddleCenter;
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            message.verticalOverflow = VerticalWrapMode.Overflow;
            message.lineSpacing = 1.3f;
            message.rectTransform.anchorMin = new Vector2(0f, 0f);
            message.rectTransform.anchorMax = new Vector2(1f, 1f);
            message.rectTransform.offsetMin = new Vector2(48f, 85f);
            message.rectTransform.offsetMax = new Vector2(-48f, -90f);

            var cancel = CreateButton(panel.transform, "取消", MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("334155"));
            var cancelRect = cancel.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0.5f, 0f);
            cancelRect.anchorMax = new Vector2(0.5f, 0f);
            cancelRect.pivot = new Vector2(0.5f, 0.5f);
            cancelRect.anchoredPosition = new Vector2(-74f, 48f);
            cancelRect.sizeDelta = new Vector2(118f, 38f);
            cancel.onClick.AddListener(() => Destroy(overlay));

            var confirm = CreateButton(panel.transform, "确认加载", MainUiTheme.Hex("2563EB"), Color.white);
            var confirmRect = confirm.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(0.5f, 0f);
            confirmRect.anchorMax = new Vector2(0.5f, 0f);
            confirmRect.pivot = new Vector2(0.5f, 0.5f);
            confirmRect.anchoredPosition = new Vector2(74f, 48f);
            confirmRect.sizeDelta = new Vector2(118f, 38f);

            var confirmText = confirm.transform.Find("Text")?.GetComponent<Text>();
            if (confirmText != null)
            {
                confirmText.fontStyle = FontStyle.Bold;
            }

            confirm.onClick.AddListener(() =>
            {
                Destroy(overlay);
                onConfirm?.Invoke();
            });
        }

        private void CreateButtonIfNeeded()
        {
            if (workspace == null)
            {
                return;
            }

            var saveButton = GameObject.Find("Save");
            var importButton = GameObject.Find("Load");
            var toolbarParent = saveButton != null ? saveButton.transform.parent as RectTransform : null;
            if (toolbarParent == null && workspace.WorkspaceRect != null)
            {
                toolbarParent = workspace.WorkspaceRect.parent as RectTransform;
            }

            if (toolbarParent == null)
            {
                return;
            }

            var oldButton = GameObject.Find("LoadTemplateButton");
            if (oldButton != null)
            {
                Destroy(oldButton);
            }

            var fileActionGroup = EnsureFileActionGroup(toolbarParent);
            if (fileActionGroup == null)
            {
                return;
            }

            var buttonObject = GameObject.Find("LoadBlueprintButton");
            if (buttonObject == null)
            {
                buttonObject = new GameObject("LoadBlueprintButton", typeof(RectTransform), typeof(Image), typeof(Button));
            }

            buttonObject.transform.SetParent(fileActionGroup, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(118f, MainUiTheme.ToolbarButtonHeight);

            var image = buttonObject.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetButtonSprite();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = buttonObject.GetComponent<Outline>() ?? buttonObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.RemoveListener(ShowTemplateSelection);
            button.onClick.AddListener(ShowTemplateSelection);

            var label = buttonObject.GetComponentInChildren<Text>();
            if (label == null)
            {
                label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(buttonObject.transform, false);
            }

            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = "加载模板";
            MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.ToolbarFileButton);
            label.color = MainUiTheme.Hex("1F2937");
            label.alignment = TextAnchor.MiddleCenter;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            ApplyFileButtonIcon(button, "ui_toolbar_load_blueprint_24", MainUiTheme.Hex("64748B"));

            PrepareFileButton(saveButton);
            PrepareFileButton(importButton);
            ApplyFileButtonIcon(saveButton != null ? saveButton.GetComponent<Button>() : null, "ui_toolbar_save_blueprint_24", MainUiTheme.Hex("64748B"));
            ApplyFileButtonIcon(importButton != null ? importButton.GetComponent<Button>() : null, "ui_toolbar_import_blueprint_24", MainUiTheme.Hex("64748B"));
            if (saveButton != null)
            {
                saveButton.transform.SetParent(fileActionGroup, false);
            }

            if (importButton != null)
            {
                importButton.transform.SetParent(fileActionGroup, false);
            }

            buttonObject.transform.SetSiblingIndex(0);
            if (saveButton != null)
            {
                saveButton.transform.SetSiblingIndex(1);
            }

            if (importButton != null)
            {
                importButton.transform.SetSiblingIndex(2);
            }
        }

        private static RectTransform EnsureFileActionGroup(RectTransform toolbarParent)
        {
            var existing = toolbarParent.Find("FileActionGroup");
            var groupObject = existing != null ? existing.gameObject : new GameObject("FileActionGroup", typeof(RectTransform));
            groupObject.transform.SetParent(toolbarParent, false);

            var rect = groupObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(382f, MainUiTheme.ToolbarHeight);

            var layout = groupObject.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
            {
                layout = groupObject.AddComponent<HorizontalLayoutGroup>();
            }

            layout.childAlignment = TextAnchor.MiddleRight;
            layout.spacing = 8f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            return rect;
        }

        private static void PrepareFileButton(GameObject buttonObject)
        {
            if (buttonObject == null)
            {
                return;
            }

            var rect = buttonObject.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(118f, MainUiTheme.ToolbarButtonHeight);

            var image = buttonObject.GetComponent<Image>() ?? buttonObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetButtonSprite();
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = buttonObject.GetComponent<Outline>() ?? buttonObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);

            var label = buttonObject.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.font = MainUiTheme.UiFont;
                label.fontSize = 15;
                label.fontStyle = FontStyle.Bold;
                label.color = MainUiTheme.Hex("1F2937");
                label.alignment = TextAnchor.MiddleCenter;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private static void ApplyFileButtonIcon(Button button, string iconPath, Color iconColor)
        {
            var icon = UiIconLibrary.EnsureButtonIcon(button, iconPath, new Vector2(24f, 24f), new Vector2(18f, 0f), iconColor);
            if (icon == null || button == null)
            {
                return;
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(46f, 0f);
                label.rectTransform.offsetMax = new Vector2(-12f, 0f);
                label.alignment = TextAnchor.MiddleCenter;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void EnsureSelectionPanel()
        {
            if (selectionPanel != null)
            {
                return;
            }

            RectTransform parent = null;
            if (workspace != null && workspace.WorkspaceRect != null)
            {
                parent = workspace.WorkspaceRect.root as RectTransform;
                if (parent == null)
                {
                    parent = workspace.WorkspaceRect.parent as RectTransform;
                }
            }

            if (parent != null)
            {
                selectionPanel = TemplateSelectionPanel.Create(parent, LoadTemplate);
            }
        }

        private static Text CreateText(string name, Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            MainUiTheme.ApplyText(label, size, style, color, TextAnchor.UpperLeft, false);
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, string text, Color color, Color textColor)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8, 64);
            image.type = Image.Type.Sliced;
            image.color = color;
            
            var label = CreateText("Text", go.transform, text, 15, FontStyle.Normal, textColor);
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }
    }
}

