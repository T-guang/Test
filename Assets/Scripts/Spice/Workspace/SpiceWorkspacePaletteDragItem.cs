using ElectricalSim.Spice.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 元件池卡片的拖放入口。它只描述指针生命周期；真正的元件只会在指针
    /// 于工作区内释放时由 SpiceWorkspaceController 创建。
    /// </summary>
    public sealed class SpiceWorkspacePaletteDragItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        private SpiceComponentKind kind;

        public void Initialize(SpiceWorkspaceController workspace, SpiceComponentKind componentKind)
        {
            owner = workspace;
            kind = componentKind;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner.BeginPaletteDrag(kind, eventData.position, eventData.pressEventCamera);
        }

        public void OnDrag(PointerEventData eventData)
        {
            owner.UpdatePaletteDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            owner.EndPaletteDrag(eventData.position, eventData.pressEventCamera);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectPaletteKind(kind);
        }
    }
}
