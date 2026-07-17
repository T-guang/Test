using System;
using System.Collections.Generic;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Results
{
    public enum SpiceResultStatus { Available, DcSteadyStateOpenCircuit, NotAvailable }

    public sealed class SpiceComponentResult
    {
        public string ComponentId { get; set; }
        public string ComponentKind { get; set; }
        public double Voltage { get; set; }
        public double Current { get; set; }
        public string VoltageDirection { get; set; }
        public string CurrentDirection { get; set; }
        public SpiceResultStatus ResultStatus { get; set; }
        public string Notes { get; set; }
    }

    public sealed class SpiceSimulationResult
    {
        public bool Success { get; set; }
        public string AnalysisType { get; set; } = "DC Operating Point";
        public TimeSpan Duration { get; set; }
        public string Netlist { get; set; }
        public List<SpiceDiagnostic> Diagnostics { get; } = new List<SpiceDiagnostic>();
        public Dictionary<string, double> NodeVoltages { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpiceComponentResult> ComponentResults { get; } = new Dictionary<string, SpiceComponentResult>(StringComparer.Ordinal);
        public NgspiceRunResult RawNgspiceResult { get; set; }
    }
}
