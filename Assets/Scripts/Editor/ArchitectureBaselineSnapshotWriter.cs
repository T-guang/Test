// Editor-only regression baseline tool. It intentionally uses the production template spawn path.
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
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools
{
    public static class ArchitectureBaselineSnapshotWriter
    {
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

        private static void Run(bool writeBaseline)
        {
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
            snapshot.checkReport = CaptureInspectorReport(inspector, "CheckCurrentCircuit");
            snapshot.explainReport = CaptureInspectorReport(inspector, "ExplainCurrentCircuit");
            return snapshot;
        }

        private static List<SemanticComponentSnapshot> CaptureComponents(CircuitStateResult analysis)
        {
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

        private static InspectorReportSnapshot CaptureInspectorReport(LocalInspectorPanel inspector, string methodName)
        {
            var method = typeof(LocalInspectorPanel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            var field = typeof(LocalInspectorPanel).GetField("reportContent", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null || field == null)
            {
                throw new MissingMemberException("LocalInspectorPanel", methodName + " 或 reportContent");
            }

            method.Invoke(inspector, null);
            Canvas.ForceUpdateCanvases();
            var content = field.GetValue(inspector) as RectTransform;
            if (content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }

            var snapshot = new InspectorReportSnapshot { entryPoint = methodName, available = content != null };
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

            snapshot.errorCount = snapshot.blocks.Count(block => block.severity == "Error");
            snapshot.warningCount = snapshot.blocks.Count(block => block.severity == "Warning");
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
            var severity = new Dictionary<string, string>
            {
                { "POWER_POTENTIAL_CONFLICT", "Error" }, { "LIVE_TO_PE_FAULT", "Error" }, { "NEUTRAL_PE_MISUSE", "Error" },
                { "COIL_VOLTAGE_MISMATCH", "ErrorOrWarningByMismatchDirection" }, { "BREAKER_OR_FUSE_BYPASSED", "Error" },
                { "MOTOR_CONTACTOR_BYPASSED", "Error" }, { "REVERSING_INTERLOCK_MISSING", "Error" },
                { "THERMAL_RELAY_CONTROL_BYPASSED", "Error" }, { "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED", "Error" },
                { "TIMER_CONTROL_BYPASSED", "Error" }, { "STOP_BUTTON_BYPASSED", "Error" },
                { "SELF_HOLDING_BRANCH_INCOMPLETE", "Warning" }, { "REVERSING_CONTACTOR_CONFLICT", "Error" },
                { "STAR_DELTA_PARTIAL_STARPOINT_SHORT", "Error" }, { "STAR_DELTA_INPUT_TERMINAL_SHORT", "Error" },
                { "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY", "Warning" }
            };
            return new RuleCatalogSnapshot
            {
                schemaVersion = 1,
                rules = KnownRuleIds.Select(id => new RuleSeveritySnapshot { ruleId = id, expectedSeverity = severity[id] }).ToList()
            };
        }

        private static void WriteBaseline(BaselineBundle bundle, RuleCatalogSnapshot ruleCatalog)
        {
            var directory = EnsureBaselineDirectory();
            WriteJson(Path.Combine(directory, "TemplateStaticSnapshots.json"), bundle);
            WriteJson(Path.Combine(directory, "InspectorReportSnapshots.json"), new InspectorBundle { schemaVersion = 1, templates = bundle.templates.Select(template => new InspectorTemplateSnapshot { templateId = template.templateId, check = template.checkReport, explain = template.explainReport }).ToList() });
            WriteJson(Path.Combine(directory, "ValidationRuleSnapshots.json"), ruleCatalog);
            AssetDatabase.Refresh();
        }

        private static List<string> VerifyAgainstBaseline(BaselineBundle actual, RuleCatalogSnapshot actualRules)
        {
            var directory = EnsureBaselineDirectory();
            var expected = ReadJson<BaselineBundle>(Path.Combine(directory, "TemplateStaticSnapshots.json"));
            var expectedRules = ReadJson<RuleCatalogSnapshot>(Path.Combine(directory, "ValidationRuleSnapshots.json"));
            var differences = new List<string>();
            if (expected == null || expectedRules == null)
            {
                differences.Add("缺少基线文件。请由人工在确认当前稳定版本后执行“生成架构重构基线”。");
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
                if (current == null || current.expectedSeverity != expectedRule.expectedSeverity)
                {
                    differences.Add("Rule severity contract: " + expectedRule.ruleId + " expected=" + expectedRule.expectedSeverity + ", actual=" + (current == null ? "<missing>" : current.expectedSeverity));
                }
            }
            return differences;
        }

        private static void CompareTemplate(TemplateSnapshot expected, TemplateSnapshot actual, List<string> differences)
        {
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

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value, "\\s+", " ").Trim();
        }

        [Serializable] private sealed class BaselineBundle { public int schemaVersion; public string generatedAtUtc; public string unityVersion; public int templateCount; public List<TemplateSnapshot> templates = new List<TemplateSnapshot>(); }
        [Serializable] private sealed class TemplateSnapshot { public string templateId; public string templateDisplayName; public string category; public int componentCount; public int wireCount; public bool analyzerReturned; public bool hasComplexLoop; public int errorCount; public int warningCount; public List<NameCount> definitionCounts = new List<NameCount>(); public List<SemanticComponentSnapshot> components = new List<SemanticComponentSnapshot>(); public AnalysisSnapshot analysis; public List<RuleIssueSnapshot> validationIssues = new List<RuleIssueSnapshot>(); public InspectorReportSnapshot checkReport; public InspectorReportSnapshot explainReport; }
        [Serializable] private sealed class NameCount { public string name; public int count; }
        [Serializable] private sealed class SemanticComponentSnapshot { public string definitionId; public int ordinal; public string state; public string judgement; public bool isBreaker; public bool breakerInputHasSupply; public bool breakerOutputHasSupply; public bool isContactor; public bool contactorCoilEnergized; public bool contactorMainClosed; public bool isTimerRelay; public bool timerCoilEnergized; public bool timerDelayElapsed; public bool timerNoClosed; public bool timerNcClosed; public string timerDelayStatus; public bool isLimitSwitch; public bool limitSwitchTriggered; public bool isMotor; public bool isStarDeltaMotor; public string starDeltaMode; public string motorFeederContactors; }
        [Serializable] private sealed class AnalysisSnapshot { public bool available; public bool hasShortCircuit; public bool hasPowerConflict; public bool hasContactorInterlockConflict; public bool hasLimitSwitches; public bool hasTimerRelays; public bool hasStarDeltaMotors; public bool hasThreePhaseCircuit; public bool unsupportedThreePhaseTopology; public int analyzerErrorCount; public int analyzerWarningCount; }
        [Serializable] private sealed class RuleIssueSnapshot { public string ruleId; public string severity; public string category; }
        [Serializable] private sealed class InspectorReportSnapshot { public string entryPoint; public bool available; public int errorCount; public int warningCount; public List<InspectorBlockSnapshot> blocks = new List<InspectorBlockSnapshot>(); }
        [Serializable] private sealed class InspectorBlockSnapshot { public string title; public string blockType; public string severity; public List<string> ruleIds = new List<string>(); public List<string> keyPhrases = new List<string>(); public bool containsRuntimeParagraph; public bool containsParameterParagraph; }
        [Serializable] private sealed class InspectorBundle { public int schemaVersion; public List<InspectorTemplateSnapshot> templates = new List<InspectorTemplateSnapshot>(); }
        [Serializable] private sealed class InspectorTemplateSnapshot { public string templateId; public InspectorReportSnapshot check; public InspectorReportSnapshot explain; }
        [Serializable] private sealed class RuleCatalogSnapshot { public int schemaVersion; public List<RuleSeveritySnapshot> rules = new List<RuleSeveritySnapshot>(); }
        [Serializable] private sealed class RuleSeveritySnapshot { public string ruleId; public string expectedSeverity; }
    }
}
#endif
