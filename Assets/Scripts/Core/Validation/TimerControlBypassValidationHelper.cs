using System;
using System.Collections.Generic;

namespace ElectricalSim.Core.Validation
{
    /// <summary>
    /// 使用静态连通性与 Analyzer 证据检测时间继电器控制旁路接线。
    /// 仅报告既有的 TIMER_CONTROL_BYPASSED 契约；不得修改 KT 计时状态、规则严重级别
    /// 或教学输出格式。
    /// </summary>
    // KT 旁路检查识别延时触点参与的静态控制依赖是否被直接外部路径绕开。它验证的是教学控制意图，
    // 不是计时器当前已经到时的运行态；运行状态仍由 RuntimeStateManager 与 Analyzer 管理。
    internal sealed class TimerControlBypassValidationHelper
    {
        private const string TimerControlBypassed = "TIMER_CONTROL_BYPASSED";

        private readonly IReadOnlyList<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly CircuitStateResult analysisResult;
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public TimerControlBypassValidationHelper(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            CircuitStateResult analysisResult)
        {
            this.components = components;
            this.wires = wires;
            this.analysisResult = analysisResult;
            BuildStaticWireGraph();
        }

        // 先建立静态 Wire 图，再对支持的 KT/接触器组合验证控制路径，避免把动态 KT 触点边作为旁路前提。
        public List<CircuitValidationIssue> Validate()
        {
            // 仅在静态图存在直接旁路候选，且 Analyzer 确认 KT 正处于计时、延时触点尚未完成而下游线圈已经得电时检查。
            // 静态存在其他控制支路不等于错误；运行态证据只用于本次判定，不能改变 Wire topology。
            var issues = new List<CircuitValidationIssue>();
            AddTimerControlBypassedIssues(issues);
            return issues;
        }

        // 仅当 KT 的 NO 延时触点被用于特定接触器控制意图、且存在绕开该依赖的直接静态路径时才报告。
        // 不能把任意 KT 触点接线或当前计时状态误分类为旁路。
        private void AddTimerControlBypassedIssues(List<CircuitValidationIssue> issues)
        {
            if (components == null || analysisResult == null || analysisResult.Components == null)
            {
                return;
            }

            for (var i = 0; i < components.Count; i++)
            {
                var timer = components[i];
                if (!IsSupportedOnDelayTimer(timer) || !IsTimerTiming(timer))
                {
                    continue;
                }

                for (var contactorIndex = 0; contactorIndex < components.Count; contactorIndex++)
                {
                    var contactor = components[contactorIndex];
                    if (!IsSupportedContactor(contactor) ||
                        !IsTimerNoContactAssignedToContactor(timer, contactor) ||
                        !IsControlCoilEnergized(contactor))
                    {
                        continue;
                    }

                    AddIssue(
                        issues,
                        TimerControlBypassed,
                        CircuitValidationSeverity.Error,
                        CircuitValidationCategory.ControlCircuit,
                        "时间继电器控制被旁路",
                        "检测到时间继电器控制可能被旁路。当前 KT 延时触点尚未闭合，但下游接触器已经提前得电，可能存在跨接 KT 触点或绕过延时控制的接线。请检查 KT 15/18 延时常开触点及下游 KM 线圈回路。",
                        timer,
                        TerminalConstants.TimerCommon15,
                        TerminalConstants.TimerNO18,
                        TerminalConstants.A1,
                        TerminalConstants.A2);
                }
            }
        }

        private bool IsTimerTiming(CircuitComponent timer)
        {
            // 计时状态由 Analyzer/RuntimeStateManager 维护，不能反向读取 KT 显示文本或视觉倒计时来改变规则结论。
            var info = FindComponentInfo(timer != null ? timer.InstanceId : null);
            return info != null &&
                info.IsTimerRelay &&
                info.IsOnDelayTimerRelay &&
                info.IsTimerRelayCoilEnergizedByAnalyzer &&
                !info.IsTimerDelayElapsed &&
                string.Equals(info.TimerDelayStatus, "Timing", StringComparison.OrdinalIgnoreCase);
        }

        // 归属判定连接“哪个 KT NO 触点”和“哪个线圈控制支路”，避免多个 KT/KM 同时存在时跨实例借用触点。
        private bool IsTimerNoContactAssignedToContactor(CircuitComponent timer, CircuitComponent contactor)
        {
            if (timer == null || contactor == null)
            {
                return false;
            }

            var timerNo = timer.GetTerminal(TerminalConstants.TimerNO18);
            if (timerNo == null)
            {
                return false;
            }

            return AreConnectedByStaticWires(timerNo, contactor.GetTerminal(TerminalConstants.A1)) ||
                AreConnectedByStaticWires(timerNo, contactor.GetTerminal(TerminalConstants.A2)) ||
                IsTimerNoBeforeStartButton(timerNo, contactor);
        }

        // 启动按钮前的 KT NO 触点仍属于控制依赖的一部分；检查采用静态节点关系而不要求触点当前闭合，
        // 否则延时尚未到期时会把正确教学接线错误地判成无关联。
        private bool IsTimerNoBeforeStartButton(TerminalView timerNo, CircuitComponent contactor)
        {
            if (timerNo == null || contactor == null || components == null)
            {
                return false;
            }

            var a1 = contactor.GetTerminal(TerminalConstants.A1);
            var a2 = contactor.GetTerminal(TerminalConstants.A2);
            for (var i = 0; i < components.Count; i++)
            {
                var button = components[i];
                if (button == null ||
                    button.Definition == null ||
                    button.Definition.kind != ComponentKind.PushButton ||
                    button.GetTerminal("23") == null ||
                    button.GetTerminal("24") == null)
                {
                    continue;
                }

                var no23 = button.GetTerminal("23");
                var no24 = button.GetTerminal("24");
                if ((AreConnectedByStaticWires(timerNo, no23) &&
                     (AreConnectedByStaticWires(no24, a1) || AreConnectedByStaticWires(no24, a2))) ||
                    (AreConnectedByStaticWires(timerNo, no24) &&
                     (AreConnectedByStaticWires(no23, a1) || AreConnectedByStaticWires(no23, a2))))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsControlCoilEnergized(CircuitComponent component)
        {
            var info = FindComponentInfo(component != null ? component.InstanceId : null);
            if (info != null)
            {
                if (info.IsContactor && info.IsContactorCoilEnergizedByAnalyzer)
                {
                    return true;
                }

                if (string.Equals(info.CoilStatus, "CoilEnergized", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return component != null && component.IsEnergized;
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

        // 查询仅沿真实外部 Wire；KT 内部延时接点与接触器内部触点不进入图，避免用动态导通证明静态旁路。
        private bool AreConnectedByStaticWires(TerminalView first, TerminalView second)
        {
            if (first == null || second == null)
            {
                return false;
            }

            if (first == second)
            {
                return true;
            }

            var visited = new HashSet<TerminalView>();
            var queue = new Queue<TerminalView>();
            var traversalSteps = 0;
            var visitedEdges = 0;
            visited.Add(first);
            queue.Enqueue(first);

            while (queue.Count > 0)
            {
                traversalSteps++;
                if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                {
                    MarkTraversalLimitExceeded("TimerControlBypassValidationHelper.AreConnectedByStaticWires");
                    return false;
                }

                var current = queue.Dequeue();
                if (!staticWireGraph.TryGetValue(current, out var next))
                {
                    continue;
                }

                foreach (var terminal in next)
                {
                    visitedEdges++;
                    if (TopologyTraversalLimits.IsTraversalBudgetExceeded(traversalSteps, visited.Count, visitedEdges))
                    {
                        MarkTraversalLimitExceeded("TimerControlBypassValidationHelper.AreConnectedByStaticWires.edges");
                        return false;
                    }

                    if (terminal == second)
                    {
                        return true;
                    }

                    if (visited.Add(terminal))
                    {
                        queue.Enqueue(terminal);
                    }
                }
            }

            return false;
        }

        // 静态图的边严格来自真实外部 Wire；这保证“直接路径”表示学生实际接线，而非元件内部导通。
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

        private static bool IsSupportedOnDelayTimer(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.ContactorCoil &&
                component.Definition.name != null &&
                component.Definition.name.IndexOf("Timer_OnDelay", StringComparison.OrdinalIgnoreCase) >= 0 &&
                component.GetTerminal(TerminalConstants.A1) != null &&
                component.GetTerminal(TerminalConstants.A2) != null &&
                component.GetTerminal(TerminalConstants.TimerCommon15) != null &&
                component.GetTerminal(TerminalConstants.TimerNO18) != null;
        }

        private static bool IsSupportedContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase);
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
            if (issues == null || HasIssue(issues, ruleId, component))
            {
                return;
            }

            var issue = new CircuitValidationIssue
            {
                RuleId = ruleId,
                Severity = severity,
                Category = category,
                Title = title,
                Message = message,
                Component = component
            };

            if (relatedTerminals != null)
            {
                for (var i = 0; i < relatedTerminals.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(relatedTerminals[i]) && !issue.RelatedTerminals.Contains(relatedTerminals[i]))
                    {
                        issue.RelatedTerminals.Add(relatedTerminals[i]);
                    }
                }
            }

            issues.Add(issue);
        }

        private static bool HasIssue(List<CircuitValidationIssue> issues, string ruleId, CircuitComponent component)
        {
            var instanceId = component != null ? component.InstanceId : string.Empty;
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                var issueInstanceId = issue != null && issue.Component != null ? issue.Component.InstanceId : string.Empty;
                if (issue != null &&
                    string.Equals(issue.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(issueInstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkTraversalLimitExceeded(string context)
        {
            if (!HasTraversalLimitExceeded)
            {
                TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
            }

            HasTraversalLimitExceeded = true;
        }
    }
}
