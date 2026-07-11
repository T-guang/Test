#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.AI;
using ElectricalSim.Core.Validation;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools
{
    public static class InspectionReportComposerTests
    {
        [MenuItem("Tools/Tests/运行 Inspector 报告模型测试")]
        public static void RunTests()
        {
            var failures = new List<string>();
            Run("无 Validation Issue 为 Success", TestNoValidationIssues, failures);
            Run("Warning Issue 使用 Warning", TestWarningValidationIssue, failures);
            Run("Error 优先且 RuleId 稳定排序", TestMixedValidationIssues, failures);
            Run("否定句不误判为 Error", TestNegativeSentence, failures);
            Run("运行态与 Summary 元数据", TestRuntimeKindAndSummarySeverity, failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Inspector 报告模型测试失败：\n" + string.Join("\n", failures.ToArray()));
            }

            Debug.Log("Inspector 报告模型测试通过：5/5 成功。");
        }

        private static void Run(string name, Action test, ICollection<string> failures)
        {
            try
            {
                test();
                Debug.Log("[PASS] " + name);
            }
            catch (Exception exception)
            {
                failures.Add(name + "：" + exception.Message);
                Debug.LogError("[FAIL] " + name + "：" + exception.Message);
            }
        }

        private static void TestNoValidationIssues()
        {
            var block = FirstValidationBlock(new List<CircuitValidationIssue>());
            AssertEqual(InspectionReportBlockKind.Validation, block.Kind, "Kind");
            AssertEqual(InspectionReportSeverity.Success, block.Severity, "Severity");
            AssertSequence(Array.Empty<string>(), block.RuleIds, "RuleIds");
        }

        private static void TestWarningValidationIssue()
        {
            var block = FirstValidationBlock(new List<CircuitValidationIssue>
            {
                Issue("RULE_WARNING", CircuitValidationSeverity.Warning)
            });
            AssertEqual(InspectionReportSeverity.Warning, block.Severity, "Severity");
            AssertSequence(new[] { "RULE_WARNING" }, block.RuleIds, "RuleIds");
        }

        private static void TestMixedValidationIssues()
        {
            var block = FirstValidationBlock(new List<CircuitValidationIssue>
            {
                Issue("Z_RULE_ERROR", CircuitValidationSeverity.Error),
                Issue("A_RULE_WARNING", CircuitValidationSeverity.Warning),
                Issue("Z_RULE_ERROR", CircuitValidationSeverity.Error)
            });
            AssertEqual(InspectionReportSeverity.Error, block.Severity, "Severity");
            AssertSequence(new[] { "A_RULE_WARNING", "Z_RULE_ERROR" }, block.RuleIds, "RuleIds");
        }

        private static void TestNegativeSentence()
        {
            var block = FirstValidationBlock(new List<CircuitValidationIssue>());
            if (block.Severity == InspectionReportSeverity.Error)
            {
                throw new InvalidOperationException("无 Issue 的否定句被错误标记为 Error。");
            }
            AssertEqual(InspectionReportSeverity.Success, block.Severity, "Severity");
        }

        private static void TestRuntimeKindAndSummarySeverity()
        {
            var runtime = InspectionReportComposer.ParseLegacyText("【当前画布运行态】\n当前停止。");
            AssertEqual(InspectionReportBlockKind.Runtime, runtime.Blocks[0].Kind, "Runtime Kind");

            AssertEqual(InspectionReportSeverity.Error, SummarySeverity("错误"), "Error summary");
            AssertEqual(InspectionReportSeverity.Warning, SummarySeverity("提醒"), "Warning summary");
            AssertEqual(InspectionReportSeverity.Success, SummarySeverity("正常"), "Success summary");
        }

        private static InspectionReportBlock FirstValidationBlock(IReadOnlyList<CircuitValidationIssue> issues)
        {
            var report = InspectionReportComposer.ParseLegacyText(
                "【接线校验】\n当前未检测到已支持规则范围内的接线错误。",
                issues);
            if (report.Blocks.Count != 1)
            {
                throw new InvalidOperationException("Validation Block 数量异常：" + report.Blocks.Count);
            }
            return report.Blocks[0];
        }

        private static CircuitValidationIssue Issue(string ruleId, CircuitValidationSeverity severity)
        {
            return new CircuitValidationIssue { RuleId = ruleId, Severity = severity };
        }

        private static InspectionReportSeverity SummarySeverity(string riskLevel)
        {
            var report = InspectionReportComposer.CreateSummary(
                "最新检查报告",
                "接线检查",
                false,
                "测试电路",
                "测试结论",
                riskLevel);
            return report.Blocks[0].Severity;
        }

        private static void AssertEqual<T>(T expected, T actual, string label)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(label + " expected=" + expected + ", actual=" + actual);
            }
        }

        private static void AssertSequence(IEnumerable<string> expected, IEnumerable<string> actual, string label)
        {
            var left = expected == null ? string.Empty : string.Join("|", expected.ToArray());
            var right = actual == null ? string.Empty : string.Join("|", actual.ToArray());
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(label + " expected=[" + left + "], actual=[" + right + "]");
            }
        }
    }
}
#endif
