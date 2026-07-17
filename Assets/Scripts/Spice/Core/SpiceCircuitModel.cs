using System;
using System.Collections.Generic;

namespace ElectricalSim.Spice.Core
{
    public enum SpiceComponentKind
    {
        DcVoltageSource,
        Resistor,
        Capacitor,
        Inductor,
        Ground
    }

    public enum SpiceParameterKey
    {
        DcVoltage,
        Resistance,
        Capacitance,
        Inductance
    }

    public sealed class SpiceParameterValue
    {
        public SpiceParameterValue(SpiceParameterKey key, double value)
        {
            Key = key;
            Value = value;
        }

        public SpiceParameterKey Key { get; }
        public double Value { get; }
    }

    public sealed class SpiceComponentModel
    {
        public const string PositiveTerminalId = "positive";
        public const string NegativeTerminalId = "negative";
        public const string GroundTerminalId = "ground";

        private readonly Dictionary<SpiceParameterKey, double> parameters = new Dictionary<SpiceParameterKey, double>();

        public SpiceComponentModel(string instanceId, SpiceComponentKind kind)
        {
            InstanceId = instanceId;
            Kind = kind;
        }

        public string InstanceId { get; }
        public SpiceComponentKind Kind { get; }
        public IReadOnlyDictionary<SpiceParameterKey, double> Parameters => parameters;

        public static SpiceComponentModel DcVoltageSource(string instanceId, double volts)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.DcVoltageSource).With(SpiceParameterKey.DcVoltage, volts);
        }

        public static SpiceComponentModel Resistor(string instanceId, double ohms)
        {
            return new SpiceComponentModel(instanceId, SpiceComponentKind.Resistor).With(SpiceParameterKey.Resistance, ohms);
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

            return string.Equals(terminalId, PositiveTerminalId, StringComparison.Ordinal) ||
                   string.Equals(terminalId, NegativeTerminalId, StringComparison.Ordinal);
        }

        public bool TryGetParameter(SpiceParameterKey key, out double value)
        {
            return parameters.TryGetValue(key, out value);
        }
    }

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

    public sealed class SpiceCircuitModel
    {
        public List<SpiceComponentModel> Components { get; } = new List<SpiceComponentModel>();
        public List<SpiceWireModel> Wires { get; } = new List<SpiceWireModel>();
    }
}
