using System;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Core
{
    public sealed class SpiceSimulationService
    {
        private readonly SpiceDcSimulationService dcService;
        private readonly SpiceAcSimulationService acService;

        public SpiceSimulationService(SpiceDcSimulationService dcService = null, SpiceAcSimulationService acService = null)
        {
            this.dcService = dcService ?? new SpiceDcSimulationService();
            this.acService = acService ?? new SpiceAcSimulationService();
        }

        internal SpiceSimulationService(
            Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> dcSimulation,
            Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> acSimulation)
        {
            dcService = null;
            acService = null;
            this.dcSimulation = dcSimulation ?? throw new ArgumentNullException(nameof(dcSimulation));
            this.acSimulation = acSimulation ?? throw new ArgumentNullException(nameof(acSimulation));
        }

        private Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> dcSimulation;
        private Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> acSimulation;

        public Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            switch (circuit.AnalysisSettings.Mode)
            {
                case SpiceAnalysisMode.DcOperatingPoint:
                    return dcSimulation != null ? dcSimulation(circuit, cancellationToken) : dcService.SimulateAsync(circuit, cancellationToken);
                case SpiceAnalysisMode.AcSingleFrequency:
                    return acSimulation != null ? acSimulation(circuit, cancellationToken) : acService.SimulateAsync(circuit, cancellationToken);
                default:
                    var result = new SpiceSimulationResult { AnalysisSettings = circuit.AnalysisSettings.Copy() };
                    result.Diagnostics.Add(new SpiceDiagnostic("SPICE_ANALYSIS_MODE_UNSUPPORTED", SpiceDiagnosticSeverity.Error,
                        "The selected SPICE analysis mode is not supported."));
                    return Task.FromResult(result);
            }
        }
    }
}
