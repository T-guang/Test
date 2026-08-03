using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 在画布锁定时统一提示模板加载和进入练习已被拒绝。
    /// 该提示只负责当前页面的可见反馈，不改变画布、页面、模板会话或练习会话；
    /// 具体入口仍须在清空和生成前完成锁定判断。
    /// </summary>
    public sealed class LockedCanvasLoadDialog : MonoBehaviour
    {
        public const string Title = "画布已锁定";
        public const string Message = "当前画布处于锁定状态，无法加载新图纸。\n请先返回模拟电路页面解锁画布后再试。";
        public const string AcknowledgeLabel = "我知道了";

        private static LockedCanvasLoadDialog instance;

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

        /// <summary>
        /// 复用同一个遮罩，避免用户连续点击多个加载入口时叠加弹窗和遮罩。
        /// </summary>
        public static void Show()
        {
            if (instance == null)
            {
                var canvas = FindObjectOfType<Canvas>();
                if (canvas == null)
                {
                    return;
                }

                var dialogObject = new GameObject(
                    "LockedCanvasLoadDialog",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(LockedCanvasLoadDialog));
                dialogObject.transform.SetParent(canvas.transform, false);
                instance = dialogObject.GetComponent<LockedCanvasLoadDialog>();
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
            panelRect.sizeDelta = new Vector2(460f, 250f);

            var panelImage = panelObject.GetComponent<Image>();
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(10);
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;

            var outline = panelObject.AddComponent<Outline>();
            outline.effectColor = UiThemeTokens.BorderColor;
            outline.effectDistance = new Vector2(1f, -1f);

            var title = CreateText("Title", panelObject.transform, Title, MainUiTheme.TitleFont, 20, TextAnchor.MiddleLeft, MainUiTheme.Hex("1E293B"));
            title.fontStyle = FontStyle.Bold;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(24f, -24f), new Vector2(-24f, -56f));

            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(panelObject.transform, false);
            var dividerRect = dividerObject.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 1f);
            dividerRect.anchorMax = new Vector2(1f, 1f);
            dividerRect.offsetMin = new Vector2(24f, -57f);
            dividerRect.offsetMax = new Vector2(-24f, -56f);
            dividerObject.GetComponent<Image>().color = UiThemeTokens.BorderColor;

            var message = CreateText("Message", panelObject.transform, Message, MainUiTheme.DenseUiFont, 15, TextAnchor.UpperLeft, MainUiTheme.Hex("475569"));
            message.lineSpacing = 1.25f;
            SetRect(message.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(28f, 72f), new Vector2(-28f, -74f));

            var acknowledge = CreateButton(panelObject.transform, "AcknowledgeButton", AcknowledgeLabel, MainUiTheme.Hex("2563EB"), Color.white);
            var buttonRect = acknowledge.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 0f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-24f, 31f);
            buttonRect.sizeDelta = new Vector2(112f, 38f);
            acknowledge.onClick.AddListener(Hide);
        }

        private void Hide()
        {
            gameObject.SetActive(false);
        }

        private static Text CreateText(string name, Transform parent, string content, Font font, int fontSize, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Color background, Color textColor)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = background;

            var text = CreateText("Text", buttonObject.transform, label, MainUiTheme.UiFontBold, 15, TextAnchor.MiddleCenter, textColor);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
