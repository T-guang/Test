using System.Collections.Generic;

namespace ElectricalSim.Core
{
    public enum ActualSupplyVoltageKind
    {
        Unknown,
        SinglePhase,
        ThreePhaseLine
    }

    public readonly struct ActualSupplyVoltageResult
    {
        public ActualSupplyVoltageResult(
            bool resolved,
            float voltage,
            ActualSupplyVoltageKind kind,
            string reason)
        {
            Resolved = resolved;
            Voltage = voltage;
            Kind = kind;
            Reason = reason;
        }

        public bool Resolved { get; }
        public float Voltage { get; }
        public ActualSupplyVoltageKind Kind { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 从活动工作区中的电源元件和端子电压标签解析实际供电电压。
    /// 只读取元件实例参数及其定义回退值，不修改元件、拓扑或运行态；调用方应传入
    /// WorkspaceController.Components，而不是扫描场景中的全部 CircuitComponent。
    /// </summary>
    public static class ActualSupplyVoltageResolver
    {
        public const float DefaultSinglePhaseVoltage = 220f;
        public const float DefaultThreePhaseLineVoltage = 380f;

        public static float ResolveSinglePhaseVoltage(
            IReadOnlyList<CircuitComponent> components,
            float fallback = DefaultSinglePhaseVoltage)
        {
            if (components == null)
            {
                return fallback;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsPowerSource(component))
                {
                    continue;
                }

                var voltage = ParameterValueResolver.GetFloatOrFallback(
                    component,
                    fallback,
                    ParameterAliases.SourceVoltage);
                if (voltage > 0f)
                {
                    return voltage;
                }
            }

            return fallback;
        }

        public static float ResolveThreePhaseLineVoltage(
            IReadOnlyList<CircuitComponent> components,
            float fallback = DefaultThreePhaseLineVoltage)
        {
            if (components == null)
            {
                return fallback;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsPowerSource(component) || !HasThreePhaseOutputTerminals(component))
                {
                    continue;
                }

                var lineVoltage = ResolvePowerSourceLineVoltage(component, fallback);
                if (lineVoltage > 0f)
                {
                    return lineVoltage;
                }
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsPowerSource(component))
                {
                    continue;
                }

                var lineVoltage = ResolvePowerSourceLineVoltage(component, 0f);
                if (lineVoltage > 0f)
                {
                    return lineVoltage;
                }
            }

            return fallback;
        }

        public static ActualSupplyVoltageResult ResolveAcrossVoltageLabels(
            IReadOnlyList<CircuitComponent> components,
            string firstVoltage,
            string secondVoltage)
        {
            if (string.IsNullOrWhiteSpace(firstVoltage) || string.IsNullOrWhiteSpace(secondVoltage))
            {
                return new ActualSupplyVoltageResult(false, 0f, ActualSupplyVoltageKind.Unknown, "Terminal voltage label is empty.");
            }

            // 端子标签优先决定电压种类；不能仅凭元件名称猜测 220V/380V，
            // 否则同类不同规格元件会得到错误的线圈或负载供电判断。
            if ((IsLineOrPhase(firstVoltage) && secondVoltage == TerminalConstants.N) ||
                (IsLineOrPhase(secondVoltage) && firstVoltage == TerminalConstants.N))
            {
                var voltage = ResolveSinglePhaseVoltage(components);
                return new ActualSupplyVoltageResult(
                    voltage > 0f,
                    voltage,
                    ActualSupplyVoltageKind.SinglePhase,
                    "Resolved as line-neutral voltage.");
            }

            if (IsThreePhaseLine(firstVoltage) &&
                IsThreePhaseLine(secondVoltage) &&
                firstVoltage != secondVoltage)
            {
                var voltage = ResolveThreePhaseLineVoltage(components);
                return new ActualSupplyVoltageResult(
                    voltage > 0f,
                    voltage,
                    ActualSupplyVoltageKind.ThreePhaseLine,
                    "Resolved as phase-phase line voltage.");
            }

            return new ActualSupplyVoltageResult(false, 0f, ActualSupplyVoltageKind.Unknown, "Terminal voltage labels do not form a supported voltage pair.");
        }

        public static ActualSupplyVoltageResult ResolveForEnergizedControlLoad(
            IReadOnlyList<CircuitComponent> components,
            CircuitComponent component)
        {
            if (component == null || !component.IsEnergized)
            {
                return new ActualSupplyVoltageResult(false, 0f, ActualSupplyVoltageKind.Unknown, "Control load is not energized.");
            }

            var ratedVoltage = ParameterValueResolver.GetFloatOrFallback(
                component,
                0f,
                ParameterKeys.RatedVoltage,
                ParameterAliases.SourceVoltage[0],
                ParameterAliases.SourceVoltage[1]);
            if (ratedVoltage <= 0f)
            {
                return new ActualSupplyVoltageResult(false, 0f, ActualSupplyVoltageKind.Unknown, "Rated voltage is not available.");
            }

            if (ratedVoltage >= 300f)
            {
                var lineVoltage = ResolveThreePhaseLineVoltage(components);
                return new ActualSupplyVoltageResult(
                    lineVoltage > 0f,
                    lineVoltage,
                    ActualSupplyVoltageKind.ThreePhaseLine,
                    "Resolved from energized 380V-rated control load.");
            }

            var phaseVoltage = ResolveSinglePhaseVoltage(components);
            return new ActualSupplyVoltageResult(
                phaseVoltage > 0f,
                phaseVoltage,
                ActualSupplyVoltageKind.SinglePhase,
                "Resolved from energized single-phase control load.");
        }

        private static bool IsPowerSource(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.PowerSource;
        }

        private static bool HasThreePhaseOutputTerminals(CircuitComponent component)
        {
            return component != null &&
                component.GetTerminal(TerminalConstants.L1) != null &&
                component.GetTerminal(TerminalConstants.L2) != null &&
                component.GetTerminal(TerminalConstants.L3) != null;
        }

        private static float ResolvePowerSourceLineVoltage(CircuitComponent component, float fallback)
        {
            if (component == null || component.Definition == null)
            {
                return fallback;
            }

            return ParameterValueResolver.GetFloatOrFallback(
                component,
                fallback,
                ParameterAliases.SourceLineVoltage);
        }

        private static bool IsLineOrPhase(string voltage)
        {
            return voltage == TerminalConstants.L || IsThreePhaseLine(voltage);
        }

        private static bool IsThreePhaseLine(string voltage)
        {
            return voltage == TerminalConstants.L1 ||
                voltage == TerminalConstants.L2 ||
                voltage == TerminalConstants.L3;
        }
    }
}
