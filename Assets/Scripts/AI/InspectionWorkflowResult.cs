namespace ElectricalSim.AI
{
    /// <summary>
    /// InspectionWorkflowService 向 UI 返回的单次工作流结果，组合结构化报告、状态消息、用户消息与成功标记。
    /// Success/Failure 工厂当前负责填充空值约定，LocalInspectorPanel 等调用方消费结果；该类型只在内存中传递，
    /// 不参与模板、保存图纸、JSON 或 Unity 资产序列化，也不重新执行分析、Validation 或报告组装。
    /// 修改字段、工厂的空值约定或 Succeeded 语义前需同步复核工作流和 UI 读取端。
    /// </summary>
    public sealed class InspectionWorkflowResult
    {
        public InspectionReportData Report { get; }
        public string StatusMessage { get; }
        public string UserMessage { get; }
        public bool Succeeded { get; }

        private InspectionWorkflowResult(
            InspectionReportData report,
            string statusMessage,
            string userMessage,
            bool succeeded)
        {
            Report = report;
            StatusMessage = statusMessage ?? string.Empty;
            UserMessage = userMessage ?? string.Empty;
            Succeeded = succeeded;
        }

        public static InspectionWorkflowResult Success(InspectionReportData report, string statusMessage = null)
        {
            return new InspectionWorkflowResult(report ?? new InspectionReportData(), statusMessage, string.Empty, true);
        }

        public static InspectionWorkflowResult Failure(string userMessage)
        {
            return new InspectionWorkflowResult(null, string.Empty, userMessage, false);
        }
    }
}
