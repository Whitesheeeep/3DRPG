using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>普通下落状态；重力由外层 Locomotion 状态机统一提交。</summary>
    public sealed class FallState : AirborneMotionState
    {
        /// <summary>创建下落状态。</summary>
        public FallState() : base(CharacterLocomotionStateId.Fall) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.FallStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.FallCanEnterQuery;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            if (Transition.FallTransition != null && Transition.FallTransition.IsValid)
                Character.AnimationPlayer.Play(AnimationLayerType.Base, Transition.FallTransition);
        }
    }
}
