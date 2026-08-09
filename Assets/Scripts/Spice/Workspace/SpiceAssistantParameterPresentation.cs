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
    // 助手参数区只重新投影当前元件选择已有的可读信息；参数编辑和模型校验仍走正式 Workspace 参数写入/模型入口。
    // 这里的标签、单位和说明文本不是 SpiceComponent 的 stable id、terminal id 或 netlist token，不能被回写为电气事实。
    internal sealed class SpiceAssistantParameterPresentation : MonoBehaviour
    {
        private Text title;
        private Text subtitle;
        private InputField input;
        private Button unitButton;
        private GameObject applyButton;
        private string lastTitle;
        private bool lastInteractable;

        public void Initialize(Text titleText, Text subtitleText, InputField parameterInput, Button unit, GameObject apply)
        {
            title = titleText;
            subtitle = subtitleText;
            input = parameterInput;
            unitButton = unit;
            applyButton = apply;
            lastTitle = null;
            Apply();
        }

        private void LateUpdate()
        {
            if (title == null) return;
            var interactable = input != null && input.interactable;
            if (lastTitle == title.text && lastInteractable == interactable) return;
            Apply();
        }

        private void Apply()
        {
            if (title == null) return;

            var rawTitle = string.IsNullOrWhiteSpace(title.text) ? "参数设置" : title.text;
            var hasSelection = input != null && input.interactable;
            var componentText = rawTitle.EndsWith(" 参数设置", StringComparison.Ordinal)
                ? rawTitle.Substring(0, rawTitle.Length - " 参数设置".Length)
                : string.Empty;

            title.text = "参数设置";
            if (subtitle != null)
            {
                subtitle.text = hasSelection && !string.IsNullOrEmpty(componentText)
                    ? "元件：" + componentText
                    : "请选择画布中的元件以编辑参数";
                subtitle.color = hasSelection ? MainUiTheme.SecondaryText : MainUiTheme.MutedText;
            }

            if (input != null) input.gameObject.SetActive(hasSelection);
            if (unitButton != null) unitButton.gameObject.SetActive(hasSelection);
            if (applyButton != null) applyButton.SetActive(hasSelection);

            lastTitle = title.text;
            lastInteractable = hasSelection;
        }
    }
}
