using System;
using System.Collections.Generic;
using ElectricalSim.Core;

namespace ElectricalSim.Core.Validation
{
    public sealed class SelfHoldingBranchValidationResult
    {
        public SelfHoldingBranchValidationResult(
            bool isApplicable,
            bool hasSelfHoldAttempt,
            bool isValidSelfHoldBranch,
            CircuitComponent stopButton,
            CircuitComponent startButton,
            CircuitComponent contactor,
            string reason)
        {
            IsApplicable = isApplicable;
            HasSelfHoldAttempt = hasSelfHoldAttempt;
            IsValidSelfHoldBranch = isValidSelfHoldBranch;
            StopButton = stopButton;
            StartButton = startButton;
            Contactor = contactor;
            Reason = reason ?? string.Empty;
        }

        public bool IsApplicable { get; }
        public bool HasSelfHoldAttempt { get; }
        public bool IsValidSelfHoldBranch { get; }
        public CircuitComponent StopButton { get; }
        public CircuitComponent StartButton { get; }
        public CircuitComponent Contactor { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// 在单接触器、单启停按钮的已支持控制结构中，验证 13/14 是否构成启动按钮并联自锁支路。
    /// 它只检查静态接线证据，不判定接触器当前吸合状态；复合按钮、星三角和自锁按钮等复杂结构应保守跳过。
    /// 修改后必须回归点动、连续运行和自锁支路缺失规则测试，避免将合法点动模板误判为故障。
    /// </summary>
    public sealed class SelfHoldingBranchValidationHelper
    {
        private readonly Dictionary<TerminalView, HashSet<TerminalView>> staticWireGraph =
            new Dictionary<TerminalView, HashSet<TerminalView>>();

        public bool HasTraversalLimitExceeded { get; private set; }

        public bool TryEvaluateSingleContactorSelfHold(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires,
            out SelfHoldingBranchValidationResult result)
        {
            // 每次评估都重建当前活动导线图，结果不可跨工作区或模板复用；遍历限制会通过属性交给 Validation Service。
            HasTraversalLimitExceeded = false;
            BuildStaticWireGraph(components, wires);

            if (components == null)
            {
                result = CreateResult(false, false, false, null, null, null, "No components.");
                return false;
            }

            if (HasCompoundPushButton(components) || HasStarDeltaMotor(components) || HasSelfLockingButton(components))
            {
                result = CreateResult(false, false, false, null, null, null, "Complex control structure skipped.");
                return false;
            }

            var stopButton = FindUnique(components, IsPureStopButtonCandidate);
            var startButton = FindUnique(components, IsPureStartButtonCandidate);
            var contactor = FindUnique(components, IsSupportedContactor);
            if (stopButton == null || startButton == null || contactor == null)
            {
                result = CreateResult(false, false, false, stopButton, startButton, contactor, "Single stop/start/contactor structure not found.");
                return false;
            }

            var contactor13 = contactor.GetTerminal(TerminalConstants.AuxNO13);
            var contactor14 = contactor.GetTerminal(TerminalConstants.AuxNO14);
            // 未接出 13/14 说明当前没有尝试自锁，连续运行规则可据此判断；不能直接当作接线错误，
            // 因为点动控制本来就不需要自锁支路。
            var hasSelfHoldAttempt = HasExternalWire(contactor13) || HasExternalWire(contactor14);
            if (!hasSelfHoldAttempt)
            {
                result = CreateResult(true, false, false, stopButton, startButton, contactor, "No 13/14 self-holding branch attempt.");
                return true;
            }

            var start23 = startButton.GetTerminal("23");
            var start24 = startButton.GetTerminal("24");
            var validNormal =
                AreConnectedByStaticWires(contactor13, start23) &&
                AreConnectedByStaticWires(contactor14, start24);
            var validReversed =
                AreConnectedByStaticWires(contactor13, start24) &&
                AreConnectedByStaticWires(contactor14, start23);
            var isValid = validNormal || validReversed;

            result = CreateResult(
                true,
                true,
                isValid,
                stopButton,
                startButton,
                contactor,
                isValid
                    ? "13/14 is wired in parallel with the start button."
                    : "13/14 does not form a valid parallel branch across the start button.");
            return true;
        }

        private void BuildStaticWireGraph(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
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
                if (wire == null)
                {
                    continue;
                }

                ConnectStatic(wire.StartTerminal, wire.EndTerminal);
            }
        }

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
                    MarkTraversalLimitExceeded("SelfHoldingBranchValidationHelper.AreConnectedByStaticWires");
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
                        MarkTraversalLimitExceeded("SelfHoldingBranchValidationHelper.AreConnectedByStaticWires.edges");
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

        private void MarkTraversalLimitExceeded(string context)
        {
            if (!HasTraversalLimitExceeded)
            {
                TopologyTraversalLimits.LogTraversalBudgetExceeded(context);
            }

            HasTraversalLimitExceeded = true;
        }

        private bool HasExternalWire(TerminalView terminal)
        {
            return terminal != null &&
                staticWireGraph.TryGetValue(terminal, out var connected) &&
                connected.Count > 0;
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

        private static CircuitComponent FindUnique(
            IReadOnlyList<CircuitComponent> components,
            Func<CircuitComponent, bool> predicate)
        {
            CircuitComponent result = null;
            var count = 0;
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (predicate == null || !predicate(component))
                {
                    continue;
                }

                count++;
                result = component;
            }

            return count == 1 ? result : null;
        }

        private static bool HasCompoundPushButton(IReadOnlyList<CircuitComponent> components)
        {
            for (var i = 0; i < components.Count; i++)
            {
                if (IsCompoundPushButton(components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStarDeltaMotor(IReadOnlyList<CircuitComponent> components)
        {
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component != null &&
                    component.Definition != null &&
                    string.Equals(component.Definition.name, "Motor_StarDelta_380V", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPureStopButtonCandidate(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.PushButton &&
                string.Equals(component.Definition.name, "Button_Stop_NC", StringComparison.OrdinalIgnoreCase) &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") == null &&
                component.GetTerminal("24") == null;
        }

        private static bool IsPureStartButtonCandidate(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.PushButton &&
                string.Equals(component.Definition.name, "Button_Start_NO", StringComparison.OrdinalIgnoreCase) &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null &&
                component.GetTerminal("11") == null &&
                component.GetTerminal("12") == null;
        }

        private static bool IsSupportedContactor(CircuitComponent component)
        {
            if (component == null || component.Definition == null)
            {
                return false;
            }

            var definitionName = component.Definition.name ?? string.Empty;
            return (string.Equals(definitionName, "Contactor_KM_380V", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(definitionName, "Contactor_KM_220V", StringComparison.OrdinalIgnoreCase)) &&
                component.GetTerminal(TerminalConstants.A1) != null &&
                component.GetTerminal(TerminalConstants.A2) != null &&
                component.GetTerminal(TerminalConstants.AuxNO13) != null &&
                component.GetTerminal(TerminalConstants.AuxNO14) != null;
        }

        private static bool IsCompoundPushButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.kind == ComponentKind.PushButton &&
                component.Definition.name.IndexOf("Button_Compound", StringComparison.OrdinalIgnoreCase) >= 0 &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null;
        }

        private static bool HasSelfLockingButton(IReadOnlyList<CircuitComponent> components)
        {
            for (int i = 0; i < components.Count; i++)
            {
                if (IsSelfLockingButton(components[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSelfLockingButton(CircuitComponent component)
        {
            return component != null &&
                component.Definition != null &&
                component.Definition.name.IndexOf("Button_SelfLock", StringComparison.OrdinalIgnoreCase) >= 0 &&
                component.GetTerminal("11") != null &&
                component.GetTerminal("12") != null &&
                component.GetTerminal("23") != null &&
                component.GetTerminal("24") != null;
        }

        private static SelfHoldingBranchValidationResult CreateResult(
            bool applicable,
            bool attempt,
            bool valid,
            CircuitComponent stopButton,
            CircuitComponent startButton,
            CircuitComponent contactor,
            string reason)
        {
            return new SelfHoldingBranchValidationResult(
                applicable,
                attempt,
                valid,
                stopButton,
                startButton,
                contactor,
                reason);
        }
    }
}
