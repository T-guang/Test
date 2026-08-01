using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Practice.Netlist;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 工业模板识别的回归入口。它通过正式模板生成、Practice 网表检查、识别服务和检查助手工作流验证
    /// 直接 Wire 不同但电气节点等价的自由接线；CSV 仅供审计，所有契约都由失败关闭的断言保护。
    /// </summary>
    public static class CircuitEquivalentRecognitionTests
    {
        private static readonly string[] IndustrialTemplateIds =
        {
            "motor_jog_control",
            "motor_jog_continuous",
            "motor_self_hold_control",
            "motor_thermal_protection",
            "motor_forward_reverse_control",
            "motor_forward_reverse_interlock",
            "motor_forward_reverse_double_interlock",
            "motor_auto_reciprocating_control",
            "motor_sequential_start_timer",
            "motor_star_delta_start"
        };

        private static readonly string[] FamilyControlIds =
        {
            "single_lamp_template",
            "double_control_lamp_template"
        };

        private sealed class Dsu
        {
            private readonly Dictionary<string, string> parent = new Dictionary<string, string>();

            public void Add(string value)
            {
                if (!parent.ContainsKey(value)) parent[value] = value;
            }

            public string Find(string value)
            {
                Add(value);
                if (parent[value] != value) parent[value] = Find(parent[value]);
                return parent[value];
            }

            public void Join(string a, string b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) parent[b] = a;
            }

            public IReadOnlyList<IReadOnlyList<string>> Groups()
            {
                // Find 会进行路径压缩；先冻结键集合，避免在 GroupBy 枚举期间修改 Dictionary。
                return parent.Keys.ToList()
                    .GroupBy(Find)
                    .Select(group => (IReadOnlyList<string>)group.OrderBy(value => value, StringComparer.Ordinal).ToList())
                    .OrderBy(group => group[0], StringComparer.Ordinal)
                    .ToList();
            }
        }

        private sealed class Pair
        {
            public string A;
            public string B;

            public Pair Clone()
            {
                return new Pair { A = A, B = B };
            }

            public string Key => string.CompareOrdinal(A, B) <= 0 ? A + "<->" + B : B + "<->" + A;
        }

        [MenuItem("Tools/Tests/Run Electrical Equivalent Recognition Tests")]
        public static void Run()
        {
            var output = Environment.GetEnvironmentVariable("E1_TEST_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetTempPath(), "E1_EquivalentRecognition");
            Directory.CreateDirectory(output);

            var failures = new List<string>();
            var variantRows = new List<string>
            {
                "templateId,category,variantBuilt,wireListChanged,nodeGroupsEqual,instanceIdsChanged,recognitionStatus,matchedTemplateId,practicePassed,prefilterCandidateCount,equivalentCandidateCount,equivalentCheckCount,elapsedMilliseconds,detail"
            };
            var originalRows = new List<string>
            {
                "templateId,category,recognitionStatus,matchedTemplateId,practicePassed,prefilterCandidateCount,equivalentCandidateCount,equivalentCheckCount,elapsedMilliseconds,reason"
            };
            var negativeRows = new List<string> { "caseId,expected,actual,passed,detail" };
            var workflowEvidence = new List<string>();
            GameObject inspectorHarness = null;

            try
            {
                // 仅用于验证必要依赖缺失时 batchmode 必须失败；默认路径始终加载正式 Demo 场景。
                if (string.Equals(Environment.GetEnvironmentVariable("E1_TEST_FORCE_MISSING_DEPENDENCY"), "1", StringComparison.Ordinal))
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                else
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity", OpenSceneMode.Single);
                }
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                if (workspace == null || saveLoad == null)
                {
                    if (workspace == null) failures.Add("必要依赖缺失：未找到 WorkspaceController，无法执行等价识别回归。");
                    if (saveLoad == null) failures.Add("必要依赖缺失：未找到 SaveLoadService，无法生成模板回归图纸。");
                }
                else
                {
                    var catalogAsset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
                    var catalog = catalogAsset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(catalogAsset.text) : null;
                    if (catalog == null || catalog.templates == null)
                    {
                        failures.Add("必要依赖缺失：未能读取模板 catalog，无法执行模板识别矩阵。");
                    }
                    else
                    {
                        var inspector = UnityEngine.Object.FindObjectOfType<LocalInspectorPanel>(true);
                        if (inspector == null)
                        {
                            // Demo 场景可能尚未实例化检查助手；临时宿主仍调用正式 Create/Initialize 和工作流，不复制展示逻辑。
                            inspectorHarness = new GameObject("E1InspectorHarness", typeof(RectTransform));
                            inspector = LocalInspectorPanel.Create(inspectorHarness.GetComponent<RectTransform>(), workspace);
                        }

                        if (inspector == null)
                        {
                            failures.Add("必要依赖缺失：未能建立 LocalInspectorPanel 正式入口，无法验证检查助手报告。");
                        }
                        else
                        {
                            RunIndustrialEquivalentVariants(workspace, saveLoad, catalog, inspector, variantRows, workflowEvidence, failures);
                            RunOriginalTemplateMatrix(workspace, saveLoad, catalog, originalRows, failures);
                            RunNegativeChecks(workspace, saveLoad, catalog, negativeRows, failures);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                failures.Add("测试执行出现未处理异常：" + exception);
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "E1_EQUIVALENT_VARIANT_MATRIX.csv"), variantRows, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "E1_ORIGINAL_18_MATRIX.csv"), originalRows, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "E1_NEGATIVE_MATRIX.csv"), negativeRows, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "E1_INSPECTION_WORKFLOW.txt"), workflowEvidence, System.Text.Encoding.UTF8);
                if (inspectorHarness != null) UnityEngine.Object.DestroyImmediate(inspectorHarness);
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("电气等价模板识别回归失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][E1.1] 工业电气等价模板识别：通过");
        }

        private static void RunIndustrialEquivalentVariants(
            WorkspaceController workspace,
            SaveLoadService saveLoad,
            CircuitTemplateCatalogDto catalog,
            LocalInspectorPanel inspector,
            ICollection<string> rows,
            ICollection<string> workflowEvidence,
            ICollection<string> failures)
        {
            var workflowChecked = false;
            foreach (var templateId in IndustrialTemplateIds)
            {
                var item = catalog.templates.FirstOrDefault(candidate => candidate.templateId == templateId);
                Require(item != null, "目录缺少工业模板：" + templateId, failures);
                CircuitTemplateDto standard = null;
                string loadError = null;
                if (item == null || !CircuitTemplateLoader.TryLoad(item.resourcePath, out standard, out loadError))
                {
                    failures.Add("读取工业模板失败：" + templateId + "，" + loadError);
                    continue;
                }

                var built = TryBuildVariantForAmbiguityProbe(standard, out var variant, out var detail);
                Require(built, templateId + " 未能构造至少一个三端电气节点的等价生成树变体：" + detail, failures);
                if (!built)
                {
                    rows.Add(CsvRow(templateId, item.category, "false", "false", "false", "false", "NotRun", string.Empty, "false", "0", "0", "0", "0", detail));
                    continue;
                }

                var normalizedStandardWires = NormalizedDirectWireSet(standard);
                var normalizedVariantWires = NormalizedDirectWireSet(variant);
                var wireChanged = !normalizedStandardWires.SetEquals(normalizedVariantWires);
                var standardNodes = NormalizedNodeGroups(standard);
                var variantNodes = NormalizedNodeGroups(variant);
                var groupsEqual = string.Equals(standardNodes, variantNodes, StringComparison.Ordinal);
                var instanceIdsChanged = variant.components.All(component => component.instanceId.StartsWith("student-", StringComparison.Ordinal)) &&
                    !variant.components.Select(component => component.instanceId).Intersect(standard.components.Select(component => component.instanceId), StringComparer.Ordinal).Any();

                Require(wireChanged, templateId + " 变体的直接 Wire 集合没有变化。", failures);
                Require(groupsEqual, templateId + " 变体的电气节点组发生变化。", failures);
                Require(instanceIdsChanged, templateId + " 变体没有使用全新的 InstanceId。", failures);

                workspace.ClearDrawing(false);
                TemplateEditSession.Clear();
                var spawned = CircuitTemplateSpawnService.Spawn(variant, workspace, saveLoad.Catalog, out var spawnMessage);
                Require(spawned, templateId + " 等价变体生成失败：" + spawnMessage, failures);
                if (!spawned)
                {
                    rows.Add(CsvRow(templateId, item.category, "true", wireChanged.ToString(), groupsEqual.ToString(), instanceIdsChanged.ToString(), "SpawnFailed", string.Empty, "false", "0", "0", "0", "0", spawnMessage));
                    continue;
                }

                TemplateEditSession.Clear();
                var recognition = new CircuitTopologyRecognitionService().Recognize(workspace);
                var practice = PracticeConnectionChecker.Check(workspace, standard);
                Require(recognition.Status == CircuitRecognitionStatus.EquivalentMatch, templateId + " 应为 EquivalentMatch，实际为 " + recognition.Status + "。", failures);
                Require(recognition.MatchedTemplateId == templateId, templateId + " 匹配到错误模板：" + recognition.MatchedTemplateId, failures);
                Require(practice.Passed, templateId + " PracticeConnectionChecker 未通过。", failures);
                Require(recognition.EquivalentCandidateCount == 1, templateId + " 等价候选数应为 1，实际为 " + recognition.EquivalentCandidateCount, failures);
                Require(recognition.EquivalentCheckCount >= 1, templateId + " 未执行等价检查。", failures);

                if (inspector != null)
                {
                    workflowChecked = true;
                    var workflow = new InspectionWorkflowService(workspace, inspector, false).CreateCheckReport();
                    var reportText = workflow.Report == null ? string.Empty : string.Join("\n", workflow.Report.Blocks.Select(block => block.ToLegacyText()));
                    Require(workflow.Succeeded, "正式 InspectionWorkflowService 返回失败：" + workflow.UserMessage, failures);
                    Require(reportText.Contains(item.templateName), "检查助手报告没有模板名称：" + item.templateName, failures);
                    Require(reportText.Contains("匹配方式：电气节点等价"), "检查助手报告没有电气节点等价文案。", failures);
                    workflowEvidence.Add("templateId=" + templateId);
                    workflowEvidence.Add("workflowSucceeded=" + workflow.Succeeded);
                    workflowEvidence.Add("status=" + workflow.StatusMessage);
                    workflowEvidence.Add(reportText);
                }

                rows.Add(CsvRow(templateId, item.category, "true", wireChanged.ToString(), groupsEqual.ToString(), instanceIdsChanged.ToString(), recognition.Status.ToString(), recognition.MatchedTemplateId, practice.Passed.ToString(), recognition.PrefilterCandidateCount.ToString(), recognition.EquivalentCandidateCount.ToString(), recognition.EquivalentCheckCount.ToString(), recognition.ElapsedMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), detail));
                File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("E1_TEST_OUTPUT") ?? Path.GetTempPath(), templateId + "_standard_wires.txt"), string.Join("\n", standard.wires.Select(FormatWire)) + "\n", System.Text.Encoding.UTF8);
                File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("E1_TEST_OUTPUT") ?? Path.GetTempPath(), templateId + "_variant_wires.txt"), string.Join("\n", variant.wires.Select(FormatWire)) + "\n", System.Text.Encoding.UTF8);
            }

            Require(workflowChecked, "没有通过正式 InspectionWorkflowService 验证等价识别展示。", failures);
        }

        private static void RunOriginalTemplateMatrix(WorkspaceController workspace, SaveLoadService saveLoad, CircuitTemplateCatalogDto catalog, ICollection<string> rows, ICollection<string> failures)
        {
            Require(catalog.templates.Count == 18, "template_catalog 期望 18 张模板，实际为 " + catalog.templates.Count, failures);
            foreach (var item in catalog.templates)
            {
                if (!CircuitTemplateLoader.TryLoad(item.resourcePath, out var original, out var error))
                {
                    failures.Add("读取原模板失败：" + item.templateId + "，" + error);
                    continue;
                }

                workspace.ClearDrawing(false);
                TemplateEditSession.Clear();
                var spawned = CircuitTemplateSpawnService.Spawn(original, workspace, saveLoad.Catalog, out var message);
                Require(spawned, item.templateId + " 原模板生成失败：" + message, failures);
                if (!spawned) continue;

                TemplateEditSession.Clear();
                var recognition = new CircuitTopologyRecognitionService().Recognize(workspace);
                var practice = PracticeConnectionChecker.Check(workspace, original);
                Require(recognition.Status == CircuitRecognitionStatus.ExactMatch, item.templateId + " 原模板应为 ExactMatch，实际为 " + recognition.Status, failures);
                Require(recognition.MatchedTemplateId == item.templateId, item.templateId + " 原模板识别结果错误：" + recognition.MatchedTemplateId, failures);
                Require(practice.Passed, item.templateId + " 原模板 PracticeConnectionChecker 未通过。", failures);
                Require(recognition.EquivalentCheckCount == 0, item.templateId + " 原模板不应进入等价检查。", failures);
                rows.Add(CsvRow(item.templateId, item.category, recognition.Status.ToString(), recognition.MatchedTemplateId, practice.Passed.ToString(), recognition.PrefilterCandidateCount.ToString(), recognition.EquivalentCandidateCount.ToString(), recognition.EquivalentCheckCount.ToString(), recognition.ElapsedMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture), recognition.Reason));
            }

            foreach (var familyId in FamilyControlIds)
            {
                Require(rows.Any(row => row.Contains(Csv(familyId))), "家庭控制组未进入 18 张原模板矩阵：" + familyId, failures);
            }
            Require(rows.Count == 19, "18 张原模板矩阵行数不正确。", failures);
        }

        private static void RunNegativeChecks(WorkspaceController workspace, SaveLoadService saveLoad, CircuitTemplateCatalogDto catalog, ICollection<string> rows, ICollection<string> failures)
        {
            var forwardReverse = LoadTemplate(catalog, "motor_forward_reverse_control", failures);
            if (forwardReverse != null)
            {
                var missingComponent = Clone(forwardReverse);
                missingComponent.components.RemoveAt(0);
                CheckDtoNegative(workspace, saveLoad, missingComponent, "missing-component", "NoMatch-or-RejectedBeforeRecognition", rows, failures, true);

                var extraComponent = Clone(forwardReverse);
                var extra = CloneComponent(extraComponent.components[0]);
                extra.instanceId = "extra-negative";
                extraComponent.components.Add(extra);
                CheckDtoNegative(workspace, saveLoad, extraComponent, "extra-component", "NoMatch", rows, failures, false);

                var missingWire = Clone(forwardReverse);
                missingWire.wires.RemoveAt(0);
                CheckDtoNegative(workspace, saveLoad, missingWire, "missing-node-connection", "NoMatch", rows, failures, false);

                var mergedNodes = Clone(forwardReverse);
                MergeTwoIndependentNodes(mergedNodes);
                CheckDtoNegative(workspace, saveLoad, mergedNodes, "merged-independent-nodes", "NoMatch", rows, failures, false);

                var phaseMiswire = Clone(forwardReverse);
                Require(SwapMotorPhaseTerminals(phaseMiswire, "Motor_ThreePhase_380V"), "无法构造正反转相序错接负向用例。", failures);
                CheckDtoNegative(workspace, saveLoad, phaseMiswire, "wrong-contactor-or-phase-terminal", "NoMatch", rows, failures, false);

                CheckExtraEffectiveWire(workspace, saveLoad, forwardReverse, rows, failures);
            }

            var starDelta = LoadTemplate(catalog, "motor_star_delta_start", failures);
            if (starDelta != null)
            {
                var starDeltaMiswire = Clone(starDelta);
                Require(SwapStarDeltaPhaseTerminals(starDeltaMiswire), "无法构造星三角端子错接负向用例。", failures);
                CheckDtoNegative(workspace, saveLoad, starDeltaMiswire, "star-delta-terminal-miswire", "NoMatch", rows, failures, false);
            }
        }

        private static void CheckExtraEffectiveWire(WorkspaceController workspace, SaveLoadService saveLoad, CircuitTemplateDto source, ICollection<string> rows, ICollection<string> failures)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            var spawned = CircuitTemplateSpawnService.Spawn(source, workspace, saveLoad.Catalog, out var message);
            Require(spawned, "额外有效 Wire 用例的标准模板生成失败：" + message, failures);
            if (!spawned) return;

            var nodes = new Dsu();
            foreach (var wire in workspace.WireManager.Wires)
            {
                nodes.Join(TerminalKey(wire.StartTerminal), TerminalKey(wire.EndTerminal));
            }

            TerminalView selectedStart = null;
            TerminalView selectedEnd = null;
            var terminals = workspace.Components.SelectMany(component => component.Terminals).Where(terminal => terminal != null).ToList();
            for (var i = 0; i < terminals.Count && selectedStart == null; i++)
            {
                for (var j = i + 1; j < terminals.Count; j++)
                {
                    if (terminals[i].Owner == terminals[j].Owner || nodes.Find(TerminalKey(terminals[i])) == nodes.Find(TerminalKey(terminals[j]))) continue;
                    if (!workspace.WireManager.CanCreateWire(terminals[i], terminals[j], out _)) continue;
                    selectedStart = terminals[i];
                    selectedEnd = terminals[j];
                    break;
                }
            }

            Require(selectedStart != null && selectedEnd != null, "未找到可通过 CanCreateWire 且连接不同电气节点的额外 Wire。", failures);
            if (selectedStart == null || selectedEnd == null) return;
            var countBefore = workspace.WireManager.Wires.Count;
            var extraWire = workspace.WireManager.CreateWire(selectedStart, selectedEnd, Color.magenta, workspace.CurrentWireStyle);
            var inserted = extraWire != null && workspace.WireManager.Wires.Count == countBefore + 1;
            Require(inserted, "额外有效 Wire 没有实际进入 Workspace。", failures);
            TemplateEditSession.Clear();
            var recognition = new CircuitTopologyRecognitionService().Recognize(workspace);
            var passed = inserted && recognition.Status == CircuitRecognitionStatus.NoMatch;
            AddNegativeRow(rows, "extra-effective-wire", "NoMatch", recognition.Status.ToString(), passed, selectedStart.Owner.InstanceId + "." + selectedStart.TerminalId + " -> " + selectedEnd.Owner.InstanceId + "." + selectedEnd.TerminalId);
            Require(passed, "额外有效 Wire 应为 NoMatch，实际为 " + recognition.Status, failures);
        }

        private static void CheckDtoNegative(WorkspaceController workspace, SaveLoadService saveLoad, CircuitTemplateDto candidate, string caseId, string expected, ICollection<string> rows, ICollection<string> failures, bool allowPreflightRejection)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            if (!CircuitTemplateSpawnService.Spawn(candidate, workspace, saveLoad.Catalog, out var spawnMessage))
            {
                var allowed = allowPreflightRejection;
                AddNegativeRow(rows, caseId, expected, "RejectedBeforeRecognition", allowed, spawnMessage);
                Require(allowed, caseId + " 被模板预检拒绝，不能替代识别器 NoMatch 用例：" + spawnMessage, failures);
                return;
            }

            TemplateEditSession.Clear();
            var result = new CircuitTopologyRecognitionService().Recognize(workspace);
            var passed = result.Status == CircuitRecognitionStatus.NoMatch;
            AddNegativeRow(rows, caseId, expected, result.Status.ToString(), passed, result.Reason);
            Require(passed, caseId + " 期望 NoMatch，实际为 " + result.Status + "。", failures);
        }

        private static CircuitTemplateDto LoadTemplate(CircuitTemplateCatalogDto catalog, string templateId, ICollection<string> failures)
        {
            var item = catalog.templates.FirstOrDefault(candidate => candidate.templateId == templateId);
            CircuitTemplateDto template = null;
            string error = null;
            if (item == null || !CircuitTemplateLoader.TryLoad(item.resourcePath, out template, out error))
            {
                failures.Add("读取模板失败：" + templateId + "，" + error);
                return null;
            }

            return template;
        }

        private static void MergeTwoIndependentNodes(CircuitTemplateDto template)
        {
            var groups = BuildNodeGroups(template);
            var first = groups[0][0].Split('|');
            var second = groups.Skip(1).First()[0].Split('|');
            var wire = template.wires.First(item =>
                item.startComponentId == second[0] && item.startTerminalId == second[1] ||
                item.endComponentId == second[0] && item.endTerminalId == second[1]);
            if (wire.startComponentId == second[0] && wire.startTerminalId == second[1])
            {
                wire.startComponentId = first[0];
                wire.startTerminalId = first[1];
            }
            else
            {
                wire.endComponentId = first[0];
                wire.endTerminalId = first[1];
            }
        }

        private static bool SwapMotorPhaseTerminals(CircuitTemplateDto template, string definitionName)
        {
            var motor = template.components.FirstOrDefault(component => component.definitionName == definitionName);
            if (motor == null) return false;
            return SwapTerminalReferences(template.wires, motor.instanceId, new[] { "U", "V", "W" });
        }

        private static bool SwapStarDeltaPhaseTerminals(CircuitTemplateDto template)
        {
            var motor = template.components.FirstOrDefault(component => component.definitionName == "Motor_StarDelta_380V");
            if (motor == null) return false;
            return SwapTerminalReferences(template.wires, motor.instanceId, new[] { "U1", "V1", "W1" }) ||
                SwapTerminalReferences(template.wires, motor.instanceId, new[] { "U2", "V2", "W2" });
        }

        private static bool SwapTerminalReferences(IList<TemplateWireDto> wires, string componentId, IReadOnlyList<string> terminalIds)
        {
            var candidates = wires.Where(wire => wire.startComponentId == componentId && terminalIds.Contains(wire.startTerminalId) || wire.endComponentId == componentId && terminalIds.Contains(wire.endTerminalId)).ToList();
            if (candidates.Count < 2) return false;
            var first = candidates[0];
            var second = candidates[1];
            var firstUsesStart = first.startComponentId == componentId && terminalIds.Contains(first.startTerminalId);
            var secondUsesStart = second.startComponentId == componentId && terminalIds.Contains(second.startTerminalId);
            var firstTerminal = firstUsesStart ? first.startTerminalId : first.endTerminalId;
            var secondTerminal = secondUsesStart ? second.startTerminalId : second.endTerminalId;
            if (firstUsesStart) first.startTerminalId = secondTerminal; else first.endTerminalId = secondTerminal;
            if (secondUsesStart) second.startTerminalId = firstTerminal; else second.endTerminalId = firstTerminal;
            return true;
        }

        internal static bool TryBuildVariantForAmbiguityProbe(CircuitTemplateDto source, out CircuitTemplateDto variant, out string detail)
        {
            variant = Clone(source);
            detail = string.Empty;
            var componentMap = new Dictionary<string, string>();
            for (var i = 0; i < variant.components.Count; i++)
            {
                var old = variant.components[i].instanceId;
                var next = "student-" + (i + 1).ToString("D3") + "-" + old;
                componentMap[old] = next;
                variant.components[i].instanceId = next;
            }
            variant.components.Reverse();

            var pairs = source.wires.Select(wire => new Pair { A = wire.startComponentId + "|" + wire.startTerminalId, B = wire.endComponentId + "|" + wire.endTerminalId }).ToList();
            var sourceGroups = BuildGroups(pairs);
            Pair replacement = null;
            Pair removed = null;
            foreach (var group in sourceGroups.Where(group => group.Count >= 3))
            {
                var existing = new HashSet<string>(pairs.Select(pair => pair.Key));
                var candidates = group.SelectMany((a, index) => group.Skip(index + 1).Select(b => new Pair { A = a, B = b }))
                    .Where(pair => Owner(pair.A) != Owner(pair.B) && !existing.Contains(pair.Key)).ToList();
                foreach (var candidate in candidates)
                {
                    foreach (var old in pairs)
                    {
                        var trial = pairs.Select(pair => pair.Clone()).ToList();
                        trial.Remove(old);
                        trial.Add(candidate);
                        if (BuildGroups(trial).SequenceEqual(sourceGroups, new StringListComparer()))
                        {
                            replacement = candidate;
                            removed = old;
                            break;
                        }
                    }
                    if (replacement != null) break;
                }
                if (replacement != null) break;
            }

            if (replacement == null)
            {
                detail = "没有包含至少三个端子的节点可替换合法跨元件生成树边。";
                return false;
            }

            var replacementIndex = source.wires.FindIndex(wire => MatchesPair(wire, removed));
            for (var i = 0; i < variant.wires.Count; i++)
            {
                var wire = variant.wires[i];
                var oldStart = i == replacementIndex ? replacement.A : source.wires[i].startComponentId + "|" + source.wires[i].startTerminalId;
                var oldEnd = i == replacementIndex ? replacement.B : source.wires[i].endComponentId + "|" + source.wires[i].endTerminalId;
                AssignEndpoint(wire, true, componentMap[SplitOwner(oldStart)], SplitTerminal(oldStart));
                AssignEndpoint(wire, false, componentMap[SplitOwner(oldEnd)], SplitTerminal(oldEnd));
            }

            variant.wires.Reverse();
            for (var i = 0; i < variant.wires.Count; i += 3)
            {
                var wire = variant.wires[i];
                var component = wire.startComponentId;
                var terminal = wire.startTerminalId;
                wire.startComponentId = wire.endComponentId;
                wire.startTerminalId = wire.endTerminalId;
                wire.endComponentId = component;
                wire.endTerminalId = terminal;
            }
            detail = "替换 " + removed.Key + " 为 " + replacement.Key;
            return true;
        }

        private sealed class StringListComparer : IEqualityComparer<IReadOnlyList<string>>
        {
            public bool Equals(IReadOnlyList<string> x, IReadOnlyList<string> y)
            {
                return ReferenceEquals(x, y) || (x != null && y != null && x.SequenceEqual(y, StringComparer.Ordinal));
            }

            public int GetHashCode(IReadOnlyList<string> value)
            {
                return value == null ? 0 : value.Aggregate(17, (hash, item) => hash * 31 + (item ?? string.Empty).GetHashCode());
            }
        }

        private static CircuitTemplateDto Clone(CircuitTemplateDto source)
        {
            return JsonUtility.FromJson<CircuitTemplateDto>(JsonUtility.ToJson(source));
        }

        private static TemplateComponentDto CloneComponent(TemplateComponentDto source)
        {
            return JsonUtility.FromJson<TemplateComponentDto>(JsonUtility.ToJson(source));
        }

        private static IReadOnlyList<IReadOnlyList<string>> BuildNodeGroups(CircuitTemplateDto template)
        {
            return BuildGroups(template.wires.Select(wire => new Pair { A = wire.startComponentId + "|" + wire.startTerminalId, B = wire.endComponentId + "|" + wire.endTerminalId }));
        }

        private static IReadOnlyList<IReadOnlyList<string>> BuildGroups(IEnumerable<Pair> pairs)
        {
            var dsu = new Dsu();
            foreach (var pair in pairs) dsu.Join(pair.A, pair.B);
            return dsu.Groups();
        }

        private static string NormalizedNodeGroups(CircuitTemplateDto template)
        {
            return string.Join(";", BuildNodeGroups(template)
                .Select(group => "[" + string.Join(" ", group.Select(NormalizeKey).OrderBy(value => value, StringComparer.Ordinal)) + "]")
                .OrderBy(group => group, StringComparer.Ordinal));
        }

        private static HashSet<string> NormalizedDirectWireSet(CircuitTemplateDto template)
        {
            return new HashSet<string>(template.wires.Select(wire =>
            {
                var start = NormalizeKey(wire.startComponentId + "|" + wire.startTerminalId);
                var end = NormalizeKey(wire.endComponentId + "|" + wire.endTerminalId);
                return string.CompareOrdinal(start, end) <= 0 ? start + "<->" + end : end + "<->" + start;
            }));
        }

        private static string NormalizeKey(string key)
        {
            var separator = key.IndexOf('|');
            return NormalizeInstanceId(key.Substring(0, separator)) + key.Substring(separator);
        }

        private static string NormalizeInstanceId(string instanceId)
        {
            return Regex.Replace(instanceId ?? string.Empty, "^student-\\d{3}-", string.Empty);
        }

        private static string TerminalKey(TerminalView terminal)
        {
            return terminal.Owner.InstanceId + "|" + terminal.TerminalId;
        }

        private static bool MatchesPair(TemplateWireDto wire, Pair pair)
        {
            var start = wire.startComponentId + "|" + wire.startTerminalId;
            var end = wire.endComponentId + "|" + wire.endTerminalId;
            return (start == pair.A && end == pair.B) || (start == pair.B && end == pair.A);
        }

        private static void AssignEndpoint(TemplateWireDto wire, bool start, string componentId, string terminalId)
        {
            if (start)
            {
                wire.startComponentId = componentId;
                wire.startTerminalId = terminalId;
            }
            else
            {
                wire.endComponentId = componentId;
                wire.endTerminalId = terminalId;
            }
        }

        private static string Owner(string key) => SplitOwner(key);
        private static string SplitOwner(string key) => key.Substring(0, key.IndexOf('|'));
        private static string SplitTerminal(string key) => key.Substring(key.IndexOf('|') + 1);

        private static void AddNegativeRow(ICollection<string> rows, string caseId, string expected, string actual, bool passed, string detail)
        {
            rows.Add(CsvRow(caseId, expected, actual, passed.ToString(), detail));
        }

        private static void Require(bool condition, string message, ICollection<string> failures)
        {
            if (!condition) failures.Add(message);
        }

        private static string FormatWire(TemplateWireDto wire)
        {
            return wire.startComponentId + "." + wire.startTerminalId + " -> " + wire.endComponentId + "." + wire.endTerminalId;
        }

        private static string CsvRow(params string[] values)
        {
            return string.Join(",", values.Select(Csv));
        }

        private static string Csv(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }
    }
}
