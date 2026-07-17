using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Core
{
    public sealed class SpiceDcSimulationService
    {
        private readonly NgspiceProcessRunner processRunner;

        public SpiceDcSimulationService(NgspiceProcessRunner processRunner = null)
        {
            this.processRunner = processRunner ?? new NgspiceProcessRunner();
        }

        public async Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new SpiceSimulationResult();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            result.Diagnostics.AddRange(graph.Diagnostics);
            if (!graph.IsValid) return result;

            var document = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph);
            result.Netlist = document.Content;
            var raw = await processRunner.RunRawNetlistAsync(document.Content, TimeSpan.FromSeconds(8), "SpiceT2", cancellationToken).ConfigureAwait(false);
            result.RawNgspiceResult = raw;
            result.Duration = raw.Duration;
            if (!raw.Success)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_NGSPICE_" + raw.FailureCode, SpiceDiagnosticSeverity.Error, raw.FailureMessage ?? "ngspice execution failed."));
                return result;
            }

            if (!SpiceDcOutputParser.TryParse(raw.StandardOutput, out var voltages, out var currents, out var parseFailure))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_OUTPUT_PARSE", SpiceDiagnosticSeverity.Error, parseFailure));
                return result;
            }

            foreach (var voltage in voltages) result.NodeVoltages[voltage.Key] = voltage.Value;
            BuildComponentResults(circuit, graph, result, currents);
            result.Success = true;
            return result;
        }

        private static void BuildComponentResults(SpiceCircuitModel circuit, SpiceCircuitGraph graph, SpiceSimulationResult result, Dictionary<string, double> currents)
        {
            foreach (var component in circuit.Components.Where(component => component.Kind != SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                var positiveNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negativeNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                var voltage = GetNodeVoltage(result.NodeVoltages, positiveNode) - GetNodeVoltage(result.NodeVoltages, negativeNode);
                var componentResult = new SpiceComponentResult
                {
                    ComponentId = component.InstanceId,
                    ComponentKind = component.Kind.ToString(),
                    Voltage = voltage,
                    VoltageDirection = "positive-to-negative",
                    CurrentDirection = "positive-to-negative",
                    ResultStatus = SpiceResultStatus.Available,
                    Notes = string.Empty
                };

                if (component.Kind == SpiceComponentKind.Resistor)
                {
                    component.TryGetParameter(SpiceParameterKey.Resistance, out var resistance);
                    componentResult.Current = voltage / resistance;
                }
                else if (component.Kind == SpiceComponentKind.Capacitor)
                {
                    componentResult.Current = 0d;
                    componentResult.ResultStatus = SpiceResultStatus.DcSteadyStateOpenCircuit;
                    componentResult.Notes = "DcSteadyStateOpenCircuit";
                }
                else
                {
                    var spiceName = graph.SpiceNameByComponentId[component.InstanceId];
                    if (currents.TryGetValue(spiceName, out var current)) componentResult.Current = current;
                    else
                    {
                        componentResult.ResultStatus = SpiceResultStatus.NotAvailable;
                        componentResult.Notes = "ngspice did not return the requested branch current.";
                    }
                }

                result.ComponentResults[component.InstanceId] = componentResult;
            }
        }

        private static double GetNodeVoltage(Dictionary<string, double> voltages, string node)
        {
            return node == "0" ? 0d : voltages.TryGetValue(node, out var voltage) ? voltage : 0d;
        }
    }
}
