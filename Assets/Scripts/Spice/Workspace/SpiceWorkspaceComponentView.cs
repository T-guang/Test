using System;
using System.Collections.Generic;
using System.Globalization;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 原型元件的 UGUI 表现层。SymbolRoot 承载可旋转的符号、端子和参考标记；
    /// AnnotationRoot 保持水平显示设计编号和工程单位参数。旋转只改变视觉位置，
    /// 不写入 SpiceCircuitModel，也不会使 DC 结果过期。
    /// </summary>
    public sealed class SpiceWorkspaceComponentView : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private readonly Dictionary<string, RectTransform> terminalRects = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Image> terminalImages = new Dictionary<string, Image>();
        private SpiceWorkspaceController owner;
        private SpiceWorkspaceComponentData data;
        private RectTransform rectTransform;
        private RectTransform symbolRoot;
        private RectTransform annotationRoot;
        private Image selectionFrame;
        private Outline selectionOutline;
        private Text summaryText;
        private Vector2 dragOffset;
        private int rotationQuarterTurns;
        private bool dragOccurred;

        public string InstanceId => data.InstanceId;
        public SpiceComponentKind Kind => data.Kind;
        public SpiceWorkspaceComponentData Data => data;
        public int RotationQuarterTurns => rotationQuarterTurns;

        public static Vector2 SizeFor(SpiceComponentKind kind) => new Vector2(180f, 180f);

        public void Initialize(SpiceWorkspaceController workspace, SpiceWorkspaceComponentData component)
        {
            owner = workspace;
            data = component;
            rectTransform = GetComponent<RectTransform>();
            rectTransform.sizeDelta = SizeFor(component.Kind);
            rectTransform.anchoredPosition = component.Position;
            var interactionSurface = gameObject.AddComponent<Image>();
            interactionSurface.color = new Color(1f, 1f, 1f, 0f);
            interactionSurface.raycastTarget = true;

            BuildSelectionFrame();
            BuildSymbolRoot();
            BuildAnnotationRoot();
            RefreshAnnotation();
            ApplyRotation();
        }

        public Vector2 GetTerminalPosition(string terminalId)
        {
            var world = symbolRoot.TransformPoint(terminalRects[terminalId].localPosition);
            return owner.WorkspaceRect.InverseTransformPoint(world);
        }

        public Vector2 GetTerminalDirection(string terminalId)
        {
            var local = terminalRects[terminalId].localPosition;
            return symbolRoot.TransformDirection(local.normalized);
        }

        public void SetSelected(bool selected)
        {
            selectionOutline.effectColor = selected ? MainUiTheme.PrimaryBlue : MainUiTheme.Divider;
        }

        public void SetTerminalHighlighted(string terminalId, SpiceTerminalHighlightState state)
        {
            if (!terminalImages.TryGetValue(terminalId, out var terminal)) return;
            terminal.color = state == SpiceTerminalHighlightState.Valid ? MainUiTheme.SuccessGreen : state == SpiceTerminalHighlightState.Invalid ? MainUiTheme.DangerRed : MainUiTheme.PrimaryBlue;
            terminal.rectTransform.sizeDelta = state == SpiceTerminalHighlightState.None ? new Vector2(16f, 16f) : new Vector2(22f, 22f);
        }

        public void RefreshAnnotation()
        {
            if (summaryText != null) summaryText.text = SpiceWorkspaceDisplay.FormatParameter(data.Kind, data.SiValue);
        }

        public void RotateClockwise()
        {
            rotationQuarterTurns = (rotationQuarterTurns + 1) % 4;
            ApplyRotation();
        }

        public void SelectTerminal(string terminalId) => owner.HandleTerminalClick(this, terminalId);
        public void HoverTerminal(string terminalId, bool entered) => owner.HandleTerminalHover(this, terminalId, entered);

        public void OnPointerDown(PointerEventData eventData)
        {
            dragOccurred = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (dragOccurred || eventData.dragging)
            {
                return;
            }

            owner.HandleComponentPointerClick(this, eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragOccurred = true;
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

        public void OnEndDrag(PointerEventData eventData)
        {
        }

        private void BuildSelectionFrame()
        {
            selectionFrame = SpiceWorkspaceUi.CreateImage(transform, "SelectionFrame", new Color(1f, 1f, 1f, 0f));
            SpiceWorkspaceUi.Stretch(selectionFrame.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            selectionFrame.raycastTarget = false;
            selectionOutline = selectionFrame.gameObject.AddComponent<Outline>();
            selectionOutline.effectColor = MainUiTheme.Divider;
            selectionOutline.effectDistance = new Vector2(1f, -1f);
        }

        private void BuildSymbolRoot()
        {
            symbolRoot = new GameObject("SymbolRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            symbolRoot.SetParent(transform, false);
            symbolRoot.sizeDelta = new Vector2(156f, 96f);
            symbolRoot.anchoredPosition = Vector2.zero;
            BuildSchematicSymbol();

            if (data.Kind == SpiceComponentKind.Ground)
            {
                CreateTerminal(SpiceComponentModel.GroundTerminalId, new Vector2(0f, 42f));
            }
            else
            {
                CreateTerminal(SpiceComponentModel.PositiveTerminalId, new Vector2(-82f, 0f));
                CreateTerminal(SpiceComponentModel.NegativeTerminalId, new Vector2(82f, 0f));
                CreateReferenceLabel("PositiveReference", data.Kind == SpiceComponentKind.DcCurrentSource ? "P" : "+", new Vector2(-62f, 18f));
                CreateReferenceLabel("NegativeReference", data.Kind == SpiceComponentKind.DcCurrentSource ? "N" : "-", new Vector2(62f, 18f));
            }
        }

        private void BuildAnnotationRoot()
        {
            annotationRoot = new GameObject("AnnotationRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            annotationRoot.SetParent(transform, false);
            SpiceWorkspaceUi.Stretch(annotationRoot, Vector2.zero, Vector2.zero);
            var label = SpiceWorkspaceUi.CreateText(annotationRoot, "Designator", DesignatorFor(data), 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.DeepText);
            label.rectTransform.sizeDelta = new Vector2(90f, 22f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 56f);
            summaryText = SpiceWorkspaceUi.CreateText(annotationRoot, "Summary", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleCenter, MainUiTheme.MutedText);
            summaryText.rectTransform.sizeDelta = new Vector2(116f, 22f);
            summaryText.rectTransform.anchoredPosition = new Vector2(0f, -56f);
        }

        private void ApplyRotation()
        {
            symbolRoot.localRotation = Quaternion.Euler(0f, 0f, -90f * rotationQuarterTurns);
            var label = annotationRoot.Find("Designator").GetComponent<RectTransform>();
            var summary = summaryText.rectTransform;
            if (data.Kind == SpiceComponentKind.Ground)
            {
                label.anchoredPosition = rotationQuarterTurns == 0 ? new Vector2(0f, 70f) : rotationQuarterTurns == 1 ? new Vector2(70f, 16f) : rotationQuarterTurns == 2 ? new Vector2(0f, -70f) : new Vector2(-70f, 16f);
                summary.anchoredPosition = rotationQuarterTurns == 0 ? new Vector2(0f, -58f) : rotationQuarterTurns == 1 ? new Vector2(70f, -12f) : rotationQuarterTurns == 2 ? new Vector2(0f, 58f) : new Vector2(-70f, -12f);
                return;
            }
            var vertical = rotationQuarterTurns % 2 != 0;
            label.anchoredPosition = vertical ? new Vector2(56f, 16f) : new Vector2(0f, 56f);
            summary.anchoredPosition = vertical ? new Vector2(56f, -12f) : new Vector2(0f, -56f);
        }

        private void BuildSchematicSymbol()
        {
            var symbol = new GameObject("Symbol", typeof(RectTransform));
            symbol.transform.SetParent(symbolRoot, false);
            var symbolRect = symbol.GetComponent<RectTransform>();
            symbolRect.sizeDelta = new Vector2(104f, 52f);
            symbolRect.anchoredPosition = Vector2.zero;
            switch (data.Kind)
            {
                case SpiceComponentKind.DcVoltageSource:
                    CreateCircle(symbol.transform, 21f, Vector2.zero);
                    CreateLine(symbol.transform, new Vector2(-13f, 0f), new Vector2(13f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(0f, -13f), new Vector2(0f, 13f), 3f);
                    break;
                case SpiceComponentKind.DcCurrentSource:
                    CreateCircle(symbol.transform, 21f, Vector2.zero);
                    CreateLine(symbol.transform, new Vector2(-14f, 0f), new Vector2(14f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(8f, -8f), new Vector2(14f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(8f, 8f), new Vector2(14f, 0f), 3f);
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
                    for (var i = 0; i < 4; i++) CreateCircle(symbol.transform, 9f, new Vector2(-27f + i * 18f, 0f));
                    break;
                case SpiceComponentKind.Ground:
                    CreateLine(symbol.transform, new Vector2(0f, 34f), new Vector2(0f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(-28f, 0f), new Vector2(28f, 0f), 3f);
                    CreateLine(symbol.transform, new Vector2(-18f, -8f), new Vector2(18f, -8f), 3f);
                    CreateLine(symbol.transform, new Vector2(-8f, -16f), new Vector2(8f, -16f), 3f);
                    break;
            }
        }

        private static void CreateCircle(Transform parent, float radius, Vector2 position)
        {
            const int segmentCount = 12;
            for (var index = 0; index < segmentCount; index++)
            {
                var startAngle = index * Mathf.PI * 2f / segmentCount;
                var endAngle = (index + 1) * Mathf.PI * 2f / segmentCount;
                var from = position + new Vector2(Mathf.Cos(startAngle), Mathf.Sin(startAngle)) * radius;
                var to = position + new Vector2(Mathf.Cos(endAngle), Mathf.Sin(endAngle)) * radius;
                CreateLine(parent, from, to, 2.5f);
            }
        }

        private static void CreateLine(Transform parent, Vector2 from, Vector2 to, float thickness)
        {
            var line = SpiceWorkspaceUi.CreateImage(parent, "Line", MainUiTheme.PrimaryBlue);
            var delta = to - from;
            line.rectTransform.sizeDelta = new Vector2(delta.magnitude <= 0.001f ? thickness : delta.magnitude, thickness);
            line.rectTransform.anchoredPosition = (from + to) * 0.5f;
            line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            line.raycastTarget = false;
        }

        private void CreateTerminal(string terminalId, Vector2 position)
        {
            var terminal = SpiceWorkspaceUi.CreateImage(symbolRoot, "Terminal_" + terminalId, MainUiTheme.PrimaryBlue);
            terminal.rectTransform.sizeDelta = new Vector2(16f, 16f);
            terminal.rectTransform.anchoredPosition = position;
            terminal.raycastTarget = true;
            terminal.gameObject.AddComponent<SpiceWorkspaceTerminalClick>().Initialize(this, terminalId);
            terminalRects.Add(terminalId, terminal.rectTransform);
            terminalImages.Add(terminalId, terminal);
        }

        private void CreateReferenceLabel(string name, string value, Vector2 position)
        {
            var label = SpiceWorkspaceUi.CreateText(symbolRoot, name, value, 12, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.MutedText);
            label.rectTransform.sizeDelta = new Vector2(18f, 18f);
            label.rectTransform.anchoredPosition = position;
            label.raycastTarget = false;
        }

        private static string DesignatorFor(SpiceWorkspaceComponentData component)
        {
            var digits = component.InstanceId.Substring(component.InstanceId.LastIndexOf('-') + 1);
            var index = int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
            var prefix = component.Kind == SpiceComponentKind.DcVoltageSource ? "V" : component.Kind == SpiceComponentKind.DcCurrentSource ? "I" : component.Kind == SpiceComponentKind.Resistor ? "R" : component.Kind == SpiceComponentKind.Capacitor ? "C" : component.Kind == SpiceComponentKind.Inductor ? "L" : "GND";
            return prefix + index.ToString(CultureInfo.InvariantCulture);
        }
    }

    public static class SpiceWorkspaceDisplay
    {
        public static string FormatParameter(SpiceComponentKind kind, double value)
        {
            if (kind == SpiceComponentKind.DcVoltageSource) return Format(value) + " V";
            if (kind == SpiceComponentKind.DcCurrentSource) return value >= 1d ? Format(value) + " A" : value >= 1e-3d ? Format(value / 1e-3d) + " mA" : Format(value / 1e-6d) + " μA";
            if (kind == SpiceComponentKind.Resistor) return value >= 1000000d ? Format(value / 1000000d) + " MΩ" : value >= 1000d ? Format(value / 1000d) + " kΩ" : Format(value) + " Ω";
            if (kind == SpiceComponentKind.Capacitor) return value < 1e-9d ? Format(value / 1e-12d) + " pF" : value < 1e-6d ? Format(value / 1e-9d) + " nF" : value < 1e-3d ? Format(value / 1e-6d) + " μF" : value < 1d ? Format(value / 1e-3d) + " mF" : Format(value) + " F";
            if (kind == SpiceComponentKind.Inductor) return value < 1e-3d ? Format(value / 1e-6d) + " μH" : value < 1d ? Format(value / 1e-3d) + " mH" : Format(value) + " H";
            return "GND";
        }

        private static string Format(double value) => value.ToString("G4", CultureInfo.InvariantCulture);
    }

    public enum SpiceTerminalHighlightState { None, Valid, Invalid }

    public sealed class SpiceWorkspaceTerminalClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private SpiceWorkspaceComponentView component;
        private string terminalId;
        public void Initialize(SpiceWorkspaceComponentView owner, string terminal) { component = owner; terminalId = terminal; }
        public void OnPointerClick(PointerEventData eventData) => component.SelectTerminal(terminalId);
        public void OnPointerEnter(PointerEventData eventData) => component.HoverTerminal(terminalId, true);
        public void OnPointerExit(PointerEventData eventData) => component.HoverTerminal(terminalId, false);
    }
}
