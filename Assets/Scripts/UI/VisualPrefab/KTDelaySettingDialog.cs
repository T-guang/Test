using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.UI;

namespace ElectricalSim.Core
{
    // KT 延时设置弹窗只编辑当前实例的 delaySeconds 参数并把确认操作交回调用方；它不推进计时器、
    // 不修改 RuntimeStateManager 的阶段，也不把屏幕上的倒计时当作可保存的电路事实。场景中已有弹窗实例时会复用它，
    // 因此每次 Show 都必须重新绑定 component、workspace 和 callback，避免上一只 KT 的操作误写到新的实例。
    public sealed class KTDelaySettingDialog : MonoBehaviour
    {
        private const string DelayParameterKey = "delaySeconds";

        private CircuitComponent component;
        private WorkspaceController workspace;
        private System.Action<float> applyDelay;
        private Text currentText;
        private Text errorText;
        private InputField inputField;
        private readonly System.Collections.Generic.List<QuickPreset> quickPresets = new System.Collections.Generic.List<QuickPreset>();

        public static void Show(CircuitComponent target, WorkspaceController owner, System.Action<float> applyCallback)
        {
            if (target == null)
            {
                return;
            }

            var existing = FindObjectOfType<KTDelaySettingDialog>();
            if (existing != null)
            {
                existing.Initialize(target, owner, applyCallback);
                existing.transform.SetAsLastSibling();
                return;
            }

            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindObjectOfType<Canvas>();
            }

            if (canvas == null)
            {
                return;
            }

            var root = new GameObject("KTDelaySettingDialog", typeof(RectTransform), typeof(Image), typeof(KTDelaySettingDialog));
            root.transform.SetParent(canvas.transform, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var backdrop = root.GetComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.36f);
            backdrop.raycastTarget = true;

            var dialog = root.GetComponent<KTDelaySettingDialog>();
            dialog.BuildContent(rect);
            dialog.Initialize(target, owner, applyCallback);
        }

        private void Initialize(CircuitComponent target, WorkspaceController owner, System.Action<float> applyCallback)
        {
            component = target;
            workspace = owner;
            applyDelay = applyCallback;
            gameObject.SetActive(true);
            RefreshCurrentText();
        }

        private void BuildContent(RectTransform root)
        {
            var panel = CreateRect("Panel", root, new Vector2(0.5f, 0.5f), new Vector2(480f, 324f));
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = UiThemeTokens.GetRoundedSprite(12);
            panelImage.type = Image.Type.Sliced;
            panelImage.color = Color.white;
            panelImage.raycastTarget = true;
            var panelOutline = panel.gameObject.AddComponent<Outline>();
            panelOutline.effectColor = MainUiTheme.Hex("E2E8F0");
            panelOutline.effectDistance = new Vector2(1f, -1f);

            CreateText("Title", panel, "设置延时时间", 20, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(24f, -30f), new Vector2(300f, 36f));
            var close = CreateButton(panel, "CloseButton", "×", new Vector2(438f, -30f), new Vector2(36f, 36f), MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("334155"));
            StyleButtonLabel(close, MainUiTheme.UiFontBold, 20);
            close.onClick.AddListener(Close);

            var currentLabel = CreateText("CurrentLabel", panel, "当前设置：", 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(24f, -78f), new Vector2(96f, 30f));
            currentLabel.color = MainUiTheme.Hex("475569");
            currentText = CreateText("CurrentText", panel, string.Empty, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(120f, -78f), new Vector2(300f, 30f));

            CreateQuickButton(panel, "Quick1", "1秒", 1f, new Vector2(62f, -128f));
            CreateQuickButton(panel, "Quick3", "3秒", 3f, new Vector2(156f, -128f));
            CreateQuickButton(panel, "Quick5", "5秒", 5f, new Vector2(250f, -128f));
            CreateQuickButton(panel, "Quick10", "10秒", 10f, new Vector2(344f, -128f));

            CreateText("InputLabel", panel, "自定义时间", 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(24f, -178f), new Vector2(120f, 30f));
            inputField = CreateInput(panel, new Vector2(286f, -178f), new Vector2(174f, 40f));

            errorText = CreateText("ErrorText", panel, string.Empty, 13, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(24f, -222f), new Vector2(420f, 28f));
            errorText.color = new Color(0.85f, 0.12f, 0.08f);

            var cancel = CreateButton(panel, "CancelButton", "关闭", new Vector2(318f, -280f), new Vector2(92f, 40f), MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("334155"));
            var apply = CreateButton(panel, "ApplyButton", "设置", new Vector2(420f, -280f), new Vector2(92f, 40f), MainUiTheme.Hex("2563EB"), Color.white);
            StyleButtonLabel(apply, MainUiTheme.UiFontBold, 15);
            apply.onClick.AddListener(ApplyCustomInput);
            cancel.onClick.AddListener(Close);
        }

        private void CreateQuickButton(RectTransform parent, string name, string label, float value, Vector2 position)
        {
            var button = CreateButton(parent, name, label, position, new Vector2(76f, 40f), MainUiTheme.Hex("EFF6FF"), MainUiTheme.Hex("2563EB"));
            quickPresets.Add(new QuickPreset { value = value, button = button });
            button.onClick.AddListener(() =>
            {
                ApplyDelay(value);
                RefreshCurrentText();
            });
        }

        private void ApplyCustomInput()
        {
            if (inputField == null || !KTTimerVisualController.TryParseDelaySeconds(inputField.text, out var seconds))
            {
                if (errorText != null)
                {
                    errorText.text = "请输入 3、3秒、00:03 或 0:03 这样的格式。";
                }
                return;
            }

            ApplyDelay(seconds);
            Close();
        }

        private void ApplyDelay(float value)
        {
            // UI 只转发已解析的值；真正参数写入、运行态失效和视觉刷新由调用方/组件正式路径负责，不能仅更新弹窗文本制造已应用假象。
            applyDelay?.Invoke(value);
            workspace?.SetStatus("时间继电器延时时间已设置为 " + KTTimerVisualController.FormatSeconds(Mathf.RoundToInt(Mathf.Clamp(value, 0f, 10f))) + "。");
        }

        private void RefreshCurrentText()
        {
            var value = 3f;
            var parameter = component != null ? component.GetParameter(DelayParameterKey) : null;
            if (parameter != null)
            {
                value = Mathf.Clamp(parameter.value, 0f, 10f);
            }

            var seconds = Mathf.RoundToInt(value);
            if (currentText != null)
            {
                currentText.text = KTTimerVisualController.FormatSeconds(seconds) + "    总计 " + seconds + " 秒";
            }

            if (inputField != null)
            {
                inputField.text = KTTimerVisualController.FormatSeconds(seconds);
            }

            if (errorText != null)
            {
                errorText.text = string.Empty;
            }

            RefreshQuickPresetState(seconds);
        }

        private void Close()
        {
            Destroy(gameObject);
        }

        private static RectTransform CreateRect(string name, RectTransform parent, Vector2 anchor, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static Text CreateText(string name, RectTransform parent, string text, int fontSize, FontStyle style, TextAnchor alignment, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Normal;
            label.alignment = alignment;
            label.color = MainUiTheme.Hex("1F2937");
            label.raycastTarget = false;
            label.resizeTextForBestFit = false;
            return label;
        }

        private static InputField CreateInput(RectTransform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject("DelayInput", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("CBD5E1");
            outline.effectDistance = new Vector2(1f, -1f);

            var text = CreateText("Text", rect, string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(8f, 0f), new Vector2(size.x - 16f, size.y));
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            text.rectTransform.offsetMax = new Vector2(-8f, 0f);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.targetGraphic = image;
            input.contentType = InputField.ContentType.Standard;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button CreateButton(RectTransform parent, string name, string text, Vector2 position, Vector2 size, Color background, Color textColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = background;

            var label = CreateText("Text", rect, text, 15, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, size);
            label.color = textColor;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private void RefreshQuickPresetState(int currentSeconds)
        {
            for (var i = 0; i < quickPresets.Count; i++)
            {
                var preset = quickPresets[i];
                if (preset.button == null) continue;
                var selected = Mathf.Approximately(preset.value, currentSeconds);
                var image = preset.button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("EFF6FF");
                }
                var label = preset.button.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.font = selected ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                    label.fontStyle = FontStyle.Normal;
                    label.fontSize = 15;
                    label.color = selected ? Color.white : MainUiTheme.Hex("2563EB");
                    label.resizeTextForBestFit = false;
                }
            }
        }

        private static void StyleButtonLabel(Button button, Font font, int fontSize)
        {
            var label = button != null ? button.GetComponentInChildren<Text>() : null;
            if (label == null) return;
            label.font = font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Normal;
            label.resizeTextForBestFit = false;
        }

        private sealed class QuickPreset
        {
            public float value;
            public Button button;
        }
    }
}
