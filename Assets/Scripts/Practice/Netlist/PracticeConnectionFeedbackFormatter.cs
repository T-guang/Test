using System.Text;
using ElectricalSim.Templates;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 将 Netlist 层的结构化连接 Issue 按既定分类和顺序格式化为练习反馈文本。
    /// 它不同于外层 PracticeFeedbackFormatter 的兼容入口：本类不建立元件映射、不判断端子连通，也不计算分数或 Passed；
    /// 分类和顺序完全读取网表检查结果。修改文本结构后，必须回归缺失、多余、接错和无法映射反馈。
    /// </summary>
    public static class PracticeConnectionFeedbackFormatter
    {
        public static string Format(CircuitTemplateCatalogItemDto templateItem, PracticeConnectionCheckResult result)
        {
            var sb = new StringBuilder();
            var title = templateItem != null && !string.IsNullOrWhiteSpace(templateItem.templateName)
                ? templateItem.templateName
                : "\u5f53\u524d\u7ec3\u4e60";

            sb.AppendLine("## \u7ec3\u4e60\u63a5\u7ebf\u68c0\u6d4b\uff1a" + title);
            sb.AppendLine(result != null && result.Passed ? "\u68c0\u6d4b\u7ed3\u8bba\uff1a\u901a\u8fc7" : "\u68c0\u6d4b\u7ed3\u8bba\uff1a\u9700\u8981\u4fee\u6539");
            sb.AppendLine();

            if (result == null)
            {
                sb.AppendLine("\u672a\u83b7\u5f97\u63a5\u7ebf\u68c0\u6d4b\u7ed3\u679c\u3002");
                return sb.ToString();
            }

            if (result.AmbiguousMapping)
            {
                sb.AppendLine("### \u6ce8\u610f");
                sb.AppendLine("- \u5f53\u524d\u65e0\u6cd5\u552f\u4e00\u786e\u5b9a\u540c\u7c7b\u578b\u5143\u4ef6\u7684\u5bf9\u5e94\u5173\u7cfb\uff0c\u8bf7\u68c0\u67e5\u662f\u5426\u7f3a\u5c11\u5143\u4ef6\u6216\u5b58\u5728\u591a\u4f59\u5143\u4ef6\u3002");
                sb.AppendLine();
            }

            AppendSection(sb, "\u7f3a\u5c11\u5143\u4ef6", result.MissingComponents);
            AppendSection(sb, "\u591a\u4f59\u5143\u4ef6", result.ExtraComponents);
            AppendSection(sb, "\u7f3a\u5c11\u8fde\u63a5", result.MissingConnections);
            AppendSection(sb, "\u7aef\u5b50\u63a5\u9519", result.WrongConnections);
            AppendSection(sb, "\u591a\u4f59\u8fde\u63a5", result.ExtraConnections);

            if (result.Passed)
            {
                sb.AppendLine("### \u63a5\u7ebf\u7ed3\u679c");
                sb.AppendLine("- \u5f53\u524d\u7aef\u5b50\u8fde\u901a\u5173\u7cfb\u4e0e\u6807\u51c6\u7b54\u6848\u4e00\u81f4\u3002");
                sb.AppendLine();
            }

            AppendSection(sb, "\u6b63\u786e\u8fde\u63a5\u5efa\u8bae", result.CorrectConnectionSuggestions);
            sb.AppendLine("\u8bf4\u660e\uff1a\u672c\u9636\u6bb5\u53ea\u6bd4\u8f83\u7aef\u5b50\u8fde\u901a\u5173\u7cfb\uff0c\u4e0d\u6bd4\u8f83\u5bfc\u7ebf\u5750\u6807\u3001\u5bfc\u7ebf\u8def\u5f84\u6216\u5143\u4ef6\u4f4d\u7f6e\u3002\u53cc\u63a7\u7167\u660e\u7b2c\u4e00\u7248\u6682\u4e0d\u5904\u7406 L1/L2 \u5bf9\u8c03\u7b49\u4ef7\u7aef\u5b50\u89c4\u5219\u3002");
            return sb.ToString();
        }

        private static void AppendSection(StringBuilder sb, string title, System.Collections.Generic.IReadOnlyList<PracticeConnectionIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return;
            }

            sb.AppendLine("### " + title);
            for (var i = 0; i < issues.Count; i++)
            {
                sb.AppendLine((i + 1) + ". " + issues[i].Message);
            }

            sb.AppendLine();
        }
    }
}

