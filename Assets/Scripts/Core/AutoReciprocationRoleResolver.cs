using System;
using System.Collections.Generic;
using ElectricalSim.Core.Validation;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 自动往返电路的运行角色解析状态。只有角色唯一且电机、正反转接触器、左右限位均已由拓扑确定时，
    /// 运行层才允许驱动虚拟运动，避免把普通正反转或不完整图纸误当作自动往返。
    /// </summary>
    public enum AutoReciprocationRoleResolutionStatus
    {
        NotApplicable,
        Ambiguous,
        Resolved
    }

    /// <summary>
    /// 同一次拓扑解析得到的自动往返角色集合。角色保存真实组件引用而非模板 InstanceId，
    /// 因此系统模板、自由接线与练习模式可以共享同一套运行态和视觉状态。
    /// </summary>
    public sealed class AutoReciprocationRoleResolution
    {
        public AutoReciprocationRoleResolution(
            AutoReciprocationRoleResolutionStatus status,
            CircuitComponent motor,
            CircuitComponent forwardContactor,
            CircuitComponent reverseContactor,
            CircuitComponent leftLimitSwitch,
            CircuitComponent rightLimitSwitch,
            string reason)
        {
            Status = status;
            Motor = motor;
            ForwardContactor = forwardContactor;
            ReverseContactor = reverseContactor;
            LeftLimitSwitch = leftLimitSwitch;
            RightLimitSwitch = rightLimitSwitch;
            Reason = reason ?? string.Empty;
        }

        public AutoReciprocationRoleResolutionStatus Status { get; }
        public CircuitComponent Motor { get; }
        public CircuitComponent ForwardContactor { get; }
        public CircuitComponent ReverseContactor { get; }
        public CircuitComponent LeftLimitSwitch { get; }
        public CircuitComponent RightLimitSwitch { get; }
        public string Reason { get; }
        public bool IsResolved => Status == AutoReciprocationRoleResolutionStatus.Resolved;
    }

    /// <summary>
    /// 从活动组件和真实 Wire 解析自动往返的五个运行角色。
    /// 正反转接触器复用既有 ReversingPairScopeHelper 的三相输出映射；限位角色仅依据其常闭触点
    /// 接入哪一条反向互锁支路，不依赖 InstanceId、显示名称、坐标、模板身份或创建顺序。
    /// </summary>
    public static class AutoReciprocationRoleResolver
    {
        public static AutoReciprocationRoleResolution Resolve(
            IReadOnlyList<CircuitComponent> components,
            IReadOnlyList<WireView> wires)
        {
            var motors = FindThreePhaseMotors(components);
            var contactors = FindReversingContactors(components);
            var limits = FindCompoundLimitSwitches(components);

            if (motors.Count == 0 || contactors.Count < 2 || limits.Count < 2)
            {
                return NotApplicable("缺少唯一自动往返所需的三相电机、正反转接触器或复合限位开关。");
            }

            if (motors.Count > 1 || contactors.Count > 2 || limits.Count > 2)
            {
                return Ambiguous("存在多个自动往返角色候选，不能按顺序任选组件。");
            }

            var scopes = ReversingPairScopeHelper.ResolveReliableReversingPairs(components, wires, out var traversalLimitExceeded);
            if (traversalLimitExceeded)
            {
                return Ambiguous("正反转拓扑遍历超过安全上限，无法可靠绑定自动往返角色。");
            }

            if (scopes.Count == 0)
            {
                return NotApplicable("未找到唯一的正反转电机与接触器作用域。");
            }

            if (scopes.Count > 1)
            {
                return Ambiguous("存在多个可靠正反转作用域，无法唯一绑定自动往返角色。");
            }

            var scope = scopes[0];
            if (scope == null || scope.Motor == null ||
                !ReferenceEquals(scope.Motor, motors[0]) ||
                scope.ForwardContactor == null || scope.ReverseContactor == null)
            {
                return Ambiguous("正反转作用域与唯一三相电机候选不一致。");
            }

            var topology = new StaticWireTopology(wires);
            CircuitComponent left = null;
            CircuitComponent right = null;
            foreach (var limit in limits)
            {
                // 正转支路由反转接触器的 21/22 常闭互锁保护；触发该支路的限位位于右端。
                if (topology.AreConnected(limit.GetTerminal("12"), scope.ReverseContactor.GetTerminal(TerminalConstants.AuxNC21)))
                {
                    if (right != null)
                    {
                        return Ambiguous("多个限位开关同时位于正转切断支路。");
                    }

                    right = limit;
                }

                // 反转支路由正转接触器的 21/22 常闭互锁保护；触发该支路的限位位于左端。
                if (topology.AreConnected(limit.GetTerminal("12"), scope.ForwardContactor.GetTerminal(TerminalConstants.AuxNC21)))
                {
                    if (left != null || ReferenceEquals(limit, right))
                    {
                        return Ambiguous("限位开关无法唯一对应左右反向切换支路。");
                    }

                    left = limit;
                }
            }

            if (left == null || right == null || ReferenceEquals(left, right))
            {
                return Ambiguous("两个限位开关没有唯一接入相反的正反转互锁支路。");
            }

            return new AutoReciprocationRoleResolution(
                AutoReciprocationRoleResolutionStatus.Resolved,
                scope.Motor,
                scope.ForwardContactor,
                scope.ReverseContactor,
                left,
                right,
                "自动往返运行角色已由正反转主回路和限位互锁支路唯一确定。");
        }

        private static AutoReciprocationRoleResolution NotApplicable(string reason)
        {
            return new AutoReciprocationRoleResolution(AutoReciprocationRoleResolutionStatus.NotApplicable, null, null, null, null, null, reason);
        }

        private static AutoReciprocationRoleResolution Ambiguous(string reason)
        {
            return new AutoReciprocationRoleResolution(AutoReciprocationRoleResolutionStatus.Ambiguous, null, null, null, null, null, reason);
        }

        private static List<CircuitComponent> FindThreePhaseMotors(IReadOnlyList<CircuitComponent> components)
        {
            var result = new List<CircuitComponent>();
            if (components == null) return result;
            foreach (var component in components)
            {
                if (component != null && component.Definition != null && component.Definition.kind == ComponentKind.Motor &&
                    component.GetTerminal(TerminalConstants.U) != null && component.GetTerminal(TerminalConstants.V) != null && component.GetTerminal(TerminalConstants.W) != null)
                {
                    result.Add(component);
                }
            }

            return result;
        }

        private static List<CircuitComponent> FindReversingContactors(IReadOnlyList<CircuitComponent> components)
        {
            var result = new List<CircuitComponent>();
            if (components == null) return result;
            foreach (var component in components)
            {
                if (component != null && component.Definition != null && component.Definition.kind == ComponentKind.ContactorCoil &&
                    component.GetTerminal(TerminalConstants.T1) != null && component.GetTerminal(TerminalConstants.T2) != null && component.GetTerminal(TerminalConstants.T3) != null &&
                    component.GetTerminal(TerminalConstants.AuxNC21) != null && component.GetTerminal(TerminalConstants.AuxNC22) != null)
                {
                    result.Add(component);
                }
            }

            return result;
        }

        private static List<CircuitComponent> FindCompoundLimitSwitches(IReadOnlyList<CircuitComponent> components)
        {
            var result = new List<CircuitComponent>();
            if (components == null) return result;
            foreach (var component in components)
            {
                if (component != null && component.GetTerminal("11") != null && component.GetTerminal("12") != null &&
                    component.GetTerminal("23") != null && component.GetTerminal("24") != null && IsLimitSwitchDefinition(component))
                {
                    result.Add(component);
                }
            }

            return result;
        }

        private static bool IsLimitSwitchDefinition(CircuitComponent component)
        {
            var definition = component != null ? component.Definition : null;
            if (definition == null) return false;
            // 这里只读取 ComponentDefinition 的稳定定义键，不读取用户可见名称、GameObject 名称或实例 ID。
            var definitionId = definition.name ?? string.Empty;
            return definitionId.IndexOf("LimitSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                definitionId.IndexOf("TravelSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                definitionId.IndexOf("PositionSwitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                definitionId.IndexOf("Switch_Limit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class StaticWireTopology
        {
            private readonly Dictionary<TerminalView, List<TerminalView>> graph = new Dictionary<TerminalView, List<TerminalView>>();

            public StaticWireTopology(IReadOnlyList<WireView> wires)
            {
                if (wires == null) return;
                foreach (var wire in wires)
                {
                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null) continue;
                    Add(wire.StartTerminal, wire.EndTerminal);
                    Add(wire.EndTerminal, wire.StartTerminal);
                }
            }

            public bool AreConnected(TerminalView first, TerminalView second)
            {
                if (first == null || second == null) return false;
                if (ReferenceEquals(first, second)) return true;
                var visited = new HashSet<TerminalView> { first };
                var queue = new Queue<TerminalView>();
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!graph.TryGetValue(current, out var next)) continue;
                    foreach (var terminal in next)
                    {
                        if (ReferenceEquals(terminal, second)) return true;
                        if (visited.Add(terminal)) queue.Enqueue(terminal);
                    }
                }

                return false;
            }

            private void Add(TerminalView from, TerminalView to)
            {
                if (!graph.TryGetValue(from, out var adjacent))
                {
                    adjacent = new List<TerminalView>();
                    graph[from] = adjacent;
                }

                adjacent.Add(to);
            }
        }
    }
}
