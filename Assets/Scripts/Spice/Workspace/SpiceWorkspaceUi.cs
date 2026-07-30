using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    // 工作区 UI 的既有轻量支持类型；保持完整类型名，避免引入新的运行时架构。
    internal sealed class SpiceScrollableTextView
    {
        public SpiceScrollableTextView(ScrollRect scrollRect, RectTransform viewport, RectTransform content, Text text)
        {
            ScrollRect = scrollRect;
            Viewport = viewport;
            Content = content;
            Text = text;
        }

        public ScrollRect ScrollRect { get; }
        public RectTransform Viewport { get; }
        public RectTransform Content { get; }
        public Text Text { get; }
    }

    internal static class SpiceScrollableTextLayout
    {
        public const float ScrollSensitivity = 28f;

        public static void ConfigureContent(RectTransform content)
        {
            var layout = content.GetComponent<VerticalLayoutGroup>() ?? content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public static bool Refresh(ScrollRect scrollRect, Text text, string value, bool resetToTop)
        {
            if (text == null) return false;
            if (text.text == value) return false;

            text.text = value;
            // Result content is authoritative; a temporarily unavailable scroll view must not hide diagnostics.
            // Hidden diagnostic sinks carry data only and must not force UGUI layout work during lifecycle changes.
            if (scrollRect == null || scrollRect.content == null || !scrollRect.isActiveAndEnabled || !scrollRect.content.gameObject.activeInHierarchy) return true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            if (!resetToTop) return true;

            scrollRect.StopMovement();
            scrollRect.horizontalNormalizedPosition = 0f;
            scrollRect.verticalNormalizedPosition = 1f;
            return true;
        }
    }

    public sealed class SpiceWorkspaceBlankClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        public void Initialize(SpiceWorkspaceController workspace) { owner = workspace; }
        public void OnPointerClick(PointerEventData eventData) => owner.HandleWorkspacePointerClick(eventData);
    }

    internal static class SpiceWorkspaceUi
    {
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }

        public static Text CreateText(Transform parent, string name, string value, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.UiFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction action, bool primary = false)
        {
            var image = CreateImage(parent, name, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (action != null) button.onClick.AddListener(action);
            var text = CreateText(image.transform, "Text", label, 14, FontStyle.Bold, TextAnchor.MiddleCenter, primary ? Color.white : MainUiTheme.NormalText);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            return button;
        }

        public static InputField CreateInput(Transform parent, string name)
        {
            var image = CreateImage(parent, name, Color.white);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            var input = image.gameObject.AddComponent<InputField>();
            var text = CreateText(image.transform, "Text", string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.NormalText);
            Stretch(text.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));
            input.textComponent = text;
            return input;
        }

        public static void Stretch(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
        public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) { rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
