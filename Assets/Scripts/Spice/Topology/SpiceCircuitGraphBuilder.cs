using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Spice.Core;

namespace ElectricalSim.Spice.Topology
{
    /// <summary>
    /// 每次计算都从当前 Wire 集合建立新的连通分量，避免删除导线后复用陈旧 nodeId。
    /// 所有 Ground 端子在图内合并到 SPICE 节点 0。
    /// </summary>
    public static class SpiceCircuitGraphBuilder
    {
        public static SpiceCircuitGraph Build(SpiceCircuitModel circuit)
        {
            var graph = new SpiceCircuitGraph();
            if (circuit == null || circuit.Components.Count == 0)
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_EMPTY_CIRCUIT", SpiceDiagnosticSeverity.Error, "The circuit contains no components."));
                return graph;
            }

            var components = new Dictionary<string, SpiceComponentModel>(StringComparer.Ordinal);
            for (var i = 0; i < circuit.Components.Count; i++)
            {
                var component = circuit.Components[i];
                if (component == null || string.IsNullOrWhiteSpace(component.InstanceId))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_COMPONENT", SpiceDiagnosticSeverity.Error, "A component is missing its instanceId."));
                    continue;
                }

                if (components.ContainsKey(component.InstanceId))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_DUPLICATE_INSTANCE", SpiceDiagnosticSeverity.Error, "Duplicate component instanceId.", component.InstanceId));
                    continue;
                }

                components.Add(component.InstanceId, component);
            }

            var terminals = new List<SpiceTerminalRef>();
            foreach (var component in components.Values.OrderBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.Ground)
                {
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.GroundTerminalId));
                }
                else
                {
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId));
                    terminals.Add(new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId));
                }
            }

            var indexByTerminal = new Dictionary<SpiceTerminalRef, int>();
            for (var i = 0; i < terminals.Count; i++) indexByTerminal[terminals[i]] = i;
            // Union-Find 只描述本次构图的端子连通关系，不向元件模型写入节点状态。
            var unionFind = new UnionFind(terminals.Count);
            var connectionCount = terminals.ToDictionary(terminal => terminal, terminal => 0);

            var grounds = components.Values.Where(component => component.Kind == SpiceComponentKind.Ground).OrderBy(component => component.InstanceId, StringComparer.Ordinal).ToList();
            if (grounds.Count == 0)
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_GROUND_MISSING", SpiceDiagnosticSeverity.Error, "The circuit requires at least one ground component."));
            }
            else
            {
                var firstGround = indexByTerminal[new SpiceTerminalRef(grounds[0].InstanceId, SpiceComponentModel.GroundTerminalId)];
                for (var i = 1; i < grounds.Count; i++) unionFind.Union(firstGround, indexByTerminal[new SpiceTerminalRef(grounds[i].InstanceId, SpiceComponentModel.GroundTerminalId)]);
            }

            foreach (var wire in circuit.Wires)
            {
                if (wire == null || wire.Start == null || wire.End == null)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_WIRE", SpiceDiagnosticSeverity.Error, "A wire endpoint is missing."));
                    continue;
                }

                if (wire.Start.Equals(wire.End))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SELF_CONNECTION", SpiceDiagnosticSeverity.Error, "A terminal cannot connect to itself.", wire.Start.ComponentInstanceId, wire.Start.TerminalId));
                    continue;
                }

                if (string.Equals(wire.Start.ComponentInstanceId, wire.End.ComponentInstanceId, StringComparison.Ordinal))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SAME_COMPONENT_CONNECTION", SpiceDiagnosticSeverity.Error, "A wire cannot directly connect two terminals of the same component.", wire.Start.ComponentInstanceId, wire.Start.TerminalId));
                    continue;
                }

                if (!TryResolveTerminal(components, indexByTerminal, wire.Start, graph) || !TryResolveTerminal(components, indexByTerminal, wire.End, graph)) continue;
                unionFind.Union(indexByTerminal[wire.Start], indexByTerminal[wire.End]);
                connectionCount[wire.Start]++;
                connectionCount[wire.End]++;
            }

            ValidateAnalysisSettings(circuit.AnalysisSettings, components, graph);
            ValidateComponents(components, graph);
            foreach (var terminal in terminals)
            {
                if (connectionCount[terminal] == 0 && components[terminal.ComponentInstanceId].Kind != SpiceComponentKind.Ground)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_FLOATING_TERMINAL", SpiceDiagnosticSeverity.Error, "A component terminal has no wire connection.", terminal.ComponentInstanceId, terminal.TerminalId));
                }
            }
            if (!graph.IsValid) return graph;

            var groundedRoot = unionFind.Find(indexByTerminal[new SpiceTerminalRef(grounds[0].InstanceId, SpiceComponentModel.GroundTerminalId)]);
            var roots = terminals.Select((terminal, index) => new { terminal, root = unionFind.Find(index) })
                .GroupBy(item => item.root)
                .Select(group => new NodeGroup(
                    group.Key,
                    group.Select(item => item.terminal)
                        .OrderBy(terminal => terminal.ComponentInstanceId, StringComparer.Ordinal)
                        .ThenBy(terminal => terminal.TerminalId, StringComparer.Ordinal)
                        .ToList()))
                .OrderBy(group => group.CanonicalTerminal.ComponentInstanceId, StringComparer.Ordinal)
                .ThenBy(group => group.CanonicalTerminal.TerminalId, StringComparer.Ordinal)
                .ToList();
            ValidateGroundReachability(components, indexByTerminal, unionFind, roots, groundedRoot, graph);
            if (!graph.IsValid) return graph;
            var nodeIndex = 1;
            // 端子和分量均按稳定顺序遍历，因此同一结构重复生成时节点名称保持确定。
            foreach (var root in roots)
            {
                var node = root.Root == groundedRoot ? "0" : "n" + nodeIndex++.ToString("D3");
                foreach (var terminal in root.Terminals) graph.NodeByTerminal[terminal] = node;
            }

            // 内部 SPICE 名称与用户 instanceId 分离，以避免中文、空格或特殊字符进入网表。
            // 电压探针不参与网表，不分配 SPICE 名称，也不参与同节点短路诊断（两端同节点是正常的 0V 测量场景）。
            foreach (var component in components.Values.OrderBy(component => component.Kind).ThenBy(component => component.InstanceId, StringComparer.Ordinal))
            {
                if (component.Kind == SpiceComponentKind.Ground || component.Kind == SpiceComponentKind.VoltageProbe) continue;
                var prefix = GetPrefix(component.Kind);
                var number = graph.SpiceNameByComponentId.Count(existing => HasSpicePrefix(existing.Value, prefix)) + 1;
                var name = prefix + number;
                graph.SpiceNameByComponentId[component.InstanceId] = name;
                graph.ComponentIdBySpiceName[name] = component.InstanceId;

                var positive = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negative = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                if (positive == negative && IsIdealVoltageConstraint(component.Kind))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SOURCE_SHORTED", SpiceDiagnosticSeverity.Error, "An ideal voltage source or current probe cannot have both terminals on the same node.", component.InstanceId));
                }

                if (positive == negative && !IsIdealVoltageConstraint(component.Kind))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_COMPONENT_SHORTED", SpiceDiagnosticSeverity.Warning, "Both component terminals resolve to the same node.", component.InstanceId));
                }
            }

            ValidateCurrentProbeConstraints(components, graph);

            if (circuit.AnalysisSettings.Mode == SpiceAnalysisMode.AcSingleFrequency &&
                !components.Values.Any(component => component.Kind == SpiceComponentKind.AcVoltageSource))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_SOURCE_MISSING", SpiceDiagnosticSeverity.Error, "Single-frequency AC analysis requires at least one AC voltage source."));
            }
            else if (circuit.AnalysisSettings.Mode == SpiceAnalysisMode.DcOperatingPoint &&
                !components.Values.Any(component => IsIndependentDcSource(component.Kind)))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_SOURCE_MISSING", SpiceDiagnosticSeverity.Error, "The circuit contains no independent DC source."));
            }

            return graph;
        }

        private static void ValidateGroundReachability(
            Dictionary<string, SpiceComponentModel> components,
            Dictionary<SpiceTerminalRef, int> indexByTerminal,
            UnionFind unionFind,
            IReadOnlyList<NodeGroup> roots,
            int groundedRoot,
            SpiceCircuitGraph graph)
        {
            // Union-Find represents wire-only electrical nodes. Components are node-graph edges,
            // so traversal from the ground root can reject a complete but ungrounded subcircuit.
            var neighbors = roots.ToDictionary(group => group.Root, group => new HashSet<int>());
            var componentIdsByRoot = roots.ToDictionary(group => group.Root, group => new HashSet<string>(StringComparer.Ordinal));
            foreach (var component in components.Values.Where(component => component.Kind != SpiceComponentKind.Ground && component.Kind != SpiceComponentKind.VoltageProbe))
            {
                var positiveRoot = unionFind.Find(indexByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)]);
                var negativeRoot = unionFind.Find(indexByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)]);
                neighbors[positiveRoot].Add(negativeRoot);
                neighbors[negativeRoot].Add(positiveRoot);
                componentIdsByRoot[positiveRoot].Add(component.InstanceId);
                componentIdsByRoot[negativeRoot].Add(component.InstanceId);
            }

            var reachable = Traverse(groundedRoot, neighbors, new HashSet<int>());
            var inspected = new HashSet<int>(reachable);
            foreach (var root in roots)
            {
                if (inspected.Contains(root.Root)) continue;
                var floatingRegion = Traverse(root.Root, neighbors, new HashSet<int>());
                inspected.UnionWith(floatingRegion);
                var representativeComponentId = floatingRegion
                    .SelectMany(regionRoot => componentIdsByRoot[regionRoot])
                    .OrderBy(componentId => componentId, StringComparer.Ordinal)
                    .First();
                graph.Diagnostics.Add(new SpiceDiagnostic(
                    "SPICE_FLOATING_SUBCIRCUIT",
                    SpiceDiagnosticSeverity.Error,
                    "Circuit subnetwork has no path to the ground reference.",
                    representativeComponentId));
            }
        }

        private static HashSet<int> Traverse(int start, Dictionary<int, HashSet<int>> neighbors, HashSet<int> visited)
        {
            var pending = new Queue<int>();
            pending.Enqueue(start);
            visited.Add(start);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var neighbor in neighbors[current])
                {
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
                }
            }
            return visited;
        }

        private static bool TryResolveTerminal(Dictionary<string, SpiceComponentModel> components, Dictionary<SpiceTerminalRef, int> indexes, SpiceTerminalRef terminal, SpiceCircuitGraph graph)
        {
            if (!components.TryGetValue(terminal.ComponentInstanceId ?? string.Empty, out var component))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_WIRE_COMPONENT_MISSING", SpiceDiagnosticSeverity.Error, "Wire references a missing component.", terminal.ComponentInstanceId, terminal.TerminalId));
                return false;
            }
            if (!component.HasTerminal(terminal.TerminalId) || !indexes.ContainsKey(terminal))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_WIRE_TERMINAL_MISSING", SpiceDiagnosticSeverity.Error, "Wire references a missing terminal.", terminal.ComponentInstanceId, terminal.TerminalId));
                return false;
            }
            return true;
        }

        private static void ValidateComponents(Dictionary<string, SpiceComponentModel> components, SpiceCircuitGraph graph)
        {
            foreach (var component in components.Values)
            {
                SpiceParameterKey? key = component.Kind == SpiceComponentKind.DcVoltageSource ? SpiceParameterKey.DcVoltage :
                    component.Kind == SpiceComponentKind.AcVoltageSource ? SpiceParameterKey.AcMagnitude :
                    component.Kind == SpiceComponentKind.DcCurrentSource ? SpiceParameterKey.DcCurrent :
                    component.Kind == SpiceComponentKind.IdealSwitch ? SpiceParameterKey.SwitchClosed :
                    component.Kind == SpiceComponentKind.Resistor ? SpiceParameterKey.Resistance :
                    component.Kind == SpiceComponentKind.Capacitor ? SpiceParameterKey.Capacitance :
                    component.Kind == SpiceComponentKind.Inductor ? SpiceParameterKey.Inductance : (SpiceParameterKey?)null;
                if (!key.HasValue) continue;
                var validValue = component.TryGetParameter(key.Value, out var value) &&
                    !double.IsNaN(value) && !double.IsInfinity(value) &&
                    (key.Value == SpiceParameterKey.SwitchClosed ? value == 0d || value == 1d :
                     key.Value == SpiceParameterKey.DcVoltage || key.Value == SpiceParameterKey.DcCurrent ? true :
                     key.Value == SpiceParameterKey.AcMagnitude ? SpiceAnalysisLimits.IsValidAcMagnitude(value) : value > 0d);
                if (!validValue)
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_INVALID_PARAMETER", SpiceDiagnosticSeverity.Error, "Component parameter is missing or outside the supported range.", component.InstanceId));
                }
                if (component.Kind == SpiceComponentKind.AcVoltageSource &&
                    (!SpiceAnalysisLimits.IsFinite(component.AcPhaseDegrees) || component.AcPhaseDegrees < -180d || component.AcPhaseDegrees >= 180d))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_SOURCE_PHASE_INVALID", SpiceDiagnosticSeverity.Error, "AC voltage source phase must be finite and normalized to [-180, 180) degrees.", component.InstanceId));
                }
            }
        }

        private static void ValidateAnalysisSettings(SpiceAnalysisSettings settings, Dictionary<string, SpiceComponentModel> components, SpiceCircuitGraph graph)
        {
            if (settings == null || !Enum.IsDefined(typeof(SpiceAnalysisMode), settings.Mode))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_ANALYSIS_MODE_INVALID", SpiceDiagnosticSeverity.Error, "The selected analysis mode is invalid."));
                return;
            }

            if (settings.Mode == SpiceAnalysisMode.AcSingleFrequency && !SpiceAnalysisLimits.IsValidFrequency(settings.FrequencyHz))
            {
                graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_AC_FREQUENCY_INVALID", SpiceDiagnosticSeverity.Error, "Single-frequency AC analysis requires a finite frequency within the supported range."));
            }

            foreach (var component in components.Values)
            {
                if (IsSupportedForAnalysis(component.Kind, settings.Mode)) continue;
                var code = settings.Mode == SpiceAnalysisMode.AcSingleFrequency
                    ? "SPICE_AC_COMPONENT_UNSUPPORTED"
                    : "SPICE_DC_COMPONENT_UNSUPPORTED";
                graph.Diagnostics.Add(new SpiceDiagnostic(code, SpiceDiagnosticSeverity.Error,
                    "Component is not supported by the selected analysis mode: " + component.Kind + ".", component.InstanceId));
            }
        }

        private static string GetPrefix(SpiceComponentKind kind)
        {
            switch (kind)
            {
                case SpiceComponentKind.DcVoltageSource: return "V";
                case SpiceComponentKind.AcVoltageSource: return "V";
                case SpiceComponentKind.DcCurrentSource: return "I";
                case SpiceComponentKind.IdealSwitch: return "SW";
                case SpiceComponentKind.SiliconDiode: return "D";
                case SpiceComponentKind.Resistor: return "R";
                case SpiceComponentKind.Capacitor: return "C";
                case SpiceComponentKind.Inductor: return "L";
                case SpiceComponentKind.CurrentProbe: return "VPROBE";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static bool HasSpicePrefix(string name, string prefix)
        {
            return !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(prefix) &&
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length &&
                char.IsDigit(name[prefix.Length]);
        }

        private static void ValidateCurrentProbeConstraints(Dictionary<string, SpiceComponentModel> components, SpiceCircuitGraph graph)
        {
            var constrainedByNodePair = new Dictionary<string, List<SpiceComponentModel>>(StringComparer.Ordinal);
            foreach (var component in components.Values.Where(component => IsIdealVoltageConstraint(component.Kind)))
            {
                var positive = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.PositiveTerminalId)];
                var negative = graph.NodeByTerminal[new SpiceTerminalRef(component.InstanceId, SpiceComponentModel.NegativeTerminalId)];
                var pair = string.CompareOrdinal(positive, negative) <= 0 ? positive + "|" + negative : negative + "|" + positive;
                if (!constrainedByNodePair.TryGetValue(pair, out var constrained))
                {
                    constrained = new List<SpiceComponentModel>();
                    constrainedByNodePair.Add(pair, constrained);
                }
                constrained.Add(component);
            }

            foreach (var constrained in constrainedByNodePair.Values.Where(group => group.Count > 1 && group.Any(component => component.Kind == SpiceComponentKind.CurrentProbe)))
            {
                foreach (var probe in constrained.Where(component => component.Kind == SpiceComponentKind.CurrentProbe))
                {
                    graph.Diagnostics.Add(new SpiceDiagnostic("SPICE_CURRENT_PROBE_CONSTRAINT_CONFLICT", SpiceDiagnosticSeverity.Error, "A current probe cannot be placed in parallel with an ideal voltage constraint.", probe.InstanceId));
                }
            }
        }

        private static bool IsIndependentDcSource(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource || kind == SpiceComponentKind.DcCurrentSource;
        }

        private static bool IsIdealVoltageConstraint(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource || kind == SpiceComponentKind.AcVoltageSource || kind == SpiceComponentKind.CurrentProbe;
        }

        private static bool IsSupportedForAnalysis(SpiceComponentKind kind, SpiceAnalysisMode mode)
        {
            if (mode == SpiceAnalysisMode.DcOperatingPoint) return kind != SpiceComponentKind.AcVoltageSource;
            return kind == SpiceComponentKind.AcVoltageSource || kind == SpiceComponentKind.Resistor ||
                kind == SpiceComponentKind.Capacitor || kind == SpiceComponentKind.Inductor ||
                kind == SpiceComponentKind.Ground || kind == SpiceComponentKind.IdealSwitch ||
                kind == SpiceComponentKind.VoltageProbe || kind == SpiceComponentKind.CurrentProbe;
        }

        private sealed class NodeGroup
        {
            public NodeGroup(int root, List<SpiceTerminalRef> terminals)
            {
                Root = root;
                Terminals = terminals;
            }

            public int Root { get; }
            public List<SpiceTerminalRef> Terminals { get; }
            public SpiceTerminalRef CanonicalTerminal => Terminals[0];
        }

        private sealed class UnionFind
        {
            private readonly int[] parent;
            public UnionFind(int count) { parent = Enumerable.Range(0, count).ToArray(); }
            public int Find(int value) { while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; } return value; }
            public void Union(int left, int right) { left = Find(left); right = Find(right); if (left != right) parent[right] = left; }
        }
    }
}
