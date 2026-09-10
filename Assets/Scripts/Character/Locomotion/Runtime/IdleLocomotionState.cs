using RPG.Character.Animation;

namespace RPG.Character
{
    /// <summary>接地且没有水平移动输入的待机状态。</summary>
    public sealed class IdleLocomotionState : CharacterLocomotionState
    {
        /// <summary>创建待机状态。</summary>
        public IdleLocomotionState() : base(CharacterLocomotionStateId.Idle) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.IdleStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.IdleCanEnterQuery;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            Character.AnimationPlayer.Play(AnimationLayerType.Base, Transition.IdleTransition);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            // Idle -> Start 由 Grounded 状态机的 Transition 统一检查。
        }
    }
}
