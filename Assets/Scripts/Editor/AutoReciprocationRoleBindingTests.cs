using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Practice;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 自动往返角色绑定的回归入口。
    /// 该测试始终通过正式模板读取、元件生成和 SimulationEngine 步进构造运行图，确保角色解析不依赖模板实例 ID、
    /// 生成顺序、导线存储方向或 GameObject 名称；CSV 仅用于审计，任何不变量不成立都会抛出异常。
    /// </summary>
    public static class AutoReciprocationRoleBindingTests
    {
        private const string AutoReciprocatingTemplateId = "motor_auto_reciprocating_control";
        private static readonly string[] NonAutomaticIndustrialTemplateIds =
        {
            "motor_jog_control",
            "motor_forward_reverse_control",
            "motor_forward_reverse_interlock",
            "motor_forward_reverse_double_interlock",
            "motor_sequential_start_timer",
            "motor_star_delta_start"
        };

        [MenuItem("Tools/Tests/Run Auto Reciprocation Role Binding Tests")]
        public static void Run()
        {
            var output = Environment.GetEnvironmentVariable("E5_TEST_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetTempPath(), "E5_AutoReciprocationRoles");
            Directory.CreateDirectory(output);

            var failures = new List<string>();
            var roleRows = new List<string> { "caseId,status,motor,forward,reverse,leftLimit,rightLimit,detail" };
            var negativeRows = new List<string> { "caseId,expected,actual,detail" };
            var templateTimeline = new List<string> { "step,position,direction,forwardCoil,reverseCoil,leftLimit,rightLimit" };
            var randomTimeline = new List<string> { "step,position,direction,forwardCoil,reverseCoil,leftLimit,rightLimit" };

            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity", OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                if (workspace == null) failures.Add("找不到 WorkspaceController，无法执行自动往返角色绑定回归。");
                if (saveLoad == null) failures.Add("找不到 SaveLoadService，无法按正式模板生成路径执行回归。");
                if (workspace == null || saveLoad == null)
                {
                    throw new InvalidOperationException("自动往返角色绑定测试所需的正式场景依赖不完整。");
                }

                var catalogAsset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
                var catalog = catalogAsset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(catalogAsset.text) : null;
                if (catalog == null || catalog.templates == null)
                {
                    failures.Add("找不到模板 catalog，无法执行自动往返角色绑定回归。");
                    throw new InvalidOperationException("自动往返角色绑定测试无法读取模板目录。");
                }

                var original = LoadTemplate(catalog, AutoReciprocatingTemplateId, failures);
                if (original == null)
                {
                    throw new InvalidOperationException("自动往返角色绑定测试无法读取自动往返模板。");
                }

                // A：系统模板控制组。解析结果必须绑定到真实生成实例，而非测试侧的固定字符串推断。
                Spawn(workspace, saveLoad, original, failures);
                var templateRoles = ResolveRoles(workspace);
                RequireResolved(templateRoles, "模板控制组", failures);
                AddRoleRow(roleRows, "template", templateRoles, string.Empty);
                RunRuntimeSequence(workspace, templateRoles, templateTimeline, failures, "模板控制组");

                // B/C/D/E/F：随机 ID、组件和 Wire 顺序、部分 Wire 方向、对象名称及模板会话均不应影响解析。
                var randomized = CreateRandomizedEquivalentVariant(original);
                Spawn(workspace, saveLoad, randomized, failures);
                TemplateEditSession.Clear();
                foreach (var component in workspace.Components) component.gameObject.name = "e5-object-" + Guid.NewGuid().ToString("N");
                var randomRoles = ResolveRoles(workspace);
                RequireResolved(randomRoles, "随机 ID 等价图", failures);
                Require(!string.Equals(randomRoles.Motor.InstanceId, "motor_1", StringComparison.Ordinal), "随机 ID 图仍保留 motor_1，测试变体无效。", failures);
                AddRoleRow(roleRows, "randomized", randomRoles, string.Empty);
                RunRuntimeSequence(workspace, randomRoles, randomTimeline, failures, "随机 ID 等价图");
                RequireSameStageOrder(templateTimeline, randomTimeline, failures);

                // C：先经正式练习入口建立会话，再将同一会话中的工作区替换为随机 ID 等价图。
                // PracticeSession 保存的标准模板不参与角色解析；此处同时证明练习检查和运行角色都只依赖当前真实拓扑。
                var practice = PracticeSessionController.Instance;
                var practiceItem = catalog.templates.FirstOrDefault(item => item.templateId == AutoReciprocatingTemplateId);
                Require(practice != null && practiceItem != null, "缺少正式练习会话或自动往返模板目录项。", failures);
                if (practice != null && practiceItem != null)
                {
                    practice.ClearPracticeState();
                    workspace.ClearDrawing(false);
                    practice.StartPractice(practiceItem);
                    Require(practice.IsPracticeActive && practice.CurrentTemplateData != null, "正式练习入口未建立自动往返练习会话。", failures);
                    Spawn(workspace, saveLoad, randomized, failures);
                    var practiceRoles = ResolveRoles(workspace);
                    RequireResolved(practiceRoles, "练习会话中的随机 ID 等价图", failures);
                    Require(ElectricalSim.Practice.Netlist.PracticeConnectionChecker.Check(workspace, practice.CurrentTemplateData).Passed, "练习会话中的随机 ID 等价图未通过正式 PracticeConnectionChecker。", failures);
                    AddRoleRow(roleRows, "practice-randomized", practiceRoles, "practice-active=" + practice.IsPracticeActive);
                    practice.EndPractice();
                }

                // G/H/I：候选不完整或存在多套候选时必须保守地拒绝，不能按组件顺序猜测角色。
                var missingRight = Clone(randomized);
                RemoveComponentAndWires(missingRight, "LimitSwitch_Compound");
                Spawn(workspace, saveLoad, missingRight, failures);
                var missingResult = ResolveRoles(workspace);
                Require(missingResult.Status == AutoReciprocationRoleResolutionStatus.NotApplicable, "缺少右限位应为 NotApplicable，实际为 " + missingResult.Status, failures);
                AddNegativeRow(negativeRows, "missing-right-limit", "NotApplicable", missingResult.Status.ToString(), missingResult.Reason);

                var extraLimit = Clone(randomized);
                AddComponentClone(extraLimit, "LimitSwitch_Compound", "extra-limit");
                Spawn(workspace, saveLoad, extraLimit, failures);
                var extraLimitResult = ResolveRoles(workspace);
                Require(extraLimitResult.Status == AutoReciprocationRoleResolutionStatus.Ambiguous, "多余限位候选应为 Ambiguous，实际为 " + extraLimitResult.Status, failures);
                AddNegativeRow(negativeRows, "extra-limit", "Ambiguous", extraLimitResult.Status.ToString(), extraLimitResult.Reason);

                var extraMotor = Clone(randomized);
                AddComponentClone(extraMotor, "Motor_ThreePhase_380V", "extra-motor");
                Spawn(workspace, saveLoad, extraMotor, failures);
                var extraMotorResult = ResolveRoles(workspace);
                Require(extraMotorResult.Status == AutoReciprocationRoleResolutionStatus.Ambiguous, "多台电机候选应为 Ambiguous，实际为 " + extraMotorResult.Status, failures);
                AddNegativeRow(negativeRows, "extra-motor", "Ambiguous", extraMotorResult.Status.ToString(), extraMotorResult.Reason);

                foreach (var templateId in NonAutomaticIndustrialTemplateIds)
                {
                    var candidate = LoadTemplate(catalog, templateId, failures);
                    if (candidate == null) continue;
                    Spawn(workspace, saveLoad, candidate, failures);
                    var result = ResolveRoles(workspace);
                    Require(result.Status == AutoReciprocationRoleResolutionStatus.NotApplicable, templateId + " 不得被误判为自动往返，实际为 " + result.Status, failures);
                    AddNegativeRow(negativeRows, templateId, "NotApplicable", result.Status.ToString(), result.Reason);
                }
            }
            catch (Exception exception)
            {
                failures.Add("自动往返角色绑定测试出现未处理异常：" + exception);
            }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "ROLE_BINDING_MATRIX.csv"), roleRows, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "NEGATIVE_MATRIX.csv"), negativeRows, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "AUTO_RECIPROCATION_TEMPLATE_TIMELINE.csv"), templateTimeline, System.Text.Encoding.UTF8);
                File.WriteAllLines(Path.Combine(output, "AUTO_RECIPROCATION_RANDOM_ID_TIMELINE.csv"), randomTimeline, System.Text.Encoding.UTF8);
                SimulationEngine.ResetRuntimeState();
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("自动往返角色绑定回归失败：\n- " + string.Join("\n- ", failures));
            }

            UnityEngine.Debug.Log("[Electrical][E5] 自动往返运行角色绑定：通过");
        }

        private static CircuitTemplateDto LoadTemplate(CircuitTemplateCatalogDto catalog, string templateId, ICollection<string> failures)
        {
            var item = catalog.templates.FirstOrDefault(candidate => candidate.templateId == templateId);
            CircuitTemplateDto template = null;
            string error = null;
            if (item == null || !CircuitTemplateLoader.TryLoad(item.resourcePath, out template, out error))
            {
                failures.Add("读取模板失败：" + templateId + "；" + error);
                return null;
            }

            return template;
        }

        private static void Spawn(WorkspaceController workspace, SaveLoadService saveLoad, CircuitTemplateDto template, ICollection<string> failures)
        {
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();
            if (!CircuitTemplateSpawnService.Spawn(template, workspace, saveLoad.Catalog, out var message))
            {
                failures.Add("正式模板生成失败：" + template.templateId + "；" + message);
            }
        }

        private static AutoReciprocationRoleResolution ResolveRoles(WorkspaceController workspace)
        {
            return AutoReciprocationRoleResolver.Resolve(workspace.Components, workspace.WireManager != null ? workspace.WireManager.Wires : null);
        }

        private static void RequireResolved(AutoReciprocationRoleResolution roles, string name, ICollection<string> failures)
        {
            Require(roles != null && roles.Status == AutoReciprocationRoleResolutionStatus.Resolved, name + " 角色解析失败：" + (roles == null ? "null" : roles.Status + "；" + roles.Reason), failures);
            if (roles == null || !roles.IsResolved) return;
            Require(roles.Motor != null && roles.ForwardContactor != null && roles.ReverseContactor != null && roles.LeftLimitSwitch != null && roles.RightLimitSwitch != null,
                name + " 没有完整绑定五个角色。", failures);
        }

        private static void RunRuntimeSequence(WorkspaceController workspace, AutoReciprocationRoleResolution roles, ICollection<string> rows, ICollection<string> failures, string name)
        {
            if (roles == null || !roles.IsResolved) return;
            var start = workspace.Components.FirstOrDefault(component => component != null && component.Definition != null && component.Definition.name.IndexOf("Button_Start", StringComparison.OrdinalIgnoreCase) >= 0);
            Require(start != null, name + " 缺少启动按钮。", failures);
            if (start == null) return;

            start.SetClosed(true);
            var visitedForward = false;
            var visitedRight = false;
            var visitedReverse = false;
            var visitedLeft = false;
            for (var step = 0; step < 18; step++)
            {
                new SimulationEngine(workspace.Components.ToList(), workspace.WireManager.Wires, 0.5f).Run();
                RuntimeStateManager.Shared.TryGetMotionState(roles.Motor.InstanceId, out var motion);
                var direction = motion != null ? motion.Direction : MotionDirection.Stopped;
                var position = motion != null ? motion.Position : -1f;
                var right = motion != null && motion.RightLimitTriggered;
                var left = motion != null && motion.LeftLimitTriggered;
                var rightVisual = ResolveLimitVisualState(roles.RightLimitSwitch);
                var leftVisual = ResolveLimitVisualState(roles.LeftLimitSwitch);
                visitedForward |= direction == MotionDirection.Forward;
                visitedRight |= right;
                visitedReverse |= direction == MotionDirection.Reverse;
                visitedLeft |= left;
                Require(right == rightVisual, name + " 右限位逻辑与视觉状态解析不一致。", failures);
                Require(left == leftVisual, name + " 左限位逻辑与视觉状态解析不一致。", failures);
                rows.Add(string.Join(",", step, position.ToString("R", System.Globalization.CultureInfo.InvariantCulture), direction,
                    roles.ForwardContactor.IsEnergized, roles.ReverseContactor.IsEnergized, left, right));
            }

            Require(visitedForward, name + " 没有进入正向运动阶段。", failures);
            Require(visitedRight, name + " 没有到达右限位阶段。", failures);
            Require(visitedReverse, name + " 没有进入反向运动阶段。", failures);
            Require(visitedLeft, name + " 没有到达左限位阶段。", failures);
        }

        private static void RequireSameStageOrder(IReadOnlyList<string> expected, IReadOnlyList<string> actual, ICollection<string> failures)
        {
            var expectedDirections = expected.Skip(1).Select(row => row.Split(',')[2]).Distinct().ToArray();
            var actualDirections = actual.Skip(1).Select(row => row.Split(',')[2]).Distinct().ToArray();
            Require(expectedDirections.SequenceEqual(actualDirections), "模板与随机 ID 图的运动阶段顺序不一致：" + string.Join("/", expectedDirections) + " != " + string.Join("/", actualDirections), failures);
        }

        private static bool ResolveLimitVisualState(CircuitComponent limitSwitch)
        {
            var method = typeof(CircuitComponent).GetMethod("ResolveLimitSwitchTriggeredVisualState", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("CircuitComponent.ResolveLimitSwitchTriggeredVisualState");
            return (bool)method.Invoke(limitSwitch, null);
        }

        private static CircuitTemplateDto CreateRandomizedEquivalentVariant(CircuitTemplateDto source)
        {
            var variant = Clone(source);
            var map = new Dictionary<string, string>();
            foreach (var component in variant.components)
            {
                var originalId = component.instanceId;
                var randomizedId = "e5-" + Guid.NewGuid().ToString("N");
                map[originalId] = randomizedId;
                component.instanceId = randomizedId;
            }

            foreach (var wire in variant.wires)
            {
                wire.startComponentId = map[wire.startComponentId];
                wire.endComponentId = map[wire.endComponentId];
            }

            variant.components.Reverse();
            variant.wires.Reverse();
            for (var i = 0; i < variant.wires.Count; i += 2)
            {
                var wire = variant.wires[i];
                SwapEndpoints(wire);
            }
            return variant;
        }

        private static void RemoveComponentAndWires(CircuitTemplateDto template, string definitionName)
        {
            var component = template.components.FirstOrDefault(item => item.definitionName == definitionName);
            if (component == null) return;
            template.components.Remove(component);
            template.wires.RemoveAll(wire => wire.startComponentId == component.instanceId || wire.endComponentId == component.instanceId);
        }

        private static void AddComponentClone(CircuitTemplateDto template, string definitionName, string suffix)
        {
            var source = template.components.FirstOrDefault(component => component.definitionName == definitionName);
            if (source == null) return;
            var clone = JsonUtility.FromJson<TemplateComponentDto>(JsonUtility.ToJson(source));
            clone.instanceId = "e5-" + suffix + "-" + Guid.NewGuid().ToString("N");
            clone.x += 1600f;
            template.components.Add(clone);
        }

        private static CircuitTemplateDto Clone(CircuitTemplateDto source)
        {
            return JsonUtility.FromJson<CircuitTemplateDto>(JsonUtility.ToJson(source));
        }

        private static void SwapEndpoints(TemplateWireDto wire)
        {
            var component = wire.startComponentId;
            var terminal = wire.startTerminalId;
            wire.startComponentId = wire.endComponentId;
            wire.startTerminalId = wire.endTerminalId;
            wire.endComponentId = component;
            wire.endTerminalId = terminal;
        }

        private static void AddRoleRow(ICollection<string> rows, string caseId, AutoReciprocationRoleResolution roles, string detail)
        {
            rows.Add(string.Join(",", caseId, roles == null ? "null" : roles.Status.ToString(), RoleId(roles?.Motor), RoleId(roles?.ForwardContactor), RoleId(roles?.ReverseContactor), RoleId(roles?.LeftLimitSwitch), RoleId(roles?.RightLimitSwitch), Csv(detail)));
        }

        private static void AddNegativeRow(ICollection<string> rows, string caseId, string expected, string actual, string detail)
        {
            rows.Add(string.Join(",", caseId, expected, actual, Csv(detail)));
        }

        private static string RoleId(CircuitComponent component) => component == null ? string.Empty : component.InstanceId;
        private static string Csv(string text) => "\"" + (text ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static void Require(bool condition, string message, ICollection<string> failures)
        {
            if (!condition) failures.Add(message);
        }
    }
}
