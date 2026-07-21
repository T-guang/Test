using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    [RequireComponent(typeof(RectTransform))]
    /// <summary>
    /// 表示一条活动导线的可视与交互层，保存端点、颜色、走线折点和选中/拖拽状态。
    /// 电气连通关系仅由 StartTerminal 与 EndTerminal 决定，几何路由和颜色不改变拓扑；
    /// 修改后需回归接线、折点拖动、已有线改色、撤销重做及保存导入。
    /// </summary>
    public sealed class WireView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image segmentPrefab;

        private const float ExitDistance = 28f;
        private const float PointEpsilon = 2f;
        private const float SegmentThickness = 5f;
        private const float SelectedSegmentThickness = 10f;
        private const float SegmentHitThickness = 22f;
        private const int LegacyManualRoutePointCount = 6;

        public string WireId { get; private set; }
        public TerminalView StartTerminal { get; private set; }
        public TerminalView EndTerminal { get; private set; }
        public Color WireColor { get; private set; }
        public WireStyle Style { get; private set; }
        public bool HasManualRoute => manualRoute;
        public bool ManualRouteHorizontal => manualRouteHorizontal;
        public float ManualRouteAxis => manualRouteAxis;
        public IReadOnlyList<Vector2> ManualRoutePoints => manualPoints;

        private RectTransform rectTransform;
        private WorkspaceController workspace;
        private bool selected;
        private float routeOffset;
        private bool manualRoute;
        private bool manualRouteHorizontal;
        private float manualRouteAxis;
        private bool bendDragging;
        private int activeDragSegmentIndex = -1;
        private bool activeDragHorizontal;
        private bool activeDragPrepared;
        private readonly List<Image> segments = new List<Image>();
        private readonly List<Vector2> currentPoints = new List<Vector2>();
        private readonly List<Vector2> manualPoints = new List<Vector2>();
        private readonly List<WireBendHandle> segmentHandles = new List<WireBendHandle>();

        private enum TerminalExitSide
        {
            Top,
            Bottom,
            Left,
            Right
        }

        private struct TerminalExit
        {
            public Vector2 Point;
            public Vector2 Direction;
            public Rect Bounds;
            public TerminalExitSide Side;
        }

        public void Initialize(TerminalView start, TerminalView end, Color color, WireStyle style, WorkspaceController owner)
        {
            // WireId 仅用于该导线实例和保存恢复；端点引用才是 Analyzer 与 Validation 使用的连接事实。
            WireId = System.Guid.NewGuid().ToString("N");
            StartTerminal = start;
            EndTerminal = end;
            WireColor = color;
            Style = style;
            workspace = owner;
            rectTransform = GetComponent<RectTransform>();
            Refresh();
        }

        public bool Uses(CircuitComponent component)
        {
            return StartTerminal.Owner == component || EndTerminal.Owner == component;
        }

        public bool Uses(TerminalView terminal)
        {
            return StartTerminal == terminal || EndTerminal == terminal;
        }

        public void SetRouteOffset(float offset)
        {
            routeOffset = offset;
            Refresh();
        }

        public void SetSelected(bool isSelected)
        {
            selected = isSelected;
            StartTerminal?.SetWireEndpointHighlight(isSelected);
            EndTerminal?.SetWireEndpointHighlight(isSelected);
            if (Style == WireStyle.Orthogonal && currentPoints.Count > 1)
            {
                UpdateSegmentHandles();
            }

            RefreshSegmentStyle();
        }

        public void SetWireColor(Color color)
        {
            // 只改变已选中导线的显示与保存颜色，不应写回 Workspace 的新建导线默认色。
            color.a = 1f;
            WireColor = color;
            RefreshSegmentStyle();
        }

        public void SetManualRoute(bool horizontal, float axis)
        {
            manualRoute = true;
            manualRouteHorizontal = horizontal;
            manualRouteAxis = axis;
            manualPoints.Clear();
            Refresh();
        }

        public void SetManualRoutePoints(IReadOnlyList<Vector2> points)
        {
            manualPoints.Clear();
            if (points != null)
            {
                for (var i = 0; i < points.Count; i++)
                {
                    manualPoints.Add(points[i]);
                }
            }

            manualRoute = manualPoints.Count >= 2;
            // 手工折点优先于自动避让；保留端点出口段，避免拖动后导线直接穿过元件本体。
            if (manualRoute)
            {
                ResolveManualRouteAxisFromPoints(manualPoints);
            }

            Refresh();
        }

        public void ClearManualRoute()
        {
            manualRoute = false;
            manualPoints.Clear();
            Refresh();
        }

        public void SelectFromBendHandle()
        {
            workspace?.SelectWire(this);
        }

        public void BeginBendDrag()
        {
            SelectFromBendHandle();
        }

        public void BeginBendDrag(int segmentIndex, PointerEventData eventData)
        {
            bendDragging = false;
            activeDragSegmentIndex = -1;
            activeDragPrepared = false;

            if (eventData == null || segmentIndex < 0)
            {
                return;
            }

            workspace?.SelectWire(this);
            if (segmentIndex >= currentPoints.Count - 1 || !CanShowDragHandle(segmentIndex, currentPoints.Count))
            {
                return;
            }

            workspace?.RecordHistoryCheckpoint();
            activeDragHorizontal = IsHorizontalSegment(currentPoints, segmentIndex);
            manualRouteAxis = ResolveSegmentAxis(currentPoints, segmentIndex, activeDragHorizontal);
            manualRoute = true;
            manualRouteHorizontal = activeDragHorizontal;
            RebuildManualRouteAsSixPoints(manualRouteHorizontal, manualRouteAxis);
            activeDragSegmentIndex = 2;
            activeDragPrepared = true;
            bendDragging = true;
            Refresh();
        }

        public void DragSegmentToScreenPosition(int segmentIndex, PointerEventData eventData)
        {
            if (!bendDragging)
            {
                BeginBendDrag(segmentIndex, eventData);
            }

            DragActiveSegment(eventData);
        }

        public void DragActiveSegment(PointerEventData eventData)
        {
            if (!bendDragging || !activeDragPrepared || eventData == null || !CanDragSegment(activeDragSegmentIndex, manualPoints.Count))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var local);
            manualRoute = true;
            manualRouteHorizontal = activeDragHorizontal;
            manualRouteAxis = SnapAxis(activeDragHorizontal ? local.y : local.x);
            RebuildManualRouteAsSixPoints(manualRouteHorizontal, manualRouteAxis);
            Refresh();
        }

        public void EndBendDrag(PointerEventData eventData)
        {
            if (!bendDragging)
            {
                return;
            }

            bendDragging = false;
            activeDragPrepared = false;
            activeDragSegmentIndex = -1;
            RebuildManualRouteAsSixPoints(manualRouteHorizontal, manualRouteAxis);
            Refresh();
            workspace?.MarkSimulationDirty("导线已调整，点击开始仿真重新检查。");
        }

        public void Refresh()
        {
            if (StartTerminal == null || EndTerminal == null)
            {
                return;
            }

            var start = ToLocal(StartTerminal.WorldPosition);
            var end = ToLocal(EndTerminal.WorldPosition);

            if (Style == WireStyle.Orthogonal)
            {
                BuildOrthogonalRoute(start, end);
                EnsureSegments(Mathf.Max(1, currentPoints.Count - 1));
                for (var i = 0; i < currentPoints.Count - 1; i++)
                {
                    DrawSegment(segments[i].rectTransform, currentPoints[i], currentPoints[i + 1]);
                }

                UpdateSegmentHandles();
            }
            else
            {
                EnsureSegments(1);
                DrawSegment(segments[0].rectTransform, start, end);
                HideSegmentHandles();
            }

            RefreshSegmentStyle();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            workspace?.SelectWire(this);
            eventData.Use();
        }

        private void BuildOrthogonalRoute(Vector2 start, Vector2 end)
        {
            currentPoints.Clear();
            var startExit = ResolveTerminalExit(start, StartTerminal);
            var endExit = ResolveTerminalExit(end, EndTerminal);

            if (manualRoute)
            {
                if (manualPoints.Count >= 2)
                {
                    manualPoints[0] = start;
                    manualPoints[manualPoints.Count - 1] = end;
                }
                else
                {
                    RebuildManualRouteAsSixPoints(start, end, startExit.Point, endExit.Point, manualRouteHorizontal, manualRouteAxis);
                }

                currentPoints.AddRange(manualPoints);
                return;
            }

            currentPoints.Add(start);
            currentPoints.Add(startExit.Point);

            if (StartTerminal.Owner == EndTerminal.Owner)
            {
                AddSameComponentJumperMiddlePoints(startExit, endExit);
            }
            else
            {
                AddAutomaticMiddlePoints(startExit, endExit);
            }

            currentPoints.Add(endExit.Point);
            currentPoints.Add(end);
            NormalizeOrthogonalPoints(currentPoints, false);
        }

        private void AddSameComponentJumperMiddlePoints(TerminalExit startExit, TerminalExit endExit)
        {
            var lane = ResolveLaneDistance() + Mathf.Abs(routeOffset);
            List<Vector2> route;
            if (startExit.Side == TerminalExitSide.Top && endExit.Side == TerminalExitSide.Top)
            {
                route = BuildCorridor(startExit.Point, endExit.Point, true, startExit.Bounds.yMax + lane);
            }
            else if (startExit.Side == TerminalExitSide.Bottom && endExit.Side == TerminalExitSide.Bottom)
            {
                route = BuildCorridor(startExit.Point, endExit.Point, true, startExit.Bounds.yMin - lane);
            }
            else
            {
                route = BuildCorridor(startExit.Point, endExit.Point, false, startExit.Bounds.xMax + lane);
            }

            for (var i = 1; i < route.Count - 1; i++)
            {
                currentPoints.Add(route[i]);
            }
        }

        private void RebuildManualRouteAsSixPoints(bool horizontal, float axis)
        {
            if (StartTerminal == null || EndTerminal == null)
            {
                return;
            }

            var start = ToLocal(StartTerminal.WorldPosition);
            var end = ToLocal(EndTerminal.WorldPosition);
            var startExit = ResolveTerminalExit(start, StartTerminal).Point;
            var endExit = ResolveTerminalExit(end, EndTerminal).Point;
            RebuildManualRouteAsSixPoints(start, end, startExit, endExit, horizontal, axis);
        }

        private void RebuildManualRouteAsSixPoints(Vector2 start, Vector2 end, Vector2 startExit, Vector2 endExit, bool horizontal, float axis)
        {
            manualPoints.Clear();
            manualPoints.Add(start);
            manualPoints.Add(startExit);
            if (horizontal)
            {
                manualPoints.Add(new Vector2(startExit.x, axis));
                manualPoints.Add(new Vector2(endExit.x, axis));
            }
            else
            {
                manualPoints.Add(new Vector2(axis, startExit.y));
                manualPoints.Add(new Vector2(axis, endExit.y));
            }

            manualPoints.Add(endExit);
            manualPoints.Add(end);
            manualRoute = true;
            manualRouteHorizontal = horizontal;
            manualRouteAxis = axis;
        }

        private void AddAutomaticMiddlePoints(TerminalExit startExit, TerminalExit endExit)
        {
            var best = ResolveAutomaticRoute(startExit, endExit);
            for (var i = 1; i < best.Count - 1; i++)
            {
                currentPoints.Add(best[i]);
            }
        }

        private List<Vector2> ResolveAutomaticRoute(TerminalExit startExit, TerminalExit endExit)
        {
            // 从有限的正交走廊候选中选取代价最低路径。评分只处理显示避让，
            // 不可将其结果用于判定电气连接或替代用户已保存的手工走线。
            var start = startExit.Point;
            var end = endExit.Point;
            var candidates = new List<List<Vector2>>();
            var lane = ResolveLaneDistance();

            if (NearlySame(start.x, end.x) || NearlySame(start.y, end.y))
            {
                AddCandidate(candidates, start, end);
            }

            AddCandidate(candidates, BuildCorridor(start, end, true, SnapAxis((start.y + end.y) * 0.5f + routeOffset)));
            AddCandidate(candidates, BuildCorridor(start, end, false, SnapAxis((start.x + end.x) * 0.5f + routeOffset)));
            AddCandidate(candidates, BuildCorridor(start, end, true, Mathf.Max(startExit.Bounds.yMax, endExit.Bounds.yMax) + lane));
            AddCandidate(candidates, BuildCorridor(start, end, true, Mathf.Min(startExit.Bounds.yMin, endExit.Bounds.yMin) - lane));
            AddCandidate(candidates, BuildCorridor(start, end, false, Mathf.Min(startExit.Bounds.xMin, endExit.Bounds.xMin) - lane));
            AddCandidate(candidates, BuildCorridor(start, end, false, Mathf.Max(startExit.Bounds.xMax, endExit.Bounds.xMax) + lane));

            var best = candidates[0];
            var bestScore = ScoreRoute(best, startExit.Bounds, endExit.Bounds);
            for (var i = 1; i < candidates.Count; i++)
            {
                var score = ScoreRoute(candidates[i], startExit.Bounds, endExit.Bounds);
                if (score < bestScore)
                {
                    best = candidates[i];
                    bestScore = score;
                }
            }

            return best;
        }

        private static void AddCandidate(List<List<Vector2>> candidates, params Vector2[] points)
        {
            var route = new List<Vector2>(points);
            NormalizeOrthogonalPoints(route);
            if (route.Count >= 2)
            {
                candidates.Add(route);
            }
        }

        private static void AddCandidate(List<List<Vector2>> candidates, List<Vector2> points)
        {
            NormalizeOrthogonalPoints(points);
            if (points.Count >= 2)
            {
                candidates.Add(points);
            }
        }

        private static List<Vector2> BuildCorridor(Vector2 start, Vector2 end, bool horizontalCorridor, float axis)
        {
            if (horizontalCorridor)
            {
                return new List<Vector2>
                {
                    start,
                    new Vector2(start.x, axis),
                    new Vector2(end.x, axis),
                    end
                };
            }

            return new List<Vector2>
            {
                start,
                new Vector2(axis, start.y),
                new Vector2(axis, end.y),
                end
            };
        }

        private float ScoreRoute(List<Vector2> points, Rect startBounds, Rect endBounds)
        {
            var score = Mathf.Max(0, points.Count - 2) * 6f;
            for (var i = 0; i < points.Count - 1; i++)
            {
                score += Vector2.Distance(points[i], points[i + 1]) * 0.015f;
                if (SegmentOverlapsRect(points[i], points[i + 1], startBounds))
                {
                    score += 10000f;
                }

                if (SegmentOverlapsRect(points[i], points[i + 1], endBounds))
                {
                    score += 10000f;
                }
            }

            return score;
        }

        private static bool SegmentOverlapsRect(Vector2 start, Vector2 end, Rect rect)
        {
            const float clearance = 4f;
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return false;
            }

            var min = Vector2.Min(start, end);
            var max = Vector2.Max(start, end);
            var segmentRect = Rect.MinMaxRect(min.x - clearance, min.y - clearance, max.x + clearance, max.y + clearance);
            return segmentRect.Overlaps(rect);
        }

        private TerminalExit ResolveTerminalExit(Vector2 terminalPosition, TerminalView terminal)
        {
            var tightBounds = GetComponentBounds(terminal.Owner, 0f);
            var paddedBounds = GetComponentBounds(terminal.Owner, 8f);
            var distanceTop = Mathf.Abs(terminalPosition.y - tightBounds.yMax);
            var distanceBottom = Mathf.Abs(terminalPosition.y - tightBounds.yMin);
            var distanceLeft = Mathf.Abs(terminalPosition.x - tightBounds.xMin);
            var distanceRight = Mathf.Abs(terminalPosition.x - tightBounds.xMax);
            var side = TerminalExitSide.Top;
            var nearest = distanceTop;

            if (distanceBottom < nearest)
            {
                side = TerminalExitSide.Bottom;
                nearest = distanceBottom;
            }

            if (distanceLeft < nearest)
            {
                side = TerminalExitSide.Left;
                nearest = distanceLeft;
            }

            if (distanceRight < nearest)
            {
                side = TerminalExitSide.Right;
            }

            var direction = ResolveDirection(side);
            return new TerminalExit
            {
                Point = terminalPosition + direction * ExitDistance,
                Direction = direction,
                Bounds = paddedBounds,
                Side = side
            };
        }

        private static Vector2 ResolveDirection(TerminalExitSide side)
        {
            switch (side)
            {
                case TerminalExitSide.Top:
                    return Vector2.up;
                case TerminalExitSide.Bottom:
                    return Vector2.down;
                case TerminalExitSide.Left:
                    return Vector2.left;
                case TerminalExitSide.Right:
                    return Vector2.right;
                default:
                    return Vector2.up;
            }
        }

        private float ResolveLaneDistance()
        {
            return 54f + Mathf.Abs(routeOffset) * 0.5f;
        }

        private static float SnapAxis(float value)
        {
            const float snap = 12f;
            return Mathf.Round(value / snap) * snap;
        }

        private static bool NearlySame(float a, float b)
        {
            return Mathf.Abs(a - b) <= PointEpsilon;
        }

        private static void NormalizeOrthogonalPoints(List<Vector2> points)
        {
            NormalizeOrthogonalPoints(points, false);
        }

        private static void NormalizeOrthogonalPoints(List<Vector2> points, bool keepCollinearMiddlePoints)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            RemoveDuplicatePoints(points, keepCollinearMiddlePoints);

            for (var i = 0; i < points.Count - 1; i++)
            {
                var current = points[i];
                var next = points[i + 1];
                var sameX = NearlySame(current.x, next.x);
                var sameY = NearlySame(current.y, next.y);

                if (sameX)
                {
                    next.x = current.x;
                    points[i + 1] = next;
                    continue;
                }

                if (sameY)
                {
                    next.y = current.y;
                    points[i + 1] = next;
                    continue;
                }

                var previousVertical = i > 0 && NearlySame(points[i - 1].x, current.x);
                var elbow = previousVertical ? new Vector2(current.x, next.y) : new Vector2(next.x, current.y);
                points.Insert(i + 1, elbow);
            }

            RemoveDuplicatePoints(points, keepCollinearMiddlePoints);
            if (!keepCollinearMiddlePoints)
            {
                RemoveCollinearPoints(points);
            }
        }

        private static void RemoveDuplicatePoints(List<Vector2> points, bool preserveManualRoute)
        {
            for (var i = points.Count - 2; i >= 0; i--)
            {
                if ((points[i] - points[i + 1]).sqrMagnitude <= PointEpsilon * PointEpsilon)
                {
                    if (preserveManualRoute)
                    {
                        var removeIndex = i + 1;
                        if (points.Count <= 5 || IsProtectedRoutePoint(removeIndex, points.Count) || IsProtectedRoutePoint(i, points.Count))
                        {
                            continue;
                        }
                    }

                    points.RemoveAt(i + 1);
                }
            }
        }

        private static bool IsProtectedRoutePoint(int index, int pointCount)
        {
            return index <= 1 || index >= pointCount - 2;
        }

        private static void RemoveCollinearPoints(List<Vector2> points)
        {
            for (var i = points.Count - 2; i >= 1; i--)
            {
                var previous = points[i - 1];
                var current = points[i];
                var next = points[i + 1];
                var sameX = NearlySame(previous.x, current.x) && NearlySame(current.x, next.x);
                var sameY = NearlySame(previous.y, current.y) && NearlySame(current.y, next.y);
                if (sameX || sameY)
                {
                    points.RemoveAt(i);
                }
            }
        }

        private bool CanDragSegment(int segmentIndex, int pointCount)
        {
            return CanMoveRoutePoint(segmentIndex, pointCount) && CanMoveRoutePoint(segmentIndex + 1, pointCount);
        }

        private static bool CanShowDragHandle(int segmentIndex, int pointCount)
        {
            return segmentIndex > 0 && segmentIndex < pointCount - 2;
        }

        private static bool CanMoveRoutePoint(int pointIndex, int pointCount)
        {
            return pointIndex >= 2 && pointIndex <= pointCount - 3;
        }

        private static bool IsHorizontalSegment(IReadOnlyList<Vector2> points, int segmentIndex)
        {
            if (points == null || segmentIndex < 0 || segmentIndex >= points.Count - 1)
            {
                return true;
            }

            var start = points[segmentIndex];
            var end = points[segmentIndex + 1];
            if (NearlySame(start.y, end.y))
            {
                return true;
            }

            if (NearlySame(start.x, end.x))
            {
                return false;
            }

            return Mathf.Abs(end.x - start.x) >= Mathf.Abs(end.y - start.y);
        }

        private void ResolveManualRouteAxisFromPoints(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                manualRouteHorizontal = true;
                manualRouteAxis = 0f;
                return;
            }

            var bestIndex = 0;
            var bestLength = -1f;
            for (var i = 1; i < points.Count - 2; i++)
            {
                var length = (points[i + 1] - points[i]).sqrMagnitude;
                if (length > bestLength)
                {
                    bestIndex = i;
                    bestLength = length;
                }
            }

            manualRouteHorizontal = IsHorizontalSegment(points, bestIndex);
            manualRouteAxis = ResolveSegmentAxis(points, bestIndex, manualRouteHorizontal);
        }

        private static float ResolveSegmentAxis(IReadOnlyList<Vector2> points, int segmentIndex, bool horizontal)
        {
            if (points == null || segmentIndex < 0 || segmentIndex >= points.Count - 1)
            {
                return 0f;
            }

            var start = points[segmentIndex];
            var end = points[segmentIndex + 1];
            return horizontal ? SnapAxis((start.y + end.y) * 0.5f) : SnapAxis((start.x + end.x) * 0.5f);
        }

        private void UpdateSegmentHandles()
        {
            var handleIndex = 0;
            for (var i = 0; i < currentPoints.Count - 1; i++)
            {
                if (manualRoute && (!CanUseLegacyManualRouteHandle() || i != 2))
                {
                    continue;
                }

                if (!CanShowDragHandle(i, currentPoints.Count))
                {
                    continue;
                }

                var handle = EnsureSegmentHandle(handleIndex, i);
                DrawSegment(handle.RectTransform, currentPoints[i], currentPoints[i + 1], SegmentHitThickness);
                handle.gameObject.SetActive(selected && Style == WireStyle.Orthogonal);
                handleIndex++;
            }

            for (var i = handleIndex; i < segmentHandles.Count; i++)
            {
                if (segmentHandles[i] != null)
                {
                    segmentHandles[i].gameObject.SetActive(false);
                }
            }
        }

        private bool CanUseLegacyManualRouteHandle()
        {
            return manualPoints.Count == LegacyManualRoutePointCount;
        }

        private WireBendHandle EnsureSegmentHandle(int handleIndex, int segmentIndex)
        {
            while (segmentHandles.Count <= handleIndex)
            {
                var go = new GameObject("WireSegmentHandle", typeof(RectTransform), typeof(Image), typeof(WireBendHandle));
                go.transform.SetParent(transform, false);
                var image = go.GetComponent<Image>();
                image.raycastTarget = true;
                image.color = new Color(1f, 1f, 1f, 0.02f);
                segmentHandles.Add(go.GetComponent<WireBendHandle>());
            }

            var handle = segmentHandles[handleIndex];
            handle.Initialize(this, segmentIndex);
            handle.transform.SetAsLastSibling();
            return handle;
        }

        private void HideSegmentHandles()
        {
            foreach (var handle in segmentHandles)
            {
                if (handle != null)
                {
                    handle.gameObject.SetActive(false);
                }
            }
        }

        private Rect GetComponentBounds(CircuitComponent component, float padding)
        {
            var componentRect = component != null ? component.GetComponent<RectTransform>() : null;
            if (componentRect == null)
            {
                return new Rect();
            }

            var corners = new Vector3[4];
            componentRect.GetWorldCorners(corners);
            var min = ToLayerLocal(corners[0]);
            var max = min;
            for (var i = 1; i < corners.Length; i++)
            {
                var point = ToLayerLocal(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            min -= Vector2.one * padding;
            max += Vector2.one * padding;
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Vector2 ToLayerLocal(Vector3 world)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, RectTransformUtility.WorldToScreenPoint(null, world), null, out var local);
            return local;
        }

        private Vector2 ToLocal(Vector3 world)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, RectTransformUtility.WorldToScreenPoint(null, world), null, out var local);
            return local;
        }

        private void EnsureSegments(int count)
        {
            while (segments.Count < count)
            {
                var segment = segmentPrefab != null ? Instantiate(segmentPrefab, transform) : CreateSegment();
                segments.Add(segment);
            }

            for (var i = 0; i < segments.Count; i++)
            {
                segments[i].gameObject.SetActive(i < count);
                segments[i].raycastTarget = true;
            }
        }

        private Image CreateSegment()
        {
            var go = new GameObject("WireSegment", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = true;
            return image;
        }

        private void RefreshSegmentStyle()
        {
            var color = selected ? Color.Lerp(WireColor, Color.white, 0.35f) : WireColor;
            var width = selected ? SelectedSegmentThickness : SegmentThickness;

            foreach (var segment in segments)
            {
                if (segment == null)
                {
                    continue;
                }

                segment.color = color;
                var size = segment.rectTransform.sizeDelta;
                segment.rectTransform.sizeDelta = new Vector2(size.x, width);
            }

            foreach (var handle in segmentHandles)
            {
                if (handle == null)
                {
                    continue;
                }

                var image = handle.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected ? new Color(WireColor.r, WireColor.g, WireColor.b, 0.06f) : new Color(1f, 1f, 1f, 0.02f);
                }

                if (!selected || Style != WireStyle.Orthogonal)
                {
                    handle.gameObject.SetActive(false);
                }
            }
        }

        private static void DrawSegment(RectTransform segment, Vector2 start, Vector2 end)
        {
            DrawSegment(segment, start, end, SegmentThickness);
        }

        private static void DrawSegment(RectTransform segment, Vector2 start, Vector2 end, float thickness)
        {
            var delta = end - start;
            if (!NearlySame(delta.x, 0f) && !NearlySame(delta.y, 0f))
            {
                if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
                {
                    end.y = start.y;
                }
                else
                {
                    end.x = start.x;
                }

                delta = end - start;
            }

            var length = delta.magnitude;
            segment.anchoredPosition = start + delta * 0.5f;
            segment.sizeDelta = new Vector2(Mathf.Max(2f, length), thickness);
            segment.localRotation = Quaternion.Euler(0f, 0f, NearlySame(delta.x, 0f) ? 90f : 0f);
        }
    }
}
