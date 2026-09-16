using System;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>复用 Grounded Locomotion 条件，查询当前 Jump Press 是否足以打断 FullBody Action。</summary>
    internal sealed class GroundedJumpTransitionQueryService
    {
        #region 依赖字段

        private readonly CharacterLocomotionStateMachine locomotion;
        private readonly GroundedLocomotionStateMachine groundedMachine;
        private readonly TraversalLocomotionStateMachine traversalMachine;
        private readonly AirborneLocomotionStateMachine airborneMachine;

        #endregion

        /// <summary>创建只读 Grounded Jump 路径查询服务。</summary>
        /// <param name="sourceLocomotion">提供当前根状态和 Blackboard 的 Locomotion。</param>
        /// <param name="sourceGroundedMachine">复用 Jump 和 Traversal 环境条件的 Grounded 分支。</param>
        /// <param name="sourceTraversalMachine">提供 Traversal 父节点和目标叶状态 CanEnter。</param>
        /// <param name="sourceAirborneMachine">提供 JumpMotion 目标叶状态 CanEnter。</param>
        internal GroundedJumpTransitionQueryService(
            CharacterLocomotionStateMachine sourceLocomotion,
            GroundedLocomotionStateMachine sourceGroundedMachine,
            TraversalLocomotionStateMachine sourceTraversalMachine,
            AirborneLocomotionStateMachine sourceAirborneMachine)
        {
            locomotion = sourceLocomotion ?? throw new ArgumentNullException(nameof(sourceLocomotion));
            groundedMachine = sourceGroundedMachine ?? throw new ArgumentNullException(nameof(sourceGroundedMachine));
            traversalMachine = sourceTraversalMachine ?? throw new ArgumentNullException(nameof(sourceTraversalMachine));
            airborneMachine = sourceAirborneMachine ?? throw new ArgumentNullException(nameof(sourceAirborneMachine));
        }

        /// <summary>判断当前 Grounded Jump Press 是否存在可进入的 Vault、Mantle 或 JumpMotion 路径。</summary>
        /// <returns>环境条件和目标状态门禁至少有一条完整通过时返回 true。</returns>
        internal bool CanInterruptFullBodyActionByBufferedJump()
        {
            if (locomotion.CurrentRootState != CharacterLocomotionStateId.Grounded ||
                !locomotion.Blackboard.IsGrounded)
                return false;

            if (groundedMachine.CanEnterTraversal(TraversalCandidateKind.Vault) &&
                CanEnterTraversalPath(CharacterLocomotionStateId.Vault))
                return true;
            if (groundedMachine.CanEnterTraversal(TraversalCandidateKind.Mantle) &&
                CanEnterTraversalPath(CharacterLocomotionStateId.Mantle))
                return true;

            return groundedMachine.CanEnterBufferedJump() &&
                   CanEnterLeaf(airborneMachine, CharacterLocomotionStateId.JumpMotion);
        }

        /// <summary>检查 Traversal 父分支及指定叶状态的现有 CanEnter 门禁。</summary>
        /// <param name="leafStateId">Vault 或 Mantle 叶状态标识。</param>
        /// <returns>父分支与叶状态都允许进入时返回 true。</returns>
        private bool CanEnterTraversalPath(CharacterLocomotionStateId leafStateId) =>
            traversalMachine.CanEnter() && CanEnterLeaf(traversalMachine, leafStateId);

        /// <summary>从已组装状态树读取目标叶状态并调用其原生 CanEnter。</summary>
        /// <param name="machine">拥有目标叶状态的分支状态机。</param>
        /// <param name="leafStateId">目标叶状态标识。</param>
        /// <returns>目标状态当前允许进入时返回 true。</returns>
        /// <exception cref="InvalidOperationException">Locomotion 状态树缺少约定目标时抛出。</exception>
        private static bool CanEnterLeaf(
            StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> machine,
            CharacterLocomotionStateId leafStateId)
        {
            if (!machine.States.TryGetValue(leafStateId,
                    out IState<CharacterLocomotionStateId, CharacterLocomotionStateMachine> state))
                throw new InvalidOperationException(
                    $"Locomotion 分支 '{machine.StateId}' 未注册目标状态 '{leafStateId}'。 ");
            return state.CanEnter();
        }
    }
}
