using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 对工作区图执行一次仿真步进，并更新元件与运行态。
    /// 它不是通用 SPICE 求解器，输入必须是工作区拥有的元件和导线列表。
    /// 副作用包括时间继电器、保护、运动、接触器及可视电气状态更新。
    /// 主要调用方是 WorkspaceController；修改后必须回归运行态模板。
    /// </summary>
    public sealed class SimulationEngine
    {
        private readonly List<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly Dictionary<TerminalView, List<TerminalView>> graph = new Dictionary<TerminalView, List<TerminalView>>();
        private readonly HashSet<CircuitComponent> closedContactors = new HashSet<CircuitComponent>();
        private readonly HashSet<CircuitComponent> energizedOnDelayTimers = new HashSet<CircuitComponent>();
        private readonly float simulationDeltaTime;
        private readonly AutoReciprocationRoleResolution autoReciprocationRoles;
        private bool timerRuntimeAdvancedThisRun;
        private bool traversalBudgetWarningLogged;
        private const int MaxContactorStabilizationIterations = 4;
        private static readonly Dictionary<int, bool> selfHoldEligibleContactors = new Dictionary<int, bool>();

        public SimulationEngine(List<CircuitComponent> components, IReadOnlyList<WireView> wires, float simulationDeltaTime = 0f)
        {
            this.components = components;
            this.wires = wires;
            this.simulationDeltaTime = Mathf.Max(0f, simulationDeltaTime);
            autoReciprocationRoles = AutoReciprocationRoleResolver.Resolve(components, wires);
        }

        public static void ResetRuntimeState()
        {
            RuntimeStateManager.Shared.ResetAll("SimulationEngine.ResetRuntimeState");
            selfHoldEligibleContactors.Clear();
        }

        public string Run()
        {
            // 必须先稳定自保持、时间继电器、互锁与星三角状态，再进行图连通扩散和负载判断。
            // 调整该顺序会改变可观察到的运行行为。
            ResetTraversalBudgetState();
            StabilizeDynamicControlDevices();

            var phaseRoots = GetPhaseRoots();
            var neutralRoots = GetNeutralRoots();

            UpdateSelfHoldEligibility();

            var powered = Flood(phaseRoots);
            var neutral = Flood(neutralRoots);
            var shorted = powered.Overlaps(neutral);
            var energizedCount = 0;

            var systemVoltage = ActualSupplyVoltageResolver.ResolveSinglePhaseVoltage(components);
            var systemLineVoltage = ActualSupplyVoltageResolver.ResolveThreePhaseLineVoltage(components);

            foreach (var component in components)
            {
                var energized = IsLoadEnergized(component, powered, neutral);
                var active = !shorted && energized;
                UpdateMotorDirection(component, active);
                component.SetEnergized(active);
                ApplyMeasurement(component, active, systemVoltage, systemLineVoltage, GetReachablePowerPhaseKeys, CanReachPowerNeutral);
                if (active)
                {
                    energizedCount++;
                }
            }

            if (UpdateThermalRelays())
            {
                StabilizeDynamicControlDevices();

                UpdateSelfHoldEligibility();

                powered = Flood(phaseRoots);
                neutral = Flood(neutralRoots);
                shorted = powered.Overlaps(neutral);
                energizedCount = 0;

                foreach (var component in components)
                {
                    var energized = IsLoadEnergized(component, powered, neutral);
                    var active = !shorted && energized;
                    UpdateMotorDirection(component, active);
                    component.SetEnergized(active);
                    ApplyMeasurement(component, active, systemVoltage, systemLineVoltage, GetReachablePowerPhaseKeys, CanReachPowerNeutral);
                    if (active)
                    {
                        energizedCount++;
                    }
                }
            }

            if (phaseRoots.Count == 0 || neutralRoots.Count == 0)
            {
                return "缺少电源，请先放置 220V 电源。";
            }

            if (shorted)
            {
                return "检测到短路：火线与零线直接连通。";
            }

            return energizedCount > 0 ? $"仿真完成：{energizedCount} 个负载/线圈已动作。" : "线路未形成完整回路。";
        }

        private void StabilizeDynamicControlDevices()
        {
            // 接触器自保持、KT 延时触点和互锁会反过来改变连通图。这里以有限轮次求稳定，
            // 每轮重建图后再判断是否收敛；不要将其与后续负载通电判断合并或调换顺序。
            closedContactors.Clear();
            energizedOnDelayTimers.Clear();
            timerRuntimeAdvancedThisRun = false;
            SeedClosedContactorsFromRuntimeState();
            BuildGraph();

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

        private void ApplyMeasurement(
            CircuitComponent component,
            bool active,
            float systemVoltage,
            float systemLineVoltage,
            TeachingParameterCalculationService.PhaseResolver resolvePhases,
            TeachingParameterCalculationService.NeutralResolver canReachNeutral)
        {
            if (component == null)
            {
                return;
            }

            var definition = component.Definition;
            if (!active || definition == null)
            {
                component.ClearMeasurement();
                return;
            }

            // 教学估算优先使用已确认的运行态；异常阶段返回零或非正常结果，
            // 不应由通用额定值计算覆盖星三角、缺相等特例。
            if (TeachingParameterCalculationService.TryCalculateSinglePhaseLoad(
                component,
                active,
                systemVoltage,
                resolvePhases,
                canReachNeutral,
                out var teachingEstimate))
            {
                component.SetMeasurement(
                    teachingEstimate.MeasuredVoltage,
                    teachingEstimate.MeasuredCurrent,
                    teachingEstimate.MeasuredPower);
                return;
            }

            if (TeachingParameterCalculationService.TryCalculateStarDeltaMotor(
                component,
                ResolveStarDeltaEstimateStage(component, active),
                systemLineVoltage,
                out var starDeltaEstimate))
            {
                component.SetMeasurement(
                    starDeltaEstimate.LineVoltage,
                    starDeltaEstimate.EstimatedCurrent,
                    starDeltaEstimate.HasNormalEstimate ? starDeltaEstimate.RatedPower : 0f);
                return;
            }

            if (TeachingParameterCalculationService.TryCalculateThreePhaseMotor(
                component,
                active,
                systemLineVoltage,
                out var motorEstimate))
            {
                component.SetMeasurement(
                    motorEstimate.LineVoltage,
                    motorEstimate.EstimatedCurrent,
                    motorEstimate.IsRunning ? motorEstimate.RatedPower : 0f);
                return;
            }

            var voltage = definition.kind == ComponentKind.PowerSource ? ResolveVoltage(component, definition) : (systemVoltage > 0f ? systemVoltage : ResolveVoltage(component, definition));
            var power = ResolvePower(component, definition);
            var current = ResolveCurrent(component, definition);

            if (!IsThreePhaseMotorComponent(component) && voltage > 0f && power > 0f)
            {
                current = power / voltage;
            }

            if (power <= 0f && voltage > 0f && current > 0f)
            {
                power = voltage * current;
            }

            component.SetMeasurement(voltage, current, power);
        }

        private StarDeltaMotorEstimateStage ResolveStarDeltaEstimateStage(
            CircuitComponent motor,
            bool active)
        {
            if (!TeachingParameterCalculationService.IsStarDeltaTeachingMotor(motor))
            {
                return StarDeltaMotorEstimateStage.Unknown;
            }

            if (HasStarDeltaMotorConflict(motor))
            {
                return StarDeltaMotorEstimateStage.Conflict;
            }

            if (!active)
            {
                return StarDeltaMotorEstimateStage.Stopped;
            }

            var u1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("U1"));
            var v1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("V1"));
            var w1Phases = GetReachablePowerPhaseKeys(motor.GetTerminal("W1"));
            if (u1Phases.Count != 1 || v1Phases.Count != 1 || w1Phases.Count != 1)
            {
                return StarDeltaMotorEstimateStage.SupplyFault;
            }

            var phases = new HashSet<string>();
            foreach (var phase in u1Phases)
            {
                phases.Add(phase);
            }
            foreach (var phase in v1Phases)
            {
                phases.Add(phase);
            }
            foreach (var phase in w1Phases)
            {
                phases.Add(phase);
            }

            if (phases.Count != 3)
            {
                return StarDeltaMotorEstimateStage.SupplyFault;
            }

            var starConnected =
                AreConnected(motor.GetTerminal("U2"), motor.GetTerminal("V2")) &&
                AreConnected(motor.GetTerminal("V2"), motor.GetTerminal("W2"));
            var deltaConnected =
                AreConnected(motor.GetTerminal("U1"), motor.GetTerminal("W2")) &&
                AreConnected(motor.GetTerminal("V1"), motor.GetTerminal("U2")) &&
                AreConnected(motor.GetTerminal("W1"), motor.GetTerminal("V2"));

            if (starConnected && deltaConnected)
            {
                return StarDeltaMotorEstimateStage.Conflict;
            }

            if (starConnected)
            {
                return StarDeltaMotorEstimateStage.Star;
            }

            if (deltaConnected)
            {
                return StarDeltaMotorEstimateStage.Delta;
            }

            return StarDeltaMotorEstimateStage.Stopped;
        }

        private static float ResolveVoltage(CircuitComponent component, ComponentDefinition definition)
        {
            if (definition.kind == ComponentKind.PowerSource &&
                TryGetParameterValue(component, "sourceVoltage", out var sourceVoltage))
            {
                return sourceVoltage;
            }

            if (TryGetParameterValue(component, "ratedVoltage", out var value))
            {
                return value;
            }

            if (TryGetParameterValue(component, "voltage", out value))
            {
                return value;
            }

            return definition.ratedVoltage > 0f ? definition.ratedVoltage : definition.sourceVoltage;
        }

        private static float ResolveLineVoltage(CircuitComponent component, ComponentDefinition definition, float fallback)
        {
            if (TryGetParameterValue(component, "sourceLineVoltage", out var value) && value > 0f)
            {
                return value;
            }

            if (TryGetParameterValue(component, "lineVoltage", out value) && value > 0f)
            {
                return value;
            }

            return definition.sourceLineVoltage > 0f ? definition.sourceLineVoltage : fallback;
        }

        private static float ResolvePower(CircuitComponent component, ComponentDefinition definition)
        {
            if (TryGetParameterValue(component, "power", out var value))
            {
                return value;
            }

            if (TryGetParameterValue(component, "ratedPower", out value))
            {
                return value;
            }

            return definition.ratedPower;
        }

        private static float ResolveCurrent(CircuitComponent component, ComponentDefinition definition)
        {
            if (TryGetParameterValue(component, "current", out var value))
            {
                return value;
            }

            var ratedCurrent = definition.ratedCurrent;
            if (TryGetParameterValue(component, "ratedCurrent", out value))
            {
                ratedCurrent = value;
            }

            if (TryGetParameterValue(component, "loadFactor", out var loadFactor))
            {
                return ratedCurrent * Mathf.Max(0f, loadFactor);
            }

            return ratedCurrent;
        }

        private static bool TryGetParameterValue(CircuitComponent component, string key, out float value)
        {
            value = 0f;
            var parameter = component != null ? component.GetParameter(key) : null;
            if (parameter == null)
            {
                return false;
            }

            value = parameter.value;
            return true;
        }

        private void SeedClosedContactorsFromRuntimeState()
        {
            foreach (var component in components)
            {
                if (IsContactorComponent(component) && component.IsEnergized && IsSelfHoldEligible(component))
                {
                    closedContactors.Add(component);
                }
            }
        }

        private bool IsLoadEnergized(CircuitComponent component, HashSet<TerminalView> powered, HashSet<TerminalView> neutral)
        {
            if (component.Definition.kind == ComponentKind.PowerSource || component.Definition.kind == ComponentKind.EnergyMeter || component.Definition.kind == ComponentKind.Switch || component.Definition.kind == ComponentKind.TwoWaySwitch || component.Definition.kind == ComponentKind.Fuse || component.Definition.kind == ComponentKind.Breaker || component.Definition.kind == ComponentKind.PushButton || component.Definition.kind == ComponentKind.TerminalBlock || component.Definition.kind == ComponentKind.Instrument)
            {
                return false;
            }

            if (component.Definition.kind == ComponentKind.Motor)
            {
                if (component.GetTerminal("U") != null && component.GetTerminal("V") != null && component.GetTerminal("W") != null)
                {
                    return IsThreePhaseMotorEnergized(component);
                }

                if (IsStarDeltaMotorComponent(component))
                {
                    return IsStarDeltaMotorEnergized(component);
                }

                return HasPhaseAndNeutral(component, powered, neutral);
            }

            if (IsOnDelayTimerRelay(component))
            {
                return energizedOnDelayTimers.Contains(component);
            }

            if (IsTimerRelayComponent(component))
            {
                return false;
            }

            if (component.Definition.kind == ComponentKind.ContactorCoil)
            {
                return closedContactors.Contains(component);
            }

            if (component.Definition.kind == ComponentKind.Indicator)
            {
                return IsIndicatorEnergized(component);
            }

            // E4：普通交流灯泡无极性，L/N 端子互换不影响亮灯判断。
            if (component.Definition.kind == ComponentKind.Lamp)
            {
                return IsLampEnergized(component, powered, neutral);
            }

            return HasPhaseAndNeutral(component, powered, neutral);
        }

        private bool IsIndicatorEnergized(CircuitComponent component)
        {
            var firstTerminal = component.GetTerminal("L") ?? component.GetTerminal("A1");
            var secondTerminal = component.GetTerminal("N") ?? component.GetTerminal("A2");
            if (firstTerminal == null || secondTerminal == null)
            {
                return false;
            }

            var firstPhases = GetReachablePowerPhaseKeys(firstTerminal);
            var secondPhases = GetReachablePowerPhaseKeys(secondTerminal);
            var ratedVoltage = component.Definition != null ? component.Definition.ratedVoltage : 0f;
            if (ratedVoltage >= 300f)
            {
                return firstPhases.Any(a => secondPhases.Any(b => b != a));
            }

            return firstPhases.Count > 0 && CanReachPowerNeutral(secondTerminal) ||
                secondPhases.Count > 0 && CanReachPowerNeutral(firstTerminal);
        }

        private static bool HasPhaseAndNeutral(CircuitComponent component, HashSet<TerminalView> powered, HashSet<TerminalView> neutral)
        {
            var hasPhase = component.Terminals.Any(t => powered.Contains(t) && t.Role != TerminalRole.Neutral && t.Role != TerminalRole.CoilA2);
            var hasNeutral = component.Terminals.Any(t => neutral.Contains(t) || t.Role == TerminalRole.CoilA2 && neutral.Contains(t));
            return hasPhase && hasNeutral;
        }

        /// <summary>
        /// E4：普通交流灯泡无极性判断。两个工作端子分别落在火线节点和零线节点即可点亮，
        /// 不区分 L/N 端子方向。不排除 Neutral 角色端子，允许 N 端子落在 powered 集合、
        /// L 端子落在 neutral 集合时正常亮灯。仅对 ComponentKind.Lamp 生效，不影响风扇、
        /// 二极管、直流器件或其他有极性器件。
        /// </summary>
        private static bool IsLampEnergized(CircuitComponent component, HashSet<TerminalView> powered, HashSet<TerminalView> neutral)
        {
            var lineTerminal = component.GetTerminal("L");
            var neutralTerminal = component.GetTerminal("N");
            if (lineTerminal == null || neutralTerminal == null || lineTerminal == neutralTerminal)
            {
                return false;
            }

            // 火线与零线集合一旦重叠，当前回路已存在短路。即使 L/N 端子分别命中集合，
            // 也不能把短路回路展示为灯泡得电。
            if (powered.Overlaps(neutral))
            {
                return false;
            }

            return powered.Contains(lineTerminal) && neutral.Contains(neutralTerminal) ||
                neutral.Contains(lineTerminal) && powered.Contains(neutralTerminal);
        }

        private List<TerminalView> GetPhaseRoots()
        {
            return components
                .Where(c => c.Definition.kind == ComponentKind.PowerSource || c.Definition.kind == ComponentKind.EnergyMeter)
                .SelectMany(c => c.Terminals)
                .Where(t => t.Role == TerminalRole.Phase)
                .ToList();
        }

        private List<TerminalView> GetNeutralRoots()
        {
            return components
                .Where(c => c.Definition.kind == ComponentKind.PowerSource || c.Definition.kind == ComponentKind.EnergyMeter)
                .SelectMany(c => c.Terminals)
                .Where(t => t.Role == TerminalRole.Neutral)
                .ToList();
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

            ResolveMutualInterlockConflicts();
            SuppressStarDeltaConflictContactors();
        }

        private void SuppressStarDeltaConflictContactors()
        {
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


