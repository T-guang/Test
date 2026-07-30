using System;
using System.Collections.Generic;

namespace ElectricalSim.Spice.Core
{
    /// <summary>
    /// T2 支持的最小直流元件集合；枚举只描述网表元件类别，不承担电路求解。
    /// </summary>
    public enum SpiceComponentKind
    {
        DcVoltageSource,
        DcCurrentSource,
        IdealSwitch,
        SiliconDiode,
        Resistor,
        Capacitor,
        Inductor,
        Ground,
        VoltageProbe,
        CurrentProbe,
        AcVoltageSource,
        IdealOperationalAmplifier
    }

    public enum SpiceParameterKey
    {
        DcVoltage,
        DcCurrent,
        SwitchClosed,
        Resistance,
        Capacitance,
        Inductance,
        AcMagnitude
    }

    /// <summary>
    /// 固定器件常量只在 Core 中定义，避免网表、工作区和测试各自复制模型参数。
    /// </summary>
    public static class SpiceComponentDefaults
    {
        // 以有限 1e6 增益近似理想运放，既保留负反馈误差的可验证数值，也避免无穷增益在数值求解中形成不可定义的约束。
        public const double IdealOperationalAmplifierOpenLoopGain = 1e6d;
    }

    /// <summary>
    /// 与 Unity 场景无关的元件实例。T2 所有数值均使用 SI 基础单位：V、Ohm、F、H。
    /// </summary>
    public sealed class SpiceComponentModel
    {
        // 所有双端器件统一使用这组端子；电压和元件电流均按 positive 到 negative 的方向表达。
        public const string PositiveTerminalId = "positive";
        public const string NegativeTerminalId = "negative";
        public const string GroundTerminalId = "ground";
        public const string NonInvertingTerminalId = "nonInverting";
        public const string InvertingTerminalId = "inverting";
        public const string OutputTerminalId = "output";

        private readonly Dictionary<SpiceParameterKey, double> parameters = new Dictionary<SpiceParameterKey, double>();

        public SpiceComponentModel(string instanceId, SpiceComponentKind kind, double acPhaseDegrees = 0d)
        {
            InstanceId = instanceId;
            Kind = kind;
            AcPhaseDegrees = acPhaseDegrees;
        }

        public string InstanceId { get; }
        public SpiceComponentKind Kind { get; }
        public IReadOnlyDictionary<SpiceParameterKey, double> Parameters => parameters;
        public double AcPhaseDegrees { get; }

        public static SpiceComponentModel DcVoltageSource(string instanceId, double volts)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.DcVoltageSource).With(SpiceParameterKey.DcVoltage, volts);
        }

        public static SpiceComponentModel AcVoltageSource(string instanceId, double magnitudeVolts, double phaseDegrees)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.AcVoltageSource, SpiceAnalysisLimits.NormalizePhaseDegrees(phaseDegrees))
                .With(SpiceParameterKey.AcMagnitude, magnitudeVolts);
        }

        public static SpiceComponentModel Resistor(string instanceId, double ohms)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.Resistor).With(SpiceParameterKey.Resistance, ohms);
        }

        public static SpiceComponentModel DcCurrentSource(string instanceId, double amps)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.DcCurrentSource).With(SpiceParameterKey.DcCurrent, amps);
        }

        public static SpiceComponentModel IdealSwitch(string instanceId, bool closed)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.IdealSwitch).With(SpiceParameterKey.SwitchClosed, closed ? 1d : 0d);
        }

        public static SpiceComponentModel SiliconDiode(string instanceId)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.SiliconDiode);
        }

        public static SpiceComponentModel Capacitor(string instanceId, double farads)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.Capacitor).With(SpiceParameterKey.Capacitance, farads);
        }

        public static SpiceComponentModel Inductor(string instanceId, double henries)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.Inductor).With(SpiceParameterKey.Inductance, henries);
        }

        public static SpiceComponentModel Ground(string instanceId)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.Ground);
        }

        public static SpiceComponentModel IdealOperationalAmplifier(string instanceId)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.IdealOperationalAmplifier);
        }

        /// <summary>
        /// 两端差分电压探针。positive=V+、negative=V-；测量定义为 Vprobe = V(V+) - V(V-)。
        /// 探针不产生 SPICE 元件行，不注入电流，不参与 component-graph 连通性判断。
        /// </summary>
        public static SpiceComponentModel VoltageProbe(string instanceId)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.VoltageProbe);
        }

        /// <summary>
        /// 串联电流探针。positive=IN、negative=OUT；网表使用 0 V 电压源并读取 i(VPROBE)。
        /// </summary>
        public static SpiceComponentModel CurrentProbe(string instanceId)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.CurrentProbe);
        }

        public SpiceComponentModel With(SpiceParameterKey key, double value)
        {
            parameters[key] = value;
            return this;
        }

        public bool HasTerminal(string terminalId)
        {
            if (Kind == SpiceComponentKind.Ground)
            {
                return string.Equals(terminalId, GroundTerminalId, StringComparison.Ordinal);
            }

            if (Kind == SpiceComponentKind.IdealOperationalAmplifier)
            {
                return string.Equals(terminalId, NonInvertingTerminalId, StringComparison.Ordinal) ||
                       string.Equals(terminalId, InvertingTerminalId, StringComparison.Ordinal) ||
                       string.Equals(terminalId, OutputTerminalId, StringComparison.Ordinal);
            }

            return string.Equals(terminalId, PositiveTerminalId, StringComparison.Ordinal) ||
                   string.Equals(terminalId, NegativeTerminalId, StringComparison.Ordinal);
        }

        public static IReadOnlyList<string> TerminalIdsFor(SpiceComponentKind kind)
        {
            if (kind == SpiceComponentKind.Ground) return new[] { GroundTerminalId };
            // 三端子顺序是稳定拓扑契约：IN+、IN-、OUT；网表 VCVS 也按该语义读取，不能按 UI 排列推断。
            if (kind == SpiceComponentKind.IdealOperationalAmplifier)
                return new[] { NonInvertingTerminalId, InvertingTerminalId, OutputTerminalId };
            return new[] { PositiveTerminalId, NegativeTerminalId };
        }

        public bool TryGetParameter(SpiceParameterKey key, out double value)
        {
            return parameters.TryGetValue(key, out value);
        }

        public double GetRequiredParameter(SpiceParameterKey key)
        {
            if (!parameters.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException("Required SPICE parameter is missing: " + key + ".");
            }

            return value;
        }
    }

    /// <summary>
    /// Wire 端点的稳定关联键；其唯一性只在当前 SpiceCircuitModel 的实例集合内成立。
    /// </summary>
    public sealed class SpiceTerminalRef : IEquatable<SpiceTerminalRef>
    {
        public SpiceTerminalRef(string componentInstanceId, string terminalId)
        {
            ComponentInstanceId = componentInstanceId;
            TerminalId = terminalId;
        }

        public string ComponentInstanceId { get; }
        public string TerminalId { get; }

        public bool Equals(SpiceTerminalRef other)
        {
            return other != null &&
                   string.Equals(ComponentInstanceId, other.ComponentInstanceId, StringComparison.Ordinal) &&
                   string.Equals(TerminalId, other.TerminalId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as SpiceTerminalRef);
        public override int GetHashCode() => ((ComponentInstanceId ?? string.Empty).GetHashCode() * 397) ^ (TerminalId ?? string.Empty).GetHashCode();
        public override string ToString() => (ComponentInstanceId ?? string.Empty) + ":" + (TerminalId ?? string.Empty);
    }

    public sealed class SpiceWireModel
    {
        public SpiceWireModel(SpiceTerminalRef start, SpiceTerminalRef end)
        {
            Start = start;
            End = end;
        }

        public SpiceTerminalRef Start { get; }
        public SpiceTerminalRef End { get; }
    }

    /// <summary>
    /// 当前计算的纯数据输入。拓扑不会写回元件；每次求解都从 Components 和 Wires 重新构建。
    /// </summary>
    public sealed class SpiceCircuitModel
    {
        public SpiceCircuitModel()
            : this(new SpiceAnalysisSettings(SpiceAnalysisMode.DcOperatingPoint, SpiceAnalysisLimits.DefaultFrequencyHz))
        {
        }

        public SpiceCircuitModel(SpiceAnalysisSettings analysisSettings)
        {
            AnalysisSettings = (analysisSettings ?? throw new ArgumentNullException(nameof(analysisSettings))).Copy();
        }

        public SpiceAnalysisSettings AnalysisSettings { get; }
        public List<SpiceComponentModel> Components { get; } = new List<SpiceComponentModel>();
        public List<SpiceWireModel> Wires { get; } = new List<SpiceWireModel>();
    }
}
