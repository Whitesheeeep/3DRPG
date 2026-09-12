using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>执行低矮障碍 Vault 根运动动画。</summary>
    public sealed class RootMotionVaultState : TraversalLocomotionState
    {
        /// <summary>创建 Vault 状态。</summary>
        public RootMotionVaultState() : base(CharacterLocomotionStateId.Vault) { }
        /// <inheritdoc />
        protected override TraversalCandidateKind CandidateKind => TraversalCandidateKind.Vault;
        /// <inheritdoc />
        protected override GameplayTag StateTag => Transition.VaultStateTag;
        /// <inheritdoc />
        protected override GameplayTagQuery StateCanEnterQuery => Transition.VaultCanEnterQuery;
        /// <inheritdoc />
        protected override TraversalAnimationSettings SelectAnimation(TraversalCandidate traversalCandidate) => Transition.VaultAnimationSetting;
    }
}
