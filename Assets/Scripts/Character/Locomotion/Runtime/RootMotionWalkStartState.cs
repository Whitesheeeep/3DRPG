using Animancer;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>使用 Walk 九方向起步根运动进入 Walk。</summary>
    public sealed class RootMotionWalkStartState : RootMotionStartState
    {
        /// <summary>创建 Walk 起步状态。</summary>
        public RootMotionWalkStartState() : base(CharacterLocomotionStateId.RootMotionWalkStart) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.WalkStartStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.WalkStartCanEnterQuery;

        /// <inheritdoc />
        protected override CharacterLocomotionStateId CompletionMoveState => CharacterLocomotionStateId.Walk;

        /// <summary>选择 Walk 起步动画。</summary>
        protected override ClipTransition SelectStartTransition(Vector3 forward, Vector3 moveDirection) =>
            Transition.SelectStartTransition(forward, moveDirection);
    }
}
