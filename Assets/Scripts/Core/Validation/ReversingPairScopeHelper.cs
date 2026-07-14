using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    public sealed class ReversingPairScope
    {
        public ReversingPairScope(
            CircuitComponent forwardContactor,
            CircuitComponent reverseContactor,
            CircuitComponent motor,
            bool isReliable,
            string reason)
        {
            ForwardContactor = forwardContactor;
            ReverseContactor = reverseContactor;
            Motor = motor;
            IsReliable = isReliable;
            Reason = reason ?? string.Empty;
        }

        public CircuitComponent ForwardContactor { get; }
        public CircuitComponent ReverseContactor { get; }
        public CircuitComponent Motor { get; }
        public bool IsReliable { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 根据静态主回路连接为正反转规则解析接触器、电机的可靠作用域。
    /// 配对依据是到同一电机端子的三相映射，不能只凭 KM 名称；证据不唯一时宁可不返回作用域，避免跨电机误报。
    /// 本类不判断运行态冲突，修改后必须回归正反转互锁缺失、接触器冲突和多电机模板测试。
    /// </summary>
    public sealed class ReversingPairScopeHelper
    {
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public static IReadOnlyList<ReversingPairScope> ResolveReliableReversingPairs(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
        {
            return ResolveReliableReversingPairs(components, wires, out _);
        }

        public static IReadOnlyList<ReversingPairScope> ResolveReliableReversingPairs(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            out bool traversalLimitExceeded)
        {
            var helper = new ReversingPairScopeHelper();
            var scopes = helper.Resolve(components, wires);
            traversalLimitExceeded = helper.HasTraversalLimitExceeded;
            return scopes;
        }

        public static bool TryResolveSingleReversingPair(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            out ReversingPairScope scope)
        {
            var scopes = ResolveReliableReversingPairs(components, wires);
            if (scopes.Count == 1)
            {
                scope = scopes[0];
                return true;
            }

            scope = new ReversingPairScope(
                null,
                null,
                null,
                false,
                scopes.Count == 0
                    ? "No reliable reversing pair found."
                    : "Multiple reliable reversing pairs found.");
            return false;
        }

        private IReadOnlyList<ReversingPairScope> Resolve(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
        {
            // 仅使用静态主回路构建作用域，不能把当前接触器吸合状态混入配对依据；运行态冲突由独立规则消费该作用域。
            var scopes = new List<ReversingPairScope>();
            BuildStaticMainCircuitGraph(components, wires);

            if (components == null)
            {
                return scopes;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var motor = components[i];
                if (!IsSupportedThreePhaseMotor(motor))
                {
                    continue;
                }

                if (TryResolvePairForMotor(motor, components, out var scope))
                {
                    scopes.Add(scope);
                }
            }

            // 当前实现只把唯一、无歧义的正反转作用域视为可靠；多个候选可能属于不同电机或复杂拓扑，
            // 清空结果比错误配对更安全，调用方应保持保守不报。
            if (scopes.Count > 1)
            {
                scopes.Clear();
            }

            return scopes;
        }

        private bool TryResolvePairForMotor(
            CircuitComponent motor,
            IReadOnlyList<CircuitComponent> components,
            out ReversingPairScope scope)
        {
            scope = null;
            var mappings = new List<ContactorMotorPhaseMapping>();

            for (var i = 0; i < components.Count; i++)
            {
                var contactor = components[i];
                if (!IsSupportedContactor(contactor))
                {
                    continue;
                }

                if (TryResolveMapping(contactor, motor, out var mapping))
                {
                    mappings.Add(mapping);
                }
            }

            if (mappings.Count != 2)
            {
                return false;
            }

            var first = mappings[0];
            var second = mappings[1];
            if (first.IsForward && second.IsReverse)
            {
                scope = CreateScope(first.Contactor, second.Contactor, motor, true, "Reliable forward/reverse contactor pair.");
                return true;
            }

            if (first.IsReverse && second.IsForward)
            {
                scope = CreateScope(second.Contactor, first.Contactor, motor, true, "Reliable forward/reverse contactor pair.");
                return true;
            }

            return false;
        }

        private bool TryResolveMapping(
            CircuitComponent contactor,
            CircuitComponent motor,
            out ContactorMotorPhaseMapping mapping)
        {
            mapping = null;
            if (!TryFindSingleConnectedMotorTerminal(contactor.GetTerminal(TerminalConstants.T1), motor, out var t1Target) ||
                !TryFindSingleConnectedMotorTerminal(contactor.GetTerminal(TerminalConstants.T2), motor, out var t2Target) ||
                !TryFindSingleConnectedMotorTerminal(contactor.GetTerminal(TerminalConstants.T3), motor, out var t3Target))
            {
                return false;
            }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!targets.Add(t1Target) || !targets.Add(t2Target) || !targets.Add(t3Target))
            {
                return false;
            }

            mapping = new ContactorMotorPhaseMapping(contactor, t1Target, t2Target, t3Target);
            return mapping.IsForward || mapping.IsReverse;
        }

        private bool TryFindSingleConnectedMotorTerminal(
            TerminalView contactorTerminal,
            CircuitComponent motor,
            out string motorTerminalId)
        {
            motorTerminalId = null;
            var matches = 0;

            MatchMotorTerminal(contactorTerminal, motor, TerminalConstants.U, ref matches, ref motorTerminalId);
            MatchMotorTerminal(contactorTerminal, motor, TerminalConstants.V, ref matches, ref motorTerminalId);
            MatchMotorTerminal(contactorTerminal, motor, TerminalConstants.W, ref matches, ref motorTerminalId);

            return matches == 1;
        }

        private void MatchMotorTerminal(
            TerminalView contactorTerminal,
            CircuitComponent motor,
            string terminalId,
            ref int matches,
            ref string motorTerminalId)
        {
            if (AreConnectedByStaticWires(contactorTerminal, motor.GetTerminal(terminalId)))
            {
                matches++;
                motorTerminalId = terminalId;
            }
        }

        private void BuildStaticMainCircuitGraph(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
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
                if (wire == null)
                {
                    continue;
                }

                ConnectStatic(wire.StartTerminal, wire.EndTerminal);
            }
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

            var visited = new HashSet<TerminalView>();
            var queue = new Queue<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            visited.Add(first);
            queue.Enqueue(first);

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("ReversingPairScopeHelper.AreConnectedByStaticWires");
                    return false;
                }

                var current = queue.Dequeue();
                if (!staticWireGraph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("ReversingPairScopeHelper.AreConnectedByStaticWires.edges");
                        return false;
                    }

                    if (terminal == second)
                    {
                        return true;
                    }

                    if (visited.Add(terminal))
                    {
                        queue.Enqueue(terminal);
                    }
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

        private static bool IsSupportedContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return (string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase)) &&
                component.GetTerminal(TerminalConstants.T1) != null &&
                component.GetTerminal(TerminalConstants.T2) != null &&
                component.GetTerminal(TerminalConstants.T3) != null;
        }

        private static bool IsSupportedThreePhaseMotor(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                string.Equals(component.Definition.name, "Motor_ThreePhase_380V", StringComparison.OrdinalIgnoreCase) &&
                component.GetTerminal(TerminalConstants.U) != null &&
                component.GetTerminal(TerminalConstants.V) != null &&
                component.GetTerminal(TerminalConstants.W) != null;
        }

        private static ReversingPairScope CreateScope(
            CircuitComponent forwardContactor,
            CircuitComponent reverseContactor,
            CircuitComponent motor,
            bool reliable,
            string reason)
        {
            return new ReversingPairScope(forwardContactor, reverseContactor, motor, reliable, reason);
        }

        private sealed class ContactorMotorPhaseMapping
        {
            public ContactorMotorPhaseMapping(
                CircuitComponent contactor,
                string t1Target,
                string t2Target,
                string t3Target)
            {
                Contactor = contactor;
                T1Target = t1Target;
                T2Target = t2Target;
                T3Target = t3Target;
            }

            public CircuitComponent Contactor { get; }
            public string T1Target { get; }
            public string T2Target { get; }
            public string T3Target { get; }

            public bool IsForward =>
                IsTarget(T1Target, TerminalConstants.U) &&
                IsTarget(T2Target, TerminalConstants.V) &&
                IsTarget(T3Target, TerminalConstants.W);

            public bool IsReverse =>
                IsTarget(T1Target, TerminalConstants.W) &&
                IsTarget(T2Target, TerminalConstants.V) &&
                IsTarget(T3Target, TerminalConstants.U);

            private static bool IsTarget(string actual, string expected)
            {
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
