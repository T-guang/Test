// 仅供 Editor 使用的回归基线工具；采集时刻意复用生产模板生成路径，避免用测试夹具替代 18 张真实模板。
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using ElectricalSim.Rules;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools
{
    /// <summary>
    /// 采集或核对 18 张标准模板的结构、检查报告模型和 Validation 规则基线。
    /// 菜单生成入口会写入 <c>Assets/EditorTests/Baselines/V2.3.9.1</c> 下的 JSON 并刷新 AssetDatabase；
    /// 验证入口只读取既有快照并报告差异，不会接受或覆盖新的期望结果。
    ///
    /// 当前采集依赖 Play Mode 中的 Workspace、LocalInspectorPanel 与生产模板生成流程，不能以本文件内的
    /// Inspector 模型契约夹具替代真实模板取证。快照是行为漂移的比较基线，不是运行时数据源；
    /// Schema、报告 Section/Block 顺序、RuleId、Severity 及模板数量的变动都必须在审查差异后显式接受。
    /// 失败通过 Console 和异常日志暴露。本工具不修改模板 JSON、规则实现或 Player 功能。
    /// </summary>
    public static class ArchitectureBaselineSnapshotWriter
    {
        // 此 Editor 工具采集模板、规则和 Inspector 报告的可比较快照，用于发现架构契约的意外漂移；它不修复
        // 生产数据、不改变场景，也不应把运行过程中的瞬时对象引用、时间戳或随机顺序写入 baseline。
        private const string CatalogPath = "Blueprints/Templates/template_catalog";
        private const string BaselineAssetDirectory = "Assets/EditorTests/Baselines/V2.3.9.1";
        private static readonly string[] KnownRuleIds =
        {
            "POWER_POTENTIAL_CONFLICT", "LIVE_TO_PE_FAULT", "NEUTRAL_PE_MISUSE", "COIL_VOLTAGE_MISMATCH",
            "BREAKER_OR_FUSE_BYPASSED", "MOTOR_CONTACTOR_BYPASSED", "REVERSING_INTERLOCK_MISSING",
            "THERMAL_RELAY_CONTROL_BYPASSED", "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED", "TIMER_CONTROL_BYPASSED",
            "STOP_BUTTON_BYPASSED", "SELF_HOLDING_BRANCH_INCOMPLETE", "REVERSING_CONTACTOR_CONFLICT",
            "STAR_DELTA_PARTIAL_STARPOINT_SHORT", "STAR_DELTA_INPUT_TERMINAL_SHORT", "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY"
        };

        [MenuItem("Tools/Tests/生成架构重构基线")]
        private static void GenerateMenu()
        {
            Run(writeBaseline: true);
        }

        [MenuItem("Tools/Tests/验证架构重构基线")]
        private static void VerifyMenu()
        {
            Run(writeBaseline: false);
        }

        [MenuItem("Tools/Tests/生成 Inspector 报告模型基线")]
        private static void GenerateInspectorModelMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Inspector 报告模型基线", "请先进入 Play Mode，并等待主界面和默认示例加载完成。", "知道了");
                return;
            }

            try
            {
                WriteInspectorBaseline(CaptureAllTemplates());
                Debug.Log("Inspector 报告模型基线已生成：" + BaselineAssetDirectory + "（未改写模板和规则基线）。");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("Inspector 报告模型基线未完成；未写入或覆盖任何 expected 快照。");
            }
        }

        [MenuItem("Tools/Tests/运行 Inspector 模型基线缺失保护测试")]
        private static void RunInspectorBaselineContractTests()
        {
            var failures = new List<string>();
            AssertInspectorContractFailure(null, "缺少 InspectorReportSnapshots.json", failures);
            AssertInspectorContractFailure(new InspectorBundle { schemaVersion = 1, templates = CreateValidInspectorTemplates() }, "schemaVersion 过旧", failures);
            AssertInspectorContractSuccess(new InspectorBundle { schemaVersion = 2, templates = CreateValidInspectorTemplates() }, failures);
            AssertInspectorContractFailure(new InspectorBundle { schemaVersion = 2, templates = null }, "templates 缺失", failures);

            var missingModelBlocks = new InspectorBundle { schemaVersion = 2, templates = CreateValidInspectorTemplates() };
            missingModelBlocks.templates[0].check.modelBlocks = null;
            AssertInspectorContractFailure(missingModelBlocks, "check.modelBlocks 缺失", failures);

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("Inspector 模型基线缺失保护测试失败：\n" + string.Join("\n", failures.ToArray()));
            }

            Debug.Log("Inspector 模型基线缺失保护测试通过：5/5 成功。");
        }

        private static void Run(bool writeBaseline)
        {
            // 生成与校验共用同一捕获路径，区别只在于是否写入已审核的基线。这样“当前实现如何被观察”只有一份
            // 定义，避免生成器与校验器因各自遍历顺序不同而制造无意义差异。
            // 生成与验证共用真实模板采集路径；仅 writeBaseline=true 的明确菜单操作允许写入期望快照。
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("架构重构基线", "请先进入 Play Mode，并等待主界面和默认示例加载完成。", "知道了");
                return;
            }

            try
            {
                var snapshot = CaptureAllTemplates();
                var ruleCatalog = CreateRuleCatalog();
                if (writeBaseline)
                {
                    WriteBaseline(snapshot, ruleCatalog);
                    Debug.Log("架构重构基线已生成：" + BaselineAssetDirectory + "（仅写入 EditorTests 基线文件）。");
                    return;
                }

                var differences = VerifyAgainstBaseline(snapshot, ruleCatalog);
                if (differences.Count == 0)
                {
                    Debug.Log("架构重构基线验证通过：18 张真实模板的结构、规则和 Inspector 报告快照均一致。");
                }
                else
                {
                    Debug.LogError("架构重构基线验证失败（不会覆盖基线）：\n" + string.Join("\n", differences.ToArray()));
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("架构重构基线未完成；未写入或覆盖任何 expected 快照。");
            }
        }

        private static BaselineBundle CaptureAllTemplates()
        {
            // 模板按稳定 identity 捕获，不能依赖 Unity 场景对象顺序；快照目标是结构/规则/报告模型，而非运行时
            // GameObject 或临时 UI 的序列化外观。
            var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>();
            var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>();
            var inspector = UnityEngine.Object.FindObjectOfType<LocalInspectorPanel>();
            if (workspace == null || saveLoad == null || inspector == null)
            {
                throw new InvalidOperationException("基线需要 WorkspaceController、SaveLoadService 和 LocalInspectorPanel 均已在 Play Mode 初始化。");
            }

            if (!CircuitTemplateCatalogLoader.TryLoad(CatalogPath, out var catalog, out var catalogError))
            {
                throw new InvalidOperationException("无法读取真实模板目录：" + catalogError);
            }

            var items = catalog.templates
                .Where(item => item != null)
                .OrderBy(item => item.sortOrder)
                .ThenBy(item => item.templateId, StringComparer.Ordinal)
                .ToList();
            if (items.Count != 18)
            {
                throw new InvalidOperationException("模板目录数量不是预期的 18，而是 " + items.Count + "。请先确认当前模板目录。");
            }

            var analyzer = new CircuitStateAnalyzer();
            var validation = new CircuitValidationService();
            var bundle = new BaselineBundle
            {
                schemaVersion = 1,
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                templateCount = items.Count
            };

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var template, out var loadError))
                {
                    throw new InvalidOperationException("模板读取失败 " + item.templateId + "：" + loadError);
                }

                if (!CircuitTemplateSpawnService.Spawn(template, workspace, saveLoad.Catalog, out var spawnMessage))
                {
                    throw new InvalidOperationException("模板生成失败 " + item.templateId + "：" + spawnMessage);
                }

                Canvas.ForceUpdateCanvases();
                if (workspace.WorkspaceRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(workspace.WorkspaceRect);
                }

                var analysis = analyzer.Analyze(workspace.Components, workspace.WireManager.Wires);
                var report = validation.Validate(workspace.Components, workspace.WireManager.Wires, analysis);
                bundle.templates.Add(CaptureTemplate(item, template, workspace, analysis, report, inspector));
            }

            workspace.ClearDrawing(false);
            return bundle;
        }

        private static TemplateSnapshot CaptureTemplate(
            CircuitTemplateCatalogItemDto item,
            CircuitTemplateDto template,
            WorkspaceController workspace,
            CircuitStateResult analysis,
            CircuitValidationReport validation,
            LocalInspectorPanel inspector)
        {
            // 捕获时先通过正式加载和分析链取得事实，再投影为纯数据快照。不要在此工具中重写模板生成、拓扑或规则逻辑。
            var snapshot = new TemplateSnapshot
            {
                templateId = item.templateId,
                templateDisplayName = item.templateName,
                category = item.category,
                componentCount = workspace.Components.Count,
                wireCount = workspace.WireManager.Wires.Count,
                analyzerReturned = analysis != null,
                errorCount = CountSeverity(validation, CircuitValidationSeverity.Error),
                warningCount = CountSeverity(validation, CircuitValidationSeverity.Warning),
                hasComplexLoop = validation != null && validation.Issues.Any(issue => issue != null && issue.RuleId == "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY")
            };

            snapshot.definitionCounts = workspace.Components
                .Where(component => component != null && component.Definition != null)
                .GroupBy(component => component.Definition.name)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new NameCount { name = group.Key, count = group.Count() })
                .ToList();
            snapshot.components = CaptureComponents(analysis);
            snapshot.validationIssues = CaptureIssues(validation);
            snapshot.analysis = CaptureAnalysis(analysis);
            var checkSources = CaptureInspectorCheckSources(workspace, analysis, validation);
            snapshot.checkReport = CaptureInspectorReport(inspector, "CheckCurrentCircuit", checkSources);
            snapshot.explainReport = CaptureInspectorReport(inspector, "ExplainCurrentCircuit", null);
            return snapshot;
        }

        private static List<SemanticComponentSnapshot> CaptureComponents(CircuitStateResult analysis)
        {
            // 元件快照只保留报告和规则真正依赖的语义字段。不要把 GameObject 名称、屏幕坐标或临时选中状态纳入，
            // 否则纯 UI 调整会造成错误的架构基线失败。
            var result = new List<SemanticComponentSnapshot>();
            if (analysis == null)
            {
                return result;
            }

            var ordinalByDefinition = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var state in analysis.Components.Where(item => item != null).OrderBy(item => item.DefinitionName, StringComparer.Ordinal).ThenBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.InstanceId, StringComparer.Ordinal))
            {
                var definition = string.IsNullOrWhiteSpace(state.DefinitionName) ? "<unknown>" : state.DefinitionName;
                ordinalByDefinition.TryGetValue(definition, out var ordinal);
                ordinalByDefinition[definition] = ordinal + 1;
                result.Add(new SemanticComponentSnapshot
                {
                    definitionId = definition,
                    ordinal = ordinal + 1,
                    state = NormalizeText(state.State),
                    judgement = NormalizeText(state.Judgement),
                    isBreaker = state.IsBreaker,
                    breakerInputHasSupply = state.BreakerInputHasSupply,
                    breakerOutputHasSupply = state.BreakerOutputHasSupply,
                    isContactor = state.IsContactor,
                    contactorCoilEnergized = state.IsContactorCoilEnergizedByAnalyzer,
                    contactorMainClosed = state.IsContactorMainContactsClosedByAnalyzer,
                    isTimerRelay = state.IsTimerRelay,
                    timerCoilEnergized = state.IsTimerRelayCoilEnergizedByAnalyzer,
                    timerDelayElapsed = state.IsTimerDelayElapsed,
                    timerNoClosed = state.IsTimerDelayedNoClosed,
                    timerNcClosed = state.IsTimerDelayedNcClosed,
                    timerDelayStatus = NormalizeText(state.TimerDelayStatus),
                    isLimitSwitch = state.IsLimitSwitch,
                    limitSwitchTriggered = state.IsLimitSwitchTriggered,
                    isMotor = state.IsThreePhaseMotor,
                    isStarDeltaMotor = state.IsStarDeltaMotor,
                    starDeltaMode = NormalizeText(state.StarDeltaConnectionMode),
                    motorFeederContactors = NormalizeText(state.MotorFeederContactorNames)
                });
            }

            return result;
        }

        private static AnalysisSnapshot CaptureAnalysis(CircuitStateResult analysis)
        {
            // 分析快照记录正式 Analyzer 已输出的结论，而不是重新计算电气关系；基线工具必须跟随生产入口，
            // 否则会把测试夹具的假设误当成模板运行事实。
            if (analysis == null)
            {
                return new AnalysisSnapshot { available = false };
            }

            return new AnalysisSnapshot
            {
                available = true,
                hasShortCircuit = analysis.HasShortCircuit,
                hasPowerConflict = analysis.HasPowerConflict,
                hasContactorInterlockConflict = analysis.HasContactorInterlockConflict,
                hasLimitSwitches = analysis.HasLimitSwitches,
                hasTimerRelays = analysis.HasTimerRelays,
                hasStarDeltaMotors = analysis.HasStarDeltaMotors,
                hasThreePhaseCircuit = analysis.HasThreePhaseCircuit,
                unsupportedThreePhaseTopology = analysis.ContainsUnsupportedThreePhaseCircuit,
                analyzerErrorCount = analysis.Errors.Count,
                analyzerWarningCount = analysis.Warnings.Count
            };
        }

        private static List<RuleIssueSnapshot> CaptureIssues(CircuitValidationReport report)
        {
            // issue 按 RuleId、严重度与可读说明投影。RuleId 是稳定的内部诊断键，文字调整也应显式显示为基线差异，
            // 不能在此处为了“通过”而丢弃或模糊化。
            if (report == null)
            {
                return new List<RuleIssueSnapshot>();
            }

            return report.Issues
                .Where(issue => issue != null)
                .OrderBy(issue => issue.RuleId, StringComparer.Ordinal)
                .ThenBy(issue => issue.Severity.ToString(), StringComparer.Ordinal)
                .Select(issue => new RuleIssueSnapshot { ruleId = issue.RuleId, severity = issue.Severity.ToString(), category = issue.Category.ToString() })
                .ToList();
        }

        private static InspectorReportSourceSnapshot CaptureInspectorCheckSources(
            WorkspaceController workspace,
            CircuitStateResult analysis,
            CircuitValidationReport validation)
        {
            var sources = new InspectorReportSourceSnapshot
            {
                analyzerErrorCount = analysis == null ? 0 : analysis.Errors.Count,
                analyzerWarningCount = analysis == null ? 0 : analysis.Warnings.Count,
                validationErrorCount = CountSeverity(validation, CircuitValidationSeverity.Error),
                validationWarningCount = CountSeverity(validation, CircuitValidationSeverity.Warning),
                validationRuleIds = validation == null
                    ? new List<string>()
                    : validation.Issues.Where(issue => issue != null).Select(issue => issue.RuleId).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList()
            };

            if (IndustrialCircuitRuleAnalyzer.TryAnalyze(workspace, out var industrial) && industrial != null && industrial.IsIndustrial)
            {
                sources.checkPipeline = "IndustrialCircuitRuleAnalyzer";
                sources.pipelineErrorCount = industrial.ErrorCount;
                sources.pipelineWarningCount = industrial.WarningCount;
                return sources;
            }

            var raw = new CircuitRuleChecker(workspace).Check();
            var filter = typeof(LocalInspectorPanel).GetMethod("FilterCheckPanelFalsePositives", BindingFlags.Static | BindingFlags.NonPublic);
            var displayed = filter == null ? raw : filter.Invoke(null, new object[] { raw, analysis }) as CircuitCheckResult;
            displayed = displayed ?? raw;
            sources.checkPipeline = "CircuitRuleChecker.FilterCheckPanelFalsePositives";
            sources.pipelineErrorCount = displayed == null ? 0 : displayed.ErrorCount;
            sources.pipelineWarningCount = displayed == null ? 0 : displayed.WarningCount;
            sources.pipelineIssueCodes = displayed == null
                ? new List<string>()
                : displayed.issues.Where(issue => issue != null).Select(issue => issue.code).Distinct().OrderBy(code => code, StringComparer.Ordinal).ToList();
            return sources;
        }

        private static InspectorReportSnapshot CaptureInspectorReport(LocalInspectorPanel inspector, string methodName, InspectorReportSourceSnapshot sources)
        {
            // Inspector 报告通过真实面板入口取得，确保 workflow、composer 与渲染镜像的交界都被覆盖；反射仅用于
            // 读取已生成模型，不应成为业务调用的替代路径。
            var method = typeof(LocalInspectorPanel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            var field = typeof(LocalInspectorPanel).GetField("reportContent", BindingFlags.Instance | BindingFlags.NonPublic);
            var modelField = typeof(LocalInspectorPanel).GetField("renderedReportData", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null || field == null || modelField == null)
            {
                throw new MissingMemberException("LocalInspectorPanel", methodName + "、reportContent 或 renderedReportData");
            }

            method.Invoke(inspector, null);
            Canvas.ForceUpdateCanvases();
            var content = field.GetValue(inspector) as RectTransform;
            if (content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }

            var snapshot = new InspectorReportSnapshot
            {
                entryPoint = methodName,
                available = content != null,
                sources = sources,
                modelBlocks = new List<InspectorModelBlockSnapshot>()
            };
            if (content == null)
            {
                return snapshot;
            }

            for (var i = 0; i < content.childCount; i++)
            {
                var block = content.GetChild(i);
                var texts = block.GetComponentsInChildren<Text>(true);
                var title = texts.Length > 0 ? NormalizeText(texts[0].text) : string.Empty;
                var body = texts.Length > 1 ? NormalizeText(string.Join(" ", texts.Skip(1).Select(text => text.text).ToArray())) : string.Empty;
                snapshot.blocks.Add(new InspectorBlockSnapshot
                {
                    title = title,
                    blockType = InferBlockType(title),
                    severity = InferSeverity(title, body),
                    ruleIds = KnownRuleIds.Where(ruleId => body.IndexOf(ruleId, StringComparison.Ordinal) >= 0).ToList(),
                    keyPhrases = ExtractKeyPhrases(body),
                    containsRuntimeParagraph = ContainsAny(body, "当前停止", "当前正转运行", "当前反转运行", "KT 正在计时", "星形启动阶段", "三角运行阶段"),
                    containsParameterParagraph = ContainsAny(body, "参数估算", "估算")
                });
            }

            var renderedReportData = modelField.GetValue(inspector) as InspectionReportData;
            if (renderedReportData == null)
            {
                throw new InvalidOperationException(methodName + " 未保留 renderedReportData。");
            }

            for (var i = 0; i < renderedReportData.Blocks.Count; i++)
            {
                var block = renderedReportData.Blocks[i];
                if (block == null) continue;
                snapshot.modelBlocks.Add(new InspectorModelBlockSnapshot
                {
                    sectionTitle = NormalizeText(block.SectionTitle),
                    kind = block.Kind.ToString(),
                    severity = block.Severity.ToString(),
                    ruleIds = (block.RuleIds ?? Array.Empty<string>())
                        .Where(ruleId => !string.IsNullOrWhiteSpace(ruleId))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(ruleId => ruleId, StringComparer.Ordinal)
                        .ToList()
                });
            }

            if (snapshot.blocks.Count != snapshot.modelBlocks.Count)
            {
                throw new InvalidOperationException(methodName + " 的 UI Block 数量与报告模型数量不一致：" + snapshot.blocks.Count + "/" + snapshot.modelBlocks.Count);
            }

            for (var i = 0; i < snapshot.blocks.Count; i++)
            {
                if (!string.Equals(snapshot.blocks[i].title, snapshot.modelBlocks[i].sectionTitle, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(methodName + " 的 UI Block 与报告模型标题顺序不一致：" + snapshot.blocks[i].title + "/" + snapshot.modelBlocks[i].sectionTitle);
                }
            }

            return snapshot;
        }

        private static string InferBlockType(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Body";
            if (title.IndexOf("错误", StringComparison.OrdinalIgnoreCase) >= 0) return "Error";
            if (title.IndexOf("提醒", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("警告", StringComparison.OrdinalIgnoreCase) >= 0) return "Warning";
            if (title.IndexOf("通过", StringComparison.OrdinalIgnoreCase) >= 0) return "Success";
            if (title.IndexOf("说明", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("解释", StringComparison.OrdinalIgnoreCase) >= 0) return "Information";
            return "Section";
        }

        private static string InferSeverity(string title, string body)
        {
            var text = title + " " + body;
            if (text.IndexOf("错误", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0) return "Error";
            if (text.IndexOf("提醒", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("警告", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0) return "Warning";
            return "None";
        }

        private static List<string> ExtractKeyPhrases(string text)
        {
            var candidates = new[] { "当前停止", "当前正转运行", "当前反转运行", "KT 正在计时", "星形启动阶段", "三角运行阶段", "热继主回路", "电机绕过接触器", "时间继电器控制旁路" };
            return candidates.Where(candidate => text.IndexOf(candidate, StringComparison.Ordinal) >= 0).ToList();
        }

        private static bool ContainsAny(string text, params string[] candidates)
        {
            return candidates.Any(candidate => text.IndexOf(candidate, StringComparison.Ordinal) >= 0);
        }

        private static int CountSeverity(CircuitValidationReport report, CircuitValidationSeverity severity)
        {
            return report == null ? 0 : report.Issues.Count(issue => issue != null && issue.Severity == severity);
        }

        private static RuleCatalogSnapshot CreateRuleCatalog()
        {
            // 规则目录从当前实现集中列举，用于检测“规则悄然消失/严重度漂移”。目录不替代运行时规则执行，
            // 新规则加入时必须同时评估模板与报告基线。
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var validationDirectory = Path.Combine(projectRoot, "Assets", "Scripts", "Core", "Validation");
            if (!Directory.Exists(validationDirectory))
            {
                throw new DirectoryNotFoundException("无法读取生产验证规则目录：" + validationDirectory);
            }

            var coreDirectory = Path.Combine(projectRoot, "Assets", "Scripts", "Core");
            var allCoreFiles = Directory.GetFiles(coreDirectory, "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).ToList();
            var discovered = new Dictionary<string, RuleSeveritySnapshot>(StringComparer.Ordinal);
            var constPattern = new Regex("const\\s+string\\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*\\\"(?<id>[A-Z0-9_]+)\\\"", RegexOptions.Multiline);
            var issuePattern = new Regex("(?:AddIssue|CreateIssue)\\s*\\(\\s*(?:[A-Za-z_][A-Za-z0-9_]*\\s*,\\s*)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*|\\\"[A-Z0-9_]+\\\")\\s*,\\s*CircuitValidationSeverity\\.(?<severity>[A-Za-z_][A-Za-z0-9_]*)\\s*,\\s*CircuitValidationCategory\\.(?<category>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
            var indirectIssuePattern = new Regex("(?:AddIssue|CreateIssue)\\s*\\(\\s*(?:[A-Za-z_][A-Za-z0-9_]*\\s*,\\s*)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_][A-Za-z0-9_]*)*|\\\"[A-Z0-9_]+\\\")\\s*,\\s*(?<severitySymbol>[A-Za-z_][A-Za-z0-9_]*)\\s*,\\s*CircuitValidationCategory\\.(?<category>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
            var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var coreFile in allCoreFiles)
            {
                var text = File.ReadAllText(coreFile);
                var className = Regex.Match(text, "(?:class|struct)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)").Groups["name"].Value;
                foreach (Match constant in constPattern.Matches(text))
                {
                    symbols[constant.Groups["symbol"].Value] = constant.Groups["id"].Value;
                    if (!string.IsNullOrWhiteSpace(className)) symbols[className + "." + constant.Groups["symbol"].Value] = constant.Groups["id"].Value;
                }
            }

            foreach (var file in Directory.GetFiles(validationDirectory, "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                var text = File.ReadAllText(file);
                foreach (Match match in issuePattern.Matches(text))
                {
                    var token = match.Groups["symbol"].Value;
                    var ruleId = token.StartsWith("\"") ? token.Trim('\"') : (symbols.TryGetValue(token, out var value) ? value : null);
                    if (string.IsNullOrWhiteSpace(ruleId) || Array.IndexOf(KnownRuleIds, ruleId) < 0) continue;
                    if (!discovered.TryGetValue(ruleId, out var rule))
                    {
                        rule = new RuleSeveritySnapshot { ruleId = ruleId };
                        discovered.Add(ruleId, rule);
                    }
                    AddDistinct(rule.severities, match.Groups["severity"].Value);
                    AddDistinct(rule.categories, match.Groups["category"].Value);
                    AddDistinct(rule.sourceLocations, MakeSourceLocation(projectRoot, file, text, match.Index));
                }

                foreach (Match match in indirectIssuePattern.Matches(text))
                {
                    if (!string.Equals(match.Groups["severitySymbol"].Value, "severity", StringComparison.Ordinal)) continue;
                    var token = match.Groups["symbol"].Value;
                    var ruleId = token.StartsWith("\"") ? token.Trim('\"') : (symbols.TryGetValue(token, out var value) ? value : null);
                    if (string.IsNullOrWhiteSpace(ruleId) || Array.IndexOf(KnownRuleIds, ruleId) < 0) continue;
                    var methodName = FindContainingMethodName(text, match.Index);
                    if (string.IsNullOrWhiteSpace(methodName)) continue;
                    var invocationPattern = new Regex(methodName + "\\s*\\([\\s\\S]{0,420}?CircuitValidationSeverity\\.(?<severity>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Singleline);
                    foreach (Match invocation in invocationPattern.Matches(text))
                    {
                        if (invocation.Index >= match.Index) continue;
                        if (!discovered.TryGetValue(ruleId, out var rule))
                        {
                            rule = new RuleSeveritySnapshot { ruleId = ruleId };
                            discovered.Add(ruleId, rule);
                        }
                        AddDistinct(rule.severities, invocation.Groups["severity"].Value);
                        AddDistinct(rule.categories, match.Groups["category"].Value);
                        AddDistinct(rule.sourceLocations, MakeSourceLocation(projectRoot, file, text, invocation.Index));
                    }
                }
            }

            var missing = KnownRuleIds.Where(id => !discovered.ContainsKey(id)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException("未能从生产 CircuitValidationIssue 发射点解析以下 RuleId 的 Severity：" + string.Join("、", missing));
            }

            return new RuleCatalogSnapshot { schemaVersion = 2, rules = KnownRuleIds.Select(id => discovered[id]).ToList() };
        }

        private static void WriteBaseline(BaselineBundle bundle, RuleCatalogSnapshot ruleCatalog)
        {
            // 写入前应保证捕获结果已经按稳定键规范化。baseline 是长期比较协议，字段重排或删除应视为兼容性变更，
            // 不能借一次快照更新掩盖真实的模板/规则行为漂移。
            var directory = EnsureBaselineDirectory();
            WriteJson(Path.Combine(directory, "TemplateStaticSnapshots.json"), bundle);
            WriteInspectorBaseline(bundle);
            WriteJson(Path.Combine(directory, "ValidationRuleSnapshots.json"), ruleCatalog);
            AssetDatabase.Refresh();
        }

        private static void WriteInspectorBaseline(BaselineBundle bundle)
        {
            var directory = EnsureBaselineDirectory();
            WriteJson(Path.Combine(directory, "InspectorReportSnapshots.json"), CreateInspectorBundle(bundle));
            AssetDatabase.Refresh();
        }

        private static InspectorBundle CreateInspectorBundle(BaselineBundle bundle)
        {
            return new InspectorBundle
            {
                schemaVersion = 2,
                templates = bundle.templates.Select(template => new InspectorTemplateSnapshot
                {
                    templateId = template.templateId,
                    check = template.checkReport,
                    explain = template.explainReport
                }).ToList()
            };
        }

        private static List<string> VerifyAgainstBaseline(BaselineBundle actual, RuleCatalogSnapshot actualRules)
        {
            // 校验返回全部可读差异而不是首个异常，便于一次定位模板、规则目录和报告模型的契约变化；不修改任何资产。
            var directory = EnsureBaselineDirectory();
            var expected = TryReadJson<BaselineBundle>(Path.Combine(directory, "TemplateStaticSnapshots.json"), out var templateReadError);
            var expectedRules = TryReadJson<RuleCatalogSnapshot>(Path.Combine(directory, "ValidationRuleSnapshots.json"), out var ruleReadError);
            var expectedInspector = TryReadJson<InspectorBundle>(Path.Combine(directory, "InspectorReportSnapshots.json"), out var inspectorReadError);
            var differences = new List<string>();
            if (expected == null)
            {
                differences.Add(string.IsNullOrWhiteSpace(templateReadError) ? "缺少 TemplateStaticSnapshots.json。" : templateReadError);
            }
            if (expectedRules == null)
            {
                differences.Add(string.IsNullOrWhiteSpace(ruleReadError) ? "缺少 ValidationRuleSnapshots.json。" : ruleReadError);
            }
            if (expectedInspector == null && !string.IsNullOrWhiteSpace(inspectorReadError))
            {
                differences.Add(inspectorReadError);
            }
            else
            {
                AppendInspectorBaselineContractErrors(expectedInspector, differences);
            }
            if (differences.Count > 0)
            {
                return differences;
            }

            if (expected.templateCount != actual.templateCount) differences.Add("TemplateCount: expected=" + expected.templateCount + ", actual=" + actual.templateCount);
            foreach (var template in actual.templates)
            {
                var old = expected.templates.FirstOrDefault(item => item.templateId == template.templateId);
                if (old == null)
                {
                    differences.Add(template.templateId + ": 新增模板或 expected 缺失。");
                    continue;
                }
                CompareTemplate(old, template, differences);
            }
            foreach (var expectedRule in expectedRules.rules)
            {
                var current = actualRules.rules.FirstOrDefault(item => item.ruleId == expectedRule.ruleId);
                if (current == null || !SameSequence(expectedRule.severities, current.severities) || !SameSequence(expectedRule.categories, current.categories))
                {
                    differences.Add("Rule severity contract: " + expectedRule.ruleId + " expected=" + DescribeRule(expectedRule) + ", actual=" + (current == null ? "<missing>" : DescribeRule(current)));
                }
            }
            CompareInspectorBundle(expectedInspector, CreateInspectorBundle(actual), differences);
            return differences;
        }

        private static void AppendInspectorBaselineContractErrors(InspectorBundle expectedInspector, List<string> differences)
        {
            if (expectedInspector == null)
            {
                differences.Add("缺少 InspectorReportSnapshots.json，无法验证 Inspector 报告模型。");
                return;
            }
            if (expectedInspector.schemaVersion < 2)
            {
                differences.Add("InspectorReportSnapshots.json 的 schemaVersion 过旧：" + expectedInspector.schemaVersion + "，当前至少需要版本 2。");
                return;
            }
            if (expectedInspector.templates == null)
            {
                differences.Add("InspectorReportSnapshots.json 的 templates 缺失，无法验证 Inspector 报告模型。");
                return;
            }
            if (expectedInspector.templates.Count != 18)
            {
                differences.Add("InspectorReportSnapshots.json 模板数量异常：expected=18, actual=" + expectedInspector.templates.Count + "。");
            }

            for (var i = 0; i < expectedInspector.templates.Count; i++)
            {
                var template = expectedInspector.templates[i];
                var templateId = template == null || string.IsNullOrWhiteSpace(template.templateId) ? "<unknown>" : template.templateId;
                if (template == null)
                {
                    differences.Add("InspectorReportSnapshots.json 包含空模板记录：index=" + i + "。");
                    continue;
                }
                if (template.check == null)
                {
                    differences.Add(templateId + ": Inspector check 快照缺失。");
                }
                else if (template.check.modelBlocks == null)
                {
                    differences.Add(templateId + ": Inspector check.modelBlocks 缺失。");
                }
                if (template.explain == null)
                {
                    differences.Add(templateId + ": Inspector explain 快照缺失。");
                }
                else if (template.explain.modelBlocks == null)
                {
                    differences.Add(templateId + ": Inspector explain.modelBlocks 缺失。");
                }
            }
        }

        private static List<InspectorTemplateSnapshot> CreateValidInspectorTemplates()
        {
            var templates = new List<InspectorTemplateSnapshot>();
            for (var i = 0; i < 18; i++)
            {
                templates.Add(new InspectorTemplateSnapshot
                {
                    templateId = "contract_" + i,
                    check = new InspectorReportSnapshot { modelBlocks = new List<InspectorModelBlockSnapshot>() },
                    explain = new InspectorReportSnapshot { modelBlocks = new List<InspectorModelBlockSnapshot>() }
                });
            }
            return templates;
        }

        private static void AssertInspectorContractFailure(InspectorBundle inspector, string expectedReason, ICollection<string> failures)
        {
            var differences = new List<string>();
            AppendInspectorBaselineContractErrors(inspector, differences);
            if (!differences.Any(error => error.IndexOf(expectedReason, StringComparison.Ordinal) >= 0))
            {
                failures.Add("未检测到预期失败：" + expectedReason + "。actual=[" + string.Join(" | ", differences.ToArray()) + "]");
            }
        }

        private static void AssertInspectorContractSuccess(InspectorBundle inspector, ICollection<string> failures)
        {
            var differences = new List<string>();
            AppendInspectorBaselineContractErrors(inspector, differences);
            if (differences.Count > 0)
            {
                failures.Add("有效 Inspector 基线被错误拒绝：" + string.Join(" | ", differences.ToArray()));
            }
        }

        private static void CompareInspectorBundle(InspectorBundle expected, InspectorBundle actual, List<string> differences)
        {
            foreach (var expectedTemplate in expected.templates)
            {
                var actualTemplate = actual.templates.FirstOrDefault(template => template.templateId == expectedTemplate.templateId);
                if (actualTemplate == null)
                {
                    differences.Add("Inspector model: expected template missing " + expectedTemplate.templateId);
                    continue;
                }

                CompareInspectorReportModel(expectedTemplate.templateId + ": Check", expectedTemplate.check, actualTemplate.check, differences);
                CompareInspectorReportModel(expectedTemplate.templateId + ": Explain", expectedTemplate.explain, actualTemplate.explain, differences);
            }
        }

        private static void CompareInspectorReportModel(string prefix, InspectorReportSnapshot expected, InspectorReportSnapshot actual, List<string> differences)
        {
            // Inspector 模型比较保留 section、kind、severity、顺序和关键短语，既能发现用户可见报告断裂，
            // 又避免把字体、布局尺寸等纯表现细节误作为架构差异。
            CompareStringSequence(prefix + " UI blocks", expected.blocks.Select(block => block.title + ":" + block.blockType), actual.blocks.Select(block => block.title + ":" + block.blockType), differences);
            CompareStringSequence(prefix + " model blocks", expected.modelBlocks.Select(DescribeModelBlock), actual.modelBlocks.Select(DescribeModelBlock), differences);
        }

        private static string DescribeModelBlock(InspectorModelBlockSnapshot block)
        {
            return block.sectionTitle + ":" + block.kind + ":" + block.severity + ":" + string.Join(",", block.ruleIds ?? new List<string>());
        }

        private static void CompareTemplate(TemplateSnapshot expected, TemplateSnapshot actual, List<string> differences)
        {
            // 比较的是规范化后的语义序列。对象创建顺序、临时 instance 引用和展示排版不应成为架构基线的噪声。
            var prefix = actual.templateId + ": ";
            if (expected.componentCount != actual.componentCount) differences.Add(prefix + "ComponentCount expected=" + expected.componentCount + ", actual=" + actual.componentCount);
            if (expected.wireCount != actual.wireCount) differences.Add(prefix + "WireCount expected=" + expected.wireCount + ", actual=" + actual.wireCount);
            if (expected.errorCount != actual.errorCount || expected.warningCount != actual.warningCount) differences.Add(prefix + "Validation counts expected=" + expected.errorCount + "/" + expected.warningCount + ", actual=" + actual.errorCount + "/" + actual.warningCount);
            CompareStringSequence(prefix + "DefinitionCounts", expected.definitionCounts.Select(item => item.name + "=" + item.count), actual.definitionCounts.Select(item => item.name + "=" + item.count), differences);
            CompareStringSequence(prefix + "RuleIds", expected.validationIssues.Select(item => item.ruleId + ":" + item.severity), actual.validationIssues.Select(item => item.ruleId + ":" + item.severity), differences);
            CompareStringSequence(prefix + "Check sections", expected.checkReport.blocks.Select(item => item.title + ":" + item.blockType), actual.checkReport.blocks.Select(item => item.title + ":" + item.blockType), differences);
            CompareStringSequence(prefix + "Explain sections", expected.explainReport.blocks.Select(item => item.title + ":" + item.blockType), actual.explainReport.blocks.Select(item => item.title + ":" + item.blockType), differences);
        }

        private static void CompareStringSequence(string label, IEnumerable<string> expected, IEnumerable<string> actual, List<string> differences)
        {
            var left = string.Join("|", expected.ToArray());
            var right = string.Join("|", actual.ToArray());
            if (!string.Equals(left, right, StringComparison.Ordinal)) differences.Add(label + " expected=[" + left + "], actual=[" + right + "]");
        }

        private static bool SameSequence(IEnumerable<string> left, IEnumerable<string> right)
        {
            return string.Join("|", left ?? new List<string>()) == string.Join("|", right ?? new List<string>());
        }

        private static string DescribeRule(RuleSeveritySnapshot rule)
        {
            return "severity=" + string.Join("/", rule.severities.ToArray()) + ", category=" + string.Join("/", rule.categories.ToArray());
        }

        private static string MakeSourceLocation(string projectRoot, string file, string text, int offset)
        {
            var line = 1;
            for (var i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
            return file.Substring(projectRoot.Length + 1).Replace('\\', '/') + ":" + line;
        }

        private static string FindContainingMethodName(string text, int offset)
        {
            var prefix = text.Substring(0, Math.Min(offset, text.Length));
            var matches = Regex.Matches(prefix, "(?:private|public|internal|protected)\\s+(?:static\\s+)?(?:[A-Za-z_][A-Za-z0-9_<> ,\\[\\]]*)\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*\\(");
            return matches.Count == 0 ? string.Empty : matches[matches.Count - 1].Groups["name"].Value;
        }

        private static void AddDistinct(List<string> values, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value)) values.Add(value);
        }

        private static string EnsureBaselineDirectory()
        {
            var fullPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, BaselineAssetDirectory).Replace("/", Path.DirectorySeparatorChar.ToString());
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        private static void WriteJson<T>(string path, T value)
        {
            File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
        }

        private static T ReadJson<T>(string path) where T : class
        {
            return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
        }

        private static T TryReadJson<T>(string path, out string error) where T : class
        {
            error = null;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (value == null)
                {
                    error = "无法读取 " + Path.GetFileName(path) + "。";
                }
                return value;
            }
            catch (Exception exception)
            {
                error = "无法读取 " + Path.GetFileName(path) + "：" + exception.Message;
                return null;
            }
        }

        private static string NormalizeText(string value)
        {
            // 文本规范化只消除跨平台空白差异；不要在这里翻译、删减诊断或抹平具有教学意义的内容变化。
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value, "\\s+", " ").Trim();
        }

        /// <summary>TemplateStaticSnapshots.json 的根基线。schemaVersion 随文件写出，但当前 VerifyAgainstBaseline 未显式拒绝旧 TemplateStatic schemaVersion；generatedAtUtc 与 unityVersion 当前不是差异比较项。</summary>
        [Serializable] private sealed class BaselineBundle { public int schemaVersion; public string generatedAtUtc; public string unityVersion; public int templateCount; public List<TemplateSnapshot> templates = new List<TemplateSnapshot>(); }
        /// <summary>单张模板的静态快照，组合模板事实、Analyzer/Validation 采集结果与报告快照；不是运行时 Workspace 或模板 JSON。</summary>
        [Serializable] private sealed class TemplateSnapshot { public string templateId; public string templateDisplayName; public string category; public int componentCount; public int wireCount; public bool analyzerReturned; public bool hasComplexLoop; public int errorCount; public int warningCount; public List<NameCount> definitionCounts = new List<NameCount>(); public List<SemanticComponentSnapshot> components = new List<SemanticComponentSnapshot>(); public AnalysisSnapshot analysis; public List<RuleIssueSnapshot> validationIssues = new List<RuleIssueSnapshot>(); public InspectorReportSnapshot checkReport; public InspectorReportSnapshot explainReport; }
        /// <summary>按 ComponentDefinition.name 归一化后的数量记录，供静态基线比较；name 的关联含义由 CaptureTemplate 与比较器共同约定。</summary>
        [Serializable] private sealed class NameCount { public string name; public int count; }
        /// <summary>由 CircuitStateResult 归一化得到的元件语义记录。definitionId 与 ordinal 仅用于本次快照内区分同类元件，不是模板实例 ID。</summary>
        [Serializable] private sealed class SemanticComponentSnapshot { public string definitionId; public int ordinal; public string state; public string judgement; public bool isBreaker; public bool breakerInputHasSupply; public bool breakerOutputHasSupply; public bool isContactor; public bool contactorCoilEnergized; public bool contactorMainClosed; public bool isTimerRelay; public bool timerCoilEnergized; public bool timerDelayElapsed; public bool timerNoClosed; public bool timerNcClosed; public string timerDelayStatus; public bool isLimitSwitch; public bool limitSwitchTriggered; public bool isMotor; public bool isStarDeltaMotor; public string starDeltaMode; public string motorFeederContactors; }
        /// <summary>CircuitStateAnalyzer 的摘要采集值，用于漂移检测；本类型不重新分析电路，也不证明模板电气正确性。</summary>
        [Serializable] private sealed class AnalysisSnapshot { public bool available; public bool hasShortCircuit; public bool hasPowerConflict; public bool hasContactorInterlockConflict; public bool hasLimitSwitches; public bool hasTimerRelays; public bool hasStarDeltaMotors; public bool hasThreePhaseCircuit; public bool unsupportedThreePhaseTopology; public int analyzerErrorCount; public int analyzerWarningCount; }
        /// <summary>从 Validation 报告采集并序列化的 RuleId、Severity 与 Category。当前 CompareTemplate 只比较 ruleId + severity；category 保留为诊断字段，模型自身不执行规则。</summary>
        [Serializable] private sealed class RuleIssueSnapshot { public string ruleId; public string severity; public string category; }
        /// <summary>检查或解释入口的报告快照，同时保存 UI Block 摘要与结构化 modelBlocks；不是 InspectionReportData，也不生成报告。</summary>
        [Serializable] private sealed class InspectorReportSnapshot { public string entryPoint; public bool available; public InspectorReportSourceSnapshot sources; public List<InspectorBlockSnapshot> blocks = new List<InspectorBlockSnapshot>(); public List<InspectorModelBlockSnapshot> modelBlocks; }
        /// <summary>报告来源链路的计数与 RuleId 摘要。这些字段会写入报告来源快照，当前主要用于取证和诊断；现有 CompareInspectorReportModel 未将 sources 纳入基线差异比较。</summary>
        [Serializable] private sealed class InspectorReportSourceSnapshot { public string checkPipeline; public int pipelineErrorCount; public int pipelineWarningCount; public List<string> pipelineIssueCodes = new List<string>(); public int analyzerErrorCount; public int analyzerWarningCount; public int validationErrorCount; public int validationWarningCount; public List<string> validationRuleIds = new List<string>(); }
        /// <summary>格式化 UI Block 的采集字段，保存标题、类型、Severity、RuleId、关键短语和运行态/参数段落标记。当前基线只比较 title + blockType 及其顺序，其余字段为诊断字段。</summary>
        [Serializable] private sealed class InspectorBlockSnapshot { public string title; public string blockType; public string severity; public List<string> ruleIds = new List<string>(); public List<string> keyPhrases = new List<string>(); public bool containsRuntimeParagraph; public bool containsParameterParagraph; }
        /// <summary>结构化报告 Block 的最小快照。当前模型 Block 比较会比较 sectionTitle、kind、severity、ruleIds，且列表顺序参与比较；本类型不参与 UI 渲染。</summary>
        [Serializable] private sealed class InspectorModelBlockSnapshot { public string sectionTitle; public string kind; public string severity; public List<string> ruleIds = new List<string>(); }
        /// <summary>InspectorReportSnapshots.json 的根 DTO。当前读取端明确要求 schemaVersion >= 2，并对 templates、check/explain 与 modelBlocks 提供缺失保护；修改前需同步复核已提交基线。</summary>
        [Serializable] private sealed class InspectorBundle { public int schemaVersion; public List<InspectorTemplateSnapshot> templates; }
        /// <summary>按 templateId 关联一张模板的检查与解释报告快照；templateId 由生成路径提供，类型自身不验证存在性。</summary>
        [Serializable] private sealed class InspectorTemplateSnapshot { public string templateId; public InspectorReportSnapshot check; public InspectorReportSnapshot explain; }
        /// <summary>ValidationRuleSnapshots.json 的根 DTO，schemaVersion 随文件写出。当前验证路径未显式检查 RuleCatalogSnapshot.schemaVersion；规则比较检查 RuleId、Severity 与 Category，sourceLocations 不参与规则契约比较。</summary>
        [Serializable] private sealed class RuleCatalogSnapshot { public int schemaVersion; public List<RuleSeveritySnapshot> rules = new List<RuleSeveritySnapshot>(); }
        /// <summary>单条规则的扫描摘要。sourceLocations 是生成器推导的诊断信息；RuleId、Severity 与 Category 的变更仍需结合真实规则与基线复核。</summary>
        [Serializable] private sealed class RuleSeveritySnapshot { public string ruleId; public List<string> severities = new List<string>(); public List<string> categories = new List<string>(); public List<string> sourceLocations = new List<string>(); }
    }
}
#endif
