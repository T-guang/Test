using System.Collections.Generic;

namespace ElectricalSim.Core
{
    /// <summary>
    /// KT 计时缓存的当前内存阶段，不进入模板或用户图纸 JSON。
    /// </summary>
    public enum TimerRuntimePhase
    {
        Reset,
        Timing,
        Elapsed
    }

    /// <summary>
    /// 运动运行态的当前方向，不代表模板中的持久化运动配置。
    /// </summary>
    public enum MotionDirection
    {
        Stopped,
        Forward,
        Reverse
    }

    /// <summary>
    /// 单个元件实例的计时运行态缓存，由 RuntimeStateManager 按 componentId 创建、仿真路径更新并在生命周期边界重置。
    /// 该状态仅存在内存，不进入模板或用户图纸 JSON；DelaySeconds/ElapsedSeconds 的单位由当前仿真调用方按秒使用。
    /// </summary>
    public sealed class TimerRuntimeState
    {
        public float DelaySeconds;
        public float ElapsedSeconds;
        public TimerRuntimePhase Phase;
        public bool IsCoilEnergized;

        public void Reset()
        {
            // 原地恢复计时字段的当前默认状态，不移除 Manager 中的字典条目。
            ElapsedSeconds = 0f;
            Phase = TimerRuntimePhase.Reset;
            IsCoilEnergized = false;
        }
    }

    /// <summary>
    /// 自动往返等元件实例的位置、速度、方向与限位缓存。它由 RuntimeStateManager 管理，不持久化到模板或保存图纸。
    /// </summary>
    public sealed class MotionRuntimeState
    {
        public const float MinPosition = 0f;
        public const float MaxPosition = 100f;

        public float Position;
        public float Speed;
        public MotionDirection Direction;
        public bool LeftLimitTriggered;
        public bool RightLimitTriggered;

        public MotionRuntimeState()
        {
            Reset();
        }

        public void Reset()
        {
            Position = 50f;
            Speed = 20f;
            Direction = MotionDirection.Stopped;
            LeftLimitTriggered = false;
            RightLimitTriggered = false;
        }

        public void AdvancePosition(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || Speed <= 0f)
            {
                ClampPositionAndUpdateLimits();
                return;
            }

            if (Direction == MotionDirection.Forward)
            {
                Position += Speed * deltaSeconds;
            }
            else if (Direction == MotionDirection.Reverse)
            {
                Position -= Speed * deltaSeconds;
            }

            ClampPositionAndUpdateLimits();
        }

        private void ClampPositionAndUpdateLimits()
        {
            if (Position < MinPosition)
            {
                Position = MinPosition;
            }
            else if (Position > MaxPosition)
            {
                Position = MaxPosition;
            }

            if (Position <= MinPosition)
            {
                LeftLimitTriggered = true;
                RightLimitTriggered = false;
            }
            else if (Position >= MaxPosition)
            {
                LeftLimitTriggered = false;
                RightLimitTriggered = true;
            }
            else
            {
                LeftLimitTriggered = false;
                RightLimitTriggered = false;
            }
        }
    }

    /// <summary>
    /// 单个元件实例的保护脱扣与过载计时缓存。它只承载运行态数据，不定义热继或保护规则，也不进入持久化图纸。
    /// </summary>
    public sealed class ProtectionRuntimeState
    {
        public bool IsTripped;
        public float OverloadSeconds;
        public float TripDelaySeconds;

        public void Reset()
        {
            IsTripped = false;
            OverloadSeconds = 0f;
        }
    }

    /// <summary>
    /// 按当前活动电路的 componentId 持有仅运行期的时间、运动和保护状态。
    /// 不创建元件、不持久化图纸；工作区和仿真代码会在生命周期边界重置它，
    /// 避免旧 KT、自动往返和热继状态泄漏到另一张电路图。
    /// </summary>
    public sealed class RuntimeStateManager
    {
        private readonly Dictionary<string, TimerRuntimeState> timerStates = new Dictionary<string, TimerRuntimeState>();
        private readonly Dictionary<string, MotionRuntimeState> motionStates = new Dictionary<string, MotionRuntimeState>();
        private readonly Dictionary<string, ProtectionRuntimeState> protectionStates = new Dictionary<string, ProtectionRuntimeState>();

        public static RuntimeStateManager Shared { get; } = new RuntimeStateManager();

        public string LastResetReason { get; private set; }

        public int TimerStateCount => timerStates.Count;
        public int MotionStateCount => motionStates.Count;
        public int ProtectionStateCount => protectionStates.Count;

        public TimerRuntimeState GetOrCreateTimerState(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                return null;
            }

            // 运行态以当前活动电路的 componentId 作为字典键隔离；
            // 该键的生命周期由 CircuitComponent 与 Workspace 管理，
            // 本类不验证其全局唯一性或跨图纸持久化稳定性。
            if (!timerStates.TryGetValue(componentId, out var state))
            {
                state = new TimerRuntimeState();
                timerStates[componentId] = state;
            }

            return state;
        }

        public MotionRuntimeState GetOrCreateMotionState(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                return null;
            }

            if (!motionStates.TryGetValue(componentId, out var state))
            {
                state = new MotionRuntimeState();
                motionStates[componentId] = state;
            }

            return state;
        }

        public ProtectionRuntimeState GetOrCreateProtectionState(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                return null;
            }

            if (!protectionStates.TryGetValue(componentId, out var state))
            {
                state = new ProtectionRuntimeState();
                protectionStates[componentId] = state;
            }

            return state;
        }

        public bool TryGetTimerState(string componentId, out TimerRuntimeState state)
        {
            return timerStates.TryGetValue(componentId ?? string.Empty, out state);
        }

        public bool TryGetMotionState(string componentId, out MotionRuntimeState state)
        {
            return motionStates.TryGetValue(componentId ?? string.Empty, out state);
        }

        public bool TryGetProtectionState(string componentId, out ProtectionRuntimeState state)
        {
            return protectionStates.TryGetValue(componentId ?? string.Empty, out state);
        }

        public void RemoveComponentState(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId))
            {
                return;
            }

            // 删除元件时必须一并清理三类运行态，避免同一实例 ID 在撤销、导入后继承旧状态。
            timerStates.Remove(componentId);
            motionStates.Remove(componentId);
            protectionStates.Remove(componentId);
        }

        public void ResetAll(string reason)
        {
            // 清空画布、加载模板、停止并重建等生命周期边界调用此处；
            // 不将计时、往返位置或保护脱扣状态写入模板和保存定义。
            LastResetReason = reason;
            timerStates.Clear();
            motionStates.Clear();
            protectionStates.Clear();
        }

        public void ResetTimer(string componentId, string reason)
        {
            LastResetReason = reason;
            if (timerStates.TryGetValue(componentId ?? string.Empty, out var state))
            {
                state.Reset();
            }
        }

        public void ResetMotion(string componentId, string reason)
        {
            LastResetReason = reason;
            if (motionStates.TryGetValue(componentId ?? string.Empty, out var state))
            {
                state.Reset();
            }
        }

        public void ResetProtection(string componentId, string reason)
        {
            LastResetReason = reason;
            if (protectionStates.TryGetValue(componentId ?? string.Empty, out var state))
            {
                state.Reset();
            }
        }
    }
}
