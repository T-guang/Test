using System;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 图纸 V1 的集中资源限制。限制只作用于导入边界，不改变画布编辑和电气模型语义。
    /// </summary>
    public static class SpiceDrawingLimits
    {
        public const long MaxFileBytes = 1024 * 1024;
        public const int MaxComponents = 500;
        public const int MaxWires = 1000;
        public const int MaxManualRoutePointsPerWire = 128;
        public const int MaxTotalManualRoutePoints = 10000;
        public const int MaxInstanceIdLength = 128;
        public const int MaxTerminalIdLength = 64;
        public const int MaxStringLength = 256;
        public const float MaxCoordinateMagnitude = 20000f;
        public const double MaxAbsoluteParameter = 1e15d;

        public static bool IsCoordinateSupported(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                Math.Abs((double)value) <= MaxCoordinateMagnitude;
        }

        public static bool IsParameterSupported(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) &&
                Math.Abs(value) <= MaxAbsoluteParameter;
        }

        public static bool IsStringLengthSupported(string value, int maximumLength)
        {
            return value == null || value.Length <= maximumLength;
        }
    }
}
