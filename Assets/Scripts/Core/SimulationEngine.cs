using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 对工作区图执行一次仿真步进，并更新元件与运行态。
    /// 它不是通用 SPICE 求解器，输入必须是工作区拥有的元件和导线列表。
    /// 副作用包括时间继电器、保护、运动、接触器及可视电气状态更新。
    /// 主要调用方是 WorkspaceController；修改后必须回归运行态模板。
    /// </summary>
    /// <remarks>
    /// 本类推进一次仿真运行所需的运行时效果：建立当前导通图、稳定动态控制器件、更新负载、保护与
    /// 运动状态。CircuitStateAnalyzer 用相同画布事实生成分析快照和诊断，但不负责在这里写入的
    /// 得电、延时或运动等运行时副作用；两者的职责不能互换。
    /// </remarks>
    public sealed partial class SimulationEngine
    {
        private readonly List<CircuitComponent> components;
        private readonly IReadOnlyList<WireView> wires;
        private readonly Dictionary<TerminalView, List<TerminalView>> graph = new Dictionary<TerminalView, List<TerminalView>>();
        private readonly HashSet<CircuitComponent> closedContactors = new HashSet<CircuitComponent>();
        private readonly HashSet<CircuitComponent> energizedOnDelayTimers = new HashSet<CircuitComponent>();
        private readonly float simulationDeltaTime;
        private readonly AutoReciprocationRoleResolution autoReciprocationRoles;
        private bool timerRuntimeAdvancedThisRun;
        private bool traversalBudgetWarningLogged;
        // 线圈得电会改变辅助/主触点，触点又会影响下一轮线圈路径。该上限防止异常反馈令一次 Run
        // 无法结束；调整它或循环顺序会改变互锁、自保持和延时触点的可观察行为。
        private const int MaxContactorStabilizationIterations = 4;
        private static readonly Dictionary<int, bool> selfHoldEligibleContactors = new Dictionary<int, bool>();

        public SimulationEngine(List<CircuitComponent> components, IReadOnlyList<WireView> wires, float simulationDeltaTime = 0f)
        {
            this.components = components;
            this.wires = wires;
            this.simulationDeltaTime = Mathf.Max(0f, simulationDeltaTime);
            autoReciprocationRoles = AutoReciprocationRoleResolver.Resolve(components, wires);
        }

        public static void ResetRuntimeState()
        {
            RuntimeStateManager.Shared.ResetAll("SimulationEngine.ResetRuntimeState");
            selfHoldEligibleContactors.Clear();
        }

        public string Run()
        {
            // 运行顺序是电气语义的一部分：先收敛控制器件，再扩散电源连通性并更新负载。不要为了
            // 合并代码把动态状态推进挪到 Flood 或测量计算之后。
            // 必须先稳定自保持、时间继电器、互锁与星三角状态，再进行图连通扩散和负载判断。
            // 调整该顺序会改变可观察到的运行行为。
            ResetTraversalBudgetState();
            StabilizeDynamicControlDevices();

            var phaseRoots = GetPhaseRoots();
            var neutralRoots = GetNeutralRoots();

            UpdateSelfHoldEligibility();

            var powered = Flood(phaseRoots);
            var neutral = Flood(neutralRoots);
            var shorted = powered.Overlaps(neutral);
            var energizedCount = 0;

            var systemVoltage = ActualSupplyVoltageResolver.ResolveSinglePhaseVoltage(components);
            var systemLineVoltage = ActualSupplyVoltageResolver.ResolveThreePhaseLineVoltage(components);

            foreach (var component in components)
            {
                var energized = IsLoadEnergized(component, powered, neutral);
                var active = !shorted && energized;
                UpdateMotorDirection(component, active);
                component.SetEnergized(active);
                ApplyMeasurement(component, active, systemVoltage, systemLineVoltage, GetReachablePowerPhaseKeys, CanReachPowerNeutral);
                if (active)
                {
                    energizedCount++;
                }
            }

            // 热继电器动作会切断原先参与控制路径的 NC 触点；一旦它改变了状态，必须重新稳定动态器件
            // 并重新扩散供电结果，不能仅在已有 Flood 结果上局部修改负载显示。
            if (UpdateThermalRelays())
            {
                StabilizeDynamicControlDevices();

                UpdateSelfHoldEligibility();

                powered = Flood(phaseRoots);
                neutral = Flood(neutralRoots);
                shorted = powered.Overlaps(neutral);
                energizedCount = 0;

                foreach (var component in components)
                {
                    var energized = IsLoadEnergized(component, powered, neutral);
                    var active = !shorted && energized;
                    UpdateMotorDirection(component, active);
                    component.SetEnergized(active);
                    ApplyMeasurement(component, active, systemVoltage, systemLineVoltage, GetReachablePowerPhaseKeys, CanReachPowerNeutral);
                    if (active)
                    {
                        energizedCount++;
                    }
                }
            }

            if (phaseRoots.Count == 0 || neutralRoots.Count == 0)
            {
                return "缺少电源，请先放置 220V 电源。";
            }

            if (shorted)
            {
                return "检测到短路：火线与零线直接连通。";
            }

            return energizedCount > 0 ? $"仿真完成：{energizedCount} 个负载/线圈已动作。" : "线路未形成完整回路。";
        }
    }
}
