using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    /// <summary>
    /// 校验活动图中的危险电位组合和线圈电压兼容性。
    /// 它是规则证据 Helper，不是仿真引擎或 UI 格式化器；有界静态图可避免把任意场景对象
    /// 误当作电气连接。
    /// </summary>
    internal sealed class PowerPotentialValidationHelper
    {
        private const string PowerPotentialConflict = "POWER_POTENTIAL_CONFLICT";
        private const string LiveToPeFault = "LIVE_TO_PE_FAULT";
        private const string NeutralPeMisuse = "NEUTRAL_PE_MISUSE";
        private const string CoilVoltageMismatch = "COIL_VOLTAGE_MISMATCH";

        private readonly IReadOnlyList<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly CircuitStateResult analysisResult;
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public PowerPotentialValidationHelper(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            this.components = components;
            this.wires = wires;
            this.analysisResult = analysisResult;
            BuildStaticWireGraph();
        }

        public List<CircuitValidationIssue> Validate()
        {
            var issues = new List<CircuitValidationIssue>();
            AddDirectPowerPotentialIssues(issues);
            AddCoilVoltageMismatchIssues(issues);
            return issues;
        }

        private void AddDirectPowerPotentialIssues(List<CircuitValidationIssue> issues)
        {
            var visited = new HashSet<TerminalView>();
            foreach (var start in staticWireGraph.Keys)
            {
                if (start == null || visited.Contains(start))
                {
                    continue;
                }

                var group = FloodStaticWireGroup(start, visited);
                if (group.Count <= 1)
                {
                    continue;
                }

                EvaluatePotentialGroup(issues, group);
            }
        }

        private void EvaluatePotentialGroup(List<CircuitValidationIssue> issues, List<TerminalView> group)
        {
            var potentials = new Dictionary<string, TerminalView>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < group.Count; i++)
            {
                var terminal = group[i];
                var potential = ResolvePowerOutputPotential(terminal);
                if (string.IsNullOrWhiteSpace(potential) || potentials.ContainsKey(potential))
                {
                    continue;
                }

                potentials.Add(potential, terminal);
            }

            if (potentials.Count < 2)
            {
                return;
            }

            var hasLive = HasAnyLive(potentials);
            var hasPe = potentials.ContainsKey(TerminalConstants.PE);
            var hasNeutral = potentials.ContainsKey(TerminalConstants.N);

            if (hasLive && HasPowerPotentialConflict(potentials))
            {
                AddIssue(
                    issues,
                    PowerPotentialConflict,
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.PowerSafety,
                    "电源电位冲突",
                    "检测到电源不同电位端子被直接连接，存在短路或电源冲突风险。请检查 L/N 或 L1/L2/L3/N 之间是否被导线直接短接。",
                    FirstTerminalOwner(potentials),
                    potentials.Keys);
            }

            if (hasLive && hasPe)
            {
                AddIssue(
                    issues,
                    LiveToPeFault,
                    CircuitValidationSeverity.Error,
                    CircuitValidationCategory.GroundSafety,
                    "相线与保护地短接",
                    "检测到火线或相线与保护地 PE 被直接连接，存在严重安全风险。请检查 PE 是否被错误接入带电相线。",
                    FirstTerminalOwner(potentials),
                    potentials.Keys);
            }

            if (hasNeutral && hasPe)
            {
                AddIssue(
                    issues,
                    NeutralPeMisuse,
                    CircuitValidationSeverity.Warning,
                    CircuitValidationCategory.GroundSafety,
                    "零线与保护地混接",
                    "检测到零线 N 与保护地 PE 被直接连接。当前教学场景下不建议在负载回路中随意短接 N 与 PE，请检查接线是否符合练习目标。",
                    FirstTerminalOwner(potentials),
                    potentials.Keys);
            }
        }

        private void AddCoilVoltageMismatchIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null || analysisResult == null || analysisResult.Components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!IsSupportedControlCoil(component))
                {
                    continue;
                }

                var ratedVoltage = ResolveRatedCoilVoltage(component);
                if (ratedVoltage <= 0f)
                {
                    continue;
                }

                var info = FindComponentInfo(component.InstanceId);
                if (info == null ||
                    !info.TerminalVoltages.TryGetValue(TerminalConstants.A1, out var a1) ||
                    !info.TerminalVoltages.TryGetValue(TerminalConstants.A2, out var a2))
                {
                    continue;
                }

                var actual = ActualSupplyVoltageResolver.ResolveAcrossVoltageLabels(components, a1, a2);
                if (!actual.Resolved)
                {
                    continue;
                }

                if (ratedVoltage < 300f && actual.Kind == ActualSupplyVoltageKind.ThreePhaseLine)
                {
                    AddCoilMismatchIssue(
                        issues,
                        component,
                        CircuitValidationSeverity.Error,
                        ratedVoltage,
                        actual);
                }
                else if (ratedVoltage >= 300f && actual.Kind == ActualSupplyVoltageKind.SinglePhase)
                {
                    AddCoilMismatchIssue(
                        issues,
                        component,
                        CircuitValidationSeverity.Warning,
                        ratedVoltage,
                        actual);
                }
            }
        }

        private void AddCoilMismatchIssue(
            List<CircuitValidationIssue> issues,
            CircuitComponent component,
            CircuitValidationSeverity severity,
            float ratedVoltage,
            ActualSupplyVoltageResult actual)
        {
            var actualText = actual.Kind == ActualSupplyVoltageKind.ThreePhaseLine ? "380V 线电压" : "220V 相电压";
            AddIssue(
                issues,
                CoilVoltageMismatch,
                severity,
                CircuitValidationCategory.PowerSafety,
                "线圈电压不匹配",
                "检测到线圈额定电压与接入电源电压不匹配。当前线圈额定为 " +
                    MathfRound(ratedVoltage) + "V，实际识别为 " + actualText + "，请检查 A1/A2 是否接入了正确的控制电压。",
                component,
                TerminalConstants.A1,
                TerminalConstants.A2);
        }

        private List<TerminalView> FloodStaticWireGroup(TerminalView start, HashSet<TerminalView> globalVisited)
        {
            var group = new List<TerminalView>();
            if (start == null || !staticWireGraph.ContainsKey(start))
            {
                return group;
            }

            var queue = new Queue<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            globalVisited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, group.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("PowerPotentialValidationHelper.FloodStaticWireGroup");
                    return group;
                }

                var current = queue.Dequeue();
                group.Add(current);
                if (!staticWireGraph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, group.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("PowerPotentialValidationHelper.FloodStaticWireGroup.edges");
                        return group;
                    }

                    if (terminal != null && globalVisited.Add(terminal))
                    {
                        queue.Enqueue(terminal);
                    }
                }
            }

            return group;
        }

        private void BuildStaticWireGraph()
        {
            staticWireGraph.Clear();
            if (components != null)
            {
                for (var i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    if (component == null)
                    {
                        continue;
                    }

                    foreach (var terminal in component.Terminals)
                    {
                        EnsureStatic(terminal);
                    }
                }
            }

            if (wires == null)
            {
                return;
            }

            for (var i = 0; i < wires.Count; i++)
            {
                var wire = wires[i];
                if (wire != null)
                {
                    ConnectStatic(wire.StartTerminal, wire.EndTerminal);
                }
            }
        }

        private void EnsureStatic(TerminalView terminal)
        {
            if (terminal != null && !staticWireGraph.ContainsKey(terminal))
            {
                staticWireGraph[terminal] = new HashSet<TerminalView>();
            }
        }

        private void ConnectStatic(TerminalView first, TerminalView second)
        {
            if (first == null || second == null)
            {
                return;
            }

            EnsureStatic(first);
            EnsureStatic(second);
            staticWireGraph[first].Add(second);
            staticWireGraph[second].Add(first);
        }

        private static string ResolvePowerOutputPotential(TerminalView terminal)
        {
            if (terminal == null || terminal.Owner == null || terminal.Owner.Definition == null)
            {
                return string.Empty;
            }

            var kind = terminal.Owner.Definition.kind;
            if (kind != ComponentKind.PowerSource && kind != ComponentKind.EnergyMeter)
            {
                return string.Empty;
            }

            var id = terminal.TerminalId ?? string.Empty;
            if (EqualsId(id, TerminalConstants.L) ||
                EqualsId(id, TerminalConstants.L1) ||
                EqualsId(id, TerminalConstants.L2) ||
                EqualsId(id, TerminalConstants.L3) ||
                EqualsId(id, TerminalConstants.N) ||
                EqualsId(id, TerminalConstants.PE))
            {
                return id.ToUpperInvariant();
            }

            if (terminal.Role == TerminalRole.Phase)
            {
                return TerminalConstants.L;
            }

            if (terminal.Role == TerminalRole.Neutral)
            {
                return TerminalConstants.N;
            }

            if (terminal.Role == TerminalRole.ProtectiveEarth)
            {
                return TerminalConstants.PE;
            }

            return string.Empty;
        }

        private static bool HasPowerPotentialConflict(Dictionary<string, TerminalView> potentials)
        {
            var nonPeCount = 0;
            foreach (var potential in potentials.Keys)
            {
                if (!EqualsId(potential, TerminalConstants.PE))
                {
                    nonPeCount++;
                }
            }

            return nonPeCount >= 2;
        }

        private static bool HasAnyLive(Dictionary<string, TerminalView> potentials)
        {
            return potentials.ContainsKey(TerminalConstants.L) ||
                potentials.ContainsKey(TerminalConstants.L1) ||
                potentials.ContainsKey(TerminalConstants.L2) ||
                potentials.ContainsKey(TerminalConstants.L3);
        }

        private static CircuitComponent FirstTerminalOwner(Dictionary<string, TerminalView> potentials)
        {
            foreach (var pair in potentials)
            {
                if (pair.Value != null && pair.Value.Owner != null)
                {
                    return pair.Value.Owner;
                }
            }

            return null;
        }

        private static bool IsSupportedControlCoil(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.GetTerminal(TerminalConstants.A1) != null &&
                component.GetTerminal(TerminalConstants.A2) != null &&
                component.Definition.kind == ComponentKind.ContactorCoil;
        }

        private static float ResolveRatedCoilVoltage(CircuitComponent component)
        {
            var ratedVoltage = ParameterValueResolver.GetFloatOrFallback(
                component,
                0f,
                ParameterKeys.RatedVoltage,
                ParameterAliases.SourceVoltage[0],
                ParameterAliases.SourceVoltage[1]);
            if (ratedVoltage > 0f)
            {
                return ratedVoltage;
            }

            var definitionName = component != null && component.Definition != null ? component.Definition.name ?? string.Empty : string.Empty;
            if (definitionName.IndexOf("220V", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 220f;
            }

            if (definitionName.IndexOf("380V", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 380f;
            }

            return 0f;
        }

        private ComponentStateInfo FindComponentInfo(string instanceId)
        {
            if (analysisResult == null ||
                analysisResult.Components == null ||
                string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            for (var i = 0; i < analysisResult.Components.Count; i++)
            {
                var info = analysisResult.Components[i];
                if (info != null && string.Equals(info.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return null;
        }

        private static void AddIssue(
            List<CircuitValidationIssue> issues,
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component,
            IEnumerable<string> relatedTerminals)
        {
            var issue = CreateIssue(ruleId, severity, category, title, message, component);
            if (relatedTerminals != null)
            {
                foreach (var terminal in relatedTerminals)
                {
                    if (!string.IsNullOrWhiteSpace(terminal) && !issue.RelatedTerminals.Contains(terminal))
                    {
                        issue.RelatedTerminals.Add(terminal);
                    }
                }
            }

            issues.Add(issue);
        }

        private static void AddIssue(
            List<CircuitValidationIssue> issues,
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component,
            params string[] relatedTerminals)
        {
            AddIssue(issues, ruleId, severity, category, title, message, component, (IEnumerable<string>)relatedTerminals);
        }

        private static CircuitValidationIssue CreateIssue(
            string ruleId,
            CircuitValidationSeverity severity,
            CircuitValidationCategory category,
            string title,
            string message,
            CircuitComponent component)
        {
            return new CircuitValidationIssue
            {
                RuleId = ruleId,
                Severity = severity,
                Category = category,
                Title = title,
                Message = message,
                Component = component
            };
        }

        private void MarkTraversalLimitExceeded(string context)
        {
            if (!HasTraversalLimitExceeded)
            {
                TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
            }

            HasTraversalLimitExceeded = true;
        }

        private static bool EqualsId(string first, string second)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }

        private static string MathfRound(float value)
        {
            return ((int)Math.Round(value)).ToString();
        }
    }
}
