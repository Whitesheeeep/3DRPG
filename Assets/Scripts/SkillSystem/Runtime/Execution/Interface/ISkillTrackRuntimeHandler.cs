namespace RPG.SkillSystem
{
    /// <summary>
    /// 定义一种 TrackConfig 在单次技能执行中的聚合有状态处理器契约。
    /// </summary>
    internal interface ISkillTrackRuntimeHandler
    {
        /// <summary>
        /// 将处理器绑定到本次执行上下文，并从完整配置中收集其负责的全部轨道。
        /// </summary>
        /// <param name="context">本次执行共享上下文。</param>
        /// <param name="config">本次执行期间视为不可变的技能配置。</param>
        void Initialize(SkillRuntimeContext context, SkillConfig config);

        /// <summary>
        /// 按顺序处理一个已经到达的整数逻辑帧；该阶段不依赖 Animator 本帧最终姿态。
        /// </summary>
        /// <param name="frame">当前整数帧。</param>
        void ProcessFrame(int frame);

        /// <summary>
        /// 在 LateUpdate 中处理依赖 Animator、Marker 或武器最终姿态的当前逻辑帧。
        /// </summary>
        /// <param name="frame">此前已经完成普通处理的整数帧。</param>
        void ProcessLateFrame(int frame);

        /// <summary>
        /// 结束本处理器；Stop 可保留自然尾迹，Cancel 必须立即释放动态资源。
        /// </summary>
        /// <param name="reason">本次技能结束原因。</param>
        void Complete(SkillCompletionReason reason);
    }

    /// <summary>
    /// 向 SkillExecution 暴露动作阶段查询，而不让 Runner 依赖具体处理器类型。
    /// </summary>
    internal interface IActionPhaseRuntimeState
    {
        ActionPhaseType CurrentPhase { get; }
        bool CanBeInterrupted { get; }
    }
}
