#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools.Testing
{
    /// <summary>
    /// 读取标准模板 DTO，并在每次评估中构造临时 CircuitComponent、TerminalView 和 WireView；候选扰动应用于临时构建过程，不会原地修改模板 DTO 或写回模板 JSON。
    /// 不调用模板写回、保存服务或场景保存；搜索不到稳定端点时明确输出为不适合人工模板测试。
    /// </summary>
    // 维护边界：仅在 Editor 中读取真实 Catalog、模板 JSON 与 Definition，在内存 Template DTO 和临时对象上搜索候选；
    // 不写回模板、保存服务或场景。只有同一候选连续三次稳定产生目标 RuleId 才能进入人工测试资料，否则保留自动测试覆盖。
    // 实例 ID、DefinitionName、terminalId、W-xxx 与线色必须来自真实项目数据，不能用通用电工知识补全；
    // 菜单会写入 Docs/TestManual 的确认政策和搜索结果并刷新 AssetDatabase，输出格式与目标规则调整前必须审查差异。
    public static class NegativePerturbationSearchTool
    {
        private const string CatalogPath = "Assets/Resources/Blueprints/Templates/template_catalog.json";
        private const string DefinitionFolder = "Assets/Data";
        private const string OutputRoot = "Docs/TestManual";
        private static readonly string[] TargetRules =
        {
            "BREAKER_OR_FUSE_BYPASSED", "MOTOR_CONTACTOR_BYPASSED", "REVERSING_INTERLOCK_MISSING",
            "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED", "THERMAL_RELAY_CONTROL_BYPASSED", "TIMER_CONTROL_BYPASSED",
            "STOP_BUTTON_BYPASSED", "SELF_HOLDING_BRANCH_INCOMPLETE", "REVERSING_CONTACTOR_CONFLICT"
        };

        [MenuItem("Tools/ElectricalSim/Testing/Search Negative Perturbations")]
        public static void Search()
        {
            // 候选扰动始终停留在内存副本；此入口只更新测试资料，不修复或接受标准模板数据。
            try
            {
                var input = LoadInput();
                var results = SearchAll(input);
                WriteConfirmedRuntimePolicy(input);
                WriteResults(results);
                AssetDatabase.Refresh();
                Debug.Log("[NegativePerturbationSearch] 完成：成功=" + results.Count(x => x.Suitable) + "，不适合人工模板测试=" + results.Count(x => !x.Suitable));
            }
            catch (Exception exception)
            {
                Debug.LogError("[NegativePerturbationSearch] 失败：" + exception);
            }
        }

        private static Input LoadInput()
        {
            var input = new Input();
            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinition", new[] { DefinitionFolder }))
            {
                var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && !string.IsNullOrWhiteSpace(definition.name)) input.Definitions[definition.name] = definition;
            }
            var catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(File.ReadAllText(CatalogPath, Encoding.UTF8));
            foreach (var item in catalog.templates ?? new List<CircuitTemplateCatalogItemDto>())
            {
                var path = "Assets/Resources/" + item.resourcePath + ".json";
                var template = JsonUtility.FromJson<CircuitTemplateDto>(File.ReadAllText(path, Encoding.UTF8));
                input.Templates.Add(new TemplateData { Item = item, Template = template, JsonPath = path });
            }
            return input;
        }

        private static List<SearchResult> SearchAll(Input input)
        {
            var results = new List<SearchResult>();
            foreach (var target in TargetRules)
            {
                SearchResult best = null;
                foreach (var template in input.Templates.OrderBy(x => x.Item.templateId, StringComparer.Ordinal))
                {
                    foreach (var candidate in BuildCandidates(template, input.Definitions, target))
                    {
                        var evaluation = EvaluateRepeated(template, input.Definitions, candidate, target);
                        if (!evaluation.Stable) continue;
                        best = new SearchResult { TargetRule = target, Template = template, Candidate = candidate, RuleIds = evaluation.RuleIds, Suitable = true };
                        break;
                    }
                    if (best != null) break;
                }
                results.Add(best ?? new SearchResult
                {
                    TargetRule = target,
                    Suitable = false,
                    Reason = "NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。"
                });
            }
            return results;
        }

        private static IEnumerable<Candidate> BuildCandidates(TemplateData template, Dictionary<string, ComponentDefinition> definitions, string target)
        {
            var components = template.Template.components ?? new List<TemplateComponentDto>();
            var wires = template.Template.wires ?? new List<TemplateWireDto>();
            var result = new List<Candidate>();
            var sources = components.Where(x => HasDefinition(definitions, x, ComponentKind.PowerSource)).ToList();
            var motors = components.Where(x => x != null && x.definitionName.IndexOf("Motor", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var contactors = components.Where(x => x != null && x.definitionName.IndexOf("Contactor", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var timers = components.Where(x => x != null && x.definitionName.IndexOf("Timer_", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var thermals = components.Where(x => x != null && x.definitionName.IndexOf("ThermalRelay", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var protection = components.Where(x => x != null && (x.definitionName.IndexOf("Breaker", StringComparison.OrdinalIgnoreCase) >= 0 || x.definitionName.IndexOf("Fuse", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            if (target == "SELF_HOLDING_BRANCH_INCOMPLETE" || target == "REVERSING_INTERLOCK_MISSING")
            {
                for (var i = 0; i < wires.Count; i++)
                    if (ContainsTerminal(wires[i], "13") || ContainsTerminal(wires[i], "14") || ContainsTerminal(wires[i], "21") || ContainsTerminal(wires[i], "22")) result.Add(Candidate.Remove(i));
                return result;
            }

            if (target == "BREAKER_OR_FUSE_BYPASSED")
            {
                foreach (var source in sources)
                    foreach (var item in protection)
                        foreach (var pair in new[] { new[] { "L1", "T1" }, new[] { "L2", "T2" }, new[] { "L3", "T3" }, new[] { "P1_IN", "P1_OUT" }, new[] { "P2_IN", "P2_OUT" }, new[] { "P3_IN", "P3_OUT" } })
                            AddIfValid(result, template, definitions, source.instanceId, PhaseFor(pair[0]), item.instanceId, pair[1], "保护器件输出旁路");
            }
            else if (target == "MOTOR_CONTACTOR_BYPASSED" || target == "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED")
            {
                foreach (var source in sources)
                    foreach (var motor in motors)
                    {
                        AddIfValid(result, template, definitions, source.instanceId, "L1", motor.instanceId, "U", "三相电机主回路旁路");
                        AddIfValid(result, template, definitions, source.instanceId, "L2", motor.instanceId, "V", "三相电机主回路旁路");
                        AddIfValid(result, template, definitions, source.instanceId, "L3", motor.instanceId, "W", "三相电机主回路旁路");
                    }
            }
            else if (target == "STOP_BUTTON_BYPASSED" || target == "REVERSING_CONTACTOR_CONFLICT" || target == "TIMER_CONTROL_BYPASSED" || target == "THERMAL_RELAY_CONTROL_BYPASSED")
            {
                foreach (var source in sources)
                    foreach (var contactor in contactors)
                    {
                        AddIfValid(result, template, definitions, source.instanceId, "L1", contactor.instanceId, "A1", "控制线圈直接旁路");
                        AddIfValid(result, template, definitions, source.instanceId, "L", contactor.instanceId, "A1", "控制线圈直接旁路");
                    }
                if (target == "THERMAL_RELAY_CONTROL_BYPASSED")
                    foreach (var thermal in thermals)
                        foreach (var contactor in contactors)
                            AddIfValid(result, template, definitions, contactor.instanceId, "A2", thermal.instanceId, "96", "热继控制触点旁路候选");
                if (target == "TIMER_CONTROL_BYPASSED")
                    foreach (var timer in timers)
                        foreach (var contactor in contactors)
                            AddIfValid(result, template, definitions, timer.instanceId, "18", contactor.instanceId, "A1", "KT 延时触点控制候选");
            }

            // 删除单线是对保护、自锁与互锁支路的最后保守尝试；不构造多线猜测。
            if (result.Count == 0 || target == "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED")
                for (var i = 0; i < wires.Count; i++) result.Add(Candidate.Remove(i));
            return result;
        }

        private static void AddIfValid(List<Candidate> candidates, TemplateData template, Dictionary<string, ComponentDefinition> definitions, string fromId, string fromTerminal, string toId, string toTerminal, string note)
        {
            if (HasTerminal(template, definitions, fromId, fromTerminal) && HasTerminal(template, definitions, toId, toTerminal) && !HasWire(template.Template.wires, fromId, fromTerminal, toId, toTerminal))
                candidates.Add(Candidate.Add(fromId, fromTerminal, toId, toTerminal, note));
        }

        private static Evaluation EvaluateRepeated(TemplateData template, Dictionary<string, ComponentDefinition> definitions, Candidate candidate, string target)
        {
            List<string> expected = null;
            for (var i = 0; i < 3; i++)
            {
                var ids = EvaluateOnce(template, definitions, candidate).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (!ids.Contains(target)) return new Evaluation();
                if (expected == null) expected = ids;
                else if (!expected.SequenceEqual(ids)) return new Evaluation();
            }
            return new Evaluation { Stable = true, RuleIds = expected };
        }

        private static List<string> EvaluateOnce(TemplateData template, Dictionary<string, ComponentDefinition> definitions, Candidate candidate)
        {
            var root = new GameObject("__NegativePerturbationSearch__");
            try
            {
                var components = new Dictionary<string, CircuitComponent>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in template.Template.components ?? new List<TemplateComponentDto>())
                {
                    if (item == null || !definitions.TryGetValue(item.definitionName, out var definition)) continue;
                    var go = new GameObject(item.instanceId);
                    go.transform.SetParent(root.transform, false);
                    var component = go.AddComponent<CircuitComponent>();
                    SetProperty(component, "InstanceId", item.instanceId);
                    SetProperty(component, "Definition", definition);
                    CreateTerminals(component, definition);
                    component.SetClosed(item.isClosed);
                    components[item.instanceId] = component;
                }
                var wires = new List<WireView>();
                var sourceWires = (template.Template.wires ?? new List<TemplateWireDto>()).Select((wire, index) => new IndexedWire { Wire = wire, Index = index }).Where(x => candidate.RemovedWireIndex != x.Index).ToList();
                foreach (var item in sourceWires) AddWire(root.transform, components, item.Wire);
                if (candidate.HasAddedWire) AddWire(root.transform, components, candidate.AddedWire);
                wires.AddRange(root.GetComponentsInChildren<WireView>(true));
                var list = components.Values.ToList();
                var analysis = new CircuitStateAnalyzer().Analyze(list, wires);
                var report = new CircuitValidationService().Validate(list, wires, analysis);
                return report?.Issues == null ? new List<string>() : report.Issues.Where(x => x != null && !string.IsNullOrWhiteSpace(x.RuleId)).Select(x => x.RuleId).Distinct(StringComparer.Ordinal).ToList();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateTerminals(CircuitComponent component, ComponentDefinition definition)
        {
            var terminals = new List<TerminalView>();
            foreach (var terminalDefinition in definition.terminals ?? new List<TerminalDefinition>())
            {
                if (terminalDefinition == null || string.IsNullOrWhiteSpace(terminalDefinition.id)) continue;
                var go = new GameObject(terminalDefinition.id);
                go.transform.SetParent(component.transform, false);
                var terminal = go.AddComponent<TerminalView>();
                SetProperty(terminal, "TerminalId", terminalDefinition.id);
                SetProperty(terminal, "Role", terminalDefinition.role);
                SetProperty(terminal, "Owner", component);
                terminals.Add(terminal);
            }
            var field = typeof(CircuitComponent).GetField("terminals", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(component, terminals);
        }

        private static void AddWire(Transform parent, Dictionary<string, CircuitComponent> components, TemplateWireDto dto)
        {
            if (dto == null || !components.TryGetValue(dto.startComponentId, out var start) || !components.TryGetValue(dto.endComponentId, out var end)) return;
            var first = start.GetTerminal(dto.startTerminalId);
            var second = end.GetTerminal(dto.endTerminalId);
            if (first == null || second == null) return;
            var go = new GameObject("Wire");
            go.transform.SetParent(parent, false);
            var wire = go.AddComponent<WireView>();
            SetProperty(wire, "StartTerminal", first);
            SetProperty(wire, "EndTerminal", second);
        }

        private static void SetProperty(object target, string name, object value)
        {
            typeof(CircuitComponent).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private static void WriteConfirmedRuntimePolicy(Input input)
        {
            Directory.CreateDirectory(OutputRoot);
            var auto = input.Templates.First(x => x.Item.templateId == "motor_auto_reciprocating_control");
            var sequential = input.Templates.First(x => x.Item.templateId == "motor_sequential_start_timer");
            var star = input.Templates.First(x => x.Item.templateId == "motor_star_delta_start");
            var builder = new StringBuilder();
            builder.AppendLine("# 已确认模板运行用例");
            builder.AppendLine();
            builder.AppendLine("## 自动往返（motor_auto_reciprocating_control）");
            builder.AppendLine("| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            builder.AppendLine("| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放；之后不人工操作 SQ。 |");
            builder.AppendLine("| 正向 KM（推导测试称呼） | `km_forward` | `Contactor_KM_380V` | `IsEnergized`；正向称呼仅根据实例 ID 推导。 |");
            builder.AppendLine("| 反向 KM（推导测试称呼） | `km_reverse` | `Contactor_KM_380V` | `IsEnergized`；反向称呼仅根据实例 ID 推导。 |");
            builder.AppendLine("| 电机与虚拟运动 | `motor_1` | `Motor_ThreePhase_380V` | `MotionRuntimeState.Direction`、`Position`、`LeftLimitTriggered`、`RightLimitTriggered`。 |");
            builder.AppendLine("| 虚拟限位 | `sq_left` / `sq_right` | `LimitSwitch_Compound` | 分别读取 `motor_1` 的左/右虚拟限位触发状态。 |");
            builder.AppendLine();
            builder.AppendLine("1. 加载模板，点击开始仿真，按下并释放 `start_1`，之后不手动操作 SQ。");
            builder.AppendLine("2. 观察 `km_forward`（正向测试称呼，根据实例 ID 推导）与 `km_reverse`（反向测试称呼，根据实例 ID 推导）自动交替；二者不得同时吸合。");
            builder.AppendLine("3. 观察 `motor_1` 的 `MotionDirection`、`Position`，以及 `sq_left` / `sq_right` 的虚拟触发；建议观察至少 6 秒，覆盖初始 Position=50、Speed=20 下的一次边界到达。");
            builder.AppendLine("4. 停止仿真后，RuntimeStateManager 清除运动状态；再次进入会以 Position=50、Direction=Stopped 重新建立。\n");
            builder.AppendLine("## 两电机顺序启动（motor_sequential_start_timer）");
            builder.AppendLine("| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            builder.AppendLine("| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放。 |");
            builder.AppendLine("| 第一回路 | `km_1` / `motor_1` | `Contactor_KM_380V` / `Motor_ThreePhase_380V` | 接触器 `IsEnergized` 与电机运行态。 |");
            builder.AppendLine("| 第二回路 | `km_2` / `motor_2` | `Contactor_KM_380V` / `Motor_ThreePhase_380V` | 初始停止，KT 到期后运行。 |");
            builder.AppendLine("| 延时器 | `kt_1` | `Timer_OnDelay_380V` | `TimerRuntimeState.Phase`、`ElapsedSeconds`、`DelaySeconds`；当前模板未覆盖延时参数，运行逻辑默认约 3 秒。 |");
            builder.AppendLine();
            builder.AppendLine("1. 点击开始仿真，按下并释放 `start_1`，不操作 `kt_1`。");
            builder.AppendLine("2. 观察 `km_1` / `motor_1` 先运行；`km_2` / `motor_2` 初始停止。");
            builder.AppendLine("3. 等待默认约 3 秒，观察 `kt_1` 从 Timing 到 Elapsed，随后 `km_2` / `motor_2` 运行；之后两台电机保持运行。");
            builder.AppendLine("4. 点击停止并再次启动时，KT 运行态复位后重新计时。\n");
            builder.AppendLine("## 星三角启动（motor_star_delta_start）");
            builder.AppendLine("| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |");
            builder.AppendLine("| --- | --- | --- | --- |");
            builder.AppendLine("| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放。 |");
            builder.AppendLine("| 主 KM | `km_main` | `Contactor_KM_380V` | `IsEnergized`。 |");
            builder.AppendLine("| 星形 KM | `km_star` | `Contactor_KM_380V` | `IsEnergized`；0 至约 3 秒为观察重点。 |");
            builder.AppendLine("| 三角 KM | `km_delta` | `Contactor_KM_380V` | `IsEnergized`；约 3 秒后为观察重点。 |");
            builder.AppendLine("| 延时器 | `kt_timer` | `Timer_OnDelay_380V` | `TimerRuntimeState.Phase`、`ElapsedSeconds`、`DelaySeconds`；当前模板未覆盖延时参数，运行逻辑默认约 3 秒。 |");
            builder.AppendLine("| 六端子电机 | `motor_star_delta` | `Motor_StarDelta_380V` | 观察正常运行视觉；不以参数估算数值判定。 |");
            builder.AppendLine();
            builder.AppendLine("1. 点击开始仿真，按下并释放 `start_1`，不手动操作 `kt_timer`、`km_star` 或 `km_delta`。");
            builder.AppendLine("2. 0 至约 3 秒观察 `km_main`、`km_star` 与 `kt_timer` 的启动/计时状态；`motor_star_delta` 可持续显示正常运行。");
            builder.AppendLine("3. 约 3 秒后观察 `km_star` 释放、`km_delta` 吸合，且星/三角接触器不得同时吸合。当前代码没有单独承诺一个可视的停机间隙；按实际状态切换观察。");
            builder.AppendLine("4. 点击停止并再次启动时，KT 复位后重新计时。\n");
            builder.AppendLine("## 测试政策");
            builder.AppendLine("- 参数估算、电流、电压与功率不属于正式人工测试范围；遗留报告内容不作为通过/失败依据。");
            builder.AppendLine("- SQ 与 KT 不要求测试人员手动操作；自动往返限位和 KT 计时由运行态自动推进。");
            builder.AppendLine("- 以上实例 ID 来自三份模板 JSON；运行态规则来自 RuntimeStateManager、SimulationEngine 与 WorkspaceController。\n");
            WriteUtf8(Path.Combine(OutputRoot, "03_TemplateRuntimeTestCases_Confirmed.md"), builder.ToString());
            WriteUtf8(Path.Combine(OutputRoot, "06_OwnerConfirmedTestPolicy.md"), "# 项目负责人确认的测试政策\n\n- 参数估算不在正式人工测试范围。\n- 自动往返：启动后只观察系统自动换向，不手动操作 SQ。\n- 两电机顺序启动：启动后等待默认约 3 秒。\n- 星三角：启动后等待自动阶段切换，电机视觉持续运行属于允许表现。\n- SQ 和 KT 不要求测试人员手动操作。\n");
        }

        private static void WriteResults(List<SearchResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# 负向规则端点搜索结果\n");
            builder.AppendLine("搜索方式：每次只在内存模板副本中删除一条既有 W-xxx 或新增一条端子导线，并连续运行 3 次 CircuitStateAnalyzer + CircuitValidationService。未写回模板 JSON。\n");
            builder.AppendLine("| RuleId | 结果 | 模板 | 修改 | 删除 W-xxx（原端点） | 新增端点 | 建议线色 | 前置状态 | 实际 RuleIds | 额外 Issue | 适合人工测试 | 证据 |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var result in results)
            {
                if (!result.Suitable)
                {
                    builder.AppendLine("| `" + result.TargetRule + "` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | " + Escape(result.Reason + "；" + AutomaticEvidence(result.TargetRule)) + " |");
                    continue;
                }
                var extraIssues = result.RuleIds.Where(x => x != result.TargetRule).ToList();
                builder.AppendLine("| `" + result.TargetRule + "` | 连续 3 次稳定 | " + Escape(result.Template.Item.templateId) + " | " + Escape(result.Candidate.Note) + " | " + Escape(result.Candidate.RemovedWireText(result.Template)) + " | " + Escape(result.Candidate.AddedStartText()) + " -> " + Escape(result.Candidate.AddedEndText()) + " | " + Escape(result.Candidate.AddedWire == null ? "-" : result.Candidate.AddedWire.color) + " | 标准模板静态接线；无需运行态操作 | " + Escape(string.Join("；", result.RuleIds)) + " | " + Escape(extraIssues.Count == 0 ? "无" : string.Join("；", extraIssues)) + " | 是 | 内存 Template DTO + CircuitStateAnalyzer + CircuitValidationService |");
            }
            WriteUtf8(Path.Combine(OutputRoot, "04_NegativeValidationSearchResults.md"), builder.ToString());
        }

        private static bool HasDefinition(Dictionary<string, ComponentDefinition> definitions, TemplateComponentDto item, ComponentKind kind) => item != null && definitions.TryGetValue(item.definitionName, out var definition) && definition.kind == kind;
        private static bool ContainsTerminal(TemplateWireDto wire, string terminal) => wire != null && (wire.startTerminalId == terminal || wire.endTerminalId == terminal);
        private static string PhaseFor(string input) => input.EndsWith("1", StringComparison.Ordinal) ? "L1" : input.EndsWith("2", StringComparison.Ordinal) ? "L2" : input.EndsWith("3", StringComparison.Ordinal) ? "L3" : "L1";
        private static bool HasTerminal(TemplateData template, Dictionary<string, ComponentDefinition> definitions, string instanceId, string terminalId) => template.Template.components.Any(x => x != null && x.instanceId == instanceId && definitions.TryGetValue(x.definitionName, out var definition) && definition.terminals.Any(t => t != null && t.id == terminalId));
        private static bool HasWire(List<TemplateWireDto> wires, string a, string at, string b, string bt) => (wires ?? new List<TemplateWireDto>()).Any(x => x != null && ((x.startComponentId == a && x.startTerminalId == at && x.endComponentId == b && x.endTerminalId == bt) || (x.startComponentId == b && x.startTerminalId == bt && x.endComponentId == a && x.endTerminalId == at)));
        private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        private static string AutomaticEvidence(string ruleId)
        {
            switch (ruleId)
            {
                case "BREAKER_OR_FUSE_BYPASSED":
                case "MOTOR_CONTACTOR_BYPASSED":
                    return "自动测试证据：Assets/Scripts/Editor/ProtectionBypassValidationTests.cs";
                case "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED":
                case "THERMAL_RELAY_CONTROL_BYPASSED":
                case "TIMER_CONTROL_BYPASSED":
                    return "自动测试证据：Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs";
                case "REVERSING_CONTACTOR_CONFLICT":
                    return "自动测试证据：Assets/Scripts/Editor/TopologySafetyTests.cs";
                default:
                    return "自动测试证据：现有 Validation/Topology Editor 测试入口";
            }
        }
        private static void WriteUtf8(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(true));

        /// <summary>搜索运行输入集合。LoadInput 会填充可变集合，搜索阶段将其作为输入使用；类型自身不阻止后续修改集合，但不会写回项目模板文件。</summary>
        private sealed class Input { public readonly Dictionary<string, ComponentDefinition> Definitions = new Dictionary<string, ComponentDefinition>(StringComparer.OrdinalIgnoreCase); public readonly List<TemplateData> Templates = new List<TemplateData>(); }
        /// <summary>一张标准模板的目录项、已反序列化 DTO 与 JSON 路径。它只提供搜索上下文，不代表活动 Workspace。</summary>
        private sealed class TemplateData { public CircuitTemplateCatalogItemDto Item; public CircuitTemplateDto Template; public string JsonPath; }
        /// <summary>模板原始导线及其稳定列表索引，用于候选删除操作；Index 仅在当前 TemplateData.wires 顺序内有效。</summary>
        private sealed class IndexedWire { public TemplateWireDto Wire; public int Index; }
        /// <summary>重复执行同一候选后的 RuleId 结果。Stable 要求目标 RuleId 三次均出现，且三次排序后的完整 RuleId 列表完全一致；只说明当前代码、模板与候选下的重复执行结果稳定。</summary>
        private sealed class Evaluation { public bool Stable; public List<string> RuleIds = new List<string>(); }
        /// <summary>单个目标 RuleId 的搜索输出。Suitable 与 Reason 由 SearchAll 赋值，供结果文档判断是否适合人工模板测试。</summary>
        private sealed class SearchResult { public string TargetRule; public bool Suitable; public string Reason; public TemplateData Template; public Candidate Candidate; public List<string> RuleIds; }
        /// <summary>只保存删除导线索引或新增导线 DTO 的候选描述。它不直接修改 TemplateData.Template；EvaluateOnce 根据 Candidate 构造临时运行对象，不修改 JSON 文件、Definition 或活动画布。</summary>
        private sealed class Candidate
        {
            public int RemovedWireIndex = -1; public TemplateWireDto AddedWire; public string Note;
            public bool HasAddedWire => AddedWire != null;
            public string RemovedWireLabel => RemovedWireIndex < 0 ? "-" : "W-" + (RemovedWireIndex + 1).ToString("000", CultureInfo.InvariantCulture);
            public static Candidate Remove(int index) => new Candidate { RemovedWireIndex = index, Note = "删除既有导线" };
            public static Candidate Add(string a, string at, string b, string bt, string note) => new Candidate { AddedWire = new TemplateWireDto { startComponentId = a, startTerminalId = at, endComponentId = b, endTerminalId = bt, color = "#E74C3C", style = "Orthogonal" }, Note = note };
            public string RemovedWireText(TemplateData template)
            {
                if (RemovedWireIndex < 0 || template == null || template.Template == null || template.Template.wires == null || RemovedWireIndex >= template.Template.wires.Count) return "-";
                return RemovedWireLabel + "：" + Endpoint(template.Template.wires[RemovedWireIndex], true) + " → " + Endpoint(template.Template.wires[RemovedWireIndex], false);
            }
            public string AddedStartText() => AddedWire == null ? "-" : Endpoint(AddedWire, true);
            public string AddedEndText() => AddedWire == null ? "-" : Endpoint(AddedWire, false);
            private static string Endpoint(TemplateWireDto wire, bool start) => start ? wire.startComponentId + "." + wire.startTerminalId : wire.endComponentId + "." + wire.endTerminalId;
        }
    }
}
#endif
