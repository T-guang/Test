using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim.Core
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class WireBendHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        // Handle 只是 WireView 的可视编辑入口，不拥有 Wire、端点或电气节点。它只转发选中与拖拽手势；
        // 实际路径修改、端点保护和持久化语义均由 WireView/ WireManager 决定。
        private WireView owner;
        private int segmentIndex;

        public RectTransform RectTransform { get; private set; }

        public void Initialize(WireView wire, int editableSegmentIndex)
        {
            // segmentIndex 指向当前渲染路径中的可编辑线段，而不是电气端子索引；路径重建后必须由 WireView 重新初始化。
            owner = wire;
            segmentIndex = editableSegmentIndex;
            RectTransform = GetComponent<RectTransform>();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner?.SelectFromBendHandle();
            eventData.Use();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner?.BeginBendDrag(segmentIndex, eventData);
            eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            owner?.DragActiveSegment(eventData);
            eventData.Use();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            owner?.EndBendDrag(eventData);
            eventData.Use();
        }
    }
}
