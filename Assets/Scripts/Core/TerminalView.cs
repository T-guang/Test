using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 表示画布上一个可接线端子，并负责端子点击、悬停和选中反馈。
    /// 端子的身份、角色和所属元件来自 TerminalDefinition；视觉状态不能替代 WireManager
    /// 中的真实连接关系。修改端子尺寸或命中区域后需回归自由接线与端子命中测试。
    /// </summary>
    public sealed class TerminalView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public string TerminalId { get; private set; }
        public string Label { get; private set; }
        public TerminalRole Role { get; private set; }
        public CircuitComponent Owner { get; private set; }
        public Color TerminalColor { get; private set; }

        private WorkspaceController workspace;
        private Image image;
        private bool selected;
        private bool wireEndpointHighlighted;
        private bool hovered;
        private bool subtleVisualMode;
        private bool showDebugMarker;

        public void Initialize(CircuitComponent owner, TerminalDefinition definition, WorkspaceController ownerWorkspace)
        {
            // 仅在所属元件构建端子时绑定一次。接线数据按 TerminalView 引用建立，
            // 因此不要在已有导线仍引用该端子时随意重建端子对象。
            Owner = owner;
            TerminalId = definition.id;
            Label = definition.label;
            Role = definition.role;
            TerminalColor = definition.color;
            workspace = ownerWorkspace;
            image = GetComponent<Image>();
            SetSelected(false);
        }

        public Vector3 WorldPosition => transform.position;

        public void SetSubtleVisualMode(bool enabled, bool debugMarker = false)
        {
            subtleVisualMode = enabled;
            showDebugMarker = debugMarker;
            ApplyVisualState();
        }

        public void SetSelected(bool isSelected)
        {
            selected = isSelected;
            ApplyVisualState();
        }

        public void SetWireEndpointHighlight(bool highlighted)
        {
            wireEndpointHighlighted = highlighted;
            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (image != null)
            {
                image.raycastTarget = true;
                image.color = ResolveVisualColor();
            }

            var scale = wireEndpointHighlighted ? 1.45f : selected ? 1.25f : 1f;
            transform.localScale = Vector3.one * scale;
        }

        private Color ResolveVisualColor()
        {
            if (!subtleVisualMode)
            {
                return TerminalColor;
            }

            if (wireEndpointHighlighted)
            {
                return new Color(0.18f, 0.55f, 1f, 0.55f);
            }

            if (selected)
            {
                return new Color(0.18f, 0.55f, 1f, 0.42f);
            }

            if (hovered)
            {
                return new Color(1f, 1f, 1f, 0.36f);
            }

            if (showDebugMarker)
            {
                return new Color(TerminalColor.r, TerminalColor.g, TerminalColor.b, 0.28f);
            }

            return new Color(1f, 1f, 1f, 0.01f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            workspace?.HandleTerminalClicked(this);
            eventData.Use();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            ApplyVisualState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            ApplyVisualState();
        }
    }
}

