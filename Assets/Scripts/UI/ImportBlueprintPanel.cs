using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.Platform;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 用户图纸列表、删除确认和外部 JSON 导入的 UI 面板。
    /// 面板通过平台文件选择包装层取得文本后交给 SaveLoadService 解析和恢复；不承担系统模板加载，不重写 JSON 解析，也不把外部文件名当作模板 ID 或有效性证据。
    /// 外部导入当前直接加载所选内容而不自动复制到 SavedBlueprints。修改后需回归取消选择、合法/非法 JSON、无效路径和 Windows EXE 文件选择。
    /// </summary>
    public sealed class ImportBlueprintPanel : MonoBehaviour
    {
        [SerializeField] private SaveLoadService saveLoadService;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private Text emptyText;
        [SerializeField] private Text errorText;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject confirmDeletePanel;
        [SerializeField] private Text confirmDeleteText;
        [SerializeField] private Button confirmDeleteButton;
        [SerializeField] private Button cancelDeleteButton;

        private SavedBlueprintInfo pendingDelete;

        public static ImportBlueprintPanel Create(RectTransform parent, SaveLoadService service)
        {
            var root = new GameObject("ImportBlueprintPanel", typeof(RectTransform), typeof(Image), typeof(ImportBlueprintPanel));
            root.transform.SetParent(parent, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.32f);

            var panel = root.GetComponent<ImportBlueprintPanel>();
            panel.saveLoadService = service;
            panel.BuildUi(rect);
            root.SetActive(false);
            return panel;
        }

        public void Initialize(SaveLoadService service)
        {
            saveLoadService = service;
            // 面板会被复用，重复初始化前移除回调，避免一次关闭或删除操作累计执行。
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Hide);
                closeButton.onClick.AddListener(Hide);
            }

            if (confirmDeleteButton != null)
            {
                confirmDeleteButton.onClick.RemoveListener(ConfirmDelete);
                confirmDeleteButton.onClick.AddListener(ConfirmDelete);
            }

            if (cancelDeleteButton != null)
            {
                cancelDeleteButton.onClick.RemoveListener(CancelDelete);
                cancelDeleteButton.onClick.AddListener(CancelDelete);
            }
        }

        public void Show()
        {
            if (!gameObject.activeSelf) ModalInputGate.NotifyOpened();
            gameObject.SetActive(true);
            CancelDelete();
            RefreshList();
        }

        public void Hide()
        {
            if (gameObject.activeSelf) ModalInputGate.NotifyClosed();
            CancelDelete();
            gameObject.SetActive(false);
        }

        private void RefreshList()
        {
            // 列表来自 SaveLoadService 的用户保存目录，不扫描系统模板或任意外部文件夹。
            ClearList();
            SetError(string.Empty);

            if (saveLoadService == null)
            {
                SetEmpty(true, "保存服务未初始化。");
                return;
            }

            var blueprints = saveLoadService.ListSavedBlueprints();
            if (blueprints.Count == 0)
            {
                SetEmpty(true, "暂无已保存图纸。");
                return;
            }

            SetEmpty(false, string.Empty);
            foreach (var blueprint in blueprints)
            {
                var item = SavedBlueprintListItem.Create(contentRoot);
                item.Initialize(blueprint, LoadBlueprint, RequestDelete);
            }
        }

        private void LoadBlueprint(SavedBlueprintInfo blueprint)
        {
            if (blueprint == null || saveLoadService == null)
            {
                return;
            }

            // F1-A：练习模式下正式导入操作前拒绝，保持练习、参考图纸、画布、运行态全部不变。
            var practice = ElectricalSim.Practice.PracticeSessionController.Instance;
            if (practice != null && practice.IsPracticeActive)
            {
                SetError("请先退出当前练习后再导入图纸。");
                return;
            }

            if (saveLoadService.LoadFromFile(blueprint.filePath, out var error))
            {
                Hide();
                return;
            }

            SetError(string.IsNullOrWhiteSpace(error) ? "导入失败。" : error);
        }

        private void RequestDelete(SavedBlueprintInfo blueprint)
        {
            if (blueprint == null)
            {
                return;
            }

            pendingDelete = blueprint;
            if (confirmDeleteText != null)
            {
                confirmDeleteText.text = $"确定要删除图纸“{blueprint.documentName}”吗？此操作不可恢复。";
            }

            if (confirmDeletePanel != null)
            {
                confirmDeletePanel.SetActive(true);
                confirmDeletePanel.transform.SetAsLastSibling();
            }
        }

        private void ConfirmDelete()
        {
            if (pendingDelete == null || saveLoadService == null)
            {
                CancelDelete();
                return;
            }

            if (saveLoadService.DeleteSavedBlueprint(pendingDelete.filePath, out var error))
            {
                pendingDelete = null;
                if (confirmDeletePanel != null)
                {
                    confirmDeletePanel.SetActive(false);
                }

                RefreshList();
                return;
            }

            SetError(string.IsNullOrWhiteSpace(error) ? "删除失败。" : error);
            CancelDelete();
        }

        private void CancelDelete()
        {
            pendingDelete = null;
            if (confirmDeletePanel != null)
            {
                confirmDeletePanel.SetActive(false);
            }
        }

        private void BuildUi(RectTransform root)
        {
            var panel = CreateRect("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(800f, 640f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = Color.white;
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(16);
            panelImage.type = Image.Type.Sliced;

            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.886f, 0.91f, 0.941f);
            outline.effectDistance = new Vector2(1, -1);

            var title = CreateText("Title", panel, "导入图纸", 20, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -28f), new Vector2(200f, 32f));
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(0.118f, 0.161f, 0.231f);
            title.font = MainUiTheme.TitleFont;

            var divider = CreateRect("Divider", panel, new Vector2(0f, 1f), new Vector2(800f, 1f));
            divider.pivot = new Vector2(0f, 1f);
            divider.anchoredPosition = new Vector2(0f, -56f);
            divider.gameObject.AddComponent<Image>().color = new Color(0.898f, 0.906f, 0.922f);
            
            var externalBtnText = "从电脑导入图纸";
            var externalButton = CreateButton(panel, "ExternalImportButton", externalBtnText, new Vector2(596f, -28f), new Vector2(140f, 40f), new Color(0.937f, 0.965f, 1f), new Color(0.145f, 0.388f, 0.922f));
            var extOutline = externalButton.gameObject.AddComponent<Outline>();
            extOutline.effectColor = new Color(0.576f, 0.773f, 0.992f);
            extOutline.effectDistance = new Vector2(1, -1);
            externalButton.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            externalButton.onClick.AddListener(OnExternalImportClicked);

            closeButton = CreateButton(panel, "CloseButton", "×", new Vector2(752f, -28f), new Vector2(36f, 36f), new Color(0.973f, 0.98f, 0.988f), new Color(0.392f, 0.455f, 0.545f));
            closeButton.GetComponentInChildren<Text>().fontSize = 22;

            emptyText = CreateText("EmptyText", panel, "暂无已保存图纸。", 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 36f));
            errorText = CreateText("ErrorText", panel, string.Empty, 14, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -616f), new Vector2(752f, 28f));
            errorText.color = new Color(0.863f, 0.149f, 0.149f);

            var scrollObject = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(panel, false);
            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0f, 1f);
            scrollRectTransform.anchorMax = new Vector2(0f, 1f);
            scrollRectTransform.pivot = new Vector2(0f, 1f);
            scrollRectTransform.anchoredPosition = new Vector2(24f, -76f);
            scrollRectTransform.sizeDelta = new Vector2(752f, 520f);
            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color(0.945f, 0.961f, 0.976f);
            scrollImage.sprite = UiThemeTokens.GetRoundedSprite(8);
            scrollImage.type = Image.Type.Sliced;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            contentRoot = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            contentRoot.SetParent(viewport.transform, false);
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.offsetMin = new Vector2(16f, 0f);
            contentRoot.offsetMax = new Vector2(-16f, 0f);

            var layout = contentRoot.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.padding = new RectOffset(0, 0, 16, 16);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = contentRoot.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRoot;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 26f;

            BuildConfirmDeletePanel(panel);
            Initialize(saveLoadService);
        }

        private void OnExternalImportClicked()
        {
            SetError(string.Empty);

            // F1-A：练习模式下正式导入操作前拒绝，保持练习、参考图纸、画布、运行态全部不变。
            var practice = ElectricalSim.Practice.PracticeSessionController.Instance;
            if (practice != null && practice.IsPracticeActive)
            {
                SetError("请先退出当前练习后再导入图纸。");
                return;
            }

            // NativeFileBrowser/WindowsFileDialog/Receiver 仅承担平台文件选择；选中文本仍需由 SaveLoadService 做格式、定义和端子校验。
            NativeFileBrowser.RequestImportBlueprint(
                json => 
                {
                    if (saveLoadService != null)
                    {
                        // 成功后直接关闭面板；当前实现不将外部文件自动复制为 SavedBlueprints 中的用户文件。
                        if (saveLoadService.LoadFromJsonString(json, out var error))
                        {
                            Hide();
                        }
                        else
                        {
                            SetError(string.IsNullOrWhiteSpace(error) ? "外部导入失败。" : error);
                        }
                    }
                },
                errorMsg => 
                {
                    SetError(errorMsg);
                }
            );
        }

        private void BuildConfirmDeletePanel(RectTransform parent)
        {
            var overlay = CreateRect("ConfirmDeletePanel", parent, new Vector2(0.5f, 0.5f), new Vector2(420f, 190f));
            var img = overlay.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 1f);
            img.sprite = UiThemeTokens.GetRoundedSprite(16);
            img.type = Image.Type.Sliced;
            confirmDeletePanel = overlay.gameObject;

            CreateText("Title", overlay, "删除图纸", 20, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(240f, 32f));
            confirmDeleteText = CreateText("Message", overlay, string.Empty, 15, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(24f, -66f), new Vector2(372f, 64f));
            cancelDeleteButton = CreateButton(overlay, "CancelButton", "取消", new Vector2(202f, -136f), new Vector2(84f, 36f), new Color(0.94f, 0.96f, 0.98f), new Color(0.05f, 0.12f, 0.24f));
            confirmDeleteButton = CreateButton(overlay, "ConfirmButton", "确认删除", new Vector2(300f, -136f), new Vector2(96f, 36f), new Color(0.95f, 0.18f, 0.14f), Color.white);
            confirmDeletePanel.SetActive(false);
        }

        private void ClearList()
        {
            if (contentRoot == null)
            {
                return;
            }

            for (var i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }
        }

        private void SetEmpty(bool visible, string message)
        {
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(visible);
                emptyText.text = message;
            }
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
            }
        }

        private static RectTransform CreateRect(string name, RectTransform parent, Vector2 anchor, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        private static Text CreateText(string name, RectTransform parent, string text, int fontSize, TextAnchor alignment, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var label = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = anchor;
            label.rectTransform.anchorMax = anchor;
            label.rectTransform.pivot = new Vector2(anchor.x == 0f ? 0f : 0.5f, 1f);
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
            label.text = text;
            label.font = MainUiTheme.BodyFont;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = new Color(0.2f, 0.255f, 0.333f);
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(RectTransform parent, string name, string text, Vector2 position, Vector2 size, Color background, Color textColor)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var img = buttonObject.GetComponent<Image>();
            img.color = background;
            img.sprite = UiThemeTokens.GetRoundedSprite(8);
            img.type = Image.Type.Sliced;

            var label = CreateText("Text", rect, text, 15, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero, size);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.color = textColor;
            var nav = buttonObject.GetComponent<Button>().navigation;
            nav.mode = Navigation.Mode.None;
            buttonObject.GetComponent<Button>().navigation = nav;
            return buttonObject.GetComponent<Button>();
        }
    }
}
