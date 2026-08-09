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
    // 助手结果区只把控制器已经形成的结果/诊断文本投影为可滚动 UI；不生成、解析或重新解释 SPICE 电气事实。
    // 轮询只同步视觉文本和运行中状态，不能借此改变 Workspace result state 或覆盖错误诊断。
    internal sealed class SpiceAssistantOutcomePresentation : MonoBehaviour
    {
        private const string EmptyOutcome = "尚无仿真结果\n完成接线后点击“运行计算”";
        private const string RunningOutcome = "正在计算……";

        private Text outcomeText;
        private Text diagnosticText;
        private Button runButton;
        private ScrollRect scrollRect;

        public void Initialize(Text result, Text diagnostics, Button run)
        {
            outcomeText = result;
            diagnosticText = diagnostics;
            runButton = run;
            scrollRect = result != null ? result.GetComponentInParent<ScrollRect>() : null;
            Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        internal void RefreshNow()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (outcomeText == null) return;

            var diagnostics = diagnosticText != null ? diagnosticText.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                Render(diagnostics, MainUiTheme.DangerRed);
                return;
            }

            if (runButton != null && !runButton.interactable)
            {
                Render(RunningOutcome, MainUiTheme.MutedText);
                return;
            }

            var source = outcomeText.text;
            Render(string.IsNullOrWhiteSpace(source) || source == "尚无结果" ? EmptyOutcome : source, MainUiTheme.NormalText);
        }

        private void Render(string value, Color color)
        {
            var contentChanged = outcomeText.text != value || outcomeText.color != color;
            if (!contentChanged) return;

            outcomeText.color = color;
            SpiceScrollableTextLayout.Refresh(scrollRect, outcomeText, value, true);
        }
    }
}
