using ElectricalSim.Rules;
using ElectricalSim.Templates;
using ElectricalSim.Practice.Netlist;
using System.Text;

namespace ElectricalSim.Practice
{
    /// <summary>
    /// 将练习检查结果组织为检查助手可显示的文本；当前结构化网表结果直接委托给 Netlist 层 Formatter，
    /// 另一个重载保留旧评分结果模型的兼容文本。
    /// 本类不执行连接检查、不修改分数或 Passed，标题和段落顺序属于用户体验与报告回归契约；修改后需回归正确和错误提交报告。
    /// </summary>
    public static class PracticeFeedbackFormatter
    {
        public static string Format(CircuitTemplateCatalogItemDto templateItem, PracticeConnectionCheckResult connectionResult)
        {
            return PracticeConnectionFeedbackFormatter.Format(templateItem, connectionResult);
        }

        public static string Format(CircuitTemplateCatalogItemDto templateItem, PracticeScore score, CircuitCheckResult ruleResult, ConnectionCheckResult connectionResult)
        {
            var sb = new StringBuilder();
            string title = templateItem != null && !string.IsNullOrWhiteSpace(templateItem.templateName) ? templateItem.templateName : "\u5f53\u524d\u7ec3\u4e60";

            sb.AppendLine("## \u7ec3\u4e60\u7ed3\u679c\uff1a" + title);
            sb.AppendLine("\u5f53\u524d\u4fdd\u7559\u65e7\u8bc4\u5206\u63a5\u53e3\uff0c\u4ec5\u7528\u4e8e\u517c\u5bb9\u65e7\u4ee3\u7801\u3002\u65b0\u7ec3\u4e60\u4e3b\u7ebf\u8bf7\u4f7f\u7528\u901a\u7528\u7f51\u8868\u5224\u5b9a\u7ed3\u679c\u3002");
            sb.AppendLine();

            if (connectionResult != null)
            {
                if (connectionResult.MissingComponents.Count > 0)
                {
                    sb.AppendLine("### \u7f3a\u5c11\u5143\u4ef6");
                    foreach (var item in connectionResult.MissingComponents)
                    {
                        sb.AppendLine("- " + item);
                    }
                }

                if (connectionResult.ExtraComponents.Count > 0)
                {
                    sb.AppendLine("### \u591a\u4f59\u5143\u4ef6");
                    foreach (var item in connectionResult.ExtraComponents)
                    {
                        sb.AppendLine("- " + item);
                    }
                }

                if (connectionResult.MissingConnections.Count > 0)
                {
                    sb.AppendLine("### \u7f3a\u5c11\u8fde\u63a5");
                    foreach (var item in connectionResult.MissingConnections)
                    {
                        sb.AppendLine("- " + item);
                    }
                }

                if (connectionResult.WrongConnections.Count > 0)
                {
                    sb.AppendLine("### \u9519\u8bef\u8fde\u63a5");
                    foreach (var item in connectionResult.WrongConnections)
                    {
                        sb.AppendLine("- " + item);
                    }
                }
            }

            return sb.ToString();
        }
    }
}
