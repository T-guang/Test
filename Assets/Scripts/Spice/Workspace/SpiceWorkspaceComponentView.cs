using System.Collections.Generic;
using ElectricalSim.Spice.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 原型元件的 UGUI 表现层。它只保存对工作区数据的引用，移动只更新画布坐标，
    /// 不会改变电路拓扑或使求解结果过期。
    /// </summary>
    public sealed class SpiceWorkspaceComponentView : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        private readonly Dictionary<string, RectTransform> terminalRects = new Dictionary<string, RectTransform>();
        private SpiceWorkspaceController owner;
        private SpiceWorkspaceComponentData data;
        private RectTransform rectTransform;
        private Vector2 dragOffset;
        private Image background;

        public string InstanceId => data.InstanceId;
        public SpiceComponentKind Kind => data.Kind;
        public SpiceWorkspaceComponentData Data => data;

        public void Initialize(SpiceWorkspaceController workspace, SpiceWorkspaceComponentData component)
        {
            owner = workspace;
            data = component;
            rectTransform = GetComponent<RectTransform>();
            background = gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.98f);
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.58f, 0.65f, 0.74f);
            outline.effectDistance = new Vector2(1f, -1f);
            rectTransform.sizeDelta = component.Kind == SpiceComponentKind.Ground ? new Vector2(112f, 76f) : new Vector2(144f, 92f);
            rectTransform.anchoredPosition = component.Position;

            var symbol = SpiceWorkspaceUi.CreateText(transform, "Symbol", SymbolFor(component.Kind), 26, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.05f, 0.18f, 0.34f));
            SpiceWorkspaceUi.Stretch(symbol.rectTransform, new Vector2(18f, 22f), new Vector2(-18f, -4f));
            var label = SpiceWorkspaceUi.CreateText(transform, "Label", component.InstanceId, 12, FontStyle.Bold, TextAnchor.UpperCenter, new Color(0.12f, 0.18f, 0.24f));
            SpiceWorkspaceUi.Stretch(label.rectTransform, new Vector2(6f, -4f), new Vector2(-6f, 0f));

            if (component.Kind == SpiceComponentKind.Ground)
            {
                CreateTerminal(SpiceComponentModel.GroundTerminalId, new Vector2(0f, -43f));
            }
            else
            {
                CreateTerminal(SpiceComponentModel.PositiveTerminalId, new Vector2(-76f, 0f));
                CreateTerminal(SpiceComponentModel.NegativeTerminalId, new Vector2(76f, 0f));
            }
        }

        public Vector2 GetTerminalPosition(string terminalId)
        {
            return terminalRects[terminalId].anchoredPosition + rectTransform.anchoredPosition;
        }

        public void SetSelected(bool selected)
        {
            background.color = selected ? new Color(0.85f, 0.94f, 1f) : new Color(1f, 1f, 1f, 0.98f);
        }

        public void SelectTerminal(string terminalId)
        {
            owner.HandleTerminalClick(this, terminalId);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectComponent(this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner.SelectComponent(this);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.WorkspaceRect, eventData.position, eventData.pressEventCamera, out var pointer);
            dragOffset = rectTransform.anchoredPosition - pointer;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.WorkspaceRect, eventData.position, eventData.pressEventCamera, out var pointer)) return;
            var half = rectTransform.sizeDelta * 0.5f;
            var bounds = owner.WorkspaceRect.rect;
            var position = pointer + dragOffset;
            position.x = Mathf.Clamp(position.x, bounds.xMin + half.x, bounds.xMax - half.x);
            position.y = Mathf.Clamp(position.y, bounds.yMin + half.y, bounds.yMax - half.y);
            rectTransform.anchoredPosition = position;
            owner.MoveComponent(data.InstanceId, position);
        }

        private void CreateTerminal(string terminalId, Vector2 position)
        {
            var terminal = SpiceWorkspaceUi.CreateImage(transform, "Terminal_" + terminalId, new Color(0.04f, 0.45f, 0.78f));
            terminal.rectTransform.sizeDelta = new Vector2(18f, 18f);
            terminal.rectTransform.anchoredPosition = position;
            terminal.raycastTarget = true;
            terminal.gameObject.AddComponent<SpiceWorkspaceTerminalClick>().Initialize(this, terminalId);
            terminalRects.Add(terminalId, terminal.rectTransform);
        }

        private static string SymbolFor(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? "V" : kind == SpiceComponentKind.Resistor ? "R" : kind == SpiceComponentKind.Capacitor ? "C" : kind == SpiceComponentKind.Inductor ? "L" : "GND";
        }
    }

    public sealed class SpiceWorkspaceTerminalClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceComponentView component;
        private string terminalId;

        public void Initialize(SpiceWorkspaceComponentView owner, string terminal)
        {
            component = owner;
            terminalId = terminal;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            component.SelectTerminal(terminalId);
        }
    }
}
