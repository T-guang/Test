using System;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using UnityEngine;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// 理想运放 V1 的真实闭环回归。期望值使用有限 1e6 开环增益推导，不能把反馈放大器误判为数学无限增益。
    /// </summary>
    public static class SpiceIdealOperationalAmplifierValidation
    {
        private const double RelativeTolerance = 2e-5d;
        private const double AbsoluteTolerance = 2e-7d;

        public static void RunAll()
        {
            ValidateCoreAndNetlist();
            ValidateTopologyDiagnostics();
            ValidateV1SaveBoundary();
            ValidateDcVoltageFollower();
            ValidateDcInvertingAmplifier();
            ValidateAcVoltageFollower();
            ValidateAcInvertingAmplifier();
            Debug.Log("[Spice][OpAmp] 真实 ngspice 回路：4/4 通过");
        }

        private static void ValidateCoreAndNetlist()
        {
            var opAmp = SpiceComponentModel.IdealOperationalAmplifier("opamp-001");
            if (!opAmp.HasTerminal(SpiceComponentModel.NonInvertingTerminalId) ||
                !opAmp.HasTerminal(SpiceComponentModel.InvertingTerminalId) ||
                !opAmp.HasTerminal(SpiceComponentModel.OutputTerminalId) ||
                SpiceComponentModel.TerminalIdsFor(opAmp.Kind).Count != 3 ||
                SpiceComponentModel.TerminalIdsFor(opAmp.Kind)[0] != SpiceComponentModel.NonInvertingTerminalId ||
                SpiceComponentModel.TerminalIdsFor(opAmp.Kind)[1] != SpiceComponentModel.InvertingTerminalId ||
                SpiceComponentModel.TerminalIdsFor(opAmp.Kind)[2] != SpiceComponentModel.OutputTerminalId ||
                SpiceComponentDefaults.IdealOperationalAmplifierOpenLoopGain != 1e6d)
                throw new InvalidOperationException("Ideal operational amplifier core contract is incomplete.");

            var dc = CreateFollower(false);
            var graph = SpiceCircuitGraphBuilder.Build(dc);
            if (!graph.IsValid || graph.SpiceNameByComponentId["opamp-001"] != "EOP1")
                throw new InvalidOperationException("Ideal operational amplifier graph naming is not deterministic.");
            var expectedDcLine = "EOP1 " + graph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.OutputTerminalId)] + " 0 " +
                graph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.NonInvertingTerminalId)] + " " +
                graph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.InvertingTerminalId)] + " 1000000";
            var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(dc, graph).Content;
            if (!netlist.Split('\n').Any(line => line.TrimEnd('\r') == expectedDcLine))
                throw new InvalidOperationException("DC VCVS line was not generated.");

            var ac = CreateFollower(true);
            var acGraph = SpiceCircuitGraphBuilder.Build(ac);
            var acNetlist = SpiceAcNetlistBuilder.Build(ac, acGraph);
            var expectedAcLine = "EOP1 " + acGraph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.OutputTerminalId)] + " 0 " +
                acGraph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.NonInvertingTerminalId)] + " " +
                acGraph.NodeByTerminal[new SpiceTerminalRef("opamp-001", SpiceComponentModel.InvertingTerminalId)] + " 1000000";
            if (!acGraph.IsValid || !acNetlist.Content.Split('\n').Any(line => line.TrimEnd('\r') == expectedAcLine) ||
                !acNetlist.OutputRequests.Any(request => request.Expression == "i(EOP1)"))
                throw new InvalidOperationException("AC VCVS line or branch-current request was not generated.");

            var twoOpAmps = SpiceCircuitGraphBuilder.Build(CreateTwoFollowers());
            if (!twoOpAmps.IsValid || twoOpAmps.SpiceNameByComponentId["opamp-001"] != "EOP1" || twoOpAmps.SpiceNameByComponentId["opamp-002"] != "EOP2")
                throw new InvalidOperationException("Multiple ideal operational amplifier names are not deterministic.");
        }

        private static void ValidateTopologyDiagnostics()
        {
            var follower = SpiceCircuitGraphBuilder.Build(CreateFollower(false));
            var inverting = SpiceCircuitGraphBuilder.Build(CreateInverting(false));
            if (!follower.IsValid || !inverting.IsValid)
                throw new InvalidOperationException("Feedback circuits must remain graph-valid.");

            var floating = CreateFloatingInputCircuit();
            var floatingGraph = SpiceCircuitGraphBuilder.Build(floating);
            if (floatingGraph.IsValid || !floatingGraph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT"))
                throw new InvalidOperationException("Floating op-amp inputs must report SPICE_FLOATING_SUBCIRCUIT.");
            var rejected = new SpiceSimulationService().SimulateAsync(floating).GetAwaiter().GetResult();
            if (rejected.Success || rejected.RawNgspiceResult != null)
                throw new InvalidOperationException("Floating op-amp input diagnostics must reject before invoking ngspice.");

            foreach (var reverse in new[] { false, true })
            {
                var shorted = CreateOutputShortCircuit(reverse);
                var shortedGraph = SpiceCircuitGraphBuilder.Build(shorted);
                if (shortedGraph.IsValid || !shortedGraph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_OPAMP_OUTPUT_SHORTED"))
                    throw new InvalidOperationException("Op-amp output-to-ground short must be rejected in both wire directions.");
            }
        }

        private static void ValidateDcVoltageFollower()
        {
            var result = Run("DC voltage follower", CreateFollower(false));
            var expected = 1e6d / (1e6d + 1d);
            AssertClose(result.ComponentResults["opamp-001"].Voltage, expected, "DC follower Vout");
            AssertFinite(result.ComponentResults["opamp-001"].Current, "DC follower i(EOP1)");
        }

        private static void ValidateV1SaveBoundary()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.zero);
            if (SpiceDrawingSerializer.TryValidateSchemaV1SaveCompatibility(model, out var error) ||
                string.IsNullOrEmpty(error) || !error.Contains("理想运算放大器"))
                throw new InvalidOperationException("V1 must reject an ideal operational amplifier before serializing a drawing.");
        }

        private static void ValidateDcInvertingAmplifier()
        {
            var result = Run("DC inverting amplifier", CreateInverting(false));
            var expected = -10d / (1d + 11d / 1e6d);
            AssertClose(result.ComponentResults["opamp-001"].Voltage, expected, "DC inverting Vout");
            AssertFinite(result.ComponentResults["opamp-001"].Current, "DC inverting i(EOP1)");
        }

        private static void ValidateAcVoltageFollower()
        {
            var result = Run("AC voltage follower", CreateFollower(true));
            var voltage = result.AcComponentResults["opamp-001"].Voltage;
            AssertClose(voltage.Magnitude, 1e6d / (1e6d + 1d), "AC follower magnitude");
            AssertPhaseClose(voltage.PhaseDegrees, 30d, "AC follower phase");
            AssertFinite(result.AcComponentResults["opamp-001"].Current.Magnitude, "AC follower i(EOP1)");
        }

        private static void ValidateAcInvertingAmplifier()
        {
            var result = Run("AC inverting amplifier", CreateInverting(true));
            var voltage = result.AcComponentResults["opamp-001"].Voltage;
            AssertClose(voltage.Magnitude, 10d / (1d + 11d / 1e6d), "AC inverting magnitude");
            AssertPhaseClose(voltage.PhaseDegrees, -150d, "AC inverting phase");
            AssertFinite(result.AcComponentResults["opamp-001"].Current.Magnitude, "AC inverting i(EOP1)");
        }

        private static SpiceSimulationResult Run(string name, SpiceCircuitModel circuit)
        {
            var result = new SpiceSimulationService().SimulateAsync(circuit).GetAwaiter().GetResult();
            if (!result.Success)
                throw new InvalidOperationException(name + " failed: " + string.Join(" | ", result.Diagnostics.Select(diagnostic => diagnostic.Code + ":" + diagnostic.Message)));
            if (result.RawNgspiceResult == null || !result.RawNgspiceResult.Success)
                throw new InvalidOperationException(name + " did not use the real ngspice service path.");
            // 审查证据直接记录正式 Service 返回的网表和 ngspice 原始输出，避免审查包另行拼装近似 netlist。
            Debug.Log("__SPICE_OPAMP_FIXTURE_EVIDENCE_BEGIN__\nfixture=" + name +
                "\n-- generated.cir --\n" + result.GeneratedNetlistContent +
                "\n-- stdout --\n" + result.RawNgspiceResult.StandardOutput +
                "\n-- stderr --\n" + result.RawNgspiceResult.StandardError +
                "\n-- exit-code --\n" + result.RawNgspiceResult.ExitCode +
                "\n__SPICE_OPAMP_FIXTURE_EVIDENCE_END__");
            return result;
        }

        private static SpiceCircuitModel CreateFollower(bool ac)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(ac ? SpiceAnalysisMode.AcSingleFrequency : SpiceAnalysisMode.DcOperatingPoint, 1000d));
            circuit.Components.Add(ac ? SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 30d) : SpiceComponentModel.DcVoltageSource("source-001", 1d));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-001"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            Wire(circuit, ac ? "ac-source-001" : "source-001", SpiceComponentModel.PositiveTerminalId, "opamp-001", SpiceComponentModel.NonInvertingTerminalId);
            Wire(circuit, ac ? "ac-source-001" : "source-001", SpiceComponentModel.NegativeTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            // 同器件 IN- 与 OUT 的直接反馈是 GraphBuilder 明确允许的电压跟随器拓扑。
            Wire(circuit, "opamp-001", SpiceComponentModel.InvertingTerminalId, "opamp-001", SpiceComponentModel.OutputTerminalId);
            return circuit;
        }

        private static SpiceCircuitModel CreateInverting(bool ac)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(ac ? SpiceAnalysisMode.AcSingleFrequency : SpiceAnalysisMode.DcOperatingPoint, 1000d));
            var sourceId = ac ? "ac-source-001" : "source-001";
            circuit.Components.Add(ac ? SpiceComponentModel.AcVoltageSource(sourceId, 1d, 30d) : SpiceComponentModel.DcVoltageSource(sourceId, 1d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-002", 10000d));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-001"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            Wire(circuit, sourceId, SpiceComponentModel.PositiveTerminalId, "resistor-001", SpiceComponentModel.PositiveTerminalId);
            Wire(circuit, "resistor-001", SpiceComponentModel.NegativeTerminalId, "opamp-001", SpiceComponentModel.InvertingTerminalId);
            Wire(circuit, "resistor-002", SpiceComponentModel.NegativeTerminalId, "opamp-001", SpiceComponentModel.InvertingTerminalId);
            Wire(circuit, "resistor-002", SpiceComponentModel.PositiveTerminalId, "opamp-001", SpiceComponentModel.OutputTerminalId);
            Wire(circuit, sourceId, SpiceComponentModel.NegativeTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            Wire(circuit, "opamp-001", SpiceComponentModel.NonInvertingTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            return circuit;
        }

        private static SpiceCircuitModel CreateTwoFollowers()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source-001", 1d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source-002", 2d));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-001"));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-002"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            foreach (var index in new[] { "001", "002" })
            {
                Wire(circuit, "source-" + index, SpiceComponentModel.PositiveTerminalId, "opamp-" + index, SpiceComponentModel.NonInvertingTerminalId);
                Wire(circuit, "source-" + index, SpiceComponentModel.NegativeTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
                Wire(circuit, "opamp-" + index, SpiceComponentModel.InvertingTerminalId, "opamp-" + index, SpiceComponentModel.OutputTerminalId);
            }
            return circuit;
        }

        private static SpiceCircuitModel CreateFloatingInputCircuit()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source-001", 1d));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-001"));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            Wire(circuit, "source-001", SpiceComponentModel.PositiveTerminalId, "opamp-001", SpiceComponentModel.NonInvertingTerminalId);
            Wire(circuit, "source-001", SpiceComponentModel.NegativeTerminalId, "opamp-001", SpiceComponentModel.InvertingTerminalId);
            Wire(circuit, "opamp-001", SpiceComponentModel.OutputTerminalId, "resistor-001", SpiceComponentModel.PositiveTerminalId);
            Wire(circuit, "resistor-001", SpiceComponentModel.NegativeTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            return circuit;
        }

        private static SpiceCircuitModel CreateOutputShortCircuit(bool reverse)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source-001", 1d));
            circuit.Components.Add(SpiceComponentModel.IdealOperationalAmplifier("opamp-001"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            Wire(circuit, "source-001", SpiceComponentModel.PositiveTerminalId, "opamp-001", SpiceComponentModel.NonInvertingTerminalId);
            Wire(circuit, "source-001", SpiceComponentModel.NegativeTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            Wire(circuit, "opamp-001", SpiceComponentModel.InvertingTerminalId, "source-001", SpiceComponentModel.PositiveTerminalId);
            if (reverse) Wire(circuit, "ground-001", SpiceComponentModel.GroundTerminalId, "opamp-001", SpiceComponentModel.OutputTerminalId);
            else Wire(circuit, "opamp-001", SpiceComponentModel.OutputTerminalId, "ground-001", SpiceComponentModel.GroundTerminalId);
            return circuit;
        }

        private static void Wire(SpiceCircuitModel circuit, string startId, string startTerminal, string endId, string endTerminal)
        {
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(startId, startTerminal), new SpiceTerminalRef(endId, endTerminal)));
        }

        private static void AssertClose(double actual, double expected, string label)
        {
            var tolerance = AbsoluteTolerance + RelativeTolerance * Math.Abs(expected);
            if (double.IsNaN(actual) || double.IsInfinity(actual) || Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException(label + " expected " + expected + " actual " + actual + " tolerance " + tolerance + ".");
        }

        private static void AssertPhaseClose(double actual, double expected, string label)
        {
            var delta = SpiceAnalysisLimits.NormalizePhaseDegrees(actual - expected);
            if (Math.Abs(delta) > 0.02d)
                throw new InvalidOperationException(label + " expected " + expected + " actual " + actual + " phase delta " + delta + ".");
        }

        private static void AssertFinite(double value, string label)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException(label + " is not finite.");
        }
    }
}
