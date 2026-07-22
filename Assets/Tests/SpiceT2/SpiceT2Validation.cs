using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;

namespace ElectricalSim.Spice.T2
{
    public static class SpiceT2Validation
    {
        public const double VoltageTolerance = 1e-6d;
        public const double CurrentTolerance = 1e-8d;
        public const double DiodeCurrentTolerance = 1e-6d;

        public static void RunPureCoreChecks()
        {
            ExpectInvalidParameter();
            ExpectDeterministicGraph();
            ExpectFloatingClosedLoop();
            ExpectGroundedCircuitDoesNotMaskFloatingSubcircuit();
            ExpectWireOrderDoesNotAffectNodeNames();
            ExpectWireDirectionDoesNotAffectNodeNames();
            ExpectMultipleGroundsMapToZero();
            ExpectDiodeNetlistStable();
            ExpectDiodeWireOrderDoesNotAffectNetlist();
            ExpectDiodeWireDirectionDoesNotAffectNetlist();
            ExpectDiodeNotParameterEditable();
        }

        public static async System.Threading.Tasks.Task<List<SpiceSimulationResult>> RunIntegrationChecksAsync()
        {
            var service = new SpiceDcSimulationService();
            var results = new List<SpiceSimulationResult>();
            results.Add(await VerifySingleResistor(service, 1000d, 0.01d).ConfigureAwait(false));
            results.Add(await VerifySingleResistor(service, 2000d, 0.005d).ConfigureAwait(false));
            results.Add(await VerifyReversedSingleResistor(service).ConfigureAwait(false));
            results.Add(await VerifyCurrentSource(service).ConfigureAwait(false));
            results.Add(await VerifySwitch(service, false).ConfigureAwait(false));
            results.Add(await VerifySwitch(service, true).ConfigureAwait(false));
            results.Add(await VerifyDivider(service, 1000d, 5d, 0.005d).ConfigureAwait(false));
            results.Add(await VerifyDivider(service, 3000d, 7.5d, 0.0025d).ConfigureAwait(false));
            results.Add(await VerifyParallel(service).ConfigureAwait(false));
            results.Add(await VerifyCapacitor(service).ConfigureAwait(false));
            results.Add(await VerifyInductor(service).ConfigureAwait(false));
            results.Add(await VerifyForwardDiode(service).ConfigureAwait(false));
            results.Add(await VerifyReverseDiode(service).ConfigureAwait(false));
            await VerifyDeletedWireBlocksSimulationAsync(service).ConfigureAwait(false);
            await VerifyFloatingSubcircuitsBlockSimulationAsync(service).ConfigureAwait(false);
            await VerifyMultipleGroundSimulationAsync(service).ConfigureAwait(false);
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

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyReversedSingleResistor(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ReversedSingleResistor()).ConfigureAwait(false);
            ExpectSuccess(result);
            ExpectNear(result.ComponentResults["r1"].Voltage, -10d, VoltageTolerance, "reversed resistor voltage");
            ExpectNear(result.ComponentResults["r1"].Current, -0.005d, CurrentTolerance, "reversed resistor current");
            ExpectNear(result.ComponentResults["source"].Current, -0.005d, CurrentTolerance, "reversed resistor source current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyCurrentSource(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.CurrentSourceAndResistor()).ConfigureAwait(false);
            ExpectSuccess(result);
            ExpectNear(result.ComponentResults["r1"].Voltage, 1d, VoltageTolerance, "current source resistor voltage");
            ExpectNear(result.ComponentResults["r1"].Current, 0.001d, CurrentTolerance, "current source resistor current");
            ExpectNear(result.ComponentResults["current"].Current, 0.001d, CurrentTolerance, "current source configured current");
            if (!result.GeneratedNetlistContent.Contains("I1")) throw new InvalidOperationException("DC current source was not emitted as an I-element.");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifySwitch(SpiceDcSimulationService service, bool closed)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.SwitchAndResistor(closed)).ConfigureAwait(false);
            ExpectSuccess(result);
            var current = result.ComponentResults["r1"].Current;
            if (closed)
            {
                ExpectNear(current, 0.01d, CurrentTolerance, "closed switch current");
            }
            else if (Math.Abs(current) > 1e-9d)
            {
                throw new InvalidOperationException("Open switch did not block normal load current.");
            }
            if (!result.GeneratedNetlistContent.Contains("RSW1")) throw new InvalidOperationException("Manual switch did not use its deterministic RON/ROFF netlist element.");
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

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyForwardDiode(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ForwardDiode()).ConfigureAwait(false);
            ExpectSuccess(result);
            var diode = result.ComponentResults["d1"];
            if (diode == null) throw new InvalidOperationException("Forward diode result is missing the d1 component entry.");
            // 二极管电流来自 ngspice 的 @D[id] 真实输出，不得使用 C# 固定 0.7 V 近似。
            if (diode.Current <= DiodeCurrentTolerance) throw new InvalidOperationException("Forward-biased diode current must be positive: " + diode.Current);
            if (diode.Voltage <= 0d) throw new InvalidOperationException("Forward VAK (A->K) must be positive: " + diode.Voltage);
            if (!diode.Notes.Contains("正向导通")) throw new InvalidOperationException("Forward diode status must read 正向导通, got: " + diode.Notes);
            if (diode.CurrentDirection != "A-to-K" || diode.VoltageDirection != "A-to-K") throw new InvalidOperationException("Diode direction labels must use A-to-K.");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyReverseDiode(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ReverseDiode()).ConfigureAwait(false);
            ExpectSuccess(result);
            var diode = result.ComponentResults["d1"];
            if (diode == null) throw new InvalidOperationException("Reverse diode result is missing the d1 component entry.");
            if (diode.Notes.Contains("正向导通")) throw new InvalidOperationException("Reverse-biased diode must not report forward conduction.");
            // 反偏时电流应接近零（仅 IS 量级漏电流），VAK 为负。
            if (Math.Abs(diode.Current) > 1e-6d) throw new InvalidOperationException("Reverse diode current must be near zero: " + diode.Current);
            if (diode.Voltage >= -0.05d) throw new InvalidOperationException("Reverse VAK (A->K) must be negative: " + diode.Voltage);
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

        private static void ExpectFloatingClosedLoop()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.FloatingClosedLoop());
            ExpectInvalidFloatingGraph(graph, "complete floating loop");
            if (graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_TERMINAL")) throw new InvalidOperationException("Complete floating loop was mistaken for disconnected terminals.");
        }

        private static void ExpectGroundedCircuitDoesNotMaskFloatingSubcircuit()
        {
            ExpectInvalidFloatingGraph(SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.GroundedAndFloatingCircuits()), "grounded circuit plus floating loop");
        }

        private static void ExpectWireOrderDoesNotAffectNodeNames()
        {
            var first = SpiceT2Fixtures.Divider();
            var reordered = SpiceT2Fixtures.Divider();
            var wires = reordered.Wires.AsEnumerable().Reverse().ToList();
            reordered.Wires.Clear();
            reordered.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reordered, "Wire order");
        }

        private static void ExpectWireDirectionDoesNotAffectNodeNames()
        {
            var first = SpiceT2Fixtures.Divider();
            var reversedEndpoints = SpiceT2Fixtures.Divider();
            var wires = reversedEndpoints.Wires.Select(wire => new SpiceWireModel(wire.End, wire.Start)).ToList();
            reversedEndpoints.Wires.Clear();
            reversedEndpoints.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reversedEndpoints, "Wire direction");
        }

        private static void ExpectMultipleGroundsMapToZero()
        {
            var baseline = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.Divider());
            var multipleGrounds = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.DividerWithMultipleGrounds());
            if (!multipleGrounds.IsValid) throw new InvalidOperationException("Multiple ground symbols should form a valid grounded circuit.");
            if (multipleGrounds.NodeByTerminal[new SpiceTerminalRef("ground", SpiceComponentModel.GroundTerminalId)] != "0" ||
                multipleGrounds.NodeByTerminal[new SpiceTerminalRef("ground2", SpiceComponentModel.GroundTerminalId)] != "0")
            {
                throw new InvalidOperationException("All ground terminals must map to SPICE node 0.");
            }

            var baselineNodes = DescribeNodes(baseline, includeGrounds: false);
            var multipleGroundNodes = DescribeNodes(multipleGrounds, includeGrounds: false);
            if (baselineNodes != multipleGroundNodes) throw new InvalidOperationException("Additional ground symbols changed non-ground node names.");
        }

        private static void ExpectDiodeNetlistStable()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.ForwardDiode());
            if (!graph.IsValid) throw new InvalidOperationException("Forward diode fixture should produce a valid graph.");
            if (graph.SpiceNameByComponentId["d1"] != "D1") throw new InvalidOperationException("Diode SPICE element name should be the stable D1.");

            var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(SpiceT2Fixtures.ForwardDiode(), graph).Content;
            if (CountModelDirectives(netlist) != 1) throw new InvalidOperationException("D_GENERIC .model directive must be emitted exactly once for a single diode.");
            if (!netlist.Contains("print @D1[id]")) throw new InvalidOperationException("Netlist must request the real ngspice diode current via print @D1[id].");
            if (!netlist.Contains("D1 ")) throw new InvalidOperationException("Netlist must emit the D1 diode instance line.");

            // 两只二极管串联时，.model 段仍只生成一次。
            var twoDiodeGraph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.TwoForwardDiodes());
            if (!twoDiodeGraph.IsValid) throw new InvalidOperationException("Two-diode fixture should produce a valid graph.");
            var twoDiodeNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(SpiceT2Fixtures.TwoForwardDiodes(), twoDiodeGraph).Content;
            if (CountModelDirectives(twoDiodeNetlist) != 1) throw new InvalidOperationException("D_GENERIC .model directive must be emitted exactly once even with multiple diodes.");
            if (!twoDiodeNetlist.Contains("print @D1[id]") || !twoDiodeNetlist.Contains("print @D2[id]")) throw new InvalidOperationException("Each diode must request its own ngspice current.");
        }

        private static int CountModelDirectives(string netlist)
        {
            var count = 0;
            foreach (var line in netlist.Split('\n'))
            {
                if (line.Trim().StartsWith(".model D_GENERIC", StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private static void ExpectDiodeWireOrderDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.ForwardDiode();
            var reordered = SpiceT2Fixtures.ForwardDiode();
            var wires = reordered.Wires.AsEnumerable().Reverse().ToList();
            reordered.Wires.Clear();
            reordered.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reordered, "Diode wire order");
        }

        private static void ExpectDiodeWireDirectionDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.ForwardDiode();
            var reversedEndpoints = SpiceT2Fixtures.ForwardDiode();
            var wires = reversedEndpoints.Wires.Select(wire => new SpiceWireModel(wire.End, wire.Start)).ToList();
            reversedEndpoints.Wires.Clear();
            reversedEndpoints.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reversedEndpoints, "Diode wire direction");
        }

        private static void ExpectDiodeNotParameterEditable()
        {
            if (SpiceParameterUnits.UnitsFor(SpiceComponentKind.SiliconDiode).Length != 0) throw new InvalidOperationException("Silicon diode must expose an empty parameter unit array.");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.SiliconDiode, 0.7d)) throw new InvalidOperationException("Silicon diode must not accept parameter writes (0.7 V伪造值).");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.SiliconDiode, 1d)) throw new InvalidOperationException("Silicon diode must not accept any positive parameter write.");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.SiliconDiode, 0d)) throw new InvalidOperationException("Silicon diode must not accept zero parameter write.");
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

        private static async System.Threading.Tasks.Task VerifyFloatingSubcircuitsBlockSimulationAsync(SpiceDcSimulationService service)
        {
            await ExpectFloatingSubcircuitBlockedAsync(service, SpiceT2Fixtures.FloatingClosedLoop(), "complete floating loop").ConfigureAwait(false);
            await ExpectFloatingSubcircuitBlockedAsync(service, SpiceT2Fixtures.GroundedAndFloatingCircuits(), "grounded circuit plus floating loop").ConfigureAwait(false);
        }

        private static async System.Threading.Tasks.Task ExpectFloatingSubcircuitBlockedAsync(SpiceDcSimulationService service, SpiceCircuitModel circuit, string name)
        {
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null || result.NodeVoltages.Count != 0 || result.ComponentResults.Count != 0 ||
                !result.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT"))
            {
                throw new InvalidOperationException("Floating subcircuit was not rejected before ngspice: " + name + ".");
            }
        }

        private static async System.Threading.Tasks.Task VerifyMultipleGroundSimulationAsync(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.DividerWithMultipleGrounds()).ConfigureAwait(false);
            ExpectSuccess(result);
            ExpectNear(GetMiddleNode(result), 5d, VoltageTolerance, "multiple-ground divider middle voltage");
        }

        private static void ExpectInvalidFloatingGraph(SpiceCircuitGraph graph, string name)
        {
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT"))
            {
                throw new InvalidOperationException("Topology was expected to contain a floating subcircuit: " + name + ".");
            }
        }

        private static void ExpectEquivalentGraphAndNetlist(SpiceCircuitModel firstCircuit, SpiceCircuitModel secondCircuit, string difference)
        {
            var first = SpiceCircuitGraphBuilder.Build(firstCircuit);
            var second = SpiceCircuitGraphBuilder.Build(secondCircuit);
            if (!first.IsValid || !second.IsValid) throw new InvalidOperationException(difference + " regression fixture is invalid.");
            if (DescribeNodes(first, includeGrounds: true) != DescribeNodes(second, includeGrounds: true) ||
                DescribeSpiceNames(first) != DescribeSpiceNames(second) ||
                SpiceNetlistBuilder.BuildDcOperatingPoint(firstCircuit, first).Content != SpiceNetlistBuilder.BuildDcOperatingPoint(secondCircuit, second).Content)
            {
                throw new InvalidOperationException(difference + " changed deterministic node or netlist output.");
            }
        }

        private static string DescribeNodes(SpiceCircuitGraph graph, bool includeGrounds)
        {
            return string.Join(";", graph.NodeByTerminal
                .Where(pair => includeGrounds || pair.Key.TerminalId != SpiceComponentModel.GroundTerminalId)
                .OrderBy(pair => pair.Key.ComponentInstanceId, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.TerminalId, StringComparer.Ordinal)
                .Select(pair => pair.Key + "=" + pair.Value));
        }

        private static string DescribeSpiceNames(SpiceCircuitGraph graph)
        {
            return string.Join(";", graph.SpiceNameByComponentId.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
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
