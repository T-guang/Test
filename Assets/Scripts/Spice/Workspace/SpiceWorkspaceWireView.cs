using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 正式 Wire 的 UGUI 视图。电气事实仍只有两个端点；Auto 路由每次按端点重算，
    /// Manual 路由保留用户折点，并只在端点变化时补充临时正交连接段。
    /// </summary>
    public sealed class SpiceWorkspaceWireView
    {
        private readonly SpiceWorkspaceController owner;
        private readonly SpiceWorkspaceWireData data;
        private readonly SpiceWorkspaceComponentView start;
        private readonly SpiceWorkspaceComponentView end;
        private readonly List<Image> segments = new List<Image>();

        public SpiceWorkspaceWireData Data => data;

        public SpiceWorkspaceWireView(SpiceWorkspaceController workspace, SpiceWorkspaceWireData wire, SpiceWorkspaceComponentView startComponent, SpiceWorkspaceComponentView endComponent)
        {
            owner = workspace;
            data = wire;
            start = startComponent;
            end = endComponent;
            Refresh();
        }

        public void Refresh()
        {
            var path = SpiceWorkspaceOrthogonalRoute.Build(
                start.GetTerminalPosition(data.StartTerminalId),
                end.GetTerminalPosition(data.EndTerminalId),
                start.GetTerminalDirection(data.StartTerminalId),
                data.VisualState);
            RefreshSegments(path);
        }

        public void SetSelected(bool selected)
        {
            var color = selected ? new Color(0.86f, 0.22f, 0.12f) : new Color(0.12f, 0.43f, 0.75f);
            foreach (var segment in segments) segment.color = color;
        }

        public void Destroy()
        {
            foreach (var segment in segments) UnityEngine.Object.Destroy(segment.gameObject);
            segments.Clear();
        }

        private void RefreshSegments(IReadOnlyList<Vector2> path)
        {
            var requiredCount = Math.Max(0, path.Count - 1);
            while (segments.Count < requiredCount) segments.Add(CreateSegment("WireSegment"));
            while (segments.Count > requiredCount)
            {
                var last = segments[segments.Count - 1];
                segments.RemoveAt(segments.Count - 1);
                UnityEngine.Object.Destroy(last.gameObject);
            }
            for (var index = 0; index < requiredCount; index++)
            {
                SetSegment(segments[index].rectTransform, path[index], path[index + 1]);
                segments[index].gameObject.SetActive(true);
            }
        }

        private Image CreateSegment(string name)
        {
            var segment = SpiceWorkspaceUi.CreateImage(owner.WireLayer, name, new Color(0.12f, 0.43f, 0.75f));
            segment.gameObject.AddComponent<SpiceWorkspaceWireClick>().Initialize(owner, this);
            return segment;
        }

        internal static void SetSegment(RectTransform rect, Vector2 from, Vector2 to)
        {
            rect.anchoredPosition = (from + to) * 0.5f;
            rect.sizeDelta = Mathf.Abs(to.x - from.x) >= Mathf.Abs(to.y - from.y)
                ? new Vector2(Mathf.Abs(to.x - from.x), 5f)
                : new Vector2(5f, Mathf.Abs(to.y - from.y));
        }
    }

    /// <summary>
    /// 待接线过程的瞬时视图。它只展示当前临时折点和鼠标位置，
    /// 完成或取消后必须销毁，不能作为正式 Wire 的视觉状态来源。
    /// </summary>
    internal sealed class SpiceWorkspaceWirePreview
    {
        private readonly SpiceWorkspaceController owner;
        private readonly List<Image> segments = new List<Image>();

        public SpiceWorkspaceWirePreview(SpiceWorkspaceController workspace)
        {
            owner = workspace;
        }

        public void Refresh(IReadOnlyList<Vector2> path)
        {
            var requiredCount = Math.Max(0, path.Count - 1);
            while (segments.Count < requiredCount) segments.Add(CreateSegment());
            while (segments.Count > requiredCount)
            {
                var last = segments[segments.Count - 1];
                segments.RemoveAt(segments.Count - 1);
                UnityEngine.Object.Destroy(last.gameObject);
            }
            for (var index = 0; index < requiredCount; index++) SpiceWorkspaceWireView.SetSegment(segments[index].rectTransform, path[index], path[index + 1]);
        }

        public void Destroy()
        {
            foreach (var segment in segments) UnityEngine.Object.Destroy(segment.gameObject);
            segments.Clear();
        }

        private Image CreateSegment()
        {
            var segment = SpiceWorkspaceUi.CreateImage(owner.OverlayLayer, "WirePreviewSegment", new Color(0.15f, 0.45f, 0.85f, 0.42f));
            segment.raycastTarget = false;
            return segment;
        }
    }

    /// <summary>
    /// 正交路径辅助函数。所有输入和输出均是工作区 RectTransform 局部坐标；
    /// 不保存自动补出的连接点，避免移动或旋转元件时修改用户确认的折点。
    /// </summary>
    public static class SpiceWorkspaceOrthogonalRoute
    {
        private const float Epsilon = 0.01f;

        public static IReadOnlyList<Vector2> Build(Vector2 start, Vector2 end, Vector2 startDirection, SpiceWireVisualState visualState)
        {
            var points = new List<Vector2> { start };
            if (visualState == null || visualState.RouteMode == SpiceWireRouteMode.Auto)
            {
                AppendOrthogonal(points, end, IsHorizontal(startDirection));
                return points;
            }

            var nextSegmentHorizontal = IsHorizontal(startDirection);
            foreach (var waypoint in visualState.Waypoints)
            {
                AppendOrthogonal(points, waypoint, nextSegmentHorizontal);
                nextSegmentHorizontal = !nextSegmentHorizontal;
            }
            AppendOrthogonal(points, end, nextSegmentHorizontal);
            return points;
        }

        public static Vector2 ConstrainToAxis(Vector2 anchor, Vector2 pointer, bool horizontal)
        {
            return horizontal ? new Vector2(pointer.x, anchor.y) : new Vector2(anchor.x, pointer.y);
        }

        public static bool IsHorizontal(Vector2 direction) => Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);

        private static void AppendOrthogonal(List<Vector2> points, Vector2 target, bool horizontalFirst)
        {
            var from = points[points.Count - 1];
            if (Approximately(from, target)) return;
            if (Mathf.Abs(from.x - target.x) <= Epsilon || Mathf.Abs(from.y - target.y) <= Epsilon)
            {
                AddPoint(points, target);
                return;
            }
            var elbow = horizontalFirst ? new Vector2(target.x, from.y) : new Vector2(from.x, target.y);
            AddPoint(points, elbow);
            AddPoint(points, target);
        }

        private static void AddPoint(List<Vector2> points, Vector2 point)
        {
            if (!Approximately(points[points.Count - 1], point)) points.Add(point);
        }

        private static bool Approximately(Vector2 left, Vector2 right) => (left - right).sqrMagnitude <= Epsilon * Epsilon;
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
            if (eventData.button != PointerEventData.InputButton.Left || owner.ConsumeViewNavigationClick(eventData)) return;
            owner.SelectWire(wire);
        }
    }
}
