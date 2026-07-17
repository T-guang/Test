using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.T2
{
    public static class SpiceT2Validation
    {
        public const double VoltageTolerance = 1e-6d;
        public const double CurrentTolerance = 1e-8d;

        public static void RunPureCoreChecks()
        {
            ExpectInvalidParameter();
            ExpectDeterministicGraph();
        }

        public static async System.Threading.Tasks.Task<List<SpiceSimulationResult>> RunIntegrationChecksAsync()
        {
            var service = new SpiceDcSimulationService();
            var results = new List<SpiceSimulationResult>();
            results.Add(await VerifySingleResistor(service, 1000d, 0.01d).ConfigureAwait(false));
            results.Add(await VerifySingleResistor(service, 2000d, 0.005d).ConfigureAwait(false));
            results.Add(await VerifyDivider(service, 1000d, 5d, 0.005d).ConfigureAwait(false));
            results.Add(await VerifyDivider(service, 3000d, 7.5d, 0.0025d).ConfigureAwait(false));
            results.Add(await VerifyParallel(service).ConfigureAwait(false));
            results.Add(await VerifyCapacitor(service).ConfigureAwait(false));
            results.Add(await VerifyInductor(service).ConfigureAwait(false));
            await VerifyDeletedWireBlocksSimulationAsync(service).ConfigureAwait(false);
            return results;
        }

        public static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyDividerForPlayerAsync()
        {
            return await VerifyDivider(new SpiceDcSimulationService(), 1000d, 5d, 0.005d).ConfigureAwait(false);
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifySingleResistor(SpiceDcSimulationService service, double resistance, double expectedCurrent)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.SingleResistor(resistance)).ConfigureAwait(false);
            ExpectSuccess(result); ExpectNear(GetOnlyPositiveNode(result), 10d, VoltageTolerance, "single resistor node voltage");
            ExpectNear(result.ComponentResults["r1"].Current, expectedCurrent, CurrentTolerance, "single resistor current");
            ExpectNear(result.ComponentResults["source"].Current, -expectedCurrent, CurrentTolerance, "source current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyDivider(SpiceDcSimulationService service, double r2, double expectedMiddle, double expectedCurrent)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.Divider(r2)).ConfigureAwait(false);
            ExpectSuccess(result); ExpectNear(GetMiddleNode(result), expectedMiddle, VoltageTolerance, "divider middle node voltage");
            ExpectNear(result.ComponentResults["r1"].Current, expectedCurrent, CurrentTolerance, "divider r1 current");
            ExpectNear(result.ComponentResults["r2"].Current, expectedCurrent, CurrentTolerance, "divider r2 current");
            ExpectNear(result.ComponentResults["source"].Current, -expectedCurrent, CurrentTolerance, "divider source current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyParallel(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ParallelResistors()).ConfigureAwait(false);
            ExpectSuccess(result); ExpectNear(result.ComponentResults["r1"].Current, 0.01d, CurrentTolerance, "parallel r1 current");
            ExpectNear(result.ComponentResults["r2"].Current, 0.005d, CurrentTolerance, "parallel r2 current");
            ExpectNear(result.ComponentResults["source"].Current, -0.015d, CurrentTolerance, "parallel source current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyCapacitor(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ResistorAndCapacitor()).ConfigureAwait(false);
            ExpectSuccess(result); ExpectNear(result.ComponentResults["r1"].Current, 0.01d, CurrentTolerance, "capacitor fixture resistor current");
            ExpectNear(result.ComponentResults["c1"].Current, 0d, CurrentTolerance, "capacitor DC current");
            if (result.ComponentResults["c1"].ResultStatus != SpiceResultStatus.DcSteadyStateOpenCircuit) throw new InvalidOperationException("Capacitor result did not declare DC steady-state open circuit semantics.");
            ExpectNear(result.ComponentResults["source"].Current, -0.01d, CurrentTolerance, "capacitor fixture source current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyInductor(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ResistorAndInductor()).ConfigureAwait(false);
            ExpectSuccess(result); ExpectNear(result.ComponentResults["r1"].Current, 0.01d, CurrentTolerance, "inductor fixture resistor current");
            ExpectNear(result.ComponentResults["l1"].Voltage, 0d, VoltageTolerance, "inductor DC voltage");
            ExpectNear(result.ComponentResults["l1"].Current, 0.01d, CurrentTolerance, "inductor branch current");
            return result;
        }

        private static void ExpectInvalidParameter()
        {
            var invalid = SpiceT2Fixtures.SingleResistor(0d);
            if (SpiceCircuitGraphBuilder.Build(invalid).IsValid) throw new InvalidOperationException("Zero-ohm resistor was not rejected.");
        }

        private static void ExpectDeterministicGraph()
        {
            var first = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.Divider());
            var second = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.Divider());
            var firstNodes = string.Join(";", first.NodeByTerminal.OrderBy(pair => pair.Key.ToString()).Select(pair => pair.Key + "=" + pair.Value));
            var secondNodes = string.Join(";", second.NodeByTerminal.OrderBy(pair => pair.Key.ToString()).Select(pair => pair.Key + "=" + pair.Value));
            if (firstNodes != secondNodes) throw new InvalidOperationException("Node assignment is not deterministic.");
        }

        private static async System.Threading.Tasks.Task VerifyDeletedWireBlocksSimulationAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.Divider();
            circuit.Wires.RemoveAt(1);
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_TERMINAL")) throw new InvalidOperationException("Deleting the divider wire did not invalidate the rebuilt topology.");
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null) throw new InvalidOperationException("Deleted-wire topology reached ngspice instead of being rejected before execution.");
        }

        private static void ExpectSuccess(SpiceSimulationResult result)
        {
            if (result == null || !result.Success) throw new InvalidOperationException("T2 DC simulation failed: " + string.Join(" | ", result == null ? new string[0] : result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        }

        private static double GetOnlyPositiveNode(SpiceSimulationResult result) => result.NodeVoltages.Values.Single();
        private static double GetMiddleNode(SpiceSimulationResult result) => result.NodeVoltages.Values.OrderBy(value => value).First(value => Math.Abs(value - 10d) > VoltageTolerance);
        private static void ExpectNear(double actual, double expected, double tolerance, string name) { if (Math.Abs(actual - expected) > tolerance) throw new InvalidOperationException(name + " expected " + expected + ", actual " + actual + "."); }
    }
}
