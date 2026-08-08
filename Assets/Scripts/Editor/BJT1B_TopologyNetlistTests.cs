using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.T2;
using ElectricalSim.Spice.Topology;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// BJT1B — 拓扑与网表测试。
    /// A 组：NPN/PNP Q 元件行、.model 指令、编号空间、端子 Union 隔离、浮空端子拒绝（纯 Core）。
    /// B 组：确定性（Wire 顺序 / 元件顺序不影响网表字节）。
    /// C 组：旧二极管网表回归。
    /// D 组：真实 ngspice fixture（直接调用 RunRawNetlistAsync，不经过 SpiceDcSimulationService）。
    /// </summary>
    public static class BJT1B_TopologyNetlistTests
    {
        private static int _passed;
        private static int _failed;

        [MenuItem("Tools/Tests/Run BJT1B Topology Netlist Tests")]
        public static void RunAllTests()
        {
            _passed = 0;
            _failed = 0;

            // ---------- A. 网表行生成（纯 Core） ----------

            // A1. NPN Q 行精确匹配
            Run("Npn_QLine_ExactFormat", () =>
            {
                var circuit = BuildNpnCommonEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "NPN common-emitter fixture must build a valid graph: " + DescribeDiagnostics(graph));
                var qName = graph.SpiceNameByComponentId["q1"];
                var expectedLine = qName + " " + Node(graph, "q1", SpiceComponentModel.CollectorTerminalId) + " " +
                    Node(graph, "q1", SpiceComponentModel.BaseTerminalId) + " " +
                    Node(graph, "q1", SpiceComponentModel.EmitterTerminalId) + " NPN_GENERIC";
                var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph).Content;
                CheckTrue(NetlistLines(netlist).Any(line => line == expectedLine),
                    "Netlist must contain the exact NPN line '" + expectedLine + "'.");
            });

            // A2. PNP Q 行精确匹配
            Run("Pnp_QLine_ExactFormat", () =>
            {
                var circuit = BuildPnpCommonEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "PNP fixture must build a valid graph: " + DescribeDiagnostics(graph));
                var qName = graph.SpiceNameByComponentId["q1"];
                var expectedLine = qName + " " + Node(graph, "q1", SpiceComponentModel.CollectorTerminalId) + " " +
                    Node(graph, "q1", SpiceComponentModel.BaseTerminalId) + " " +
                    Node(graph, "q1", SpiceComponentModel.EmitterTerminalId) + " PNP_GENERIC";
                var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph).Content;
                CheckTrue(NetlistLines(netlist).Any(line => line == expectedLine),
                    "Netlist must contain the exact PNP line '" + expectedLine + "'.");
            });

            // A3. NPN/PNP 同时存在：共用 Q 编号空间，NPN 得 Q1
            Run("NpnPnp_SharedNumbering_NpnFirst", () =>
            {
                var circuit = BuildNpnPnpPair();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "NPN+PNP fixture must build a valid graph: " + DescribeDiagnostics(graph));
                CheckEqual("Q1", graph.SpiceNameByComponentId["qnpn"], "NPN must receive Q1 (Kind-ordered numbering).");
                CheckEqual("Q2", graph.SpiceNameByComponentId["qpnp"], "PNP must receive Q2 in the shared Q namespace.");
                var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph).Content;
                var lines = NetlistLines(netlist);
                CheckTrue(lines.Any(line => line.StartsWith("Q1 ", StringComparison.Ordinal) && line.EndsWith(" NPN_GENERIC", StringComparison.Ordinal)),
                    "Netlist must contain a Q1 line ending with NPN_GENERIC.");
                CheckTrue(lines.Any(line => line.StartsWith("Q2 ", StringComparison.Ordinal) && line.EndsWith(" PNP_GENERIC", StringComparison.Ordinal)),
                    "Netlist must contain a Q2 line ending with PNP_GENERIC.");
                CheckEqual(2, lines.Count(line => line.StartsWith("Q", StringComparison.Ordinal) && line.Length > 1 && char.IsDigit(line[1])),
                    "Exactly two Q element lines must be emitted.");
            });

            // A4. .model 指令只输出一次
            Run("ModelDirectives_EmittedExactlyOnce", () =>
            {
                var twoNpn = BuildTwoNpn();
                var twoNpnGraph = SpiceCircuitGraphBuilder.Build(twoNpn);
                CheckTrue(twoNpnGraph.IsValid, "Two-NPN fixture must build a valid graph: " + DescribeDiagnostics(twoNpnGraph));
                var twoNpnNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(twoNpn, twoNpnGraph).Content;
                CheckEqual(1, CountLinesStarting(twoNpnNetlist, ".model NPN_GENERIC"), "Two NPNs must emit exactly one NPN_GENERIC .model directive.");

                var pair = BuildNpnPnpPair();
                var pairGraph = SpiceCircuitGraphBuilder.Build(pair);
                CheckTrue(pairGraph.IsValid, "NPN+PNP fixture must build a valid graph: " + DescribeDiagnostics(pairGraph));
                var pairNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(pair, pairGraph).Content;
                CheckEqual(1, CountLinesStarting(pairNetlist, ".model NPN_GENERIC"), "NPN+PNP must emit exactly one NPN_GENERIC .model directive.");
                CheckEqual(1, CountLinesStarting(pairNetlist, ".model PNP_GENERIC"), "NPN+PNP must emit exactly one PNP_GENERIC .model directive.");
            });

            // A5. 只有 NPN 时不输出 PNP 模型（反之亦然）
            Run("ModelDirectives_OnlyForPresentDeviceKinds", () =>
            {
                var npnOnly = BuildNpnCommonEmitter();
                var npnGraph = SpiceCircuitGraphBuilder.Build(npnOnly);
                CheckTrue(npnGraph.IsValid, "NPN-only fixture must build a valid graph.");
                var npnNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(npnOnly, npnGraph).Content;
                CheckEqual(1, CountLinesStarting(npnNetlist, ".model NPN_GENERIC"), "NPN-only circuit must emit the NPN_GENERIC model exactly once.");
                CheckEqual(0, CountLinesStarting(npnNetlist, ".model PNP_GENERIC"), "NPN-only circuit must not emit any PNP_GENERIC model.");

                var pnpOnly = BuildPnpCommonEmitter();
                var pnpGraph = SpiceCircuitGraphBuilder.Build(pnpOnly);
                CheckTrue(pnpGraph.IsValid, "PNP-only fixture must build a valid graph.");
                var pnpNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(pnpOnly, pnpGraph).Content;
                CheckEqual(1, CountLinesStarting(pnpNetlist, ".model PNP_GENERIC"), "PNP-only circuit must emit the PNP_GENERIC model exactly once.");
                CheckEqual(0, CountLinesStarting(pnpNetlist, ".model NPN_GENERIC"), "PNP-only circuit must not emit any NPN_GENERIC model.");
            });

            // A6. C/B/E 不被错误 Union：三个端子映射到三个不同节点
            Run("CollectorBaseEmitter_MapToThreeDistinctNodes", () =>
            {
                var circuit = BuildNpnCommonEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "NPN fixture must build a valid graph: " + DescribeDiagnostics(graph));
                var collector = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var baseNode = Node(graph, "q1", SpiceComponentModel.BaseTerminalId);
                var emitter = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                CheckFalse(collector == baseNode, "Collector and base must not be unioned into the same node.");
                CheckFalse(baseNode == emitter, "Base and emitter must not be unioned into the same node.");
                CheckFalse(collector == emitter, "Collector and emitter must not be unioned into the same node.");
            });

            // A7. 浮空 base 端子被拒绝
            Run("FloatingBase_RejectedWithDiagnostic", () =>
            {
                var circuit = BuildNpnFloatingBase();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckFalse(graph.IsValid, "A floating BJT base must invalidate the graph.");
                CheckTrue(graph.Diagnostics.Any(d => d.Code == "SPICE_FLOATING_TERMINAL" &&
                        string.Equals(d.ComponentId, "q1", StringComparison.Ordinal) &&
                        string.Equals(d.TerminalId, SpiceComponentModel.BaseTerminalId, StringComparison.Ordinal)),
                    "Expected a SPICE_FLOATING_TERMINAL error on q1:base. Got: " + DescribeDiagnostics(graph));
            });

            // A8. 浮空 collector 端子被拒绝
            Run("FloatingCollector_Rejected", () =>
            {
                var circuit = BuildNpnFloatingCollector();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckFalse(graph.IsValid, "A floating BJT collector must invalidate the graph.");
                CheckTrue(graph.Diagnostics.Any(d => d.Code == "SPICE_FLOATING_TERMINAL" &&
                        d.Severity == SpiceDiagnosticSeverity.Error &&
                        string.Equals(d.ComponentId, "q1", StringComparison.Ordinal) &&
                        string.Equals(d.TerminalId, SpiceComponentModel.CollectorTerminalId, StringComparison.Ordinal)),
                    "Expected a SPICE_FLOATING_TERMINAL error on q1:collector. Got: " + DescribeDiagnostics(graph));
            });

            // A9. 浮空 base 端子被拒绝（复用 BuildNpnFloatingBase fixture，与 A7 同一场景，断言补齐 Severity）
            Run("FloatingBase_Rejected", () =>
            {
                var circuit = BuildNpnFloatingBase();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckFalse(graph.IsValid, "A floating BJT base must invalidate the graph.");
                CheckTrue(graph.Diagnostics.Any(d => d.Code == "SPICE_FLOATING_TERMINAL" &&
                        d.Severity == SpiceDiagnosticSeverity.Error &&
                        string.Equals(d.ComponentId, "q1", StringComparison.Ordinal) &&
                        string.Equals(d.TerminalId, SpiceComponentModel.BaseTerminalId, StringComparison.Ordinal)),
                    "Expected a SPICE_FLOATING_TERMINAL error on q1:base. Got: " + DescribeDiagnostics(graph));
            });

            // A10. 浮空 emitter 端子被拒绝
            Run("FloatingEmitter_Rejected", () =>
            {
                var circuit = BuildNpnFloatingEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckFalse(graph.IsValid, "A floating BJT emitter must invalidate the graph.");
                CheckTrue(graph.Diagnostics.Any(d => d.Code == "SPICE_FLOATING_TERMINAL" &&
                        d.Severity == SpiceDiagnosticSeverity.Error &&
                        string.Equals(d.ComponentId, "q1", StringComparison.Ordinal) &&
                        string.Equals(d.TerminalId, SpiceComponentModel.EmitterTerminalId, StringComparison.Ordinal)),
                    "Expected a SPICE_FLOATING_TERMINAL error on q1:emitter. Got: " + DescribeDiagnostics(graph));
            });

            // A11. C↔B 外部 Wire 直连（二极管接法）合法：不触发同元件连接禁令，C/B 合并为同一节点
            Run("CollectorBase_DiodeConnected_Allowed", () =>
            {
                var circuit = BuildNpnWithExternalBjtWire(SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.BaseTerminalId);
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "Diode-connected C-B external wire must build a valid graph: " + DescribeDiagnostics(graph));
                CheckFalse(graph.Diagnostics.Any(d => d.Code == "SPICE_SAME_COMPONENT_CONNECTION"),
                    "C-B external wire must not raise SPICE_SAME_COMPONENT_CONNECTION. Got: " + DescribeDiagnostics(graph));
                CheckFalse(graph.Diagnostics.Any(d => d.Severity == SpiceDiagnosticSeverity.Error),
                    "C-B diode connection must not produce any Error diagnostics. Got: " + DescribeDiagnostics(graph));
                var collector = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var baseNode = Node(graph, "q1", SpiceComponentModel.BaseTerminalId);
                var emitter = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                CheckEqual(collector, baseNode, "Collector and base must be unioned into the same node by the external wire.");
                CheckFalse(collector == emitter, "Emitter must remain on a different node (no internal short).");
            });

            // A12. B↔E 外部 Wire 直连合法：B/E 合并为同一节点，collector 保持独立
            Run("BaseEmitter_ExternalWire_Allowed", () =>
            {
                var circuit = BuildNpnWithExternalBjtWire(SpiceComponentModel.BaseTerminalId, SpiceComponentModel.EmitterTerminalId);
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "B-E external wire must build a valid graph: " + DescribeDiagnostics(graph));
                CheckFalse(graph.Diagnostics.Any(d => d.Code == "SPICE_SAME_COMPONENT_CONNECTION"),
                    "B-E external wire must not raise SPICE_SAME_COMPONENT_CONNECTION. Got: " + DescribeDiagnostics(graph));
                CheckFalse(graph.Diagnostics.Any(d => d.Severity == SpiceDiagnosticSeverity.Error),
                    "B-E external wire must not produce any Error diagnostics. Got: " + DescribeDiagnostics(graph));
                var collector = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var baseNode = Node(graph, "q1", SpiceComponentModel.BaseTerminalId);
                var emitter = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                CheckEqual(baseNode, emitter, "Base and emitter must be unioned into the same node by the external wire.");
                CheckFalse(collector == baseNode, "Collector must remain on a different node (no internal short).");
            });

            // A13. C↔E 外部 Wire 直连：图仍有效，仅产生 SPICE_COMPONENT_SHORTED Warning
            Run("CollectorEmitter_ExternalWire_ProducesWarning", () =>
            {
                var circuit = BuildNpnWithExternalBjtWire(SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.EmitterTerminalId);
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "A Warning must not invalidate the graph: " + DescribeDiagnostics(graph));
                CheckTrue(graph.Diagnostics.Any(d => d.Code == "SPICE_COMPONENT_SHORTED" &&
                        d.Severity == SpiceDiagnosticSeverity.Warning &&
                        string.Equals(d.ComponentId, "q1", StringComparison.Ordinal)),
                    "Expected a SPICE_COMPONENT_SHORTED warning for q1 (C/E on the same node). Got: " + DescribeDiagnostics(graph));
                CheckFalse(graph.Diagnostics.Any(d => d.Severity == SpiceDiagnosticSeverity.Error),
                    "C-E external wire must not produce any Error diagnostics. Got: " + DescribeDiagnostics(graph));
                var collector = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var emitter = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                CheckEqual(collector, emitter, "Collector and emitter must be unioned into the same node by the external wire.");
            });

            // ---------- B. 确定性 ----------

            // B8. Wire 顺序反转，网表字节级不变
            Run("WireOrderReversal_NetlistUnchanged", () =>
            {
                var first = BuildNpnCommonEmitter();
                var reordered = BuildNpnCommonEmitter();
                var wires = reordered.Wires.AsEnumerable().Reverse().ToList();
                reordered.Wires.Clear();
                reordered.Wires.AddRange(wires);
                ExpectEquivalentNetlists(first, reordered, "BJT wire order reversal");
            });

            // B9. 元件添加顺序重排，网表字节级不变
            Run("ComponentOrderReordering_NetlistUnchanged", () =>
            {
                var first = BuildNpnPnpPair();
                var reordered = BuildNpnPnpPair();
                var components = reordered.Components.AsEnumerable().Reverse().ToList();
                reordered.Components.Clear();
                reordered.Components.AddRange(components);
                ExpectEquivalentNetlists(first, reordered, "BJT component order reordering");
            });

            // ---------- C. 旧 DC Netlist 回归 ----------

            // C10. 二极管网表与 BJT-1B 修改前一致
            Run("DiodeNetlist_UnchangedByBjtSupport", () =>
            {
                var circuit = SpiceT2Fixtures.ForwardDiode();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "Forward diode fixture must build a valid graph.");
                CheckEqual("D1", graph.SpiceNameByComponentId["d1"], "Diode SPICE element name must remain the stable D1.");
                var netlist = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph).Content;
                var expectedLine = "D1 " + Node(graph, "d1", SpiceComponentModel.PositiveTerminalId) + " " +
                    Node(graph, "d1", SpiceComponentModel.NegativeTerminalId) + " D_GENERIC";
                CheckTrue(NetlistLines(netlist).Any(line => line == expectedLine),
                    "Netlist must contain the exact diode line '" + expectedLine + "'.");
                CheckEqual(1, CountLinesStarting(netlist, ".model D_GENERIC"), "D_GENERIC .model directive must be emitted exactly once.");
                CheckTrue(netlist.Contains(".model D_GENERIC D(IS=2.52e-9 N=1.752 RS=0.568)"),
                    "The D_GENERIC model parameters must remain exactly as before BJT-1B.");
                CheckTrue(netlist.Contains("print @D1[id]"), "Diode current print request must remain intact.");
            });

            // ---------- D. 真实 ngspice fixture ----------

            // D12. NPN 共射放大（active 区）
            Run("Ngspice_NpnCommonEmitter_OperatingPoint", () =>
            {
                var circuit = BuildNpnCommonEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "NPN ngspice fixture must build a valid graph: " + DescribeDiagnostics(graph));
                var document = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph);
                var runner = new NgspiceProcessRunner();
                var result = runner.RunRawNetlistAsync(document.Content, TimeSpan.FromSeconds(30), "BJT1B").GetAwaiter().GetResult();
                var logPath = WriteRawLog("NpnCommonEmitter", document.Content, result);
                Debug.Log("[BJT1B] NPN ngspice raw log: " + logPath);
                CheckTrue(result.Success && result.ExitCode == 0,
                    "ngspice run failed: code=" + result.ExitCode + " failure=" + result.FailureCode + " " + result.FailureMessage);

                var collectorNode = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var baseNode = Node(graph, "q1", SpiceComponentModel.BaseTerminalId);
                var emitterNode = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                var vCollector = ExtractVoltage(result.StandardOutput, collectorNode);
                var vBase = ExtractVoltage(result.StandardOutput, baseNode);
                // emitter 直接接 GND（SPICE 节点 0），网表不会 print v(0)，其电压按定义为 0。
                var vEmitter = emitterNode == "0" ? 0d : ExtractVoltage(result.StandardOutput, emitterNode);
                Debug.Log($"[BJT1B] NPN fixture: v({collectorNode})={vCollector:0.######} V, v({baseNode})={vBase:0.######} V, v({emitterNode})={vEmitter:0.######} V");
                // 手推：IB≈(5-0.65)/200k≈21.75µA，IC≈β·IB≈2.175mA，VCE≈5-2.175mA·1k≈2.8V（宽松容差覆盖模型差异）。
                CheckTrue(vCollector >= 2.0d && vCollector <= 3.5d,
                    "Collector voltage must be within 2.0-3.5 V (active region), got " + vCollector + " V.");
                CheckTrue(vBase > 0.3d && vBase < 1.0d, "Forward-biased base-emitter voltage must be roughly 0.3-1.0 V, got " + vBase + " V.");
                CheckTrue(Math.Abs(vEmitter) < 1e-6d, "Emitter is grounded and must read 0 V, got " + vEmitter + " V.");
            });

            // D13. PNP 镜像电路（emitter 接 +5V，base/collector 经电阻到 GND）
            Run("Ngspice_PnpMirror_OperatingPoint", () =>
            {
                var circuit = BuildPnpCommonEmitter();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "PNP ngspice fixture must build a valid graph: " + DescribeDiagnostics(graph));
                var document = SpiceNetlistBuilder.BuildDcOperatingPoint(circuit, graph);
                var runner = new NgspiceProcessRunner();
                var result = runner.RunRawNetlistAsync(document.Content, TimeSpan.FromSeconds(30), "BJT1B").GetAwaiter().GetResult();
                var logPath = WriteRawLog("PnpMirror", document.Content, result);
                Debug.Log("[BJT1B] PNP ngspice raw log: " + logPath);
                CheckTrue(result.Success && result.ExitCode == 0,
                    "ngspice run failed: code=" + result.ExitCode + " failure=" + result.FailureCode + " " + result.FailureMessage);

                var collectorNode = Node(graph, "q1", SpiceComponentModel.CollectorTerminalId);
                var baseNode = Node(graph, "q1", SpiceComponentModel.BaseTerminalId);
                var emitterNode = Node(graph, "q1", SpiceComponentModel.EmitterTerminalId);
                var vCollector = ExtractVoltage(result.StandardOutput, collectorNode);
                var vBase = ExtractVoltage(result.StandardOutput, baseNode);
                var vEmitter = ExtractVoltage(result.StandardOutput, emitterNode);
                Debug.Log($"[BJT1B] PNP fixture: v({collectorNode})={vCollector:0.######} V, v({baseNode})={vBase:0.######} V, v({emitterNode})={vEmitter:0.######} V");
                // 手推：IB≈(5-0.65)/200k≈21.75µA（流出基极），IC≈β·IB≈2.175mA，V(collector)=IC·1k≈2.175V。
                CheckTrue(vCollector >= 1.2d && vCollector <= 3.5d,
                    "PNP collector voltage must sit at a positive mid-rail value (1.2-3.5 V), got " + vCollector + " V.");
                CheckTrue(vBase > 3.5d && vBase < 4.8d,
                    "PNP base must sit about one VEB below the 5 V emitter (3.5-4.8 V), got " + vBase + " V.");
                CheckTrue(Math.Abs(vEmitter - 5d) < 1e-6d, "PNP emitter must read the 5 V supply, got " + vEmitter + " V.");
                CheckTrue(vEmitter > vBase && vBase > vCollector,
                    "PNP active-region ordering must hold: V(emitter) > V(base) > V(collector).");
            });

            var total = _passed + _failed;
            Debug.Log($"BJT1B Topology Netlist Tests: {_passed}/{total} passed.");
            if (_failed > 0)
            {
                throw new InvalidOperationException($"BJT1B Topology Netlist Tests: {_failed} of {total} FAILED.");
            }
        }

        // ---------- Fixture 构造 ----------

        // NPN 共射放大（active 区）：VCC=5V；RC=1k 接 VCC→collector；RB=200k 接 VCC→base；emitter→GND。
        private static SpiceCircuitModel BuildNpnCommonEmitter()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vcc", "positive", "rb", "positive");
            Wire(circuit, "rb", "negative", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // PNP：emitter 接 +5V；base 经 RB=200k 到 GND；collector 经 RC=1k 到 GND。
        private static SpiceCircuitModel BuildPnpCommonEmitter()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericPnpBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "q1", SpiceComponentModel.EmitterTerminalId);
            Wire(circuit, "q1", SpiceComponentModel.BaseTerminalId, "rb", "positive");
            Wire(circuit, "rb", "negative", "gnd", "ground");
            Wire(circuit, "q1", SpiceComponentModel.CollectorTerminalId, "rc", "positive");
            Wire(circuit, "rc", "negative", "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // NPN + PNP 各一只，共用同一 VCC 与 GND。
        private static SpiceCircuitModel BuildNpnPnpPair()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb1", 200000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc2", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb2", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("qnpn"));
            circuit.Components.Add(SpiceComponentModel.GenericPnpBjt("qpnp"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            // NPN 侧
            Wire(circuit, "vcc", "positive", "rc1", "positive");
            Wire(circuit, "rc1", "negative", "qnpn", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vcc", "positive", "rb1", "positive");
            Wire(circuit, "rb1", "negative", "qnpn", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "qnpn", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            // PNP 侧
            Wire(circuit, "vcc", "positive", "qpnp", SpiceComponentModel.EmitterTerminalId);
            Wire(circuit, "qpnp", SpiceComponentModel.BaseTerminalId, "rb2", "positive");
            Wire(circuit, "rb2", "negative", "gnd", "ground");
            Wire(circuit, "qpnp", SpiceComponentModel.CollectorTerminalId, "rc2", "positive");
            Wire(circuit, "rc2", "negative", "gnd", "ground");
            // 电源
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // 两只 NPN 共用偏置结构，用于 .model 单次输出校验。
        private static SpiceCircuitModel BuildTwoNpn()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb1", 200000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc2", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb2", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q2"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc1", "positive");
            Wire(circuit, "rc1", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vcc", "positive", "rb1", "positive");
            Wire(circuit, "rb1", "negative", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "positive", "rc2", "positive");
            Wire(circuit, "rc2", "negative", "q2", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vcc", "positive", "rb2", "positive");
            Wire(circuit, "rb2", "negative", "q2", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "q2", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // NPN 的 base 不接任何 Wire：应产生 SPICE_FLOATING_TERMINAL。
        private static SpiceCircuitModel BuildNpnFloatingBase()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // NPN 的 collector 不接任何 Wire：应产生 SPICE_FLOATING_TERMINAL（base/emitter 正常接线且接地可达）。
        private static SpiceCircuitModel BuildNpnFloatingCollector()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rb", "positive");
            Wire(circuit, "rb", "negative", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // NPN 的 emitter 不接任何 Wire：应产生 SPICE_FLOATING_TERMINAL（collector/base 正常接线且电源接地可达）。
        private static SpiceCircuitModel BuildNpnFloatingEmitter()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb", 200000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vcc", "positive", "rb", "positive");
            Wire(circuit, "rb", "negative", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // 在完整 NPN 共射 fixture 基础上，额外用一条外部 Wire 直连 q1 的两个 BJT 端子（C↔B / B↔E / C↔E）。
        private static SpiceCircuitModel BuildNpnWithExternalBjtWire(string terminalA, string terminalB)
        {
            var circuit = BuildNpnCommonEmitter();
            Wire(circuit, "q1", terminalA, "q1", terminalB);
            return circuit;
        }

        private static void Wire(SpiceCircuitModel circuit, string startComponent, string startTerminal, string endComponent, string endTerminal)
        {
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(startComponent, startTerminal), new SpiceTerminalRef(endComponent, endTerminal)));
        }

        // ---------- 断言辅助 ----------

        private static void ExpectEquivalentNetlists(SpiceCircuitModel firstCircuit, SpiceCircuitModel secondCircuit, string difference)
        {
            var first = SpiceCircuitGraphBuilder.Build(firstCircuit);
            var second = SpiceCircuitGraphBuilder.Build(secondCircuit);
            if (!first.IsValid || !second.IsValid) throw new InvalidOperationException(difference + ": regression fixture is invalid.");
            var firstNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(firstCircuit, first).Content;
            var secondNetlist = SpiceNetlistBuilder.BuildDcOperatingPoint(secondCircuit, second).Content;
            if (!string.Equals(firstNetlist, secondNetlist, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(difference + " changed deterministic netlist output (byte-level comparison failed).");
            }
        }

        private static string Node(SpiceCircuitGraph graph, string componentId, string terminalId)
        {
            return graph.NodeByTerminal[new SpiceTerminalRef(componentId, terminalId)];
        }

        private static IEnumerable<string> NetlistLines(string netlist)
        {
            return netlist.Split('\n').Select(line => line.TrimEnd('\r'));
        }

        private static int CountLinesStarting(string netlist, string prefix)
        {
            return NetlistLines(netlist).Count(line => line.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        }

        private static string DescribeDiagnostics(SpiceCircuitGraph graph)
        {
            return string.Join(" | ", graph.Diagnostics.Select(d => d.Code + ":" + d.Message));
        }

        // 直接从 ngspice 原始 stdout 用正则提取 v(node) 值，不依赖 SpiceDcOutputParser。
        private static double ExtractVoltage(string standardOutput, string node)
        {
            var pattern = @"^\s*v\s*\(\s*" + Regex.Escape(node) + @"\s*\)\s*=\s*(?<value>[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?)\s*$";
            var match = Regex.Matches(standardOutput ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline)
                .Cast<Match>().LastOrDefault();
            if (match == null)
            {
                throw new InvalidOperationException("ngspice stdout did not contain a printed value for v(" + node + ").");
            }
            var numericText = match.Groups["value"].Value.Replace('D', 'E').Replace('d', 'e');
            if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new InvalidOperationException("Unable to parse ngspice value '" + match.Groups["value"].Value + "' for v(" + node + ").");
            }
            return value;
        }

        private static string WriteRawLog(string fixtureName, string netlist, NgspiceRunResult result)
        {
            var logsDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(logsDirectory);
            var logPath = Path.Combine(logsDirectory, "BJT1B_Ngspice_" + fixtureName + ".log");
            var content =
                "== BJT1B ngspice fixture: " + fixtureName + " ==" + Environment.NewLine +
                "success=" + result.Success + Environment.NewLine +
                "exitCode=" + result.ExitCode + Environment.NewLine +
                "timedOut=" + result.TimedOut + Environment.NewLine +
                "failureCode=" + result.FailureCode + Environment.NewLine +
                "failureMessage=" + (result.FailureMessage ?? string.Empty) + Environment.NewLine +
                Environment.NewLine +
                "== INPUT NETLIST ==" + Environment.NewLine +
                netlist + Environment.NewLine +
                "== STDOUT ==" + Environment.NewLine +
                (result.StandardOutput ?? string.Empty) + Environment.NewLine +
                "== STDERR ==" + Environment.NewLine +
                (result.StandardError ?? string.Empty) + Environment.NewLine;
            File.WriteAllText(logPath, content);
            return logPath;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Debug.Log($"[BJT1B] PASS: {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Debug.LogError($"[BJT1B] FAIL: {name} — {ex.Message}");
            }
        }

        private static void CheckTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFalse(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException(message);
        }

        private static void CheckEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}
