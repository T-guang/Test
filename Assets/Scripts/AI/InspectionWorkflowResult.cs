namespace ElectricalSim.AI
{
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
