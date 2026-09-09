using Animancer;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>从静止状态使用 Run 专用根运动起步并衔接 Run 循环。</summary>
    public sealed class RootMotionRunStartState : RootMotionStartState
    {
        /// <summary>创建 Run 起步状态。</summary>
        public RootMotionRunStartState()
            : base(CharacterLocomotionStateId.RootMotionRunStart)
        {
        }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.RunStartStateTag;

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery =>
            Transition.RunStartCanEnterQuery;

        /// <inheritdoc />
        protected override CharacterLocomotionStateId CompletionMoveState => CharacterLocomotionStateId.Run;

        /// <summary>选择一次 Run 起步动画；前向槽位根据进入时脚相位选择 L0 或 R0。</summary>
        /// <param name="forward">角色当前世界空间前向。</param>
        /// <param name="moveDirection">Blackboard 中的世界空间移动方向。</param>
        /// <returns>当前方向和脚相位对应的 Run 起步动画。</returns>
        protected override ClipTransition SelectStartTransition(Vector3 forward, Vector3 moveDirection) =>
            Transition.SelectRunStartTransition(
                forward,
                moveDirection,
                Character.IsLeftFootAhead);

        /// <summary>判断当前选中的 Run 起步是否为前向 L0 或 R0 槽位。</summary>
        /// <param name="selected">当前选中的 Transition。</param>
        /// <returns>前向 Run 起步返回 true。</returns>
        protected override bool IsForwardTransition(ITransition selected) =>
            ReferenceEquals(selected, Transition.RunForwardLeftStart) ||
            ReferenceEquals(selected, Transition.RunForwardRightStart);

        /// <summary>在进入 Run 前把起步结束姿态的脚相位交给 RunAsset。</summary>
        protected override void PrepareMoveEntry()
        {
            // 此时仍处于 RunStart 的自然结束姿态，必须先写参数再播放 Move Mixer，避免 Mixer 默认值先采样一帧。
            Character.AnimationPlayer.SetFloatParameter(
                Transition.RunFeetParameter,
                Character.IsLeftFootAhead ? 0f : 1f);
        }
    }
}
