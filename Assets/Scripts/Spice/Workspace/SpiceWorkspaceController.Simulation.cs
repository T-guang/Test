using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    // 仿真请求、revision/cancellation 防线与结果、网表、诊断展示资格；不处理画布交互或文件对话框。
    public sealed partial class SpiceWorkspaceController
    {
        public async Task<SpiceSimulationResult> RunCalculationAsync()
        {
            EnsureInitialized();
            if (ResultState == SpiceWorkspaceResultState.Running) return null;

            CancelPendingWire();
            CancelPaletteDrag();
            var previousState = ResultState;
            var requestId = ++calculationRequestId;
            var revisionAtStart = electricalRevision;
            var modelAtStart = Model;
            ResultState = SpiceWorkspaceResultState.Running;
            activeCalculationRequestId = requestId;
            runButton.interactable = false;
            statusText.text = "计算中...";
            lastOutcomeText = null;
            RefreshCopyResultButton();
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            RefreshNetlistUi();
            // 文件工作流：进入 Running 时禁用保存/另存为/导入按钮（不依赖 Update 轮询）。
            RefreshResultStateDependentControls();

            CancellationTokenSource localCancellation = null;
            try
            {
                localCancellation = new CancellationTokenSource();
                simulationCancellation = localCancellation;

                var result = await SimulateCircuitAsync(modelAtStart.BuildCircuitModel(), localCancellation.Token);

                if (!CanCommitCalculationResult(requestId, revisionAtStart, modelAtStart, localCancellation))
                {
                    DiscardOutdatedCalculation(requestId, revisionAtStart, modelAtStart);
                    return null;
                }

                generatedNetlistContent = result.GeneratedNetlistContent;
                generatedNetlistRevision = string.IsNullOrEmpty(generatedNetlistContent)
                    ? -1
                    : electricalRevision;
                if (result.Success)
                {
                    ResultState = SpiceWorkspaceResultState.Current;
                    statusText.text = "结果有效";
                    var formattedResult = FormatResult(result);
                    lastOutcomeText = string.IsNullOrEmpty(formattedResult) ? null : formattedResult;
                    SetResultText(formattedResult);
                }
                else
                {
                    ResultState = SpiceWorkspaceResultState.Failed;
                    statusText.text = "计算失败";
                    var formattedDiagnostics = FormatDiagnostics(result);
                    lastOutcomeText = string.IsNullOrEmpty(formattedDiagnostics) ? null : formattedDiagnostics;
                    SetDiagnosticText(formattedDiagnostics);
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                if (IsActiveCalculationRequest(requestId) && !shuttingDown)
                {
                    if (revisionAtStart != electricalRevision || !ReferenceEquals(modelAtStart, Model))
                    {
                        DiscardOutdatedCalculation(requestId, revisionAtStart, modelAtStart);
                    }
                    else
                    {
                        ResultState = previousState;
                        statusText.text = "计算已取消";
                    }
                }
                return null;
            }
            catch (Exception exception)
            {
                if (!CanCommitCalculationResult(requestId, revisionAtStart, modelAtStart, localCancellation))
                {
                    DiscardOutdatedCalculation(requestId, revisionAtStart, modelAtStart);
                    return null;
                }

                ResultState = SpiceWorkspaceResultState.Failed;
                generatedNetlistContent = null;
                generatedNetlistRevision = -1;
                statusText.text = "计算失败";
                lastOutcomeText = UnexpectedSimulationErrorMessage;
                SetDiagnosticText(UnexpectedSimulationErrorMessage);
                Debug.LogException(new InvalidOperationException(
                    "Unexpected SPICE simulation failure. requestId=" + requestId +
                    " electricalRevision=" + revisionAtStart + ".", exception));
                return null;
            }
            finally
            {
                if (IsActiveCalculationRequest(requestId))
                {
                    if (ReferenceEquals(simulationCancellation, localCancellation))
                        simulationCancellation = null;
                    activeCalculationRequestId = 0;
                    if (!shuttingDown && runButton != null)
                    {
                        runButton.interactable = true;
                        RefreshNetlistUi();
                        RefreshCopyResultButton();
                        // 文件工作流：离开 Running 时恢复保存/另存为/导入按钮。
                        RefreshResultStateDependentControls();
                    }
                }

                if (localCancellation != null)
                    localCancellation.Dispose();
            }
        }

        public void RunCalculation() => RunFromButton();

        internal void SetSimulationOverrideForTesting(Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> simulationOverride)
        {
            simulationOverrideForTesting = simulationOverride;
        }

        internal void SetSimulationServiceForTesting(SpiceSimulationService service)
        {
            simulationService = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// 统一电气编辑的 Running 防线。新增、删除、接线和参数写入均先经过此处，
        /// 防止在途求解读取到中途变化的模型；文件会话操作使用独立的 CanImportDrawing 防线。
        /// </summary>
        private bool CanModifyElectricalModel()
        {
            if (ResultState != SpiceWorkspaceResultState.Running) return true;
            if (statusText != null) statusText.text = "仿真计算进行中，请稍后修改电路。";
            return false;
        }

        private void AdvanceElectricalRevision()
        {
            unchecked { electricalRevision++; }
        }

        private bool IsActiveCalculationRequest(long requestId)
        {
            return requestId != 0 && requestId == activeCalculationRequestId;
        }

        private bool CanCommitCalculationResult(long requestId, long revisionAtStart, SpiceWorkspaceModel modelAtStart, CancellationTokenSource localCancellation)
        {
            return !shuttingDown && localCancellation != null && !localCancellation.IsCancellationRequested &&
                IsActiveCalculationRequest(requestId) && revisionAtStart == electricalRevision && ReferenceEquals(modelAtStart, Model);
        }

        private void DiscardOutdatedCalculation(long requestId, long revisionAtStart, SpiceWorkspaceModel modelAtStart)
        {
            if (!IsActiveCalculationRequest(requestId) || shuttingDown) return;
            if (revisionAtStart == electricalRevision && ReferenceEquals(modelAtStart, Model)) return;

            lastOutcomeText = null;
            ResultState = Model.Components.Count == 0 && Model.Wires.Count == 0
                ? SpiceWorkspaceResultState.NeverRun
                : SpiceWorkspaceResultState.Stale;
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            if (statusText != null) statusText.text = StateMessage();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            RefreshAnalysisControls();
            RefreshParameterPanel();
        }

        private Task<SpiceSimulationResult> SimulateCircuitAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken)
        {
            return simulationOverrideForTesting != null
                ? simulationOverrideForTesting(circuit, cancellationToken)
                : simulationService.SimulateAsync(circuit, cancellationToken);
        }

        // 网表仅显示 SpiceNetlistBuilder 已生成的原始文本，UI 不自行重建近似内容。
        private void ToggleNetlist()
        {
            netlistExpanded = !netlistExpanded;
            RefreshNetlistUi();
        }

        private void CopyNetlist()
        {
            if (!TryGetCopyableNetlistText(out var text)) return;
            GUIUtility.systemCopyBuffer = text;
            statusText.text = "已复制网表";
        }

        /// <summary>
        /// 获取当前电气修订可复制的正式网表。显示中的旧网表可以保留用于对比，
        /// 但只有修订一致且未处于运行中时才允许复制。
        /// </summary>
        public bool TryGetCopyableNetlistText(out string text)
        {
            var isCurrentRevision = generatedNetlistRevision == electricalRevision;
            text = isCurrentRevision && ResultState != SpiceWorkspaceResultState.Running
                ? generatedNetlistContent
                : null;
            return !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// 获取当前可复制的正式结果/阻断诊断文本。
        /// 文本来源为同一 Presentation 的权威输出（与正式可见 ResultText 一致），
        /// 不读取隐藏 DiagnosticRoot，不重新格式化结果。
        /// 仅在 ResultState 为 Current 或 Failed 且存在实际内容时可复制；
        /// NeverRun、Running、Stale、空文本或占位文本时不可复制。
        /// </summary>
        public bool TryGetCopyableOutcomeText(out string text)
        {
            var isCurrentOutcome = ResultState == SpiceWorkspaceResultState.Current || ResultState == SpiceWorkspaceResultState.Failed;
            text = isCurrentOutcome ? lastOutcomeText : null;
            return !string.IsNullOrEmpty(text);
        }

        private void CopyResult()
        {
            if (!TryGetCopyableOutcomeText(out var text)) return;
            GUIUtility.systemCopyBuffer = text;
            statusText.text = "结果已复制";
        }

        private void RefreshCopyResultButton()
        {
            if (copyResultButton != null) copyResultButton.interactable = TryGetCopyableOutcomeText(out _);
        }

        private void RefreshNetlistUi()
        {
            if (netlistView == null) return;
            var hasNetlist = !string.IsNullOrEmpty(generatedNetlistContent);
            netlistView.ScrollRect.gameObject.SetActive(netlistExpanded);
            if (netlistExpanded) RefreshScrollableText(netlistView, generatedNetlistContent ?? string.Empty);
            else netlistText.text = generatedNetlistContent ?? string.Empty;
            netlistToggleButton.GetComponentInChildren<Text>().text = netlistExpanded ? "收起" : "展开";
            copyNetlistButton.interactable = TryGetCopyableNetlistText(out _);
            netlistStatusText.text = NetlistStatusMessage(hasNetlist);
        }


        private string NetlistStatusMessage(bool hasNetlist)
        {
            if (ResultState == SpiceWorkspaceResultState.NeverRun) return "尚未生成网表。";
            if (ResultState == SpiceWorkspaceResultState.Running) return "正在生成本次网表。";
            if (ResultState == SpiceWorkspaceResultState.Stale && hasNetlist) return "该网表对应修改前的电路，已过期。";
            if (ResultState == SpiceWorkspaceResultState.Failed) return hasNetlist ? "本次网表已生成，但执行或解析失败。" : "当前电路未通过校验，尚未生成新网表。";
            return hasNetlist ? "当前计算使用的网表。" : "尚未生成网表。";
        }


        private async void RunFromButton() => await RunCalculationAsync();


        private string StateMessage()
        {
            return ResultState == SpiceWorkspaceResultState.Stale ? "结果已过期" : ResultState == SpiceWorkspaceResultState.Failed ? "计算失败" : ResultState == SpiceWorkspaceResultState.Current ? "结果有效" : "未计算";
        }

        private static string PaletteLabel(SpiceComponentKind kind)
        {
            if (kind == SpiceComponentKind.AcVoltageSource) return "交流电压源";
            if (kind == SpiceComponentKind.IdealOperationalAmplifier) return "理想运算放大器";
            return kind == SpiceComponentKind.DcVoltageSource ? "直流电压源" : kind == SpiceComponentKind.DcCurrentSource ? "直流电流源" : kind == SpiceComponentKind.IdealSwitch ? "理想开关" : kind == SpiceComponentKind.SiliconDiode ? "通用硅二极管" : kind == SpiceComponentKind.Resistor ? "电阻" : kind == SpiceComponentKind.Capacitor ? "电容" : kind == SpiceComponentKind.Inductor ? "电感" : kind == SpiceComponentKind.VoltageProbe ? "电压探针" : kind == SpiceComponentKind.CurrentProbe ? "电流探针" : "接地";
        }

        private static string FormatResult(SpiceSimulationResult result)
        {
            if (result.AnalysisSettings.Mode == SpiceAnalysisMode.AcSingleFrequency)
            {
                return SpiceAcResultFormatter.Format(result);
            }

            return string.Join("\n\n", result.ComponentResults.Values.OrderBy(value => value.ComponentId, StringComparer.Ordinal).Select(value =>
                value.ComponentId + "  " + value.ComponentKind + "\n" + VoltageLabel(value) + "  " + value.Voltage.ToString("G6", CultureInfo.InvariantCulture) + " V\n" + FormatCurrentLine(value) + "参考方向：" + DirectionLabel(value.CurrentDirection) +
                (string.IsNullOrEmpty(value.Notes) ? string.Empty : "\n" + value.Notes)));
        }

        private static string VoltageLabel(SpiceComponentResult value)
        {
            // 二极管电压按 A→K 报告为 VAK，电压探针按 V+→V- 报告为差分电压，避免与“正端 → 负端”通用文案混淆极性。
            return value.ComponentKind == "SiliconDiode" ? "VAK" : value.ComponentKind == "VoltageProbe" ? "差分电压" : "电压";
        }

        private static string FormatCurrentLine(SpiceComponentResult value)
        {
            // 电压探针不注入电流，内部 Current=0 仅为占位值，不得作为测量值展示，故整行省略。
            if (value.ComponentKind == "VoltageProbe") return string.Empty;
            return "电流  " + value.Current.ToString("G6", CultureInfo.InvariantCulture) + " A\n";
        }

        private static string DirectionLabel(string direction)
        {
            return direction == "A-to-K" ? "A → K"
                : direction == "P-to-N" ? "P → N"
                : direction == "A-to-B" ? "A → B"
                : direction == "V-plus-to-V-minus" ? "V+ → V-"
                : direction == "IN-to-OUT" ? "IN → OUT"
                : direction != null && direction.StartsWith("OUT-to-GND", StringComparison.Ordinal) ? "OUT → GND（ngspice 支路约定）"
                : "正端 → 负端";
        }

        private static string FormatDiagnostics(SpiceSimulationResult result)
        {
            return string.Join("\n\n", result.Diagnostics.Select(FormatDiagnostic));
        }

        private static string FormatDiagnostic(SpiceDiagnostic diagnostic)
        {
            var title = diagnostic.Code == "SPICE_GROUND_MISSING" ? "缺少接地参考" :
                diagnostic.Code == "SPICE_FLOATING_TERMINAL" ? "存在悬空端子" :
                diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT" ? "存在未接地子电路" :
                diagnostic.Code == "SPICE_INVALID_PARAMETER" ? "元件参数无效" :
                diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION" ? "同一元件端子不能直接连接" :
                diagnostic.Code == "SPICE_SOURCE_MISSING" ? "缺少直流电压源" :
                diagnostic.Code == "SPICE_SOURCE_SHORTED" ? "电压源两端短接" :
                diagnostic.Code == "SPICE_OPAMP_OUTPUT_SHORTED" ? "运放输出端短接" :
                diagnostic.Code == "SPICE_COMPONENT_SHORTED" ? "元件两端短接" : "SPICE 计算诊断";
            var detail = diagnostic.Code == "SPICE_GROUND_MISSING" ? "电路至少需要一个 GND 作为 0 V 参考。" :
                diagnostic.Code == "SPICE_FLOATING_TERMINAL" ? "该端子尚未通过导线连接。" :
                diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT" ? "该子电路无法通过元件与导线到达 GND。" :
                diagnostic.Code == "SPICE_INVALID_PARAMETER" ? "请检查数值是否为支持范围内的 SI 参数。" :
                diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION" ? "请改为连接不同元件的端子。" :
                diagnostic.Code == "SPICE_SOURCE_MISSING" ? "当前直流工作点计算需要一个直流电压源。" :
                diagnostic.Code == "SPICE_SOURCE_SHORTED" ? "请断开电压源两端的直接短接。" :
                diagnostic.Code == "SPICE_OPAMP_OUTPUT_SHORTED" ? "理想运放 OUT 不能直接连接 GND，因为 V1 输出本身是相对于 GND 的理想受控电压源。" :
                diagnostic.Code == "SPICE_COMPONENT_SHORTED" ? "请检查该元件两端是否被同一电气节点直接连接。" : diagnostic.Message;
            var related = string.IsNullOrEmpty(diagnostic.ComponentId) ? string.Empty : "\n关联元件：" + diagnostic.ComponentId + (string.IsNullOrEmpty(diagnostic.TerminalId) ? string.Empty : " / 端子：" + diagnostic.TerminalId);
            return title + "\n" + detail + related + "\n错误码：" + diagnostic.Code;
        }
    }
}
