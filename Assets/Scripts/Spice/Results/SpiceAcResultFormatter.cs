using System;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Results
{
    /// <summary>
    /// Presentation-only formatter for a completed single-frequency AC result.
    /// It deliberately consumes the immutable simulation snapshot rather than workspace state.
    /// </summary>
    public static class SpiceAcResultFormatter
    {
        public const double PresentationZeroMagnitude = 1e-12d;

        public static string Format(SpiceSimulationResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            var text = new StringBuilder();
            text.Append("分析：单频 AC\n频率：");
            text.Append(FormatFrequency(result.AnalysisSettings.FrequencyHz));

            foreach (var value in result.AcComponentResults.Values
                .Where(component => !string.Equals(component.ComponentKind, "Ground", StringComparison.Ordinal))
                .OrderBy(component => component.ComponentId, StringComparer.Ordinal))
            {
                text.Append("\n\n");
                text.Append(value.ComponentId);
                text.Append("  ");
                text.Append(value.ComponentKind);
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
            var mapped = direction == "A-to-B" ? "A → B" : direction == "V-plus-to-V-minus" ? "V+ → V-" :
                direction == "IN-to-OUT" ? "IN → OUT" : "positive → negative";
            if (string.Equals(componentKind, "VoltageProbe", StringComparison.Ordinal)) return "V+ → V-";
            if (string.Equals(componentKind, "CurrentProbe", StringComparison.Ordinal)) return "IN → OUT";
            if (string.Equals(componentKind, "AcVoltageSource", StringComparison.Ordinal)) return mapped + "（ngspice 支路约定）";
            return mapped;
        }
    }
}
