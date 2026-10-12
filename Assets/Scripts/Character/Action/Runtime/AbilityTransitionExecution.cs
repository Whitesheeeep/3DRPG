using System;
using RPG.PlayerInputSystem;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>在当前 FullBody 动作允许交接时执行技能槽输入激活。</summary>
    internal sealed class AbilityTransitionExecution
    {
        #region 依赖字段

        // 依赖字段：复用角色既有的 Ability 授予、激活与 Press 消费事务。
        private readonly CharacterCombatSystem combatSystem;

        #endregion

        #region 构造与执行

        /// <summary>创建角色能力输入执行器。</summary>
        /// <param name="sourceCombatSystem">负责 Ability Handle 和输入消费的角色战斗系统。</param>
        internal AbilityTransitionExecution(CharacterCombatSystem sourceCombatSystem)
        {
            combatSystem = sourceCombatSystem ?? throw new ArgumentNullException(nameof(sourceCombatSystem));
        }

        /// <summary>在当前 FullBody Runtime 可取消时，尝试技能槽输入；具体能力阻断由 GAS 检查。</summary>
        /// <param name="inputRequests">当前玩家输入请求缓冲区。</param>
        /// <param name="currentFullBodyRuntime">当前 FullBody Runtime；为空表示无动作占据。</param>
        /// <returns>技能槽 Ability 成功激活并消费对应输入阶段时返回 true。</returns>
        internal bool TryExecute(
            IPlayerInputRequestBuffer inputRequests,
            GameplayAbilityRuntime currentFullBodyRuntime)
        {
            if (currentFullBodyRuntime != null)
            {
                // 这里仅约束正在占据 FullBody 的动作，其他并行 GA 不参与角色动作交接判断。
                if (!currentFullBodyRuntime.IsCancelable)
                    return false;
            }

            return combatSystem.TryExecuteSkillInput(inputRequests);
        }

        #endregion
    }
}
