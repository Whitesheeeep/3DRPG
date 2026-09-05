namespace RPG.Character
{
    /// <summary>
    /// 定义角色主动移动、动画根运动与固定阶段推进的统一边界。
    /// </summary>
    public interface IMotionDriver
    {
        /// <summary>获取当前 Active ASC Tag 是否允许角色进行水平移动。</summary>
        bool CanMoveHorizontally { get; }
        /// <summary>获取共享 CharacterController 上次移动后的接地状态。</summary>
        bool IsGrounded { get; }

        /// <summary>登记一个跨帧有效的运动控制权请求。</summary>
        /// <param name="request">Owner、通道和优先级。</param>
        /// <returns>用于提交运动并在状态结束时释放请求的句柄。</returns>
        MotionControlHandle RequestControl(MotionControlRequest request);

        /// <summary>向当前物理步提交运动。</summary>
        /// <param name="handle">已经登记且尚未释放的控制请求。</param>
        /// <param name="request">本物理步世界空间位移和旋转。</param>
        void SubmitFixed(MotionControlHandle handle, FixedMotionRequest request);

        /// <summary>向当前 Animator 阶段提交运动。</summary>
        /// <param name="handle">已经登记且尚未释放的控制请求。</param>
        /// <param name="submission">本阶段世界空间根运动增量。</param>
        void SubmitAnimatorMotion(MotionControlHandle handle, AnimatorMotionSubmission submission);

    }
}
