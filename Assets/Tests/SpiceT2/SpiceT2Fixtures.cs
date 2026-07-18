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
