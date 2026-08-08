using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Results
{
    /// <summary>
    /// 单频 AC 分析的元件结果。Voltage/Current 均为复数相量（小信号量）。
    /// 方向约定由 VoltageDirection/CurrentDirection 字符串与 Notes 说明：
    /// 双端器件默认 positive-to-negative；BJT 为 C-to-E，电流以流入 collector 为正
    /// （经内部 0V 探针源 i() 读取的小信号 collector 电流）；DcVoltageSource 仅作偏置，AC 激励为 0。
    /// </summary>
    public sealed class SpiceAcComponentResult
    {
        public string ComponentId { get; set; }
        public string ComponentKind { get; set; }
        public SpicePhasor Voltage { get; set; }
        public SpicePhasor Current { get; set; }
        public string VoltageDirection { get; set; }
        public string CurrentDirection { get; set; }
        public SpiceResultStatus ResultStatus { get; set; }
        public string Notes { get; set; }
    }
}
