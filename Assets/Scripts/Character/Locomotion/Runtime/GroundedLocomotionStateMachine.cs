using RPG.PlayerInputSystem;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>组合 Grounded 下的叶状态，并独立保存 Grounded 来源的 Traversal 尝试。</summary>
    public sealed class
        GroundedLocomotionStateMachine : StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>
    {
        #region 依赖与运行时缓存
        private readonly GroundMoveRuntime groundMoveRuntime = new();
        private readonly TraversalEnvironmentDetector traversalDetector;
        private PreparedTraversalAttempt preparedTraversalAttempt;
        private bool hasPreparedTraversalAttempt;
        #endregion

        #region 生命周期与状态组装
        /// <summary>创建 Grounded 子状态机并注册全部直接子状态。</summary>
        /// <param name="detector">由根 Locomotion 共享的按需 Traversal 几何检测器。</param>
        public GroundedLocomotionStateMachine(TraversalEnvironmentDetector detector)
            : base(CharacterLocomotionStateId.Grounded)
        {
            traversalDetector = detector ?? throw new System.ArgumentNullException(nameof(detector));
            AddState(new IdleLocomotionState());
            AddState(new RootMotionWalkStartState());
            AddState(new RootMotionRunStartState());
            AddState(new WalkLocomotionState(groundMoveRuntime));
            AddState(new RunLocomotionState(groundMoveRuntime));
            AddState(new RootMotionStopState());
            SetDefaultState(CharacterLocomotionStateId.Idle);

            // Grounded 内部只放稳定的同分支转换；跨分支路径由根状态机统一组合。
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                        CharacterLocomotionStateId.Idle, CharacterLocomotionStateId.RootMotionRunStart, 200)
                    .AddCondition(owner => owner.Blackboard.HasMovement && owner.Blackboard.IsSprintHeld));
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                        CharacterLocomotionStateId.Idle, CharacterLocomotionStateId.RootMotionWalkStart, 100)
                    .AddCondition(owner => owner.Blackboard.HasMovement && !owner.Blackboard.IsSprintHeld));
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Walk, CharacterLocomotionStateId.Run, 100).AddCondition(owner =>
                    owner.Blackboard.IsSprintHeld && owner.Blackboard.HasMovement));
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                        CharacterLocomotionStateId.Walk, CharacterLocomotionStateId.RootMotionStop, 300)
                    .AddCondition(owner => !owner.Blackboard.HasMovement));
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                        CharacterLocomotionStateId.Run, CharacterLocomotionStateId.RootMotionStop, 300)
                    .AddCondition(owner => !owner.Blackboard.HasMovement));
            AddTransition(
                new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Run, CharacterLocomotionStateId.Walk, 200).AddCondition(owner =>
                    !owner.Blackboard.IsSprintHeld && owner.Blackboard.HasMovement));
        }

        /// <summary>判断 Grounded 是否可以为当前 Jump Press 准备指定 Traversal 候选。</summary>
        /// <param name="candidateKind">需要的候选类型。</param>
        /// <returns>候选有效且状态门禁可通过时返回 true。</returns>
        internal bool CanEnterTraversal(TraversalCandidateKind candidateKind)
        {
            return Owner.Owner.StateBlackboard.IsGrounded &&
                   TryPrepareTraversalAttempt() &&
                   preparedTraversalAttempt.Candidate.Kind == candidateKind;
        }

        /// <summary>获取 Grounded 来源已经准备好的指定 Traversal 候选，供根路径预检读取。</summary>
        /// <param name="candidateKind">需要的候选类型。</param>
        /// <param name="candidate">已冻结的世界空间候选。</param>
        /// <returns>当前缓存与目标类型匹配时返回 true。</returns>
        internal bool TryGetPreparedCandidate(
            TraversalCandidateKind candidateKind,
            out TraversalCandidate candidate)
        {
            candidate = preparedTraversalAttempt.Candidate;
            return hasPreparedTraversalAttempt && candidate.Kind == candidateKind;
        }

        /// <summary>判断 Grounded 是否存在满足接地/土狼时间和头顶门禁的 Jump Press。</summary>
        /// <returns>允许主动跳跃时返回 true。</returns>
        internal bool CanEnterBufferedJump()
        {
            return Owner.Owner.StateBlackboard.InputRequests.TryGetRequest(PlayerInputType.Jump,
                       out IReadOnlyPlayerInputRequest request) &&
                   request.HasBufferedPress &&
                   Owner.Owner.StateBlackboard.TimeSinceGrounded <= Owner.Transition.CoyoteTime &&
                   !Owner.Owner.StateBlackboard.IsCeilingBlocked;
        }

        /// <summary>确认 Grounded 来源已经成功进入目标路径的 Jump Press。</summary>
        internal void CommitPreparedJump()
        {
            if (hasPreparedTraversalAttempt)
                Owner.Owner.StateBlackboard.InputRequests.TryConfirmConsumed(preparedTraversalAttempt.JumpPressHandle);
            else if (Owner.Owner.StateBlackboard.InputRequests.TryGetRequest(
                         PlayerInputType.Jump,
                         out IReadOnlyPlayerInputRequest request) && request.HasBufferedPress)
                Owner.Owner.StateBlackboard.InputRequests.TryConfirmConsumed(request.PressHandle);
            ClearPreparedJump();
        }

        /// <summary>清理 Grounded 来源尚未提交的 Traversal 尝试。</summary>
        internal void ClearPreparedJump()
        {
            hasPreparedTraversalAttempt = false;
            preparedTraversalAttempt = default;
        }

        /// <summary>离开 Grounded 分支时重置连续 Walk/Run 运行时。</summary>
        public override void OnExit()
        {
            groundMoveRuntime.ResetForActivation();
            base.OnExit();
        }

        /// <summary>重新激活角色时清理 Grounded 的候选缓存和共享移动运行时。</summary>
        internal void ResetRuntimeForActivation()
        {
            groundMoveRuntime.ResetForActivation();
            ClearPreparedJump();
        }
        #endregion

        #region 内部检测
        /// <summary>为当前仍有效的 Jump Press 执行一次几何检测并缓存结果。</summary>
        private bool TryPrepareTraversalAttempt()
        {
            // 检查是否拥有有效的 Jump Press；若无则清理缓存并返回 false。
            if (!Owner.Owner.StateBlackboard.InputRequests.TryGetRequest(PlayerInputType.Jump,
                    out IReadOnlyPlayerInputRequest request) ||
                !request.HasBufferedPress || !Owner.Owner.StateBlackboard.HasMovement)
            {
                ClearPreparedJump();
                return false;
            }

            // 已经缓存了当前 Jump Press 的检测结果，则直接返回缓存的有效性。
            if (hasPreparedTraversalAttempt && preparedTraversalAttempt.JumpPressHandle == request.PressHandle)
                return preparedTraversalAttempt.Candidate.Kind != TraversalCandidateKind.None;

            // 检查角色朝向与移动输入是否大致一致；若不一致则直接缓存无效结果并返回 false。
            // 默认情况下，角色朝向与移动输入的夹角不超过 60° 时才允许 Traversal。
            Vector3 forward = Vector3.ProjectOnPlane(Owner.Owner.RootTransform.forward, Vector3.up).normalized;
            Vector3 move = Vector3.ProjectOnPlane(Owner.Owner.StateBlackboard.MoveWorldInput, Vector3.up).normalized;
            if (forward.sqrMagnitude <= 0.0001f || move.sqrMagnitude <= 0.0001f || Vector3.Dot(forward, move) < 0.5f)
            {
                preparedTraversalAttempt = new PreparedTraversalAttempt(request.PressHandle, default);
                hasPreparedTraversalAttempt = true;
                return false;
            }

            bool detected = traversalDetector.TryDetect(out TraversalCandidate candidate);
            preparedTraversalAttempt =
                new PreparedTraversalAttempt(request.PressHandle, detected ? candidate : default);
            hasPreparedTraversalAttempt = true;
            return detected;
        }
        #endregion
    }
}