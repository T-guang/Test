using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Results
{
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
