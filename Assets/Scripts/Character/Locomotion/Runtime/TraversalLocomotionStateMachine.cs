using RPG.PlayerInputSystem;
using WS_Modules.FSM;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>组合 Traversal 叶状态，并提供当前动画窗口的输入与下落门禁。</summary>
    public sealed class TraversalLocomotionStateMachine : StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>
    {
        #region 生命周期与查询

        /// <summary>创建 Traversal 状态机。</summary>
        public TraversalLocomotionStateMachine()
            : base(CharacterLocomotionStateId.Traversal)
        {
            AddState(new RootMotionVaultState());
            AddState(new RootMotionMantleState());
            SetDefaultState(CharacterLocomotionStateId.Vault);
        }

        /// <summary>使用 Traversal 分支自身的 TagQuery 作为根路径预检门禁。</summary>
        /// <returns>当前 ASC 满足 Traversal 分支门禁时返回 true。</returns>
        public override bool CanEnter()
        {
            GameplayTagQuery query = Owner.Transition.TraversalCanEnterQuery;
            return query.IsEmpty || query.Matches(Owner.Owner.AbilitySystemComponent.Tags);
        }

        /// <summary>进入 Traversal 分支并维护父级 Traversal Tag。</summary>
        /// <param name="suppressDefaultState">路径切换时是否跳过默认 Vault。</param>
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter(suppressDefaultState);
            GameplayTag tag = Owner.Transition.TraversalStateTag;
            if (tag.IsValid)
                Owner.Owner.AbilitySystemComponent.AddLooseGameplayTag(tag);
        }

        /// <summary>退出 Traversal 分支并对称移除父级 Traversal Tag。</summary>
        public override void OnExit()
        {
            base.OnExit();
            GameplayTag tag = Owner.Transition.TraversalStateTag;
            if (tag.IsValid)
                Owner.Owner.AbilitySystemComponent.RemoveLooseGameplayTag(tag);
        }

        /// <summary>获取当前 Traversal 动画是否已经开放新的输入。</summary>
        internal bool CanExitByInput => CurrentState is TraversalLocomotionState state && state.IsExitInputOpen;

        /// <summary>获取当前 Traversal 动画是否已经开放下落检测。</summary>
        internal bool CanDetectFall => CurrentState is TraversalLocomotionState state && state.IsFallDetectionOpen;

        /// <summary>判断 Traversal 是否可以直接进入普通主动跳跃。</summary>
        /// <returns>输入开放、头顶无阻挡且存在 Press 时返回 true。</returns>
        internal bool CanEnterBufferedJump()
        {
            return CanExitByInput &&
                Owner.Owner.StateBlackboard.InputRequests.TryGetRequest(PlayerInputType.Jump, out IReadOnlyPlayerInputRequest request) &&
                request.HasBufferedPress && !Owner.Owner.StateBlackboard.IsCeilingBlocked;
        }

        /// <summary>确认 Traversal 分支成功进入主动跳跃后消费当前 Jump Press。</summary>
        internal void CommitBufferedJump()
        {
            if (Owner.Owner.StateBlackboard.InputRequests.TryGetRequest(
                    PlayerInputType.Jump,
                    out IReadOnlyPlayerInputRequest request) && request.HasBufferedPress)
                Owner.Owner.StateBlackboard.InputRequests.TryConfirmConsumed(request.PressHandle);
        }

        #endregion
    }
}
