using System;

namespace ElectricalSim.Spice.Core
{
    public enum SpiceAnalysisMode
    {
        DcOperatingPoint,
        AcSingleFrequency
    }

    /// <summary>
    /// 单频 AC 分析所用纯模型限制的唯一权威来源。
    /// </summary>
    public static class SpiceAnalysisLimits
    {
        public const double DefaultFrequencyHz = 1000d;
        public const double MinFrequencyHz = 1e-3d;
        public const double MaxFrequencyHz = 1e7d;
        public const double MaxAcMagnitudeVolts = 1e15d;

        public static bool IsValidFrequency(double frequencyHz)
        {
            return IsFinite(frequencyHz) && frequencyHz >= MinFrequencyHz && frequencyHz <= MaxFrequencyHz;
        }

        public static bool IsValidAcMagnitude(double magnitudeVolts)
        {
            return IsFinite(magnitudeVolts) && magnitudeVolts > 0d && magnitudeVolts <= MaxAcMagnitudeVolts;
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static double NormalizePhaseDegrees(double phaseDegrees)
        {
            if (!IsFinite(phaseDegrees)) return double.NaN;

            var normalized = phaseDegrees % 360d;
            if (normalized >= 180d) normalized -= 360d;
            if (normalized < -180d) normalized += 360d;
            return normalized == 0d ? 0d : normalized;
        }
    }

    /// <summary>
    /// 会按值复制到每个电路快照的不可变分析配置。
    /// 工作区修改 API 负责约束有效 AC 频率范围；GraphBuilder 还会为外部构造的电路防御性校验该对象。
    /// </summary>
    public sealed class SpiceAnalysisSettings
    {
        public SpiceAnalysisSettings(SpiceAnalysisMode mode, double frequencyHz)
        {
            Mode = mode;
            FrequencyHz = frequencyHz;
        }

        public SpiceAnalysisMode Mode { get; }
        public double FrequencyHz { get; }

        public SpiceAnalysisSettings Copy()
        {
            return new SpiceAnalysisSettings(Mode, FrequencyHz);
        }
    }
}
