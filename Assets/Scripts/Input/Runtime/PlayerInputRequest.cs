using UnityEngine;

namespace RPG.PlayerInputSystem
{
    /// <summary>向仲裁层暴露单个输入请求的只读状态。</summary>
    public interface IReadOnlyPlayerInputRequest
    {
        /// <summary>获取输入类型。</summary>
        PlayerInputType InputType { get; }
        /// <summary>获取当前物理状态。</summary>
        PlayerInputPhysicalState PhysicalState { get; }
        /// <summary>获取本手势已按住的真实时间。</summary>
        float HeldDuration { get; }
        /// <summary>获取 Press 阶段是否仍待消费。</summary>
        bool HasBufferedPress { get; }
        /// <summary>获取 Press 阶段剩余真实时间。</summary>
        float PressBufferRemaining { get; }
        /// <summary>获取 Press 阶段句柄。</summary>
        InputRequestHandle PressHandle { get; }
        /// <summary>获取 Release 阶段是否仍待消费。</summary>
        bool HasBufferedRelease { get; }
        /// <summary>获取 Release 阶段剩余真实时间。</summary>
        float ReleaseBufferRemaining { get; }
        /// <summary>获取 Release 阶段句柄。</summary>
        InputRequestHandle ReleaseHandle { get; }
    }

    /// <summary>保存一个输入类型当前手势及其独立 Press 和 Release 缓冲阶段。</summary>
    public sealed class PlayerInputRequest : IReadOnlyPlayerInputRequest
    {
        #region 属性
        // 标识该实例唯一承载的输入类型，且在生命周期内不可变。
        public PlayerInputType InputType { get; }
        // 标识当前手势的物理状态，按下后会从 Press 过渡到 Held，松开后会进入 Released。
        public PlayerInputPhysicalState PhysicalState { get; private set; }
        public float HeldDuration { get; private set; }
        // 两个独立缓冲阶段，按下后会创建 Press 缓冲，松开后会创建 Release 缓冲。
        public bool HasBufferedPress { get; private set; }
        public float PressBufferRemaining { get; private set; }
        public InputRequestHandle PressHandle { get; private set; }
        // 释放后会创建独立 Release 缓冲，且新手势会淘汰旧手势的 Release 阶段。
        public bool HasBufferedRelease { get; private set; }
        public float ReleaseBufferRemaining { get; private set; }
        public InputRequestHandle ReleaseHandle { get; private set; }
        internal bool CanRemove => PhysicalState == PlayerInputPhysicalState.Released &&
                                   !HasBufferedPress && !HasBufferedRelease;
        #endregion

        #region 字段
        /// <summary>
        /// 标识当前手势的版本号，每次 Perform 都会自增，确保 Press 和 Release 阶段句柄不会被新手势覆盖。
        /// </summary>
        private uint gestureVersion;
        /// <summary>
        /// 标识当前手势的"按下"帧号，用于判断手势的持续时间。
        /// </summary>
        private int pressedFrame;
        #endregion

        #region 构造与状态推进
        /// <summary>创建指定类型的输入请求。</summary>
        /// <param name="inputType">该实例唯一承载的输入类型。</param>
        public PlayerInputRequest(PlayerInputType inputType) => InputType = inputType;

        /// <summary>开始新手势并刷新 Press 阶段，旧 Release 阶段会被新手势淘汰。</summary>
        /// <param name="duration">Press 缓冲秒数。</param>
        /// <param name="frame">回调发生的 Unity 帧号。</param>
        internal void Perform(float duration, int frame)
        {
            gestureVersion++;
            PhysicalState = PlayerInputPhysicalState.Pressed;
            HeldDuration = 0f;
            pressedFrame = frame;
            HasBufferedPress = duration > 0f;
            PressBufferRemaining = Mathf.Max(0f, duration);
            PressHandle = new InputRequestHandle(InputType, gestureVersion, PlayerInputRequestStage.Press);

            // 新手势替代同类型旧手势，因此旧 Release 不能继续被业务确认。
            HasBufferedRelease = false;
            ReleaseBufferRemaining = 0f;
            ReleaseHandle = default;
        }

        /// <summary>结束当前手势并创建独立 Release 缓冲，不改变 Press 阶段。</summary>
        /// <param name="duration">Release 缓冲秒数。</param>
        internal void Release(float duration)
        {
            // 不要在 Release 时重置 HeldDuration 和 pressedFrame，因为业务可能需要在 Release 后继续使用该值。
            PhysicalState = PlayerInputPhysicalState.Released;
            HasBufferedRelease = duration > 0f;
            ReleaseBufferRemaining = Mathf.Max(0f, duration);
            ReleaseHandle = new InputRequestHandle(InputType, gestureVersion, PlayerInputRequestStage.Release);
        }

        /// <summary>按真实帧间隔推进物理状态和两个独立缓冲计时器。</summary>
        /// <param name="unscaledDeltaTime">不受 timeScale 影响的帧间隔。</param>
        /// <param name="frame">当前 Unity 帧号。</param>
        internal void Tick(float unscaledDeltaTime, int frame)
        {
            // 物理阶段还没释放时，持续时间累加；按下后会从 Press 过渡到 Held。
            if (PhysicalState != PlayerInputPhysicalState.Released)
            {
                HeldDuration += unscaledDeltaTime;
                if (PhysicalState == PlayerInputPhysicalState.Pressed && frame > pressedFrame)
                    PhysicalState = PlayerInputPhysicalState.Held;
            }

            // 两个窗口并行倒计时；任一到期都只影响自己的阶段。
            PressBufferRemaining = TickStage(HasBufferedPress, PressBufferRemaining, unscaledDeltaTime,
                out bool pressPending);
            HasBufferedPress = pressPending;
            ReleaseBufferRemaining = TickStage(HasBufferedRelease, ReleaseBufferRemaining, unscaledDeltaTime,
                out bool releasePending);
            HasBufferedRelease = releasePending;
        }

        /// <summary>仅在句柄仍指向当前手势的对应待消费阶段时提交消费。</summary>
        /// <param name="handle">业务成功后回传的来源句柄。</param>
        /// <returns>成功清除对应阶段时返回 true。</returns>
        internal bool TryConsume(InputRequestHandle handle)
        {
            if (handle.InputType != InputType || handle.GestureVersion != gestureVersion) return false;

            // 仅在句柄仍指向当前手势的对应待消费阶段时提交消费。
            if (handle.Stage == PlayerInputRequestStage.Press && HasBufferedPress && handle == PressHandle)
            {
                HasBufferedPress = false;
                PressBufferRemaining = 0f;
                return true;
            }

            if (handle.Stage == PlayerInputRequestStage.Release && HasBufferedRelease && handle == ReleaseHandle)
            {
                HasBufferedRelease = false;
                ReleaseBufferRemaining = 0f;
                return true;
            }

            return false;
        }

        /// <summary>推进一个阶段的剩余时间并在到期时取消待消费状态。</summary>
        /// <param name="pending">该阶段推进前是否待消费。</param>
        /// <param name="remaining">该阶段推进前的剩余时间。</param>
        /// <param name="deltaTime">本帧真实时间增量。</param>
        /// <param name="remainsPending">返回推进后是否仍待消费。</param>
        /// <returns>推进后的非负剩余时间。</returns>
        private static float TickStage(bool pending, float remaining, float deltaTime, out bool remainsPending)
        {
            if (!pending)
            {
                remainsPending = false;
                return remaining;
            }
            float updatedRemaining = Mathf.Max(0f, remaining - deltaTime);
            remainsPending = updatedRemaining > 0f;
            return updatedRemaining;
        }
        #endregion
    }
}
