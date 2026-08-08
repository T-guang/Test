using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// BJT2 — AC 支持测试（正式 AC 闭环）。
    /// 全部闭环用例经 SpiceAcSimulationService.SimulateAsync(circuit) 完整运行（图校验→AC 网表→ngspice→相量解析→元件结果），
    /// 不直接调用 NgspiceProcessRunner；每个闭环用例把原始 ngspice StandardOutput 保存到 Logs\。
    ///
    /// 关键构造（串联源法）：AcVoltageSource 只有幅值/相位、DcVoltageSource 只有 DC 偏置，
    /// 故 base 同时需要 DC 偏置与 AC 激励时用两个源串联：
    ///   VBIAS(DC 0.7V, +→vbi, -→0)：vbi=+0.7；VIN(AC 1∠0, +→vb, -→vbi)：vb=vbi+ac。
    ///   DC 时 VIN 短路→vb=0.7；AC 时 VBIAS 短路→vb=AC1。
    ///
    /// 参照先验（ngspice-45.2，NPN 共射 VCC=10V、Rc=1k、base 由 DC0.7+AC1 驱动、IS=1e-14 BF=100）：
    ///   collector AC 电压 ≈ -219.229 + j0（增益≈-gm·Rc，gm≈0.219 S，IC≈5.67mA）；
    ///   collector 探针复数电流 ≈ +0.2192 + j0（流入 collector 为正）；
    ///   模型卡无动态参数 → AC 响应纯阻性，所有虚部≈0、与频率无关（预期，非 bug）。
    ///
    /// 设计结论（F5 审计固化）：@q[ic] 在 .ac 下只输出 DC 工作点标量、非复数，
    /// 故 AC collector 电流经内部 0V 探针源 i(VQ1C) 取得——不伪造直接 Q 复数电流支持。
    /// </summary>
    public static class BJT2_AcSupportTests
    {
        // 先验基准（ngspice-45.2 前置探测，见任务参照）；F2/F3 首遍真实运行已确认：
        // NPN 与 PNP 镜像电路实测均为 v(collector)=(-219.229, j0)、i(VQ1C)=(+0.2192293, j0)、gain=219.229。
        private const double NpnExpectedCollectorAcReal = -219.229d;
        private const double NpnExpectedProbeCurrentReal = 0.2192293d;
        private const double PnpExpectedCollectorAcReal = -219.229d;
        private const double PnpExpectedProbeCurrentReal = 0.2192293d; // 流入 collector 为正：base AC 抬高→VEB 减小→流出 collector 的电流减小→等效流入为正
        private const double RelativeTolerance = 0.10d;

        private static int _passed;
        private static int _failed;
        private static SpiceSimulationResult _cachedNpnAc;
        private static SpiceSimulationResult _cachedPnpAc;

        [MenuItem("Tools/Tests/Run BJT2 AC Support Tests")]
        public static void RunAllTests()
        {
            _passed = 0;
            _failed = 0;
            _cachedNpnAc = null;
            _cachedPnpAc = null;

            // ---------- F1. AC_DC_BIAS：只有 DC 偏置、AC 激励幅值≈0 时小信号全零 ----------
            // 生产门禁要求 AC 电路至少含一个幅值>0 的 AcVoltageSource（SPICE_AC_SOURCE_MISSING，
            // 且幅值必须 >0，见 SpiceAnalysisLimits.IsValidAcMagnitude），故用幅值 1e-30 的
            // AcVoltageSource 表达“无有效 AC 激励”；DC 工作点仍收敛，全部小信号相量≈0。
            Run("F1_AC_DcBias_ZeroSmallSignal", () =>
            {
                var circuit = BuildNpnDcBiasOnly();
                var result = Simulate(circuit);
                WriteClosedLoopLog("AC_DC_BIAS", "NPN bias-only (VCC=10V DC, VBIAS=0.7V DC, Rc=1k, AC excitation magnitude 1e-30 ~= no excitation): all small-signal phasors must be ~0", circuit, result);
                CheckTrue(result.Success, "SimulateAsync must succeed: " + DescribeDiagnostics(result));
                CheckFalse(HasErrorDiagnostic(result), "Diagnostics must carry no Error severity entry: " + DescribeDiagnostics(result));
                CheckTrue(result.AcNodeVoltages.Count > 0, "Result must be stable: AcNodeVoltages must be populated.");
                CheckTrue(result.AcComponentResults.Count > 0, "Result must be stable: AcComponentResults must be populated.");
                foreach (var pair in result.AcNodeVoltages)
                    CheckTrue(pair.Value.Magnitude < 1e-12d, "Node " + pair.Key + " small-signal must be ~0 without AC excitation, got magnitude " + pair.Value.Magnitude + ".");
                foreach (var pair in result.AcComponentResults)
                {
                    CheckTrue(pair.Value.Voltage.Magnitude < 1e-12d, "Component " + pair.Key + " AC voltage must be ~0 without AC excitation, got " + pair.Value.Voltage.Magnitude + ".");
                    CheckTrue(pair.Value.Current.Magnitude < 1e-12d, "Component " + pair.Key + " AC current must be ~0 without AC excitation, got " + pair.Value.Current.Magnitude + ".");
                }
            });

            // ---------- F2. AC_NPN：1 kHz NPN 共射真实收敛 ----------
            Run("F2_AC_Npn_ClosedLoop", () =>
            {
                var circuit = BuildNpnCommonEmitterAc();
                var result = Simulate(circuit);
                WriteClosedLoopLog("AC_NPN", "NPN common-emitter AC 1kHz (VCC=10V, Rc=1k, base via VBIAS 0.7V + VIN AC1 series sources, emitter GND)", circuit, result);
                CheckTrue(result.Success, "SimulateAsync must succeed: " + DescribeDiagnostics(result));
                CheckFalse(HasErrorDiagnostic(result), "Diagnostics must carry no Error severity entry: " + DescribeDiagnostics(result));

                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "Fixture topology must build a valid graph: " + string.Join(" | ", graph.Diagnostics.Select(d => d.Code)));
                var collectorNode = graph.NodeByTerminal[new SpiceTerminalRef("q1", SpiceComponentModel.CollectorTerminalId)];
                var baseNode = graph.NodeByTerminal[new SpiceTerminalRef("vin", SpiceComponentModel.PositiveTerminalId)];
                CheckTrue(result.AcNodeVoltages.ContainsKey(collectorNode), "Collector node phasor must be present in AcNodeVoltages.");
                CheckTrue(result.AcNodeVoltages.ContainsKey(baseNode), "Base node phasor must be present in AcNodeVoltages.");

                var vCollector = result.AcNodeVoltages[collectorNode];
                var vBase = result.AcNodeVoltages[baseNode];
                Debug.Log($"[BJT2] AC_NPN measured: v(collector)=({vCollector.Real:0.######e+0},{vCollector.Imaginary:0.######e+0}), v(base)=({vBase.Real:0.######e+0},{vBase.Imaginary:0.######e+0})");
                CheckTrue(vCollector.Magnitude > 10d, "Collector AC voltage must be amplified (magnitude > 10), got " + vCollector.Magnitude + ".");
                CheckTrue(vCollector.Real < 0d, "Collector AC real part must be negative (inverting gain), got " + vCollector.Real + ".");
                CheckRelClose(NpnExpectedCollectorAcReal, vCollector.Real, RelativeTolerance, "Collector AC real part must match the measured baseline -219.229.");
                CheckImagNearZero(vCollector, "collector AC voltage");

                var gain = vCollector.Magnitude / vBase.Magnitude;
                Debug.Log($"[BJT2] AC_NPN measured gain |v(c)|/|v(b)|={gain:0.######} (v(base) magnitude={vBase.Magnitude:0.######})");
                CheckTrue(!double.IsNaN(gain) && !double.IsInfinity(gain) && gain > 0d, "Gain must be a finite positive number, got " + gain + ".");

                CheckTrue(result.AcComponentResults.ContainsKey("q1"), "AcComponentResults must contain the BJT instance 'q1'.");
                var bjt = result.AcComponentResults["q1"];
                Debug.Log($"[BJT2] AC_NPN measured probe current i(VQ1C)=({bjt.Current.Real:0.######e+0},{bjt.Current.Imaginary:0.######e+0}), V(C)-V(E)=({bjt.Voltage.Real:0.######e+0},{bjt.Voltage.Imaginary:0.######e+0})");
                CheckRelClose(NpnExpectedProbeCurrentReal, bjt.Current.Real, RelativeTolerance, "Probe branch complex current real part must match the measured baseline +0.2192293 (into collector).");
                CheckImagNearZero(bjt.Current, "probe branch complex current");
                CheckFalse(string.IsNullOrEmpty(bjt.CurrentDirection), "BJT CurrentDirection must be non-empty.");
                CheckFalse(string.IsNullOrEmpty(bjt.VoltageDirection), "BJT VoltageDirection must be non-empty.");
                _cachedNpnAc = result;
            });

            // ---------- F3. AC_PNP：PNP 镜像电路真实收敛（先真实运行再固化断言） ----------
            Run("F3_AC_Pnp_ClosedLoop", () =>
            {
                var circuit = BuildPnpCommonEmitterAc();
                var result = Simulate(circuit);
                WriteClosedLoopLog("AC_PNP", "PNP common-emitter AC 1kHz (emitter VCC=10V, Rc=1k collector->GND, base via VBIAS 0.7V below VCC + VIN AC1 series sources)", circuit, result);
                CheckTrue(result.Success, "SimulateAsync must succeed: " + DescribeDiagnostics(result));
                CheckFalse(HasErrorDiagnostic(result), "Diagnostics must carry no Error severity entry: " + DescribeDiagnostics(result));

                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                CheckTrue(graph.IsValid, "Fixture topology must build a valid graph: " + string.Join(" | ", graph.Diagnostics.Select(d => d.Code)));
                var collectorNode = graph.NodeByTerminal[new SpiceTerminalRef("q1", SpiceComponentModel.CollectorTerminalId)];
                var baseNode = graph.NodeByTerminal[new SpiceTerminalRef("vin", SpiceComponentModel.PositiveTerminalId)];
                CheckTrue(result.AcNodeVoltages.ContainsKey(collectorNode), "Collector node phasor must be present in AcNodeVoltages.");
                CheckTrue(result.AcNodeVoltages.ContainsKey(baseNode), "Base node phasor must be present in AcNodeVoltages.");

                var vCollector = result.AcNodeVoltages[collectorNode];
                var vBase = result.AcNodeVoltages[baseNode];
                Debug.Log($"[BJT2] AC_PNP measured: v(collector)=({vCollector.Real:0.######e+0},{vCollector.Imaginary:0.######e+0}), v(base)=({vBase.Real:0.######e+0},{vBase.Imaginary:0.######e+0})");
                CheckTrue(vCollector.Magnitude > 10d, "Collector AC voltage must be amplified (magnitude > 10), got " + vCollector.Magnitude + ".");
                CheckTrue(vCollector.Real < 0d, "Collector AC real part must be negative (inverting gain), got " + vCollector.Real + ".");
                CheckRelClose(PnpExpectedCollectorAcReal, vCollector.Real, RelativeTolerance, "Collector AC real part must match the real-run baseline -219.229.");
                CheckImagNearZero(vCollector, "collector AC voltage");

                var gain = vCollector.Magnitude / vBase.Magnitude;
                Debug.Log($"[BJT2] AC_PNP measured gain |v(c)|/|v(b)|={gain:0.######} (v(base) magnitude={vBase.Magnitude:0.######})");
                CheckTrue(!double.IsNaN(gain) && !double.IsInfinity(gain) && gain > 0d, "Gain must be a finite positive number, got " + gain + ".");
                CheckRelClose(Math.Abs(PnpExpectedCollectorAcReal), gain, RelativeTolerance, "Gain must match the real-run baseline ~219.229.");

                CheckTrue(result.AcComponentResults.ContainsKey("q1"), "AcComponentResults must contain the BJT instance 'q1'.");
                var bjt = result.AcComponentResults["q1"];
                Debug.Log($"[BJT2] AC_PNP measured probe current i(VQ1C)=({bjt.Current.Real:0.######e+0},{bjt.Current.Imaginary:0.######e+0}), V(C)-V(E)=({bjt.Voltage.Real:0.######e+0},{bjt.Voltage.Imaginary:0.######e+0})");
                CheckTrue(!double.IsNaN(bjt.Current.Real) && !double.IsNaN(bjt.Current.Imaginary), "Probe current must not be NaN.");
                CheckTrue(bjt.Current.Magnitude > 1e-6d, "Probe complex current must be finite and non-zero, got magnitude " + bjt.Current.Magnitude + ".");
                CheckRelClose(PnpExpectedProbeCurrentReal, bjt.Current.Real, RelativeTolerance, "Probe branch complex current real part must match the real-run baseline +0.2192293 (into-collector convention).");
                CheckImagNearZero(bjt.Current, "probe branch complex current");
                CheckFalse(string.IsNullOrEmpty(bjt.CurrentDirection), "BJT CurrentDirection must be non-empty.");
                CheckFalse(string.IsNullOrEmpty(bjt.VoltageDirection), "BJT VoltageDirection must be non-empty.");
                _cachedPnpAc = result;
            });

            // ---------- F4. 兼容性门禁矩阵 ----------
            Run("F4_AcCompatibilityMatrix", () =>
            {
                var cases = new (string Label, SpiceComponentModel Component, bool ExpectedAllowed)[]
                {
                    ("GenericNpnBjt", SpiceComponentModel.GenericNpnBjt("gate-qn"), true),
                    ("GenericPnpBjt", SpiceComponentModel.GenericPnpBjt("gate-qp"), true),
                    ("DcVoltageSource", SpiceComponentModel.DcVoltageSource("gate-vdc", 1d), true),
                    ("DcCurrentSource", SpiceComponentModel.DcCurrentSource("gate-idc", 0.001d), false),
                    ("SiliconDiode", SpiceComponentModel.SiliconDiode("gate-d1"), false),
                    ("Resistor", SpiceComponentModel.Resistor("gate-r1", 1000d), true),
                    ("Capacitor", SpiceComponentModel.Capacitor("gate-c1", 1e-6d), true),
                    ("Inductor", SpiceComponentModel.Inductor("gate-l1", 0.01d), true),
                    ("AcVoltageSource", SpiceComponentModel.AcVoltageSource("gate-vac", 1d, 0d), true),
                    ("IdealSwitch", SpiceComponentModel.IdealSwitch("gate-sw", true), true),
                    ("VoltageProbe", SpiceComponentModel.VoltageProbe("gate-vp"), true),
                    ("CurrentProbe", SpiceComponentModel.CurrentProbe("gate-ip"), true),
                    ("IdealOperationalAmplifier", SpiceComponentModel.IdealOperationalAmplifier("gate-op"), true),
                    ("Ground", SpiceComponentModel.Ground("gate-gnd2"), true)
                };

                var builder = new StringBuilder();
                builder.AppendLine("# BJT2 AC compatibility matrix: SpiceCircuitGraphBuilder.Build (AcSingleFrequency 1000 Hz) gate");
                builder.AppendLine("Kind,AC_Allowed,预期,实测,一致");
                var allConsistent = true;
                foreach (var testCase in cases)
                {
                    var actual = IsAcAllowedByGate(testCase.Component);
                    var consistent = actual == testCase.ExpectedAllowed;
                    allConsistent &= consistent;
                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4}",
                        testCase.Label,
                        testCase.ExpectedAllowed ? "允许" : "拒绝",
                        testCase.ExpectedAllowed ? "true" : "false",
                        actual ? "true" : "false",
                        consistent ? "YES" : "NO"));
                    Debug.Log($"[BJT2] Gate: {testCase.Label} expected={(testCase.ExpectedAllowed ? "allowed" : "rejected")} actual={(actual ? "allowed" : "rejected")} consistent={consistent}");
                    CheckTrue(consistent, "Compatibility gate mismatch for " + testCase.Label + ": expected " +
                        (testCase.ExpectedAllowed ? "allowed (no SPICE_AC_COMPONENT_UNSUPPORTED)" : "rejected (SPICE_AC_COMPONENT_UNSUPPORTED)") + ", got " + (actual ? "allowed" : "rejected") + ".");
                }
                var csvPath = Path.Combine(LogsDirectory(), "BJT2_AC_CompatibilityMatrix.csv");
                File.WriteAllText(csvPath, builder.ToString());
                Debug.Log("[BJT2] Compatibility matrix saved: " + csvPath);
                CheckTrue(allConsistent, "Compatibility matrix must be fully consistent.");
            });

            // ---------- F5. AC current vector audit（基于 F2 NPN 真实结果） ----------
            Run("F5_AcCurrentVectorAudit", () =>
            {
                var result = _cachedNpnAc ?? Simulate(BuildNpnCommonEmitterAc());
                CheckTrue(result.Success, "NPN AC result must be successful for the audit: " + DescribeDiagnostics(result));
                var circuit = BuildNpnCommonEmitterAc();
                var graph = SpiceCircuitGraphBuilder.Build(circuit);
                var collectorNode = graph.NodeByTerminal[new SpiceTerminalRef("q1", SpiceComponentModel.CollectorTerminalId)];
                var baseNode = graph.NodeByTerminal[new SpiceTerminalRef("vin", SpiceComponentModel.PositiveTerminalId)];
                var vCollector = result.AcNodeVoltages[collectorNode];
                var vBase = result.AcNodeVoltages[baseNode];
                var probe = result.AcComponentResults["q1"].Current;
                var gain = vCollector.Magnitude / vBase.Magnitude;

                // gm 理论值：IC≈5.67mA（前置探测）、VT=kT/q≈0.025852 V（ngspice 名义温度 27°C）。
                const double referenceIc = 5.67e-3d;
                const double thermalVoltage = 0.025852d;
                var gmTheory = referenceIc / thermalVoltage;
                var gainTheory = gmTheory * 1000d;

                var builder = new StringBuilder();
                builder.AppendLine("# BJT2 AC current vector audit (NPN common-emitter, 1 kHz, Rc=1k, VCC=10V, base=DC0.7+AC1)");
                builder.AppendLine("Metric,Value,Note");
                builder.AppendLine(Line("probe_current_real_A", probe.Real, "i(VQ1C) 实部；流入 collector 为正"));
                builder.AppendLine(Line("probe_current_imag_A", probe.Imaginary, "i(VQ1C) 虚部；模型卡无动态参数→≈0"));
                builder.AppendLine(Line("probe_current_magnitude_A", probe.Magnitude, "复数幅值"));
                builder.AppendLine(Line("probe_current_phase_deg", probe.PhaseDegrees, "相位（度）"));
                builder.AppendLine(Line("collector_voltage_real_V", vCollector.Real, "v(collector) 实部"));
                builder.AppendLine(Line("collector_voltage_imag_V", vCollector.Imaginary, "v(collector) 虚部；≈0"));
                builder.AppendLine(Line("collector_voltage_magnitude_V", vCollector.Magnitude, "复数幅值"));
                builder.AppendLine(Line("collector_voltage_phase_deg", vCollector.PhaseDegrees, "相位（度）"));
                builder.AppendLine(Line("gain_measured", gain, "|v(c)|/|v(base)|"));
                builder.AppendLine(Line("gm_theory_S", gmTheory, "IC/VT，IC≈5.67mA、VT=0.025852V"));
                builder.AppendLine(Line("gain_theory", gainTheory, "gm·Rc，Rc=1k"));
                builder.AppendLine("design_conclusion,@q[ic] 在 .ac 下只输出 DC 工作点标量、非复数；AC collector 电流经内部 0V 探针源 i(VQ1C) 取得。这是设计结论，不伪造直接 Q 复数电流支持。");
                var csvPath = Path.Combine(LogsDirectory(), "BJT2_AC_CurrentVectorAudit.csv");
                File.WriteAllText(csvPath, builder.ToString());
                Debug.Log("[BJT2] AC current vector audit saved: " + csvPath);
                CheckTrue(File.Exists(csvPath), "Audit CSV must exist on disk.");
            });

            var total = _passed + _failed;
            Debug.Log($"BJT2 AC Support Tests: {_passed}/{total} passed.");
            if (_failed > 0)
            {
                throw new InvalidOperationException($"BJT2 AC Support Tests: {_failed} of {total} FAILED.");
            }
        }

        // ---------- 闭环运行辅助 ----------

        private static SpiceSimulationResult Simulate(SpiceCircuitModel circuit)
        {
            var service = new SpiceAcSimulationService();
            return service.SimulateAsync(circuit).GetAwaiter().GetResult();
        }

        private static bool HasErrorDiagnostic(SpiceSimulationResult result)
        {
            return result.Diagnostics.Any(diagnostic => diagnostic.Severity == SpiceDiagnosticSeverity.Error);
        }

        // F4 门禁：只用 SpiceCircuitGraphBuilder.Build（AC 模式）检查 SPICE_AC_COMPONENT_UNSUPPORTED 是否存在。
        private static bool IsAcAllowedByGate(SpiceComponentModel component)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(component);
            circuit.Components.Add(SpiceComponentModel.Ground("gate-gnd"));
            var graph = SpiceCircuitGraphBuilder.Build(circuit);
            return !graph.Diagnostics.Any(diagnostic => diagnostic.Code == "SPICE_AC_COMPONENT_UNSUPPORTED");
        }

        // ---------- Fixture 构造 ----------

        // F1：DC 偏置（VCC=10V + VBIAS=0.7V）+ 幅值 1e-30 的 AC 源（生产门禁要求至少一个幅值>0 的
        // AcVoltageSource；1e-30 等效“无激励”）。串联源法与 F2 一致；AC 小信号应全零。
        private static SpiceCircuitModel BuildNpnDcBiasOnly()
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 10d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vbias", 0.7d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("vin", 1e-30d, 0d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vbias", "positive", "vin", "negative");
            Wire(circuit, "vin", "positive", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "vbias", "negative", "gnd", "ground");
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // F2：NPN 共射 AC。串联源法：VBIAS(DC 0.7, +→vbi, -→0)；VIN(AC 1∠0, +→vb, -→vbi)；base 接 vb。
        private static SpiceCircuitModel BuildNpnCommonEmitterAc()
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vbias", 0.7d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("vin", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.GenericNpnBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "rc", "positive");
            Wire(circuit, "rc", "negative", "q1", SpiceComponentModel.CollectorTerminalId);
            Wire(circuit, "vbias", "positive", "vin", "negative");
            Wire(circuit, "vin", "positive", "q1", SpiceComponentModel.BaseTerminalId);
            Wire(circuit, "vbias", "negative", "gnd", "ground");
            Wire(circuit, "q1", SpiceComponentModel.EmitterTerminalId, "gnd", "ground");
            Wire(circuit, "vcc", "negative", "gnd", "ground");
            return circuit;
        }

        // F3：PNP 镜像。emitter 接 VCC=10V；VBIAS(DC 0.7, +→vcc, -→vbi) 使 base DC=9.3（VEB=0.7）；
        // VIN(AC 1∠0, +→vb, -→vbi) 叠加 AC；Rc=1k collector→GND。
        private static SpiceCircuitModel BuildPnpCommonEmitterAc()
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vcc", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("rc", 1000d));
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("vbias", 0.7d));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("vin", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.GenericPnpBjt("q1"));
            circuit.Components.Add(SpiceComponentModel.Ground("gnd"));
            Wire(circuit, "vcc", "positive", "q1", SpiceComponentModel.EmitterTerminalId);
            Wire(circuit, "vcc", "positive", "vbias", "positive");
            Wire(circuit, "vbias", "negative", "vin", "negative");
            Wire(circuit, "vin", "positive", "q1", SpiceComponentModel.BaseTerminalId);
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
                "== BJT2 AC closed-loop fixture: " + description + " ==" + Environment.NewLine +
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
            Debug.Log("[BJT2] Raw ngspice log saved: " + logPath);
        }

        private static string Line(string metric, double value, string note)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1:0.###############e+0},{2}", metric, value, note);
        }

        // ---------- 断言辅助（沿用 BJT1C 模式，不引入 NUnit） ----------

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
                Debug.Log($"[BJT2] PASS: {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Debug.LogError($"[BJT2] FAIL: {name} — {ex.Message}");
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

        // 模型卡无动态参数 → AC 响应纯阻性，虚部应≈0；用相对幅值 1e-6 的阈值判定。
        private static void CheckImagNearZero(SpicePhasor phasor, string label)
        {
            var threshold = 1e-6d * Math.Max(1d, Math.Abs(phasor.Real));
            if (Math.Abs(phasor.Imaginary) > threshold)
                throw new InvalidOperationException($"{label} imaginary part must be ~0 (resistive AC response), got {phasor.Imaginary:0.######e+0} with threshold {threshold:0.######e+0}.");
        }
    }
}
