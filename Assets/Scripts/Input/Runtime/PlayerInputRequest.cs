using System;
using UnityEngine;

namespace RPG.PlayerInputSystem
{
    /// <summary>向仲裁层暴露单个输入请求的只读状态。</summary>
    public interface IReadOnlyPlayerInputRequest
    {
        /// <summary>获取输入类型。</summary>
        PlayerInputType InputType { get; }
        /// <summary>获取当前手势的物理状态。</summary>
        PlayerInputPhysicalState PhysicalState { get; }
        /// <summary>获取本手势已按住的真实时间。</summary>
        float HeldDuration { get; }
        /// <summary>获取本次手势使用的 Press 缓冲时长快照。</summary>
        float PressBufferDuration { get; }
        /// <summary>获取本次手势使用的 Release 缓冲时长快照。</summary>
        float ReleaseBufferDuration { get; }
        /// <summary>获取本次手势使用的 Click 与长按阈值快照。</summary>
        float ClickMaxHeldDuration { get; }
        /// <summary>获取本次手势使用的 Click 缓冲时长快照。</summary>
        float ClickBufferDuration { get; }
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
        /// <summary>获取 Click 阶段是否仍待消费。</summary>
        bool HasBufferedClick { get; }
        /// <summary>获取 Click 阶段剩余真实时间。</summary>
        float ClickBufferRemaining { get; }
        /// <summary>获取 Click 阶段句柄。</summary>
        InputRequestHandle ClickHandle { get; }
    }

    /// <summary>保存一个输入手势及其独立 Press、Release 与 Click 缓冲阶段。</summary>
    public sealed class PlayerInputRequest : IReadOnlyPlayerInputRequest
    {
        #region 属性

        /// <summary>获取该实例唯一承载的输入类型。</summary>
        public PlayerInputType InputType { get; }
        /// <summary>获取当前手势的物理状态。</summary>
        public PlayerInputPhysicalState PhysicalState { get; private set; }
        /// <summary>获取当前或最近一次手势已按住的真实时间。</summary>
        public float HeldDuration { get; private set; }
        /// <summary>获取本次手势使用的 Press 缓冲时长快照。</summary>
        public float PressBufferDuration { get; private set; }
        /// <summary>获取本次手势使用的 Click 与长按阈值快照。</summary>
        public float ClickMaxHeldDuration { get; private set; }
        /// <summary>获取本次手势使用的 Release 缓冲时长快照。</summary>
        public float ReleaseBufferDuration { get; private set; }
        /// <summary>获取本次手势使用的 Click 缓冲时长快照。</summary>
        public float ClickBufferDuration { get; private set; }
        /// <summary>获取 Press 阶段是否仍待消费。</summary>
        public bool HasBufferedPress { get; private set; }
        /// <summary>获取 Press 阶段剩余真实时间。</summary>
        public float PressBufferRemaining { get; private set; }
        /// <summary>获取 Press 阶段句柄。</summary>
        public InputRequestHandle PressHandle { get; private set; }
        /// <summary>获取 Release 阶段是否仍待消费。</summary>
        public bool HasBufferedRelease { get; private set; }
        /// <summary>获取 Release 阶段剩余真实时间。</summary>
        public float ReleaseBufferRemaining { get; private set; }
        /// <summary>获取 Release 阶段句柄。</summary>
        public InputRequestHandle ReleaseHandle { get; private set; }
        /// <summary>获取 Click 阶段是否仍待消费。</summary>
        public bool HasBufferedClick { get; private set; }
        /// <summary>获取 Click 阶段剩余真实时间。</summary>
        public float ClickBufferRemaining { get; private set; }
        /// <summary>获取 Click 阶段句柄。</summary>
        public InputRequestHandle ClickHandle { get; private set; }

        /// <summary>判断已释放手势的全部缓冲阶段均结束，可以从 Controller 索引移除。</summary>
        internal bool CanRemove => PhysicalState == PlayerInputPhysicalState.Released &&
                                   !HasBufferedPress && !HasBufferedRelease && !HasBufferedClick;

        #endregion

        #region 手势版本与计时

        // 全局递增版本让 Request 实例被移除重建后，旧手势 Handle 仍不会与新对象碰撞。
        private static uint nextGestureVersion;
        private uint gestureVersion;
        private int pressedFrame;
        private double pressedAtRealtime;
        private double releasedAtRealtime;

        #endregion

        #region 构造与状态推进

        /// <summary>创建指定类型的输入请求。</summary>
        /// <param name="inputType">该实例唯一承载的输入类型。</param>
        public PlayerInputRequest(PlayerInputType inputType)
        {
            InputType = inputType;
            PhysicalState = PlayerInputPhysicalState.Released;
        }

        /// <summary>开始新手势，复制本次配置快照并淘汰旧手势的 Release 与 Click。</summary>
        /// <param name="pressBufferDuration">Press 缓冲秒数。</param>
        /// <param name="releaseBufferDuration">Release 缓冲秒数。</param>
        /// <param name="clickMaxHeldDuration">Click 与长按分界秒数。</param>
        /// <param name="clickBufferDuration">Click 缓冲秒数。</param>
        /// <param name="frame">输入回调发生的 Unity 帧号。</param>
        /// <param name="realtime">输入回调发生的不受 timeScale 影响的时间。</param>
        internal void Perform(float pressBufferDuration, float releaseBufferDuration,
            float clickMaxHeldDuration, float clickBufferDuration, int frame, double realtime)
        {
            // 所有输入回调都在 Unity 主线程执行；新实例也沿用同一序列拒绝过期 Handle。
            gestureVersion = unchecked(++nextGestureVersion);
            PhysicalState = PlayerInputPhysicalState.Pressed;
            HeldDuration = 0f;
            PressBufferDuration = pressBufferDuration;
            ReleaseBufferDuration = releaseBufferDuration;
            ClickMaxHeldDuration = clickMaxHeldDuration;
            ClickBufferDuration = clickBufferDuration;
            pressedFrame = frame;
            pressedAtRealtime = realtime;
            releasedAtRealtime = 0d;
            HasBufferedPress = pressBufferDuration > 0f;
            PressBufferRemaining = Mathf.Max(0f, pressBufferDuration);
            PressHandle = new InputRequestHandle(InputType, gestureVersion, PlayerInputRequestStage.Press);

            HasBufferedRelease = false;
            ReleaseBufferRemaining = 0f;
            ReleaseHandle = default;
            HasBufferedClick = false;
            ClickBufferRemaining = 0f;
            ClickHandle = default;
        }

        /// <summary>结束当前手势并生成 Release；短于配置阈值时同时生成独立 Click 阶段。</summary>
        /// <param name="realtime">输入回调发生的不受 timeScale 影响的时间。</param>
        /// <returns>当前存在未释放手势并成功处理释放时返回 true。</returns>
        internal bool Release(double realtime)
        {
            if (PhysicalState == PlayerInputPhysicalState.Released)
                return false;

            // 用按键回调时间补齐本帧 Tick 尚未累计的间隔，Click 边界不受渲染帧率影响。
            float heldSeconds = (float)Math.Max(0d, realtime - pressedAtRealtime);
            HeldDuration = Math.Max(HeldDuration, heldSeconds);
            PhysicalState = PlayerInputPhysicalState.Released;
            HasBufferedRelease = ReleaseBufferDuration > 0f;
            ReleaseBufferRemaining = Mathf.Max(0f, ReleaseBufferDuration);
            ReleaseHandle = new InputRequestHandle(InputType, gestureVersion, PlayerInputRequestStage.Release);
            releasedAtRealtime = realtime;

            HasBufferedClick = heldSeconds < ClickMaxHeldDuration && ClickBufferDuration > 0f;
            ClickBufferRemaining = HasBufferedClick ? ClickBufferDuration : 0f;
            ClickHandle = HasBufferedClick
                ? new InputRequestHandle(InputType, gestureVersion, PlayerInputRequestStage.Click)
                : default;
            return true;
        }

        /// <summary>用同一单调真实时钟更新按住时长与三个阶段的缓冲剩余时间。</summary>
        /// <param name="frame">当前 Unity 帧号。</param>
        /// <param name="realtime">本次更新的不受 timeScale 影响的单调时间。</param>
        internal void Tick(int frame, double realtime)
        {
            if (PhysicalState != PlayerInputPhysicalState.Released)
            {
                // 使用按键回调的同一时钟计算长按边界，避免首帧完整 deltaTime 导致短按误入奔跑。
                HeldDuration = (float)Math.Max(0d, realtime - pressedAtRealtime);
                if (PhysicalState == PlayerInputPhysicalState.Pressed && frame > pressedFrame)
                    PhysicalState = PlayerInputPhysicalState.Held;
            }

            PressBufferRemaining = GetRemainingBuffer(HasBufferedPress, PressBufferDuration,
                pressedAtRealtime, realtime, out bool pressPending);
            HasBufferedPress = pressPending;
            ReleaseBufferRemaining = GetRemainingBuffer(HasBufferedRelease, ReleaseBufferDuration,
                releasedAtRealtime, realtime, out bool releasePending);
            HasBufferedRelease = releasePending;
            ClickBufferRemaining = GetRemainingBuffer(HasBufferedClick, ClickBufferDuration,
                releasedAtRealtime, realtime, out bool clickPending);
            HasBufferedClick = clickPending;
        }

        /// <summary>仅在句柄仍指向当前手势对应的待消费阶段时提交消费。</summary>
        /// <param name="handle">业务成功后回传的来源句柄。</param>
        /// <returns>成功清除对应阶段时返回 true。</returns>
        internal bool TryConsume(InputRequestHandle handle)
        {
            if (handle.InputType != InputType || handle.GestureVersion != gestureVersion)
                return false;

            switch (handle.Stage)
            {
                case PlayerInputRequestStage.Press when HasBufferedPress && handle == PressHandle:
                    HasBufferedPress = false;
                    PressBufferRemaining = 0f;
                    return true;
                case PlayerInputRequestStage.Release when HasBufferedRelease && handle == ReleaseHandle:
                    HasBufferedRelease = false;
                    ReleaseBufferRemaining = 0f;
                    return true;
                case PlayerInputRequestStage.Click when HasBufferedClick && handle == ClickHandle:
                    HasBufferedClick = false;
                    ClickBufferRemaining = 0f;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>按阶段创建时间和时长快照计算剩余窗口，到期时清除缓冲状态。</summary>
        /// <param name="pending">该阶段推进前是否待消费。</param>
        /// <param name="duration">该阶段创建时复制的缓冲时长。</param>
        /// <param name="startedAtRealtime">该阶段由 InputAction 回调创建时的单调时间。</param>
        /// <param name="realtime">当前单调时间。</param>
        /// <param name="remainsPending">返回推进后是否仍待消费。</param>
        /// <returns>推进后的非负剩余时间。</returns>
        private static float GetRemainingBuffer(bool pending, float duration,
            double startedAtRealtime, double realtime, out bool remainsPending)
        {
            if (!pending || duration <= 0f)
            {
                remainsPending = false;
                return 0f;
            }

            // 用回调的真实时间戳而不是完整帧 deltaTime，避免 Request 刚创建就被提前扣掉一帧。
            float elapsed = (float)Math.Max(0d, realtime - startedAtRealtime);
            float updatedRemaining = Mathf.Max(0f, duration - elapsed);
            remainsPending = updatedRemaining > 0f;
            return updatedRemaining;
        }

        #endregion
    }
}
