using Animancer;

namespace RPG.Character
{
    /// <summary>使用 Run 目标速度倍率和普通平滑转向进行移动；Mixer 档位由实际速度计算。</summary>
    internal sealed class RunLocomotionState : GroundMoveLocomotionState
    {
        #region 构造与状态入口

        /// <summary>创建 Run 状态。</summary>
        /// <param name="runtime">与 Walk 共享的连续移动运行时。</param>
        internal RunLocomotionState(GroundMoveRuntime runtime)
            : base(CharacterLocomotionStateId.Run, runtime) { }

        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTag StateTag => Transition.RunStateTag;
        /// <inheritdoc />
        protected override WS_Modules.GAS.TAG.GameplayTagQuery StateCanEnterQuery => Transition.RunCanEnterQuery;

        /// <summary>Run 使用配置的 GAS Speed 倍率。</summary>
        protected override float SpeedMultiplier => Transition.RunSpeedMultiplier;

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            base.ResetForActivation();
        }

        #endregion
    }
}
