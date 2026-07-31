using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 工作区视图控制器：只负责画布缩放、平移和视图命令。
    /// 它不参与接线、元件选择、参数编辑或 SPICE 计算。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SpiceWorkspaceViewController : MonoBehaviour, IScrollHandler
    {
        private const float MinScale = 0.40f;
        private const float DefaultScale = 1.00f;
        private const float MaxScale = 2.00f;
        private const float ScaleStep = 0.10f;
        private const float FitPadding = 0.9f;

        private RectTransform viewportRect;
        private RectTransform contentRect;
        private SpiceWorkspaceController workspace;
        private Text zoomLabel;

        private bool isPanning;
        private PanMode panMode;
        private Vector2 panStartScreen;
        private Vector2 panStartAnchoredPosition;
        private PointerEventData.InputButton suppressedButton;
        private int suppressClickUntilFrame = -1;

        public float CurrentScale => contentRect != null ? contentRect.localScale.x : DefaultScale;
        public RectTransform ContentRect => contentRect;
        public RectTransform ViewportRect => viewportRect;
        public bool IsPanning => isPanning;

        /// <summary>由 SpiceWorkspaceController.BuildUi 在装配 Content 后调用。</summary>
        public void Initialize(SpiceWorkspaceController owner, RectTransform viewport, RectTransform content, Text label)
        {
            workspace = owner;
            viewportRect = viewport;
            contentRect = content;
            zoomLabel = label;
            ResetView();
        }

        /// <summary>
        /// 工作区在根节点激活后才获得可信的 Viewport 尺寸。此入口只重新夹紧当前平移，
        /// 不重置缩放、不改变元件逻辑坐标，也不参与任何电气状态更新。
        /// </summary>
        public void SynchronizeGeometry()
        {
            if (viewportRect == null || contentRect == null || viewportRect.rect.width < 1f || viewportRect.rect.height < 1f)
            {
                return;
            }

            ClampContentPosition();
            UpdateZoomLabel();
        }

        private void Update()
        {
            // 导航仅改变视图；平移结束后会抑制与其对应的点击事件。
            if (!isPanning)
            {
                if (!IsPointerOverViewportContent()) return;
                if (Input.GetMouseButtonDown(2)) StartPan(PanMode.Middle, Input.mousePosition);
                else if (Input.GetKey(KeyCode.Space) && Input.GetMouseButtonDown(0)) StartPan(PanMode.SpaceLeft, Input.mousePosition);
                return;
            }

            var button = panMode == PanMode.Middle ? 2 : 0;
            if (!Input.GetMouseButton(button) || (panMode == PanMode.SpaceLeft && !Input.GetKey(KeyCode.Space)))
            {
                EndPan();
                return;
            }

            UpdatePan(Input.mousePosition);
        }

        public bool ConsumeNavigationClick(PointerEventData eventData)
        {
            if (Time.frameCount > suppressClickUntilFrame || eventData.button != suppressedButton) return false;
            suppressClickUntilFrame = -1;
            return true;
        }

        private bool IsPointerOverViewportContent()
        {
            if (viewportRect == null || !RectTransformUtility.RectangleContainsScreenPoint(viewportRect, Input.mousePosition, null)) return false;
            if (EventSystem.current == null) return true;

            var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            return results.Count == 0 || (results[0].gameObject != null &&
                (results[0].gameObject.transform == viewportRect || results[0].gameObject.transform.IsChildOf(viewportRect)));
        }

        /// <summary>IScrollHandler：鼠标滚轮缩放，围绕鼠标所在逻辑点。</summary>
        public void OnScroll(PointerEventData eventData)
        {
            if (!IsPointerOverViewportContent()) return;
            var delta = Mathf.Sign(eventData.scrollDelta.y) * ScaleStep;
            ApplyZoom(delta, eventData.position);
        }

        /// <summary>工具栏 [－] 按钮。</summary>
        public void ZoomOut() => ApplyZoom(-ScaleStep, GetViewportCenterScreen());

        /// <summary>工具栏 [＋] 按钮。</summary>
        public void ZoomIn() => ApplyZoom(ScaleStep, GetViewportCenterScreen());

        private Vector2 GetViewportCenterScreen()
        {
            var worldCenter = viewportRect.TransformPoint(viewportRect.rect.center);
            return RectTransformUtility.WorldToScreenPoint(null, worldCenter);
        }

        private void ApplyZoom(float delta, Vector2 screenPivot)
        {
            if (contentRect == null) return;
            var oldScale = contentRect.localScale.x;
            var newScale = Mathf.Clamp(Mathf.Round((oldScale + delta) * 10f) / 10f, MinScale, MaxScale);
            if (Mathf.Approximately(newScale, oldScale)) return;

            // 鼠标所在逻辑点（缩放前）
            RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRect, screenPivot, null, out var beforeLocal);
            contentRect.localScale = Vector3.one * newScale;
            // 调整 anchoredPosition 使同一逻辑点保持在鼠标下：
            // viewportLocal = anchoredPosition + contentLocal * scale（pivot 居中、stretch 锚点）
            // 保持 viewportLocal 不变 => anchoredPosition += beforeLocal * (oldScale - newScale)
            contentRect.anchoredPosition += beforeLocal * (oldScale - newScale);
            ClampContentPosition();
            UpdateZoomLabel();
        }

        private void StartPan(PanMode mode, Vector2 screenPos)
        {
            isPanning = true;
            panMode = mode;
            panStartScreen = screenPos;
            panStartAnchoredPosition = contentRect.anchoredPosition;
        }

        private void UpdatePan(Vector2 screenPos)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewportRect, screenPos, null, out var current);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewportRect, panStartScreen, null, out var start);
            contentRect.anchoredPosition = panStartAnchoredPosition + (current - start);
            ClampContentPosition();
        }

        private void EndPan()
        {
            suppressedButton = panMode == PanMode.Middle ? PointerEventData.InputButton.Middle : PointerEventData.InputButton.Left;
            suppressClickUntilFrame = Time.frameCount + 2;
            isPanning = false;
        }

        private void ClampContentPosition()
        {
            var viewportSize = viewportRect.rect.size;
            var contentSize = contentRect.rect.size * contentRect.localScale.x;
            var pos = contentRect.anchoredPosition;
            for (var axis = 0; axis < 2; axis++)
            {
                var vp = viewportSize[axis];
                var cs = contentSize[axis];
                if (cs <= vp)
                {
                    // Content 小于 Viewport 的轴自动居中
                    pos[axis] = 0f;
                }
                else
                {
                    // Content 大于 Viewport 时禁止看到逻辑画布外的空白
                    var half = (cs - vp) * 0.5f;
                    pos[axis] = Mathf.Clamp(pos[axis], -half, half);
                }
            }
            contentRect.anchoredPosition = pos;
        }

        /// <summary>重置视图：100% 缩放，初始中心。</summary>
        public void ResetView()
        {
            if (contentRect == null) return;
            contentRect.localScale = Vector3.one * DefaultScale;
            contentRect.anchoredPosition = Vector2.zero;
            ClampContentPosition();
            UpdateZoomLabel();
        }

        /// <summary>适配全部：根据元件、导线和手动折点边界计算缩放和居中。</summary>
        public void FitAll()
        {
            if (workspace == null || contentRect == null) return;
            var bounds = workspace.ComputeContentBounds();
            if (!bounds.HasValue)
            {
                // 空画布时"适配全部"等价于"重置视图"
                ResetView();
                return;
            }
            var boundsSize = bounds.Value.size;
            var viewportSize = viewportRect.rect.size;
            if (boundsSize.x <= 0.01f || boundsSize.y <= 0.01f) { ResetView(); return; }
            var scaleX = viewportSize.x / boundsSize.x;
            var scaleY = viewportSize.y / boundsSize.y;
            var fitScale = Mathf.Min(scaleX, scaleY) * FitPadding;
            fitScale = Mathf.Clamp(fitScale, MinScale, MaxScale);
            var center = bounds.Value.center;
            contentRect.localScale = Vector3.one * fitScale;
            // 使 boundsCenter 映射到 Viewport 中心（0,0）
            contentRect.anchoredPosition = -center * fitScale;
            ClampContentPosition();
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel()
        {
            if (zoomLabel != null)
                zoomLabel.text = Mathf.RoundToInt(CurrentScale * 100f) + "%";
            workspace?.UpdateZoomControlState(CurrentScale);
        }

        private enum PanMode { Middle, SpaceLeft }
    }

}
