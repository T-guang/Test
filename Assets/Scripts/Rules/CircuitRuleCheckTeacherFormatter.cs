using System.Text;
using System.Collections.Generic;

namespace ElectricalSim.Rules
{
    /// <summary>
    /// 将 <see cref="CircuitRuleChecker"/> 的结构化教学检查结果转换为面向学生的解释文本。
    /// 本类只读取既有的 CircuitIssue、严重级别和问题代码，不重新判断电路，也不通过中文关键词反向修改 Severity。
    /// 输出会继续进入检查报告组装链；分组顺序、标题和教学文本受 Inspector 报告快照保护，改动后必须运行模型测试与 18 张模板基线。
    /// </summary>
    public static class CircuitRuleCheckTeacherFormatter
    {
        public static string FormatForTeaching(CircuitCheckResult result)
        {
            if (result == null)
            {
                return "电路检查失败：未能获取检查结果。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("电路检查结果");
            builder.AppendLine();

            if (result.issues.Count == 0)
            {
                builder.AppendLine("未发现明显接线错误。");
                builder.AppendLine();
                builder.AppendLine("当前电路包含电源、保护元件、控制元件和负载，火线与零线回路基本完整。");
                builder.AppendLine("你可以点击“开始仿真”，观察灯泡 RUN 状态和万用表读数。");
                builder.AppendLine();
                builder.AppendLine("提示：");
                builder.AppendLine("本检查为基础教学规则检查，主要用于帮助发现常见接线问题，不替代真实电工规范验收。");
                return builder.ToString().TrimEnd();
            }

            builder.AppendLine("本次检查发现：");
            int errorCount = result.ErrorCount;
            int warningCount = result.WarningCount;
            int infoCount = result.InfoCount;

            if (errorCount > 0) builder.AppendLine($"严重问题：{errorCount} 个");
            if (warningCount > 0) builder.AppendLine($"提醒：{warningCount} 个");
            if (infoCount > 0) builder.AppendLine($"信息：{infoCount} 条");
            builder.AppendLine();

            // 先错误、再提醒、最后说明的顺序是当前检查报告的稳定展示契约，不要仅为文案整理而调整。
            AppendGroup(builder, result, CircuitIssueSeverity.Error, "一、需要优先处理的问题");
            AppendGroup(builder, result, CircuitIssueSeverity.Warning, errorCount > 0 ? "二、提醒" : "一、提醒");
            AppendGroup(builder, result, CircuitIssueSeverity.Info, errorCount > 0 && warningCount > 0 ? "三、说明" : (errorCount > 0 || warningCount > 0 ? "二、说明" : "一、说明"));

            builder.AppendLine();
            builder.AppendLine("本检查只读取当前画布，不会修改元件、导线或仿真结果。");
            builder.AppendLine("本功能用于教学辅助，不替代真实电工规范验收。");

            return builder.ToString().TrimEnd();
        }

        private static void AppendGroup(StringBuilder builder, CircuitCheckResult result, CircuitIssueSeverity severity, string title)
        {
            // 这里仅按既有严重级别取组并补充教学解释，不承担规则复算或问题级别判定。
            var issues = new List<CircuitIssue>();
            foreach (var issue in result.issues)
            {
                if (issue != null && issue.severity == severity)
                {
                    issues.Add(issue);
                }
            }

            if (issues.Count == 0)
            {
                return;
            }

            builder.AppendLine(title);
            builder.AppendLine();

            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                builder.AppendLine($"{i + 1}. {issue.title}");
                
                var teacherExplanation = GetTeacherExplanation(issue);
                
                builder.AppendLine($"   原因：{teacherExplanation.Reason}");
                builder.AppendLine($"   建议：{teacherExplanation.Suggestion}");
                builder.AppendLine();
            }
        }

        private class TeacherExplanation
        {
            public string Reason { get; set; }
            public string Suggestion { get; set; }
        }

        private static TeacherExplanation GetTeacherExplanation(CircuitIssue issue)
        {
            var explanation = new TeacherExplanation();

            switch (issue.code)
            {
                case "NO_POWER":
                    explanation.Reason = "电源是电路能量来源。没有电源时，即使有灯泡、开关和导线，电路也无法工作。";
                    explanation.Suggestion = "请从左侧元件池拖入 220V 电源，再连接火线 L 和零线 N。";
                    break;
                case "NO_LOAD":
                    explanation.Reason = "负载是消耗电能并产生效果的元件，例如灯泡、电风扇或三相电动机。没有负载时，电路没有实际工作对象。";
                    explanation.Suggestion = "请添加电灯泡、电风扇或三相电动机等负载元件。";
                    break;
                case "MOTOR_PHASE_MISSING":
                    explanation.Reason = "三相电机不能只接一根线或两根线，U/V/W 三个相线端子都需要接入主回路。";
                    explanation.Suggestion = "请检查电机 U/V/W 是否分别接到接触器、熔断器、空开后的三相输出端。";
                    break;
                case "MOTOR_PHASE_PATH_MISSING":
                    explanation.Reason = "当前电机端子虽然可能接了导线，但该相没有通过主回路连回三相电源。";
                    explanation.Suggestion = "请沿该相导线从三相电源开始，依次检查空开、熔断器、接触器和电机端子是否连通。";
                    break;
                case "NO_WIRE":
                    explanation.Reason = "元件必须通过导线连接成回路，电流才能流动。";
                    explanation.Suggestion = "请使用红色导线连接火线 L 支路，使用蓝色导线连接零线 N 支路。";
                    break;
                case "LOAD_L_MISSING":
                    explanation.Reason = "L 端通常接火线，是负载获得电源的一侧。如果 L 端没有连接到电源火线支路，负载不会通电。";
                    explanation.Suggestion = "检查负载红色端子是否连接到开关或空开输出的火线支路。";
                    break;
                case "LOAD_N_MISSING":
                    explanation.Reason = "N 端需要回到电源零线，形成完整回路。如果 N 端断开，电流没有返回路径。";
                    explanation.Suggestion = "检查负载蓝色端子是否接回电源 N。";
                    break;
                case "PHASE_PATH_MISSING":
                    explanation.Reason = "火线 L 应从电源出发，经过保护元件和控制元件后到达负载 L 端。如果中间任意一段断开，负载就不能获得电源。";
                    explanation.Suggestion = "沿红色导线从电源 L 开始检查，依次查看空开、开关、负载 L 端是否连通。";
                    break;
                case "NEUTRAL_PATH_MISSING":
                    explanation.Reason = "零线 N 是电流返回电源的路径。负载 N 端没有回到电源 N 时，电路无法形成闭合回路。";
                    explanation.Suggestion = "沿蓝色导线从负载 N 端检查，确认是否最终回到电源 N。";
                    break;
                case "NO_SWITCH":
                case "SWITCH_NOT_ON_PHASE":
                case "SWITCH_ON_NEUTRAL":
                    explanation.Reason = "家庭照明电路中，开关通常应控制火线。如果开关没有串在火线路径上，可能导致灯具维护时仍带电，存在安全隐患。";
                    explanation.Suggestion = "请确认电源 L 经过开关后再进入灯泡 L 端。";
                    break;
                case "BREAKER_INCOMPLETE":
                    explanation.Reason = "空气开关用于保护后级电路。通常电源 L/N 先进入空开输入端，再由空开输出端连接到后续电路。如果进线或出线缺失，保护和供电都可能不正常。";
                    explanation.Suggestion = "检查电源 L/N 是否接到空开输入端，空开输出端是否接到后续开关、灯泡或风扇回路。";
                    break;
                case "METER_INCOMPLETE":
                    explanation.Reason = "单相电能表通常需要 L_IN、L_OUT、N_IN、N_OUT 形成完整进出线关系。进线或出线缺失会导致后级电路不能正确供电或计量。";
                    explanation.Suggestion = "请检查电源 L/N 是否进入电表输入端，电表输出端是否连接到后续电路。";
                    break;
                case "ISOLATED_COMPONENT":
                    explanation.Reason = "孤立元件没有任何导线连接，当前不会参与电路工作。";
                    explanation.Suggestion = "如果该元件是本次练习需要的，请将它接入电路；如果不是需要的元件，可以删除以保持画布清晰。";
                    break;
                case "SwitchBypassed":
                    explanation.Reason = "这通常说明灯泡火线没有真正经过该开关，或者存在绕过开关的旁路。开关虽然画在电路中，但没有实际控制灯泡。";
                    explanation.Suggestion = "请检查火线是否按照“电源 L → 空开 → 开关 L → 开关 L1 → 灯泡 L”的顺序连接。";
                    break;
                case "BreakerBypassed":
                    explanation.Reason = "这说明负载可能绕过了空气开关，直接从电源或其它路径获得火线，空开没有起到保护和断电作用。";
                    explanation.Suggestion = "请检查电源 L/N 是否先进入空气开关输入端，再由空气开关输出端连接到后级开关和负载。";
                    break;
                case "LoadLivePathWithoutSwitch":
                    explanation.Reason = "当前负载 L 端可以直接获得火线，但未检测到火线路径中存在单开单控开关。这样负载可能一直通电，无法通过开关控制。";
                    explanation.Suggestion = "请将开关串联到火线路径中，推荐接法为“电源 L → 空开 → 开关 L → 开关 L1 → 负载 L”。";
                    break;
                case "SwitchOnNeutralPath":
                    explanation.Reason = "开关如果切断的是零线，灯可能会熄灭，但灯具火线端仍可能带电，维护时存在安全隐患。";
                    explanation.Suggestion = "请将开关改接到火线路径中，让电源 L 经过开关后再进入灯泡 L 端。";
                    break;
                case "LoadConnectedDirectlyToPower":
                    explanation.Reason = "这样虽然可以形成回路，但缺少空气开关保护或开关控制，不符合常见家庭照明教学接线习惯。";
                    explanation.Suggestion = "建议加入空气开关和单控开关，使电路具备保护和控制功能。";
                    break;
                case "ParallelLoadBypassedControl":
                    explanation.Reason = "并联电路中每个负载都需要按设计接入对应控制支路。如果某个负载绕过开关，它可能无法被正确控制。";
                    explanation.Suggestion = "请检查该负载 L 端是否接在对应开关输出端，而不是直接接到电源或空开输出端。";
                    break;
                case "SingleSwitchTerminalMiswired":
                case "SingleSwitchTerminalIncomplete":
                    explanation.Reason = "单控开关不是只要接上线就可以起控制作用，它需要让火线从 L 端进入，再从 L1 端输出到灯泡。这样开关断开时，才能真正切断灯泡火线。";
                    explanation.Suggestion = "请检查接线顺序是否为“电源 L → 空开 → 开关 L → 开关 L1 → 灯泡 L”。如果灯泡火线接在开关 L 侧，或者 L1 没有接到灯泡，开关就可能无法正确控制灯泡。";
                    break;
                case "OPEN_DEVICE":
                    if (issue.title != null && issue.title.Contains("以下控制元件"))
                    {
                        explanation.Reason = "多个控制元件断开时，会切断负载的火线控制支路。接线结构可能正确，但当前状态下负载不会运行。";
                        explanation.Suggestion = "请依次确认这些控制元件是否需要切换为 ON，再观察负载状态。";
                    }
                    else if (issue.title != null && issue.title.Contains("空气开关"))
                    {
                        explanation.Reason = "空气开关处于 OFF 时，会切断后级电路。即使导线连接正确，负载也可能不会通电。";
                        explanation.Suggestion = "请确认是否需要将空气开关切换为 ON，再运行仿真观察结果。";
                    }
                    else if (issue.title != null && issue.title.Contains("单开单控开关"))
                    {
                        explanation.Reason = "单控开关处于 OFF 时，会切断灯泡或风扇的火线控制支路。接线结构可能正确，但当前状态下负载不会运行。";
                        explanation.Suggestion = "请将对应单控开关切换为 ON，再观察灯泡或风扇状态。";
                    }
                    else
                    {
                        explanation.Reason = "控制元件断开时，即使接线结构正确，负载也不会通电。";
                        explanation.Suggestion = "请检查控制元件的 ON/OFF 状态。";
                    }
                    break;
                default:
                    explanation.Reason = issue.message ?? "电路存在影响运行的结构或状态问题。";
                    explanation.Suggestion = issue.suggestion ?? "请检查相关元件和接线。";
                    break;
            }

            return explanation;
        }
    }
}


