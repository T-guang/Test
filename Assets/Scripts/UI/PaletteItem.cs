using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ElectricalSim.Core;

namespace ElectricalSim.UI
{
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
            if (workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，解锁后再添加元件。");
                return;
            }

            workspace.SpawnComponent(definition, Vector2.zero);
            workspace.SetStatus("已添加元件：" + definition.displayName);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
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
            if (workspace.IsInteractionLocked)
            {
                return;
            }

            if (dragPreview != null)
            {
                Destroy(dragPreview.gameObject);
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

