using System;
using RPG.PlayerInputSystem;

namespace RPG.Character
{
    /// <summary>在 Ability 转换窗口中复用 CharacterCombatSystem 的现有激活事务。</summary>
    internal sealed class AbilityTransitionExecution
    {
        // 依赖字段：复用角色既有的 Ability 授予、激活与 Press 消费事务。
        private readonly CharacterCombatSystem combatSystem;

        /// <summary>创建角色能力输入执行器。</summary>
        /// <param name="sourceCombatSystem">负责 Ability Handle 和输入消费的角色战斗系统。</param>
        internal AbilityTransitionExecution(CharacterCombatSystem sourceCombatSystem)
        {
            combatSystem = sourceCombatSystem ?? throw new ArgumentNullException(nameof(sourceCombatSystem));
        }

        /// <summary>
        /// 尝试执行输入，但不携带普通攻击实时交接标记。
        /// </summary>
        /// <param name="inputRequests">当前玩家输入请求缓冲区。</param>
        /// <returns>存在 Ability 成功激活并消费 Press 时返回 true。</returns>
        internal bool TryExecute(IPlayerInputRequestBuffer inputRequests) =>
            TryExecute(inputRequests, false);

        /// <summary>尝试执行本帧第一个可激活的技能或普通攻击输入。</summary>
        /// <param name="inputRequests">当前玩家输入请求缓冲区。</param>
        /// <param name="useComboHandoff">是否将本次 Primary 普攻标记为实时连段交接。</param>
        /// <returns>存在 Ability 成功激活并消费 Press 时返回 true。</returns>
        internal bool TryExecute(IPlayerInputRequestBuffer inputRequests, bool useComboHandoff) =>
            combatSystem.TryExecuteAbilityInput(inputRequests, useComboHandoff);
    }
}
