using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 正式 Wire 的最小正交视图。端点事实只保存在 SpiceWorkspaceModel，
    /// 本类按当前元件位置重绘，不承担临时接线预览的生命周期。
    /// </summary>
    public sealed class SpiceWorkspaceWireView
    {
        private readonly SpiceWorkspaceController owner;
        private readonly SpiceWorkspaceWireData data;
        private readonly SpiceWorkspaceComponentView start;
        private readonly SpiceWorkspaceComponentView end;
        private readonly Image horizontal;
        private readonly Image vertical;

        public SpiceWorkspaceWireData Data => data;

        public SpiceWorkspaceWireView(SpiceWorkspaceController workspace, SpiceWorkspaceWireData wire, SpiceWorkspaceComponentView startComponent, SpiceWorkspaceComponentView endComponent)
        {
            owner = workspace;
            data = wire;
            start = startComponent;
            end = endComponent;
            horizontal = CreateSegment("WireHorizontal");
            vertical = CreateSegment("WireVertical");
            Refresh();
        }

        public void Refresh()
        {
            SetPath(horizontal.rectTransform, vertical.rectTransform, start.GetTerminalPosition(data.StartTerminalId), end.GetTerminalPosition(data.EndTerminalId));
        }

        public void SetSelected(bool selected)
        {
            var color = selected ? new Color(0.86f, 0.22f, 0.12f) : new Color(0.12f, 0.43f, 0.75f);
            horizontal.color = color;
            vertical.color = color;
        }

        public void Destroy()
        {
            Object.Destroy(horizontal.gameObject);
            Object.Destroy(vertical.gameObject);
        }

        internal static void SetPath(RectTransform horizontal, RectTransform vertical, Vector2 from, Vector2 to)
        {
            var elbow = new Vector2(to.x, from.y);
            SetHorizontal(horizontal, from, elbow);
            SetVertical(vertical, elbow, to);
        }

        private Image CreateSegment(string name)
        {
            var segment = SpiceWorkspaceUi.CreateImage(owner.WireLayer, name, new Color(0.12f, 0.43f, 0.75f));
            segment.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            segment.gameObject.AddComponent<SpiceWorkspaceWireClick>().Initialize(owner, this);
            return segment;
        }

        private static void SetHorizontal(RectTransform rect, Vector2 from, Vector2 to)
        {
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = new Vector2(Mathf.Abs(to.x - from.x), 5f);
        }

        private static void SetVertical(RectTransform rect, Vector2 from, Vector2 to)
        {
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = new Vector2(5f, Mathf.Abs(to.y - from.y));
        }
    }

    /// <summary>
    /// 接线过程中的瞬时视觉，不对应模型中的导线；取消或完成接线后必须销毁。
    /// </summary>
    internal sealed class SpiceWorkspaceWirePreview
    {
        private readonly Image horizontal;
        private readonly Image vertical;

        public SpiceWorkspaceWirePreview(SpiceWorkspaceController owner)
        {
            horizontal = SpiceWorkspaceUi.CreateImage(owner.WireLayer, "WirePreviewHorizontal", new Color(0.15f, 0.45f, 0.85f, 0.42f));
            vertical = SpiceWorkspaceUi.CreateImage(owner.WireLayer, "WirePreviewVertical", new Color(0.15f, 0.45f, 0.85f, 0.42f));
            horizontal.raycastTarget = false;
            vertical.raycastTarget = false;
        }

        public void Refresh(Vector2 from, Vector2 to)
        {
            SpiceWorkspaceWireView.SetPath(horizontal.rectTransform, vertical.rectTransform, from, to);
        }

        public void Destroy()
        {
            Object.Destroy(horizontal.gameObject);
            Object.Destroy(vertical.gameObject);
        }
    }

    public sealed class SpiceWorkspaceWireClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        private SpiceWorkspaceWireView wire;

        public void Initialize(SpiceWorkspaceController workspace, SpiceWorkspaceWireView value)
        {
            owner = workspace;
            wire = value;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectWire(wire);
        }
    }
}
