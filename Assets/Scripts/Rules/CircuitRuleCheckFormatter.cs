using System.Text;

namespace ElectricalSim.Rules
{
    /// <summary>
    /// 将 CircuitRuleChecker 的 CircuitCheckResult 格式化为基础规则检查文本。
    /// 不执行规则、不改变问题等级；与 CircuitRuleCheckTeacherFormatter 的教学分组和 TeachingCheckReportFormatter 的最终报告组装职责不同。
    /// 标题、分组顺序和文本会进入检查报告链，修改后需运行 Inspector 模型测试与报告基线。
    /// </summary>
    public static class CircuitRuleCheckFormatter
    {
        public static string Format(CircuitCheckResult result)
        {
            if (result == null)
            {
                return "电路检查结果\n\n检查失败：未能获取检查结果。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("电路检查结果");
            builder.AppendLine();
            builder.AppendLine(result.Summary);

            if (result.issues.Count == 0)
            {
                builder.AppendLine();
                builder.AppendLine("当前电路的电源、控制元件和负载连接结构基本完整。");
                builder.AppendLine("提示：本检查为基础教学规则检查，不替代真实电工规范验收。");
                return builder.ToString().TrimEnd();
            }

            AppendGroup(builder, result, CircuitIssueSeverity.Error, "严重问题");
            AppendGroup(builder, result, CircuitIssueSeverity.Warning, "提醒");
            AppendGroup(builder, result, CircuitIssueSeverity.Info, "信息");
            builder.AppendLine();
            builder.AppendLine("提示：本检查只读取当前画布，不会修改元件、导线或仿真结果。");
            return builder.ToString().TrimEnd();
        }

        private static void AppendGroup(StringBuilder builder, CircuitCheckResult result, CircuitIssueSeverity severity, string title)
        {
            var index = 1;
            foreach (var issue in result.issues)
            {
                if (issue == null || issue.severity != severity)
                {
                    continue;
                }

                if (index == 1)
                {
                    builder.AppendLine();
                    builder.AppendLine(title + "：");
                    builder.AppendLine();
                }

                builder.Append(index++).Append(". ").AppendLine(issue.title);
                if (!string.IsNullOrWhiteSpace(issue.message))
                {
                    builder.AppendLine("   " + issue.message);
                }

                if (!string.IsNullOrWhiteSpace(issue.suggestion))
                {
                    builder.AppendLine("   建议：" + issue.suggestion);
                }

                builder.AppendLine();
            }
        }
    }
}
