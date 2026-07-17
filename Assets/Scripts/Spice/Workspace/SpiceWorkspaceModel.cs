using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ElectricalSim.Spice.Core;
using UnityEngine;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// T3 原型的唯一电路事实来源。它与正式 WorkspaceController 完全隔离，
    /// 只保存可映射为 SpiceCircuitModel 的元件、导线和画布位置。
    /// </summary>
    public sealed class SpiceWorkspaceModel
    {
        private readonly List<SpiceWorkspaceComponentData> components = new List<SpiceWorkspaceComponentData>();
        private readonly List<SpiceWorkspaceWireData> wires = new List<SpiceWorkspaceWireData>();
        private readonly Dictionary<SpiceComponentKind, int> nextInstanceNumbers = new Dictionary<SpiceComponentKind, int>();

        public IReadOnlyList<SpiceWorkspaceComponentData> Components => components;
        public IReadOnlyList<SpiceWorkspaceWireData> Wires => wires;
        public event Action<SpiceWorkspaceChange> Changed;

        public SpiceWorkspaceComponentData AddComponent(SpiceComponentKind kind, Vector2 position)
        {
            var number = nextInstanceNumbers.TryGetValue(kind, out var current) ? current + 1 : 1;
            nextInstanceNumbers[kind] = number;
            var component = new SpiceWorkspaceComponentData(BuildInstanceId(kind, number), kind, position, DefaultValue(kind));
            components.Add(component);
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
            return component;
        }

        public bool RemoveComponent(string instanceId)
        {
            var component = FindComponent(instanceId);
            if (component == null) return false;
            components.Remove(component);
            wires.RemoveAll(wire => wire.StartComponentId == instanceId || wire.EndComponentId == instanceId);
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
            return true;
        }

        public bool AddWire(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            if (string.Equals(startComponentId, endComponentId, StringComparison.Ordinal) && string.Equals(startTerminalId, endTerminalId, StringComparison.Ordinal)) return false;
            var start = FindComponent(startComponentId);
            var end = FindComponent(endComponentId);
            if (start == null || end == null || !start.HasTerminal(startTerminalId) || !end.HasTerminal(endTerminalId)) return false;
            wires.Add(new SpiceWorkspaceWireData(startComponentId, startTerminalId, endComponentId, endTerminalId));
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
            return true;
        }

        public bool RemoveWire(SpiceWorkspaceWireData wire)
        {
            if (wire == null || !wires.Remove(wire)) return false;
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
            return true;
        }

        public void MoveComponent(string instanceId, Vector2 position)
        {
            var component = FindComponent(instanceId);
            if (component != null) component.Position = position;
        }

        public bool TrySetParameter(string instanceId, double value)
        {
            var component = FindComponent(instanceId);
            if (component == null || !IsValidParameter(component.Kind, value)) return false;
            component.SiValue = value;
            Changed?.Invoke(SpiceWorkspaceChange.Parameter);
            return true;
        }

        public void Clear()
        {
            if (components.Count == 0 && wires.Count == 0) return;
            components.Clear();
            wires.Clear();
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
        }

        public SpiceCircuitModel BuildCircuitModel()
        {
            var circuit = new SpiceCircuitModel();
            foreach (var component in components)
            {
                circuit.Components.Add(component.ToSpiceComponentModel());
            }
            foreach (var wire in wires)
            {
                circuit.Wires.Add(new SpiceWireModel(
                    new SpiceTerminalRef(wire.StartComponentId, wire.StartTerminalId),
                    new SpiceTerminalRef(wire.EndComponentId, wire.EndTerminalId)));
            }
            return circuit;
        }

        public SpiceWorkspaceComponentData FindComponent(string instanceId)
        {
            return components.FirstOrDefault(component => string.Equals(component.InstanceId, instanceId, StringComparison.Ordinal));
        }

        public static bool IsValidParameter(SpiceComponentKind kind, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return false;
            return kind == SpiceComponentKind.DcVoltageSource || (kind != SpiceComponentKind.Ground && value > 0d);
        }

        private static string BuildInstanceId(SpiceComponentKind kind, int number)
        {
            var prefix = kind == SpiceComponentKind.DcVoltageSource ? "source" :
                kind == SpiceComponentKind.Resistor ? "resistor" :
                kind == SpiceComponentKind.Capacitor ? "capacitor" :
                kind == SpiceComponentKind.Inductor ? "inductor" : "ground";
            return prefix + "-" + number.ToString("D3", CultureInfo.InvariantCulture);
        }

        private static double DefaultValue(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? 10d :
                kind == SpiceComponentKind.Resistor ? 1000d :
                kind == SpiceComponentKind.Capacitor ? 1e-6d :
                kind == SpiceComponentKind.Inductor ? 0.01d : 0d;
        }
    }

    public enum SpiceWorkspaceChange { Topology, Parameter }

    public sealed class SpiceWorkspaceComponentData
    {
        public SpiceWorkspaceComponentData(string instanceId, SpiceComponentKind kind, Vector2 position, double siValue)
        {
            InstanceId = instanceId;
            Kind = kind;
            Position = position;
            SiValue = siValue;
        }

        public string InstanceId { get; }
        public SpiceComponentKind Kind { get; }
        public Vector2 Position { get; set; }
        public double SiValue { get; set; }
        public bool HasTerminal(string terminalId) => Kind == SpiceComponentKind.Ground
            ? string.Equals(terminalId, SpiceComponentModel.GroundTerminalId, StringComparison.Ordinal)
            : string.Equals(terminalId, SpiceComponentModel.PositiveTerminalId, StringComparison.Ordinal) || string.Equals(terminalId, SpiceComponentModel.NegativeTerminalId, StringComparison.Ordinal);

        public SpiceComponentModel ToSpiceComponentModel()
        {
            switch (Kind)
            {
                case SpiceComponentKind.DcVoltageSource: return SpiceComponentModel.DcVoltageSource(InstanceId, SiValue);
                case SpiceComponentKind.Resistor: return SpiceComponentModel.Resistor(InstanceId, SiValue);
                case SpiceComponentKind.Capacitor: return SpiceComponentModel.Capacitor(InstanceId, SiValue);
                case SpiceComponentKind.Inductor: return SpiceComponentModel.Inductor(InstanceId, SiValue);
                case SpiceComponentKind.Ground: return SpiceComponentModel.Ground(InstanceId);
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }

    public sealed class SpiceWorkspaceWireData
    {
        public SpiceWorkspaceWireData(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            StartComponentId = startComponentId;
            StartTerminalId = startTerminalId;
            EndComponentId = endComponentId;
            EndTerminalId = endTerminalId;
        }

        public string StartComponentId { get; }
        public string StartTerminalId { get; }
        public string EndComponentId { get; }
        public string EndTerminalId { get; }
    }

    public static class SpiceParameterUnits
    {
        public static readonly string[] VoltageUnits = { "V" };
        public static readonly string[] ResistanceUnits = { "Ohm", "kOhm", "MOhm" };
        public static readonly string[] CapacitanceUnits = { "F", "mF", "uF", "nF", "pF" };
        public static readonly string[] InductanceUnits = { "H", "mH", "uH" };

        public static bool TryToSi(SpiceComponentKind kind, double displayValue, string unit, out double siValue)
        {
            siValue = displayValue * Multiplier(kind, unit);
            return SpiceWorkspaceModel.IsValidParameter(kind, siValue);
        }

        public static double FromSi(SpiceComponentKind kind, double siValue, string unit)
        {
            return siValue / Multiplier(kind, unit);
        }

        public static string[] UnitsFor(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? VoltageUnits :
                kind == SpiceComponentKind.Resistor ? ResistanceUnits :
                kind == SpiceComponentKind.Capacitor ? CapacitanceUnits :
                kind == SpiceComponentKind.Inductor ? InductanceUnits : Array.Empty<string>();
        }

        private static double Multiplier(SpiceComponentKind kind, string unit)
        {
            if (kind == SpiceComponentKind.Resistor) return unit == "kOhm" ? 1e3d : unit == "MOhm" ? 1e6d : 1d;
            if (kind == SpiceComponentKind.Capacitor) return unit == "mF" ? 1e-3d : unit == "uF" ? 1e-6d : unit == "nF" ? 1e-9d : unit == "pF" ? 1e-12d : 1d;
            if (kind == SpiceComponentKind.Inductor) return unit == "mH" ? 1e-3d : unit == "uH" ? 1e-6d : 1d;
            return 1d;
        }
    }
}
