using System.Text;
using ElectricalSim.Core;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 将当前已支持工业教学电路的事实结果组织为教学解释文本。
    /// 依赖 IndustrialCircuitRuleAnalyzer 提供的事实，不重新判断电路是否合法、不替代工业规则分析器，也不通过中文关键词反向决定状态或 Severity。
    /// 支持范围限于现有工业教学模板；修改后需回归工业模板的解释正文与报告基线。
    /// </summary>
    public static class IndustrialCircuitExplainer
    {
        public static bool TryExplain(WorkspaceController workspace, out string explanation)
        {
            var facts = IndustrialCircuitRuleAnalyzer.BuildFacts(workspace);
            if (!facts.IsIndustrial)
            {
                explanation = string.Empty;
                return false;
            }

            var builder = new StringBuilder();
            builder.AppendLine("检查助手：");
            builder.AppendLine("当前电路解释");
            builder.AppendLine();
            builder.AppendLine("电路类型：" + facts.CircuitType);
            builder.AppendLine();
            builder.AppendLine("主回路：");
            builder.AppendLine(BuildMainCircuitExplanation(facts));
            builder.AppendLine();
            builder.AppendLine("控制回路：");
            builder.AppendLine(BuildControlCircuitExplanation(facts));
            builder.AppendLine();
            builder.AppendLine("当前状态：");
            AppendRuntimeState(builder, facts);
            builder.AppendLine();
            builder.AppendLine("说明：本解释根据当前画布元件和导线结构生成，用于帮助理解电路工作过程，不会修改电路。 ");

            explanation = builder.ToString().TrimEnd();
            return true;
        }

        private static string BuildMainCircuitExplanation(IndustrialCircuitFacts facts)
        {
            if (facts.Contactors.Count >= 2 && facts.Motors.Count > 0)
            {
                return "三相电源通常经过 3P 空开、3P 熔断器后送入两个接触器。KM1 按正常相序接入电机 U/V/W，KM2 通过交换任意两相实现反转。";
            }

            if (facts.ThermalRelays.Count > 0 && facts.Motors.Count > 0)
            {
                return "三相电源经过空开、熔断器、接触器主触点和热继电器主触点后进入三相异步电动机。热继电器用于检测电机过载。";
            }

            if (facts.Contactors.Count > 0 && facts.Motors.Count > 0)
            {
                return "三相电源经过空开、熔断器和接触器主触点后进入三相异步电动机。接触器吸合时主触点闭合，电机获得三相电源。";
            }

            return "当前画布包含工业元件。请确认三相电源、保护元件、接触器和负载之间的主回路是否完整。";
        }

        private static string BuildControlCircuitExplanation(IndustrialCircuitFacts facts)
        {
            if (facts.CircuitType == "电气互锁正反转控制电路")
            {
                return "停止按钮位于控制回路前级。正转支路通过 KM2 的 21/22 常闭触点进入 KM1 线圈，反转支路通过 KM1 的 21/22 常闭触点进入 KM2 线圈。某一方向接触器吸合后，会切断另一方向线圈回路，防止同时吸合。";
            }

            if (facts.CircuitType == "电动机正反转控制电路")
            {
                return "两个方向启动按钮分别控制 KM1 和 KM2 线圈。KM1 控制正转，KM2 控制反转；停止按钮用于切断控制回路，使当前运行方向停止。";
            }

            if (facts.CircuitType == "点动与连续运行混合控制电路")
            {
                return "复合按钮的常开触点 23/24 用于点动运行，按下时接触器线圈得电；复合按钮的常闭触点 11/12 串在连续运行和自锁支路前级，按下点动按钮时会切断自锁路径，防止点动被 13/14 保持。普通启动按钮用于连续运行，13/14 闭合后形成自锁。";
            }

            if (facts.CircuitType == "热继电器保护电动机控制电路")
            {
                return "启动按钮闭合后接触器线圈得电，13/14 可形成自锁。热继电器 95/96 常闭触点应串入线圈回路；热继跳闸时 95/96 断开，接触器释放，电机停止。";
            }

            if (facts.CircuitType == "电动机连续运行控制电路")
            {
                return "启动按钮闭合后接触器线圈得电，主触点吸合，电机运行；接触器辅助常开触点 13/14 同时闭合，形成自锁。松开启动按钮后，电机仍可保持运行；按下停止按钮后控制回路断开，电机停止。";
            }

            if (facts.CircuitType == "电动机点动控制电路")
            {
                return "启动按钮闭合时接触器线圈得电，主触点吸合，电机运行；启动按钮断开后线圈失电，接触器释放，电机停止。";
            }

            return "控制回路通常由停止按钮、启动按钮、接触器线圈和辅助触点组成。请结合当前接线检查 A1/A2、13/14、21/22 等端子。";
        }

        private static void AppendRuntimeState(StringBuilder builder, IndustrialCircuitFacts facts)
        {
            if (facts.Contactors.Count == 0)
            {
                builder.AppendLine("接触器：未检测到。 ");
            }
            else
            {
                for (var i = 0; i < facts.Contactors.Count; i++)
                {
                    var label = facts.Contactors.Count >= 2 ? "KM" + (i + 1) : IndustrialCircuitRuleAnalyzer.DisplayName(facts.Contactors[i]);
                    builder.AppendLine(label + "：" + (facts.Contactors[i].IsEnergized ? "RUN" : "STOP"));
                }
            }

            if (facts.Motors.Count == 0)
            {
                builder.AppendLine("电机：未检测到。 ");
            }
            else
            {
                for (var i = 0; i < facts.Motors.Count; i++)
                {
                    var prefix = facts.Motors.Count > 1 ? "电机" + (i + 1) : "电机";
                    builder.AppendLine(prefix + "：" + IndustrialCircuitRuleAnalyzer.MotorStateText(facts.Motors[i]));
                }
            }

            foreach (var relay in facts.ThermalRelays)
            {
                var tripped = IndustrialCircuitRuleAnalyzer.TryGetParameterValue(relay, "tripState", out var tripState) && tripState >= 0.5f;
                builder.AppendLine(IndustrialCircuitRuleAnalyzer.DisplayName(relay) + "：" + (tripped ? "已跳闸" : "未跳闸"));
            }
        }
    }
}
