namespace RPG.Character
{
    /// <summary>使用 GAS Speed 进行普通接地移动；Mixer 档位由实际速度计算。</summary>
    internal sealed class WalkLocomotionState : GroundMoveLocomotionState
    {
        /// <summary>创建 Walk 状态。</summary>
        /// <param name="runtime">与 Run 共享的连续移动运行时。</param>
        internal WalkLocomotionState(GroundMoveRuntime runtime)
            : base(CharacterLocomotionStateId.Walk, runtime) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.WalkStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.WalkCanEnterQuery;

        /// <summary>Walk 不额外放大 GAS Speed。</summary>
        protected override float SpeedMultiplier => 1f;

    }
}
