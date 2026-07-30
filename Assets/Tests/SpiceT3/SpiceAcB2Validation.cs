using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Infrastructure;

namespace ElectricalSim.Spice.T3
{
    public static class SpiceAcB2Validation
    {
        public static void RunNetlistAndParserChecks()
        {
            ValidatePhasorMath();
            ValidateAcNetlistDeterminism();
            ValidateAcParserContract();
            ValidateInjectedAcService();
            ValidateUnifiedSimulationServiceDispatch();
            RunRealAcFixtures();
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
            AssertParserFails(SpiceAcNetlistBuilder.BeginMarker + "\nv(n001)=1,0\n" + SpiceAcNetlistBuilder.EndMarker + "\n" + SpiceAcNetlistBuilder.BeginMarker + "\ni(v1)=1,0\n" + SpiceAcNetlistBuilder.EndMarker,
                requests, SpiceAcParseFailure.MarkersDuplicate);
            try
            {
                SpiceAcOutputParser.TryParse(SpiceAcNetlistBuilder.BeginMarker + "\n" + SpiceAcNetlistBuilder.EndMarker,
                    new List<SpiceAcOutputRequest> { new SpiceAcOutputRequest(SpiceAcOutputKind.NodeVoltage, "v(a)", "same"), new SpiceAcOutputRequest(SpiceAcOutputKind.BranchCurrent, "i(v1)", "same") }, out _, out _, out _);
                throw new InvalidOperationException("Duplicate AC result keys must be rejected.");
            }
            catch (ArgumentException) { }
        }

        private static void ValidateInjectedAcService()
        {
            var circuit = CreateBasicCircuit();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            var document = SpiceAcNetlistBuilder.Build(circuit, graph);
            var stdout = SpiceAcNetlistBuilder.BeginMarker + "\n" +
                string.Join("\n", document.OutputRequests.Select(request => request.Expression + " = " +
                    (request.Kind == SpiceAcOutputKind.NodeVoltage ? "0.866025403784,0.5" : "-0.000866025403784,-0.0005"))) +
                "\n" + SpiceAcNetlistBuilder.EndMarker;
            var service = new SpiceAcSimulationService((_, _, _, _) => System.Threading.Tasks.Task.FromResult(new NgspiceRunResult
            {
                Success = true,
                ExitCode = 0,
                StandardOutput = stdout,
                StandardError = "Warning: optional initialization file was not found."
            }));
            var result = service.SimulateAsync(circuit).GetAwaiter().GetResult();
            if (!result.Success || result.NodeVoltages.Count != 0 || result.ComponentResults.Count != 0 ||
                result.AcNodeVoltages.Count != 2 || result.AcComponentResults.Count != 2 ||
                result.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
                throw new InvalidOperationException("Injected AC service did not preserve DC/AC result isolation.");
            AssertClose(result.AcComponentResults["resistor-001"].Current.Real, 0.000866025403784d, 1e-12d, "AC resistor current real");
            AssertClose(result.AcComponentResults["ac-source-001"].Current.Real, -0.000866025403784d, 1e-12d, "AC source current sign");

            var errorService = new SpiceAcSimulationService((_, _, _, _) => System.Threading.Tasks.Task.FromResult(new NgspiceRunResult
            {
                Success = true,
                ExitCode = 0,
                StandardOutput = stdout,
                StandardError = "Error: invalid analysis command"
            }));
            var errorResult = errorService.SimulateAsync(circuit).GetAwaiter().GetResult();
            if (errorResult.Success || !errorResult.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_AC_NGSPICE_ERROR"))
                throw new InvalidOperationException("AC stderr Error: must reject exit-code-zero output.");
            if (!SpiceNgspiceErrorClassifier.TryGetSevereErrorLine("Fatal: solver stopped", out _) ||
                SpiceNgspiceErrorClassifier.TryGetSevereErrorLine("Note: normal diagnostic\nWarning: allowed", out _))
                throw new InvalidOperationException("AC stderr severity classifier mismatch.");
        }

        private static void ValidateUnifiedSimulationServiceDispatch()
        {
            var dcCalls = 0;
            var acCalls = 0;
            var service = new SpiceSimulationService(
                (circuit, _) =>
                {
                    dcCalls++;
                    if (circuit.AnalysisSettings.Mode != SpiceAnalysisMode.DcOperatingPoint)
                        throw new InvalidOperationException("Unified service sent a non-DC circuit to the DC service.");
                    return System.Threading.Tasks.Task.FromResult(new SpiceSimulationResult { Success = true, AnalysisSettings = circuit.AnalysisSettings.Copy() });
                },
                (circuit, _) =>
                {
                    acCalls++;
                    if (circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
                        throw new InvalidOperationException("Unified service sent a non-AC circuit to the AC service.");
                    return System.Threading.Tasks.Task.FromResult(new SpiceSimulationResult { Success = true, AnalysisSettings = circuit.AnalysisSettings.Copy() });
                });

            var dcResult = service.SimulateAsync(new SpiceCircuitModel()).GetAwaiter().GetResult();
            var acResult = service.SimulateAsync(new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d))).GetAwaiter().GetResult();
            if (!dcResult.Success || !acResult.Success || dcCalls != 1 || acCalls != 1)
                throw new InvalidOperationException("Unified simulation service did not dispatch exactly once to each analysis-specific service.");

            var unsupportedCircuit = new SpiceCircuitModel(new SpiceAnalysisSettings((SpiceAnalysisMode)999, 1000d));
            var unsupportedResult = service.SimulateAsync(unsupportedCircuit).GetAwaiter().GetResult();
            if (unsupportedResult.Success || dcCalls != 1 || acCalls != 1 ||
                !unsupportedResult.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_ANALYSIS_MODE_UNSUPPORTED"))
                throw new InvalidOperationException("Unified simulation service did not reject an unsupported analysis mode without dispatching it.");
        }

        private static void RunRealAcFixtures()
        {
            ValidateRealResistorFixture();
            ValidateRealSourcePhaseFixture();
            ValidateRealRcLowPassFixture();
            ValidateRealRcHighPassFixture();
            ValidateRealRlFixture();
            ValidateRealRlcResonanceFixture();
            ValidateRealClosedSwitchFixture();
            ValidateRealOpenSwitchFixture();
            ValidateRealVoltageProbeFixtures();
            ValidateRealCurrentProbeFixtures();
            ValidateRealDualSourceFixtures();
            UnityEngine.Debug.Log("[Spice][AC-B2] 真实 ngspice Fixtures=14/14：通过");
        }

        private static SpiceSimulationResult RunRealAcFixture(string name, SpiceCircuitModel circuit)
        {
            var result = new SpiceSimulationService().SimulateAsync(circuit).GetAwaiter().GetResult();
            if (!result.Success)
                throw new InvalidOperationException(name + " failed: " + string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
            if (result.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency || result.NodeVoltages.Count != 0 ||
                result.ComponentResults.Count != 0 || result.AcNodeVoltages.Count == 0 || result.AcComponentResults.Count == 0 ||
                result.RawNgspiceResult == null || !result.RawNgspiceResult.Success ||
                string.IsNullOrEmpty(result.GeneratedNetlistContent) || !result.GeneratedNetlistContent.Contains("ac lin 1"))
                throw new InvalidOperationException(name + " did not complete the real AC service contract.");
            return result;
        }

        private static void ValidateRealResistorFixture()
        {
            var result = RunRealAcFixture("resistor", CreateResistorCircuit(0d, 1000d, 1000d));
            AssertPhasorClose(result.AcNodeVoltages.Values.First(value => value.Magnitude > 0.5d), 1d, 0d, "resistor node");
            AssertPhasorClose(result.AcComponentResults["resistor-001"].Current, .001d, 0d, "resistor current");
            AssertPhasorClose(result.AcComponentResults["ac-source-001"].Current, -.001d, 0d, "source current");
        }

        private static void ValidateRealSourcePhaseFixture()
        {
            var result = RunRealAcFixture("source phase", CreateResistorCircuit(30d, 1000d, 1000d));
            var c = Math.Sqrt(3d) / 2d;
            AssertPhasorClose(result.AcComponentResults["resistor-001"].Current, .001d * c, .0005d, "30 degree resistor current");
            AssertPhasorClose(result.AcComponentResults["ac-source-001"].Current, -.001d * c, -.0005d, "30 degree source current");
        }

        private static void ValidateRealRcLowPassFixture() => ValidateFilterFixture("RC low pass", SpiceComponentModel.Resistor("resistor-001", 1000d), SpiceComponentModel.Capacitor("capacitor-001", 1e-6d), .5d, -.5d);
        private static void ValidateRealRcHighPassFixture() => ValidateFilterFixture("RC high pass", SpiceComponentModel.Capacitor("capacitor-001", 1e-6d), SpiceComponentModel.Resistor("resistor-001", 1000d), .5d, .5d);

        private static void ValidateFilterFixture(string name, SpiceComponentModel first, SpiceComponentModel second, double expectedReal, double expectedImaginary)
        {
            var circuit = CreateSeriesCircuit(first, second, 1d / (2d * Math.PI * 1000d * 1e-6d));
            var result = RunRealAcFixture(name, circuit);
            var output = GetNodeVoltage(result, circuit, second.InstanceId, SpiceComponentModel.PositiveTerminalId, name + " Vout");
            AssertPhasorClose(output, expectedReal, expectedImaginary, name + " output");
        }

        private static void ValidateRealRlFixture()
        {
            var result = RunRealAcFixture("RL", CreateSeriesCircuit(SpiceComponentModel.Resistor("resistor-001", 1000d), SpiceComponentModel.Inductor("inductor-001", 1d), 1000d / (2d * Math.PI)));
            AssertPhasorClose(result.AcComponentResults["resistor-001"].Current, .0005d, -.0005d, "RL current");
        }

        private static void ValidateRealRlcResonanceFixture()
        {
            var frequency = 1d / (2d * Math.PI * Math.Sqrt(.01d * 1e-6d));
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, frequency));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 100d));
            circuit.Components.Add(SpiceComponentModel.Inductor("inductor-001", .01d));
            circuit.Components.Add(SpiceComponentModel.Capacitor("capacitor-001", 1e-6d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "positive"), new SpiceTerminalRef("resistor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-001", "negative"), new SpiceTerminalRef("inductor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("inductor-001", "negative"), new SpiceTerminalRef("capacitor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("capacitor-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            var result = RunRealAcFixture("RLC resonance", circuit);
            AssertClose(result.AcComponentResults["resistor-001"].Current.Magnitude, .01d, 1e-8d, "RLC current magnitude");
        }

        private static void ValidateRealClosedSwitchFixture() => ValidateSwitchFixture(true);
        private static void ValidateRealOpenSwitchFixture() => ValidateSwitchFixture(false);
        private static void ValidateSwitchFixture(bool closed)
        {
            var circuit = CreateSeriesCircuit(SpiceComponentModel.IdealSwitch("switch-001", closed), SpiceComponentModel.Resistor("resistor-001", 1000d), 1000d);
            var result = RunRealAcFixture(closed ? "closed switch" : "open switch", circuit);
            if (!result.AcComponentResults["switch-001"].Notes.Contains("static-resistance")) throw new InvalidOperationException("Switch result must state its static resistance model.");
            if (closed) AssertClose(result.AcComponentResults["switch-001"].Current.Magnitude, .001d, 1e-8d, "closed switch current");
            else if (result.AcComponentResults["switch-001"].Current.Magnitude > 1e-9d) throw new InvalidOperationException("Open switch current is too large.");
        }

        private static void ValidateRealVoltageProbeFixtures()
        {
            var forward = RunRealAcFixture("voltage probe forward", CreateVoltageProbeCircuit(false));
            var reverse = RunRealAcFixture("voltage probe reverse", CreateVoltageProbeCircuit(true));
            var a = forward.AcComponentResults["voltage-probe-001"].Voltage;
            var b = reverse.AcComponentResults["voltage-probe-001"].Voltage;
            AssertPhasorClose(a, 1d, 0d, "voltage probe forward");
            AssertPhasorClose(b, -1d, 0d, "voltage probe reverse");
        }

        private static void ValidateRealCurrentProbeFixtures()
        {
            var forward = RunRealAcFixture("current probe forward", CreateCurrentProbeCircuit(false));
            var reverse = RunRealAcFixture("current probe reverse", CreateCurrentProbeCircuit(true));
            AssertPhasorClose(forward.AcComponentResults["current-probe-001"].Current, .001d, 0d, "current probe forward");
            AssertPhasorClose(reverse.AcComponentResults["current-probe-001"].Current, -.001d, 0d, "current probe reverse");
        }

        private static void ValidateRealDualSourceFixtures()
        {
            var orthogonalCircuit = CreateDualSourceCircuit(90d);
            var cancellingCircuit = CreateDualSourceCircuit(180d);
            var orthogonal = RunRealAcFixture("orthogonal sources", orthogonalCircuit);
            var cancelling = RunRealAcFixture("cancelling sources", cancellingCircuit);
            var orthogonalOutput = GetNodeVoltage(orthogonal, orthogonalCircuit, "resistor-003", SpiceComponentModel.PositiveTerminalId, "orthogonal Vout");
            AssertPhasorClose(orthogonalOutput, 1d / 3d, 1d / 3d, "orthogonal Vout");

            var cancellingOutput = GetNodeVoltage(cancelling, cancellingCircuit, "resistor-003", SpiceComponentModel.PositiveTerminalId, "cancelling Vout");
            AssertFinite(cancellingOutput.Real, "cancelling Vout real");
            AssertFinite(cancellingOutput.Imaginary, "cancelling Vout imaginary");
            AssertFinite(cancellingOutput.Magnitude, "cancelling Vout magnitude");
            AssertRealFixtureClose(cancellingOutput.Real, 0d, "cancelling Vout real");
            AssertRealFixtureClose(cancellingOutput.Imaginary, 0d, "cancelling Vout imaginary");
            AssertRealFixtureClose(cancellingOutput.Magnitude, 0d, "cancelling Vout magnitude");
        }

        private static SpicePhasor GetNodeVoltage(SpiceSimulationResult result, SpiceCircuitModel circuit, string componentId, string terminalId, string label)
        {
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (!graph.IsValid)
                throw new InvalidOperationException(label + " fixture topology was unexpectedly invalid.");
            var terminal = new SpiceTerminalRef(componentId, terminalId);
            if (!graph.NodeByTerminal.TryGetValue(terminal, out var node) || !result.AcNodeVoltages.TryGetValue(node, out var voltage))
                throw new InvalidOperationException(label + " node was missing from the AC result.");
            return voltage;
        }

        private static void AssertFinite(double value, string label)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(label + " must be finite.");
        }

        private static void AssertParserFails(string output, IReadOnlyList<SpiceAcOutputRequest> requests, SpiceAcParseFailure expected)
        {
            if (SpiceAcOutputParser.TryParse(output, requests, out _, out var failure, out _) || failure != expected)
                throw new InvalidOperationException("AC parser failure contract mismatch. Expected " + expected + ", actual " + failure + ".");
        }

        private static SpiceCircuitModel CreateBasicCircuit()
        {
            return CreateResistorCircuit(30d, 1000d, 1000d);
        }

        private static SpiceCircuitModel CreateResistorCircuit(double phase, double resistance, double frequency)
        {
            return CreateSeriesCircuit(SpiceComponentModel.Resistor("resistor-001", resistance), null, frequency, phase);
        }

        private static SpiceCircuitModel CreateVoltageProbeCircuit(bool reverse)
        {
            var circuit = CreateResistorCircuit(0d, 1000d, 1000d);
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("voltage-probe-001"));
            var positive = reverse ? "ground-001" : "ac-source-001";
            var negative = reverse ? "ac-source-001" : "ground-001";
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("voltage-probe-001", "positive"), new SpiceTerminalRef(positive, reverse ? "ground" : "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("voltage-probe-001", "negative"), new SpiceTerminalRef(negative, reverse ? "positive" : "ground")));
            return circuit;
        }

        private static SpiceCircuitModel CreateCurrentProbeCircuit(bool reverse)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("current-probe-001"));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            var sourceToProbe = reverse ? "negative" : "positive";
            var probeFromSource = reverse ? "negative" : "positive";
            var probeToResistor = reverse ? "positive" : "negative";
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "positive"), new SpiceTerminalRef("current-probe-001", probeFromSource)));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("current-probe-001", probeToResistor), new SpiceTerminalRef("resistor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            return circuit;
        }

        private static SpiceCircuitModel CreateDualSourceCircuit(double secondPhase)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-002", 1d, secondPhase));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-002", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-003", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-002", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "positive"), new SpiceTerminalRef("resistor-001", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-002", "positive"), new SpiceTerminalRef("resistor-002", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-001", "negative"), new SpiceTerminalRef("resistor-003", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-002", "negative"), new SpiceTerminalRef("resistor-003", "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("resistor-003", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            return circuit;
        }

        private static SpiceCircuitModel CreateSeriesCircuit(SpiceComponentModel first, SpiceComponentModel second, double frequency, double phase = 0d)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, frequency));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, phase));
            circuit.Components.Add(first);
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "positive"), new SpiceTerminalRef(first.InstanceId, "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef("ac-source-001", "negative"), new SpiceTerminalRef("ground-001", "ground")));
            if (second == null)
            {
                circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(first.InstanceId, "negative"), new SpiceTerminalRef("ground-001", "ground")));
                return circuit;
            }
            circuit.Components.Add(second);
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(first.InstanceId, "negative"), new SpiceTerminalRef(second.InstanceId, "positive")));
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(second.InstanceId, "negative"), new SpiceTerminalRef("ground-001", "ground")));
            return circuit;
        }

        private static void AssertPhasorClose(SpicePhasor actual, double real, double imaginary, string label)
        {
            AssertRealFixtureClose(actual.Real, real, label + " real");
            AssertRealFixtureClose(actual.Imaginary, imaginary, label + " imaginary");
        }

        private static void AssertRealFixtureClose(double actual, double expected, string label)
        {
            const double absoluteTolerance = 1e-8d;
            const double relativeTolerance = 1e-6d;
            var allowed = absoluteTolerance + relativeTolerance * Math.Abs(expected);
            if (Math.Abs(actual - expected) > allowed)
                throw new InvalidOperationException(label + " mismatch. Expected " + expected + ", actual " + actual + ", allowed " + allowed + ".");
        }

        private static void AssertClose(double actual, double expected, double tolerance, string label)
        {
            if (Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(label + " mismatch. Expected " + expected + ", actual " + actual + ".");
        }
    }
}
