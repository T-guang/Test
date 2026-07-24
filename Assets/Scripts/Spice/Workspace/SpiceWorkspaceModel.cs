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

        /// <summary>
        /// 按指定 InstanceId 和旋转创建组件，供导入保留文件内原始身份使用。
        /// 不推进 nextInstanceNumbers；导入完成后应调用 RestoreInstanceNumbersFromExisting 恢复下一编号。
        /// </summary>
        public SpiceWorkspaceComponentData AddComponentWithIdentity(SpiceComponentKind kind, string instanceId, Vector2 position, double siValue, int rotationQuarterTurns)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("InstanceId is required.", nameof(instanceId));
            if (FindComponent(instanceId) != null) throw new InvalidOperationException("Duplicate InstanceId: " + instanceId);
            // 无参数器件（GND/二极管/探针）使用固定默认值 0，不通过 IsValidParameter 校验用户参数。
            var effectiveValue = HasUserParameter(kind) ? siValue : DefaultValue(kind);
            if (HasUserParameter(kind) && !IsValidParameter(kind, siValue)) throw new ArgumentOutOfRangeException(nameof(siValue), "Parameter is invalid for " + kind + ".");
            var clampedRotation = ((rotationQuarterTurns % 4) + 4) % 4;
            var component = new SpiceWorkspaceComponentData(instanceId, kind, position, effectiveValue, clampedRotation);
            components.Add(component);
            Changed?.Invoke(SpiceWorkspaceChange.Topology);
            return component;
        }

        /// <summary>
        /// 导入完成后，根据已存在组件的 InstanceId 数字部分恢复各类型下一编号，
        /// 确保后续新建组件编号 = max(已存在编号) + 1。
        /// </summary>
        public void RestoreInstanceNumbersFromExisting()
        {
            nextInstanceNumbers.Clear();
            foreach (var component in components)
            {
                var number = ExtractInstanceNumber(component.InstanceId);
                if (number <= 0) continue;
                var kind = component.Kind;
                if (!nextInstanceNumbers.TryGetValue(kind, out var current) || number > current)
                {
                    nextInstanceNumbers[kind] = number;
                }
            }
        }

        private static int ExtractInstanceNumber(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return 0;
            var dashIndex = instanceId.LastIndexOf('-');
            if (dashIndex < 0 || dashIndex + 1 >= instanceId.Length) return 0;
            var digits = instanceId.Substring(dashIndex + 1);
            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
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

        public bool AddWire(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId, SpiceWireVisualState visualState = null)
        {
            if (string.Equals(startComponentId, endComponentId, StringComparison.Ordinal)) return false;
            var start = FindComponent(startComponentId);
            var end = FindComponent(endComponentId);
            if (start == null || end == null || !start.HasTerminal(startTerminalId) || !end.HasTerminal(endTerminalId)) return false;
            wires.Add(new SpiceWorkspaceWireData(startComponentId, startTerminalId, endComponentId, endTerminalId, visualState ?? SpiceWireVisualState.Auto()));
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

        /// <summary>
        /// Clears only the per-kind naming counters after a successful workspace clear.
        /// Existing component instance IDs are never renumbered or reused by single deletion.
        /// </summary>
        public void ResetInstanceNaming()
        {
            nextInstanceNumbers.Clear();
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
            if (kind == SpiceComponentKind.IdealSwitch) return value == 0d || value == 1d;
            // 二极管使用固定 D_GENERIC 模型，GND 与两类探针无参数；均不允许通过参数区写入内部值。
            return kind == SpiceComponentKind.DcVoltageSource || kind == SpiceComponentKind.DcCurrentSource ||
                (kind != SpiceComponentKind.Ground && kind != SpiceComponentKind.SiliconDiode && kind != SpiceComponentKind.VoltageProbe && kind != SpiceComponentKind.CurrentProbe && value > 0d);
        }

        /// <summary>
        /// 判断器件类型是否有用户可编辑参数。无参数器件（GND/二极管/探针）导入时使用固定默认值。
        /// </summary>
        public static bool HasUserParameter(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource || kind == SpiceComponentKind.DcCurrentSource ||
                kind == SpiceComponentKind.Resistor || kind == SpiceComponentKind.Capacitor ||
                kind == SpiceComponentKind.Inductor || kind == SpiceComponentKind.IdealSwitch;
        }

        /// <summary>
        /// 返回器件类型对应的规范 InstanceId 前缀。与 BuildInstanceId 使用同一映射。
        /// </summary>
        public static string GetExpectedPrefix(SpiceComponentKind kind)
        {
            switch (kind)
            {
                case SpiceComponentKind.DcVoltageSource: return "source";
                case SpiceComponentKind.DcCurrentSource: return "current-source";
                case SpiceComponentKind.IdealSwitch: return "switch";
                case SpiceComponentKind.SiliconDiode: return "diode";
                case SpiceComponentKind.Resistor: return "resistor";
                case SpiceComponentKind.Capacitor: return "capacitor";
                case SpiceComponentKind.Inductor: return "inductor";
                case SpiceComponentKind.VoltageProbe: return "voltage-probe";
                case SpiceComponentKind.CurrentProbe: return "current-probe";
                default: return "ground";
            }
        }

        /// <summary>
        /// 校验 InstanceId 是否符合规范：前缀必须与器件类型匹配，后缀必须为最少三位纯数字正整数的 D3 格式（001、010、1000 等）。
        /// 逐字符校验确保不接受加号、减号、空白、小数点或指数表示法。
        /// </summary>
        public static bool IsValidInstanceId(SpiceComponentKind kind, string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return false;
            var expectedPrefix = GetExpectedPrefix(kind);
            if (!instanceId.StartsWith(expectedPrefix + "-", StringComparison.Ordinal)) return false;
            var suffix = instanceId.Substring(expectedPrefix.Length + 1);
            if (suffix.Length < 3) return false;
            // 每一个字符都必须在 '0' 到 '9'，不接受符号、空白、小数点或指数表示法。
            for (var i = 0; i < suffix.Length; i++)
            {
                if (suffix[i] < '0' || suffix[i] > '9') return false;
            }
            // 已确认全是纯数字，使用 NumberStyles.None 安全解析为正整数。
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return false;
            return number >= 1;
        }


        private static string BuildInstanceId(SpiceComponentKind kind, int number)
        {
            var prefix = kind == SpiceComponentKind.DcVoltageSource ? "source" :
                kind == SpiceComponentKind.DcCurrentSource ? "current-source" :
                kind == SpiceComponentKind.IdealSwitch ? "switch" :
                kind == SpiceComponentKind.SiliconDiode ? "diode" :
                kind == SpiceComponentKind.Resistor ? "resistor" :
                kind == SpiceComponentKind.Capacitor ? "capacitor" :
                kind == SpiceComponentKind.Inductor ? "inductor" :
                kind == SpiceComponentKind.VoltageProbe ? "voltage-probe" :
                kind == SpiceComponentKind.CurrentProbe ? "current-probe" : "ground";
            return prefix + "-" + number.ToString("D3", CultureInfo.InvariantCulture);
        }

        private static double DefaultValue(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? 10d :
                kind == SpiceComponentKind.DcCurrentSource ? 0.001d :
                kind == SpiceComponentKind.IdealSwitch ? 0d :
                kind == SpiceComponentKind.SiliconDiode ? 0d :
                kind == SpiceComponentKind.Resistor ? 1000d :
                kind == SpiceComponentKind.Capacitor ? 1e-6d :
                kind == SpiceComponentKind.Inductor ? 0.01d :
                kind == SpiceComponentKind.VoltageProbe ? 0d :
                kind == SpiceComponentKind.CurrentProbe ? 0d : 0d;
        }
    }

    public enum SpiceWorkspaceChange { Topology, Parameter }

    public sealed class SpiceWorkspaceComponentData
    {
        public SpiceWorkspaceComponentData(string instanceId, SpiceComponentKind kind, Vector2 position, double siValue)
            : this(instanceId, kind, position, siValue, 0)
        {
        }

        public SpiceWorkspaceComponentData(string instanceId, SpiceComponentKind kind, Vector2 position, double siValue, int rotationQuarterTurns)
        {
            InstanceId = instanceId;
            Kind = kind;
            Position = position;
            SiValue = siValue;
            RotationQuarterTurns = ((rotationQuarterTurns % 4) + 4) % 4;
        }

        public string InstanceId { get; }
        public SpiceComponentKind Kind { get; }
        public Vector2 Position { get; set; }
        public double SiValue { get; set; }
        /// <summary>
        /// 离散旋转状态（0-3，表示顺时针 90 度的倍数）。
        /// 旋转只影响视觉布局，不写入 SpiceCircuitModel，也不使 DC 结果过期。
        /// </summary>
        public int RotationQuarterTurns { get; set; }
        public bool HasTerminal(string terminalId) => Kind == SpiceComponentKind.Ground
            ? string.Equals(terminalId, SpiceComponentModel.GroundTerminalId, StringComparison.Ordinal)
            : string.Equals(terminalId, SpiceComponentModel.PositiveTerminalId, StringComparison.Ordinal) || string.Equals(terminalId, SpiceComponentModel.NegativeTerminalId, StringComparison.Ordinal);

        public SpiceComponentModel ToSpiceComponentModel()
        {
            switch (Kind)
            {
                case SpiceComponentKind.DcVoltageSource: return SpiceComponentModel.DcVoltageSource(InstanceId, SiValue);
                case SpiceComponentKind.DcCurrentSource: return SpiceComponentModel.DcCurrentSource(InstanceId, SiValue);
                case SpiceComponentKind.IdealSwitch: return SpiceComponentModel.IdealSwitch(InstanceId, SiValue > 0.5d);
                case SpiceComponentKind.SiliconDiode: return SpiceComponentModel.SiliconDiode(InstanceId);
                case SpiceComponentKind.Resistor: return SpiceComponentModel.Resistor(InstanceId, SiValue);
                case SpiceComponentKind.Capacitor: return SpiceComponentModel.Capacitor(InstanceId, SiValue);
                case SpiceComponentKind.Inductor: return SpiceComponentModel.Inductor(InstanceId, SiValue);
                case SpiceComponentKind.Ground: return SpiceComponentModel.Ground(InstanceId);
                case SpiceComponentKind.VoltageProbe: return SpiceComponentModel.VoltageProbe(InstanceId);
                case SpiceComponentKind.CurrentProbe: return SpiceComponentModel.CurrentProbe(InstanceId);
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }

    /// <summary>
    /// SPICE 原型中一条正式导线的电气端点与视图路由状态。
    /// 只有端点会映射到 SpiceWireModel；折点始终是工作区局部坐标，不参与拓扑或网表。
    /// </summary>
    public sealed class SpiceWorkspaceWireData
    {
        public SpiceWorkspaceWireData(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId, SpiceWireVisualState visualState)
        {
            StartComponentId = startComponentId;
            StartTerminalId = startTerminalId;
            EndComponentId = endComponentId;
            EndTerminalId = endTerminalId;
            VisualState = visualState;
        }

        public string StartComponentId { get; }
        public string StartTerminalId { get; }
        public string EndComponentId { get; }
        public string EndTerminalId { get; }
        public SpiceWireVisualState VisualState { get; }
    }

    public enum SpiceWireRouteMode { Auto, Manual }

    /// <summary>
    /// 工作区视觉路由数据。Manual 模式只保存用户确认的中间折点；
    /// 元件移动或旋转时，视图可临时补折点以保持正交，但不会改写这里的用户数据。
    /// </summary>
    public sealed class SpiceWireVisualState
    {
        private SpiceWireVisualState(SpiceWireRouteMode routeMode, IReadOnlyList<Vector2> waypoints)
        {
            RouteMode = routeMode;
            Waypoints = waypoints;
        }

        public SpiceWireRouteMode RouteMode { get; }
        public IReadOnlyList<Vector2> Waypoints { get; }

        public static SpiceWireVisualState Auto() => new SpiceWireVisualState(SpiceWireRouteMode.Auto, Array.Empty<Vector2>());

        public static SpiceWireVisualState Manual(IReadOnlyList<Vector2> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0) throw new ArgumentException("Manual wire routing requires at least one waypoint.", nameof(waypoints));
            return new SpiceWireVisualState(SpiceWireRouteMode.Manual, waypoints.ToArray());
        }
    }

    public static class SpiceParameterUnits
    {
        public static readonly string[] VoltageUnits = { "V" };
        public static readonly string[] CurrentUnits = { "A", "mA", "uA" };
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
                kind == SpiceComponentKind.DcCurrentSource ? CurrentUnits :
                kind == SpiceComponentKind.Resistor ? ResistanceUnits :
                kind == SpiceComponentKind.Capacitor ? CapacitanceUnits :
                kind == SpiceComponentKind.Inductor ? InductanceUnits : Array.Empty<string>();
        }

        private static double Multiplier(SpiceComponentKind kind, string unit)
        {
            if (kind == SpiceComponentKind.Resistor) return unit == "kOhm" ? 1e3d : unit == "MOhm" ? 1e6d : 1d;
            if (kind == SpiceComponentKind.DcCurrentSource) return unit == "mA" ? 1e-3d : unit == "uA" ? 1e-6d : 1d;
            if (kind == SpiceComponentKind.Capacitor) return unit == "mF" ? 1e-3d : unit == "uF" ? 1e-6d : unit == "nF" ? 1e-9d : unit == "pF" ? 1e-12d : 1d;
            if (kind == SpiceComponentKind.Inductor) return unit == "mH" ? 1e-3d : unit == "uH" ? 1e-6d : 1d;
            return 1d;
        }
    }
}
