using Animancer;
using RPG.Character.Animation;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>Walk/Run 共用的根运动起步基类，具体状态提供动画选择和固定完成去向。</summary>
    public abstract class RootMotionStartState : RootMotionLocomotionState
    {
        #region 运行时状态

        // 起步动画只在进入时选择一次；播放期间输入变化只影响方向修正和完成后的目标状态。
        private ITransition selectedTransition;
        private bool isForwardStart;

        #endregion

        #region 构造与状态入口

        /// <summary>创建指定起步状态。</summary>
        /// <param name="stateId">状态标识。</param>
        protected RootMotionStartState(CharacterLocomotionStateId stateId) : base(stateId)
        {
        }

        /// <summary>根据当前角色前向和移动输入选择本状态的起步动画。</summary>
        protected abstract ClipTransition SelectStartTransition(Vector3 forward, Vector3 moveDirection);

        /// <summary>获取该起步动画自然结束后的固定代码移动状态。</summary>
        protected abstract CharacterLocomotionStateId CompletionMoveState { get; }

        /// <summary>在自然结束切换到代码移动前执行状态专属的表现交接。</summary>
        protected virtual void PrepareMoveEntry()
        {
        }

        /// <summary>判断选中的起步 Transition 是否为本状态的前向槽位。</summary>
        protected virtual bool IsForwardTransition(ITransition selected) =>
            ReferenceEquals(selected, Transition.ForwardStart);

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            selectedTransition = SelectStartTransition(Character.RootTransform.forward, MovementInput);
            if (selectedTransition == null || !selectedTransition.IsValid)
            {
                throw new System.InvalidOperationException(
                    $"角色 '{Character.name}' 的 {StateId} 未找到有效起步动画。" +
                    $"当前 MoveWorldInput={MovementInput}，请检查 PlayerFSMTransition 的对应方向槽位配置。 ");
            }

            isForwardStart = IsForwardTransition(selectedTransition);
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement)
            {
                Owner.ChangeState(CharacterLocomotionStateId.RootMotionStop);
                return;
            }
        }

        /// <inheritdoc />
        public override void OnAnimationMove()
        {
            bool shouldCorrect = isForwardStart ||
                AnimationState != null &&
                AnimationState.NormalizedTime >= Transition.StartDirectionCorrectionNormalizedTime;
            SubmitAnimatorMotionWithDirectionCorrection(shouldCorrect && HasMovement);
        }

        /// <inheritdoc />
        protected override AnimancerState PlayRootMotionAnimation() =>
            Character.AnimationPlayer.Play(AnimationLayerType.Base, selectedTransition);

        /// <inheritdoc />
        protected override void OnAnimationFinished()
        {
            if (HasMovement)
                PrepareMoveEntry();
            selectedTransition = null;
            isForwardStart = false;
            Owner.ChangeState(HasMovement
                ? CompletionMoveState
                : CharacterLocomotionStateId.RootMotionStop);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            selectedTransition = null;
            isForwardStart = false;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            selectedTransition = null;
            isForwardStart = false;
            base.ResetForActivation();
        }

        #endregion
    }
}
