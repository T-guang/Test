using System;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Results
{
    public readonly struct SpicePhasor
    {
        public static readonly SpicePhasor Zero = new SpicePhasor(0d, 0d);

        public SpicePhasor(double real, double imaginary)
        {
            if (!IsFinite(real) || !IsFinite(imaginary)) throw new ArgumentOutOfRangeException(nameof(real), "Phasor values must be finite.");
            var magnitude = CalculateMagnitude(real, imaginary);
            if (!IsFinite(magnitude)) throw new ArgumentOutOfRangeException(nameof(real), "Phasor magnitude must be finite.");
            Real = real;
            Imaginary = imaginary;
        }

        public double Real { get; }
        public double Imaginary { get; }
        public double Magnitude => CalculateMagnitude(Real, Imaginary);
        public double PhaseDegrees => Magnitude == 0d ? 0d : SpiceAnalysisLimits.NormalizePhaseDegrees(Math.Atan2(Imaginary, Real) * 180d / Math.PI);

        public SpicePhasor Add(SpicePhasor other) => new SpicePhasor(Real + other.Real, Imaginary + other.Imaginary);
        public SpicePhasor Subtract(SpicePhasor other) => new SpicePhasor(Real - other.Real, Imaginary - other.Imaginary);
        public SpicePhasor Multiply(double scalar) => new SpicePhasor(Real * scalar, Imaginary * scalar);
        public SpicePhasor Divide(double scalar)
        {
            if (!IsFinite(scalar) || scalar == 0d) throw new ArgumentOutOfRangeException(nameof(scalar));
            return new SpicePhasor(Real / scalar, Imaginary / scalar);
        }

        public SpicePhasor MultiplyByJ(double scalar) => new SpicePhasor(-Imaginary * scalar, Real * scalar);
        public SpicePhasor DivideByJ(double scalar)
        {
            if (!IsFinite(scalar) || scalar == 0d) throw new ArgumentOutOfRangeException(nameof(scalar));
            return new SpicePhasor(Imaginary / scalar, -Real / scalar);
        }

        public bool IsMagnitudeBelow(double tolerance) => Magnitude <= tolerance;

        private static double CalculateMagnitude(double real, double imaginary)
        {
            var largest = Math.Max(Math.Abs(real), Math.Abs(imaginary));
            if (largest == 0d) return 0d;
            return largest * Math.Sqrt((real / largest) * (real / largest) + (imaginary / largest) * (imaginary / largest));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
