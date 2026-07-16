#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools.Testing
{
    /// <summary>
    /// 从 Catalog、模板 JSON、静态/报告快照与当前规则快照生成运行和负向测试资料。
    /// 本工具只读取项目事实并写入 Docs/TestManual；未被快照或代码直接证明的人工操作顺序会明确标为需项目负责人确认。
    /// </summary>
    // 维护边界：菜单会读取 Catalog、模板与三类既有快照，写入 Docs/TestManual 的 03/04/05 文档并调用 AssetDatabase.Refresh。
    // 它不要求当前场景或 Play Mode，不改写模板、Definition、快照或 Player 数据。快照只用于既有行为的证据映射，
    // 不是运行时数据源；无法由代码或快照证实的步骤必须保留为负责人确认，不能按通用电工知识补全端点。
    // CSV/Markdown 字段顺序与 UTF-8 写入属于测试资料兼容约束，异常由 Console 报告；生成前后应人工审查文档差异。
    public static class GenerateRuntimeValidationTestManualData
    {
        private const string CatalogPath = "Assets/Resources/Blueprints/Templates/template_catalog.json";
        private const string DefinitionFolder = "Assets/Data";
        private const string OutputRoot = "Docs/TestManual";
        private const string StaticSnapshotPath = "Assets/EditorTests/Baselines/V2.3.9.1/TemplateStaticSnapshots.json";
        private const string InspectorSnapshotPath = "Assets/EditorTests/Baselines/V2.3.9.1/InspectorReportSnapshots.json";
        private const string ValidationSnapshotPath = "Assets/EditorTests/Baselines/V2.3.9.1/ValidationRuleSnapshots.json";

        [MenuItem("Tools/ElectricalSim/Testing/Generate Runtime And Validation Documents")]
        public static void Generate()
        {
            // 仅显式菜单调用会改写 Docs/TestManual；不会接受或重写任何基线快照。
            try
            {
                var data = Collect();
                WriteRuntimeDocuments(data);
                WriteNegativeDocuments(data);
                WriteEvidenceMapping(data);
                AssetDatabase.Refresh();
                var message = "[RuntimeTestManual] 生成完成：模板=" + data.Templates.Count + "，运行步骤=" + data.RuntimeRows.Count + "，规则=" + data.Rules.Count + "，错误=" + data.Errors.Count + "，待确认=" + data.OwnerConfirmations.Count;
                if (data.Errors.Count > 0) Debug.LogError(message);
                else if (data.OwnerConfirmations.Count > 0) Debug.LogWarning(message);
                else Debug.Log(message);
            }
            catch (Exception exception)
            {
                Debug.LogError("[RuntimeTestManual] 生成失败：" + exception);
            }
        }

        private static DataSet Collect()
        {
            var data = new DataSet
            {
                Branch = Git("branch --show-current"),
                Commit = Git("rev-parse HEAD")
            };
            LoadDefinitions(data);
            var catalog = ReadJson<CircuitTemplateCatalogDto>(CatalogPath, data, "Template Catalog");
            var staticSnapshots = ReadJson<StaticSnapshotRoot>(StaticSnapshotPath, data, "TemplateStaticSnapshots");
            var inspectorSnapshots = ReadJson<InspectorSnapshotRoot>(InspectorSnapshotPath, data, "InspectorReportSnapshots");
            var ruleSnapshots = ReadJson<ValidationSnapshotRoot>(ValidationSnapshotPath, data, "ValidationRuleSnapshots");
            if (catalog == null || catalog.templates == null) return data;

            var staticById = (staticSnapshots?.templates ?? new List<StaticTemplateSnapshot>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.templateId))
                .ToDictionary(x => x.templateId, x => x, StringComparer.OrdinalIgnoreCase);
            var inspectorById = (inspectorSnapshots?.templates ?? new List<InspectorTemplateSnapshot>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.templateId))
                .ToDictionary(x => x.templateId, x => x, StringComparer.OrdinalIgnoreCase);

            foreach (var item in catalog.templates.Where(x => x != null).OrderBy(x => x.sortOrder).ThenBy(x => x.templateId, StringComparer.Ordinal))
            {
                var path = "Assets/Resources/" + item.resourcePath + ".json";
                var template = ReadJson<CircuitTemplateDto>(path, data, item.templateId);
                if (template == null) continue;
                if (!staticById.TryGetValue(item.templateId, out var snapshot))
                    data.Errors.Add(item.templateId + " 缺少 TemplateStaticSnapshots 条目。");
                if (!inspectorById.TryGetValue(item.templateId, out var inspector))
                    data.Errors.Add(item.templateId + " 缺少 InspectorReportSnapshots 条目。");
                var model = new TemplateModel { Item = item, Template = template, Snapshot = snapshot, Inspector = inspector, JsonPath = path };
                ValidateTemplate(model, data);
                data.Templates.Add(model);
                AddRuntimeRows(model, data);
            }

            if (data.Templates.Count != 18) data.Errors.Add("可读取模板数量不是 18：" + data.Templates.Count);
            if (data.Templates.Count(x => x.Item.category == "家庭电路") != 8 || data.Templates.Count(x => x.Item.category == "工业电路") != 10)
                data.Errors.Add("模板分类数量不是家庭 8 / 工业 10。");

            foreach (var rule in ruleSnapshots?.rules ?? new List<RuleSnapshot>())
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.ruleId)) continue;
                data.Rules.Add(rule);
            }
            if (data.Rules.Count == 0) data.Errors.Add("ValidationRuleSnapshots 未提供 RuleId。");
            AddNegativeCases(data);
            return data;
        }

        private static void LoadDefinitions(DataSet data)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinition", new[] { DefinitionFolder }).OrderBy(x => x, StringComparer.Ordinal))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null || string.IsNullOrWhiteSpace(definition.name)) continue;
                if (data.Definitions.ContainsKey(definition.name)) data.Errors.Add("ComponentDefinition 重名：" + definition.name);
                else data.Definitions.Add(definition.name, definition);
            }
        }

        private static T ReadJson<T>(string path, DataSet data, string label) where T : class
        {
            if (!File.Exists(path))
            {
                data.Errors.Add(label + " 不存在：" + path);
                return null;
            }
            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                data.Errors.Add(label + " 解析失败：" + exception.Message);
                return null;
            }
        }

        private static void ValidateTemplate(TemplateModel model, DataSet data)
        {
            var instanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var component in model.Template.components ?? new List<TemplateComponentDto>())
            {
                if (component == null || string.IsNullOrWhiteSpace(component.instanceId))
                {
                    data.Errors.Add(model.Item.templateId + " 存在空 instanceId。");
                    continue;
                }
                if (!instanceIds.Add(component.instanceId)) data.Errors.Add(model.Item.templateId + " instanceId 重复：" + component.instanceId);
                if (string.IsNullOrWhiteSpace(component.definitionName) || !data.Definitions.ContainsKey(component.definitionName))
                    data.Errors.Add(model.Item.templateId + " 缺失 Definition：" + component.instanceId + " -> " + component.definitionName);
            }
            foreach (var wire in model.Template.wires ?? new List<TemplateWireDto>())
            {
                ValidateWireEndpoint(model, data, wire, wire?.startComponentId, wire?.startTerminalId);
                ValidateWireEndpoint(model, data, wire, wire?.endComponentId, wire?.endTerminalId);
            }
        }

        private static void ValidateWireEndpoint(TemplateModel model, DataSet data, TemplateWireDto wire, string instanceId, string terminalId)
        {
            if (wire == null || string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(terminalId))
            {
                data.Errors.Add(model.Item.templateId + " 存在空导线端点。");
                return;
            }
            var component = (model.Template.components ?? new List<TemplateComponentDto>()).FirstOrDefault(x => x != null && string.Equals(x.instanceId, instanceId, StringComparison.OrdinalIgnoreCase));
            if (component == null || !data.Definitions.TryGetValue(component.definitionName, out var definition))
            {
                data.Errors.Add(model.Item.templateId + " 导线引用不存在实例：" + instanceId);
                return;
            }
            if (!(definition.terminals ?? new List<TerminalDefinition>()).Any(x => x != null && string.Equals(x.id, terminalId, StringComparison.OrdinalIgnoreCase)))
                data.Errors.Add(model.Item.templateId + " 导线引用不存在端子：" + instanceId + "." + terminalId);
        }

        private static void AddRuntimeRows(TemplateModel model, DataSet data)
        {
            var baseline = BuildBaseline(model);
            data.RuntimeRows.Add(new RuntimeRow
            {
                Id = model.Item.templateId + "-R-001",
                Template = model,
                Step = "加载模板后点击“开始仿真”，再点击“检查当前电路”。",
                ObjectId = "Workspace",
                Operation = "点击",
                ExpectedKm = baseline.Km,
                ExpectedKt = baseline.Kt,
                ExpectedSq = baseline.Sq,
                ExpectedMotor = baseline.Motor,
                ExpectedLoad = baseline.Load,
                ExpectedMotion = baseline.Motion,
                ExpectedParameters = baseline.Parameters,
                ExpectedCheck = baseline.Check,
                Evidence = "TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。",
                NeedsOwner = false
            });

            foreach (var component in model.Template.components ?? new List<TemplateComponentDto>())
            {
                if (component == null || !data.Definitions.TryGetValue(component.definitionName, out var definition) || !definition.togglable) continue;
                var momentary = IsMomentaryButton(definition.name);
                data.RuntimeRows.Add(new RuntimeRow
                {
                    Id = model.Item.templateId + "-R-" + (data.RuntimeRows.Count(x => x.Template == model) + 1).ToString("000", CultureInfo.InvariantCulture),
                    Template = model,
                    Step = momentary
                        ? "在仿真运行中按下并保持该瞬时按钮，观察后释放；每次状态变化后由当前实现刷新仿真。"
                        : "双击该可切换元件的操作区域切换状态，再执行一次仿真刷新；完成后恢复模板初始状态。",
                    ObjectId = component.instanceId,
                    DefinitionName = component.definitionName,
                    Operation = momentary ? "按下 / 释放" : "切换",
                    ExpectedCheck = "局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。",
                    Evidence = momentary ? "CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。" : "CircuitComponent.OnPointerClick/Toggle。",
                    NeedsOwner = true
                });
            }

            foreach (var timer in (model.Template.components ?? new List<TemplateComponentDto>()).Where(x => x != null && x.definitionName.IndexOf("Timer_OnDelay", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var seconds = ResolveParameter(timer, "delaySeconds", 3f);
                data.RuntimeRows.Add(new RuntimeRow
                {
                    Id = model.Item.templateId + "-R-" + (data.RuntimeRows.Count(x => x.Template == model) + 1).ToString("000", CultureInfo.InvariantCulture),
                    Template = model,
                    Step = "仅在线圈已经得电的前提下保持仿真运行，等待 delaySeconds 指定时长。",
                    ObjectId = timer.instanceId,
                    DefinitionName = timer.definitionName,
                    Operation = "等待",
                    WaitSeconds = seconds,
                    ExpectedKt = "条件确定：线圈得电且等待不足 " + Number(seconds) + "s 时为 Timing；达到 " + Number(seconds) + "s 时为 Elapsed，15/18 闭合、15/16 断开；线圈失电即 Reset。",
                    Evidence = "SimulationEngine.UpdateOnDelayTimerRuntimeState / AddInternalConnections；模板参数 delaySeconds。",
                    NeedsOwner = true
                });
            }

            if (model.Item.templateId == "motor_auto_reciprocating_control")
            {
                data.RuntimeRows.Add(new RuntimeRow
                {
                    Id = model.Item.templateId + "-R-" + (data.RuntimeRows.Count(x => x.Template == model) + 1).ToString("000", CultureInfo.InvariantCulture),
                    Template = model,
                    Step = "仅在 motor_1 已运行时持续观察运行态，直到位置到达边界或方向切换。",
                    ObjectId = "motor_1 / sq_left / sq_right",
                    DefinitionName = "Motor_ThreePhase_380V / LimitSwitch_Compound",
                    Operation = "等待",
                    WaitSeconds = 5f,
                    ExpectedMotion = "条件确定：Position 初始 50，范围 0..100，速度 20；正向增加、反向减少，到边界分别触发 sq_left/sq_right 的虚拟限位。实际从哪个按钮进入正反向需项目负责人确认。",
                    Evidence = "RuntimeStateManager.MotionRuntimeState；SimulationEngine.UpdateAutoReciprocatingMotionDirection / IsVirtualLimitSwitchTriggered。",
                    NeedsOwner = true
                });
            }

            data.RuntimeRows.Add(new RuntimeRow
            {
                Id = model.Item.templateId + "-R-" + (data.RuntimeRows.Count(x => x.Template == model) + 1).ToString("000", CultureInfo.InvariantCulture),
                Template = model,
                Step = "点击“停止仿真”，随后重新加载同一模板。",
                ObjectId = "Workspace",
                Operation = "复位",
                ExpectedKt = "TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。",
                ExpectedMotion = "MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。",
                ExpectedCheck = "停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。",
                Evidence = "WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。",
                NeedsOwner = false
            });
        }

        private static Baseline BuildBaseline(TemplateModel model)
        {
            var baseline = new Baseline
            {
                Check = "结构化 Validation 基线：Error=" + (model.Snapshot?.errorCount ?? -1) + "，Warning=" + (model.Snapshot?.warningCount ?? -1) + "；RuleId=" + Join(model.Inspector?.check?.sources?.validationRuleIds) + "。",
                Parameters = model.Inspector?.check != null && model.Inspector.check.blocks != null && model.Inspector.check.blocks.Any(x => x != null && x.containsParameterParagraph)
                    ? "Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。"
                    : "快照未提供可复核的参数估算数值。"
            };
            foreach (var component in model.Snapshot?.components ?? new List<StaticComponentSnapshot>())
            {
                if (component == null) continue;
                var state = component.definitionId + "#" + component.ordinal + "=" + component.state;
                if (component.isContactor) baseline.Km = Append(baseline.Km, state + "，线圈=" + Bool(component.contactorCoilEnergized) + "，主触点=" + Bool(component.contactorMainClosed));
                if (component.isTimerRelay) baseline.Kt = Append(baseline.Kt, state + "，" + component.timerDelayStatus + "，延时到达=" + Bool(component.timerDelayElapsed));
                if (component.isLimitSwitch) baseline.Sq = Append(baseline.Sq, state + "，触发=" + Bool(component.limitSwitchTriggered));
                if (component.isMotor) baseline.Motor = Append(baseline.Motor, state + "，星三角=" + component.starDeltaMode);
                if (component.definitionId.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0 || component.definitionId.IndexOf("Fan", StringComparison.OrdinalIgnoreCase) >= 0)
                    baseline.Load = Append(baseline.Load, state);
            }
            if (string.IsNullOrWhiteSpace(baseline.Km)) baseline.Km = "模板默认状态未包含 KM。";
            if (string.IsNullOrWhiteSpace(baseline.Kt)) baseline.Kt = "模板默认状态未包含 KT。";
            if (string.IsNullOrWhiteSpace(baseline.Sq)) baseline.Sq = "模板默认状态未包含 SQ。";
            if (string.IsNullOrWhiteSpace(baseline.Motor)) baseline.Motor = "模板默认状态未包含电机。";
            if (string.IsNullOrWhiteSpace(baseline.Load)) baseline.Load = "模板默认状态未包含灯泡或风扇。";
            baseline.Motion = model.Item.templateId == "motor_auto_reciprocating_control" ? "默认运行态由 Reset 创建时 Position=50、Direction=Stopped。" : "不适用。";
            return baseline;
        }

        private static void AddNegativeCases(DataSet data)
        {
            AddRuleCase(data, "POWER_POTENTIAL_CONFLICT", "N/A（Editor 测试工厂）", "增加导线", "AC_220V_Power.power 的 L → 同一 power 的 N。", "Assets/Scripts/Editor/PowerSafetyValidationTests.cs:TestPowerConflictSinglePhaseLn", false);
            AddRuleCase(data, "LIVE_TO_PE_FAULT", "N/A（Editor 测试工厂）", "增加导线", "AC_220V_Power.power 的 L → 同一 power 的 PE。", "Assets/Scripts/Editor/PowerSafetyValidationTests.cs:TestLiveToPe", false);
            AddRuleCase(data, "NEUTRAL_PE_MISUSE", "N/A（Editor 测试工厂）", "增加导线", "AC_220V_Power.power 的 N → 同一 power 的 PE。", "Assets/Scripts/Editor/PowerSafetyValidationTests.cs:TestNeutralPeMisuse", false);
            AddRuleCase(data, "COIL_VOLTAGE_MISMATCH", "N/A（Editor 测试工厂）", "改变端点", "AC_ThreePhase_Power.power L1 → Contactor_KM_220V.coil A1；L2 → A2。", "Assets/Scripts/Editor/PowerSafetyValidationTests.cs:TestCoilMismatch", false);
            AddRuleCase(data, "BREAKER_OR_FUSE_BYPASSED", "需项目负责人确认", "旁路", "现有测试工厂可触发，但未提供可唯一映射到 18 张标准模板的安全人工扰动端点。", "Assets/Scripts/Editor/ProtectionBypassValidationTests.cs", true);
            AddRuleCase(data, "MOTOR_CONTACTOR_BYPASSED", "需项目负责人确认", "旁路", "现有测试工厂使用三相电源直接接入电机 U/V/W；标准模板中应删除/旁路哪些既有线需负责人确认。", "Assets/Scripts/Editor/ProtectionBypassValidationTests.cs:TestMotorBypassedDirectSupply", true);
            AddRuleCase(data, "REVERSING_INTERLOCK_MISSING", "需项目负责人确认", "删除导线", "规则存在；当前快照和历史资料没有提供唯一可复用的标准模板删除线编号。", "Assets/Scripts/Core/Validation/ProtectionBypassValidationHelper.cs；ValidationRuleSnapshots.json", true);
            AddRuleCase(data, "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED", "需项目负责人确认", "旁路", "规则存在；现有测试使用工厂夹具，未提供可唯一映射到热继模板的删除/新增端点组合。", "Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs；ValidationRuleSnapshots.json", true);
            AddRuleCase(data, "THERMAL_RELAY_CONTROL_BYPASSED", "需项目负责人确认", "旁路", "工厂夹具证据：KM A2 → AC_ThreePhase_Power N 会绕开 FR 95/96；在正式模板中的完整操作需负责人确认。", "Assets/Scripts/Editor/TopologySafetyTests.cs:Test_ThermalRelayBypassed", true);
            AddRuleCase(data, "TIMER_CONTROL_BYPASSED", "需项目负责人确认", "旁路", "工厂夹具证据：KT 未到时追加 AC_220V_Power L → km_delta A1；标准星三角模板扰动前提需负责人确认。", "Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs:TestTimerBypassedStarDeltaEarlyDelta", true);
            AddRuleCase(data, "STOP_BUTTON_BYPASSED", "motor_self_hold_control", "旁路", "历史人工用例：停止按钮断开后，控制电源 L1 → KM A1 的直接跨接。模板中对应 stop/KM 实例与原导线编号需负责人确认。", "Docs/V2.3.6.4_NegativePerturbationTestSet.md:SB-01；TopologySafetyTests.cs:Test_StopButtonBypassed", true);
            AddRuleCase(data, "SELF_HOLDING_BRANCH_INCOMPLETE", "motor_self_hold_control", "删除导线", "保留 KM 13 或 14 的一侧，删除另一侧自锁线；历史资料未冻结具体 W-xxx，需负责人确认。", "Docs/V2.3.6.4_NegativePerturbationTestSet.md:SH-01；TopologySafetyTests.cs:Test_SelfHoldingIncomplete", true);
            AddRuleCase(data, "REVERSING_CONTACTOR_CONFLICT", "motor_forward_reverse_control", "旁路", "历史人工用例：正转运行后，控制电源 L1 直接跨接反转 KM A1；模板的方向 KM 实例与启动前提需负责人确认。", "Docs/V2.3.6.4_NegativePerturbationTestSet.md:RV-01；TopologySafetyTests.cs:Test_ReversingConflict", true);
            AddStarDeltaCase(data, "STAR_DELTA_PARTIAL_STARPOINT_SHORT", new[] { "U2", "V2" }, "SD-S-01");
            AddStarDeltaCase(data, "STAR_DELTA_PARTIAL_STARPOINT_SHORT", new[] { "V2", "W2" }, "SD-S-02");
            AddStarDeltaCase(data, "STAR_DELTA_PARTIAL_STARPOINT_SHORT", new[] { "U2", "W2" }, "SD-S-03");
            AddStarDeltaCase(data, "STAR_DELTA_INPUT_TERMINAL_SHORT", new[] { "U1", "V1" }, "SD-I-01");
            AddStarDeltaCase(data, "STAR_DELTA_INPUT_TERMINAL_SHORT", new[] { "V1", "W1" }, "SD-I-02");
            AddStarDeltaCase(data, "STAR_DELTA_INPUT_TERMINAL_SHORT", new[] { "U1", "W1" }, "SD-I-03");
            AddRuleCase(data, "COMPLEX_LOOP_OR_UNSUPPORTED_TOPOLOGY", "N/A（Editor 测试工厂）", "增加导线", "当前自动测试会临时降低 TopologyTraversalLimits 后构造 30 节点链；正式产品阈值下没有稳定手工端点。", "Assets/Scripts/Editor/TopologySafetyTests.cs:Test_ComplexTopologyExceeded", true);
        }

        private static void AddRuleCase(DataSet data, string ruleId, string templateId, string modification, string steps, string evidence, bool needsOwner)
        {
            var rule = data.Rules.FirstOrDefault(x => string.Equals(x.ruleId, ruleId, StringComparison.Ordinal));
            if (rule == null)
            {
                data.Errors.Add("负向资料引用的 RuleId 不存在于快照：" + ruleId);
                return;
            }
            data.NegativeRows.Add(new NegativeRow
            {
                Id = "NEG-" + (data.NegativeRows.Count + 1).ToString("000", CultureInfo.InvariantCulture),
                Rule = rule,
                TemplateId = templateId,
                Modification = modification,
                Steps = steps,
                Evidence = evidence,
                NeedsOwner = needsOwner,
                StopsNormalRun = "未由当前规则快照统一声明；需项目负责人确认。",
                StopsParameters = ruleId.StartsWith("STAR_DELTA_", StringComparison.Ordinal) ? "是：历史负向资料要求不输出正常星三角电流估算。" : "需项目负责人确认。",
                AllowsAdditionalIssues = ruleId == "THERMAL_RELAY_CONTROL_BYPASSED" ? "是：历史资料记录可叠加 MOTOR_MISSING_PHASE。" : "需项目负责人确认。"
            });
            if (needsOwner) data.OwnerConfirmations.Add("负向用例 " + ruleId + " 缺少唯一的标准模板人工扰动端点。");
        }

        private static void AddStarDeltaCase(DataSet data, string ruleId, string[] terminals, string historicalId)
        {
            var template = data.Templates.FirstOrDefault(x => string.Equals(x.Item.templateId, "motor_star_delta_start", StringComparison.Ordinal));
            var motor = template?.Template.components?.FirstOrDefault(x => x != null && string.Equals(x.definitionName, "Motor_StarDelta_380V", StringComparison.Ordinal));
            AddRuleCase(data, ruleId, template == null ? "需项目负责人确认" : template.Item.templateId, "增加导线", "在 " + (motor?.instanceId ?? "需确认电机实例") + " 的 " + terminals[0] + " → " + terminals[1] + " 增加一条直接导线；不删除既有导线。历史用例=" + historicalId + "。", "Docs/V2.3.6.4_NegativePerturbationTestSet.md:" + historicalId + "；CircuitValidationService 星三角静态直接导线检查。", motor == null);
        }

        private static void WriteRuntimeDocuments(DataSet data)
        {
            Directory.CreateDirectory(OutputRoot);
            var markdown = new StringBuilder();
            markdown.AppendLine("# 18 张模板标准运行操作用例");
            markdown.AppendLine();
            markdown.AppendLine("- Git 分支：`" + Escape(data.Branch) + "`");
            markdown.AppendLine("- Git 提交：`" + Escape(data.Commit) + "`");
            markdown.AppendLine("- 证据规则：只有模板 JSON、现有快照或运行代码直接证明的结果写为确定；其余标记“需项目负责人确认”。");
            markdown.AppendLine();
            foreach (var template in data.Templates)
            {
                markdown.AppendLine("## " + Escape(template.Item.templateName) + "（" + Escape(template.Item.templateId) + "）");
                markdown.AppendLine();
                markdown.AppendLine("- 分类：" + Escape(template.Item.category));
                markdown.AppendLine("- 模板 JSON：`" + Escape(template.JsonPath) + "`");
                markdown.AppendLine("- 默认元件数/导线数：" + (template.Template.components?.Count ?? 0) + " / " + (template.Template.wires?.Count ?? 0));
                markdown.AppendLine();
                markdown.AppendLine("| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |");
                markdown.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
                foreach (var row in data.RuntimeRows.Where(x => x.Template == template))
                    markdown.AppendLine("| " + Escape(row.Id) + " | " + Escape(row.ObjectId) + " | " + Escape(row.DefinitionName) + " | " + Escape(row.Operation) + " | " + (row.WaitSeconds > 0 ? Number(row.WaitSeconds) + "s" : "-") + " | " + Escape(RuntimeExpectation(row)) + " | " + Escape(row.ExpectedCheck) + " | " + Escape(row.Evidence) + " | " + (row.NeedsOwner ? "是" : "否") + " |");
                markdown.AppendLine();
            }
            WriteUtf8(Path.Combine(OutputRoot, "03_TemplateRuntimeTestCases.md"), markdown.ToString());

            var csv = new StringBuilder();
            csv.AppendLine(Csv("模板 ID", "模板名称", "分类", "步骤编号", "前置状态", "操作步骤", "操作对象实例 ID", "DefinitionName", "操作端或按钮含义", "操作类型", "等待时间", "预期 KM 状态", "预期 KT 状态", "预期 SQ 状态", "预期电机状态", "预期灯泡或风扇状态", "预期运动方向或位置", "预期参数估算", "预期检查助手结果", "数据或代码证据来源", "是否需项目负责人确认"));
            foreach (var row in data.RuntimeRows)
                csv.AppendLine(Csv(row.Template.Item.templateId, row.Template.Item.templateName, row.Template.Item.category, row.Id, "模板 JSON 默认 isClosed；运行态在 StartSimulation 后建立。", row.Step, row.ObjectId, row.DefinitionName, "见操作步骤", row.Operation, row.WaitSeconds > 0 ? Number(row.WaitSeconds) + "s" : string.Empty, row.ExpectedKm, row.ExpectedKt, row.ExpectedSq, row.ExpectedMotor, row.ExpectedLoad, row.ExpectedMotion, row.ExpectedParameters, row.ExpectedCheck, row.Evidence, row.NeedsOwner ? "是" : "否"));
            WriteUtf8(Path.Combine(OutputRoot, "03_TemplateRuntimeTestCases.csv"), csv.ToString());
        }

        private static void WriteNegativeDocuments(DataSet data)
        {
            var markdown = new StringBuilder();
            markdown.AppendLine("# 错误接线与规则检测用例");
            markdown.AppendLine();
            markdown.AppendLine("本文件不把 Editor 测试工厂的临时实例误写成 18 张标准模板数据。没有唯一人工端点的规则已明确标记为需项目负责人确认。");
            markdown.AppendLine();
            markdown.AppendLine("| 用例 ID | 模板 ID | RuleId | Severity | 分类 | 修改类型 | 精确操作步骤 | 是否阻止正常运行 | 参数估算 | 允许叠加 | 证据 | 需确认 |");
            markdown.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var row in data.NegativeRows)
                markdown.AppendLine("| " + Escape(row.Id) + " | " + Escape(row.TemplateId) + " | `" + Escape(row.Rule.ruleId) + "` | " + Escape(Join(row.Rule.severities)) + " | " + Escape(Join(row.Rule.categories)) + " | " + Escape(row.Modification) + " | " + Escape(row.Steps) + " | " + Escape(row.StopsNormalRun) + " | " + Escape(row.StopsParameters) + " | " + Escape(row.AllowsAdditionalIssues) + " | " + Escape(row.Evidence) + " | " + (row.NeedsOwner ? "是" : "否") + " |");
            WriteUtf8(Path.Combine(OutputRoot, "04_NegativeValidationTestCases.md"), markdown.ToString());

            var csv = new StringBuilder();
            csv.AppendLine(Csv("用例 ID", "模板 ID", "模板名称", "规则 ID", "严重等级", "规则分类", "初始模板", "修改类型", "涉及实例 ID", "DefinitionName", "起点 terminalId", "终点 terminalId", "原导线编号", "精确操作步骤", "预期检查结果", "是否应阻止正常运行", "是否应停止正常参数估算", "是否允许叠加其他错误", "证据来源", "是否需项目负责人确认"));
            foreach (var row in data.NegativeRows)
                csv.AppendLine(Csv(row.Id, row.TemplateId, TemplateName(data, row.TemplateId), row.Rule.ruleId, Join(row.Rule.severities), Join(row.Rule.categories), row.TemplateId, row.Modification, NegativeInstances(row), NegativeDefinitions(row), NegativeStartTerminals(row), NegativeEndTerminals(row), row.NeedsOwner ? "需项目负责人确认" : "不适用（新增直接导线）", row.Steps, "出现 " + row.Rule.ruleId + "。", row.StopsNormalRun, row.StopsParameters, row.AllowsAdditionalIssues, row.Evidence, row.NeedsOwner ? "是" : "否"));
            WriteUtf8(Path.Combine(OutputRoot, "04_NegativeValidationTestCases.csv"), csv.ToString());
        }

        private static void WriteEvidenceMapping(DataSet data)
        {
            var markdown = new StringBuilder();
            markdown.AppendLine("# 测试证据映射");
            markdown.AppendLine();
            markdown.AppendLine("## 模板运行证据");
            markdown.AppendLine();
            markdown.AppendLine("| 模板 ID | 数据来源 | 代码类 / 方法 | 快照 | 结论 | 是否推导 | 是否需负责人确认 |");
            markdown.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var template in data.Templates)
                markdown.AppendLine("| " + Escape(template.Item.templateId) + " | `" + Escape(template.JsonPath) + "` | `WorkspaceController.StartSimulation`；`CircuitComponent` 交互；按需 `SimulationEngine` | `TemplateStaticSnapshots.json`；`InspectorReportSnapshots.json` | 默认状态与检查报告结构已固定；人工操作序列仅在代码直接覆盖时确定。 | 是（操作别名） | " + (data.RuntimeRows.Any(x => x.Template == template && x.NeedsOwner) ? "是" : "否") + " |");
            markdown.AppendLine();
            markdown.AppendLine("## 规则证据");
            markdown.AppendLine();
            markdown.AppendLine("| RuleId | 数据来源 | 代码类 / 方法 | 快照名称 | 结论 | 是否推导 | 是否需负责人确认 |");
            markdown.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var rule in data.Rules.OrderBy(x => x.ruleId, StringComparer.Ordinal))
            {
                var rows = data.NegativeRows.Where(x => x.Rule == rule).ToList();
                markdown.AppendLine("| `" + Escape(rule.ruleId) + "` | `ValidationRuleSnapshots.json` | " + Escape(Join(rule.sourceLocations)) + " | `ValidationRuleSnapshots.json` | Severity=" + Escape(Join(rule.severities)) + "，Category=" + Escape(Join(rule.categories)) + "。 | 否 | " + (rows.Any(x => x.NeedsOwner) ? "是" : "否") + " |");
            }
            markdown.AppendLine();
            markdown.AppendLine("## 负责人确认项");
            markdown.AppendLine();
            foreach (var item in data.OwnerConfirmations.Distinct(StringComparer.Ordinal)) markdown.AppendLine("- " + Escape(item));
            WriteUtf8(Path.Combine(OutputRoot, "05_TestEvidenceMapping.md"), markdown.ToString());
        }

        private static bool IsMomentaryButton(string definitionName)
        {
            return !string.IsNullOrWhiteSpace(definitionName) &&
                (definitionName.IndexOf("Button_Start", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 definitionName.IndexOf("Button_Stop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 definitionName.IndexOf("Button_Compound", StringComparison.OrdinalIgnoreCase) >= 0) &&
                definitionName.IndexOf("SelfLock", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static float ResolveParameter(TemplateComponentDto component, string key, float fallback)
        {
            var parameter = (component.parameters ?? new List<ComponentParameter>()).FirstOrDefault(x => x != null && string.Equals(x.key, key, StringComparison.OrdinalIgnoreCase));
            return parameter == null ? fallback : parameter.value;
        }

        private static string RuntimeExpectation(RuntimeRow row)
        {
            return JoinNonEmpty(row.ExpectedKm, row.ExpectedKt, row.ExpectedSq, row.ExpectedMotor, row.ExpectedLoad, row.ExpectedMotion, row.ExpectedParameters);
        }

        private static string TemplateName(DataSet data, string id)
        {
            return data.Templates.FirstOrDefault(x => string.Equals(x.Item.templateId, id, StringComparison.OrdinalIgnoreCase))?.Item.templateName ?? string.Empty;
        }

        private static string NegativeInstances(NegativeRow row)
        {
            if (row.Rule.ruleId.StartsWith("STAR_DELTA_", StringComparison.Ordinal)) return "motor_star_delta";
            if (row.Rule.ruleId == "COIL_VOLTAGE_MISMATCH") return "power；coil";
            if (row.Rule.ruleId == "POWER_POTENTIAL_CONFLICT" || row.Rule.ruleId == "LIVE_TO_PE_FAULT" || row.Rule.ruleId == "NEUTRAL_PE_MISUSE") return "power";
            return "需项目负责人确认";
        }

        private static string NegativeDefinitions(NegativeRow row)
        {
            if (row.Rule.ruleId.StartsWith("STAR_DELTA_", StringComparison.Ordinal)) return "Motor_StarDelta_380V";
            if (row.Rule.ruleId == "COIL_VOLTAGE_MISMATCH") return "AC_ThreePhase_Power；Contactor_KM_220V";
            if (row.Rule.ruleId == "POWER_POTENTIAL_CONFLICT" || row.Rule.ruleId == "LIVE_TO_PE_FAULT" || row.Rule.ruleId == "NEUTRAL_PE_MISUSE") return "AC_220V_Power";
            return "需项目负责人确认";
        }

        private static string NegativeStartTerminals(NegativeRow row)
        {
            if (row.Rule.ruleId == "POWER_POTENTIAL_CONFLICT") return "L";
            if (row.Rule.ruleId == "LIVE_TO_PE_FAULT") return "L";
            if (row.Rule.ruleId == "NEUTRAL_PE_MISUSE") return "N";
            if (row.Rule.ruleId == "COIL_VOLTAGE_MISMATCH") return "L1；L2";
            if (row.Rule.ruleId == "STAR_DELTA_PARTIAL_STARPOINT_SHORT") return row.Steps.Contains("U2 → V2") || row.Steps.Contains("U2 → W2") ? "U2" : "V2";
            if (row.Rule.ruleId == "STAR_DELTA_INPUT_TERMINAL_SHORT") return row.Steps.Contains("U1 → V1") || row.Steps.Contains("U1 → W1") ? "U1" : "V1";
            return "需项目负责人确认";
        }

        private static string NegativeEndTerminals(NegativeRow row)
        {
            if (row.Rule.ruleId == "POWER_POTENTIAL_CONFLICT") return "N";
            if (row.Rule.ruleId == "LIVE_TO_PE_FAULT" || row.Rule.ruleId == "NEUTRAL_PE_MISUSE") return "PE";
            if (row.Rule.ruleId == "COIL_VOLTAGE_MISMATCH") return "A1；A2";
            if (row.Rule.ruleId == "STAR_DELTA_PARTIAL_STARPOINT_SHORT") return row.Steps.Contains("U2 → V2") ? "V2" : "W2";
            if (row.Rule.ruleId == "STAR_DELTA_INPUT_TERMINAL_SHORT") return row.Steps.Contains("U1 → V1") ? "V1" : "W1";
            return "需项目负责人确认";
        }

        private static string Append(string existing, string addition) => string.IsNullOrWhiteSpace(existing) ? addition : existing + "；" + addition;
        private static string Bool(bool value) => value ? "是" : "否";
        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Join(IEnumerable<string> values) => values == null ? "" : string.Join("；", values.Where(x => !string.IsNullOrWhiteSpace(x)));
        private static string JoinNonEmpty(params string[] values) => string.Join("；", values.Where(x => !string.IsNullOrWhiteSpace(x)));
        private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        private static string Csv(params string[] values) => string.Join(",", values.Select(x => "\"" + (x ?? string.Empty).Replace("\"", "\"\"") + "\""));
        private static void WriteUtf8(string path, string content) => File.WriteAllText(path, content, new UTF8Encoding(true));

        private static string Git(string arguments)
        {
            try
            {
                var start = new System.Diagnostics.ProcessStartInfo("git", arguments) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(start)) return process == null ? "" : process.StandardOutput.ReadToEnd().Trim();
            }
            catch { return ""; }
        }

        private sealed class DataSet
        {
            public string Branch;
            public string Commit;
            public readonly Dictionary<string, ComponentDefinition> Definitions = new Dictionary<string, ComponentDefinition>(StringComparer.OrdinalIgnoreCase);
            public List<TemplateModel> Templates = new List<TemplateModel>();
            public readonly List<RuntimeRow> RuntimeRows = new List<RuntimeRow>();
            public readonly List<RuleSnapshot> Rules = new List<RuleSnapshot>();
            public readonly List<NegativeRow> NegativeRows = new List<NegativeRow>();
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> OwnerConfirmations = new List<string>();
        }

        private sealed class TemplateModel { public CircuitTemplateCatalogItemDto Item; public CircuitTemplateDto Template; public StaticTemplateSnapshot Snapshot; public InspectorTemplateSnapshot Inspector; public string JsonPath; }
        private sealed class RuntimeRow { public string Id; public TemplateModel Template; public string Step; public string ObjectId; public string DefinitionName; public string Operation; public float WaitSeconds; public string ExpectedKm; public string ExpectedKt; public string ExpectedSq; public string ExpectedMotor; public string ExpectedLoad; public string ExpectedMotion; public string ExpectedParameters; public string ExpectedCheck; public string Evidence; public bool NeedsOwner; }
        private sealed class NegativeRow { public string Id; public RuleSnapshot Rule; public string TemplateId; public string Modification; public string Steps; public string Evidence; public bool NeedsOwner; public string StopsNormalRun; public string StopsParameters; public string AllowsAdditionalIssues; }
        private sealed class Baseline { public string Km; public string Kt; public string Sq; public string Motor; public string Load; public string Motion; public string Parameters; public string Check; }

        // 这些字段仅由 JsonUtility 从固定快照文件填充；不要改成属性或构造赋值，否则会改变快照读取行为。
#pragma warning disable CS0649
        [Serializable] private sealed class StaticSnapshotRoot { public List<StaticTemplateSnapshot> templates = new List<StaticTemplateSnapshot>(); }
        [Serializable] private sealed class StaticTemplateSnapshot { public string templateId; public int errorCount; public int warningCount; public List<StaticComponentSnapshot> components = new List<StaticComponentSnapshot>(); }
        [Serializable] private sealed class StaticComponentSnapshot { public string definitionId; public int ordinal; public string state; public bool isContactor; public bool contactorCoilEnergized; public bool contactorMainClosed; public bool isTimerRelay; public bool timerDelayElapsed; public string timerDelayStatus; public bool isLimitSwitch; public bool limitSwitchTriggered; public bool isMotor; public string starDeltaMode; }
        [Serializable] private sealed class InspectorSnapshotRoot { public List<InspectorTemplateSnapshot> templates = new List<InspectorTemplateSnapshot>(); }
        [Serializable] private sealed class InspectorTemplateSnapshot { public string templateId; public InspectorCheckSnapshot check; }
        [Serializable] private sealed class InspectorCheckSnapshot { public InspectorSourcesSnapshot sources; public List<InspectorBlockSnapshot> blocks = new List<InspectorBlockSnapshot>(); }
        [Serializable] private sealed class InspectorSourcesSnapshot { public int validationErrorCount; public int validationWarningCount; public List<string> validationRuleIds = new List<string>(); }
        [Serializable] private sealed class InspectorBlockSnapshot { public bool containsParameterParagraph; }
        [Serializable] private sealed class ValidationSnapshotRoot { public List<RuleSnapshot> rules = new List<RuleSnapshot>(); }
        [Serializable] private sealed class RuleSnapshot { public string ruleId; public List<string> severities = new List<string>(); public List<string> categories = new List<string>(); public List<string> sourceLocations = new List<string>(); }
#pragma warning restore CS0649
    }
}
#endif
