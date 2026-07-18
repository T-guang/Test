using System;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using UnityEngine;

namespace ElectricalSim.Spice.T3
{
    public static class SpiceT3WorkspaceValidation
    {
        public static void RunPureChecks()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            var ground = model.AddComponent(SpiceComponentKind.Ground, Vector2.down);
            if (!model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive") ||
                !model.AddWire(source.InstanceId, "negative", ground.InstanceId, "ground") ||
                !model.AddWire(resistor.InstanceId, "negative", ground.InstanceId, "ground")) throw new InvalidOperationException("Workspace did not retain valid terminal wires.");
            var circuit = model.BuildCircuitModel();
            if (circuit.Components.Count != 3 || circuit.Wires.Count != 3) throw new InvalidOperationException("Workspace-to-SpiceCircuitModel mapping is incomplete.");
            if (!SpiceParameterUnits.TryToSi(SpiceComponentKind.Resistor, 2d, "kOhm", out var resistance) || Math.Abs(resistance - 2000d) > 1e-12d) throw new InvalidOperationException("Resistance unit conversion failed.");
            if (!SpiceParameterUnits.TryToSi(SpiceComponentKind.Capacitor, 1d, "uF", out var capacitance) || Math.Abs(capacitance - 1e-6d) > 1e-15d) throw new InvalidOperationException("Capacitance unit conversion failed.");
            if (SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Resistor, 2000d) != "2 kΩ" ||
                SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Capacitor, 1e-6d) != "1 μF" ||
                SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Inductor, 0.01d) != "10 mH")
                throw new InvalidOperationException("Engineering-unit annotation formatting is inconsistent with the workspace model.");
            if (model.TrySetParameter(resistor.InstanceId, 0d)) throw new InvalidOperationException("Invalid zero-ohm parameter was accepted.");
            if (model.AddWire(source.InstanceId, "positive", source.InstanceId, "negative")) throw new InvalidOperationException("Workspace accepted a direct same-component wire.");

            var manualModel = new SpiceWorkspaceModel();
            var manualSource = manualModel.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var manualResistor = manualModel.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            if (!manualModel.AddWire(manualSource.InstanceId, "positive", manualResistor.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints))) throw new InvalidOperationException("Workspace did not retain a manual wire route.");
            var manualWire = manualModel.Wires[0];
            if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual || manualWire.VisualState.Waypoints.Count != 3 || manualWire.VisualState.Waypoints[1] != waypoints[1]) throw new InvalidOperationException("Manual waypoint data was not preserved in workspace-local coordinates.");
            var route = SpiceWorkspaceOrthogonalRoute.Build(Vector2.zero, new Vector2(140f, 20f), Vector2.right, manualWire.VisualState);
            for (var index = 1; index < route.Count; index++)
            {
                var delta = route[index] - route[index - 1];
                if (Math.Abs(delta.x) > 0.01f && Math.Abs(delta.y) > 0.01f) throw new InvalidOperationException("Manual wire route produced a diagonal segment.");
            }
            var autoRoute = SpiceWorkspaceOrthogonalRoute.Build(Vector2.zero, new Vector2(140f, 20f), Vector2.right, SpiceWireVisualState.Auto());
            if (autoRoute.Count != 3 || Math.Abs(autoRoute[1].y) > 0.01f || Math.Abs(autoRoute[1].x - 140f) > 0.01f) throw new InvalidOperationException("Auto routing no longer uses the default horizontal-first elbow.");

            var generatedNetlist = "V1 n001 0 DC 10\nR1 n001 0 1000";
            var netlistResult = new SpiceSimulationResult { GeneratedNetlistContent = generatedNetlist };
            if (netlistResult.Netlist != generatedNetlist) throw new InvalidOperationException("Generated netlist compatibility alias no longer exposes the submitted content.");

            var bypassCircuit = new SpiceCircuitModel();
            bypassCircuit.Components.Add(SpiceComponentModel.DcVoltageSource("bypass-source", 10d));
            bypassCircuit.Components.Add(SpiceComponentModel.Ground("bypass-ground"));
            bypassCircuit.Wires.Add(new SpiceWireModel(
                new SpiceTerminalRef("bypass-source", SpiceComponentModel.PositiveTerminalId),
                new SpiceTerminalRef("bypass-source", SpiceComponentModel.NegativeTerminalId)));
            var bypassGraph = SpiceCircuitGraphBuilder.Build(bypassCircuit);
            if (!bypassGraph.Diagnostics.Exists(diagnostic => diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION")) throw new InvalidOperationException("Topology validation did not report a bypassed same-component wire.");
            var changes = 0;
            model.Changed += _ => changes++;
            model.MoveComponent(resistor.InstanceId, new Vector2(10f, 20f));
            if (changes != 0) throw new InvalidOperationException("Pure view movement changed the electrical result version.");
            if (!model.RemoveComponent(resistor.InstanceId) || model.Wires.Count != 1) throw new InvalidOperationException("Deleting a component did not remove all associated wires.");
        }

        public static void ConnectSingleResistor(SpiceWorkspaceController workspace, string source, string resistor, string ground)
        {
            if (!workspace.Connect(source, "positive", resistor, "positive") ||
                !workspace.Connect(source, "negative", ground, "ground") ||
                !workspace.Connect(resistor, "negative", ground, "ground")) throw new InvalidOperationException("Unable to construct the single-resistor T3 topology.");
        }
    }
}
