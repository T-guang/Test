using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 收集用户保存图纸的名称、覆盖确认和界面反馈。
    /// 本弹窗不遍历 Workspace、不拼装 JSON，真实文件名处理、覆盖判断和写入均由 SaveLoadService 完成；输入框文本不是图纸数据的权威来源。
    /// Initialize 采用先移除再绑定监听器的方式支持复用，修改后必须回归空名称、正常名称、重名覆盖、取消和重复打开。
    /// </summary>
    public sealed class SaveBlueprintDialog : MonoBehaviour
    {
        [SerializeField] private SaveLoadService saveLoadService;
        [SerializeField] private InputField nameInput;
        [SerializeField] private Text errorText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private GameObject overwriteConfirmPanel;
        [SerializeField] private Text overwriteConfirmText;
        [SerializeField] private Button overwriteConfirmButton;
        [SerializeField] private Button overwriteCancelButton;
        
        private string pendingOverwriteName;

        public static SaveBlueprintDialog Create(RectTransform parent, SaveLoadService service)
        {
            var root = new GameObject("SaveBlueprintDialog", typeof(RectTransform), typeof(Image), typeof(SaveBlueprintDialog));
            root.transform.SetParent(parent, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var overlay = root.GetComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.32f);

            var dialog = root.GetComponent<SaveBlueprintDialog>();
            dialog.saveLoadService = service;
            dialog.BuildUi(rect);
            root.SetActive(false);
            return dialog;
        }

        public void Initialize(SaveLoadService service)
        {
            saveLoadService = service;
            // 弹窗可能被反复创建或重新注入服务，先解除旧监听以避免一次确认触发多次保存。
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(Confirm);
                confirmButton.onClick.AddListener(Confirm);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(Hide);
                cancelButton.onClick.AddListener(Hide);
            }
            
            if (overwriteConfirmButton != null)
            {
                overwriteConfirmButton.onClick.RemoveListener(ConfirmOverwrite);
                overwriteConfirmButton.onClick.AddListener(ConfirmOverwrite);
            }
            
            if (overwriteCancelButton != null)
            {
                overwriteCancelButton.onClick.RemoveListener(CancelOverwrite);
                overwriteCancelButton.onClick.AddListener(CancelOverwrite);
            }
        }

        public void Show()
        {
            // 当前实现每次打开都会恢复默认名称并清除错误，不保留上一次未确认输入。
            gameObject.SetActive(true);
            if (nameInput != null)
            {
                nameInput.text = "未命名图纸";
                nameInput.Select();
                nameInput.ActivateInputField();
            }

            SetError(string.Empty);
            CancelOverwrite();
        }

        public void Hide()
        {
            CancelOverwrite();
            gameObject.SetActive(false);
        }

        private void Confirm()
        {
            var rawName = nameInput != null ? nameInput.text : string.Empty;
            var documentName = FilterFileName(rawName);
            if (string.IsNullOrWhiteSpace(documentName))
            {
                SetError("图纸名称不能为空。");
                return;
            }

            if (saveLoadService == null)
            {
                SetError("保存失败：保存服务未初始化。");
                return;
            }

            // 名称过滤只服务输入体验；最终目录、扩展名和覆盖语义仍由 SaveLoadService 统一处理。
            if (saveLoadService.SaveAs(documentName, false, out _, out var exists, out var error))
            {
                Hide();
                return;
            }
            
            if (exists)
            {
                ShowOverwriteConfirm(documentName);
                return;
            }

            SetError(string.IsNullOrWhiteSpace(error) ? "保存失败。" : error);
        }
        
        private void ShowOverwriteConfirm(string documentName)
        {
            pendingOverwriteName = documentName;
            if (overwriteConfirmText != null)
            {
                overwriteConfirmText.text = $"已存在同名图纸“{documentName}”，是否覆盖？";
            }
            if (overwriteConfirmPanel != null)
            {
                overwriteConfirmPanel.SetActive(true);
                overwriteConfirmPanel.transform.SetAsLastSibling();
            }
        }
        
        private void ConfirmOverwrite()
        {
            // 只有用户确认且仍保留待覆盖名称时才传递 overwrite=true，取消不会改动已存在文件。
            if (string.IsNullOrWhiteSpace(pendingOverwriteName) || saveLoadService == null)
            {
                CancelOverwrite();
                return;
            }
            
            if (saveLoadService.SaveAs(pendingOverwriteName, true, out _, out _, out var error))
            {
                Hide();
                return;
            }
            
            SetError(string.IsNullOrWhiteSpace(error) ? "保存失败。" : error);
            CancelOverwrite();
        }
        
        private void CancelOverwrite()
        {
            pendingOverwriteName = null;
            if (overwriteConfirmPanel != null)
            {
                overwriteConfirmPanel.SetActive(false);
            }
        }

        private void BuildUi(RectTransform root)
        {
            var panel = CreateRect("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(500f, 280f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = Color.white;
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(16);
            panelImage.type = Image.Type.Sliced;

            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.886f, 0.91f, 0.941f);
            outline.effectDistance = new Vector2(1, -1);

            var title = CreateText("Title", panel, "保存图纸", 20, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -28f), new Vector2(360f, 32f));
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(0.118f, 0.161f, 0.231f);
            title.font = MainUiTheme.TitleFont;

            var divider = CreateRect("Divider", panel, new Vector2(0f, 1f), new Vector2(500f, 1f));
            divider.pivot = new Vector2(0f, 1f);
            divider.anchoredPosition = new Vector2(0f, -56f);
            divider.gameObject.AddComponent<Image>().color = new Color(0.898f, 0.906f, 0.922f);

            CreateText("NameLabel", panel, "图纸名称", 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -92f), new Vector2(120f, 30f));
            nameInput = CreateInput(panel, new Vector2(24f, -128f), new Vector2(452f, 42f));
            
            errorText = CreateText("ErrorText", panel, string.Empty, 14, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -174f), new Vector2(452f, 30f));
            errorText.color = new Color(0.863f, 0.149f, 0.149f);

            confirmButton = CreateButton(panel, "ConfirmButton", "确认保存", new Vector2(272f, -220f), new Vector2(104f, 36f), new Color(0.145f, 0.388f, 0.922f), Color.white);
            cancelButton = CreateButton(panel, "CancelButton", "取消", new Vector2(388f, -220f), new Vector2(88f, 36f), Color.white, new Color(0.275f, 0.275f, 0.275f));
            var cancelOutline = cancelButton.gameObject.AddComponent<Outline>();
            cancelOutline.effectColor = new Color(0.824f, 0.824f, 0.824f);
            cancelOutline.effectDistance = new Vector2(1, -1);
            
            BuildOverwriteConfirmPanel(panel);
            Initialize(saveLoadService);
        }
        
        private void BuildOverwriteConfirmPanel(RectTransform parent)
        {
            var overlay = CreateRect("OverwriteConfirmPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(420f, 190f));
            var img = overlay.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 1f);
            img.sprite = UiThemeTokens.GetRoundedSprite(16);
            img.type = Image.Type.Sliced;
            overwriteConfirmPanel = overlay.gameObject;

            CreateText("Title", overlay, "覆盖确认", 20, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(240f, 32f));
            overwriteConfirmText = CreateText("Message", overlay, string.Empty, 15, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(24f, -66f), new Vector2(372f, 64f));
            overwriteCancelButton = CreateButton(overlay, "CancelButton", "取消", new Vector2(202f, -136f), new Vector2(84f, 36f), new Color(0.94f, 0.96f, 0.98f), new Color(0.05f, 0.12f, 0.24f));
            overwriteConfirmButton = CreateButton(overlay, "ConfirmButton", "覆盖保存", new Vector2(300f, -136f), new Vector2(96f, 36f), new Color(0.95f, 0.18f, 0.14f), Color.white);
            overwriteConfirmPanel.SetActive(false);
        }

        private void SetError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
            }
        }

        private static string FilterFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }).ToArray();
            return new string(value.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim();
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
            label.rectTransform.pivot = new Vector2(0f, 1f);
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

        private static InputField CreateInput(RectTransform parent, Vector2 position, Vector2 size)
        {
            var inputObject = new GameObject("NameInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            inputObject.transform.SetParent(parent, false);
            var rect = inputObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var img = inputObject.GetComponent<Image>();
            img.color = new Color(0.973f, 0.98f, 0.988f);
            img.sprite = UiThemeTokens.GetRoundedSprite(8);
            img.type = Image.Type.Sliced;

            var outline = inputObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.796f, 0.835f, 0.882f);
            outline.effectDistance = new Vector2(1, -1);

            var text = CreateText("Text", rect, string.Empty, 16, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(12f, 0f), new Vector2(size.x - 24f, size.y));
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            text.rectTransform.offsetMax = new Vector2(-12f, 0f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var placeholder = CreateText("Placeholder", rect, "请输入图纸名称", 16, TextAnchor.MiddleLeft, Vector2.zero, new Vector2(12f, 0f), new Vector2(size.x - 24f, size.y));
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(12f, 0f);
            placeholder.rectTransform.offsetMax = new Vector2(-12f, 0f);
            placeholder.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            placeholder.color = new Color(0.45f, 0.52f, 0.62f);

            var input = inputObject.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = InputField.LineType.SingleLine;
            return input;
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
