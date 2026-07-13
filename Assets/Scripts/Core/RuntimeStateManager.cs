using System.Collections.Generic;

namespace ElectricalSim.Core
{
    public enum TimerRuntimePhase
    {
        Reset,
        Timing,
        Elapsed
    }

    public enum MotionDirection
    {
        Stopped,
        Forward,
        Reverse
    }

    public sealed class TimerRuntimeState
    {
        public float DelaySeconds;
        public float ElapsedSeconds;
        public TimerRuntimePhase Phase;
        public bool IsCoilEnergized;

        public void Reset()
        {
            ElapsedSeconds = 0f;
            Phase = TimerRuntimePhase.Reset;
            IsCoilEnergized = false;
        }
    }

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
    /// 按元件实例 ID 持有仅运行期的时间、运动和保护状态。
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

            timerStates.Remove(componentId);
            motionStates.Remove(componentId);
            protectionStates.Remove(componentId);
        }

        public void ResetAll(string reason)
        {
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
