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
        private const float AssistantSectionHeaderHeight = 42f;
        private const float ParameterSectionHeight = 146f;
        private const float ResultSectionHeight = 286f;
        private const float NetlistSectionHeight = 280f;
        private const float DiagnosticSectionHeight = 112f;
        private const float AssistantSectionGap = 8f;
        private const float ParameterApplyButtonWidth = 120f;
        private const float ParameterApplyButtonHeight = 34f;

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

            StyleAssistant(bindings);
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

        private static void StyleAssistant(SpiceWorkspaceViewBindings bindings)
        {
            var root = bindings.AssistantRoot;
            if (root == null) return;

            var image = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = MainUiTheme.PanelBackground;
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            var outline = root.GetComponent<Outline>() ?? root.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(-1f, 0f);

            var title = root.Find("AssistantTitle") as RectTransform;
            if (title != null)
            {
                Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
                var titleText = title.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "仿真助手";
                    MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.InspectorTitle);
                    titleText.color = MainUiTheme.DeepText;
                    titleText.alignment = TextAnchor.MiddleLeft;
                }
            }

            LayoutAssistantSections(bindings);
            StyleParameterSection(bindings.ParameterRoot);
            StyleTextSection(bindings.ResultRoot, "ResultHeader", "计算结果", "ResultScrollView", "ResultText", "尚无计算结果\n完成接线后点击运行计算", false, MainUiTheme.NormalText);
            StyleNetlistSection(bindings.NetlistRoot);
            StyleTextSection(bindings.DiagnosticRoot, "DiagnosticHeader", "诊断信息", "DiagnosticScrollView", "DiagnosticText", "暂无诊断信息", false, MainUiTheme.DangerRed);
        }

        private static void LayoutAssistantSections(SpiceWorkspaceViewBindings bindings)
        {
            Anchor(bindings.ParameterRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -54f - ParameterSectionHeight), new Vector2(-12f, -54f));

            var resultTop = -54f - ParameterSectionHeight - AssistantSectionGap;
            Anchor(bindings.ResultRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, resultTop - ResultSectionHeight), new Vector2(-12f, resultTop));

            var netlistTop = resultTop - ResultSectionHeight - AssistantSectionGap;
            Anchor(bindings.NetlistRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, netlistTop - NetlistSectionHeight), new Vector2(-12f, netlistTop));

            Anchor(bindings.DiagnosticRoot, Vector2.zero, new Vector2(1f, 0f), new Vector2(12f, 18f), new Vector2(-12f, 18f + DiagnosticSectionHeight));
        }

        private static void StyleParameterSection(RectTransform section)
        {
            if (section == null) return;

            StyleSectionPanel(section);
            var header = EnsureHeader(section, "ParameterHeader", "参数设置");
            var title = section.Find("ParameterTitle") as RectTransform;
            if (title != null)
            {
                title.SetParent(header, false);
                Stretch(title, new Vector2(14f, 0f), new Vector2(-14f, 0f));
                var titleText = title.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "参数设置";
                    MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.InspectorCardTitle);
                    titleText.alignment = TextAnchor.MiddleLeft;
                    titleText.color = MainUiTheme.SecondaryText;
                }
            }

            var subtitle = EnsureText(section, "ParameterSubtitle", "请选择画布中的元件以编辑参数", MainUiTheme.MutedText, MainUiTheme.UiTextRole.InspectorBody);
            Anchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -80f), new Vector2(-14f, -48f));

            var input = section.Find("ParameterInput") as RectTransform;
            var unit = section.Find("Unit") as RectTransform;
            var apply = section.Find("Apply") as RectTransform;
            if (input != null)
            {
                Anchor(input, new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(14f, -122f), new Vector2(-4f, -88f));
                StyleInput(input.GetComponent<InputField>());
            }
            if (unit != null)
            {
                Anchor(unit, new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -122f), new Vector2(-14f, -88f));
                StyleSmallButton(unit.GetComponent<Button>(), false);
            }
            if (apply != null)
            {
                Anchor(apply, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f - ParameterApplyButtonWidth, -164f), new Vector2(-14f, -164f + ParameterApplyButtonHeight));
                StyleSmallButton(apply.GetComponent<Button>(), true);
            }

            var presenter = section.GetComponent<SpiceAssistantParameterPresentation>() ?? section.gameObject.AddComponent<SpiceAssistantParameterPresentation>();
            presenter.Initialize(title != null ? title.GetComponent<Text>() : null, subtitle, input != null ? input.GetComponent<InputField>() : null, unit != null ? unit.GetComponent<Button>() : null, apply != null ? apply.gameObject : null);
        }

        private static void StyleNetlistSection(RectTransform section)
        {
            if (section == null) return;

            StyleSectionPanel(section);
            var header = section.Find("NetlistHeader") as RectTransform;
            if (header != null)
            {
                StyleHeader(header, "生成网表");
                var title = header.Find("Title") as RectTransform;
                if (title != null) Anchor(title, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-142f, -4f));
                var status = header.Find("Status") as RectTransform;
                if (status != null)
                {
                    Anchor(status, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(14f, 4f), new Vector2(-14f, 0f));
                    var text = status.GetComponent<Text>();
                    if (text != null)
                    {
                        MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.MetaText);
                        text.color = MainUiTheme.MutedText;
                    }
                }

                StyleSmallButton((header.Find("Toggle") as RectTransform)?.GetComponent<Button>(), false);
                StyleSmallButton((header.Find("Copy") as RectTransform)?.GetComponent<Button>(), false);
            }

            var view = StyleScrollableText(section, "NetlistScrollView", "NetlistText", true, MainUiTheme.NormalText);
            if (view != null && string.IsNullOrWhiteSpace(view.text)) view.text = "尚未生成网表";
        }

        private static void StyleTextSection(RectTransform section, string headerName, string title, string scrollName, string textName, string emptyText, bool monospace, Color color)
        {
            if (section == null) return;

            StyleSectionPanel(section);
            var header = section.Find(headerName) as RectTransform;
            if (header != null) StyleHeader(header, title);

            var text = StyleScrollableText(section, scrollName, textName, monospace, color);
            if (text != null && string.IsNullOrWhiteSpace(text.text)) text.text = emptyText;
        }

        private static void StyleSectionPanel(RectTransform section)
        {
            var image = section.GetComponent<Image>() ?? section.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;

            var outline = section.GetComponent<Outline>() ?? section.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static RectTransform EnsureHeader(RectTransform section, string name, string title)
        {
            var header = section.Find(name) as RectTransform;
            if (header == null)
            {
                header = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                header.SetParent(section, false);
            }

            StyleHeader(header, title);
            header.SetAsFirstSibling();
            return header;
        }

        private static void StyleHeader(RectTransform header, string title)
        {
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -AssistantSectionHeaderHeight), new Vector2(-10f, 0f));
            var image = header.GetComponent<Image>() ?? header.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = MainUiTheme.Hex("F8FBFF");

            var text = header.Find("Title") as RectTransform;
            if (text == null)
            {
                text = new GameObject("Title", typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
                text.SetParent(header, false);
            }
            Stretch(text, new Vector2(14f, 0f), new Vector2(-14f, 0f));
            var label = text.GetComponent<Text>();
            if (label != null)
            {
                label.text = title;
                MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.InspectorCardTitle);
                label.alignment = TextAnchor.MiddleLeft;
                label.color = MainUiTheme.SecondaryText;
            }
        }

        private static Text StyleScrollableText(RectTransform section, string scrollName, string textName, bool monospace, Color color)
        {
            var scrollRect = section.Find(scrollName) as RectTransform;
            if (scrollRect == null) return null;

            Anchor(scrollRect, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -50f));
            var scrollImage = scrollRect.GetComponent<Image>() ?? scrollRect.gameObject.AddComponent<Image>();
            scrollImage.color = MainUiTheme.Hex("FBFDFF");
            scrollImage.sprite = UiThemeTokens.GetRoundedSprite(6);
            scrollImage.type = Image.Type.Sliced;

            var scroll = scrollRect.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }

            var textRect = scrollRect.Find("Viewport/Content/" + textName) as RectTransform;
            var text = textRect != null ? textRect.GetComponent<Text>() : null;
            if (text != null)
            {
                if (monospace)
                {
                    text.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, 14);
                    text.fontSize = 14;
                    text.fontStyle = FontStyle.Normal;
                }
                else
                {
                    MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.InspectorBody);
                }
                text.color = color;
                text.alignment = TextAnchor.UpperLeft;
                text.lineSpacing = monospace ? 1.1f : 1.28f;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return text;
        }

        private static Text EnsureText(RectTransform parent, string name, string value, Color color, MainUiTheme.UiTextRole role)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
                rect.SetParent(parent, false);
            }

            var text = rect.GetComponent<Text>();
            text.text = value;
            MainUiTheme.ApplyTextRole(text, role);
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static void StyleInput(InputField input)
        {
            if (input == null) return;
            var image = input.GetComponent<Image>() ?? input.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = input.GetComponent<Outline>() ?? input.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);
            if (input.textComponent != null)
            {
                MainUiTheme.ApplyTextRole(input.textComponent, MainUiTheme.UiTextRole.SearchInputText);
                input.textComponent.alignment = TextAnchor.MiddleLeft;
                input.textComponent.color = MainUiTheme.NormalText;
            }
        }

        private static void StyleSmallButton(Button button, bool primary)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.FilterButton;
            button.targetGraphic = image;
            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                MainUiTheme.ApplyTextRole(text, primary ? MainUiTheme.UiTextRole.InspectorButton : MainUiTheme.UiTextRole.FilterText);
                text.alignment = TextAnchor.MiddleCenter;
                text.color = primary ? Color.white : MainUiTheme.SecondaryText;
                Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            }
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

    internal sealed class SpiceAssistantParameterPresentation : MonoBehaviour
    {
        private Text title;
        private Text subtitle;
        private InputField input;
        private Button unitButton;
        private GameObject applyButton;
        private string lastTitle;
        private bool lastInteractable;

        public void Initialize(Text titleText, Text subtitleText, InputField parameterInput, Button unit, GameObject apply)
        {
            title = titleText;
            subtitle = subtitleText;
            input = parameterInput;
            unitButton = unit;
            applyButton = apply;
            lastTitle = null;
            Apply();
        }

        private void LateUpdate()
        {
            if (title == null) return;
            var interactable = input != null && input.interactable;
            if (lastTitle == title.text && lastInteractable == interactable) return;
            Apply();
        }

        private void Apply()
        {
            if (title == null) return;

            var rawTitle = string.IsNullOrWhiteSpace(title.text) ? "参数设置" : title.text;
            var hasSelection = input != null && input.interactable;
            var componentText = rawTitle.EndsWith(" 参数设置", StringComparison.Ordinal)
                ? rawTitle.Substring(0, rawTitle.Length - " 参数设置".Length)
                : string.Empty;

            title.text = "参数设置";
            if (subtitle != null)
            {
                subtitle.text = hasSelection && !string.IsNullOrEmpty(componentText)
                    ? "元件：" + componentText
                    : "请选择画布中的元件以编辑参数";
                subtitle.color = hasSelection ? MainUiTheme.SecondaryText : MainUiTheme.MutedText;
            }

            if (input != null) input.gameObject.SetActive(hasSelection);
            if (unitButton != null) unitButton.gameObject.SetActive(hasSelection);
            if (applyButton != null) applyButton.SetActive(hasSelection);

            lastTitle = title.text;
            lastInteractable = hasSelection;
        }
    }
}
