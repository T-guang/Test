using System;
using System.Collections.Generic;
using System.IO;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Netlist;
using ElectricalSim.Spice.Results;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// BJT1C — DC 解析与闭环结果测试。
    /// 全部闭环用例经 SpiceDcSimulationService.SimulateAsync(circuit) 完整运行（图校验→网表→ngspice→解析→元件结果），
    /// 不直接调用 NgspiceProcessRunner；每个闭环用例把原始 ngspice StandardOutput 保存到 Logs\。
    ///
    /// 方向约定（实测 ngspice-45.2 确认）：
    ///   Voltage = V(C) - V(E)（恒为 C-to-E）；
    ///   Current = IC = ngspice @q[ic]，符号约定为“流入 collector 端子为正”；
    ///   NPN 放大区 IC > 0；PNP 放大区 IC < 0；ic+ib+ie≈0（KCL）。
    ///
    /// A 组：NPN/PNP active 闭环数值 + 方向约定文档化。
    /// B 组：cutoff / active / saturation 趋势（改基极偏置，三点真实收敛）。
    /// C 组：解析失败为明确状态（不伪造 0）。
    /// D 组：旧回归不在本文件重复，由 SpiceT2Validation.RunPureCoreChecks / SpiceT2ValidationTools.RunEditorValidation 覆盖。
    /// </summary>
    public static class BJT1C_DcParserResultTests
    {
        // 实测基准（ngspice-45.2，见 Logs\BJT1C_Probe_Npn.log / Logs\BJT1C_Probe_Pnp.log）
        private const double NpnExpectedIc = 2.162520e-03;
        private const double NpnExpectedVce = 2.837533;          // V(C)-V(E)，emitter 接地
        private const double PnpExpectedIc = -2.17770e-03;
        private const double PnpExpectedVce = 2.162377 - 5.0;    // V(C)-V(E) = 2.162377 - 5.0 = -2.837623
        private const double RelativeTolerance = 1e-3;

        private static int _passed;
        private static int _failed;
        private static SpiceSimulationResult _cachedNpnActive;

        [MenuItem("Tools/Tests/Run BJT1C DC Parser Result Tests")]
        public static void RunAllTests()
        {
            _passed = 0;
            _failed = 0;
            _cachedNpnActive = null;

            // ---------- A. 闭环 DC 结果（经 SpiceDcSimulationService.SimulateAsync） ----------

            // A1. NPN active 闭环：IC 与 VCE 对实测基准，方向字段非空，原始日志落盘。
            Run("Npn_Active_ClosedLoop", () =>
            {
                var circuit = BuildNpnCommonEmitter(200000d);
                var result = Simulate(circuit);
                WriteClosedLoopLog("BJT1C_Npn_Active", "NPN active (V1=5V, Rb=200k, Rc=1k, emitter GND)", circuit, result);
                CheckTrue(result.Success, "SimulateAsync must succeed: " + DescribeDiagnostics(result));
                CheckTrue(result.ComponentResults.ContainsKey("q1"), "ComponentResults must contain the BJT instance 'q1'.");
                var bjt = result.ComponentResults["q1"];
                CheckRelClose(NpnExpectedIc, bjt.Current, RelativeTolerance, "NPN active IC must match the measured baseline.");
                CheckRelClose(NpnExpectedVce, bjt.Voltage, RelativeTolerance, "NPN active VCE=V(C)-V(E) must match the measured baseline.");
                CheckFalse(string.IsNullOrEmpty(bjt.CurrentDirection), "BJT CurrentDirection must be non-empty.");
                CheckFalse(string.IsNullOrEmpty(bjt.VoltageDirection), "BJT VoltageDirection must be non-empty.");
                _cachedNpnActive = result;
            });

            // A2. PNP active 闭环：IC 为负、VCE = 2.162377-5.0 ≈ -2.837623。
            Run("Pnp_Active_ClosedLoop", () =>
            {
                var circuit = BuildPnpCommonEmitter();
                var result = Simulate(circuit);
                WriteClosedLoopLog("BJT1C_Pnp_Active", "PNP active (emitter +5V, Rb=200k base->GND, Rc=1k collector->GND)", circuit, result);
                CheckTrue(result.Success, "SimulateAsync must succeed: " + DescribeDiagnostics(result));
                CheckTrue(result.ComponentResults.ContainsKey("q1"), "ComponentResults must contain the BJT instance 'q1'.");
                var bjt = result.ComponentResults["q1"];
                CheckRelClose(PnpExpectedIc, bjt.Current, RelativeTolerance, "PNP active IC must match the measured baseline (negative, flowing out of collector).");
                CheckRelClose(PnpExpectedVce, bjt.Voltage, RelativeTolerance, "PNP active VCE=V(C)-V(E) must match the measured baseline.");
                CheckFalse(string.IsNullOrEmpty(bjt.CurrentDirection), "BJT CurrentDirection must be non-empty.");
                CheckFalse(string.IsNullOrEmpty(bjt.VoltageDirection), "BJT VoltageDirection must be non-empty.");
            });

            // A3. 方向约定：NPN active 时 IC > 0（流入 collector 为正），方向字段固化 C-to-E / collector 约定。
            Run("Npn_Direction_Convention", () =>
            {
                // 复用 A1 的真实运行结果；若执行顺序变化导致缓存缺失则重新真实运行，绝不伪造。
                var result = _cachedNpnActive ?? Simulate(BuildNpnCommonEmitter(200000d));
                CheckTrue(result.Success, "NPN active result must be successful: " + DescribeDiagnostics(result));
                var bjt = result.ComponentResults["q1"];
                // 约定文档化：NPN 放大区电流实际流入 collector，ngspice @q[ic] 流入端子为正，故 Current > 0。
                CheckTrue(bjt.Current > 0d, "NPN active IC must be positive (into-collector convention), got " + bjt.Current + ".");
                CheckTrue(bjt.Voltage > 0d, "NPN active VCE=V(C)-V(E) must be positive, got " + bjt.Voltage + ".");
                CheckEqual("C-to-E", bjt.VoltageDirection, "BJT voltage direction must be fixed as C-to-E.");
                CheckTrue((bjt.CurrentDirection ?? string.Empty).IndexOf("collector", StringComparison.OrdinalIgnoreCase) >= 0,
                    "BJT current direction must document the collector-terminal convention, got '" + bjt.CurrentDirection + "'.");
            });

            // ---------- B. cutoff / active / saturation 趋势（NPN，只改基极偏置） ----------

            // B4. cutoff：base 经 0V 源接到 GND，Vbe=0，IC≈0，V(C)≈5V。
            Run("Npn_Cutoff_ClosedLoop", () =>
            {
                var cutoff = RunTrendPoint("cutoff", BuildNpnCutoff(), out var ic, out var vce);
                CheckTrue(cutoff.Success, "Cutoff point must converge: " + DescribeDiagnostics(cutoff));
                CheckTrue(Math.Abs(ic) < 1e-9, "Cutoff IC must be ~0 (<1e-9), got " + ic + ".");
                CheckTrue(vce > 4.99, "Cutoff V(C)≈VCE must read ~5 V (Rc carries no current), got " + vce + ".");
            });

            // B5. saturation：强基极驱动 Rb=10k，β·IB≈43mA 远超 Ic_max=V1/Rc=5mA，IC≈(V1-VCEsat)/Rc，VCE<0.2V。
            Run("Npn_Saturation_ClosedLoop", () =>
            {
                var saturation = RunTrendPoint("saturation", BuildNpnCommonEmitter(10000d), out var ic, out var vce);
                CheckTrue(saturation.Success, "Saturation point must converge: " + DescribeDiagnostics(saturation));
                CheckTrue(ic > 4.0e-3 && ic < 5.1e-3, "Saturation IC must be close to (V1-VCEsat)/Rc ≈ 5 mA, got " + ic + ".");
                CheckTrue(vce < 0.2, "Saturation VCE must be below 0.2 V, got " + vce + ".");
            });

            // B6. 趋势：IC(cutoff) < IC(active) < IC(saturation)，且 saturation 的 VCE 显著小于 active 的 VCE。
            // 三个工作点均真实收敛；三条原始 stdout 拼接写入 Logs\BJT1C_Npn_Trend.log。
            Run("Npn_Trend_CutoffActiveSaturation", () =>
            {
                var cutoffResult = RunTrendPoint("cutoff", BuildNpnCutoff(), out var icCutoff, out var vceCutoff);
                var activeResult = RunTrendPoint("active", BuildNpnCommonEmitter(200000d), out var icActive, out var vceActive);
                var saturationResult = RunTrendPoint("saturation", BuildNpnCommonEmitter(10000d), out var icSaturation, out var vceSaturation);
                WriteTrendLog(new[]
                {
                    ("cutoff", BuildNpnCutoff(), cutoffResult, icCutoff, vceCutoff),
                    ("active", BuildNpnCommonEmitter(200000d), activeResult, icActive, vceActive),
                    ("saturation", BuildNpnCommonEmitter(10000d), saturationResult, icSaturation, vceSaturation)
                });
                Debug.Log($"[BJT1C] Trend: IC cutoff={icCutoff:0.###e+0}, active={icActive:0.###e+0}, saturation={icSaturation:0.###e+0}; " +
                          $"VCE cutoff={vceCutoff:0.######}, active={vceActive:0.######}, saturation={vceSaturation:0.######}");
                CheckTrue(icCutoff < icActive, "Trend must hold: IC(cutoff) < IC(active). Got " + icCutoff + " vs " + icActive + ".");
                CheckTrue(icActive < icSaturation, "Trend must hold: IC(active) < IC(saturation). Got " + icActive + " vs " + icSaturation + ".");
                CheckTrue(vceSaturation < 0.2 && vceActive > 2.0,
                    "Saturation VCE must be far below active-region VCE. Got VCE(sat)=" + vceSaturation + ", VCE(active)=" + vceActive + ".");
            });

            // ---------- C. 解析失败为明确状态（不伪造 0） ----------

            // C7. 无 T2 标记：TryParse 返回 false 且 failure 非空。
            Run("Parse_MissingMarker_Fails", () =>
            {
                var parsed = SpiceDcOutputParser.TryParse("no markers here", out var voltages, out var currents, out var failure);
                CheckFalse(parsed, "TryParse must fail when T2 markers are absent.");
                CheckFalse(string.IsNullOrEmpty(failure), "Parse failure must carry a non-empty failure message.");
                CheckEqual(0, voltages.Count, "Failed parse must not produce node voltages.");
                CheckEqual(0, currents.Count, "Failed parse must not produce branch currents.");
            });

            // C8. 标记存在但内容为空：TryParse 返回 true 且两个字典为空。
            // 分工说明：Parser 层只负责“标记内有什么就解析什么”，向量完整性校验由
            // SpiceDcSimulationService.SimulateAsync 的 ContainsAll(voltages, PrintedNodes) /
            // ContainsAll(currents, PrintedBranchNames) 负责——缺失向量会转为 SPICE_OUTPUT_INCOMPLETE 诊断，
            // 而不是在这里伪造 0。该完整性路径在真实闭环中由 A/B 组用例隐式覆盖（缺向量即 Success=false）。
            Run("Parse_EmptyMarkers_YieldsEmptyDictionaries", () =>
            {
                var empty = SpiceNetlistBuilder.BeginMarker + Environment.NewLine + SpiceNetlistBuilder.EndMarker;
                var parsed = SpiceDcOutputParser.TryParse(empty, out var voltages, out var currents, out var failure);
                CheckTrue(parsed, "Markers with empty content must parse successfully (completeness is SimulateAsync's job). failure=" + (failure ?? string.Empty));
                CheckEqual(0, voltages.Count, "Empty marked section must yield no node voltages.");
                CheckEqual(0, currents.Count, "Empty marked section must yield no branch currents.");
            });

            var total = _passed + _failed;
            Debug.Log($"BJT1C DC Parser Result Tests: {_passed}/{total} passed.");
            if (_failed > 0)
            {
                throw new InvalidOperationException($"BJT1C DC Parser Result Tests: {_failed} of {total} FAILED.");
            }
        }

        // ---------- 闭环运行辅助 ----------

        private static SpiceSimulationResult Simulate(SpiceCircuitModel circuit)
        {
            var service = new SpiceDcSimulationService();
            return service.SimulateAsync(circuit).GetAwaiter().GetResult();
        }

        private static SpiceSimulationResult RunTrendPoint(string label, SpiceCircuitModel circuit, out double ic, out double vce)
        {
            var result = Simulate(circuit);
            if (!result.Success || !result.ComponentResults.ContainsKey("q1"))
            {
                ic = double.NaN;
                vce = double.NaN;
                return result;
            }
            var bjt = result.ComponentResults["q1"];
            ic = bjt.Current;
            vce = bjt.Voltage;
            Debug.Log($"[BJT1C] Trend point '{label}': IC={ic:0.######e+0} A, VCE={vce:0.######} V");
            return result;
        }

        // ---------- Fixture 构造 ----------

        // NPN 共射（V1=5V；Rc=1k 接 VCC→collector；Rb 接 VCC→base；emitter→GND）。Rb 取值决定工作区。
        private static SpiceCircuitModel BuildNpnCommonEmitter(double rbOhms)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rb", rbOhms));
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

        // cutoff：base 通过 0V 源（vbb）直接钳位到 GND，Vbe=0，晶体管截止。
        private static SpiceCircuitModel BuildNpnCutoff()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 5d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vbb", 0d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vbb", "positive", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "vbb", "negative", "gnd", "ground");
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // PNP：emitter 接 +5V；base 经 RB=200k 到 GND；collector 经 RC=1k 到 GND（与 BJT1C_Probe_Pnp.log 同构）。
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

        private static void Wire(SpiceCircuitModel circuit, string startComponent, string startTerminal, string endComponent, string endTerminal)
        {
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(startComponent, startTerminal), new SpiceTerminalRef(endComponent, endTerminal)));
        }

        // ---------- 日志落盘 ----------

        private static string LogsDirectory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteClosedLoopLog(string fileName, string description, SpiceCircuitModel circuit, SpiceSimulationResult result)
        {
            var logPath = Path.Combine(LogsDirectory(), fileName + ".log");
            var content =
                "== BJT1C closed-loop fixture: " + description + " ==" + Environment.NewLine +
                "success=" + result.Success + Environment.NewLine +
                "diagnostics=" + DescribeDiagnostics(result) + Environment.NewLine +
                Environment.NewLine +
                "== INPUT NETLIST ==" + Environment.NewLine +
                (result.GeneratedNetlistContent ?? string.Empty) + Environment.NewLine +
                "== NGSPICE STDOUT ==" + Environment.NewLine +
                (result.RawNgspiceResult?.StandardOutput ?? string.Empty) + Environment.NewLine +
                "== NGSPICE STDERR ==" + Environment.NewLine +
                (result.RawNgspiceResult?.StandardError ?? string.Empty) + Environment.NewLine;
            File.WriteAllText(logPath, content);
            Debug.Log("[BJT1C] Raw ngspice log saved: " + logPath);
        }

        private static void WriteTrendLog((string Label, SpiceCircuitModel Circuit, SpiceSimulationResult Result, double Ic, double Vce)[] points)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("== BJT1C NPN cutoff/active/saturation trend (V1=5V, Rc=1k, emitter GND) ==");
            foreach (var point in points)
            {
                builder.AppendLine();
                builder.AppendLine("==== POINT: " + point.Label + " | IC=" + point.Ic.ToString("0.######e+0") + " A | VCE=" + point.Vce.ToString("0.######") + " V ====");
                builder.AppendLine("success=" + point.Result.Success);
                builder.AppendLine("diagnostics=" + DescribeDiagnostics(point.Result));
                builder.AppendLine();
                builder.AppendLine("-- INPUT NETLIST --");
                builder.AppendLine(point.Result.GeneratedNetlistContent ?? string.Empty);
                builder.AppendLine("-- NGSPICE STDOUT --");
                builder.AppendLine(point.Result.RawNgspiceResult?.StandardOutput ?? string.Empty);
                builder.AppendLine("-- NGSPICE STDERR --");
                builder.AppendLine(point.Result.RawNgspiceResult?.StandardError ?? string.Empty);
            }
            var logPath = Path.Combine(LogsDirectory(), "BJT1C_Npn_Trend.log");
            File.WriteAllText(logPath, builder.ToString());
            Debug.Log("[BJT1C] Trend raw ngspice log saved: " + logPath);
        }

        // ---------- 断言辅助（沿用 BJT1B 模式，不引入 NUnit） ----------

        private static string DescribeDiagnostics(SpiceSimulationResult result)
        {
            var diagnostics = new List<string>();
            foreach (var diagnostic in result.Diagnostics)
            {
                diagnostics.Add(diagnostic.Code + ":" + diagnostic.Message);
            }
            return diagnostics.Count == 0 ? "(none)" : string.Join(" | ", diagnostics);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Debug.Log($"[BJT1C] PASS: {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Debug.LogError($"[BJT1C] FAIL: {name} — {ex.Message}");
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

        private static void CheckRelClose(double expected, double actual, double relativeTolerance, string message)
        {
            var tolerance = relativeTolerance * Math.Abs(expected);
            if (Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException($"{message} Expected {expected:0.######e+0} ±{relativeTolerance:0.#e+0} (rel), got {actual:0.######e+0}.");
        }
    }
}
