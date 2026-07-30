using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Netlist
{
    public enum SpiceAcOutputKind { NodeVoltage, BranchCurrent }

    public sealed class SpiceAcOutputRequest
    {
        public SpiceAcOutputRequest(SpiceAcOutputKind kind, string expression, string resultKey)
        {
            Kind = kind;
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            ResultKey = resultKey ?? throw new ArgumentNullException(nameof(resultKey));
        }

        public SpiceAcOutputKind Kind { get; }
        public string Expression { get; }
        public string ResultKey { get; }
    }

    public sealed class SpiceAcNetlistDocument
    {
        public string Content { get; set; }
        public IReadOnlyList<SpiceAcOutputRequest> OutputRequests { get; set; }
    }

    /// <summary>
    /// Builds a deterministic, one-point ngspice AC netlist. DC netlist generation remains isolated.
    /// </summary>
    public static class SpiceAcNetlistBuilder
    {
        public const string BeginMarker = "__SPICE_AC_B2_BEGIN__";
        public const string EndMarker = "__SPICE_AC_B2_END__";

        public static SpiceAcNetlistDocument Build(SpiceCircuitModel circuit, SpiceCircuitGraph graph)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            if (circuit.AnalysisSettings == null || circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
                throw new InvalidOperationException("Single-frequency AC analysis settings are required.");
            if (graph == null || !graph.IsValid)
                throw new InvalidOperationException("A valid SpiceCircuitGraph is required before generating an AC netlist.");

            var componentById = circuit.Components.ToDictionary(component => component.InstanceId, StringComparer.Ordinal);
            var ordered = graph.SpiceNameByComponentId.OrderBy(pair => pair.Value, StringComparer.Ordinal).ToList();
            var builder = new StringBuilder("* ElectricalSimulation2D SPICE Single-Frequency AC\n");
            foreach (var pair in ordered)
            {
                var component = componentById[pair.Key];
                if (component.Kind == SpiceComponentKind.IdealOperationalAmplifier)
                {
                    AppendIdealOperationalAmplifierLine(builder, component, pair.Value, graph);
                    continue;
                }
                var positive = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negative = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                AppendComponentLine(builder, component, pair.Value, positive, negative);
            }

            var requests = new List<SpiceAcOutputRequest>();
            var nodes = graph.NodeByTerminal.Values
                .Where(node => node != "0")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(node => node, StringComparer.Ordinal)
                .ToList();
            foreach (var node in nodes)
                requests.Add(new SpiceAcOutputRequest(SpiceAcOutputKind.NodeVoltage, "v(" + node + ")", node));

            var branchNames = ordered
                .Where(pair => componentById[pair.Key].Kind == SpiceComponentKind.AcVoltageSource ||
                               componentById[pair.Key].Kind == SpiceComponentKind.CurrentProbe ||
                               componentById[pair.Key].Kind == SpiceComponentKind.IdealOperationalAmplifier)
                .Select(pair => pair.Value)
                .ToList();
            foreach (var branch in branchNames)
                requests.Add(new SpiceAcOutputRequest(SpiceAcOutputKind.BranchCurrent, "i(" + branch + ")", branch));

            var frequency = circuit.AnalysisSettings.FrequencyHz.ToString("R", CultureInfo.InvariantCulture);
            builder.AppendLine().AppendLine(".control").AppendLine("set noaskquit")
                .AppendLine("ac lin 1 " + frequency + " " + frequency)
                .AppendLine("echo " + BeginMarker);
            foreach (var request in requests) builder.AppendLine("print " + request.Expression);
            builder.AppendLine("echo " + EndMarker).AppendLine("quit").AppendLine(".endc").AppendLine().AppendLine(".end");

            return new SpiceAcNetlistDocument { Content = builder.ToString(), OutputRequests = requests };
        }

        private static void AppendComponentLine(StringBuilder builder, SpiceComponentModel component, string graphName, string positive, string negative)
        {
            switch (component.Kind)
            {
                case SpiceComponentKind.AcVoltageSource:
                    builder.Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(" AC ")
                        .Append(component.GetRequiredParameter(SpiceParameterKey.AcMagnitude).ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                        .Append(component.AcPhaseDegrees.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    return;
                case SpiceComponentKind.CurrentProbe:
                    builder.Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(" 0").AppendLine();
                    return;
                case SpiceComponentKind.IdealSwitch:
                    builder.Append('R').Append(graphName).Append(' ').Append(positive).Append(' ').Append(negative).Append(' ')
                        .Append(SpiceNetlistBuilder.SwitchResistance(component).ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                    return;
                case SpiceComponentKind.Resistor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Resistance));
                    return;
                case SpiceComponentKind.Capacitor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Capacitance));
                    return;
                case SpiceComponentKind.Inductor:
                    AppendValueLine(builder, graphName, positive, negative, component.GetRequiredParameter(SpiceParameterKey.Inductance));
                    return;
                default:
                    throw new InvalidOperationException("Unsupported component reached the AC netlist builder: " + component.Kind + ".");
            }
        }

        private static void AppendIdealOperationalAmplifierLine(StringBuilder builder, SpiceComponentModel component, string graphName, SpiceCircuitGraph graph)
        {
            // DC 和单频 AC 共用同一线性 VCVS：频率响应完全由外部 R/C/L 决定，固定增益模型不暗含带宽、饱和或电源轨。
            var output = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.OutputTerminalId)];
            var nonInverting = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NonInvertingTerminalId)];
            var inverting = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.InvertingTerminalId)];
            builder.Append(graphName).Append(' ').Append(output).Append(" 0 ").Append(nonInverting).Append(' ').Append(inverting).Append(' ')
                .Append(SpiceComponentDefaults.IdealOperationalAmplifierOpenLoopGain.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }

        private static void AppendValueLine(StringBuilder builder, string name, string positive, string negative, double value)
        {
            builder.Append(name).Append(' ').Append(positive).Append(' ').Append(negative).Append(' ')
                .Append(value.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
        }
    }
}
