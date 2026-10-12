using System;
using RPG.PlayerInputSystem;
using RPG.SkillSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>独立仲裁 Primary 普攻，并解析当前动作到普攻的时间轴入口。</summary>
    internal sealed class NormalAttackTransitionExecution
    {
        #region 依赖字段

        // 依赖字段：普攻段位、Press 快照与 GAS 激活事务由角色战斗系统统一维护。
        private readonly CharacterCombatSystem combatSystem;

        #endregion

        #region 构造与执行

        /// <summary>创建普通攻击转换执行器。</summary>
        /// <param name="sourceCombatSystem">维护普攻段位和激活流程的角色战斗系统。</param>
        /// <exception cref="ArgumentNullException">战斗系统依赖为空时抛出。</exception>
        internal NormalAttackTransitionExecution(CharacterCombatSystem sourceCombatSystem)
        {
            combatSystem = sourceCombatSystem ?? throw new ArgumentNullException(nameof(sourceCombatSystem));
        }

        /// <summary>按当前 FullBody Runtime 选择普攻的时间轴入口并尝试激活。</summary>
        /// <param name="inputRequests">当前玩家输入请求缓冲区。</param>
        /// <param name="currentFullBodyRuntime">当前 FullBody Runtime；为空表示从 Idle 启动。</param>
        /// <returns>普攻成功激活并消费 Primary Press 时返回 true。</returns>
        internal bool TryExecute(
            IPlayerInputRequestBuffer inputRequests,
            GameplayAbilityRuntime currentFullBodyRuntime)
        {
            SkillStartMode startMode = SkillStartMode.TimelineStart;
            if (currentFullBodyRuntime != null)
            {
                // 只判断当前 FullBody 是否允许被替换；普攻是否被阻断由新 GA 的 AbilityTags 交给 GAS 判断。
                if (!currentFullBodyRuntime.IsCancelable)
                    return false;

                if (currentFullBodyRuntime.AbilityTags.HasTag(GameplayTags.Tag_Skill_NormalAttack))
                    startMode = SkillStartMode.FirstActivePhase;
            }

            // Idle、受击转普攻及其他技能转普攻都保留从时间轴起点播放。
            return combatSystem.TryExecuteNormalAttackInput(inputRequests, startMode);
        }

        #endregion
    }
}
