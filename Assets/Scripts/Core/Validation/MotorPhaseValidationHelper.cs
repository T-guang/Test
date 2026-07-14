using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    /// <summary>
    /// 基于活动导线与已建内部连接图校验三相电机端子的相线证据。
    /// 动态图用于反映当前可导通路径，静态图用于区分既有接线；本类只提供规则证据，不修改电机运行态或 UI。
    /// 遍历受拓扑预算保护，预算耗尽时调用方必须保守处理并运行拓扑安全测试与电机、星三角负向用例。
    /// </summary>
    internal sealed class MotorPhaseValidationHelper
    {
        private readonly IReadOnlyList<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> graph = new Dictionary<TerminalView, HashSet<TerminalView>>();
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph = new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public MotorPhaseValidationHelper(IReadOnlyList<CircuitComponent> components, IReadOnlyList<WireView> wires)
        {
            this.components = components;
            this.wires = wires;
            BuildStaticWireGraph();
            BuildGraph();
        }

        public MotorPhaseValidationResult Validate(
            CircuitComponent motor,
            ComponentStateInfo info,
            bool starDelta)
        {
            // 普通三相电机使用 U/V/W，星三角电机在本规则中以 U1/V1/W1 作为相线入口；
            // 不可把两种端子结构混为一谈，否则正常星点或三角支路会被误判。
            var result = new MotorPhaseValidationResult
            {
                Motor = motor,
                SourceDescription = "当前导通路径相位诊断"
            };

            if (motor == null)
            {
                return result;
            }

            var terminals = starDelta
                ? new[] { TerminalConstants.U1, TerminalConstants.V1, TerminalConstants.W1 }
                : new[] { TerminalConstants.U, TerminalConstants.V, TerminalConstants.W };

            var phaseCounts = new Dictionary<string, int>();
            var hasAnyPhaseOrConflict = false;
            for (var i = 0; i < terminals.Length; i++)
            {
                var terminalId = terminals[i];
                var phase = ResolveTerminalPhase(motor.GetTerminal(terminalId));
                result.TerminalPhaseMap[terminalId] = phase;

                if (IsThreePhaseLine(phase))
                {
                    hasAnyPhaseOrConflict = true;
                    phaseCounts.TryGetValue(phase, out var count);
                    phaseCounts[phase] = count + 1;
                }
                else
                {
                    if (phase == CircuitStateAnalyzer.VoltageConflict)
                    {
                        hasAnyPhaseOrConflict = true;
                    }

                    result.MissingTerminals.Add(terminalId);
                }
            }

            foreach (var pair in phaseCounts)
            {
                if (pair.Value > 1)
                {
                    result.DuplicatePhases.Add(pair.Key);
                }
            }

            result.HasDuplicatePhase = result.DuplicatePhases.Count > 0;
            result.HasMissingPhase = result.MissingTerminals.Count > 0 ||
                (!result.HasDuplicatePhase && phaseCounts.Count > 0 && phaseCounts.Count < 3);
            result.ShouldEvaluate = ShouldEvaluateMotor(info, motor, hasAnyPhaseOrConflict, result);
            return result;
        }

        private static bool ShouldEvaluateMotor(
            ComponentStateInfo info,
            CircuitComponent motor,
            bool hasAnyPhaseOrConflict,
            MotorPhaseValidationResult result)
        {
            // 已得电、已有相线冲突或 Analyzer 明确故障时才强制评价。接触器输出尚未建立的停止电机
            // 缺少相线证据是常见正常状态，应保守跳过，避免把未运行模板当作缺相故障。
            if (motor != null && motor.IsEnergized)
            {
                return true;
            }

            if (hasAnyPhaseOrConflict)
            {
                return true;
            }

            if (info == null)
            {
                return false;
            }

            if (info.IsMotorDeferredByContactorOutput)
            {
                return false;
            }

            if (string.Equals(info.State, "Fault", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.State, "StarDeltaConflict", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(info.State, "Stopped", StringComparison.OrdinalIgnoreCase) &&
                result != null &&
                (result.MissingTerminals.Count > 0 || result.DuplicatePhases.Count > 0))
            {
                return HasMotorIssue(info);
            }

            return false;
        }

        private static bool HasMotorIssue(ComponentStateInfo info)
        {
            return info != null &&
                (!string.IsNullOrWhiteSpace(info.Judgement) ||
                (info.MotorIssues != null && info.MotorIssues.Count > 0));
        }

        public bool AreTerminalsConnected(CircuitComponent component, string firstTerminalId, string secondTerminalId)
        {
            return AreTerminalsConnected(component, firstTerminalId, component, secondTerminalId);
        }

        public bool AreTerminalsConnected(
            CircuitComponent firstComponent,
            string firstTerminalId,
            CircuitComponent secondComponent,
            string secondTerminalId)
        {
            if (firstComponent == null || secondComponent == null ||
                string.IsNullOrWhiteSpace(firstTerminalId) ||
                string.IsNullOrWhiteSpace(secondTerminalId))
            {
                return false;
            }

            var first = firstComponent.GetTerminal(firstTerminalId);
            var second = secondComponent.GetTerminal(secondTerminalId);
            return first != null && second != null && Flood(first).Contains(second);
        }

        public bool AreTerminalsLocallyConnected(CircuitComponent component, string firstTerminalId, string secondTerminalId)
        {
            if (component == null ||
                string.IsNullOrWhiteSpace(firstTerminalId) ||
                string.IsNullOrWhiteSpace(secondTerminalId))
            {
                return false;
            }

            var first = component.GetTerminal(firstTerminalId);
            var second = component.GetTerminal(secondTerminalId);
            return first != null && second != null && FloodWithoutSupplyTerminals(first).Contains(second);
        }

        public bool AreTerminalsDirectlyWired(CircuitComponent component, string firstTerminalId, string secondTerminalId)
        {
            if (component == null ||
                string.IsNullOrWhiteSpace(firstTerminalId) ||
                string.IsNullOrWhiteSpace(secondTerminalId))
            {
                return false;
            }

            var first = component.GetTerminal(firstTerminalId);
            var second = component.GetTerminal(secondTerminalId);
            return first != null && second != null && FloodStaticWires(first).Contains(second);
        }

        private string ResolveTerminalPhase(TerminalView terminal)
        {
            if (terminal == null)
            {
                return CircuitStateAnalyzer.VoltageNone;
            }

            var phases = new HashSet<string>();
            foreach (var reachable in Flood(terminal))
            {
                if (IsPowerPhaseTerminal(reachable))
                {
                    var phase = NormalizePhaseLabel(reachable.TerminalId);
                    if (!string.IsNullOrWhiteSpace(phase))
                    {
                        phases.Add(phase);
                    }
                }
            }

            if (phases.Count == 0)
            {
                return CircuitStateAnalyzer.VoltageNone;
            }

            if (phases.Count > 1)
            {
                return CircuitStateAnalyzer.VoltageConflict;
            }

            foreach (var phase in phases)
            {
                return phase;
            }

            return CircuitStateAnalyzer.VoltageNone;
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

        private void BuildGraph()
        {
            graph.Clear();
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
                        Ensure(terminal);
                    }
                }
            }

            if (wires != null)
            {
                for (var i = 0; i < wires.Count; i++)
                {
                    var wire = wires[i];
                    if (wire != null)
                    {
                        Connect(wire.StartTerminal, wire.EndTerminal);
                    }
                }
            }

            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    AddInternalConnections(components[i]);
                }
            }
        }

        private void AddInternalConnections(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return;
            }

            if (IsThermalRelay(component))
            {
                ConnectById(component, TerminalConstants.L1, TerminalConstants.T1);
                ConnectById(component, TerminalConstants.L2, TerminalConstants.T2);
                ConnectById(component, TerminalConstants.L3, TerminalConstants.T3);

                if (component.IsClosed)
                {
                    ConnectById(component, TerminalConstants.ThermalNC95, TerminalConstants.ThermalNC96);
                }
                else
                {
                    ConnectById(component, TerminalConstants.ThermalNO97, TerminalConstants.ThermalNO98);
                }

                return;
            }

            if (IsLimitSwitch(component))
            {
                if (component.IsClosed)
                {
                    ConnectById(component, "23", "24");
                }
                else
                {
                    ConnectById(component, "11", "12");
                }

                return;
            }

            if (IsCompoundPushButton(component))
            {
                if (component.IsClosed)
                {
                    ConnectById(component, "23", "24");
                }
                else
                {
                    ConnectById(component, "11", "12");
                }

                return;
            }

            if (IsSelfLockingButton(component))
            {
                if (component.IsClosed)
                {
                    ConnectById(component, "23", "24");
                }
                else
                {
                    ConnectById(component, "11", "12");
                }

                return;
            }

            if (IsOnDelayTimer(component))
            {
                if (IsOnDelayTimerElapsed(component))
                {
                    ConnectById(component, TerminalConstants.TimerCommon15, TerminalConstants.TimerNO18);
                }
                else
                {
                    ConnectById(component, TerminalConstants.TimerCommon15, TerminalConstants.TimerNC16);
                }

                return;
            }

            if (IsTimerRelay(component))
            {
                return;
            }

            switch (component.Definition.kind)
            {
                case ComponentKind.TwoWaySwitch:
                    ConnectTwoWaySwitch(component);
                    break;
                case ComponentKind.EnergyMeter:
                    ConnectPairs(component, true);
                    break;
                case ComponentKind.ContactorCoil:
                    if (component.IsEnergized)
                    {
                        ConnectById(component, TerminalConstants.L1, TerminalConstants.T1);
                        ConnectById(component, TerminalConstants.L2, TerminalConstants.T2);
                        ConnectById(component, TerminalConstants.L3, TerminalConstants.T3);
                        ConnectById(component, TerminalConstants.AuxNO13, TerminalConstants.AuxNO14);
                    }
                    else
                    {
                        ConnectById(component, TerminalConstants.AuxNC21, TerminalConstants.AuxNC22);
                    }
                    break;
                case ComponentKind.Switch:
                case ComponentKind.PushButton:
                case ComponentKind.Fuse:
                case ComponentKind.Breaker:
                    ConnectPairs(component, component.IsClosed);
                    break;
                case ComponentKind.TerminalBlock:
                    ConnectPairs(component, true);
                    break;
            }
        }

        private void ConnectTwoWaySwitch(CircuitComponent component)
        {
            var terms = new List<TerminalView>(component.Terminals);
            if (terms.Count >= 3)
            {
                Connect(terms[0], component.IsClosed ? terms[1] : terms[2]);
            }
        }

        private void ConnectPairs(CircuitComponent component, bool enabled)
        {
            if (!enabled || component == null)
            {
                return;
            }

            var terms = new List<TerminalView>(component.Terminals);
            for (var i = 0; i + 1 < terms.Count; i += 2)
            {
                Connect(terms[i], terms[i + 1]);
            }
        }

        private HashSet<TerminalView> Flood(TerminalView start)
        {
            var visited = new HashSet<TerminalView>();
            if (start == null || !graph.ContainsKey(start))
            {
                return visited;
            }

            var stack = new Stack<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            stack.Push(start);
            visited.Add(start);
            while (stack.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("MotorPhaseValidationHelper.Flood");
                    break;
                }

                var current = stack.Pop();
                if (!graph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("MotorPhaseValidationHelper.Flood.edges");
                        stack.Clear();
                        break;
                    }

                    if (terminal != null && visited.Add(terminal))
                    {
                        stack.Push(terminal);
                    }
                }
            }

            return visited;
        }

        private HashSet<TerminalView> FloodStaticWires(TerminalView start)
        {
            var visited = new HashSet<TerminalView>();
            if (start == null || !staticWireGraph.ContainsKey(start))
            {
                return visited;
            }

            var stack = new Stack<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            stack.Push(start);
            visited.Add(start);
            while (stack.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("MotorPhaseValidationHelper.FloodStaticWires");
                    break;
                }

                var current = stack.Pop();
                if (!staticWireGraph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("MotorPhaseValidationHelper.FloodStaticWires.edges");
                        stack.Clear();
                        break;
                    }

                    if (terminal != null && visited.Add(terminal))
                    {
                        stack.Push(terminal);
                    }
                }
            }

            return visited;
        }

        private HashSet<TerminalView> FloodWithoutSupplyTerminals(TerminalView start)
        {
            var visited = new HashSet<TerminalView>();
            if (start == null || !graph.ContainsKey(start))
            {
                return visited;
            }

            var stack = new Stack<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            stack.Push(start);
            visited.Add(start);
            while (stack.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("MotorPhaseValidationHelper.FloodWithoutSupplyTerminals");
                    break;
                }

                var current = stack.Pop();
                if (!graph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("MotorPhaseValidationHelper.FloodWithoutSupplyTerminals.edges");
                        stack.Clear();
                        break;
                    }

                    if (terminal == null || IsSupplyTerminal(terminal))
                    {
                        continue;
                    }

                    if (visited.Add(terminal))
                    {
                        stack.Push(terminal);
                    }
                }
            }

            return visited;
        }

        private void MarkTraversalLimitExceeded(string context)
        {
            if (!HasTraversalLimitExceeded)
            {
                TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
            }

            HasTraversalLimitExceeded = true;
        }

        private void Ensure(TerminalView terminal)
        {
            if (terminal != null && !graph.ContainsKey(terminal))
            {
                graph[terminal] = new HashSet<TerminalView>();
            }
        }

        private void EnsureStatic(TerminalView terminal)
        {
            if (terminal != null && !staticWireGraph.ContainsKey(terminal))
            {
                staticWireGraph[terminal] = new HashSet<TerminalView>();
            }
        }

        private void ConnectById(CircuitComponent component, string first, string second)
        {
            if (component == null)
            {
                return;
            }

            Connect(component.GetTerminal(first), component.GetTerminal(second));
        }

        private void Connect(TerminalView first, TerminalView second)
        {
            if (first == null || second == null)
            {
                return;
            }

            Ensure(first);
            Ensure(second);
            graph[first].Add(second);
            graph[second].Add(first);
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

        private static bool IsPowerPhaseTerminal(TerminalView terminal)
        {
            return terminal != null &&
                terminal.Role == TerminalRole.Phase &&
                terminal.Owner != null &&
                terminal.Owner.Definition != null &&
                (terminal.Owner.Definition.kind == ComponentKind.PowerSource ||
                terminal.Owner.Definition.kind == ComponentKind.EnergyMeter);
        }

        private static bool IsSupplyTerminal(TerminalView terminal)
        {
            return terminal != null &&
                terminal.Owner != null &&
                terminal.Owner.Definition != null &&
                (terminal.Owner.Definition.kind == ComponentKind.PowerSource ||
                terminal.Owner.Definition.kind == ComponentKind.EnergyMeter);
        }

        private static string NormalizePhaseLabel(string terminalId)
        {
            if (string.Equals(terminalId, TerminalConstants.L1, StringComparison.OrdinalIgnoreCase))
            {
                return CircuitStateAnalyzer.VoltageL1;
            }

            if (string.Equals(terminalId, TerminalConstants.L2, StringComparison.OrdinalIgnoreCase))
            {
                return CircuitStateAnalyzer.VoltageL2;
            }

            if (string.Equals(terminalId, TerminalConstants.L3, StringComparison.OrdinalIgnoreCase))
            {
                return CircuitStateAnalyzer.VoltageL3;
            }

            return string.Empty;
        }

        private static bool IsThreePhaseLine(string voltage)
        {
            return voltage == CircuitStateAnalyzer.VoltageL1 ||
                voltage == CircuitStateAnalyzer.VoltageL2 ||
                voltage == CircuitStateAnalyzer.VoltageL3;
        }

        private static bool IsThermalRelay(CircuitComponent component)
        {
            return component != null &&
                component.GetTerminal(TerminalConstants.ThermalNC95) != null &&
                component.GetTerminal(TerminalConstants.ThermalNC96) != null &&
                component.GetTerminal(TerminalConstants.ThermalNO97) != null &&
                component.GetTerminal(TerminalConstants.ThermalNO98) != null &&
                component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.T1) != null;
        }

        private static bool IsLimitSwitch(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                component.GetTerminal("11") == null ||
                component.GetTerminal("12") == null ||
                component.GetTerminal("23") == null ||
                component.GetTerminal("24") == null)
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("LimitSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("TravelSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("PositionSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("Switch_Limit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.Contains("行程开关") ||
                displayName.Contains("限位开关");
        }

        private static bool IsCompoundPushButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null &&
                component.Definition.name.IndexOf("Button_Compound", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSelfLockingButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.name.IndexOf("Button_SelfLock", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsOnDelayTimer(CircuitComponent component)
        {
            return IsTimerRelay(component) &&
                component.Definition.name.IndexOf("OnDelay", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTimerRelay(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                (component.GetTerminal(TerminalConstants.TimerCommon15) != null ||
                component.Definition.name.IndexOf("Timer_", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsOnDelayTimerElapsed(CircuitComponent component)
        {
            if (component == null)
            {
                return false;
            }

            return RuntimeStateManager.Shared.TryGetTimerState(component.InstanceId, out var state) &&
                state != null &&
                state.Phase == TimerRuntimePhase.Elapsed;
        }
    }
}
