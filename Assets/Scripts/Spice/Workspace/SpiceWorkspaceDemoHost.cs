using System;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// Demo 模拟电路页面中的正式 SPICE 宿主。它只消费场景序列化的局部 Root 绑定，
    /// 初始化独立工作区一次；不创建 Canvas、EventSystem、Camera 或页面外壳。
    /// </summary>
    public sealed class SpiceWorkspaceDemoHost : MonoBehaviour
    {
        [SerializeField] private SpiceWorkspaceViewBindings viewBindings;
        [SerializeField] private SpiceWorkspaceController workspaceController;

        private bool initialized;

        public SpiceWorkspaceController Controller => workspaceController;
        public bool IsInitialized => initialized;

        /// <summary>仅供场景装配器写入已创建的正式引用；不会自动创建或查找 Root。</summary>
        public void Configure(SpiceWorkspaceViewBindings bindings, SpiceWorkspaceController controller)
        {
            if (initialized)
            {
                throw new InvalidOperationException("Spice Demo host cannot be configured after initialization.");
            }

            viewBindings = bindings;
            workspaceController = controller;
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// 场景加载时调用一次。模式切换仅显隐 Root，不会再次绑定或重建 SPICE 工作区。
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (viewBindings == null || workspaceController == null)
            {
                throw new InvalidOperationException(
                    "Spice Demo host is missing serialized workspace bindings or controller.");
            }

            viewBindings.Validate();
            workspaceController.Initialize(viewBindings);
            SpiceWorkspacePresentationAdapter.Apply(viewBindings);
            initialized = true;
        }
    }

    internal static class SpiceWorkspacePresentationAdapter
    {
        private const float ToolbarButtonTop = -20f;
        private const float ToolbarButtonBottom = 20f;
        private const float PaletteCardWidth = 120f;
        private const float PaletteCardHeight = 116f;
        private const float PaletteCardGap = 12f;

        public static void Apply(SpiceWorkspaceViewBindings bindings)
        {
            if (bindings == null) return;

            StyleToolbarButton(bindings.RunButton, "运行计算", true, false, 18f, 132f);
            StyleToolbarButton(bindings.RotateButton, "旋转", false, false, 144f, 238f);
            StyleToolbarButton(bindings.DeleteButton, "删除", false, true, 250f, 344f);
            StyleToolbarButton(bindings.ClearButton, "清空", false, true, 356f, 450f);

            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Resistor, "电阻", "1 kΩ", 1, 0);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Capacitor, "电容", "1 μF", 0, 1);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Ground, "接地", "GND", 0, 2);
        }

        private static void StyleToolbarButton(Button button, string label, bool primary, bool danger, float left, float right)
        {
            if (button == null) return;

            Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, ToolbarButtonTop), new Vector2(right, ToolbarButtonBottom));

            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(10);
            image.type = Image.Type.Sliced;
            image.color = primary ? MainUiTheme.PrimaryBlue : Color.white;
            button.targetGraphic = image;

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : danger ? MainUiTheme.DangerBorder : MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                MainUiTheme.ApplyTextRole(text, primary ? MainUiTheme.UiTextRole.ToolbarPrimaryButton : danger ? MainUiTheme.UiTextRole.ToolbarDangerButton : MainUiTheme.UiTextRole.ToolbarButton);
                text.color = primary ? Color.white : danger ? MainUiTheme.DangerRed : MainUiTheme.NormalText;
                Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? MainUiTheme.Hex("1D4ED8") : danger ? MainUiTheme.Hex("FEF2F2") : MainUiTheme.Hex("F8FAFC");
            colors.pressedColor = primary ? MainUiTheme.Hex("1E40AF") : danger ? MainUiTheme.Hex("FEE2E2") : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = MainUiTheme.Hex("E5E7EB");
            button.colors = colors;
        }

        private static void StylePaletteCard(RectTransform paletteRoot, SpiceComponentKind kind, string title, string summary, int column, int row)
        {
            if (paletteRoot == null) return;

            var cardTransform = paletteRoot.Find(kind + "Card") as RectTransform;
            if (cardTransform == null) return;

            var left = 16f + column * (PaletteCardWidth + PaletteCardGap);
            var top = -66f - row * (PaletteCardHeight + PaletteCardGap);
            Anchor(cardTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, top - PaletteCardHeight), new Vector2(left + PaletteCardWidth, top));

            var image = cardTransform.GetComponent<Image>() ?? cardTransform.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;
            var interaction = cardTransform.GetComponent<SpiceWorkspacePaletteCardPresentation>() ?? cardTransform.gameObject.AddComponent<SpiceWorkspacePaletteCardPresentation>();
            interaction.Initialize(image);

            var outline = cardTransform.GetComponent<Outline>() ?? cardTransform.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);

            RemoveChild(cardTransform, "Symbol");
            CreatePaletteSymbol(cardTransform, kind);
            StyleCardText(cardTransform, "Title", title, new Vector2(8f, 32f), new Vector2(-8f, 58f), MainUiTheme.DeepText, MainUiTheme.UiTextRole.PaletteCardTitle);
            StyleCardText(cardTransform, "Summary", summary, new Vector2(8f, 8f), new Vector2(-8f, 32f), MainUiTheme.MutedText, MainUiTheme.UiTextRole.MetaText);
        }

        private static void StyleCardText(RectTransform card, string name, string value, Vector2 offsetMin, Vector2 offsetMax, Color color, MainUiTheme.UiTextRole role)
        {
            var textTransform = card.Find(name) as RectTransform;
            var text = textTransform != null ? textTransform.GetComponent<Text>() : null;
            if (text == null) return;

            text.text = value;
            MainUiTheme.ApplyTextRole(text, role);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            Anchor(text.rectTransform, Vector2.zero, new Vector2(1f, 0f), offsetMin, offsetMax);
        }

        private static void CreatePaletteSymbol(RectTransform card, SpiceComponentKind kind)
        {
            var symbol = new GameObject("Symbol", typeof(RectTransform)).GetComponent<RectTransform>();
            symbol.SetParent(card, false);
            Anchor(symbol, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -70f), new Vector2(-18f, -14f));

            switch (kind)
            {
                case SpiceComponentKind.DcVoltageSource:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(36f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(66f, 0f), new Vector2(94f, 0f));
                    AddLine(symbol, "Source", new Vector2(36f, -15f), new Vector2(66f, 15f), 3f);
                    AddText(symbol, "Plus", "+", new Vector2(44f, 6f), 13);
                    AddText(symbol, "Minus", "-", new Vector2(58f, -7f), 13);
                    break;
                case SpiceComponentKind.Resistor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(28f, 0f));
                    AddLine(symbol, "Body", new Vector2(28f, 0f), new Vector2(74f, 0f), 10f);
                    AddLine(symbol, "LeadRight", new Vector2(74f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.Capacitor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(42f, 0f));
                    AddLine(symbol, "PlateA", new Vector2(42f, -15f), new Vector2(42f, 15f));
                    AddLine(symbol, "PlateB", new Vector2(58f, -15f), new Vector2(58f, 15f));
                    AddLine(symbol, "LeadRight", new Vector2(58f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.Inductor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(24f, 0f));
                    AddLine(symbol, "CoilA", new Vector2(24f, 0f), new Vector2(36f, 12f));
                    AddLine(symbol, "CoilB", new Vector2(36f, 12f), new Vector2(48f, -12f));
                    AddLine(symbol, "CoilC", new Vector2(48f, -12f), new Vector2(60f, 12f));
                    AddLine(symbol, "CoilD", new Vector2(60f, 12f), new Vector2(72f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(72f, 0f), new Vector2(94f, 0f));
                    break;
                default:
                    AddLine(symbol, "Stem", new Vector2(51f, 20f), new Vector2(51f, -4f));
                    AddLine(symbol, "GroundA", new Vector2(34f, -4f), new Vector2(68f, -4f));
                    AddLine(symbol, "GroundB", new Vector2(39f, -12f), new Vector2(63f, -12f));
                    AddLine(symbol, "GroundC", new Vector2(44f, -20f), new Vector2(58f, -20f));
                    break;
            }
        }

        private static void AddLine(RectTransform parent, string name, Vector2 start, Vector2 end, float thickness = 3f)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = MainUiTheme.PrimaryBlue;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            image.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var delta = end - start;
            image.rectTransform.anchoredPosition = start + delta * 0.5f;
            image.rectTransform.sizeDelta = new Vector2(Mathf.Max(2f, delta.magnitude), thickness);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * Mathf.Atan2(delta.y, delta.x));
        }

        private static void AddText(RectTransform parent, string name, string value, Vector2 position, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.MetaTextBold);
            text.text = value;
            text.fontSize = size;
            text.color = MainUiTheme.PrimaryBlue;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(18f, 18f);
        }

        private static void RemoveChild(RectTransform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) UnityEngine.Object.Destroy(child.gameObject);
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }

    internal sealed class SpiceWorkspacePaletteCardPresentation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image image;
        private bool hovered;
        private bool pressed;

        public void Initialize(Image target)
        {
            image = target;
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            Apply();
        }

        private void Apply()
        {
            if (image == null) return;
            image.color = pressed ? MainUiTheme.Hex("DBEAFE") : hovered ? MainUiTheme.Hex("F8FBFF") : Color.white;
        }
    }
}
