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
            ExpectVoltageProbeNotInNetlist();
            ExpectVoltageProbeNotParameterEditable();
            ExpectVoltageProbeWireOrderDoesNotAffectNetlist();
            ExpectVoltageProbeWireDirectionDoesNotAffectNetlist();
            ExpectVoltageProbeComponentOrderDoesNotAffectNetlist();
            ExpectVoltageProbeSameNodeNoShortDiagnostic();
            ExpectVoltageProbeDoesNotBridgeFloatingSubcircuit();
            ExpectCurrentProbeNetlistStable();
            ExpectCurrentProbeWireOrderDoesNotAffectNetlist();
            ExpectCurrentProbeWireDirectionDoesNotAffectNetlist();
            ExpectCurrentProbeNotParameterEditable();
            ExpectCurrentProbeConstraintConflict();
            ExpectDcLibraryV1NetlistContract();
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
            // 电压探针：基础差分、非侵入、双探针、无效连接四组。
            results.Add(await VerifyVoltageProbeAcrossSource(service).ConfigureAwait(false));
            results.Add(await VerifyReversedVoltageProbe(service).ConfigureAwait(false));
            results.Add(await VerifyVoltageProbeSameNode(service).ConfigureAwait(false));
            results.Add(await VerifyVoltageProbeNonInvasive(service).ConfigureAwait(false));
            results.Add(await VerifyTwoVoltageProbes(service).ConfigureAwait(false));
            await VerifyVoltageProbeOnlyPositiveConnectedAsync(service).ConfigureAwait(false);
            await VerifyVoltageProbeBothDisconnectedAsync(service).ConfigureAwait(false);
            await VerifyVoltageProbeOnFloatingSubcircuitAsync(service).ConfigureAwait(false);
            await VerifyVoltageProbeWithoutGroundAsync(service).ConfigureAwait(false);
            results.Add(await VerifyCurrentProbeSeries(service).ConfigureAwait(false));
            results.Add(await VerifyReversedCurrentProbe(service).ConfigureAwait(false));
            results.Add(await VerifyTwoCurrentProbes(service).ConfigureAwait(false));
            results.Add(await VerifyDcLibraryV1SeriesChain(service).ConfigureAwait(false));
            await VerifyCurrentProbeOnlyInConnectedAsync(service).ConfigureAwait(false);
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

        // 电压探针不得在网表中输出 R/V/I/D/L/C 元件行，也不得分配 SPICE 名称。
        private static void ExpectVoltageProbeNotInNetlist()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.VoltageProbeAcrossSource());
            if (!graph.IsValid) throw new InvalidOperationException("Voltage probe fixture should produce a valid graph.");
            if (graph.SpiceNameByComponentId.ContainsKey("vprobe-1")) throw new InvalidOperationException("Voltage probe must not be assigned a SPICE element name.");
            if (graph.ComponentIdBySpiceName.Values.Contains("vprobe-1")) throw new InvalidOperationException("Voltage probe instance id must not appear in the SPICE name reverse map.");

            var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(SpiceT2Fixtures.VoltageProbeAcrossSource(), graph).Content;
            // 探针不得产生独立元件行：网表中不得出现以 VP 开头的 SPICE 实例名。
            foreach (var line in netlist.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("*", StringComparison.Ordinal) || trimmed.StartsWith(".", StringComparison.Ordinal)) continue;
                if (trimmed.StartsWith("VP", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Voltage probe must not emit a VP element line in the netlist: " + trimmed);
            }
            // 探针不得注入电流，网表不得为其输出 print @VP[id] / print i(VP...) 之类的支路请求。
            if (netlist.Contains("print @VP") || netlist.Contains("print i(VP")) throw new InvalidOperationException("Voltage probe must not request a branch current from ngspice.");
        }

        // 电压探针不得接受任何参数写入（含伪造 SiValue）。
        private static void ExpectVoltageProbeNotParameterEditable()
        {
            if (SpiceParameterUnits.UnitsFor(SpiceComponentKind.VoltageProbe).Length != 0) throw new InvalidOperationException("Voltage probe must expose an empty parameter unit array.");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.VoltageProbe, 10d)) throw new InvalidOperationException("Voltage probe must not accept parameter writes (10 V伪造值).");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.VoltageProbe, 1d)) throw new InvalidOperationException("Voltage probe must not accept any positive parameter write.");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.VoltageProbe, 0d)) throw new InvalidOperationException("Voltage probe must not accept zero parameter write.");
        }

        private static void ExpectVoltageProbeWireOrderDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.VoltageProbeAcrossSource();
            var reordered = SpiceT2Fixtures.VoltageProbeAcrossSource();
            var wires = reordered.Wires.AsEnumerable().Reverse().ToList();
            reordered.Wires.Clear();
            reordered.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reordered, "Voltage probe wire order");
        }

        private static void ExpectVoltageProbeWireDirectionDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.VoltageProbeAcrossSource();
            var reversedEndpoints = SpiceT2Fixtures.VoltageProbeAcrossSource();
            var wires = reversedEndpoints.Wires.Select(wire => new SpiceWireModel(wire.End, wire.Start)).ToList();
            reversedEndpoints.Wires.Clear();
            reversedEndpoints.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reversedEndpoints, "Voltage probe wire direction");
        }

        // 元件创建顺序变化不得改变图/网表语义（图构建器按 Kind+InstanceId 排序分配 SPICE 名称）。
        private static void ExpectVoltageProbeComponentOrderDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.VoltageProbeAcrossSource();
            var reordered = new SpiceCircuitModel();
            // 故意把探针放到最前面，电源/电阻/地放到最后。
            reordered.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            reordered.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            reordered.Components.Add(SpiceComponentModel.Ground("ground"));
            reordered.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            SpiceT2Fixtures.Wire(reordered, "source", "positive", "r1", "positive");
            SpiceT2Fixtures.Wire(reordered, "source", "negative", "ground", "ground");
            SpiceT2Fixtures.Wire(reordered, "r1", "negative", "ground", "ground");
            SpiceT2Fixtures.Wire(reordered, "vprobe-1", "positive", "source", "positive");
            SpiceT2Fixtures.Wire(reordered, "vprobe-1", "negative", "ground", "ground");
            ExpectEquivalentGraphAndNetlist(first, reordered, "Voltage probe component order");
        }

        // 探针两端接同一节点是合法的 0 V 测量场景，不得触发 SPICE_COMPONENT_SHORTED 警告。
        private static void ExpectVoltageProbeSameNodeNoShortDiagnostic()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.VoltageProbeSameNode());
            if (!graph.IsValid) throw new InvalidOperationException("Voltage probe across the same node should produce a valid graph.");
            if (graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_COMPONENT_SHORTED" && diagnostic.ComponentId == "vprobe-1"))
            {
                throw new InvalidOperationException("Voltage probe across the same node must not raise a short diagnostic.");
            }
        }

        // 探针不得把浮空子回路桥接到地：浮空子回路仍须被 SPICE_FLOATING_SUBCIRCUIT 拦截。
        private static void ExpectVoltageProbeDoesNotBridgeFloatingSubcircuit()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.VoltageProbeOnFloatingSubcircuit());
            if (graph.IsValid) throw new InvalidOperationException("Voltage probe must not bridge a floating subcircuit to ground.");
            if (!graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT"))
            {
                throw new InvalidOperationException("Floating subcircuit diagnostic must remain when a probe is attached.");
            }
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyVoltageProbeAcrossSource(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.VoltageProbeAcrossSource()).ConfigureAwait(false);
            ExpectSuccess(result);
            var probe = result.ComponentResults["vprobe-1"];
            if (probe == null) throw new InvalidOperationException("Voltage probe result is missing the vprobe-1 entry.");
            ExpectNear(probe.Voltage, 10d, VoltageTolerance, "voltage probe differential voltage (+10 V)");
            ExpectNear(probe.Current, 0d, CurrentTolerance, "voltage probe current must be zero");
            if (probe.VoltageDirection != "V-plus-to-V-minus") throw new InvalidOperationException("Voltage probe direction must read V-plus-to-V-minus.");
            if (!result.GeneratedNetlistContent.Contains("print v(")) throw new InvalidOperationException("Voltage probe fixture must still rely on ngspice node voltage prints.");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyReversedVoltageProbe(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ReversedVoltageProbeAcrossSource()).ConfigureAwait(false);
            ExpectSuccess(result);
            var probe = result.ComponentResults["vprobe-1"];
            if (probe == null) throw new InvalidOperationException("Reversed voltage probe result is missing the vprobe-1 entry.");
            ExpectNear(probe.Voltage, -10d, VoltageTolerance, "reversed voltage probe differential voltage (-10 V)");
            ExpectNear(probe.Current, 0d, CurrentTolerance, "reversed voltage probe current must be zero");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyVoltageProbeSameNode(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.VoltageProbeSameNode()).ConfigureAwait(false);
            ExpectSuccess(result);
            var probe = result.ComponentResults["vprobe-1"];
            if (probe == null) throw new InvalidOperationException("Same-node voltage probe result is missing the vprobe-1 entry.");
            ExpectNear(probe.Voltage, 0d, VoltageTolerance, "same-node voltage probe differential voltage (0 V)");
            return result;
        }

        // 非侵入性：加入探针后电阻电流和节点电压必须保持不变，且网表与无探针基线字节一致。
        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyVoltageProbeNonInvasive(SpiceDcSimulationService service)
        {
            var baseline = await service.SimulateAsync(SpiceT2Fixtures.SingleResistor(1000d)).ConfigureAwait(false);
            ExpectSuccess(baseline);
            var baselineCurrent = baseline.ComponentResults["r1"].Current;
            var baselineNodeVoltage = GetOnlyPositiveNode(baseline);

            var withProbe = await service.SimulateAsync(SpiceT2Fixtures.VoltageProbeAcrossSource()).ConfigureAwait(false);
            ExpectSuccess(withProbe);
            ExpectNear(withProbe.ComponentResults["r1"].Current, baselineCurrent, CurrentTolerance, "resistor current must not change when probe is added");
            ExpectNear(GetOnlyPositiveNode(withProbe), baselineNodeVoltage, VoltageTolerance, "node voltage must not change when probe is added");
            ExpectNear(withProbe.ComponentResults["source"].Current, baseline.ComponentResults["source"].Current, CurrentTolerance, "source current must not change when probe is added");

            // 网表语义稳定：探针不得新增元件行或支路请求，与基线网表字节一致。
            if (baseline.GeneratedNetlistContent != withProbe.GeneratedNetlistContent)
            {
                throw new InvalidOperationException("Voltage probe must not alter the generated netlist content.");
            }
            return withProbe;
        }

        // 双探针：两只探针同时跨接在同一电源两端，结果互不冲突且实例名稳定。
        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyTwoVoltageProbes(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.TwoVoltageProbesAcrossSource()).ConfigureAwait(false);
            ExpectSuccess(result);
            var probe1 = result.ComponentResults["vprobe-1"];
            var probe2 = result.ComponentResults["vprobe-2"];
            if (probe1 == null || probe2 == null) throw new InvalidOperationException("Both voltage probe results must be present.");
            ExpectNear(probe1.Voltage, 10d, VoltageTolerance, "first probe differential voltage");
            ExpectNear(probe2.Voltage, 10d, VoltageTolerance, "second probe differential voltage");
            // 实例名稳定：探针不分配 SPICE 名称，结果键仍为用户 instanceId。
            if (!result.ComponentResults.ContainsKey("vprobe-1") || !result.ComponentResults.ContainsKey("vprobe-2"))
            {
                throw new InvalidOperationException("Voltage probe result keys must remain stable instance ids.");
            }
            return result;
        }

        private static async System.Threading.Tasks.Task VerifyVoltageProbeOnlyPositiveConnectedAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.VoltageProbeOnlyPositiveConnected();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_TERMINAL" && diagnostic.ComponentId == "vprobe-1"))
            {
                throw new InvalidOperationException("Probe with only V+ connected must be rejected by SPICE_FLOATING_TERMINAL on the probe.");
            }
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null) throw new InvalidOperationException("Probe with only V+ connected reached ngspice instead of being rejected before execution.");
            if (result.ComponentResults.ContainsKey("vprobe-1")) throw new InvalidOperationException("Probe with only V+ connected must not produce a fabricated voltage result.");
        }

        private static async System.Threading.Tasks.Task VerifyVoltageProbeBothDisconnectedAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.VoltageProbeBothTerminalsDisconnected();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_TERMINAL" && diagnostic.ComponentId == "vprobe-1"))
            {
                throw new InvalidOperationException("Probe with both terminals disconnected must be rejected by SPICE_FLOATING_TERMINAL on the probe.");
            }
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null) throw new InvalidOperationException("Probe with both terminals disconnected reached ngspice instead of being rejected before execution.");
            if (result.ComponentResults.ContainsKey("vprobe-1")) throw new InvalidOperationException("Probe with both terminals disconnected must not produce a fabricated voltage result.");
        }

        private static async System.Threading.Tasks.Task VerifyVoltageProbeOnFloatingSubcircuitAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.VoltageProbeOnFloatingSubcircuit();
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null) throw new InvalidOperationException("Probe attached to a floating subcircuit reached ngspice instead of being rejected before execution.");
            if (!result.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_SUBCIRCUIT"))
            {
                throw new InvalidOperationException("Floating subcircuit diagnostic must remain when a probe is attached.");
            }
            if (result.ComponentResults.ContainsKey("vprobe-1")) throw new InvalidOperationException("Probe on a floating subcircuit must not produce a fabricated voltage result.");
        }

        private static async System.Threading.Tasks.Task VerifyVoltageProbeWithoutGroundAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.VoltageProbeWithoutGround();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_GROUND_MISSING"))
            {
                throw new InvalidOperationException("Probe fixture without GND must be rejected by SPICE_GROUND_MISSING.");
            }
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null) throw new InvalidOperationException("Probe fixture without GND reached ngspice instead of being rejected before execution.");
            if (result.ComponentResults.ContainsKey("vprobe-1")) throw new InvalidOperationException("Probe without GND must not produce a fabricated voltage result.");
        }

        private static void ExpectCurrentProbeNetlistStable()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.CurrentProbeSeries());
            if (!graph.IsValid) throw new InvalidOperationException("Current probe series fixture should produce a valid graph.");
            if (graph.SpiceNameByComponentId["iprobe-1"] != "VPROBE1") throw new InvalidOperationException("Current probe must use a stable VPROBE1 branch name.");
            var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(SpiceT2Fixtures.CurrentProbeSeries(), graph).Content;
            if (!netlist.Split('\n').Any(line => line.Trim().StartsWith("VPROBE1 ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(" 0", StringComparison.Ordinal)) || !netlist.Contains("print i(VPROBE1)"))
            {
                throw new InvalidOperationException("Current probe must emit a 0 V VPROBE branch and request its current.");
            }
        }

        private static void ExpectCurrentProbeWireOrderDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.CurrentProbeSeries();
            var reordered = SpiceT2Fixtures.CurrentProbeSeries();
            var wires = reordered.Wires.AsEnumerable().Reverse().ToList();
            reordered.Wires.Clear();
            reordered.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reordered, "Current probe wire order");
        }

        private static void ExpectCurrentProbeWireDirectionDoesNotAffectNetlist()
        {
            var first = SpiceT2Fixtures.CurrentProbeSeries();
            var reversedEndpoints = SpiceT2Fixtures.CurrentProbeSeries();
            var wires = reversedEndpoints.Wires.Select(wire => new SpiceWireModel(wire.End, wire.Start)).ToList();
            reversedEndpoints.Wires.Clear();
            reversedEndpoints.Wires.AddRange(wires);
            ExpectEquivalentGraphAndNetlist(first, reversedEndpoints, "Current probe wire direction");
        }

        private static void ExpectCurrentProbeNotParameterEditable()
        {
            if (SpiceParameterUnits.UnitsFor(SpiceComponentKind.CurrentProbe).Length != 0) throw new InvalidOperationException("Current probe must expose no editable parameter units.");
            if (SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.CurrentProbe, 0d) || SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.CurrentProbe, 1d))
            {
                throw new InvalidOperationException("Current probe must reject parameter writes.");
            }
        }

        private static void ExpectCurrentProbeConstraintConflict()
        {
            var graph = SpiceCircuitGraphBuilder.Build(SpiceT2Fixtures.CurrentProbeParallelSource());
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_CURRENT_PROBE_CONSTRAINT_CONFLICT" && diagnostic.ComponentId == "iprobe-1"))
            {
                throw new InvalidOperationException("Current probe parallel to an ideal voltage source must be rejected before ngspice.");
            }
        }

        private static void ExpectDcLibraryV1NetlistContract()
        {
            var circuit = SpiceT2Fixtures.DcLibraryV1SeriesChain();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (!graph.IsValid) throw new InvalidOperationException("DC library V1 series-chain fixture should produce a valid graph.");
            if (graph.SpiceNameByComponentId.ContainsKey("vprobe-1")) throw new InvalidOperationException("Voltage probe must remain non-invasive in the DC library V1 fixture.");

            var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph).Content;
            if (!netlist.Contains("RSW") || !netlist.Contains("VPROBE") || !netlist.Contains("D1 ") || CountModelDirectives(netlist) != 1)
            {
                throw new InvalidOperationException("DC library V1 netlist must contain the switch, current probe, diode, and exactly one diode model directive.");
            }
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyCurrentProbeSeries(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.CurrentProbeSeries()).ConfigureAwait(false);
            ExpectSuccess(result);
            var probe = result.ComponentResults["iprobe-1"];
            if (probe == null) throw new InvalidOperationException("Current probe result is missing.");
            ExpectNear(probe.Current, 0.01d, CurrentTolerance, "current probe series current");
            if (probe.CurrentDirection != "IN-to-OUT" || probe.VoltageDirection != "IN-to-OUT") throw new InvalidOperationException("Current probe direction must be IN-to-OUT.");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyReversedCurrentProbe(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.ReversedCurrentProbeSeries()).ConfigureAwait(false);
            ExpectSuccess(result);
            ExpectNear(result.ComponentResults["iprobe-1"].Current, -0.01d, CurrentTolerance, "reversed current probe current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyTwoCurrentProbes(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.TwoCurrentProbesSeries()).ConfigureAwait(false);
            ExpectSuccess(result);
            ExpectNear(result.ComponentResults["iprobe-1"].Current, 0.01d, CurrentTolerance, "first current probe current");
            ExpectNear(result.ComponentResults["iprobe-2"].Current, 0.01d, CurrentTolerance, "second current probe current");
            return result;
        }

        private static async System.Threading.Tasks.Task<SpiceSimulationResult> VerifyDcLibraryV1SeriesChain(SpiceDcSimulationService service)
        {
            var result = await service.SimulateAsync(SpiceT2Fixtures.DcLibraryV1SeriesChain()).ConfigureAwait(false);
            ExpectSuccess(result);

            var currentProbe = result.ComponentResults["iprobe-1"];
            var r1 = result.ComponentResults["r1"];
            var r2 = result.ComponentResults["r2"];
            var diode = result.ComponentResults["d1"];
            var voltageProbe = result.ComponentResults["vprobe-1"];
            if (currentProbe.Current <= DiodeCurrentTolerance) throw new InvalidOperationException("DC library V1 current probe must report positive series current.");
            ExpectNear(r1.Current, currentProbe.Current, DiodeCurrentTolerance, "DC library V1 r1/probe current");
            ExpectNear(r2.Current, currentProbe.Current, DiodeCurrentTolerance, "DC library V1 r2/probe current");
            ExpectNear(diode.Current, currentProbe.Current, DiodeCurrentTolerance, "DC library V1 diode/probe current");
            ExpectNear(voltageProbe.Voltage, r2.Voltage, VoltageTolerance, "DC library V1 voltage probe differential voltage");
            if (!diode.Notes.Contains("状态：正向导通")) throw new InvalidOperationException("DC library V1 diode must report forward conduction.");
            if (currentProbe.CurrentDirection != "IN-to-OUT" || voltageProbe.VoltageDirection != "V-plus-to-V-minus")
            {
                throw new InvalidOperationException("DC library V1 probe direction labels are inconsistent.");
            }

            return result;
        }

        private static async System.Threading.Tasks.Task VerifyCurrentProbeOnlyInConnectedAsync(SpiceDcSimulationService service)
        {
            var circuit = SpiceT2Fixtures.CurrentProbeOnlyInConnected();
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            if (graph.IsValid || !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_FLOATING_TERMINAL" && diagnostic.ComponentId == "iprobe-1"))
            {
                throw new InvalidOperationException("Current probe with only IN connected must be rejected by SPICE_FLOATING_TERMINAL.");
            }
            var result = await service.SimulateAsync(circuit).ConfigureAwait(false);
            if (result.Success || result.RawNgspiceResult != null || result.ComponentResults.ContainsKey("iprobe-1"))
            {
                throw new InvalidOperationException("Incomplete current probe must be rejected before ngspice and produce no fabricated measurement.");
            }
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
