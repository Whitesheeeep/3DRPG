using System;
using RPG.Character.State;
using RPG.PlayerInputSystem;
using RPG.SkillSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>按 Ability、Jump、Move 顺序仲裁当前角色的 FullBody 转换候选。</summary>
    internal sealed class CharacterActionArbiter : IFullBodyActionArbiter, IDisposable
    {
        #region 依赖字段

        private readonly CharacterActor owner;
        private readonly PlayerStateBlackboard blackboard;
        private readonly AbilityTransitionExecution abilityExecution;
        private readonly JumpTransitionExecution jumpExecution;
        private readonly MoveTransitionExecution moveExecution;

        #endregion

        #region 运行时状态

        private FullBodySkillExecution currentFullBodyExecution;
        private int nextRegistrationId;
        private bool disposed;

        #endregion

        /// <summary>创建当前角色唯一的 FullBody 动作仲裁器。</summary>
        /// <param name="sourceOwner">拥有该仲裁器的 CharacterActor。</param>
        /// <param name="sourceBlackboard">Player 共享输入与控制事实黑板。</param>
        /// <param name="combatSystem">角色 Ability 输入业务。</param>
        /// <param name="abilitySystemComponent">角色自己的 ASC。</param>
        /// <param name="jumpTransitionQueryService">Grounded Jump 可行性查询服务。</param>
        internal CharacterActionArbiter(
            CharacterActor sourceOwner,
            PlayerStateBlackboard sourceBlackboard,
            CharacterCombatSystem combatSystem,
            GameplayAbilitySystemComponent abilitySystemComponent,
            GroundedJumpTransitionQueryService jumpTransitionQueryService)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
            blackboard = sourceBlackboard ?? throw new ArgumentNullException(nameof(sourceBlackboard));
            abilityExecution = new AbilityTransitionExecution(combatSystem);
            jumpExecution = new JumpTransitionExecution(abilitySystemComponent, jumpTransitionQueryService);
            moveExecution = new MoveTransitionExecution(abilitySystemComponent);
        }

        /// <summary>登记当前角色唯一的 FullBody Skill Runtime。</summary>
        /// <param name="runtime">已经成功启动 SkillRuntimeHost 的 Ability Runtime。</param>
        /// <returns>控制本次注册生命周期的 Handle。</returns>
        public FullBodyActionHandle RegisterFullBodyAction(GameplayAbilityRuntime runtime)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CharacterActionArbiter));
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (currentFullBodyExecution != null)
                throw new InvalidOperationException(
                    $"角色 '{owner.name}' 已存在 FullBody Ability ActivationId={currentFullBodyExecution.ActivationId}。 ");

            int registrationId = ++nextRegistrationId;
            currentFullBodyExecution = new FullBodySkillExecution(
                registrationId,
                runtime);
            blackboard.IsFullBodyActionOccupied = true;
            Debug.Log(
                $"[CharacterActionArbiter] 角色 '{owner.name}' 注册 FullBody Ability，ActivationId={runtime.ActivationId}，RuntimeTags={runtime.RuntimeTags.Count}。",
                owner);
            return new FullBodyActionHandle(this, registrationId);
        }

        /// <summary>按固定优先级执行当前帧动作候选。</summary>
        /// <param name="inputRequests">稳定 Player 输入请求缓冲区。</param>
        internal void ArbitrateFrame(IPlayerInputRequestBuffer inputRequests)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(CharacterActionArbiter));
            if (inputRequests == null)
                throw new ArgumentNullException(nameof(inputRequests));

            FullBodySkillExecution execution = currentFullBodyExecution;
            // 没有 FullBody 占据时，Ability 输入仍统一经过同一个执行入口；Jump 和 Move 留给 Locomotion。
            if (execution == null)
            {
                abilityExecution.TryExecute(inputRequests, false);
                return;
            }

            // 存在 FullBody 占据时，
            if (execution.Runtime.RuntimeTags.HasTag(GameplayTags.Tag_Skill_Window_CancelBy_Ability) &&
                abilityExecution.TryExecute(inputRequests,
                    execution.Runtime.AbilityTags.HasTag(GameplayTags.Tag_Skill_NormalAttack)))
                return;

            // Ability 激活可能同步替换 FullBody Runtime；后续候选必须使用刷新后的当前执行。
            execution = currentFullBodyExecution;
            if (execution == null)
                return;
            if (execution.Runtime.RuntimeTags.HasTag(GameplayTags.Tag_Skill_Window_CancelBy_Jump) &&
                jumpExecution.TryExecute(inputRequests, execution))
                return;

            // 同样，Jump 激活可能同步替换 FullBody Runtime；后续候选必须使用刷新后的当前执行。
            execution = currentFullBodyExecution;
            if (execution != null &&
                execution.Runtime.RuntimeTags.HasTag(GameplayTags.Tag_Skill_Window_CancelBy_Move) &&
                blackboard.HasMovement)
                moveExecution.TryExecute(execution);
        }

        /// <summary>注销与 Handle 标识匹配的 FullBody 执行并归还 Blackboard 占据。</summary>
        /// <param name="registrationId">待注销的注册标识。</param>
        internal void UnregisterFullBodyAction(int registrationId)
        {
            if (disposed || currentFullBodyExecution == null ||
                currentFullBodyExecution.RegistrationId != registrationId)
                return;

            int activationId = currentFullBodyExecution.ActivationId;
            currentFullBodyExecution = null;
            blackboard.IsFullBodyActionOccupied = false;
            Debug.Log(
                $"[CharacterActionArbiter] 角色 '{owner.name}' 注销 FullBody Ability，ActivationId={activationId}。",
                owner);
        }

        /// <summary>销毁角色动作环境时清理当前注册和共享 Blackboard 占据。</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (currentFullBodyExecution != null)
            {
                Debug.LogWarning(
                    $"[CharacterActionArbiter] 角色 '{owner.name}' 销毁时仍存在 FullBody Ability，ActivationId={currentFullBodyExecution.ActivationId}，现强制归还占据。",
                    owner);
                currentFullBodyExecution = null;
                blackboard.IsFullBodyActionOccupied = false;
            }
        }
    }
}
