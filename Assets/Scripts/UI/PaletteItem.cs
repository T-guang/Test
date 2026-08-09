using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ElectricalSim.Core;

namespace ElectricalSim.UI
{
    // PaletteItem 是 Definition 的单张展示卡及其创建意图入口，不是画布上的 CircuitComponent。点击/拖放始终委托 Workspace 创建新实例，
    // 因而卡片的 RectTransform、hover 样式和 drag preview 不得参与 terminal identity、Wire endpoint 或已保存电路的判断。
    public sealed class PaletteItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private ComponentDefinition definition;
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private Text label;
        [SerializeField] private Image background;

        private Color normalColor = new Color(0.96f, 0.98f, 1f, 1f);
        private Color hoverColor = new Color(0.88f, 0.93f, 1f, 1f);
        private Outline outline;
        private Color normalOutlineColor = new Color(0.91f, 0.94f, 0.97f, 0.55f);
        private Color hoverOutlineColor = new Color(0.58f, 0.77f, 0.99f, 1f);

        public ComponentDefinition Definition => definition;

        private RectTransform dragPreview;

        public void Initialize(ComponentDefinition componentDefinition, WorkspaceController targetWorkspace)
        {
            definition = componentDefinition;
            workspace = targetWorkspace;
            if (label == null)
            {
                label = GetComponentInChildren<Text>();
            }

            if (label != null)
            {
                label.text = PaletteController.GetPaletteDisplayName(definition);
            }

            if (background == null)
            {
                background = GetComponent<Image>();
            }
        }

        public void ConfigureCardVisual(
            Image cardBackground,
            Color cardNormalColor,
            Color cardHoverColor,
            Outline cardOutline,
            Color cardNormalOutlineColor,
            Color cardHoverOutlineColor)
        {
            background = cardBackground != null ? cardBackground : GetComponent<Image>();
            normalColor = cardNormalColor;
            hoverColor = cardHoverColor;
            outline = cardOutline != null ? cardOutline : GetComponent<Outline>();
            normalOutlineColor = cardNormalOutlineColor;
            hoverOutlineColor = cardHoverOutlineColor;

            if (background != null)
            {
                background.color = normalColor;
            }

            if (outline != null)
            {
                outline.effectColor = normalOutlineColor;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // 单击只保留卡片交互，严格双击才创建一个实例；不要把 clickCount 之外的 UI 事件当作创建命令，避免拖放结束时重复生成元件。
            if (workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，解锁后再添加元件。");
                return;
            }

            // 严格双击语义：仅 clickCount == 2 时生成一次。
            // clickCount=1/3/4... 以及 null eventData 都不生成，避免双击周期内重复触发。
            if (eventData == null || eventData.clickCount != 2)
            {
                return;
            }

            workspace.SpawnComponent(definition, Vector2.zero);
            workspace.SetStatus("已添加元件：" + definition.displayName);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // 预览对象仅提供拖放反馈且关闭 raycast，真实组件只在有效 Workspace 区域内的 EndDrag 阶段创建。
            if (workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，解锁后再添加元件。");
                return;
            }

            dragPreview = new GameObject("PaletteDragPreview", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            dragPreview.SetParent(workspace.WorkspaceRect.root, false);
            dragPreview.sizeDelta = definition.size;
            var image = dragPreview.GetComponent<Image>();
            image.color = new Color(definition.bodyColor.r, definition.bodyColor.g, definition.bodyColor.b, 0.55f);
            image.raycastTarget = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (workspace.IsInteractionLocked)
            {
                return;
            }

            if (dragPreview != null)
            {
                dragPreview.position = eventData.position;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // 正常未锁定路径会先销毁视觉预览，再验证画布边界并转换坐标；落在画布外必须是无副作用操作，不能污染历史记录。
            if (workspace.IsInteractionLocked)
            {
                return;
            }

            if (dragPreview != null)
            {
                Destroy(dragPreview.gameObject);
            }

            // 严格判断指针是否位于电工画布有效矩形内。
            // 在坐标转换和 SpawnComponent 之前完成边界判断，画布外不生成、不改变历史。
            if (eventData == null || workspace.WorkspaceRect == null ||
                !RectTransformUtility.RectangleContainsScreenPoint(workspace.WorkspaceRect, eventData.position, eventData.pressEventCamera))
            {
                return;
            }

            if (workspace.TryScreenToCanvasLocal(eventData.position, eventData.pressEventCamera, out var localPoint))
            {
                workspace.SpawnComponent(definition, localPoint);
                workspace.SetStatus("已放置元件：" + definition.displayName);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (background != null)
            {
                background.color = hoverColor;
            }

            if (outline != null)
            {
                outline.effectColor = hoverOutlineColor;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (background != null)
            {
                background.color = normalColor;
            }

            if (outline != null)
            {
                outline.effectColor = normalOutlineColor;
            }
        }
    }
}

