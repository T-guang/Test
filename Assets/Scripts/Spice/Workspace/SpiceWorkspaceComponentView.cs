using System.Collections.Generic;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 原型元件的 UGUI 表现层。数据和端子标识仍由工作区模型拥有；
    /// 视图拖动只更新画布位置，因此不会使数值结果过期。
    /// </summary>
    public sealed class SpiceWorkspaceComponentView : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler
    {
        private readonly Dictionary<string, RectTransform> terminalRects = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Image> terminalImages = new Dictionary<string, Image>();
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
            background.raycastTarget = true;
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            rectTransform.sizeDelta = component.Kind == SpiceComponentKind.Ground ? new Vector2(108f, 80f) : new Vector2(156f, 96f);
            rectTransform.anchoredPosition = component.Position;

            BuildSchematicSymbol();
            var label = SpiceWorkspaceUi.CreateText(transform, "Label", component.InstanceId, 12, FontStyle.Bold, TextAnchor.UpperCenter, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Stretch(label.rectTransform, new Vector2(8f, -5f), new Vector2(-8f, 0f));
            var summary = SpiceWorkspaceUi.CreateText(transform, "Summary", SummaryFor(component), 12, FontStyle.Normal, TextAnchor.LowerCenter, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Stretch(summary.rectTransform, new Vector2(8f, 1f), new Vector2(-8f, 20f));

            if (component.Kind == SpiceComponentKind.Ground)
            {
                CreateTerminal(SpiceComponentModel.GroundTerminalId, new Vector2(0f, -45f));
            }
            else
            {
                CreateTerminal(SpiceComponentModel.PositiveTerminalId, new Vector2(-82f, 0f));
                CreateTerminal(SpiceComponentModel.NegativeTerminalId, new Vector2(82f, 0f));
            }
        }

        public Vector2 GetTerminalPosition(string terminalId)
        {
            return terminalRects[terminalId].anchoredPosition + rectTransform.anchoredPosition;
        }

        public void SetSelected(bool selected)
        {
            background.color = selected ? MainUiTheme.SelectedBlue : new Color(1f, 1f, 1f, 0.98f);
        }

        public void SetTerminalHighlighted(string terminalId, bool highlighted)
        {
            if (!terminalImages.TryGetValue(terminalId, out var terminal)) return;
            terminal.color = highlighted ? MainUiTheme.SuccessGreen : MainUiTheme.PrimaryBlue;
            terminal.rectTransform.sizeDelta = highlighted ? new Vector2(22f, 22f) : new Vector2(16f, 16f);
        }

        public void SelectTerminal(string terminalId)
        {
            owner.HandleTerminalClick(this, terminalId);
        }

        public void HoverTerminal(string terminalId, bool entered)
        {
            owner.HandleTerminalHover(this, terminalId, entered);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner.SelectComponent(this);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner.SelectComponent(this);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(owner.WorkspaceRect, eventData.position, eventData.pressEventCamera, out var pointer)) return;
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

        private void BuildSchematicSymbol()
        {
            var symbol = new GameObject("Symbol", typeof(RectTransform));
            symbol.transform.SetParent(transform, false);
            var symbolRect = symbol.GetComponent<RectTransform>();
            SpiceWorkspaceUi.Anchor(symbolRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-52f, -21f), new Vector2(52f, 25f));
            switch (data.Kind)
            {
                case SpiceComponentKind.DcVoltageSource:
                    CreateCircle(symbol.transform, new Vector2(42f, 42f), Vector2.zero);
                    CreateLine(symbol.transform, new Vector2(-13f, 0f), new Vector2(13f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(0f, -13f), new Vector2(0f, 13f), 3f);
                    break;
                case SpiceComponentKind.Resistor:
                    var resistor = SpiceWorkspaceUi.CreateImage(symbol.transform, "Resistor", Color.white);
                    resistor.rectTransform.sizeDelta = new Vector2(64f, 24f);
                    resistor.rectTransform.anchoredPosition = Vector2.zero;
                    var resistorOutline = resistor.gameObject.AddComponent<Outline>();
                    resistorOutline.effectColor = MainUiTheme.PrimaryBlue;
                    resistorOutline.effectDistance = new Vector2(2f, -2f);
                    break;
                case SpiceComponentKind.Capacitor:
                    CreateLine(symbol.transform, new Vector2(-7f, -14f), new Vector2(-7f, 14f), 3f);
                    CreateLine(symbol.transform, new Vector2(7f, -14f), new Vector2(7f, 14f), 3f);
                    break;
                case SpiceComponentKind.Inductor:
                    for (var i = 0; i < 4; i++) CreateCircle(symbol.transform, new Vector2(18f, 18f), new Vector2(-27f + i * 18f, 0f));
                    break;
                case SpiceComponentKind.Ground:
                    CreateLine(symbol.transform, new Vector2(-28f, 8f), new Vector2(28f, 8f), 3f);
                    CreateLine(symbol.transform, new Vector2(-18f, 0f), new Vector2(18f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(-8f, -8f), new Vector2(8f, -8f), 3f);
                    break;
            }
        }

        private static void CreateCircle(Transform parent, Vector2 size, Vector2 position)
        {
            var circle = SpiceWorkspaceUi.CreateImage(parent, "Coil", Color.white);
            circle.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            circle.rectTransform.sizeDelta = size;
            circle.rectTransform.anchoredPosition = position;
            var outline = circle.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.PrimaryBlue;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        private static void CreateLine(Transform parent, Vector2 from, Vector2 to, float thickness)
        {
            var line = SpiceWorkspaceUi.CreateImage(parent, "Line", MainUiTheme.PrimaryBlue);
            var delta = to - from;
            line.rectTransform.sizeDelta = new Vector2(delta.magnitude <= 0.001f ? thickness : delta.magnitude, thickness);
            line.rectTransform.anchoredPosition = (from + to) * 0.5f;
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void CreateTerminal(string terminalId, Vector2 position)
        {
            var terminal = SpiceWorkspaceUi.CreateImage(transform, "Terminal_" + terminalId, MainUiTheme.PrimaryBlue);
            terminal.rectTransform.sizeDelta = new Vector2(16f, 16f);
            terminal.rectTransform.anchoredPosition = position;
            terminal.raycastTarget = true;
            terminal.gameObject.AddComponent<SpiceWorkspaceTerminalClick>().Initialize(this, terminalId);
            terminalRects.Add(terminalId, terminal.rectTransform);
            terminalImages.Add(terminalId, terminal);
        }

        private static string SummaryFor(SpiceWorkspaceComponentData component)
        {
            return component.Kind == SpiceComponentKind.DcVoltageSource ? component.SiValue.ToString("G4") + " V" :
                component.Kind == SpiceComponentKind.Resistor ? component.SiValue / 1000d + " kOhm" :
                component.Kind == SpiceComponentKind.Capacitor ? component.SiValue / 1e-6d + " uF" :
                component.Kind == SpiceComponentKind.Inductor ? component.SiValue / 1e-3d + " mH" : "GND";
        }
    }

    public sealed class SpiceWorkspaceTerminalClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private SpiceWorkspaceComponentView component;
        private string terminalId;

        public void Initialize(SpiceWorkspaceComponentView owner, string terminal)
        {
            component = owner;
            terminalId = terminal;
        }

        public void OnPointerClick(PointerEventData eventData) => component.SelectTerminal(terminalId);
        public void OnPointerEnter(PointerEventData eventData) => component.HoverTerminal(terminalId, true);
        public void OnPointerExit(PointerEventData eventData) => component.HoverTerminal(terminalId, false);
    }
}
