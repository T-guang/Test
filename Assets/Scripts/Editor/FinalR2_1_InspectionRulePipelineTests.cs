using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using ElectricalSim.Rules;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// Final-R2.1: 工业检查助手规则管线正式回归测试。
    ///
    /// 通过正式 InspectionWorkflowService.CreateCheckReport() 验证 9 类工业场景，
    /// 守住最终 learner-facing 报告的产品调用路径：
    /// - S01-S05: 工业 Analyzer / B5 bypass 产生的 issue 必须在"问题与风险"段落中有具体说明
    /// - S06: 正常 34→A1 跳线不得产生旁路误报
    /// - S07: 正常工业自锁电路不得因 B5 合流新增误报
    /// - S08: 加入 L1→T1 主触点旁路后 Error 准确增加 1
    /// - S09: 加入 33→34 辅助触点旁路后 Warning 准确增加 1
    ///
    /// 本测试为永久正式回归，不删除。缺陷之所以长期没发现，就是之前只测了
    /// CircuitRuleChecker.Check() 而没有测试 InspectionWorkflowService.CreateCheckReport()
    /// 最终产品链。
    /// </summary>
    public static class FinalR2_1_InspectionRulePipelineTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string KmDef = "Contactor_KM_220V";
        private const string PowerDef = "AC_220V_Power";
        private const string MotorDef = "Motor_StarDelta_380V";
        private const string StartDef = "Button_Start_NO";
        private const string StopDef = "Button_Stop_NC";

        [MenuItem("Tools/Tests/Run Final-R2.1 Inspection Rule Pipeline Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            var evidence = new StringBuilder();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                if (workspace == null || saveLoad == null)
                    throw new InvalidOperationException("Missing WorkspaceController or SaveLoadService");

                var adapter = new AuditRuntimeAdapter(workspace);

                S01_NoThreePhasePower(failures, evidence, workspace, saveLoad, adapter);
                S02_ContactorA1A2Incomplete(failures, evidence, workspace, saveLoad, adapter);
                S03_MotorPhaseMissing(failures, evidence, workspace, saveLoad, adapter);
                S04_MainContactBypass_L1_T1(failures, evidence, workspace, saveLoad, adapter);
                S05_AuxContactBypass_33_34(failures, evidence, workspace, saveLoad, adapter);
                S06_NormalJumper_34_A1(failures, evidence, workspace, saveLoad, adapter);
                S07_NormalIndustrialSelfHold(failures, evidence, workspace, saveLoad, adapter);
                S08_MainBypassSeverityIncrement(failures, evidence, workspace, saveLoad, adapter);
                S09_AuxBypassSeverityIncrement(failures, evidence, workspace, saveLoad, adapter);
            }
            catch (Exception e)
            {
                failures.Add("测试执行出现未处理异常：" + e);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            Debug.Log("[FINAL_R2_1_EVIDENCE_START]\n" + evidence + "\n[FINAL_R2_1_EVIDENCE_END]");

            if (failures.Count > 0)
            {
                Debug.LogError("=== FinalR2_1 Pipeline Tests: " + failures.Count + " 项 FAIL ===");
                foreach (var f in failures) Debug.LogError("[FAIL] " + f);
                throw new InvalidOperationException(
                    "FinalR2_1 Pipeline Tests " + failures.Count + " 项 FAIL:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== FinalR2_1 Pipeline Tests 全部 GREEN (S01-S09) ===");
        }

        // =========================================================================
        // S01: 缺三相电源 → industrialResult.Errors 有"未检测到三相交流电源"
        // 修复后最终"问题与风险"必须包含该具体说明
        // =========================================================================
        private static void S01_NoThreePhasePower(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S01-NoThreePhasePower";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s01", "R2.1 S01");
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            AssertDetailContainsKeyword(failures, evidence, scenario, workspace, adapter,
                expectedDetailKeyword: "未检测到三相交流电源");
        }

        // =========================================================================
        // S02: KM A1/A2 不完整 → 最终报告必须明确出现"A1/A2"
        // =========================================================================
        private static void S02_ContactorA1A2Incomplete(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S02-ContactorA1A2Incomplete";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s02", "R2.1 S02");
            dto.components.Add(new TemplateComponentDto { instanceId = "power_1", definitionName = PowerDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 200, y = 0 });
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            AssertDetailContainsKeyword(failures, evidence, scenario, workspace, adapter,
                expectedDetailKeyword: "A1/A2");
        }

        // =========================================================================
        // S03: 电机缺相 → 最终报告必须包含"U1"相关缺相/未接入提示
        // =========================================================================
        private static void S03_MotorPhaseMissing(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S03-MotorPhaseMissing";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s03", "R2.1 S03");
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "motor_1", definitionName = MotorDef, x = 200, y = 0 });
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            AssertDetailContainsKeyword(failures, evidence, scenario, workspace, adapter,
                expectedDetailKeyword: "U1");
        }

        // =========================================================================
        // S04: L1→T1 主触点旁路 → 最终报告必须出现 L1/T1 旁路具体说明，Severity=Error
        // =========================================================================
        private static void S04_MainContactBypass_L1_T1(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S04-MainContactBypass-L1-T1";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s04", "R2.1 S04");
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.wires.Add(Wire("km_1", "L1", "km_1", "T1"));
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            AssertBypassDetail(failures, evidence, scenario, workspace, adapter,
                expectedKeyword: "L1/T1",
                expectedSeverity: CircuitIssueSeverity.Error,
                expectedCode: "CONTACTOR_MAIN_CONTACT_BYPASS");
        }

        // =========================================================================
        // S05: 33→34 辅助触点旁路 → 最终报告必须出现 33/34 旁路具体说明，Severity=Warning
        // =========================================================================
        private static void S05_AuxContactBypass_33_34(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S05-AuxContactBypass-33-34";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s05", "R2.1 S05");
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.wires.Add(Wire("km_1", "33", "km_1", "34"));
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            AssertBypassDetail(failures, evidence, scenario, workspace, adapter,
                expectedKeyword: "33/34",
                expectedSeverity: CircuitIssueSeverity.Warning,
                expectedCode: "CONTACTOR_AUX_CONTACT_BYPASS");
        }

        // =========================================================================
        // S06: 34→A1 正常跳线 → 不得产生旁路误报
        // =========================================================================
        private static void S06_NormalJumper_34_A1(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S06-NormalJumper-34-A1";
            ResetWorkspace(workspace);

            var dto = NewDto("r2_1_s06", "R2.1 S06");
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 0, y = 0 });
            dto.wires.Add(Wire("km_1", "34", "km_1", "A1"));
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            // B5 detector 不应产生任何旁路 issue
            var bypassIssues = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var bypassCount = bypassIssues != null ? bypassIssues.Count : 0;
            if (bypassCount > 0)
            {
                var codes = bypassIssues != null
                    ? string.Join(", ", bypassIssues.Select(i => i.code))
                    : "(null)";
                failures.Add($"{scenario}: B5 detector 产生了 {bypassCount} 个旁路 issue（{codes}），正常跳线不应误报");
            }

            // 最终报告"问题与风险"不应包含旁路文本
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);
            var containsBypassText = detail.Contains("触点被直接短接");
            if (containsBypassText)
            {
                failures.Add($"{scenario}: 最终报告包含旁路文本 触点被直接短接，正常跳线不应误报");
            }

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  B5 bypass issue count: {bypassCount}");
            evidence.AppendLine($"  detail contains bypass text: {containsBypassText}");
            evidence.AppendLine();
        }

        // =========================================================================
        // S07: 正常工业自锁电路 → 不得因 B5 合流新增误报
        // =========================================================================
        private static void S07_NormalIndustrialSelfHold(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S07-NormalIndustrialSelfHold";
            ResetWorkspace(workspace);

            var dto = BuildNormalSelfHoldDto("r2_1_s07", "R2.1 S07");
            if (!CircuitTemplateSpawnService.Spawn(dto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Spawn failed: {msg}");
                return;
            }

            // B5 detector 不应产生任何旁路 issue
            var bypassIssues = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var bypassCount = bypassIssues != null ? bypassIssues.Count : 0;
            if (bypassCount > 0)
            {
                var codes = bypassIssues != null
                    ? string.Join(", ", bypassIssues.Select(i => i.code))
                    : "(null)";
                failures.Add($"{scenario}: 正常自锁电路 B5 detector 产生了 {bypassCount} 个旁路 issue（{codes}），不应误报");
            }

            // 最终报告不应包含旁路文本
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);
            var containsBypassText = detail.Contains("触点被直接短接");
            if (containsBypassText)
            {
                failures.Add($"{scenario}: 正常自锁电路最终报告包含旁路文本 触点被直接短接，不应误报");
            }

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  B5 bypass issue count: {bypassCount}");
            evidence.AppendLine($"  detail contains bypass text: {containsBypassText}");
            evidence.AppendLine();
        }

        // =========================================================================
        // S08: baseline + L1→T1 → 主触点旁路 Error 准确增加 1
        // =========================================================================
        private static void S08_MainBypassSeverityIncrement(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S08-MainBypassSeverityIncrement";

            // baseline: 正常自锁电路
            ResetWorkspace(workspace);
            var baselineDto = BuildNormalSelfHoldDto("r2_1_s08_base", "R2.1 S08 Baseline");
            if (!CircuitTemplateSpawnService.Spawn(baselineDto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Baseline Spawn failed: {msg}");
                return;
            }

            IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var baselineIndustrial);
            var baselineBypass = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var baselineBypassErrors = CountBypassBySeverity(baselineBypass, CircuitIssueSeverity.Error);
            var baselineMergedErrors = baselineIndustrial.ErrorCount + baselineBypassErrors;

            // modified: 加入 L1→T1
            ResetWorkspace(workspace);
            var modifiedDto = BuildNormalSelfHoldDto("r2_1_s08_mod", "R2.1 S08 Modified");
            modifiedDto.wires.Add(Wire("km_1", "L1", "km_1", "T1"));
            if (!CircuitTemplateSpawnService.Spawn(modifiedDto, workspace, saveLoad.Catalog, out msg))
            {
                failures.Add($"{scenario}: Modified Spawn failed: {msg}");
                return;
            }

            IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var modifiedIndustrial);
            var modifiedBypass = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var modifiedBypassErrors = CountBypassBySeverity(modifiedBypass, CircuitIssueSeverity.Error);
            var modifiedMergedErrors = modifiedIndustrial.ErrorCount + modifiedBypassErrors;

            // 最终报告必须包含 L1/T1 旁路文本
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);
            var containsL1T1 = detail.Contains("L1/T1");

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  baseline: industrialErrors={baselineIndustrial.ErrorCount}, bypassErrors={baselineBypassErrors}, merged={baselineMergedErrors}");
            evidence.AppendLine($"  modified: industrialErrors={modifiedIndustrial.ErrorCount}, bypassErrors={modifiedBypassErrors}, merged={modifiedMergedErrors}");
            evidence.AppendLine($"  detail contains L1/T1: {containsL1T1}");
            evidence.AppendLine();

            if (modifiedBypassErrors != baselineBypassErrors + 1)
            {
                failures.Add($"{scenario}: bypass Error 增量={modifiedBypassErrors - baselineBypassErrors}，期望 1");
            }

            if (modifiedMergedErrors != baselineMergedErrors + 1)
            {
                failures.Add($"{scenario}: merged Error 增量={modifiedMergedErrors - baselineMergedErrors}，期望 1（去重后语义一致）");
            }

            if (!detail.Contains("L1/T1"))
            {
                failures.Add($"{scenario}: 最终报告不含 'L1/T1' 旁路文本");
            }
        }

        // =========================================================================
        // S09: baseline + 33→34 → 辅助触点旁路 Warning 准确增加 1
        // =========================================================================
        private static void S09_AuxBypassSeverityIncrement(
            List<string> failures, StringBuilder evidence,
            WorkspaceController workspace, SaveLoadService saveLoad, IInspectionWorkflowRuntimeAdapter adapter)
        {
            const string scenario = "S09-AuxBypassSeverityIncrement";

            // baseline: 正常自锁电路
            ResetWorkspace(workspace);
            var baselineDto = BuildNormalSelfHoldDto("r2_1_s09_base", "R2.1 S09 Baseline");
            if (!CircuitTemplateSpawnService.Spawn(baselineDto, workspace, saveLoad.Catalog, out var msg))
            {
                failures.Add($"{scenario}: Baseline Spawn failed: {msg}");
                return;
            }

            IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var baselineIndustrial);
            var baselineBypass = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var baselineBypassWarnings = CountBypassBySeverity(baselineBypass, CircuitIssueSeverity.Warning);
            var baselineMergedWarnings = baselineIndustrial.WarningCount + baselineBypassWarnings;

            // modified: 加入 33→34
            ResetWorkspace(workspace);
            var modifiedDto = BuildNormalSelfHoldDto("r2_1_s09_mod", "R2.1 S09 Modified");
            modifiedDto.wires.Add(Wire("km_1", "33", "km_1", "34"));
            if (!CircuitTemplateSpawnService.Spawn(modifiedDto, workspace, saveLoad.Catalog, out msg))
            {
                failures.Add($"{scenario}: Modified Spawn failed: {msg}");
                return;
            }

            IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var modifiedIndustrial);
            var modifiedBypass = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var modifiedBypassWarnings = CountBypassBySeverity(modifiedBypass, CircuitIssueSeverity.Warning);
            var modifiedMergedWarnings = modifiedIndustrial.WarningCount + modifiedBypassWarnings;

            // 最终报告必须包含 33/34 旁路文本
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);
            var contains33_34 = detail.Contains("33/34");

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  baseline: industrialWarnings={baselineIndustrial.WarningCount}, bypassWarnings={baselineBypassWarnings}, merged={baselineMergedWarnings}");
            evidence.AppendLine($"  modified: industrialWarnings={modifiedIndustrial.WarningCount}, bypassWarnings={modifiedBypassWarnings}, merged={modifiedMergedWarnings}");
            evidence.AppendLine($"  detail contains 33/34: {contains33_34}");
            evidence.AppendLine();

            if (modifiedBypassWarnings != baselineBypassWarnings + 1)
            {
                failures.Add($"{scenario}: bypass Warning 增量={modifiedBypassWarnings - baselineBypassWarnings}，期望 1");
            }

            if (modifiedMergedWarnings != baselineMergedWarnings + 1)
            {
                failures.Add($"{scenario}: merged Warning 增量={modifiedMergedWarnings - baselineMergedWarnings}，期望 1（去重后语义一致）");
            }

            if (!detail.Contains("33/34"))
            {
                failures.Add($"{scenario}: 最终报告不含 '33/34' 旁路文本");
            }
        }

        // =========================================================================
        // 断言辅助
        // =========================================================================

        private static void AssertDetailContainsKeyword(
            List<string> failures, StringBuilder evidence, string scenario,
            WorkspaceController workspace, IInspectionWorkflowRuntimeAdapter adapter,
            string expectedDetailKeyword)
        {
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  expected keyword: '{expectedDetailKeyword}'");
            evidence.AppendLine($"  detail contains keyword: {detail.Contains(expectedDetailKeyword)}");
            evidence.AppendLine($"  detail:\n{detail}");
            evidence.AppendLine();

            if (!detail.Contains(expectedDetailKeyword))
            {
                failures.Add($"{scenario}: 最终[问题与风险]不含 '{expectedDetailKeyword}'");
            }
        }

        private static void AssertBypassDetail(
            List<string> failures, StringBuilder evidence, string scenario,
            WorkspaceController workspace, IInspectionWorkflowRuntimeAdapter adapter,
            string expectedKeyword, CircuitIssueSeverity expectedSeverity, string expectedCode)
        {
            // 验证 B5 detector 产出
            var bypassIssues = CircuitRuleChecker.DetectContactorBypassIssues(workspace);
            var matchingIssue = bypassIssues != null
                ? bypassIssues.FirstOrDefault(i => i != null && i.code == expectedCode)
                : null;

            if (matchingIssue == null)
            {
                failures.Add($"{scenario}: B5 detector 未产出 code='{expectedCode}'");
            }
            else if (matchingIssue.severity != expectedSeverity)
            {
                failures.Add($"{scenario}: B5 detector code='{expectedCode}' severity={matchingIssue.severity}，期望 {expectedSeverity}");
            }

            // 验证最终报告包含旁路文本
            var service = new InspectionWorkflowService(workspace, adapter, false);
            var wfResult = service.CreateCheckReport();
            var detail = ExtractProblemDetail(wfResult.Report);
            var containsKeyword = detail.Contains(expectedKeyword);
            var containsBypassText = detail.Contains("触点被直接短接");

            evidence.AppendLine($"--- {scenario} ---");
            evidence.AppendLine($"  B5 code: {expectedCode}, severity: {expectedSeverity}");
            evidence.AppendLine($"  B5 issue found: {matchingIssue != null}");
            evidence.AppendLine($"  expected keyword: '{expectedKeyword}'");
            evidence.AppendLine($"  detail contains keyword: {containsKeyword}");
            evidence.AppendLine($"  detail contains '触点被直接短接': {containsBypassText}");
            evidence.AppendLine($"  detail:\n{detail}");
            evidence.AppendLine();

            if (!containsKeyword)
            {
                failures.Add($"{scenario}: 最终[问题与风险]不含 '{expectedKeyword}'");
            }

            if (!containsBypassText)
            {
                failures.Add($"{scenario}: 最终[问题与风险]不含旁路通用文本'触点被直接短接'");
            }
        }

        private static int CountBypassBySeverity(IReadOnlyList<CircuitIssue> issues, CircuitIssueSeverity severity)
        {
            if (issues == null) return 0;
            var count = 0;
            for (var i = 0; i < issues.Count; i++)
            {
                if (issues[i] != null && issues[i].severity == severity)
                {
                    count++;
                }
            }
            return count;
        }

        private static string ExtractProblemDetail(InspectionReportData report)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < report.Blocks.Count; i++)
            {
                var block = report.Blocks[i];
                if (block.SectionTitle.Contains("问题与风险") ||
                    block.SectionTitle.Contains("问题") && block.SectionTitle.Contains("风险"))
                {
                    sb.AppendLine("[" + block.SectionTitle + "]");
                    sb.AppendLine(block.Body);
                }
            }
            if (sb.Length == 0)
            {
                sb.AppendLine("(未找到 [问题与风险] block)");
                for (var i = 0; i < report.Blocks.Count; i++)
                {
                    sb.AppendLine("  block[" + i + "]: " + report.Blocks[i].SectionTitle + " (Kind=" + report.Blocks[i].Kind + ")");
                }
            }
            return sb.ToString().Trim();
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();
        }

        private static CircuitTemplateDto NewDto(string id, string name)
        {
            return new CircuitTemplateDto
            {
                templateId = id,
                templateName = name,
                category = "Industrial",
                difficulty = "test"
            };
        }

        /// <summary>
        /// 构造正常工业自锁控制电路：
        /// Power.L → Stop.11 → Stop.12 → Start.23 → Start.24 → KM.A1 → KM.A2 → Power.N
        /// 自锁: Stop.12 → KM.13, KM.14 → KM.A1
        /// 主触点 L1/T1/L2/T2/L3/T3 未接（无电机），33/34 未接，21/22 未接。
        /// </summary>
        private static CircuitTemplateDto BuildNormalSelfHoldDto(string id, string name)
        {
            var dto = NewDto(id, name);
            dto.components.Add(new TemplateComponentDto { instanceId = "power_1", definitionName = PowerDef, x = 0, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "stop_1", definitionName = StopDef, x = 150, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "start_1", definitionName = StartDef, x = 300, y = 0 });
            dto.components.Add(new TemplateComponentDto { instanceId = "km_1", definitionName = KmDef, x = 450, y = 0 });

            dto.wires.Add(Wire("power_1", "L", "stop_1", "11"));
            dto.wires.Add(Wire("stop_1", "12", "start_1", "23"));
            dto.wires.Add(Wire("start_1", "24", "km_1", "A1"));
            dto.wires.Add(Wire("km_1", "A2", "power_1", "N"));
            dto.wires.Add(Wire("stop_1", "12", "km_1", "13"));
            dto.wires.Add(Wire("km_1", "14", "km_1", "A1"));
            return dto;
        }

        private static TemplateWireDto Wire(string fromC, string fromT, string toC, string toT)
        {
            return new TemplateWireDto
            {
                startComponentId = fromC, startTerminalId = fromT,
                endComponentId = toC, endTerminalId = toT,
                color = "#F21F1F", style = "Orthogonal"
            };
        }

        /// <summary>
        /// Stub 适配器：绕过 LocalInspectorPanel 运行态 UI 依赖，复现正式
        /// InspectionWorkflowService 调用链（CircuitStateAnalyzer + CircuitValidationService）。
        /// 不修改生产代码；仅用于隔离 EditMode 测试。
        /// </summary>
        private sealed class AuditRuntimeAdapter : IInspectionWorkflowRuntimeAdapter
        {
            private readonly WorkspaceController workspace;
            private readonly CircuitValidationService validationService;

            public AuditRuntimeAdapter(WorkspaceController workspace)
            {
                this.workspace = workspace;
                this.validationService = new CircuitValidationService();
            }

            public CircuitStateResult AnalyzeCircuitState()
            {
                var analyzer = new CircuitStateAnalyzer();
                return analyzer.Analyze(workspace.Components, workspace.WireManager.Wires);
            }

            public void ApplyRuntimeDisplayOverrides(CircuitStateResult stateResult)
            {
            }

            public string BuildRuntimeDisplaySummary(CircuitStateResult stateResult)
            {
                return stateResult != null ? stateResult.ToReadableText() : string.Empty;
            }

            public InspectionReportData BuildCurrentCircuitExplanationReportData(CircuitStateResult stateResult)
            {
                return new InspectionReportData();
            }

            public CircuitCheckResult FilterCheckPanelFalsePositives(CircuitCheckResult ruleResult, CircuitStateResult stateResult)
            {
                return ruleResult;
            }

            public string PrependCheckPanelRuntimeNotices(
                string report,
                CircuitStateResult stateResult,
                out IReadOnlyList<CircuitValidationIssue> validationIssues)
            {
                validationIssues = validationService.Validate(
                    workspace.Components, workspace.WireManager.Wires, stateResult).Issues;
                return report;
            }

            public string ResolveCurrentCircuitName()
            {
                return "Pipeline Test Circuit";
            }

            public string ApplyCurrentCircuitName(string text)
            {
                return text;
            }
        }
    }
}
