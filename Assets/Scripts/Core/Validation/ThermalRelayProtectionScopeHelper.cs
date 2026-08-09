using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    public sealed class ThermalRelayProtectionScope
    {
        public ThermalRelayProtectionScope(
            CircuitComponent thermalRelay,
            CircuitComponent upstreamContactor,
            CircuitComponent protectedMotor,
            bool isReliable,
            string reason)
        {
            ThermalRelay = thermalRelay;
            UpstreamContactor = upstreamContactor;
            ProtectedMotor = protectedMotor;
            IsReliable = isReliable;
            Reason = reason ?? string.Empty;
        }

        public CircuitComponent ThermalRelay { get; }
        public CircuitComponent UpstreamContactor { get; }
        public CircuitComponent ProtectedMotor { get; }
        public bool IsReliable { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 在静态主回路中解析热继电器、其上游接触器和受保护三相电机之间的唯一保护作用域。
    /// 作用域依赖端子连通证据，不依据显示名称或画布距离；无法唯一对应时返回不可靠结果，避免多电机场景跨范围误报。
    /// 本类不判断热继电器当前脱扣状态，修改后必须回归热继主回路与控制旁路规则测试。
    /// </summary>
    // 热继保护作用域把 FR、上游接触器与受保护三相电机按主回路真实连接关联起来；不能只依赖名称或
    // 单个 NC 控制触点。主回路保护范围与控制回路 NC 作用是相关但不同的结构事实。
    public sealed class ThermalRelayProtectionScopeHelper
    {
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        // 只有上游接触器和下游电机均能唯一确定时才返回可靠作用域；模糊映射不能被后续旁路规则用作
        // 断言依据，避免多电机图纸出现跨回路误报。
        public bool TryResolveProtectionScope(
            CircuitComponent relay,
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            out ThermalRelayProtectionScope scope)
        {
            // 先锁定唯一上游接触器，再锁定唯一受保护电机；顺序不能调换，否则并联主回路会把不相关元件纳入同一保护范围。
            HasTraversalLimitExceeded = false;
            BuildStaticMainCircuitGraph(components, wires);

            if (!IsSupportedThermalRelay(relay))
            {
                scope = CreateScope(relay, null, null, false, "Unsupported thermal relay.");
                return false;
            }

            if (!TryFindUniqueUpstreamContactor(relay, components, out var upstreamContactor, out var contactorReason))
            {
                scope = CreateScope(relay, null, null, false, contactorReason);
                return false;
            }

            if (!TryFindUniqueProtectedMotor(relay, components, out var protectedMotor, out var motorReason))
            {
                scope = CreateScope(relay, upstreamContactor, null, false, motorReason);
                return false;
            }

            scope = CreateScope(relay, upstreamContactor, protectedMotor, true, "Reliable thermal relay protection scope.");
            return true;
        }

        // 上游接触器必须通过三相主回路得到唯一证明；仅有控制线、名称相似或单相接触均不能建立 FR 主回路范围。
        private bool TryFindUniqueUpstreamContactor(
            CircuitComponent relay,
            IReadOnlyList<CircuitComponent> components,
            out CircuitComponent contactor,
            out string reason)
        {
            // 多个候选接触器或电机均意味着静态证据不足，不把“距离最近”或名称相似作为兜底依据。
            contactor = null;
            var matchCount = 0;

            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    var candidate = components[i];
                    if (!IsSupportedContactor(candidate) ||
                        !AreContactorOutputsConnectedToRelayInputs(candidate, relay))
                    {
                        continue;
                    }

                    matchCount++;
                    contactor = candidate;
                }
            }

            if (matchCount == 1)
            {
                reason = string.Empty;
                return true;
            }

            reason = matchCount == 0
                ? "No unique upstream contactor found for thermal relay."
                : "Multiple upstream contactors found for thermal relay.";
            contactor = null;
            return false;
        }

        // 受保护电机同样要求 FR 输出三相与电机输入一一对应，避免一个 FR 被错误关联到并列负载。
        private bool TryFindUniqueProtectedMotor(
            CircuitComponent relay,
            IReadOnlyList<CircuitComponent> components,
            out CircuitComponent motor,
            out string reason)
        {
            motor = null;
            var matchCount = 0;

            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    var candidate = components[i];
                    if (!IsSupportedThreePhaseMotor(candidate) ||
                        !AreRelayOutputsConnectedToMotor(relay, candidate))
                    {
                        continue;
                    }

                    matchCount++;
                    motor = candidate;
                }
            }

            if (matchCount == 1)
            {
                reason = string.Empty;
                return true;
            }

            reason = matchCount == 0
                ? "No unique protected motor found for thermal relay."
                : "Multiple protected motors found for thermal relay.";
            motor = null;
            return false;
        }

        private bool AreContactorOutputsConnectedToRelayInputs(
            CircuitComponent contactor,
            CircuitComponent relay)
        {
            return AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T1), relay.GetTerminal(TerminalConstants.L1)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T2), relay.GetTerminal(TerminalConstants.L2)) &&
                AreConnectedByStaticWires(contactor.GetTerminal(TerminalConstants.T3), relay.GetTerminal(TerminalConstants.L3));
        }

        private bool AreRelayOutputsConnectedToMotor(
            CircuitComponent relay,
            CircuitComponent motor)
        {
            return AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T1), motor.GetTerminal(TerminalConstants.U)) &&
                AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T2), motor.GetTerminal(TerminalConstants.V)) &&
                AreConnectedByStaticWires(relay.GetTerminal(TerminalConstants.T3), motor.GetTerminal(TerminalConstants.W));
        }

        // 图中只连接外部主回路 Wire；接触器当前闭合不改变 FR 所属主回路的静态范围。
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
                    MarkTraversalLimitExceeded("ThermalRelayProtectionScopeHelper.AreConnectedByStaticWires");
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
                        MarkTraversalLimitExceeded("ThermalRelayProtectionScopeHelper.AreConnectedByStaticWires.edges");
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

        private static bool IsSupportedThermalRelay(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            if (string.Equals(component.Definition.name, "ThermalRelay_FR_380V", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.T1) != null &&
                component.GetTerminal(TerminalConstants.L2) != null &&
                component.GetTerminal(TerminalConstants.T2) != null &&
                component.GetTerminal(TerminalConstants.L3) != null &&
                component.GetTerminal(TerminalConstants.T3) != null &&
                component.GetTerminal(TerminalConstants.ThermalNC95) != null &&
                component.GetTerminal(TerminalConstants.ThermalNC96) != null &&
                component.GetTerminal(TerminalConstants.ThermalNO97) != null &&
                component.GetTerminal(TerminalConstants.ThermalNO98) != null;
        }

        private static bool IsSupportedContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase);
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

        private static ThermalRelayProtectionScope CreateScope(
            CircuitComponent relay,
            CircuitComponent upstreamContactor,
            CircuitComponent protectedMotor,
            bool reliable,
            string reason)
        {
            return new ThermalRelayProtectionScope(relay, upstreamContactor, protectedMotor, reliable, reason);
        }
    }
}
