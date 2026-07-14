using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 负责显示全局退出确认弹窗，并在确认后按运行环境停止 Play Mode 或退出 Standalone。
    /// 不负责自动保存、页面切换或 Workspace 修改；同一 Canvas 只复用一个实例，重复打开时不得累计按钮监听。
    /// </summary>
    public sealed class ExitApplicationDialog : MonoBehaviour
    {
        private static ExitApplicationDialog instance;

        private void Awake()
        {
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public static void Show(Transform canvasTransform)
        {
            if (canvasTransform == null)
            {
                return;
            }

            if (instance == null)
            {
                var dialogObject = new GameObject(
                    "ExitApplicationDialog",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(ExitApplicationDialog));
                dialogObject.transform.SetParent(canvasTransform, false);
                instance = dialogObject.GetComponent<ExitApplicationDialog>();
                instance.BuildUi();
            }

            instance.transform.SetAsLastSibling();
            instance.gameObject.SetActive(true);
        }

        private void BuildUi()
        {
            var overlayRect = GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            var overlayImage = GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0.45f);
            overlayImage.raycastTarget = true;

            var panelObject = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(transform, false);
            var panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(440f, 248f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(10);
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;

            var panelOutline = panelObject.AddComponent<Outline>();
            panelOutline.effectColor = UiThemeTokens.BorderColor;
            panelOutline.effectDistance = new Vector2(1f, -1f);

            var header = CreateContainer("Header", panelObject.transform);
            SetTopAnchoredRect(header, 56f);

            var title = CreateText(header, "Title", "退出系统", MainUiTheme.TitleFont, 20, TextAnchor.MiddleLeft, MainUiTheme.Hex("1E293B"));
            SetFixedRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(240f, 32f));

            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(header, false);
            var divider = dividerObject.GetComponent<RectTransform>();
            divider.anchorMin = new Vector2(0f, 0f);
            divider.anchorMax = new Vector2(1f, 0f);
            divider.offsetMin = new Vector2(24f, 0f);
            divider.offsetMax = new Vector2(-24f, 1f);
            dividerObject.GetComponent<Image>().color = UiThemeTokens.BorderColor;

            var body = CreateContainer("Body", panelObject.transform);
            StretchBetweenHeaderAndFooter(body, 56f, 64f, 24f);

            var message = CreateText(
                body,
                "Message",
                "确定退出系统吗？\n未保存的图纸改动将不会保留。",
                MainUiTheme.DenseUiFont,
                15,
                TextAnchor.UpperLeft,
                MainUiTheme.Hex("475569"));
            message.lineSpacing = 1.2f;
            message.rectTransform.anchorMin = Vector2.zero;
            message.rectTransform.anchorMax = Vector2.one;
            message.rectTransform.offsetMin = Vector2.zero;
            message.rectTransform.offsetMax = Vector2.zero;

            var footer = CreateContainer("Footer", panelObject.transform);
            SetBottomAnchoredRect(footer, 64f);

            var cancelButton = CreateButton(footer, "CancelButton", "取消", MainUiTheme.Hex("F8FAFC"), MainUiTheme.Hex("334155"));
            SetFixedRect(cancelButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-144f, 0f), new Vector2(96f, 36f));
            cancelButton.onClick.AddListener(Hide);

            var confirmButton = CreateButton(footer, "ConfirmButton", "退出系统", MainUiTheme.Hex("DC2626"), Color.white);
            SetFixedRect(confirmButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(104f, 36f));
            confirmButton.onClick.AddListener(QuitApplication);
        }

        private void Hide()
        {
            gameObject.SetActive(false);
        }

        private static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int fontSize, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string content, Color background, Color textColor)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = background;

            var label = CreateText(buttonObject.transform, "Text", content, MainUiTheme.UiFontBold, 15, TextAnchor.MiddleCenter, textColor);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static RectTransform CreateContainer(string name, Transform parent)
        {
            var containerObject = new GameObject(name, typeof(RectTransform));
            containerObject.transform.SetParent(parent, false);
            return containerObject.GetComponent<RectTransform>();
        }

        private static void SetTopAnchoredRect(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void SetBottomAnchoredRect(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(0f, height);
        }

        private static void StretchBetweenHeaderAndFooter(RectTransform rect, float headerHeight, float footerHeight, float horizontalPadding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalPadding, footerHeight);
            rect.offsetMax = new Vector2(-horizontalPadding, -headerHeight);
        }

        private static void SetFixedRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }
    }
}
