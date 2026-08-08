using System;
using System.Collections.Generic;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Rules;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 将既有运行态、教学规则结果和可选调试信息组织为检查助手的中文报告文本。
    /// 本类不执行规则、不推进仿真，也不拥有电气状态；RuleId 与 Severity 的真实来源仍是规则和 Validation 链，最终结构化 Block 由 InspectionReportComposer 生成。
    /// 报告 Section 的标题、正文和顺序已受到 Inspector 快照保护，修改任何格式化文本后必须运行 Inspector 模型测试与 18 张模板基线。
    /// </summary>
    public static class TeachingCheckReportFormatter
    {
        public static string Format(
            CircuitStateResult stateResult,
            CircuitAnalysisResult industrialResult,
            string debugDetails,
            bool showDeveloperDebugInfo = false)
        {
            // 工业专项分析的 Errors/Warnings 必须进入"问题与风险"段落，否则顶部计数与正文不一致。
            // AppendProblems 的 AddRangeUnique 会按 Trim 精确文本去重，不会与 stateResult.Errors 重复。
            return Build(stateResult,
                industrialResult != null ? industrialResult.Errors : null,
                industrialResult != null ? industrialResult.Warnings : null,
                debugDetails, showDeveloperDebugInfo);
        }

        public static string Format(
            CircuitStateResult stateResult,
            CircuitCheckResult ruleResult,
            string debugDetails,
            bool showDeveloperDebugInfo = false)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (ruleResult != null)
            {
                // 仅把 CircuitRuleChecker 已给出的错误和提醒汇入报告，不在此处重新判断问题或升级、降低严重级别。
                for (var i = 0; i < ruleResult.issues.Count; i++)
                {
                    var issue = ruleResult.issues[i];
                    if (issue == null)
                    {
                        continue;
                    }

                    var text = string.IsNullOrWhiteSpace(issue.message) ? issue.title : issue.message;
                    if (issue.severity == CircuitIssueSeverity.Error)
                    {
                        AddUnique(errors, text);
                    }
                    else if (issue.severity == CircuitIssueSeverity.Warning)
                    {
                        AddUnique(warnings, text);
                    }
                }
            }

            return Build(stateResult, errors, warnings, debugDetails, showDeveloperDebugInfo);
        }

        private static string Build(
            CircuitStateResult stateResult,
            IEnumerable<string> additionalErrors,
            IEnumerable<string> additionalWarnings,
            string debugDetails,
            bool showDeveloperDebugInfo)
        {
            if (stateResult == null)
            {
                var fallback = "【检查结论】\n未能生成当前电路的教学化检查结论。";
                return showDeveloperDebugInfo
                    ? fallback + "\n\n【调试详情（开发者）】\n" + (debugDetails ?? string.Empty)
                    : fallback;
            }

            // Section 顺序是 Inspector 报告快照的保护对象；新增或移动段落必须先更新相应基线并完成真实模板回归。
            var builder = new StringBuilder();
            builder.AppendLine("【检查结论】");
            builder.AppendLine(BuildConclusion(stateResult, additionalErrors));

            builder.AppendLine();
            builder.AppendLine("【当前关键状态】");
            AppendKeyStates(builder, stateResult);

            builder.AppendLine();
            builder.AppendLine("【问题与风险】");
            AppendProblems(builder, stateResult, additionalErrors, additionalWarnings);

            builder.AppendLine();
            builder.AppendLine("【教学说明】");
            AppendTeachingExplanation(builder, stateResult);

            if (showDeveloperDebugInfo)
            {
                builder.AppendLine();
                builder.AppendLine("==============================");
                builder.AppendLine("【调试详情（开发者）】");
                builder.AppendLine("以下内容用于开发者排查，保留原始规则检查与 V1.x 分阶段分析。");
                builder.AppendLine();
                builder.Append(SanitizeTwoWaySwitchLanguage(debugDetails, stateResult));
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildConclusion(CircuitStateResult result, IEnumerable<string> additionalErrors)
        {
            if (HasStarDeltaConflict(result))
            {
                return "危险：检测到星形连接与三角形连接同时存在，疑似星三角短接。";
            }

            if (result.HasShortCircuit || result.HasPowerConflict)
            {
                return "危险：当前电路存在短路或电源冲突风险，请先断开电源并检查接线。";
            }

            if (result.HasContactorInterlockConflict || result.HasCompoundButtonInterlockConflict)
            {
                return "检测到正反转控制冲突或互锁状态异常，当前状态不能作为正常运行状态。";
            }

            if (HasThermalRelayOpen(result))
            {
                return "热继电器保护触点断开，控制回路被切断，电机停止。";
            }

            if (HasSelfHoldHistoryAmbiguity(result))
            {
                return "检测到接触器自锁结构。当前画布可能处于历史自锁保持运行状态。";
            }

            if (HasAny(additionalErrors) || result.Errors.Count > 0)
            {
                return "当前电路存在需要优先处理的接线或供电问题。";
            }

            var starDeltaMotor = FindStarDeltaMotor(result);
            if (starDeltaMotor != null && starDeltaMotor.State == "StarConnected")
            {
                return "当前电路处于星形启动阶段，星三角电机具备星形启动条件。";
            }

            if (starDeltaMotor != null && starDeltaMotor.State == "DeltaConnected")
            {
                return "当前电路处于三角运行阶段，星三角电机具备三角运行条件。";
            }

            if (HasRunningLoad(result))
            {
                return "当前控制与供电路径成立，负载处于运行状态。";
            }

            return "当前负载未运行。请结合下方关键状态确认这是正常停止还是控制路径尚未闭合。";
        }

        private static void AppendKeyStates(StringBuilder builder, CircuitStateResult result)
        {
            var count = 0;
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.IsContactor)
                {
                    builder.AppendLine("- " + component.DisplayName + "：线圈" +
                        (component.IsContactorCoilEnergizedByAnalyzer ? "得电" : "未得电") +
                        "，主触点" + (component.IsContactorMainContactsClosedByAnalyzer ? "闭合" : "断开") + "。");
                    count++;
                }
                else if (component.IsTimerRelay)
                {
                    builder.AppendLine("- " + component.DisplayName + "：线圈" +
                        (component.IsTimerRelayCoilEnergizedByAnalyzer ? "得电" : "未得电") +
                        "，" + TimerDelayStatusForTeaching(component.TimerDelayStatus) +
                        "；15/16 " + (component.IsTimerDelayedNcClosed ? "导通" : "断开") +
                        "，15/18 " + (component.IsTimerDelayedNoClosed ? "导通" : "断开") + "。");
                    count++;
                }
                else if (IsThermalRelay(component))
                {
                    builder.AppendLine("- " + component.DisplayName + "：保护触点" +
                        (IsThermalRelayOpen(component) ? "断开" : "导通") + "。");
                    count++;
                }
                else if (component.IsTwoWaySwitch)
                {
                    builder.AppendLine("- " + component.DisplayName + "：当前拨位 " +
                        (component.State == "TwoWayL1" ? "L-L1" : "L-L2") + "。");
                    count++;
                }
                else if (component.IsLimitSwitch)
                {
                    builder.AppendLine("- " + component.DisplayName + "：" +
                        (component.IsLimitSwitchTriggered ? "已触发" : "未触发") + "。");
                    count++;
                }
                else if (IsKnifeSwitch(component))
                {
                    builder.AppendLine("- " + component.DisplayName + "：" +
                        (component.State == "Closed"
                            ? "刀开关当前闭合，主回路经刀开关接通。"
                            : "刀开关当前断开，主回路被切断，后级电机或负载无法获得电源。"));
                    count++;
                }
                else if (IsEmergencyStop(component))
                {
                    builder.AppendLine("- " + component.DisplayName + "：" +
                        (component.State == "Closed"
                            ? "急停按钮未触发，NC 触点导通，控制回路可继续供电。"
                            : "急停按钮当前触发，NC 触点断开，控制回路被急停切断，后级负载或线圈应失电。"));
                    count++;
                }
                else if (component.SummaryGroup == ComponentStateInfo.GroupLoad)
                {
                    builder.AppendLine("- " + component.DisplayName + "：" +
                        (IsIndicator(component) ? IndicatorReadableState(component.State) : ReadableState(component.State)) + "。");
                    count++;
                }
            }

            if (count == 0)
            {
                builder.AppendLine("- 未识别到需要突出显示的关键负载或控制元件。");
            }
        }

        private static void AppendProblems(
            StringBuilder builder,
            CircuitStateResult result,
            IEnumerable<string> additionalErrors,
            IEnumerable<string> additionalWarnings)
        {
            var problems = new List<string>();
            AddRangeUnique(problems, additionalErrors);
            AddRangeUnique(problems, result.Errors);
            AddRangeUnique(problems, additionalWarnings);
            AddRangeUnique(problems, result.Warnings);

            if (HasThermalRelayOpen(result))
            {
                AddUnique(problems, "热继电器保护触点断开，控制回路被切断。");
            }

            if (result.HasContactorInterlockConflict || result.HasCompoundButtonInterlockConflict)
            {
                AddUnique(problems, "检测到正反转控制冲突或互锁状态异常。");
            }

            if (HasSelfHoldHistoryAmbiguity(result))
            {
                AddUnique(problems, "存在自锁历史状态边界；静态检查不能确认接触器此前是否已经吸合。");
            }

            if (problems.Count == 0)
            {
                builder.AppendLine("- 未发现需要优先处理的问题。");
                return;
            }

            for (var i = 0; i < problems.Count && i < 12; i++)
            {
                builder.AppendLine("- " + problems[i]);
            }
        }

        private static void AppendTeachingExplanation(StringBuilder builder, CircuitStateResult result)
        {
            var appended = false;
            if (HasTwoWaySwitch(result))
            {
                builder.AppendLine("- 双控开关不是简单 ON/OFF 开关，而是在 L-L1 和 L-L2 两个位置之间切换。");
                builder.AppendLine(HasRunningLoad(result)
                    ? "- 两个双控开关当前拨位形成完整火线路径，灯泡可以亮起。"
                    : "- 两个双控开关当前拨位未形成完整火线路径，灯泡不亮。请检查两个双控开关的 L-L1 / L-L2 拨位组合。");
                appended = true;
            }

            if (HasOpenHouseholdSingleSwitch(result))
            {
                var stoppedLoads = GetStoppedHouseholdLoadNames(result);
                if (!string.IsNullOrWhiteSpace(stoppedLoads))
                {
                    builder.AppendLine("- " + stoppedLoads + "当前未通电；对应控制支路中的单开单控开关处于 OFF，火线路径被切断。这是正常的分支控制结果，不表示存在旁路。");
                    appended = true;
                }
            }

            if (HasThermalRelayOpen(result))
            {
                builder.AppendLine("- 热继电器用于电机过载保护。当保护触点断开时，接触器线圈控制回路被切断，主触点断开，电机停止。");
                appended = true;
            }

            if (result.HasContactorInterlockConflict || result.HasCompoundButtonInterlockConflict)
            {
                builder.AppendLine("- 检测到正反转控制冲突或互锁状态异常。当前检查面板为静态拓扑分析，不读取画布历史 RUN。如果正转或反转接触器此前已经吸合，画布运行态可能继续保持。建议按停止按钮复位后再重新测试。");
                appended = true;
            }

            if (HasSelfHoldHistoryAmbiguity(result))
            {
                builder.AppendLine("- 检测到接触器 13/14 自锁结构。当前检查面板为静态拓扑分析，不读取画布历史 RUN。如果该接触器此前已经吸合，则画布运行态可能通过自锁继续保持。若要验证冷启动状态，请先按停止按钮或重置运行状态。");
                appended = true;
            }

            var starDeltaMotor = FindStarDeltaMotor(result);
            if (starDeltaMotor != null)
            {
                if (starDeltaMotor.State == "StarDeltaConflict")
                {
                    builder.AppendLine("- 星三角启动中，星形和三角形连接不能同时成立。请检查 KMY、KMD 控制回路以及电机端子跳线。");
                }
                else if (starDeltaMotor.State == "StarConnected")
                {
                    builder.AppendLine("- 当前星点已经形成，电机处于星形启动阶段。KT 延时到达后应先断开星形路径，再接通三角路径。");
                }
                else if (starDeltaMotor.State == "DeltaConnected")
                {
                    builder.AppendLine("- 当前标准三角连接已经形成，电机处于三角运行阶段。");
                }
                appended = true;
            }

            if (result.HasTimerRelays)
            {
                builder.AppendLine("- 通电延时时间继电器 KT 的实时状态以画布运行态为准：线圈得电后进入 Timing，达到 delaySeconds 后进入 Elapsed；Elapsed 时 15/18 导通、15/16 断开，失电后立即 Reset。");
                appended = true;
            }

            if (!appended)
            {
                builder.AppendLine("- 检查面板依据当前元件状态和接线拓扑解释电路，不读取画布的历史运行保持状态。");
            }
        }

        private static bool HasStarDeltaConflict(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (result.Components[i].IsStarDeltaMotor && result.Components[i].State == "StarDeltaConflict")
                {
                    return true;
                }
            }

            return false;
        }

        private static ComponentStateInfo FindStarDeltaMotor(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (result.Components[i].IsStarDeltaMotor)
                {
                    return result.Components[i];
                }
            }

            return null;
        }

        private static bool HasRunningLoad(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.SummaryGroup == ComponentStateInfo.GroupLoad && IsRunningState(component.State))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSelfHoldHistoryAmbiguity(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (result.Components[i].HasSelfHoldHistoryAmbiguity)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasThermalRelayOpen(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (IsThermalRelayOpen(result.Components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTwoWaySwitch(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                if (result.Components[i].IsTwoWaySwitch)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOpenHouseholdSingleSwitch(CircuitStateResult result)
        {
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.IsHouseholdSwitch && !component.IsTwoWaySwitch && component.State == "Open")
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetStoppedHouseholdLoadNames(CircuitStateResult result)
        {
            var names = new List<string>();
            for (var i = 0; i < result.Components.Count; i++)
            {
                var component = result.Components[i];
                if (component.SummaryGroup == ComponentStateInfo.GroupLoad &&
                    !component.IsThreePhaseMotor &&
                    !IsRunningState(component.State))
                {
                    AddUnique(names, component.DisplayName);
                }
            }

            return string.Join("、", names.ToArray());
        }

        private static bool IsThermalRelayOpen(ComponentStateInfo component)
        {
            return IsThermalRelay(component) &&
                !string.IsNullOrWhiteSpace(component.Judgement) &&
                component.Judgement.Contains("断开");
        }

        private static bool IsThermalRelay(ComponentStateInfo component)
        {
            return Contains(component.DefinitionName, "ThermalRelay") ||
                Contains(component.DisplayName, "热继");
        }

        private static bool IsIndicator(ComponentStateInfo component)
        {
            return Contains(component.DefinitionName, "Indicator") ||
                Contains(component.DisplayName, "指示灯");
        }

        private static bool IsKnifeSwitch(ComponentStateInfo component)
        {
            return Contains(component.DefinitionName, "KnifeSwitch") ||
                Contains(component.DisplayName, "刀开关");
        }

        private static bool IsEmergencyStop(ComponentStateInfo component)
        {
            return Contains(component.DefinitionName, "EmergencyStop") ||
                Contains(component.DisplayName, "急停");
        }

        private static string IndicatorReadableState(string state)
        {
            return state == "On" || state == "Running" ? "点亮" : "熄灭";
        }

        private static bool IsRunningState(string state)
        {
            return state == "On" || state == "Running" || state == "Forward" || state == "Reverse" ||
                state == "StarConnected" || state == "DeltaConnected";
        }

        private static string ReadableState(string state)
        {
            switch (state)
            {
                case "On":
                case "Running":
                    return "运行";
                case "Forward":
                    return "正转运行";
                case "Reverse":
                    return "反转运行";
                case "StarConnected":
                    return "星形连接，具备启动条件";
                case "DeltaConnected":
                    return "三角形连接，具备运行条件";
                case "StarDeltaConflict":
                    return "星三角冲突，危险";
                case "Fault":
                    return "故障或异常";
                default:
                    return "停止";
            }
        }

        private static string SanitizeTwoWaySwitchLanguage(string text, CircuitStateResult result)
        {
            var safeText = text ?? string.Empty;
            if (!HasTwoWaySwitch(result))
            {
                return safeText;
            }

            return safeText
                .Replace("ON/OFF", "L-L1 / L-L2 拨位")
                .Replace("开关 OFF", "双控开关当前拨位")
                .Replace("开关 ON", "双控开关当前拨位")
                .Replace("开关 off", "双控开关当前拨位")
                .Replace("开关 on", "双控开关当前拨位");
        }

        private static bool HasAny(IEnumerable<string> items)
        {
            if (items == null)
            {
                return false;
            }

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddRangeUnique(List<string> target, IEnumerable<string> source)
        {
            if (source == null)
            {
                return;
            }

            foreach (var item in source)
            {
                AddUnique(target, item);
            }
        }

        private static void AddUnique(List<string> target, string item)
        {
            if (target == null || string.IsNullOrWhiteSpace(item) || target.Contains(item))
            {
                return;
            }

            target.Add(item.Trim());
        }

        private static bool Contains(string text, string value)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TimerDelayStatusForTeaching(string status)
        {
            switch (status)
            {
                case "Reset":
                    return "Reset / 复位";
                case "Timing":
                case "Waiting":
                    return "Timing / 计时中";
                case "Elapsed":
                    return "Elapsed / 延时到达";
                default:
                    return SafeText(status, "延时状态未知");
            }
        }

        private static string SafeText(string text, string fallback)
        {
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
    }
}
