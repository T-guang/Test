using System;
using System.Collections.Generic;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Results
{
    public enum SpiceResultStatus { Available, DcSteadyStateOpenCircuit }

    /// <summary>
    /// 单个元件的 DC 结果。电压固定为 positive/a 减 negative/b；普通元件电流同向，
    /// 电压源电流保留 ngspice 的符号约定。
    /// BJT：Voltage = V(C)-V(E)；Current = IC（ngspice @q[ic]，流入 collector 端子为正），
    /// NPN 放大区 IC 为正、PNP 放大区 IC 为负；仅报告 IC，不报告 ib/ie。
    /// </summary>
    public sealed class SpiceComponentResult
    {
        public string ComponentId { get; set; }
        public string ComponentKind { get; set; }
        public double Voltage { get; set; }
        public double Current { get; set; }
        public string VoltageDirection { get; set; }
        public string CurrentDirection { get; set; }
        public SpiceResultStatus ResultStatus { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>
    /// 一次 DC 工作点运行的结构化结果；Diagnostics 记录校验、进程或解析失败，不以默认数值掩盖失败。
    /// </summary>
    public sealed class SpiceSimulationResult
    {
        public bool Success { get; set; }
        public SpiceAnalysisSettings AnalysisSettings { get; set; } =
            new SpiceAnalysisSettings(SpiceAnalysisMode.DcOperatingPoint, SpiceAnalysisLimits.DefaultFrequencyHz);
        public string AnalysisType => AnalysisSettings.Mode == SpiceAnalysisMode.AcSingleFrequency
            ? "AC Single Frequency"
            : "DC Operating Point";
        public TimeSpan Duration { get; set; }
        /// <summary>
        /// 图校验通过后由 SpiceNetlistBuilder 生成、并准备提交给 ngspice 的原始文本。
        /// 拓扑校验失败时保持 null；后续进程或解析失败不会清除已经生成的网表。
        /// </summary>
        public string GeneratedNetlistContent { get; set; }

        // 保留 T1/T2 测试报告的既有读取名称；其内容始终等同于实际生成的网表文本。
        public string Netlist
        {
            get => GeneratedNetlistContent;
            set => GeneratedNetlistContent = value;
        }
        public List<SpiceDiagnostic> Diagnostics { get; } = new List<SpiceDiagnostic>();
        public Dictionary<string, double> NodeVoltages { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpiceComponentResult> ComponentResults { get; } = new Dictionary<string, SpiceComponentResult>(StringComparer.Ordinal);
        public Dictionary<string, SpicePhasor> AcNodeVoltages { get; } = new Dictionary<string, SpicePhasor>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpiceAcComponentResult> AcComponentResults { get; } = new Dictionary<string, SpiceAcComponentResult>(StringComparer.Ordinal);
        public NgspiceRunResult RawNgspiceResult { get; set; }
    }
}
