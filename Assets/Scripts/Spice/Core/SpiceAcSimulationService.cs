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
    public sealed class SpiceAcSimulationService
    {
        private readonly Func<string, TimeSpan, string, CancellationToken, Task<NgspiceRunResult>> runRawNetlistAsync;

        public SpiceAcSimulationService(Func<string, TimeSpan, string, CancellationToken, Task<NgspiceRunResult>> runRawNetlistAsync = null)
        {
            if (runRawNetlistAsync != null)
            {
                this.runRawNetlistAsync = runRawNetlistAsync;
                return;
            }

            var runner = new NgspiceProcessRunner();
            this.runRawNetlistAsync = runner.RunRawNetlistAsync;
        }

        public async Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = CreateResult(circuit);
            if (circuit == null || circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_ANALYSIS_UNSUPPORTED", SpiceDiagnosticSeverity.Error,
                    "Single-frequency AC analysis requires AC analysis settings."));
                return result;
            }

            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            result.Diagnostics.AddRange(graph.Diagnostics);
            if (!graph.IsValid) return result;

            var document = SpiceAcNetlistBuilder.Build(circuit, graph);
            result.GeneratedNetlistContent = document.Content;
            var raw = await runRawNetlistAsync(document.Content, TimeSpan.FromSeconds(8), "SpiceAcB2", cancellationToken).ConfigureAwait(false);
            result.RawNgspiceResult = raw;
            result.Duration = raw.Duration;
            if (!raw.Success)
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_NGSPICE_" + raw.FailureCode, SpiceDiagnosticSeverity.Error,
                    raw.FailureMessage ?? "ngspice execution failed."));
                return result;
            }

            if (SpiceNgspiceErrorClassifier.TryGetSevereErrorLine(raw.StandardError, out _))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_NGSPICE_ERROR", SpiceDiagnosticSeverity.Error,
                    "ngspice reported an error while executing the AC analysis."));
                return result;
            }

            if (!SpiceAcOutputParser.TryParse(raw.StandardOutput, document.OutputRequests, out var values, out var failure, out var message))
            {
                result.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_OUTPUT_" + failure.ToString().ToUpperInvariant(), SpiceDiagnosticSeverity.Error, message));
                return result;
            }

            result.AcNodeVoltages["0"] = SpicePhasor.Zero;
            foreach (var request in document.OutputRequests.Where(request => request.Kind == SpiceAcOutputKind.NodeVoltage))
                result.AcNodeVoltages[request.ResultKey] = values[request.ResultKey];
            BuildComponentResults(circuit, graph, result, values);
            result.Success = true;
            return result;
        }

        private static SpiceSimulationResult CreateResult(SpiceCircuitModel circuit)
        {
            var settings = circuit == null
                ? new SpiceAnalysisSettings(SpiceAnalysisMode.DcOperatingPoint, SpiceAnalysisLimits.DefaultFrequencyHz)
                : circuit.AnalysisSettings.Copy();
            return new SpiceSimulationResult { AnalysisSettings = settings };
        }

        private static void BuildComponentResults(SpiceCircuitModel circuit, SpiceCircuitGraph graph, SpiceSimulationResult result,
            IReadOnlyDictionary<string, SpicePhasor> branchValues)
        {
            var omega = 2d * Math.PI * circuit.AnalysisSettings.FrequencyHz;
            foreach (var component in circuit.Components.Where(component => component.Kind != SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                var positiveNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negativeNode = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                var voltage = GetNodeVoltage(result.AcNodeVoltages, positiveNode).Subtract(GetNodeVoltage(result.AcNodeVoltages, negativeNode));
                var componentResult = new SpiceAcComponentResult
                {
                    ComponentId = component.InstanceId,
                    ComponentKind = component.Kind.ToString(),
                    Voltage = voltage,
                    VoltageDirection = "positive-to-negative",
                    CurrentDirection = "positive-to-negative",
                    ResultStatus = SpiceResultStatus.Available,
                    Notes = string.Empty
                };

                switch (component.Kind)
                {
                    case SpiceComponentKind.Resistor:
                        componentResult.Current = voltage.Divide(component.GetRequiredParameter(SpiceParameterKey.Resistance));
                        break;
                    case SpiceComponentKind.Capacitor:
                        componentResult.Current = voltage.MultiplyByJ(omega * component.GetRequiredParameter(SpiceParameterKey.Capacitance));
                        break;
                    case SpiceComponentKind.Inductor:
                        componentResult.Current = voltage.DivideByJ(omega * component.GetRequiredParameter(SpiceParameterKey.Inductance));
                        break;
                    case SpiceComponentKind.IdealSwitch:
                        componentResult.Current = voltage.Divide(SpiceNetlistBuilder.SwitchResistance(component));
                        componentResult.VoltageDirection = "A-to-B";
                        componentResult.CurrentDirection = "A-to-B";
                        componentResult.Notes = component.GetRequiredParameter(SpiceParameterKey.SwitchClosed) > 0.5d
                            ? "closed static-resistance equivalent"
                            : "open static-resistance equivalent";
                        break;
                    case SpiceComponentKind.AcVoltageSource:
                        componentResult.Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]];
                        componentResult.CurrentDirection = "positive-to-negative (ngspice branch convention)";
                        break;
                    case SpiceComponentKind.VoltageProbe:
                        componentResult.Current = SpicePhasor.Zero;
                        componentResult.VoltageDirection = "V-plus-to-V-minus";
                        componentResult.CurrentDirection = "V-plus-to-V-minus";
                        componentResult.Notes = "differential voltage measurement";
                        break;
                    case SpiceComponentKind.CurrentProbe:
                        componentResult.Current = branchValues[graph.SpiceNameByComponentId[component.InstanceId]];
                        componentResult.VoltageDirection = "IN-to-OUT";
                        componentResult.CurrentDirection = "IN-to-OUT";
                        componentResult.Notes = "series current measurement";
                        break;
                    default:
                        throw new InvalidOperationException("Unsupported component reached AC result mapping: " + component.Kind + ".");
                }

                result.AcComponentResults[component.InstanceId] = componentResult;
            }
        }

        private static SpicePhasor GetNodeVoltage(IReadOnlyDictionary<string, SpicePhasor> nodeVoltages, string node)
        {
            return node == "0" ? SpicePhasor.Zero : nodeVoltages[node];
        }
    }
}
