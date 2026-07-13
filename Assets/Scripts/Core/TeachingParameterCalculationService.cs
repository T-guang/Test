using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
    public sealed class TeachingParameterCalculationResult
    {
        public bool HasEffectiveSinglePhaseVoltage;
        public string LineLabel;
        public float MeasuredVoltage;
        public float MeasuredCurrent;
        public float MeasuredPower;
        public float RatedPower;
    }

    public sealed class ThreePhaseMotorEstimate
    {
        public bool IsRunning;
        public float LineVoltage;
        public float RatedPower;
        public float Efficiency;
        public float PowerFactor;
        public float EstimatedCurrent;
    }

    public enum StarDeltaMotorEstimateStage
    {
        Stopped,
        Star,
        Delta,
        Conflict,
        SupplyFault,
        Unknown
    }

    public sealed class StarDeltaMotorEstimate
    {
        public StarDeltaMotorEstimateStage Stage;
        public float LineVoltage;
        public float RatedPower;
        public float Efficiency;
        public float PowerFactor;
        public float DeltaEstimatedCurrent;
        public float StarEstimatedCurrent;
        public float EstimatedCurrent;
        public bool HasNormalEstimate;
    }

    public sealed class ControlCircuitLoadEstimate
    {
        public string DisplayName;
        public string LoadType;
        public float RatedVoltage;
        public float RatedPower;
        public float RatedCurrent;
        public float ActualSupplyVoltage;
        public float ActualPower;
        public float EstimatedCurrent;
        public bool UsesRatedVoltageFallback;
        public bool HasEnoughParameters;
    }

    public enum ThermalRelaySettingJudgement
    {
        Unknown,
        TooLow,
        Reasonable,
        High,
        TooHigh
    }

    public sealed class ThermalRelaySettingEstimate
    {
        public float SettingCurrent;
        public float MotorCurrent;
        public bool UsesStaticReference;
        public ThermalRelaySettingJudgement Judgement;
        public bool HasEnoughParameters;
    }

    /// <summary>
    /// 基于活动元件参数和已推导的运行状态生成教学用途的电压、电流、功率与整定估算。
    /// 输出用于显示和检查报告，不替代工程测量、仿真求解或规则校验；拓扑异常、缺相和
    /// 星三角冲突等情况不应伪造为正常估算。修改后需回归工业模板报告与参数估算基线。
    /// </summary>
    public static class TeachingParameterCalculationService
    {
        public delegate IReadOnlyCollection<string> PhaseResolver(TerminalView terminal);
        public delegate bool NeutralResolver(TerminalView terminal);

        public static bool TryCalculateSinglePhaseLoad(
            CircuitComponent component,
            bool circuitActive,
            float sourceVoltage,
            PhaseResolver resolvePhases,
            NeutralResolver canReachNeutral,
            out TeachingParameterCalculationResult result)
        {
            result = null;
            if (!IsSinglePhaseTeachingLoad(component))
            {
                return false;
            }

            result = new TeachingParameterCalculationResult();
            if (!circuitActive || resolvePhases == null || canReachNeutral == null)
            {
                return true;
            }

            var firstTerminal = ResolveFirstTerminal(component);
            var secondTerminal = ResolveSecondTerminal(component);
            if (firstTerminal == null || secondTerminal == null)
            {
                return true;
            }

            var firstLine = ResolveSingleLineLabel(resolvePhases(firstTerminal));
            var secondLine = ResolveSingleLineLabel(resolvePhases(secondTerminal));
            var firstNeutral = canReachNeutral(firstTerminal);
            var secondNeutral = canReachNeutral(secondTerminal);

            var valid = false;
            var lineLabel = string.Empty;
            if (!string.IsNullOrWhiteSpace(firstLine) && secondNeutral && string.IsNullOrWhiteSpace(secondLine))
            {
                valid = true;
                lineLabel = firstLine;
            }
            else if (!string.IsNullOrWhiteSpace(secondLine) && firstNeutral && string.IsNullOrWhiteSpace(firstLine))
            {
                valid = true;
                lineLabel = secondLine;
            }

            if (!valid)
            {
                return true;
            }

            var voltage = ResolvePositive(sourceVoltage, ResolveParameterValue(component, ParameterKeys.RatedVoltage, component.Definition.ratedVoltage));
            var power = Mathf.Max(0f, ResolveParameterValue(component, ParameterKeys.RatedPower, component.Definition.ratedPower));
            result.HasEffectiveSinglePhaseVoltage = voltage > 0f;
            result.LineLabel = lineLabel;
            result.MeasuredVoltage = result.HasEffectiveSinglePhaseVoltage ? voltage : 0f;
            result.MeasuredPower = result.HasEffectiveSinglePhaseVoltage ? power : 0f;
            result.MeasuredCurrent = result.MeasuredVoltage > 0f ? power / result.MeasuredVoltage : 0f;
            result.RatedPower = power;
            return true;
        }

        public static bool TryCalculateThreePhaseMotor(
            CircuitComponent component,
            bool circuitActive,
            float sourceLineVoltage,
            out ThreePhaseMotorEstimate result)
        {
            result = null;
            if (!IsThreePhaseTeachingMotor(component))
            {
                return false;
            }

            var lineVoltage = ResolvePositive(sourceLineVoltage, ResolveParameterValue(component, ParameterKeys.RatedVoltage, component.Definition.ratedVoltage));
            var ratedPower = Mathf.Max(0f, ResolveParameterValue(component, ParameterKeys.RatedPower, component.Definition.ratedPower));
            var efficiency = Mathf.Max(0.01f, ResolveParameterValue(component, ParameterKeys.Efficiency, 0.85f));
            var powerFactor = Mathf.Max(0.01f, ResolveParameterValue(component, ParameterKeys.PowerFactor, 0.8f));

            result = new ThreePhaseMotorEstimate
            {
                IsRunning = circuitActive,
                LineVoltage = circuitActive ? lineVoltage : 0f,
                RatedPower = ratedPower,
                Efficiency = efficiency,
                PowerFactor = powerFactor,
                EstimatedCurrent = circuitActive && lineVoltage > 0f && ratedPower > 0f
                    ? ratedPower / (Mathf.Sqrt(3f) * lineVoltage * efficiency * powerFactor)
                    : 0f
            };

            return true;
        }

        public static bool TryCalculateStarDeltaMotor(
            CircuitComponent component,
            StarDeltaMotorEstimateStage stage,
            float sourceLineVoltage,
            out StarDeltaMotorEstimate result)
        {
            result = null;
            if (!IsStarDeltaTeachingMotor(component))
            {
                return false;
            }

            var lineVoltage = ResolvePositive(sourceLineVoltage, ResolveParameterValue(component, ParameterKeys.RatedVoltage, component.Definition.ratedVoltage));
            var ratedPower = Mathf.Max(0f, ResolveParameterValue(component, ParameterKeys.RatedPower, component.Definition.ratedPower));
            var efficiency = Mathf.Max(0.01f, ResolveParameterValue(component, ParameterKeys.Efficiency, 0.85f));
            var powerFactor = Mathf.Max(0.01f, ResolveParameterValue(component, ParameterKeys.PowerFactor, 0.8f));
            // 星形阶段仅按教学近似将三角运行线电流折算为三分之一；
            // 非正常阶段保留参数但不输出“正常运行”估算，避免掩盖冲突或供电故障。
            var deltaCurrent = lineVoltage > 0f && ratedPower > 0f
                ? ratedPower / (Mathf.Sqrt(3f) * lineVoltage * efficiency * powerFactor)
                : 0f;
            var starCurrent = deltaCurrent / 3f;
            var normal = stage == StarDeltaMotorEstimateStage.Star ||
                stage == StarDeltaMotorEstimateStage.Delta;

            result = new StarDeltaMotorEstimate
            {
                Stage = stage,
                LineVoltage = normal ? lineVoltage : 0f,
                RatedPower = ratedPower,
                Efficiency = efficiency,
                PowerFactor = powerFactor,
                DeltaEstimatedCurrent = deltaCurrent,
                StarEstimatedCurrent = starCurrent,
                EstimatedCurrent = stage == StarDeltaMotorEstimateStage.Star
                    ? starCurrent
                    : stage == StarDeltaMotorEstimateStage.Delta
                        ? deltaCurrent
                        : 0f,
                HasNormalEstimate = normal && lineVoltage > 0f && ratedPower > 0f
            };

            return true;
        }

        public static bool TryEstimateControlCircuitLoad(
            CircuitComponent component,
            float actualSupplyVoltage,
            bool hasActualSupplyVoltage,
            out ControlCircuitLoadEstimate result)
        {
            result = null;
            if (!IsControlCircuitTeachingLoad(component))
            {
                return false;
            }

            var ratedVoltage = ResolveParameterValue(component, ParameterKeys.RatedVoltage, component.Definition.ratedVoltage);
            if (ratedVoltage <= 0f)
            {
                ratedVoltage = ParameterValueResolver.GetFloatOrFallback(
                    component,
                    component.Definition.sourceVoltage,
                    ParameterAliases.SourceVoltage);
            }

            var ratedPower = ResolveParameterValue(component, ParameterKeys.RatedPower, component.Definition.ratedPower);
            var ratedCurrent = ResolveParameterValue(component, ParameterKeys.RatedCurrent, component.Definition.ratedCurrent);
            if (ratedPower <= 0f && ratedCurrent > 0f && ratedVoltage > 0f)
            {
                ratedPower = ratedVoltage * ratedCurrent;
            }

            // 真实供电不可确认时才回退额定电压，并在结果中显式标记；
            // 报告层可据此说明估算依据，不能把回退值误表示为实测供电。
            var resolvedActualVoltage = hasActualSupplyVoltage && actualSupplyVoltage > 0f
                ? actualSupplyVoltage
                : ratedVoltage;
            var hasEnoughParameters = ratedVoltage > 0f && (ratedPower > 0f || ratedCurrent > 0f);
            var estimatedCurrent = 0f;
            var actualPower = 0f;
            if (hasEnoughParameters && resolvedActualVoltage > 0f)
            {
                if (ratedPower > 0f && ratedVoltage > 0f)
                {
                    estimatedCurrent = ratedPower * resolvedActualVoltage / (ratedVoltage * ratedVoltage);
                    actualPower = resolvedActualVoltage * estimatedCurrent;
                }
                else
                {
                    estimatedCurrent = ratedCurrent;
                    actualPower = resolvedActualVoltage * estimatedCurrent;
                }
            }

            result = new ControlCircuitLoadEstimate
            {
                DisplayName = component.Definition.displayName,
                LoadType = ControlLoadTypeName(component),
                RatedVoltage = Mathf.Max(0f, ratedVoltage),
                RatedPower = Mathf.Max(0f, ratedPower),
                RatedCurrent = Mathf.Max(0f, ratedCurrent),
                ActualSupplyVoltage = Mathf.Max(0f, resolvedActualVoltage),
                ActualPower = Mathf.Max(0f, actualPower),
                EstimatedCurrent = Mathf.Max(0f, estimatedCurrent),
                UsesRatedVoltageFallback = !hasActualSupplyVoltage || actualSupplyVoltage <= 0f,
                HasEnoughParameters = hasEnoughParameters
            };

            return true;
        }

        public static bool TryEstimateThermalRelaySetting(
            CircuitComponent thermalRelay,
            float motorCurrent,
            bool usesStaticReference,
            out ThermalRelaySettingEstimate result)
        {
            result = null;
            if (!IsThermalRelay(thermalRelay))
            {
                return false;
            }

            var settingCurrent = ResolveParameterValue(thermalRelay, ParameterKeys.SettingCurrent, 0f);
            if (settingCurrent <= 0f)
            {
                settingCurrent = ResolveParameterValue(thermalRelay, ParameterKeys.RatedCurrent, thermalRelay.Definition.ratedCurrent);
            }

            var hasEnoughParameters = settingCurrent > 0f && motorCurrent > 0f;
            var judgement = ThermalRelaySettingJudgement.Unknown;
            if (hasEnoughParameters)
            {
                if (settingCurrent < 0.8f * motorCurrent)
                {
                    judgement = ThermalRelaySettingJudgement.TooLow;
                }
                else if (settingCurrent <= 1.2f * motorCurrent)
                {
                    judgement = ThermalRelaySettingJudgement.Reasonable;
                }
                else if (settingCurrent <= 1.8f * motorCurrent)
                {
                    judgement = ThermalRelaySettingJudgement.High;
                }
                else
                {
                    judgement = ThermalRelaySettingJudgement.TooHigh;
                }
            }

            result = new ThermalRelaySettingEstimate
            {
                SettingCurrent = Mathf.Max(0f, settingCurrent),
                MotorCurrent = Mathf.Max(0f, motorCurrent),
                UsesStaticReference = usesStaticReference,
                Judgement = judgement,
                HasEnoughParameters = hasEnoughParameters
            };
            return true;
        }

        public static bool IsSinglePhaseTeachingLoad(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                !component.Definition.canParticipateInParameterCalculation)
            {
                return false;
            }

            if (component.Definition.kind == ComponentKind.Lamp ||
                component.Definition.kind == ComponentKind.Fan)
            {
                return true;
            }

            if (component.Definition.kind != ComponentKind.Indicator)
            {
                return false;
            }

            var ratedVoltage = ResolveParameterValue(component, ParameterKeys.RatedVoltage, component.Definition.ratedVoltage);
            return ratedVoltage > 0f && ratedVoltage < 300f;
        }

        public static bool IsThreePhaseTeachingMotor(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.canParticipateInParameterCalculation &&
                component.Definition.kind == ComponentKind.Motor &&
                component.GetTerminal(TerminalConstants.U) != null &&
                component.GetTerminal(TerminalConstants.V) != null &&
                component.GetTerminal(TerminalConstants.W) != null &&
                component.GetTerminal(TerminalConstants.U1) == null &&
                component.GetTerminal(TerminalConstants.V1) == null &&
                component.GetTerminal(TerminalConstants.W1) == null;
        }

        public static bool IsStarDeltaTeachingMotor(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.canParticipateInParameterCalculation &&
                component.Definition.kind == ComponentKind.Motor &&
                component.GetTerminal(TerminalConstants.U1) != null &&
                component.GetTerminal(TerminalConstants.V1) != null &&
                component.GetTerminal(TerminalConstants.W1) != null &&
                component.GetTerminal(TerminalConstants.U2) != null &&
                component.GetTerminal(TerminalConstants.V2) != null &&
                component.GetTerminal(TerminalConstants.W2) != null;
        }

        public static bool IsControlCircuitTeachingLoad(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                component.Definition.supportLevel == ComponentSupportLevel.VisualOnly ||
                !component.Definition.canParticipateInRuntime)
            {
                return false;
            }

            if (component.Definition.kind == ComponentKind.Indicator)
            {
                return component.Definition.canParticipateInParameterCalculation;
            }

            if (IsTimerRelay(component))
            {
                return true;
            }

            return IsContactor(component);
        }

        public static bool IsThermalRelay(CircuitComponent component)
        {
            if (component == null ||
                component.Definition == null ||
                component.GetTerminal(TerminalConstants.ThermalNC95) == null ||
                component.GetTerminal(TerminalConstants.ThermalNC96) == null)
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("ThermalRelay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("Thermal_Relay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.Contains("热继") ||
                displayName.Contains("鐑户");
        }

        public static float ResolveParameterValue(CircuitComponent component, string key, float fallback)
        {
            return ParameterValueResolver.GetFloatOrFallback(component, fallback, key);
        }

        public static string LoadDisplayName(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return "负载";
            }

            if (component.Definition.kind == ComponentKind.Fan)
            {
                return "电风扇";
            }

            if (component.Definition.kind == ComponentKind.Indicator)
            {
                return "220V 指示灯";
            }

            return "灯泡";
        }

        private static string ControlLoadTypeName(CircuitComponent component)
        {
            if (component.Definition.kind == ComponentKind.Indicator)
            {
                return "指示灯";
            }

            if (IsTimerRelay(component))
            {
                return "时间继电器 KT 线圈";
            }

            return "接触器线圈";
        }

        private static bool IsContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            if (IsTimerRelay(component))
            {
                return false;
            }

            return component.Definition.kind == ComponentKind.ContactorCoil ||
                component.GetTerminal(TerminalConstants.A1) != null &&
                component.GetTerminal(TerminalConstants.A2) != null &&
                component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.T1) != null;
        }

        private static bool IsTimerRelay(CircuitComponent component)
        {
            if (component == null || component.Definition == null ||
                component.GetTerminal(TerminalConstants.A1) == null || component.GetTerminal(TerminalConstants.A2) == null)
            {
                return false;
            }

            var id = component.Definition.name ?? string.Empty;
            var displayName = component.Definition.displayName ?? string.Empty;
            return id.IndexOf("Timer_", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("TimerRelay", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                displayName.Contains("时间继电器");
        }

        private static TerminalView ResolveFirstTerminal(CircuitComponent component)
        {
            return component.GetTerminal(TerminalConstants.L) ?? component.GetTerminal(TerminalConstants.A1);
        }

        private static TerminalView ResolveSecondTerminal(CircuitComponent component)
        {
            return component.GetTerminal(TerminalConstants.N) ?? component.GetTerminal(TerminalConstants.A2);
        }

        private static float ResolvePositive(float preferred, float fallback)
        {
            return preferred > 0f ? preferred : Mathf.Max(0f, fallback);
        }

        private static string ResolveSingleLineLabel(IReadOnlyCollection<string> phaseKeys)
        {
            if (phaseKeys == null || phaseKeys.Count != 1)
            {
                return string.Empty;
            }

            foreach (var key in phaseKeys)
            {
                return NormalizeLineLabel(key);
            }

            return string.Empty;
        }

        private static string NormalizeLineLabel(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var dot = key.LastIndexOf('.');
            return dot >= 0 && dot < key.Length - 1 ? key.Substring(dot + 1) : key;
        }
    }
}
