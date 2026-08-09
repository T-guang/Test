using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    public sealed partial class SimulationEngine
    {
        private bool IsThreePhaseMotorEnergized(CircuitComponent motor)
        {
            var u = motor.GetTerminal("U");
            var v = motor.GetTerminal("V");
            var w = motor.GetTerminal("W");
            if (u == null || v == null || w == null)
            {
                return false;
            }

            return EvaluateThreePhaseMotorDirection(motor).IsRunning;
        }

        private static bool IsStarDeltaMotorComponent(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.Motor &&
                component.GetTerminal("U1") != null &&
                component.GetTerminal("V1") != null &&
                component.GetTerminal("W1") != null &&
                component.GetTerminal("U2") != null &&
                component.GetTerminal("V2") != null &&
                component.GetTerminal("W2") != null;
        }

        private bool IsStarDeltaMotorEnergized(CircuitComponent motor)
        {
            if (HasStarDeltaMotorConflict(motor))
            {
                return false;
            }

            var u1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("U1"));
            var v1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("V1"));
            var w1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("W1"));
            if (u1Phases.Count != 1 || v1Phases.Count != 1 || w1Phases.Count != 1)
            {
                return false;
            }

            var allPhases = new HashSet<string>(u1Phases);
            allPhases.UnionWith(v1Phases);
            allPhases.UnionWith(w1Phases);
            if (allPhases.Count != 3)
            {
                return false;
            }

            var starConnected =
                AreConnected(motor.GetTerminal("U2"), motor.GetTerminal("V2")) &&
                AreConnected(motor.GetTerminal("V2"), motor.GetTerminal("W2"));
            var deltaConnected =
                AreConnected(motor.GetTerminal("U1"), motor.GetTerminal("W2")) &&
                AreConnected(motor.GetTerminal("V1"), motor.GetTerminal("U2")) &&
                AreConnected(motor.GetTerminal("W1"), motor.GetTerminal("V2"));

            return starConnected != deltaConnected;
        }

        private bool HasStarDeltaMotorConflict(CircuitComponent motor)
        {
            if (!IsStarDeltaMotorComponent(motor))
            {
                return false;
            }

            var dynamicStarConnected =
                AreConnected(motor.GetTerminal("U2"), motor.GetTerminal("V2")) &&
                AreConnected(motor.GetTerminal("V2"), motor.GetTerminal("W2"));
            var dynamicDeltaConnected =
                AreConnected(motor.GetTerminal("U1"), motor.GetTerminal("W2")) &&
                AreConnected(motor.GetTerminal("V1"), motor.GetTerminal("U2")) &&
                AreConnected(motor.GetTerminal("W1"), motor.GetTerminal("V2"));

            if (dynamicStarConnected && dynamicDeltaConnected)
            {
                return true;
            }

            var explicitStarPoint =
                AreWireConnected(motor.GetTerminal("U2"), motor.GetTerminal("V2")) &&
                AreWireConnected(motor.GetTerminal("V2"), motor.GetTerminal("W2"));
            var explicitDelta =
                AreWireConnected(motor.GetTerminal("U1"), motor.GetTerminal("W2")) &&
                AreWireConnected(motor.GetTerminal("V1"), motor.GetTerminal("U2")) &&
                AreWireConnected(motor.GetTerminal("W1"), motor.GetTerminal("V2"));

            return explicitStarPoint && (explicitDelta || HasConfiguredDeltaContactor(motor)) ||
                explicitDelta && HasConfiguredStarContactor(motor);
        }

        private bool HasConfiguredStarContactor(CircuitComponent motor)
        {
            foreach (var component in components)
            {
                if (IsContactorComponent(component) &&
                    IsStarContactorInstanceId(component.InstanceId) &&
                    HasStandardStarContactorWiring(motor, component))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasConfiguredDeltaContactor(CircuitComponent motor)
        {
            foreach (var component in components)
            {
                if (IsContactorComponent(component) &&
                    IsDeltaContactorInstanceId(component.InstanceId) &&
                    HasStandardDeltaContactorWiring(motor, component))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasStandardStarContactorWiring(CircuitComponent motor, CircuitComponent contactor)
        {
            return AreWireConnected(motor.GetTerminal("U2"), contactor.GetTerminal("L1")) &&
                AreWireConnected(motor.GetTerminal("V2"), contactor.GetTerminal("L2")) &&
                AreWireConnected(motor.GetTerminal("W2"), contactor.GetTerminal("L3")) &&
                AreWireConnected(contactor.GetTerminal("T1"), contactor.GetTerminal("T2")) &&
                AreWireConnected(contactor.GetTerminal("T2"), contactor.GetTerminal("T3"));
        }

        private bool HasStandardDeltaContactorWiring(CircuitComponent motor, CircuitComponent contactor)
        {
            return AreWireConnected(motor.GetTerminal("U1"), contactor.GetTerminal("L1")) &&
                AreWireConnected(motor.GetTerminal("W2"), contactor.GetTerminal("T1")) &&
                AreWireConnected(motor.GetTerminal("V1"), contactor.GetTerminal("L2")) &&
                AreWireConnected(motor.GetTerminal("U2"), contactor.GetTerminal("T2")) &&
                AreWireConnected(motor.GetTerminal("W1"), contactor.GetTerminal("L3")) &&
                AreWireConnected(motor.GetTerminal("V2"), contactor.GetTerminal("T3"));
        }

        private bool AreWireConnected(TerminalView start, TerminalView end)
        {
            if (start == null || end == null)
            {
                return false;
            }

            if (start == end)
            {
                return true;
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
                    MarkTraversalBudgetExceeded("SimulationEngine.AreWireConnected");
                    return false;
                }

                var current = queue.Dequeue();
                foreach (var wire in wires)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalBudgetExceeded("SimulationEngine.AreWireConnected.wires");
                        return false;
                    }

                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                    {
                        continue;
                    }

                    TerminalView next = null;
                    if (wire.StartTerminal == current)
                    {
                        next = wire.EndTerminal;
                    }
                    else if (wire.EndTerminal == current)
                    {
                        next = wire.StartTerminal;
                    }

                    if (next == null || !visited.Add(next))
                    {
                        continue;
                    }

                    if (next == end)
                    {
                        return true;
                    }

                    queue.Enqueue(next);
                }
            }

            return false;
        }

        private static bool IsStarOrDeltaContactor(CircuitComponent component)
        {
            return component != null &&
                (IsStarContactorInstanceId(component.InstanceId) ||
                 IsDeltaContactorInstanceId(component.InstanceId));
        }

        private static bool IsStarContactorInstanceId(string instanceId)
        {
            return string.Equals(instanceId, "km_star", System.StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(instanceId) &&
                 instanceId.IndexOf("kmy", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsDeltaContactorInstanceId(string instanceId)
        {
            return string.Equals(instanceId, "km_delta", System.StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(instanceId) &&
                 instanceId.IndexOf("kmd", System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void UpdateMotorDirection(CircuitComponent component, bool active)
        {
            // 电机方向是由已稳定的供电相序和控制器运行态导出的副作用状态，
            // 不是 Flood 图本身的一条边；不要在连通性查询中通过写入方向来影响后续搜索。
            if (!IsThreePhaseMotorComponent(component))
            {
                return;
            }

            var directionResult = EvaluateThreePhaseMotorDirection(component);
            var motorState = RuntimeStateManager.Shared.GetOrCreateMotorState(component.InstanceId);
            motorState?.Update(directionResult, active);

            // Old templates may carry this presentation parameter. Keep it synchronized,
            // but the runtime state above is the authority for all ordinary motors.
            var rotationDirection = directionResult.Direction == MotorDirectionState.Forward ? 1f :
                directionResult.Direction == MotorDirectionState.Reverse ? -1f : 0f;
            SetParameterIfPresent(component, "rotationDirection", rotationDirection);
            UpdateAutoReciprocatingMotionDirection(component, motorState != null && motorState.IsRunning, rotationDirection);
        }

        private void UpdateAutoReciprocatingMotionDirection(CircuitComponent component, bool active, float rotationDirection)
        {
            // 往复运动在电机已有效运行后才推进。限位开关触发与方向切换之间依赖本轮稳定的控制状态，
            // 因此不能把位置推进提前到接触器/限位触点尚未完成求值的阶段。
            if (!IsAutoReciprocatingMotionMotor(component))
            {
                return;
            }

            var motionState = RuntimeStateManager.Shared.GetOrCreateMotionState(component.InstanceId);
            if (motionState == null)
            {
                return;
            }

            if (!active)
            {
                motionState.Direction = MotionDirection.Stopped;
                return;
            }

            if (rotationDirection > 0.5f)
            {
                motionState.Direction = MotionDirection.Forward;
            }
            else if (rotationDirection < -0.5f)
            {
                motionState.Direction = MotionDirection.Reverse;
            }
            else
            {
                motionState.Direction = MotionDirection.Stopped;
            }

            motionState.AdvancePosition(simulationDeltaTime);
        }

        private bool IsAutoReciprocatingMotionMotor(CircuitComponent component)
        {
            return autoReciprocationRoles != null &&
                autoReciprocationRoles.IsResolved &&
                ReferenceEquals(autoReciprocationRoles.Motor, component);
        }

        private MotorDirectionResult EvaluateThreePhaseMotorDirection(CircuitComponent motor)
        {
            return MotorPhaseSequenceEvaluator.Evaluate(
                GetReachablePowerTerminalIds(motor != null ? motor.GetTerminal("U") : null),
                GetReachablePowerTerminalIds(motor != null ? motor.GetTerminal("V") : null),
                GetReachablePowerTerminalIds(motor != null ? motor.GetTerminal("W") : null));
        }

        private HashSet<string> GetReachablePowerTerminalIds(TerminalView terminal)
        {
            var ids = new HashSet<string>();
            if (terminal == null)
            {
                return ids;
            }

            foreach (var reachable in Flood(new List<TerminalView> { terminal }))
            {
                if (IsPowerTerminal(reachable, TerminalRole.Phase))
                {
                    ids.Add(reachable.TerminalId);
                }
            }

            return ids;
        }

        private bool UpdateThermalRelays()
        {
            // 保护状态是运行时副作用。返回值仅表示它是否改变了后续拓扑，调用方据此决定是否需要重算，
            // 不能把参数复位或跳闸状态更新混入纯连通性查询。
            var changed = false;
            foreach (var relay in components)
            {
                if (!IsThermalRelayComponent(relay))
                {
                    continue;
                }

                if (IsThermalRelayTripped(relay))
                {
                    changed |= SetThermalRelayTripState(relay, false);
                }

                if (TryGetParameterValue(relay, "manualTrip", out var manualTrip) && manualTrip > 0f)
                {
                    SetParameterIfPresent(relay, "manualTrip", 0f);
                    changed = true;
                }

                SetParameterIfPresent(relay, "overloadTimer", 0f);
            }

            return changed;
        }

        private static float ResolveParameterValue(CircuitComponent component, string key, float fallback)
        {
            return TryGetParameterValue(component, key, out var value) ? value : fallback;
        }

        private static bool SetThermalRelayTripState(CircuitComponent relay, bool tripped)
        {
            var oldState = IsThermalRelayTripped(relay);
            SetParameterIfPresent(relay, "tripState", tripped ? 1f : 0f);
            SetParameterIfPresent(relay, "isTripped", tripped ? 1f : 0f);
            SetParameterIfPresent(relay, "tripped", tripped ? 1f : 0f);
            return oldState != tripped;
        }

        private static void SetParameterIfPresent(CircuitComponent component, string key, float value)
        {
            component?.SetParameterValue(key, value);
        }

        private static bool IsThreePhaseMotorComponent(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.Motor &&
                component.GetTerminal("U") != null &&
                component.GetTerminal("V") != null &&
                component.GetTerminal("W") != null;
        }

        private HashSet<string> GetReachablePowerPhaseKeys(TerminalView terminal)
        {
            var phases = new HashSet<string>();
            foreach (var reachable in Flood(new List<TerminalView> { terminal }))
            {
                if (IsPowerTerminal(reachable, TerminalRole.Phase))
                {
                    phases.Add(PowerTerminalKey(reachable));
                }
            }

            return phases;
        }

        private bool CanReachPowerNeutral(TerminalView terminal)
        {
            return Flood(new List<TerminalView> { terminal }).Any(t => IsPowerTerminal(t, TerminalRole.Neutral));
        }

        private bool AreConnected(TerminalView a, TerminalView b)
        {
            return a != null && b != null && Flood(new List<TerminalView> { a }).Contains(b);
        }

    }
}
