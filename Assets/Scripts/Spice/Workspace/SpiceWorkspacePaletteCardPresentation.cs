using System;
using System.Globalization;
using ElectricalSim.Platform;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    // 元件池卡片的既有悬停与按下视觉；不修改电路模型。
    internal sealed class SpiceWorkspacePaletteCardPresentation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image image;
        private bool hovered;
        private bool pressed;

        public void Initialize(Image target)
        {
            image = target;
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            Apply();
        }

        private void Apply()
        {
            if (image == null) return;
            image.color = pressed ? MainUiTheme.Hex("DBEAFE") : hovered ? MainUiTheme.Hex("F8FBFF") : Color.white;
        }
    }
}
