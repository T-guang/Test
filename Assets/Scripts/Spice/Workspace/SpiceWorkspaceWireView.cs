using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 最小正交导线视图。导线端点保存在 SpiceWorkspaceModel，视图仅根据端子位置重绘。
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
            var startPosition = start.GetTerminalPosition(data.StartTerminalId);
            var endPosition = end.GetTerminalPosition(data.EndTerminalId);
            SetHorizontal(horizontal.rectTransform, startPosition, new Vector2(endPosition.x, startPosition.y));
            SetVertical(vertical.rectTransform, new Vector2(endPosition.x, startPosition.y), endPosition);
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
