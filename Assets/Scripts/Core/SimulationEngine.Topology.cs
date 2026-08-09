using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    public sealed partial class SimulationEngine
    {
        private void ResetTraversalBudgetState()
        {
            traversalBudgetWarningLogged = false;
        }

        private void MarkTraversalBudgetExceeded(string context)
        {
            if (traversalBudgetWarningLogged)
            {
                return;
            }

            traversalBudgetWarningLogged = true;
            TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
        }

        private static bool IsPowerTerminal(TerminalView terminal, TerminalRole role)
        {
            return terminal != null &&
                terminal.Role == role &&
                terminal.Owner != null &&
                terminal.Owner.Definition != null &&
                (terminal.Owner.Definition.kind == ComponentKind.PowerSource || terminal.Owner.Definition.kind == ComponentKind.EnergyMeter);
        }

        private static string PowerTerminalKey(TerminalView terminal)
        {
            var ownerId = terminal.Owner != null ? terminal.Owner.InstanceId : string.Empty;
            return ownerId + "." + terminal.TerminalId;
        }

        private void BuildGraph()
        {
            // graph 是本次运行的瞬时可导通图：先放入外部 Wire，再按当前器件状态添加内部边。
            // 它不是保存拓扑，也不能用于反向推断用户是否实际画过一根导线。
            graph.Clear();

            foreach (var component in components)
            {
                foreach (var terminal in component.Terminals)
                {
                    Ensure(terminal);
                }
            }

            foreach (var wire in wires)
            {
                Connect(wire.StartTerminal, wire.EndTerminal);
            }

            foreach (var component in components)
            {
                AddInternalConnections(component);
            }
        }

        private void AddInternalConnections(CircuitComponent component)
        {
            // 内部边仅对当前运行步骤有效。接触器、按钮、限位开关和时间继电器的边都依赖运行状态，
            // 因此不得缓存为永久连接，也不得并入 WireManager 的用户接线集合。
            var terms = component.Terminals.ToList();
            if (terms.Count < 2)
            {
                return;
            }

            if (IsThermalRelayComponent(component))
            {
                AddThermalRelayInternalConnections(component);
                return;
            }

            if (IsLimitSwitch(component))
            {
                if (IsLimitSwitchEffectivelyTriggered(component))
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

            if (IsOnDelayTimerRelay(component))
            {
                if (IsOnDelayTimerElapsed(component))
                {
                    ConnectById(component, "15", "18");
                }
                else
                {
                    ConnectById(component, "15", "16");
                }

                return;
            }

            if (IsTimerRelayComponent(component))
            {
                return;
            }

            switch (component.Definition.kind)
            {
                case ComponentKind.TwoWaySwitch:
                    Connect(terms[0], component.IsClosed ? terms[1] : terms[2]);
                    break;
                case ComponentKind.EnergyMeter:
                    ConnectPairs(terms, true);
                    break;
                case ComponentKind.ContactorCoil:
                    foreach (var pair in ContactorTerminalSchema.EnumerateClosedPairs(closedContactors.Contains(component)))
                    {
                        ConnectById(component, pair.StartTerminalId, pair.EndTerminalId);
                    }
                    break;
                case ComponentKind.Switch:
                case ComponentKind.PushButton:
                case ComponentKind.Fuse:
                case ComponentKind.Breaker:
                case ComponentKind.TerminalBlock:
                    ConnectPairs(terms, component.IsClosed || component.Definition.kind == ComponentKind.TerminalBlock);
                    break;
            }
        }

        private bool IsLimitSwitchEffectivelyTriggered(CircuitComponent component)
        {
            return component != null && (component.IsClosed || IsVirtualLimitSwitchTriggered(component));
        }

        private bool IsVirtualLimitSwitchTriggered(CircuitComponent component)
        {
            if (component == null || autoReciprocationRoles == null || !autoReciprocationRoles.IsResolved)
            {
                return false;
            }

            if (!RuntimeStateManager.Shared.TryGetMotionState(autoReciprocationRoles.Motor.InstanceId, out var motionState) || motionState == null)
            {
                return false;
            }

            if (ReferenceEquals(component, autoReciprocationRoles.LeftLimitSwitch)) return motionState.LeftLimitTriggered;
            if (ReferenceEquals(component, autoReciprocationRoles.RightLimitSwitch)) return motionState.RightLimitTriggered;
            return false;
        }

        private static bool IsLimitSwitch(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                component.GetTerminal("11") == null || component.GetTerminal("12") == null ||
                component.GetTerminal("23") == null || component.GetTerminal("24") == null)
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("LimitSwitch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("TravelSwitch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("PositionSwitch", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("Switch_Limit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
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
                component.Definition.name.IndexOf("Button_Compound", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSelfLockingButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null &&
                component.Definition.name.IndexOf("Button_SelfLock", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddThermalRelayInternalConnections(CircuitComponent component)
        {
            if (component == null)
            {
                return;
            }

            ConnectById(component, "L1", "T1");
            ConnectById(component, "L2", "T2");
            ConnectById(component, "L3", "T3");

            if (component.IsClosed)
            {
                ConnectById(component, "95", "96");
            }
            else
            {
                ConnectById(component, "97", "98");
            }
        }

        private static bool IsThermalRelayComponent(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.GetTerminal("95") != null &&
                component.GetTerminal("96") != null &&
                component.GetTerminal("97") != null &&
                component.GetTerminal("98") != null &&
                component.GetTerminal("L1") != null &&
                component.GetTerminal("T1") != null;
        }

        private static bool IsThermalRelayTripped(CircuitComponent component)
        {
            return TryGetParameterValue(component, "tripState", out var value) && value >= 0.5f ||
                TryGetParameterValue(component, "isTripped", out value) && value >= 0.5f ||
                TryGetParameterValue(component, "tripped", out value) && value >= 0.5f;
        }

        private void ConnectById(CircuitComponent component, string a, string b)
        {
            Connect(component.GetTerminal(a), component.GetTerminal(b));
        }

        private void ConnectPairs(List<TerminalView> terms, bool enabled)
        {
            if (!enabled)
            {
                return;
            }

            for (var i = 0; i + 1 < terms.Count; i += 2)
            {
                Connect(terms[i], terms[i + 1]);
            }
        }

        private HashSet<TerminalView> Flood(List<TerminalView> roots)
        {
            // Flood 仅查询当前 graph 的可达端子，用于本 Tick 的供电与测量判断；它不修改元件状态。
            // 遍历预算是防御异常环路或损坏图的上限，达到上限时宁可返回受限结果，也不能无限阻塞仿真。
            var visited = new HashSet<TerminalView>();
            var queue = new Queue<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;

            foreach (var root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                visited.Add(root);
                queue.Enqueue(root);
            }

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalBudgetExceeded("SimulationEngine.Flood");
                    break;
                }

                var current = queue.Dequeue();
                if (!graph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalBudgetExceeded("SimulationEngine.Flood.edges");
                        queue.Clear();
                        break;
                    }

                    if (visited.Add(terminal))
                    {
                        queue.Enqueue(terminal);
                    }
                }
            }

            return visited;
        }

        private void Ensure(TerminalView terminal)
        {
            if (terminal != null && !graph.ContainsKey(terminal))
            {
                graph.Add(terminal, new List<TerminalView>());
            }
        }

        private void Connect(TerminalView a, TerminalView b)
        {
            if (a == null || b == null)
            {
                return;
            }

            Ensure(a);
            Ensure(b);

            if (!graph[a].Contains(b))
            {
                graph[a].Add(b);
            }

            if (!graph[b].Contains(a))
            {
                graph[b].Add(a);
            }
        }
    }
}
