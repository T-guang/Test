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
        // TerminalId 是保存、拓扑与 Wire 端点使用的稳定电气身份；Label 仅用于显示，坐标与缩放仅用于命中和绘制，
        // 两者都不能参与端子等价判断。TerminalView 由所属 CircuitComponent 创建并拥有，导线保存其两端引用而非屏幕位置。
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

        // WorldPosition 是当前画布锚点，供 WireView 绘制路线使用；移动视觉或调整 RectTransform 不会改变电气连接。

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

