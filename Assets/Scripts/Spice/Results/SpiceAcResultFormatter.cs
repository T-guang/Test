using System;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Results
{
    /// <summary>
    /// 已完成单频 AC 结果的纯展示格式化器。
    /// 它刻意只读取不可变仿真快照，而不读取可变的工作区状态。
    /// </summary>
    public static class SpiceAcResultFormatter
    {
        public const double PresentationZeroMagnitude = 1e-12d;

        public static string Format(SpiceSimulationResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var text = new StringBuilder();
            text.Append("分析：单频交流\n频率：");
            text.Append(FormatFrequency(result.AnalysisSettings.FrequencyHz));

            foreach (var value in result.AcComponentResults.Values
                .Where(component => !string.Equals(component.ComponentKind, "Ground", StringComparison.Ordinal))
                .OrderBy(component => component.ComponentId, StringComparer.Ordinal))
            {
                text.Append("\n\n");
                text.Append(value.ComponentId);
                text.Append("  ");
                text.Append(GetComponentDisplayName(value.ComponentKind));
                text.Append("\n");
                text.Append(string.Equals(value.ComponentKind, "VoltageProbe", StringComparison.Ordinal) ? "差分电压  " : "电压  ");
                text.Append(FormatVoltage(value.Voltage));
                text.Append("\n");
                if (!string.Equals(value.ComponentKind, "VoltageProbe", StringComparison.Ordinal))
                {
                    text.Append("电流  ");
                    text.Append(FormatCurrent(value.Current));
                    text.Append("\n");
                }
                text.Append("参考方向：");
                text.Append(FormatDirection(value));
                if (!string.IsNullOrEmpty(value.Notes))
                {
                    text.Append("\n");
                    text.Append(value.Notes);
                }
            }
            return text.ToString();
        }

        public static string FormatVoltage(SpicePhasor phasor) => FormatPhasor(phasor, true);
        public static string FormatCurrent(SpicePhasor phasor) => FormatPhasor(phasor, false);

        /// <summary>将内部器件类型名转换为面向学习者的中文名称；实例 ID 仍保留原样便于定位元件。</summary>
        public static string GetComponentDisplayName(string componentKind)
        {
            switch (componentKind)
            {
                case "DcVoltageSource": return "直流电压源";
                case "AcVoltageSource": return "交流电压源";
                case "DcCurrentSource": return "直流电流源";
                case "IdealSwitch": return "理想开关";
                case "SiliconDiode": return "通用硅二极管";
                case "Resistor": return "电阻";
                case "Capacitor": return "电容";
                case "Inductor": return "电感";
                case "VoltageProbe": return "电压探针";
                case "CurrentProbe": return "电流探针";
                case "IdealOperationalAmplifier": return "理想运算放大器";
                case "GenericNpnBjt": return "通用 NPN 三极管";
                case "GenericPnpBjt": return "通用 PNP 三极管";
                case "Ground": return "接地";
                default: return "未知元件";
            }
        }

        private static string FormatPhasor(SpicePhasor phasor, bool voltage)
        {
            if (phasor.Magnitude <= PresentationZeroMagnitude)
                return "0 " + (voltage ? "V" : "A") + " ∠ --";
            var engineering = voltage ? FormatVoltageMagnitude(phasor.Magnitude) : FormatCurrentMagnitude(phasor.Magnitude);
            var phase = SpiceAnalysisLimits.NormalizePhaseDegrees(phasor.PhaseDegrees);
            if (Math.Abs(phase) < 0.0005d) phase = 0d;
            return engineering + " ∠ " + phase.ToString("0.000", CultureInfo.InvariantCulture) + "°";
        }

        private static string FormatFrequency(double frequencyHz)
        {
            if (frequencyHz >= 1e6d) return FormatNumber(frequencyHz / 1e6d) + " MHz";
            if (frequencyHz >= 1e3d) return FormatNumber(frequencyHz / 1e3d) + " kHz";
            if (frequencyHz >= 1d) return FormatNumber(frequencyHz) + " Hz";
            return FormatNumber(frequencyHz * 1e3d) + " mHz";
        }

        private static string FormatVoltageMagnitude(double magnitude)
        {
            if (magnitude >= 1e3d) return FormatNumber(magnitude / 1e3d) + " kV";
            if (magnitude >= 1d) return FormatNumber(magnitude) + " V";
            if (magnitude >= 1e-3d) return FormatNumber(magnitude * 1e3d) + " mV";
            if (magnitude >= 1e-6d) return FormatNumber(magnitude * 1e6d) + " μV";
            return FormatNumber(magnitude * 1e9d) + " nV";
        }

        private static string FormatCurrentMagnitude(double magnitude)
        {
            if (magnitude >= 1d) return FormatNumber(magnitude) + " A";
            if (magnitude >= 1e-3d) return FormatNumber(magnitude * 1e3d) + " mA";
            if (magnitude >= 1e-6d) return FormatNumber(magnitude * 1e6d) + " μA";
            if (magnitude >= 1e-9d) return FormatNumber(magnitude * 1e9d) + " nA";
            return FormatNumber(magnitude * 1e12d) + " pA";
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value) < 0.5e-12d) value = 0d;
            return value.ToString("G6", CultureInfo.InvariantCulture);
        }

        private static string FormatDirection(SpiceAcComponentResult value)
        {
            var componentKind = value.ComponentKind;
            var direction = string.IsNullOrEmpty(value.CurrentDirection) ? value.VoltageDirection : value.CurrentDirection;
            var mapped = direction == "A-to-B" ? "A → B" : direction == "A-to-K" ? "A → K" :
                direction == "P-to-N" ? "P → N" : direction == "V-plus-to-V-minus" ? "V+ → V-" :
                direction == "IN-to-OUT" ? "输入端 → 输出端" : direction == "C-to-E" || (!string.IsNullOrEmpty(direction) && direction.StartsWith("collector", StringComparison.OrdinalIgnoreCase)) ? "C → E（集电极电流流入 C 为正）" :
                !string.IsNullOrEmpty(direction) && direction.StartsWith("OUT-to-GND", StringComparison.Ordinal) ? "OUT → GND" : "正端 → 负端";
            if (string.Equals(componentKind, "VoltageProbe", StringComparison.Ordinal)) return "V+ → V-";
            if (string.Equals(componentKind, "CurrentProbe", StringComparison.Ordinal)) return "输入端 → 输出端";
            if (string.Equals(componentKind, "AcVoltageSource", StringComparison.Ordinal)) return mapped + "（ngspice 支路约定）";
            if (string.Equals(componentKind, "IdealOperationalAmplifier", StringComparison.Ordinal) && direction.Contains("ngspice branch convention"))
                return mapped + "（ngspice 支路约定）";
            return mapped;
        }
    }
}
