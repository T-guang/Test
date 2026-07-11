using System;
using System.Collections.Generic;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.AI
{
    public enum InspectionReportBlockKind
    {
        General,
        Summary,
        Runtime,
        Validation,
        Parameter,
        Teaching
    }

    public enum InspectionReportSeverity
    {
        Information,
        Success,
        Warning,
        Error
    }

    public sealed class InspectionReportBlock
    {
        public string SectionTitle { get; }
        public string Body { get; }
        public InspectionReportBlockKind Kind { get; }
        public InspectionReportSeverity Severity { get; }
        public IReadOnlyList<string> RuleIds { get; }

        public InspectionReportBlock(
            string sectionTitle,
            string body,
            InspectionReportBlockKind kind,
            InspectionReportSeverity severity,
            IReadOnlyList<string> ruleIds = null)
        {
            SectionTitle = sectionTitle ?? string.Empty;
            Body = body ?? string.Empty;
            Kind = kind;
            Severity = severity;
            RuleIds = ruleIds ?? new List<string>();
        }

        public string ToLegacyText()
        {
            if (string.IsNullOrWhiteSpace(SectionTitle))
            {
                return Body;
            }

            return string.IsNullOrWhiteSpace(Body)
                ? "【" + SectionTitle + "】"
                : "【" + SectionTitle + "】\n" + Body;
        }
    }

    public sealed class InspectionReportData
    {
        private readonly List<InspectionReportBlock> blocks = new List<InspectionReportBlock>();

        public IReadOnlyList<InspectionReportBlock> Blocks => blocks;

        public void Add(InspectionReportBlock block)
        {
            if (block != null)
            {
                blocks.Add(block);
            }
        }

        public void AddRange(InspectionReportData source)
        {
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Blocks.Count; i++)
            {
                Add(source.Blocks[i]);
            }
        }

        public static InspectionReportData FromLegacyText(
            string text,
            IReadOnlyList<CircuitValidationIssue> validationIssues = null)
        {
            return InspectionReportComposer.ParseLegacyText(text, validationIssues);
        }
    }
}
