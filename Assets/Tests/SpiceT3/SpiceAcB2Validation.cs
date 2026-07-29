using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.T3
{
    public static class SpiceAcB2Validation
    {
        public static void RunNetlistAndParserChecks()
        {
            ValidatePhasorMath();
            ValidateAcNetlistDeterminism();
            ValidateAcParserContract();
        }

        private static void ValidatePhasorMath()
        {
            var phasor = new SpicePhasor(0.5d, -0.5d);
            AssertClose(phasor.Magnitude, Math.Sqrt(0.5d), 1e-12d, "phasor magnitude");
            AssertClose(phasor.PhaseDegrees, -45d, 1e-12d, "phasor phase");
            var capacitorCurrent = phasor.MultiplyByJ(2d);
            AssertClose(capacitorCurrent.Real, 1d, 1e-12d, "j multiplication real");
            AssertClose(capacitorCurrent.Imaginary, 1d, 1e-12d, "j multiplication imaginary");
            var inductorCurrent = phasor.DivideByJ(2d);
            AssertClose(inductorCurrent.Real, -0.25d, 1e-12d, "j division real");
            AssertClose(inductorCurrent.Imaginary, -0.25d, 1e-12d, "j division imaginary");
            if (SpicePhasor.Zero.PhaseDegrees != 0d || !SpicePhasor.Zero.IsMagnitudeBelow(1e-12d))
                throw new InvalidOperationException("Zero phasor must have a stable zero phase.");
        }

        private static void ValidateAcNetlistDeterminism()
        {
            var circuit = CreateBasicCircuit();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (!graph.IsValid) throw new InvalidOperationException("Basic AC circuit graph should be valid.");
            var first = SpiceAcNetlistBuilder.Build(circuit, graph);
            var second = SpiceAcNetlistBuilder.Build(circuit, graph);
            if (first.Content != second.Content || first.OutputRequests.Count != 2)
                throw new InvalidOperationException("AC netlist output must be deterministic and contain node plus branch requests.");
            var nodeRequest = first.OutputRequests.Single(request => request.Kind == SpiceAcOutputKind.NodeVoltage);
            var branchRequest = first.OutputRequests.Single(request => request.Kind == SpiceAcOutputKind.BranchCurrent);
            if (!first.Content.Contains("V1 ") || !first.Content.Contains(" AC 1 30") ||
                !first.Content.Contains("ac lin 1 1000 1000") ||
                !first.Content.Contains("print " + nodeRequest.Expression) ||
                !first.Content.Contains("print " + branchRequest.Expression) ||
                first.Content.Contains("vm(") || first.Content.Contains("vp(") || first.Content.Contains("wrdata"))
                throw new InvalidOperationException("AC netlist did not use the required bare-complex single-point contract.");
        }

        private static void ValidateAcParserContract()
        {
            var requests = new List<SpiceAcOutputRequest>
            {
                new SpiceAcOutputRequest(SpiceAcOutputKind.NodeVoltage, "v(n001)", "n001"),
                new SpiceAcOutputRequest(SpiceAcOutputKind.BranchCurrent, "i(V1)", "V1")
            };
            var output = "ngspice banner\n" + SpiceAcNetlistBuilder.BeginMarker + "\n" +
                " i(v1) = -1.000000D-03 , 0.000000d+00 \n" +
                " V( n001 )=5.000000e-01,-5.000000E-01\n" + SpiceAcNetlistBuilder.EndMarker + "\nwarning outside";
            if (!SpiceAcOutputParser.TryParse(output, requests, out var values, out var failure, out var error) ||
                failure != SpiceAcParseFailure.None || error != null || values.Count != 2)
                throw new InvalidOperationException("AC parser rejected valid unordered bare-complex output.");
            AssertClose(values["n001"].Real, 0.5d, 1e-12d, "parsed real");
            AssertClose(values["n001"].Imaginary, -0.5d, 1e-12d, "parsed imaginary");
            AssertClose(values["V1"].Real, -0.001d, 1e-12d, "parsed branch current");

            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(n001)=1,0\nv(n001)=1,0\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.DuplicateExpression);
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(n001)=1,0\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.RequiredExpressionMissing);
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(unexpected)=1,0\ni(v1)=1,0\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.UnexpectedExpression);
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(n001)=1e999,0\ni(v1)=1,0\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.NonFiniteValue);
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(n001)=1,0\ni(v1)=1,0\n", requests, SpiceAcParseFailure.MarkersMissing);
            AssertParserFails(SpiceAcNetlistBuilder.EndMarker + "\n" + SpiceAcNetlistBuilder.BeginMarker, requests, SpiceAcParseFailure.MarkersOutOfOrder);
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nnot-data\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.MalformedLine);
        }

        private static void AssertParserFails(string output, IReadOnlyList<SpiceAcOutputRequest> requests, SpiceAcParseFailure expected)
        {
            if (SpiceAcOutputParser.TryParse(output, requests, out _, out var failure, out _) || failure != expected)
                throw new InvalidOperationException("AC parser failure contract mismatch. Expected " + expected + ", actual " + failure + ".");
        }

        private static SpiceCircuitModel CreateBasicCircuit()
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 30d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "positive"), new SpiceTerminalRef("resistor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            return circuit;
        }

        private static void AssertClose(double actual, double expected, double tolerance, string label)
        {
            if (Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(label + " mismatch. Expected " + expected + ", actual " + actual + ".");
        }
    }
}
