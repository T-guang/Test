using System;
using System.Collections.Generic;

namespace ElectricalSim.Core.Validation
{
    /// <summary>
    /// 基于有界静态导线图和 Analyzer 证据检测保护路径与互锁旁路。
    /// 仅产出问题，聚合由 CircuitValidationService 负责；调用方不得借此修改接线。
    /// 遍历超限时必须保守处理，不能静默相信环路结果。
    /// </summary>
    internal sealed class ProtectionBypassValidationHelper
    {
        private const string BreakerOrFuseBypassed = "BREAKER_OR_FUSE_BYPASSED";
        private const string MotorContactorBypassed = "MOTOR_CONTACTOR_BYPASSED";
        private const string ReversingInterlockMissing = "REVERSING_INTERLOCK_MISSING";
        private const string ThermalRelayMainCircuitBypassed = "THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED";

        private readonly IReadOnlyList<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly CircuitStateResult analysisResult;
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public ProtectionBypassValidationHelper(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            this.components = components;
            this.wires = wires;
            this.analysisResult = analysisResult;
            BuildStaticWireGraph();
        }

        public List<CircuitValidationIssue> Validate()
        {
            var issues = new List<CircuitValidationIssue>();
            AddBreakerOrFuseBypassedIssues(issues);
            AddMotorContactorBypassedIssues(issues);
            AddThermalRelayMainCircuitBypassedIssues(issues);
            AddReversingInterlockMissingIssues(issues);
            return issues;
        }

        private void AddBreakerOrFuseBypassedIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null || analysisResult == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var protection = components[i];
                if (!IsProtectionDevice(protection) || !IsProtectionDeviceOpen(protection))
                {
                    continue;
                }

                var outputs = GetProtectionOutputTerminals(protection);
                for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    var output = outputs[outputIndex];
                    if (!IsTerminalLiveByAnalysis(output))
                    {
                        continue;
                    }

                    AddIssue(
                        issues,
                        BreakerOrFuseBypassed,
                        CircuitValidationSeverity.Error,
                        CircuitValidationCategory.Protection,
                        "保护器件被旁路",
                        "检测到断路器或熔断器处于断开状态，但其下游输出端仍然带电，可能存在跨接线绕过保护器件的接线风险。请检查电源是否必须先经过空开、熔断器等保护元件后再进入负载或控制回路。",
                        protection,
                        TerminalId(output));
                    break;
                }
            }
        }

        private void AddMotorContactorBypassedIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null || !HasSupportedContactorStructure())
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var motor = components[i];
                if (!IsSupportedOrdinaryThreePhaseMotor(motor))
                {
                    continue;
                }

                if (!IsMotorDirectlyConnectedToThreePhasePower(motor))
                {
                    continue;
                }

                AddIssue(
                    issues,
                    MotorContactorBypassed,
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.PowerSafety,
                    "电机绕过接触器直接得电",
                    "检测到三相电机 U/V/W 端子通过导线直接接入三相电源，未经过可识别的接触器主触点控制。工业电机控制练习中，电机主回路通常应经接触器主触点和保护元件后供电，请检查是否存在绕过 KM 主触点的跨接线。",
                    motor,
                    TerminalConstants.U,
                    TerminalConstants.V,
                    TerminalConstants.W);
            }
        }

        private void AddThermalRelayMainCircuitBypassedIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var relay = components[i];
                if (!IsSupportedThermalRelay(relay))
                {
                    continue;
                }

                if (!TryFindUniqueUpstreamContactorForRelay(relay, out var upstreamContactor))
                {
                    continue;
                }

                if (!TryFindUniqueBypassedMotor(relay, upstreamContactor, out var motor))
                {
                    continue;
                }

                AddIssue(
                    issues,
                    ThermalRelayMainCircuitBypassed,
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.Protection,
                    "热继主回路被旁路",
                    "检测到热继主回路可能被绕过。当前电机主回路未可靠经过热继 FR 的 L1/L2/L3 -> T1/T2/T3 保护路径，热继可能无法对电机过载起保护作用。请检查电机三相主回路是否经过热继输出端。",
                    relay,
                    TerminalConstants.L1,
                    TerminalConstants.L2,
                    TerminalConstants.L3,
                    TerminalConstants.T1,
                    TerminalConstants.T2,
                    TerminalConstants.T3);
            }
        }

        private void AddReversingInterlockMissingIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null)
            {
                return;
            }

            var scopes = ReversingPairScopeHelper.ResolveReliableReversingPairs(components, wires, out var traversalLimitExceeded);
            if (traversalLimitExceeded)
            {
                MarkTraversalLimitExceeded("ProtectionBypassValidationHelper.ReversingPairScope");
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

                if (HasElectricalInterlock(scope) || HasButtonInterlock(scope))
                {
                    continue;
                }

                var issue = CreateIssue(
                    ReversingInterlockMissing,
                    CircuitValidationSeverity.Warning,
                    CircuitValidationCategory.Interlock,
                    "正反转互锁缺失",
                    "检测到同一台三相电机的正转、反转接触器 pair，但未识别到 21/22 电气互锁或可确认的按钮互锁。正反转控制中建议使用互锁保护，避免两个接触器同时吸合造成相序冲突或短路风险。该提示不替代动态互锁冲突检测。",
                    scope.ForwardContactor,
                    TerminalConstants.AuxNC21,
                    TerminalConstants.AuxNC22,
                    TerminalConstants.A1,
                    TerminalConstants.A2);
                issue.RelatedComponents.Add(scope.ReverseContactor);
                if (scope.Motor != null)
                {
                    issue.RelatedComponents.Add(scope.Motor);
                }

                issues.Add(issue);
            }
        }

        private bool IsMotorDirectlyConnectedToThreePhasePower(CircuitComponent motor)
        {
            var phases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDirectPowerPhase(motor.GetTerminal(TerminalConstants.U), phases);
            AddDirectPowerPhase(motor.GetTerminal(TerminalConstants.V), phases);
            AddDirectPowerPhase(motor.GetTerminal(TerminalConstants.W), phases);
            return phases.Contains(TerminalConstants.L1) &&
                phases.Contains(TerminalConstants.L2) &&
                phases.Contains(TerminalConstants.L3);
        }

        private bool TryFindUniqueUpstreamContactorForRelay(CircuitComponent relay, out CircuitComponent contactor)
        {
            contactor = null;
            var count = 0;
            if (components == null)
            {
                return false;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var candidate = components[i];
                if (!IsSupportedContactor(candidate) ||
                    !AreContactorOutputsConnectedToRelayInputs(candidate, relay))
                {
                    continue;
                }

                count++;
                contactor = candidate;
            }

            if (count == 1)
            {
                return true;
            }

            contactor = null;
            return false;
        }

        private bool TryFindUniqueBypassedMotor(CircuitComponent relay, CircuitComponent upstreamContactor, out CircuitComponent motor)
        {
            motor = null;
            var count = 0;
            if (components == null || relay == null || upstreamContactor == null)
            {
                return false;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var candidate = components[i];
                if (!IsSupportedOrdinaryThreePhaseMotor(candidate) ||
                    AreRelayOutputsConnectedToMotor(relay, candidate))
                {
                    continue;
                }

                if (!AreContactorOutputsConnectedToMotor(upstreamContactor, candidate) &&
                    !IsMotorDirectlyConnectedToThreePhasePower(candidate))
                {
                    continue;
                }

                count++;
                motor = candidate;
            }

            if (count == 1)
            {
                return true;
            }

            motor = null;
            return false;
        }

        private bool AreContactorOutputsConnectedToRelayInputs(CircuitComponent contactor, CircuitComponent relay)
        {
            return AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T1), relay.GetTerminal(TerminalConstants.L1)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T2), relay.GetTerminal(TerminalConstants.L2)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T3), relay.GetTerminal(TerminalConstants.L3));
        }

        private bool AreRelayOutputsConnectedToMotor(CircuitComponent relay, CircuitComponent motor)
        {
            return AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T1), motor.GetTerminal(TerminalConstants.U)) &&
                AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T2), motor.GetTerminal(TerminalConstants.V)) &&
                AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T3), motor.GetTerminal(TerminalConstants.W));
        }

        private bool AreContactorOutputsConnectedToMotor(CircuitComponent contactor, CircuitComponent motor)
        {
            return AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T1), motor.GetTerminal(TerminalConstants.U)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T2), motor.GetTerminal(TerminalConstants.V)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T3), motor.GetTerminal(TerminalConstants.W));
        }

        private void AddDirectPowerPhase(TerminalView terminal, HashSet<string> phases)
        {
            if (terminal == null || phases == null)
            {
                return;
            }

            var group = FloodStaticWireGroup(terminal);
            for (var i = 0; i < group.Count; i++)
            {
                var potential = ResolvePowerOutputPotential(group[i]);
                if (IsThreePhaseLive(potential))
                {
                    phases.Add(potential);
                }
            }
        }

        private bool HasElectricalInterlock(ReversingPairScope scope)
        {
            return IsNcContactInOtherCoilPath(scope.ReverseContactor, scope.ForwardContactor) &&
                IsNcContactInOtherCoilPath(scope.ForwardContactor, scope.ReverseContactor);
        }

        private bool IsNcContactInOtherCoilPath(CircuitComponent ncOwner, CircuitComponent coilOwner)
        {
            if (ncOwner == null || coilOwner == null)
            {
                return false;
            }

            var nc21 = ncOwner.GetTerminal(TerminalConstants.AuxNC21);
            var nc22 = ncOwner.GetTerminal(TerminalConstants.AuxNC22);
            var a1 = coilOwner.GetTerminal(TerminalConstants.A1);
            var a2 = coilOwner.GetTerminal(TerminalConstants.A2);
            return AreConnectedByStaticWires(nc21, a1) ||
                AreConnectedByStaticWires(nc21, a2) ||
                AreConnectedByStaticWires(nc22, a1) ||
                AreConnectedByStaticWires(nc22, a2) ||
                IsNcContactBeforeStartButton(nc21, coilOwner) ||
                IsNcContactBeforeStartButton(nc22, coilOwner);
        }

        private bool IsNcContactBeforeStartButton(TerminalView ncTerminal, CircuitComponent coilOwner)
        {
            if (ncTerminal == null || coilOwner == null || components == null)
            {
                return false;
            }

            var a1 = coilOwner.GetTerminal(TerminalConstants.A1);
            var a2 = coilOwner.GetTerminal(TerminalConstants.A2);
            for (var i = 0; i < components.Count; i++)
            {
                var button = components[i];
                if (button == null ||
                    button.Definition == null ||
                    button.Definition.kind != ComponentKind.PushButton ||
                    button.GetTerminal("23") == null ||
                    button.GetTerminal("24") == null)
                {
                    continue;
                }

                var no23 = button.GetTerminal("23");
                var no24 = button.GetTerminal("24");
                if ((AreConnectedByStaticWires(ncTerminal, no23) &&
                     (AreConnectedByStaticWires(no24, a1) || AreConnectedByStaticWires(no24, a2))) ||
                    (AreConnectedByStaticWires(ncTerminal, no24) &&
                     (AreConnectedByStaticWires(no23, a1) || AreConnectedByStaticWires(no23, a2))))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasButtonInterlock(ReversingPairScope scope)
        {
            if (components == null || scope == null)
            {
                return false;
            }

            return HasCompoundButtonNcInCoilPath(scope.ForwardContactor) &&
                HasCompoundButtonNcInCoilPath(scope.ReverseContactor);
        }

        private bool HasCompoundButtonNcInCoilPath(CircuitComponent coilOwner)
        {
            if (coilOwner == null)
            {
                return false;
            }

            var a1 = coilOwner.GetTerminal(TerminalConstants.A1);
            var a2 = coilOwner.GetTerminal(TerminalConstants.A2);
            for (var i = 0; i < components.Count; i++)
            {
                var button = components[i];
                if (!IsCompoundButton(button))
                {
                    continue;
                }

                var nc11 = button.GetTerminal("11");
                var nc12 = button.GetTerminal("12");
                if (AreConnectedByStaticWires(nc11, a1) ||
                    AreConnectedByStaticWires(nc11, a2) ||
                    AreConnectedByStaticWires(nc12, a1) ||
                    AreConnectedByStaticWires(nc12, a2))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsTerminalLiveByAnalysis(TerminalView terminal)
        {
            if (terminal == null || terminal.Owner == null)
            {
                return false;
            }

            var info = FindComponentInfo(terminal.Owner.InstanceId);
            if (info == null || info.TerminalVoltages == null)
            {
                return false;
            }

            return info.TerminalVoltages.TryGetValue(TerminalId(terminal), out var voltage) && IsLiveVoltage(voltage);
        }

        private ComponentStateInfo FindComponentInfo(string instanceId)
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

        private List<TerminalView> FloodStaticWireGroup(TerminalView start)
        {
            var group = new List<TerminalView>();
            if (start == null || !staticWireGraph.ContainsKey(start))
            {
                return group;
            }

            var visited = new HashSet<TerminalView>();
            var queue = new Queue<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("ProtectionBypassValidationHelper.FloodStaticWireGroup");
                    return group;
                }

                var current = queue.Dequeue();
                group.Add(current);
                if (!staticWireGraph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("ProtectionBypassValidationHelper.FloodStaticWireGroup.edges");
                        return group;
                    }

                    if (terminal != null && visited.Add(terminal))
                    {
                        queue.Enqueue(terminal);
                    }
                }
            }

            return group;
        }

        private bool AreConnectedByStaticWires(TerminalView first, TerminalView second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (first == second)
            {
                return true;
            }

            var group = FloodStaticWireGroup(first);
            for (var i = 0; i < group.Count; i++)
            {
                if (group[i] == second)
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildStaticWireGraph()
        {
            staticWireGraph.Clear();
            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    if (component == null)
                    {
                        continue;
                    }

                    foreach (var terminal in component.Terminals)
                    {
                        EnsureStatic(terminal);
                    }
                }
            }

            if (wires == null)
            {
                return;
            }

            for (var i = 0; i < wires.Count; i++)
            {
                var wire = wires[i];
                if (wire != null)
                {
                    ConnectStatic(wire.StartTerminal, wire.EndTerminal);
                }
            }
        }

        private void EnsureStatic(TerminalView terminal)
        {
            if (terminal != null && !staticWireGraph.ContainsKey(terminal))
            {
                staticWireGraph[terminal] = new HashSet<TerminalView>();
            }
        }

        private void ConnectStatic(TerminalView first, TerminalView second)
        {
            if (first == null || second == null)
            {
                return;
            }

            EnsureStatic(first);
            EnsureStatic(second);
            staticWireGraph[first].Add(second);
            staticWireGraph[second].Add(first);
        }

        private static List<TerminalView> GetProtectionOutputTerminals(CircuitComponent component)
        {
            var outputs = new List<TerminalView>();
            if (component == null)
            {
                return outputs;
            }

            AddIfExists(component, outputs, TerminalConstants.T1);
            AddIfExists(component, outputs, TerminalConstants.T2);
            AddIfExists(component, outputs, TerminalConstants.T3);
            AddIfExists(component, outputs, "T");
            AddIfExists(component, outputs, "OUT");
            AddIfExists(component, outputs, "L_OUT");
            AddIfExists(component, outputs, "N_OUT");
            AddIfExists(component, outputs, "2");
            AddIfExists(component, outputs, "4");
            AddIfExists(component, outputs, "6");

            foreach (var terminal in component.Terminals)
            {
                if (terminal != null && terminal.Role == TerminalRole.Output && !outputs.Contains(terminal))
                {
                    outputs.Add(terminal);
                }
            }

            return outputs;
        }

        private static void AddIfExists(CircuitComponent component, List<TerminalView> terminals, string terminalId)
        {
            var terminal = component != null ? component.GetTerminal(terminalId) : null;
            if (terminal != null && !terminals.Contains(terminal))
            {
                terminals.Add(terminal);
            }
        }

        private bool HasSupportedContactorStructure()
        {
            if (components == null)
            {
                return false;
            }

            for (var i = 0; i < components.Count; i++)
            {
                if (IsSupportedContactor(components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsProtectionDevice(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            return component.Definition.kind == ComponentKind.Breaker ||
                component.Definition.kind == ComponentKind.Fuse ||
                DefinitionContains(component, "Breaker", "AirSwitch", "CircuitBreaker", "Fuse", "空开", "空气开关", "断路器", "熔断器", "保险");
        }

        private static bool IsProtectionDeviceOpen(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            if (component.Definition.kind == ComponentKind.Fuse && !component.Definition.togglable)
            {
                return false;
            }

            return !component.IsClosed;
        }

        private static bool IsSupportedOrdinaryThreePhaseMotor(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                string.Equals(component.Definition.name, "Motor_ThreePhase_380V", StringComparison.OrdinalIgnoreCase) &&
                component.GetTerminal(TerminalConstants.U) != null &&
                component.GetTerminal(TerminalConstants.V) != null &&
                component.GetTerminal(TerminalConstants.W) != null &&
                component.GetTerminal(TerminalConstants.U1) == null;
        }

        private static bool IsSupportedThermalRelay(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            return string.Equals(component.Definition.name, "ThermalRelay_FR_380V", StringComparison.OrdinalIgnoreCase) &&
                component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.L2) != null &&
                component.GetTerminal(TerminalConstants.L3) != null &&
                component.GetTerminal(TerminalConstants.T1) != null &&
                component.GetTerminal(TerminalConstants.T2) != null &&
                component.GetTerminal(TerminalConstants.T3) != null;
        }

        private static bool IsSupportedContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return (string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase)) &&
                component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.L2) != null &&
                component.GetTerminal(TerminalConstants.L3) != null &&
                component.GetTerminal(TerminalConstants.T1) != null &&
                component.GetTerminal(TerminalConstants.T2) != null &&
                component.GetTerminal(TerminalConstants.T3) != null;
        }

        private static bool IsCompoundButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.PushButton &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null;
        }

        private static string ResolvePowerOutputPotential(TerminalView terminal)
        {
            if (terminal == null || terminal.Owner == null || terminal.Owner.Definition == null)
            {
                return string.Empty;
            }

            var kind = terminal.Owner.Definition.kind;
            if (kind != ComponentKind.PowerSource && kind != ComponentKind.EnergyMeter)
            {
                return string.Empty;
            }

            var id = TerminalId(terminal);
            if (EqualsId(id, TerminalConstants.L1) ||
                EqualsId(id, TerminalConstants.L2) ||
                EqualsId(id, TerminalConstants.L3))
            {
                return id.ToUpperInvariant();
            }

            return string.Empty;
        }

        private static bool IsThreePhaseLive(string potential)
        {
            return EqualsId(potential, TerminalConstants.L1) ||
                EqualsId(potential, TerminalConstants.L2) ||
                EqualsId(potential, TerminalConstants.L3);
        }

        private static bool IsLiveVoltage(string voltage)
        {
            return string.Equals(voltage, CircuitStateAnalyzer.VoltageL, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voltage, CircuitStateAnalyzer.VoltageL1, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voltage, CircuitStateAnalyzer.VoltageL2, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voltage, CircuitStateAnalyzer.VoltageL3, StringComparison.OrdinalIgnoreCase);
        }

        private static bool DefinitionContains(CircuitComponent component, params string[] tokens)
        {
            var definitionName = component != null && component.Definition != null ? component.Definition.name ?? string.Empty : string.Empty;
            var displayName = component != null && component.Definition != null ? component.Definition.displayName ?? string.Empty : string.Empty;
            for (var i = 0; i < tokens.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(tokens[i]))
                {
                    continue;
                }

                if (definitionName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0 ||
                    displayName.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static CircuitValidationIssue CreateIssue(
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component,
            params string[] relatedTerminals)
        {
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
                    if (!string.IsNullOrWhiteSpace(relatedTerminals[i]) && !issue.RelatedTerminals.Contains(relatedTerminals[i]))
                    {
                        issue.RelatedTerminals.Add(relatedTerminals[i]);
                    }
                }
            }

            return issue;
        }

        private static void AddIssue(
            List<CircuitValidationIssue> issues,
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component,
            params string[] relatedTerminals)
        {
            if (issues == null || HasIssue(issues, ruleId, component))
            {
                return;
            }

            issues.Add(CreateIssue(ruleId, severity, category, title, message, component, relatedTerminals));
        }

        private static bool HasIssue(List<CircuitValidationIssue> issues, string ruleId, CircuitComponent component)
        {
            var instanceId = component != null ? component.InstanceId : string.Empty;
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
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

        private void MarkTraversalLimitExceeded(string context)
        {
            if (!HasTraversalLimitExceeded)
            {
                TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
            }

            HasTraversalLimitExceeded = true;
        }

        private static string TerminalId(TerminalView terminal)
        {
            return terminal != null ? terminal.TerminalId ?? string.Empty : string.Empty;
        }

        private static bool EqualsId(string first, string second)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }
}
