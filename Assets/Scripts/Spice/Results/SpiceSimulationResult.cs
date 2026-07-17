using System;
using System.Collections.Generic;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Results
{
    public enum SpiceResultStatus { Available, DcSteadyStateOpenCircuit }

    /// <summary>
    /// 单个元件的 DC 结果。电压固定为 positive/a 减 negative/b；普通元件电流同向，
    /// 电压源电流保留 ngspice 的符号约定。
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
        public string AnalysisType { get; set; } = "DC Operating Point";
        public TimeSpan Duration { get; set; }
        public string Netlist { get; set; }
        public List<SpiceDiagnostic> Diagnostics { get; } = new List<SpiceDiagnostic>();
        public Dictionary<string, double> NodeVoltages { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpiceComponentResult> ComponentResults { get; } = new Dictionary<string, SpiceComponentResult>(StringComparer.Ordinal);
        public NgspiceRunResult RawNgspiceResult { get; set; }
    }
}
