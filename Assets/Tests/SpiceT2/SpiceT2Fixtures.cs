using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.T2
{
    public static class SpiceT2Fixtures
    {
        public static SpiceCircuitModel SingleResistor(double resistance = 1000d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", resistance));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel ReversedSingleResistor(double resistance = 2000d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", resistance));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "negative");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "positive", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel CurrentSourceAndResistor(double current = 0.001d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcCurrentSource("current", current));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            // P -> N injects the configured current into the resistor node.
            Wire(circuit, "current", "positive", "ground", "ground");
            Wire(circuit, "current", "negative", "r1", "positive");
            Wire(circuit, "r1", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel SwitchAndResistor(bool closed)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.IdealSwitch("switch", closed));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "switch", "positive");
            Wire(circuit, "switch", "negative", "r1", "positive");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel Divider(double r2Resistance = 1000d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r2", r2Resistance));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "r2", "positive");
            Wire(circuit, "r2", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel ParallelResistors()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r2", 2000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "positive", "r2", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "r2", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel ResistorAndCapacitor()
        {
            var circuit = SingleResistor();
            circuit.Components.Add(SpiceComponentModel.Capacitor("c1", 1e-6d));
            Wire(circuit, "source", "positive", "c1", "positive");
            Wire(circuit, "c1", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel ResistorAndInductor()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Inductor("l1", 0.01d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "l1", "positive");
            Wire(circuit, "l1", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // 正偏二极管回路：5 V -> 1 kOhm -> 二极管阳极(A=positive) -> 阴极(K=negative) -> GND。
        public static SpiceCircuitModel ForwardDiode(double volts = 5d, double resistance = 1000d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", volts));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", resistance));
            circuit.Components.Add(SpiceComponentModel.SiliconDiode("d1"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "d1", "positive");
            Wire(circuit, "d1", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // 反偏二极管回路：5 V -> 1 kOhm -> 二极管阴极(K=negative)；阳极(A=positive) -> GND。
        public static SpiceCircuitModel ReverseDiode(double volts = 5d, double resistance = 1000d)
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", volts));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", resistance));
            circuit.Components.Add(SpiceComponentModel.SiliconDiode("d1"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "d1", "negative");
            Wire(circuit, "d1", "positive", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // 两只二极管串联，用于校验 .model D_GENERIC 只生成一次。
        public static SpiceCircuitModel TwoForwardDiodes()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 5d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.SiliconDiode("d1"));
            circuit.Components.Add(SpiceComponentModel.SiliconDiode("d2"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "d1", "positive");
            Wire(circuit, "d1", "negative", "d2", "positive");
            Wire(circuit, "d2", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // 电流探针串联在 10 V / 1 kOhm 支路：IN -> OUT 为实际电流方向。
        public static SpiceCircuitModel CurrentProbeSeries()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "iprobe-1", "positive");
            Wire(circuit, "iprobe-1", "negative", "r1", "positive");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // 探针反向串联：实际电流由 OUT 流向 IN，读数应为负。
        public static SpiceCircuitModel ReversedCurrentProbeSeries()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "iprobe-1", "negative");
            Wire(circuit, "iprobe-1", "positive", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel TwoCurrentProbesSeries()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-2"));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "iprobe-1", "positive");
            Wire(circuit, "iprobe-1", "negative", "iprobe-2", "positive");
            Wire(circuit, "iprobe-2", "negative", "r1", "positive");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            return circuit;
        }

        // Cross-component contract: closed switch, serial current probe, diode, and differential voltage probe.
        public static SpiceCircuitModel DcLibraryV1SeriesChain()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 5d));
            circuit.Components.Add(SpiceComponentModel.IdealSwitch("switch", true));
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.SiliconDiode("d1"));
            circuit.Components.Add(SpiceComponentModel.Resistor("r2", 2340d));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "source", "positive", "switch", "positive");
            Wire(circuit, "switch", "negative", "iprobe-1", "positive");
            Wire(circuit, "iprobe-1", "negative", "r1", "positive");
            Wire(circuit, "r1", "negative", "d1", "positive");
            Wire(circuit, "d1", "negative", "r2", "positive");
            Wire(circuit, "r2", "negative", "ground", "ground");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "r2", "positive");
            Wire(circuit, "vprobe-1", "negative", "ground", "ground");
            return circuit;
        }

        // Parallel with an ideal voltage source must be rejected before ngspice.
        public static SpiceCircuitModel CurrentProbeParallelSource()
        {
            var circuit = SingleResistor();
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            Wire(circuit, "iprobe-1", "positive", "source", "positive");
            Wire(circuit, "iprobe-1", "negative", "ground", "ground");
            return circuit;
        }

        public static SpiceCircuitModel CurrentProbeOnlyInConnected()
        {
            var circuit = SingleResistor();
            circuit.Components.Add(SpiceComponentModel.CurrentProbe("iprobe-1"));
            Wire(circuit, "iprobe-1", "positive", "source", "positive");
            return circuit;
        }

        public static SpiceCircuitModel FloatingClosedLoop()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("v1", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            Wire(circuit, "v1", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "v1", "negative");
            return circuit;
        }

        // 电压探针跨接在 10 V 电源两端：V+ -> source+、V- -> ground。期望差分电压约 +10 V。
        public static SpiceCircuitModel VoltageProbeAcrossSource()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "source", "positive");
            Wire(circuit, "vprobe-1", "negative", "ground", "ground");
            return circuit;
        }

        // 电压探针反接：V+ -> ground、V- -> source+。期望差分电压约 -10 V。
        public static SpiceCircuitModel ReversedVoltageProbeAcrossSource()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "ground", "ground");
            Wire(circuit, "vprobe-1", "negative", "source", "positive");
            return circuit;
        }

        // 电压探针两端接在同一节点（source+ 与 r1+ 同属一个电气节点）。期望差分电压约 0 V。
        public static SpiceCircuitModel VoltageProbeSameNode()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "source", "positive");
            Wire(circuit, "vprobe-1", "negative", "r1", "positive");
            return circuit;
        }

        // 两只电压探针同时跨接在电源两端，用于校验双探针结果不冲突且实例名稳定。
        public static SpiceCircuitModel TwoVoltageProbesAcrossSource()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-2"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "source", "positive");
            Wire(circuit, "vprobe-1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-2", "positive", "source", "positive");
            Wire(circuit, "vprobe-2", "negative", "ground", "ground");
            return circuit;
        }

        // 电压探针仅 V+ 连接，V- 浮空。应被 SPICE_FLOATING_TERMINAL 拦截。
        public static SpiceCircuitModel VoltageProbeOnlyPositiveConnected()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "vprobe-1", "positive", "source", "positive");
            return circuit;
        }

        // 电压探针两端均未接。应被 SPICE_FLOATING_TERMINAL 拦截。
        public static SpiceCircuitModel VoltageProbeBothTerminalsDisconnected()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            return circuit;
        }

        // 电压探针接入浮空子回路。探针不得把浮空子回路桥接到地。
        public static SpiceCircuitModel VoltageProbeOnFloatingSubcircuit()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground"));
            // 浮空子回路
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("v2", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r2", 1000d));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "source", "negative", "ground", "ground");
            Wire(circuit, "r1", "negative", "ground", "ground");
            Wire(circuit, "v2", "positive", "r2", "positive");
            Wire(circuit, "r2", "negative", "v2", "negative");
            Wire(circuit, "vprobe-1", "positive", "v2", "positive");
            Wire(circuit, "vprobe-1", "negative", "v2", "negative");
            return circuit;
        }

        // 电压探针跨接在电源两端，但电路缺少 GND。应被 SPICE_GROUND_MISSING 拦截。
        public static SpiceCircuitModel VoltageProbeWithoutGround()
        {
            var circuit = new SpiceCircuitModel();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("source", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r1", 1000d));
            circuit.Components.Add(SpiceComponentModel.VoltageProbe("vprobe-1"));
            Wire(circuit, "source", "positive", "r1", "positive");
            Wire(circuit, "r1", "negative", "source", "negative");
            Wire(circuit, "vprobe-1", "positive", "source", "positive");
            Wire(circuit, "vprobe-1", "negative", "source", "negative");
            return circuit;
        }

        public static SpiceCircuitModel GroundedAndFloatingCircuits()
        {
            var circuit = SingleResistor();
            circuit.Components.Add(SpiceComponentModel.DcVoltageSource("v2", 10d));
            circuit.Components.Add(SpiceComponentModel.Resistor("r2", 1000d));
            Wire(circuit, "v2", "positive", "r2", "positive");
            Wire(circuit, "r2", "negative", "v2", "negative");
            return circuit;
        }

        public static SpiceCircuitModel DividerWithMultipleGrounds()
        {
            var circuit = Divider();
            circuit.Components.Add(SpiceComponentModel.Ground("ground2"));
            return circuit;
        }

        public static void Wire(SpiceCircuitModel circuit, string startComponent, string startTerminal, string endComponent, string endTerminal)
        {
            circuit.Wires.Add(new SpiceWireModel(new SpiceTerminalRef(startComponent, startTerminal), new SpiceTerminalRef(endComponent, endTerminal)));
        }
    }
}
