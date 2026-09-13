using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>执行按障碍高度选择的 Mantle 根运动动画。</summary>
    public sealed class RootMotionMantleState : TraversalLocomotionState
    {
        /// <summary>创建 Mantle 状态。</summary>
        public RootMotionMantleState() : base(CharacterLocomotionStateId.Mantle) { }
        /// <inheritdoc />
        protected override TraversalCandidateKind CandidateKind => TraversalCandidateKind.Mantle;
        /// <inheritdoc />
        protected override GameplayTag StateTag => Transition.MantleStateTag;
        /// <inheritdoc />
        protected override GameplayTagQuery StateCanEnterQuery => Transition.MantleCanEnterQuery;
        /// <inheritdoc />
        protected override TraversalAnimationSettings SelectAnimation(TraversalCandidate traversalCandidate) =>
            Transition.SelectMantleAnimation(traversalCandidate.ObstacleHeight);
    }
}
