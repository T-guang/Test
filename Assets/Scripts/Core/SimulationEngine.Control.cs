using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    public sealed partial class SimulationEngine
    {
        private void StabilizeDynamicControlDevices()
        {
            // 接触器自保持、KT 延时触点和互锁会反过来改变连通图。这里以有限轮次求稳定，
            // 每轮重建图后再判断是否收敛；不要将其与后续负载通电判断合并或调换顺序。
            closedContactors.Clear();
            energizedOnDelayTimers.Clear();
            timerRuntimeAdvancedThisRun = false;
            SeedClosedContactorsFromRuntimeState();
            BuildGraph();

            // 图必须在状态变化后重建：旧图中触点的导通边不能代表下一轮的闭合组合。
            for (var i = 0; i < MaxContactorStabilizationIterations; i++)
            {
                var previousContactors = new HashSet<CircuitComponent>(closedContactors);
                var previousTimers = new HashSet<CircuitComponent>(energizedOnDelayTimers);
                var previousTimerContacts = CaptureOnDelayTimerElapsedStates();

                UpdateClosedContactors();
                UpdateEnergizedOnDelayTimers();
                var currentTimerContacts = CaptureOnDelayTimerElapsedStates();
                if (previousContactors.SetEquals(closedContactors) &&
                    previousTimers.SetEquals(energizedOnDelayTimers) &&
                    AreTimerContactStatesEqual(previousTimerContacts, currentTimerContacts))
                {
                    break;
                }

                BuildGraph();
            }
        }

        private Dictionary<string, bool> CaptureOnDelayTimerElapsedStates()
        {
            var states = new Dictionary<string, bool>();
            foreach (var component in components)
            {
                if (!IsOnDelayTimerRelay(component))
                {
                    continue;
                }

                states[component.InstanceId] = IsOnDelayTimerElapsed(component);
            }

            return states;
        }

        private static bool AreTimerContactStatesEqual(
            IReadOnlyDictionary<string, bool> previousStates,
            IReadOnlyDictionary<string, bool> currentStates)
        {
            if (previousStates == null || currentStates == null || previousStates.Count != currentStates.Count)
            {
                return false;
            }

            foreach (var pair in previousStates)
            {
                if (!currentStates.TryGetValue(pair.Key, out var current) || current != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }


        private void SeedClosedContactorsFromRuntimeState()
        {
            // 仅把已经得电且仍具备保持条件的接触器作为初始猜测；这不是最终结论，随后稳定循环会用
            // 当前拓扑重新判定线圈状态，避免停止按钮或互锁状态被上一帧永久保留。
            foreach (var component in components)
            {
                if (IsContactorComponent(component) && component.IsEnergized && IsSelfHoldEligible(component))
                {
                    closedContactors.Add(component);
                }
            }
        }


        private void UpdateClosedContactors()
        {
            closedContactors.Clear();
            foreach (var component in components)
            {
                if (!IsContactorComponent(component))
                {
                    continue;
                }

                if (IsCoilEnergized(component))
                {
                    closedContactors.Add(component);
                }
            }

            // 先从线圈路径得到候选闭合集合，再处理互锁和星三角冲突。冲突处理不能提前写入图，
            // 否则同一轮的其他线圈会基于不一致的触点状态计算。
            ResolveMutualInterlockConflicts();
            SuppressStarDeltaConflictContactors();
        }

        private void SuppressStarDeltaConflictContactors()
        {
            // 星三角冲突抑制只作用于当前运行时闭合集合，防止互斥主回路在同一轮被同时加入图中。
            // 它不改写用户外部导线，也不抹除 Analyzer 需要展示的静态星/三角接线冲突证据。
            if (!HasAnyStarDeltaMotorConflict())
            {
                return;
            }

            closedContactors.RemoveWhere(IsStarOrDeltaContactor);
        }

        private bool HasAnyStarDeltaMotorConflict()
        {
            foreach (var component in components)
            {
                if (IsStarDeltaMotorComponent(component) && HasStarDeltaMotorConflict(component))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateEnergizedOnDelayTimers()
        {
            // 定时器运行态由 RuntimeStateManager 保存，而触点是否已到时会反过来影响本轮图。这里仅推进
            // 一次并由稳定循环比较触点状态，避免同一 Run 内重复累计延时。
            energizedOnDelayTimers.Clear();
            var timerDelta = timerRuntimeAdvancedThisRun ? 0f : simulationDeltaTime;
            foreach (var component in components)
            {
                if (!IsOnDelayTimerRelay(component))
                {
                    continue;
                }

                var coilEnergized = IsCoilEnergized(component);
                UpdateOnDelayTimerRuntimeState(component, coilEnergized, timerDelta);
                if (coilEnergized)
                {
                    energizedOnDelayTimers.Add(component);
                }
            }

            timerRuntimeAdvancedThisRun = true;
        }

        private static void UpdateOnDelayTimerRuntimeState(CircuitComponent component, bool coilEnergized, float deltaSeconds)
        {
            // KT 状态由 RuntimeStateManager 持有。线圈失电必须先复位，再由下一轮图分析决定延时触点，
            // 否则停止后可能遗留已动作触点。KT、两电机顺序启动和星三角修改后必须回归。
            var runtimeState = RuntimeStateManager.Shared.GetOrCreateTimerState(component != null ? component.InstanceId : null);
            if (runtimeState == null)
            {
                return;
            }

            var delaySeconds = Mathf.Max(0f, ResolveParameterValue(component, "delaySeconds", 3f));
            runtimeState.DelaySeconds = delaySeconds;

            if (!coilEnergized)
            {
                runtimeState.Reset();
                runtimeState.DelaySeconds = delaySeconds;
                return;
            }

            runtimeState.IsCoilEnergized = true;

            if (delaySeconds <= 0f)
            {
                runtimeState.ElapsedSeconds = 0f;
                runtimeState.Phase = TimerRuntimePhase.Elapsed;
                return;
            }

            var elapsedSeconds = Mathf.Max(0f, runtimeState.ElapsedSeconds);
            elapsedSeconds = Mathf.Min(delaySeconds, elapsedSeconds + Mathf.Max(0f, deltaSeconds));
            runtimeState.ElapsedSeconds = elapsedSeconds;
            runtimeState.Phase = elapsedSeconds >= delaySeconds ? TimerRuntimePhase.Elapsed : TimerRuntimePhase.Timing;
        }

        private static bool IsContactorComponent(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            if (IsTimerRelayComponent(component))
            {
                return false;
            }

            return component.Definition.kind == ComponentKind.ContactorCoil ||
                component.GetTerminal("A1") != null &&
                component.GetTerminal("A2") != null &&
                component.GetTerminal("L1") != null &&
                component.GetTerminal("T1") != null;
        }

        private bool IsCoilEnergized(CircuitComponent component)
        {
            var coilA1 = component.GetTerminal("A1");
            var coilA2 = component.GetTerminal("A2");
            if (coilA1 == null || coilA2 == null || AreConnected(coilA1, coilA2))
            {
                return false;
            }

            var a1Phases = GetReachablePowerPhaseKeys(coilA1);
            var a2Phases = GetReachablePowerPhaseKeys(coilA2);

            if (a1Phases.Any(a => a2Phases.Any(b => b != a)))
            {
                return true;
            }

            return a1Phases.Count > 0 && CanReachPowerNeutral(coilA2) ||
                a2Phases.Count > 0 && CanReachPowerNeutral(coilA1);
        }

        private static bool IsOnDelayTimerRelay(CircuitComponent component)
        {
            if (!IsTimerRelayComponent(component))
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("Timer_OnDelay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.Contains("通电延时");
        }

        private static bool IsOnDelayTimerElapsed(CircuitComponent component)
        {
            return component != null &&
                RuntimeStateManager.Shared.TryGetTimerState(component.InstanceId, out var timerState) &&
                timerState != null &&
                timerState.IsCoilEnergized &&
                timerState.Phase == TimerRuntimePhase.Elapsed;
        }

        private static bool IsTimerRelayComponent(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                component.GetTerminal("A1") == null || component.GetTerminal("A2") == null)
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("Timer_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("TimerRelay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.Contains("时间继电器");
        }

        private void UpdateSelfHoldEligibility()
        {
            // 自保持资格是稳定迭代中的运行时辅助状态，而不是接线图上的永久属性。
            // 点动按钮闭合时必须压制保持资格；连续启动路径与已闭合辅助触点则可在下一轮参与线圈判定。
            // 因而不能把这段求值挪到接触器状态更新之后，或改为只在首次运行时计算一次。
            foreach (var component in components)
            {
                if (!IsContactorComponent(component))
                {
                    continue;
                }

                var id = component.GetInstanceID();
                if (!closedContactors.Contains(component))
                {
                    selfHoldEligibleContactors[id] = false;
                    continue;
                }

                if (IsSelfHoldBrokenByJogButton(component))
                {
                    selfHoldEligibleContactors[id] = false;
                    continue;
                }

                selfHoldEligibleContactors[id] = IsSelfHoldEligible(component) || HasClosedContinuousStartPath(component);
            }
        }

        private static bool IsSelfHoldEligible(CircuitComponent component)
        {
            return component != null &&
                selfHoldEligibleContactors.TryGetValue(component.GetInstanceID(), out var eligible) &&
                eligible;
        }

        private bool HasClosedContinuousStartPath(CircuitComponent contactor)
        {
            var a1 = contactor.GetTerminal("A1");
            if (a1 == null)
            {
                return false;
            }

            foreach (var component in components)
            {
                if (!IsClosedContinuousStartComponent(component))
                {
                    continue;
                }

                if (IsJogStartButtonForContactor(component, contactor))
                {
                    continue;
                }

                if (AreConnected(component.GetTerminal("23"), a1) || AreConnected(component.GetTerminal("24"), a1))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSelfHoldBrokenByJogButton(CircuitComponent contactor)
        {
            foreach (var component in components)
            {
                if (component == null || !component.IsClosed)
                {
                    continue;
                }

                if (IsJogStartButtonForContactor(component, contactor))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsJogStartButtonForContactor(CircuitComponent button, CircuitComponent contactor)
        {
            if (!IsCompoundPushButton(button) || contactor == null)
            {
                return false;
            }

            var a1 = contactor.GetTerminal("A1");
            var button23 = button.GetTerminal("23");
            var button24 = button.GetTerminal("24");
            if (a1 == null || button23 == null || button24 == null)
            {
                return false;
            }

            if (!AreConnected(button23, a1) && !AreConnected(button24, a1))
            {
                return false;
            }

            var instanceId = button.InstanceId ?? string.Empty;
            if (instanceId.IndexOf("jog", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                instanceId.IndexOf("点动", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var button11 = button.GetTerminal("11");
            var button12 = button.GetTerminal("12");
            if (button11 == null || button12 == null)
            {
                return false;
            }

            // 泛化：遍历 ContactorTerminalSchema.NormallyOpenContactPairs（13/14、33/34…），
            // 任意一组 NO 辅助触点与按钮 11/12 相连即判定为点动按钮。
            // 不再硬编码 13/14，也不假设第一组 NO 与第二组 NO 有固定用途。
            foreach (var pair in ContactorTerminalSchema.NormallyOpenContactPairs)
            {
                var noStart = contactor.GetTerminal(pair.StartTerminalId);
                var noEnd = contactor.GetTerminal(pair.EndTerminalId);
                if (noStart == null || noEnd == null)
                {
                    continue;
                }

                if (AreConnected(button11, noStart) ||
                    AreConnected(button11, noEnd) ||
                    AreConnected(button12, noStart) ||
                    AreConnected(button12, noEnd))
                {
                    return true;
                }
            }

            return false;
        }
        private bool IsClosedContinuousStartComponent(CircuitComponent component)
        {
            if (component == null ||
                component.Definition == null ||
                component.GetTerminal("23") == null ||
                component.GetTerminal("24") == null)
            {
                return false;
            }

            if (IsLimitSwitch(component))
            {
                return IsLimitSwitchEffectivelyTriggered(component);
            }

            return component.Definition.kind == ComponentKind.PushButton && component.IsClosed;
        }

        private void ResolveMutualInterlockConflicts()
        {
            // 互锁冲突是在两个方向的线圈同一稳定轮内都请求吸合时的运行时裁决。
            // 这里消除不允许共存的状态，不能把它替代为结构接线检查；后者属于 Analyzer/RuleChecker 的职责。
            if (closedContactors.Count < 2)
            {
                return;
            }

            var orderedContactors = closedContactors
                .OrderBy(GetComponentPriority)
                .ToList();
            var toRelease = new HashSet<CircuitComponent>();

            foreach (var contactor in orderedContactors)
            {
                if (toRelease.Contains(contactor))
                {
                    continue;
                }

                foreach (var other in orderedContactors)
                {
                    if (contactor == other || toRelease.Contains(other))
                    {
                        continue;
                    }

                    if (!CoilDependsOnNormallyClosedAuxiliary(contactor, other))
                    {
                        continue;
                    }

                    if (CoilDependsOnNormallyClosedAuxiliary(other, contactor))
                    {
                        var loser = GetComponentPriority(contactor) <= GetComponentPriority(other) ? other : contactor;
                        toRelease.Add(loser);
                    }
                    else
                    {
                        toRelease.Add(contactor);
                    }
                }
            }

            foreach (var contactor in toRelease)
            {
                closedContactors.Remove(contactor);
            }
        }

        private int GetComponentPriority(CircuitComponent component)
        {
            var index = components.IndexOf(component);
            return index >= 0 ? index : int.MaxValue;
        }

        private bool CoilDependsOnNormallyClosedAuxiliary(CircuitComponent contactor, CircuitComponent auxiliaryOwner)
        {
            var a1 = contactor.GetTerminal("A1");
            var a2 = contactor.GetTerminal("A2");
            if (a1 == null || a2 == null || auxiliaryOwner == null)
            {
                return false;
            }

            // 泛化：遍历 ContactorTerminalSchema.NormallyClosedContactPairs（当前 21/22…），
            // 任意一组 NC 辅助触点与接触器线圈 A1/A2 相连即判定存在 NC 互锁依赖。
            // 当前 Schema 只有 21/22 一组 NC，因此泛化前后所有现有电路运行结果完全一致。
            foreach (var pair in ContactorTerminalSchema.NormallyClosedContactPairs)
            {
                var ncStart = auxiliaryOwner.GetTerminal(pair.StartTerminalId);
                var ncEnd = auxiliaryOwner.GetTerminal(pair.EndTerminalId);
                if (ncStart == null || ncEnd == null)
                {
                    continue;
                }

                if (AreConnected(a1, ncStart) ||
                    AreConnected(a1, ncEnd) ||
                    AreConnected(a2, ncStart) ||
                    AreConnected(a2, ncEnd))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
