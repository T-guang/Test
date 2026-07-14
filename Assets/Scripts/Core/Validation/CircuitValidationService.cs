using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    /// <summary>
    /// 将生产校验 Helper 聚合为已分析工作区图的规则报告。
    /// 不推进仿真，也不负责检查助手的报告排版和教学报告组装。RuleId 与 Severity 属于测试和快照保护的契约数据；
    /// 改动 Helper 顺序或严重级别后，必须运行规则和模板验证。
    /// </summary>
    public sealed class CircuitValidationService
    {
        public CircuitValidationReport Validate(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            // 生产规则只接收活动工作区的元件、导线和同一次 Analyzer 结果；不负责推进仿真、
            // 修改接线或排版教学报告。Helper 调用顺序以及 RuleId、Severity、Category 是快照与报告的稳定契约。
            // 必须保持 Helper 调用顺序稳定。多个 Helper 依赖 Analyzer 证据，结果随后会被
            // 结构化 Inspector Block 与回归基线消费。
            var report = new CircuitValidationReport();
            var phaseHelper = new MotorPhaseValidationHelper(components, wires);
            AddMotorIssues(report, components, analysisResult, phaseHelper);
            AddComponentInvariantIssues(report, components, phaseHelper);
            AddPowerSafetyIssues(report, components, wires, analysisResult);
            AddProtectionBypassIssues(report, components, wires, analysisResult);
            AddTimerControlBypassIssues(report, components, wires, analysisResult);
            AddControlCircuitStructureIssues(report, components, wires, analysisResult);
            if (phaseHelper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }

            AddUnsupportedComponentIssues(report, components);
            return report;
        }

        private static void AddPowerSafetyIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            if (report == null)
            {
                return;
            }

            var helper = new PowerPotentialValidationHelper(components, wires, analysisResult);
            var issues = helper.Validate();
            for (var i = 0; i < issues.Count; i++)
            {
                AddIssue(report, issues[i]);
            }

            if (helper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }
        }

        private static void AddProtectionBypassIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            if (report == null)
            {
                return;
            }

            var helper = new ProtectionBypassValidationHelper(components, wires, analysisResult);
            var issues = helper.Validate();
            for (var i = 0; i < issues.Count; i++)
            {
                AddIssue(report, issues[i]);
            }

            if (helper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }
        }

        private static void AddTimerControlBypassIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            if (report == null)
            {
                return;
            }

            var helper = new TimerControlBypassValidationHelper(components, wires, analysisResult);
            var issues = helper.Validate();
            for (var i = 0; i < issues.Count; i++)
            {
                AddIssue(report, issues[i]);
            }

            if (helper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }
        }

        private static void AddMotorIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            CircuitStateResult analysisResult,
            MotorPhaseValidationHelper phaseHelper)
        {
            if (report == null || analysisResult == null || analysisResult.Components == null)
            {
                return;
            }

            for (var i = 0; i < analysisResult.Components.Count; i++)
            {
                var info = analysisResult.Components[i];
                if (info == null)
                {
                    continue;
                }

                var component = FindComponent(components, info.InstanceId);
                if (info.IsStarDeltaMotor)
                {
                    AddStarDeltaIssue(report, component, info, phaseHelper);
                }
                else if (info.IsThreePhaseMotor)
                {
                    AddThreePhaseMotorIssue(report, component, info, phaseHelper);
                }
            }
        }

        private static void AddThreePhaseMotorIssue(
            CircuitValidationReport report,
            CircuitComponent component,
            ComponentStateInfo info,
            MotorPhaseValidationHelper phaseHelper)
        {
            var phaseResult = phaseHelper != null ? phaseHelper.Validate(component, info, false) : null;
            if (phaseResult == null || !phaseResult.ShouldEvaluate)
            {
                return;
            }

            if (phaseResult.HasDuplicatePhase)
            {
                AddIssue(
                    report,
                    "MOTOR_DUPLICATE_PHASE",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.Motor,
                    "三相电机重复相",
                    "三相电机存在重复相接入，当前接线不满足正常三相运行条件。",
                    component,
                    TerminalConstants.U,
                    TerminalConstants.V,
                    TerminalConstants.W);
                return;
            }

            if (phaseResult.HasMissingPhase)
            {
                AddIssue(
                    report,
                    "MOTOR_MISSING_PHASE",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.Motor,
                    "三相电机缺相",
                    "三相电机缺少有效三相供电，当前不满足正常运行条件。",
                    component,
                    TerminalConstants.U,
                    TerminalConstants.V,
                    TerminalConstants.W);
            }
        }

        private static void AddStarDeltaIssue(
            CircuitValidationReport report,
            CircuitComponent component,
            ComponentStateInfo info,
            MotorPhaseValidationHelper phaseHelper)
        {
            if (string.Equals(info.State, "StarDeltaConflict", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.StarDeltaConnectionMode, "Conflict", StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(
                    report,
                    "STAR_DELTA_CONFLICT",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.StarDelta,
                    "星三角冲突",
                    "检测到星形连接与三角连接同时存在，存在星三角冲突风险，系统不输出正常电机估算。",
                    component,
                    TerminalConstants.U1,
                    TerminalConstants.V1,
                    TerminalConstants.W1,
                    TerminalConstants.U2,
                    TerminalConstants.V2,
                    TerminalConstants.W2);
                return;
            }

            if (!ShouldEvaluateStarDeltaPhaseIssue(info, component) ||
                HasStarDeltaTerminalInvariantIssue(component, phaseHelper))
            {
                return;
            }

            var phaseResult = phaseHelper != null ? phaseHelper.Validate(component, info, true) : null;
            if (phaseResult == null || !phaseResult.ShouldEvaluate)
            {
                return;
            }

            if (phaseResult.HasDuplicatePhase)
            {
                AddIssue(
                    report,
                    "STAR_DELTA_DUPLICATE_PHASE",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.StarDelta,
                    "星三角电机重复相",
                    "星三角电机存在重复相接入，当前接线不满足正常三相运行条件。",
                    component,
                    TerminalConstants.U1,
                    TerminalConstants.V1,
                    TerminalConstants.W1);
                return;
            }

            if (phaseResult.HasMissingPhase)
            {
                AddIssue(
                    report,
                    "STAR_DELTA_MISSING_PHASE",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.StarDelta,
                    "星三角电机缺相",
                    "星三角电机缺少有效三相供电，当前不满足正常运行条件。",
                    component,
                    TerminalConstants.U1,
                    TerminalConstants.V1,
                    TerminalConstants.W1);
            }
        }

        private static bool ShouldEvaluateStarDeltaPhaseIssue(ComponentStateInfo info, CircuitComponent component)
        {
            if (info == null)
            {
                return false;
            }

            if (component != null && component.IsEnergized)
            {
                return true;
            }

            if (string.Equals(info.State, "StarConnected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.State, "DeltaConnected", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.State, "StarDeltaConflict", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(info.StarDeltaConnectionMode, "Star", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.StarDeltaConnectionMode, "Delta", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.StarDeltaConnectionMode, "Conflict", StringComparison.OrdinalIgnoreCase))
            {
                return !string.Equals(info.State, "Stopped", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool HasStarDeltaTerminalInvariantIssue(
            CircuitComponent motor,
            MotorPhaseValidationHelper connectivityHelper)
        {
            if (motor == null || connectivityHelper == null)
            {
                return false;
            }

            if (AnyTerminalPairConnected(
                connectivityHelper,
                motor,
                TerminalConstants.U1,
                TerminalConstants.V1,
                TerminalConstants.W1))
            {
                return true;
            }

            var secondaryConnectedPairs = CountConnectedTerminalPairs(
                connectivityHelper,
                motor,
                TerminalConstants.U2,
                TerminalConstants.V2,
                TerminalConstants.W2);
            return secondaryConnectedPairs > 0 && secondaryConnectedPairs < 3;
        }

        private static void AddComponentInvariantIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            MotorPhaseValidationHelper connectivityHelper)
        {
            if (report == null || components == null || connectivityHelper == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (TeachingParameterCalculationService.IsThreePhaseTeachingMotor(component))
                {
                    AddThreePhaseMotorInvariantIssues(report, component, connectivityHelper);
                }

                if (TeachingParameterCalculationService.IsStarDeltaTeachingMotor(component))
                {
                    AddStarDeltaInvariantIssues(report, component, connectivityHelper);
                }
            }
        }

        private static void AddThreePhaseMotorInvariantIssues(
            CircuitValidationReport report,
            CircuitComponent motor,
            MotorPhaseValidationHelper connectivityHelper)
        {
            if (AnyTerminalPairConnected(
                connectivityHelper,
                motor,
                TerminalConstants.U,
                TerminalConstants.V,
                TerminalConstants.W))
            {
                AddIssue(
                    report,
                    "MOTOR_PHASE_TERMINAL_SHORT",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.Motor,
                    "三相电机输入端短接",
                    "检测到三相电机 U/V/W 输入端之间存在直接短接，当前接线不符合三相电机接线要求。",
                    motor,
                    TerminalConstants.U,
                    TerminalConstants.V,
                    TerminalConstants.W);
            }
        }

        private static void AddStarDeltaInvariantIssues(
            CircuitValidationReport report,
            CircuitComponent motor,
            MotorPhaseValidationHelper connectivityHelper)
        {
            if (AnyTerminalPairConnected(
                connectivityHelper,
                motor,
                TerminalConstants.U1,
                TerminalConstants.V1,
                TerminalConstants.W1))
            {
                AddIssue(
                    report,
                    "STAR_DELTA_INPUT_TERMINAL_SHORT",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.StarDelta,
                    "星三角电机输入端短接",
                    "检测到星三角电机 U1/V1/W1 输入端之间存在直接短接，当前接线不符合三相电机输入要求。",
                    motor,
                    TerminalConstants.U1,
                    TerminalConstants.V1,
                    TerminalConstants.W1);
            }

            var connectedPairs = CountConnectedTerminalPairs(
                connectivityHelper,
                motor,
                TerminalConstants.U2,
                TerminalConstants.V2,
                TerminalConstants.W2);
            if (connectedPairs > 0 && connectedPairs < 3)
            {
                AddIssue(
                    report,
                    "STAR_DELTA_PARTIAL_STARPOINT_SHORT",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.StarDelta,
                    "星三角局部星点短接",
                    "星三角电机 U2/V2/W2 存在局部短接。该连接不是完整星形连接，也不是标准三角连接，属于异常接线。",
                    motor,
                    TerminalConstants.U2,
                    TerminalConstants.V2,
                    TerminalConstants.W2);
            }
        }

        private static bool AnyTerminalPairConnected(
            MotorPhaseValidationHelper connectivityHelper,
            CircuitComponent component,
            string first,
            string second,
            string third)
        {
            return CountConnectedTerminalPairs(connectivityHelper, component, first, second, third) > 0;
        }

        private static int CountConnectedTerminalPairs(
            MotorPhaseValidationHelper connectivityHelper,
            CircuitComponent component,
            string first,
            string second,
            string third)
        {
            if (connectivityHelper == null || component == null)
            {
                return 0;
            }

            var count = 0;
            if (connectivityHelper.AreTerminalsDirectlyWired(component, first, second))
            {
                count++;
            }

            if (connectivityHelper.AreTerminalsDirectlyWired(component, second, third))
            {
                count++;
            }

            if (connectivityHelper.AreTerminalsDirectlyWired(component, first, third))
            {
                count++;
            }

            return count;
        }

        private static void AddUnsupportedComponentIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components)
        {
            if (report == null || components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                var definition = component != null ? component.Definition : null;
                if (definition == null ||
                    (definition.supportLevel != ComponentSupportLevel.VisualOnly && definition.canParticipateInRuntime))
                {
                    continue;
                }

                var reason = string.IsNullOrWhiteSpace(definition.unsupportedReason)
                    ? "该元件当前暂未支持完整仿真判断。"
                    : definition.unsupportedReason;

                AddIssue(
                    report,
                    "UNSUPPORTED_COMPONENT",
                    CircuitValidationSeverity.Warning,
                    CircuitValidationCategory.UnsupportedComponent,
                    "暂未支持元件",
                    reason,
                    component);
            }
        }

        private static void AddControlCircuitStructureIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            // 控制回路结构问题与保护旁路问题不能合并：前者结合运行态识别互锁冲突和支路结构，
            // 后者验证静态供电路径；两者对正常教学模板的保守边界不同。
            AddStopButtonBypassedIssues(report, components, analysisResult);
            AddThermalRelayControlBypassedIssues(report, components, wires, analysisResult);
            AddSelfHoldingBranchIssues(report, components, wires);
            AddReversingContactorConflictIssues(report, components, wires, analysisResult);
        }

        private static void AddReversingContactorConflictIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            if (report == null || components == null)
            {
                return;
            }

            var scopes = ReversingPairScopeHelper.ResolveReliableReversingPairs(components, wires, out var traversalLimitExceeded);
            if (traversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }

            for (var i = 0; i < scopes.Count; i++)
            {
                var scope = scopes[i];
                if (scope == null ||
                    !scope.IsReliable ||
                    scope.ForwardContactor == null ||
                    scope.ReverseContactor == null)
                {
                    continue;
                }

                if (!HasReversingContactorConflict(scope, analysisResult))
                {
                    continue;
                }

                AddIssue(
                    report,
                    "REVERSING_CONTACTOR_CONFLICT",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.ControlCircuit,
                    "\u6b63\u53cd\u8f6c\u63a5\u89e6\u5668\u4e92\u9501\u51b2\u7a81",
                    "\u540c\u4e00\u53f0\u7535\u673a\u7684\u6b63\u8f6c\u63a5\u89e6\u5668\u548c\u53cd\u8f6c\u63a5\u89e6\u5668\u51fa\u73b0\u4e92\u9501\u51b2\u7a81\uff0c\u5b58\u5728\u540c\u65f6\u5438\u5408\u6216\u76f8\u5e8f\u51b2\u7a81\u98ce\u9669\u3002\u6b63\u53cd\u8f6c\u63a7\u5236\u4e2d\uff0c\u6b63\u8f6c\u548c\u53cd\u8f6c\u63a5\u89e6\u5668\u4e0d\u80fd\u540c\u65f6\u5438\u5408\u3002\u5f53\u524d\u7cfb\u7edf\u68c0\u6d4b\u5230\u6b63\u53cd\u8f6c\u4e92\u9501\u51b2\u7a81\uff0c\u8fd0\u884c\u5c42\u5df2\u963b\u6b62\u5371\u9669\u72b6\u6001\u7ee7\u7eed\u4f20\u64ad\u3002\u8bf7\u68c0\u67e5\u7535\u6c14\u4e92\u9501 21/22\u3001\u6309\u94ae\u4e92\u9501\u6216\u662f\u5426\u5b58\u5728\u8de8\u63a5\u7ebf\u3002",
                    scope.ForwardContactor,
                    TerminalConstants.A1,
                    TerminalConstants.A2,
                    TerminalConstants.T1,
                    TerminalConstants.T2,
                    TerminalConstants.T3);
            }
        }

        private static bool HasReversingContactorConflict(
            ReversingPairScope scope,
            CircuitStateResult analysisResult)
        {
            if (scope == null ||
                scope.ForwardContactor == null ||
                scope.ReverseContactor == null)
            {
                return false;
            }

            if (IsControlCoilEnergized(scope.ForwardContactor, analysisResult) &&
                IsControlCoilEnergized(scope.ReverseContactor, analysisResult))
            {
                return true;
            }

            return IsContactorInterlockConflict(scope.ForwardContactor, analysisResult) &&
                IsContactorInterlockConflict(scope.ReverseContactor, analysisResult);
        }

        private static bool IsContactorInterlockConflict(
            CircuitComponent contactor,
            CircuitStateResult analysisResult)
        {
            if (analysisResult == null || !analysisResult.HasContactorInterlockConflict)
            {
                return false;
            }

            var info = FindComponentInfo(analysisResult, contactor != null ? contactor.InstanceId : null);
            return info != null &&
                (string.Equals(info.State, "InterlockConflict", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(info.CoilStatus, "InterlockConflict", StringComparison.OrdinalIgnoreCase));
        }

        private static void AddSelfHoldingBranchIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
        {
            if (report == null || components == null)
            {
                return;
            }

            var helper = new SelfHoldingBranchValidationHelper();
            var evaluated = helper.TryEvaluateSingleContactorSelfHold(components, wires, out var result);
            if (helper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }

            if (!evaluated ||
                result == null ||
                !result.IsApplicable ||
                !result.HasSelfHoldAttempt ||
                result.IsValidSelfHoldBranch)
            {
                return;
            }

            AddIssue(
                report,
                "SELF_HOLDING_BRANCH_INCOMPLETE",
                CircuitValidationSeverity.Warning,
                CircuitValidationCategory.ControlCircuit,
                "\u81ea\u9501\u652f\u8def\u4e0d\u5b8c\u6574",
                "\u68c0\u6d4b\u5230\u63a5\u89e6\u5668 13/14 \u53ef\u80fd\u7528\u4e8e\u81ea\u9501\uff0c\u4f46\u672a\u5f62\u6210\u6709\u6548\u7684\u542f\u52a8\u6309\u94ae\u5e76\u8054\u4fdd\u6301\u652f\u8def\uff0c\u8fde\u7eed\u8fd0\u884c\u4fdd\u6301\u80fd\u529b\u53ef\u80fd\u7f3a\u5931\u3002\u8fde\u7eed\u8fd0\u884c\u63a7\u5236\u4e2d\uff0c\u63a5\u89e6\u5668\u7684 13/14 \u5e38\u5f00\u8f85\u52a9\u89e6\u70b9\u901a\u5e38\u5e94\u5e76\u8054\u5728\u542f\u52a8\u6309\u94ae\u4e24\u7aef\uff1b\u82e5 13/14 \u63a5\u7ebf\u4e0d\u5b8c\u6574\u6216\u672a\u5e76\u8054\u5230\u542f\u52a8\u6309\u94ae\u4e24\u4fa7\uff0c\u7535\u8def\u53ef\u80fd\u9000\u5316\u4e3a\u70b9\u52a8\u8fd0\u884c\u3002",
                result.Contactor,
                TerminalConstants.AuxNO13,
                TerminalConstants.AuxNO14,
                "23",
                "24");
        }

        private static void AddThermalRelayControlBypassedIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            if (report == null || components == null)
            {
                return;
            }

            var scopeHelper = new ThermalRelayProtectionScopeHelper();
            for (var i = 0; i < components.Count; i++)
            {
                var relay = components[i];
                if (relay == null || relay.IsClosed)
                {
                    continue;
                }

                if (!scopeHelper.TryResolveProtectionScope(relay, components, wires, out var scope) ||
                    scope == null ||
                    !scope.IsReliable ||
                    scope.UpstreamContactor == null)
                {
                    continue;
                }

                if (!IsControlCoilEnergized(scope.UpstreamContactor, analysisResult))
                {
                    continue;
                }

                AddIssue(
                    report,
                    "THERMAL_RELAY_CONTROL_BYPASSED",
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.Protection,
                    "\u70ed\u7ee7\u63a7\u5236\u4fdd\u62a4\u88ab\u65c1\u8def",
                    "\u70ed\u7ee7\u7535\u5668\u63a7\u5236\u89e6\u70b9\u5df2\u65ad\u5f00\uff0c\u4f46\u5bf9\u5e94\u63a5\u89e6\u5668\u7ebf\u5708\u4ecd\u7136\u5f97\u7535\uff0c\u53ef\u80fd\u5b58\u5728\u70ed\u7ee7 95/96 \u88ab\u65c1\u8def\u6216\u672a\u6709\u6548\u4e32\u5165\u63a7\u5236\u56de\u8def\u3002\u70ed\u7ee7\u7535\u5668\u7684 95/96 \u5e38\u95ed\u89e6\u70b9\u901a\u5e38\u5e94\u4e32\u8054\u5728\u63a7\u5236\u56de\u8def\u4e2d\uff0c\u7528\u4e8e\u5728\u8fc7\u8f7d\u6216\u624b\u52a8\u8df3\u95f8\u65f6\u5207\u65ad\u63a5\u89e6\u5668\u7ebf\u5708\u7535\u6e90\u3002",
                    relay,
                    TerminalConstants.ThermalNC95,
                    TerminalConstants.ThermalNC96);
            }

            if (scopeHelper.HasTraversalLimitExceeded)
            {
                AddComplexTopologyIssue(report);
            }
        }

        private static void AddStopButtonBypassedIssues(
            CircuitValidationReport report,
            IReadOnlyList<CircuitComponent> components,
            CircuitStateResult analysisResult)
        {
            if (report == null || components == null)
            {
                return;
            }

            var stopButtonCount = 0;
            CircuitComponent stopButton = null;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsPureStopButtonCandidate(component))
                {
                    continue;
                }

                stopButtonCount++;
                stopButton = component;
            }

            // Multiple stop buttons need a later StopButton -> Coil control-scope model.
            if (stopButtonCount != 1 || stopButton == null || stopButton.IsClosed)
            {
                return;
            }

            var energizedCoilCount = 0;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsControlCoilCandidate(component) ||
                    !IsControlCoilEnergized(component, analysisResult))
                {
                    continue;
                }

                energizedCoilCount++;
            }

            if (energizedCoilCount <= 0)
            {
                return;
            }

            AddIssue(
                report,
                "STOP_BUTTON_BYPASSED",
                CircuitValidationSeverity.Error,
                CircuitValidationCategory.ControlCircuit,
                "\u505c\u6b62\u6309\u94ae\u88ab\u65c1\u8def",
                "\u505c\u6b62\u6309\u94ae\u5904\u4e8e\u65ad\u5f00\u72b6\u6001\uff0c\u4f46\u63a7\u5236\u7ebf\u5708\u4ecd\u7136\u5f97\u7535\uff0c\u53ef\u80fd\u5b58\u5728\u8de8\u63a5\u7ebf\u7ed5\u8fc7\u505c\u6b62\u6309\u94ae\u6216\u505c\u6b62\u6309\u94ae\u672a\u6709\u6548\u4e32\u5165\u63a7\u5236\u56de\u8def\u3002\u505c\u6b62\u6309\u94ae\u901a\u5e38\u5e94\u4e32\u8054\u5728\u63a7\u5236\u56de\u8def\u4e2d\uff0c\u7528\u4e8e\u5207\u65ad\u63a5\u89e6\u5668\u6216\u7ee7\u7535\u5668\u7ebf\u5708\u7535\u6e90\u3002",
                stopButton,
                "11",
                "12");
        }

        private static bool IsPureStopButtonCandidate(CircuitComponent component)
        {
            if (component == null ||
                component.Definition == null ||
                component.Definition.kind != ComponentKind.PushButton)
            {
                return false;
            }

            if (!string.Equals(component.Definition.name, "Button_Stop_NC", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return component.GetTerminal(TerminalConstants.AuxNC21) == null &&
                component.GetTerminal(TerminalConstants.AuxNC22) == null &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") == null &&
                component.GetTerminal("24") == null;
        }

        private static bool IsControlCoilCandidate(CircuitComponent component)
        {
            if (component == null ||
                component.Definition == null ||
                component.Definition.kind != ComponentKind.ContactorCoil)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definitionName, "Timer_OnDelay_220V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definitionName, "Timer_OnDelay_380V", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsControlCoilEnergized(
            CircuitComponent component,
            CircuitStateResult analysisResult)
        {
            var info = FindComponentInfo(analysisResult, component != null ? component.InstanceId : null);
            if (info != null)
            {
                if (info.IsContactor && info.IsContactorCoilEnergizedByAnalyzer)
                {
                    return true;
                }

                if (info.IsTimerRelay && info.IsTimerRelayCoilEnergizedByAnalyzer)
                {
                    return true;
                }

                if (string.Equals(info.CoilStatus, "CoilEnergized", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return component != null && component.IsEnergized;
        }

        private static ComponentStateInfo FindComponentInfo(
            CircuitStateResult analysisResult,
            string instanceId)
        {
            if (analysisResult == null ||
                analysisResult.Components == null ||
                string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            for (var i = 0; i < analysisResult.Components.Count; i++)
            {
                var info = analysisResult.Components[i];
                if (info != null && string.Equals(info.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return null;
        }

        private static CircuitComponent FindComponent(IReadOnlyList<CircuitComponent> components, string instanceId)
        {
            if (components == null || string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component != null && string.Equals(component.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }

            return null;
        }

        private static void AddIssue(
            CircuitValidationReport report,
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component,
            params string[] relatedTerminals)
        {
            // 以 RuleId 和元件实例去重，避免多个 Helper 对同一证据重复报错；不要在这里改变
            // Severity 或 Category，否则会破坏 Validation 与 Inspector 的稳定快照契约。
            if (report == null || HasIssue(report, ruleId, component))
            {
                return;
            }

            var issue = new CircuitValidationIssue
            {
                RuleId = ruleId,
                Severity = severity,
                Category = category,
                Title = title,
                Message = message,
                Component = component
            };

            if (relatedTerminals != null)
            {
                for (var i = 0; i < relatedTerminals.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(relatedTerminals[i]))
                    {
                        issue.RelatedTerminals.Add(relatedTerminals[i]);
                    }
                }
            }

            report.Issues.Add(issue);
        }

        private static void AddIssue(CircuitValidationReport report, CircuitValidationIssue issue)
        {
            if (report == null || issue == null || HasIssue(report, issue.RuleId, issue.Component))
            {
                return;
            }

            report.Issues.Add(issue);
        }

        private static void AddComplexTopologyIssue(CircuitValidationReport report)
        {
            AddIssue(
                report,
                TopologyTraversalLimits.ComplexTopologyRuleId,
                CircuitValidationSeverity.Warning,
                CircuitValidationCategory.General,
                TopologyTraversalLimits.ComplexTopologyTitle,
                TopologyTraversalLimits.ComplexTopologyMessage,
                null);
        }

        private static bool HasIssue(CircuitValidationReport report, string ruleId, CircuitComponent component)
        {
            var instanceId = component != null ? component.InstanceId : string.Empty;
            for (var i = 0; i < report.Issues.Count; i++)
            {
                var issue = report.Issues[i];
                var issueInstanceId = issue != null && issue.Component != null ? issue.Component.InstanceId : string.Empty;
                if (issue != null &&
                    string.Equals(issue.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issueInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
