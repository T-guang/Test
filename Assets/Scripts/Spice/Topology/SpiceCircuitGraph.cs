using System;
using System.Collections.Generic;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Topology
{
    public enum SpiceDiagnosticSeverity { Error, Warning }

    public sealed class SpiceDiagnostic
    {
        public SpiceDiagnostic(string code, SpiceDiagnosticSeverity severity, string message, string componentId = null, string terminalId = null)
        {
            Code = code;
            Severity = severity;
            Message = message;
            ComponentId = componentId;
            TerminalId = terminalId;
        }

        public string Code { get; }
        public SpiceDiagnosticSeverity Severity { get; }
        public string Message { get; }
        public string ComponentId { get; }
        public string TerminalId { get; }
    }

    public sealed class SpiceCircuitGraph
    {
        public Dictionary<SpiceTerminalRef, string> NodeByTerminal { get; } = new Dictionary<SpiceTerminalRef, string>();
        public Dictionary<string, string> SpiceNameByComponentId { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> ComponentIdBySpiceName { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SpiceDiagnostic> Diagnostics { get; } = new List<SpiceDiagnostic>();
        public bool IsValid => Diagnostics.TrueForAll(diagnostic => diagnostic.Severity != SpiceDiagnosticSeverity.Error);
    }
}
