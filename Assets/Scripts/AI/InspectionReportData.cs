using System;
using System.Collections.Generic;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 结构化检查报告的 Block 分类。当前由 InspectionReportComposer 和检查流程填充，LocalInspectorPanel 依此展示；
    /// 枚举本身不执行规则判断。成员调整前需复核报告快照、Composer、UI 渲染与测试模型契约。
    /// </summary>
    public enum InspectionReportBlockKind
    {
        General,
        Summary,
        Runtime,
        Validation,
        Parameter,
        Teaching
    }

    /// <summary>
    /// 报告 Block 的展示严重等级，不等同于 CircuitValidationSeverity 的规则定义；映射由报告组装流程决定。
    /// 成员变更前需复核 Inspector 报告模型快照和渲染颜色/排序消费端。
    /// </summary>
    public enum InspectionReportSeverity
    {
        Information,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// 单个结构化报告区块，保存标题、正文、分类、展示严重等级和关联 RuleId 列表。
    /// InspectionReportComposer 创建该模型，LocalInspectorPanel 展示它；本类型不重新分析电路、不升级 Severity，也不定义 RuleId。
    /// 构造函数将 null 文本规范为空字符串，并复制 RuleIds 为只读列表；Section/Block 顺序由容器加入顺序保持。
    /// </summary>
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
            RuleIds = ruleIds == null
                ? Array.Empty<string>()
                : new List<string>(ruleIds).AsReadOnly();
        }

        public string ToLegacyText()
        {
            // 仅为兼容旧文本展示生成字符串，不反向解析或修改 Block 结构。
            if (string.IsNullOrWhiteSpace(SectionTitle))
            {
                return Body;
            }

            return string.IsNullOrWhiteSpace(Body)
                ? "【" + SectionTitle + "】"
                : "【" + SectionTitle + "】\n" + Body;
        }
    }

    /// <summary>
    /// 当前一次检查/解释流程的内存结构化报告容器。Composer 与工作流负责组装，LocalInspectorPanel 负责展示；
    /// 它不是 JSON/Unity 资产契约，也不保存电路状态或执行 Validation。Blocks 的加入顺序是当前报告展示与快照比对的重要输入。
    /// AddRange 仅追加既有 Block 引用，FromLegacyText 委托 Composer 的兼容解析；修改这些行为前需同步复核报告模型测试和快照。
    /// </summary>
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
