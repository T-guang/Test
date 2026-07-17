using System;
using System.Collections.Generic;

namespace ElectricalSim.Spice.Infrastructure
{
    public enum NgspiceFailureCode
    {
        None,
        ExecutableMissing,
        StartFailed,
        TimedOut,
        NonZeroExitCode,
        OutputMarkersMissing,
        RequiredValueMissing,
        ParseFailed,
        Cancelled
    }

    public sealed class NgspiceRunResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string ExecutablePath { get; set; }
        public string WorkingDirectory { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public TimeSpan Duration { get; set; }
        public Dictionary<string, double> NodeVoltages { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> BranchCurrents { get; } =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public NgspiceFailureCode FailureCode { get; set; }
        public string FailureMessage { get; set; }
    }
}
