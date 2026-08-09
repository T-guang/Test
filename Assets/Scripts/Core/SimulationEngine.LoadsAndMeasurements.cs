using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    public sealed partial class SimulationEngine
    {
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

            // 普通交流灯泡按无极性负载处理：工作端分别到达火线和零线即可得电，端子标签交换不改变判断。
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
        /// 普通交流灯泡无极性判断：两个工作端子分别落在火线节点和零线节点即可点亮，
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

    }
}
