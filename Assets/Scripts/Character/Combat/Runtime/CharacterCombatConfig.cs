using System;
using System.Collections.Generic;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>保存单个角色的普通攻击连段和固定技能输入槽位配置。</summary>
    [Serializable]
    public sealed class CharacterCombatConfig
    {
        #region 配置字段

        [SerializeField, LabelText("普攻连段")]
        private List<GameplayAbilityData> normalAttackAbilities = new();

        [SerializeField, LabelText("技能输入绑定")]
        private List<CharacterAbilityInputBinding> skillInputBindings = new();

        #endregion

        #region 属性

        /// <summary>获取按连段顺序排列的普通攻击能力。</summary>
        public IReadOnlyList<GameplayAbilityData> NormalAttackAbilities => normalAttackAbilities;

        /// <summary>获取按作者配置顺序排列的技能输入绑定。</summary>
        public IReadOnlyList<CharacterAbilityInputBinding> SkillInputBindings => skillInputBindings;

        #endregion

        #region 校验

        /// <summary>校验战斗配置中的引用、技能输入类型和槽位唯一性。</summary>
        /// <param name="characterConfigName">所属 CharacterConfig 的资源名称。</param>
        /// <exception cref="InvalidOperationException">配置引用或技能槽位不满足契约时抛出。</exception>
        public void Validate(string characterConfigName)
        {
            if (normalAttackAbilities == null)
                throw new InvalidOperationException($"CharacterConfig '{characterConfigName}' 的普通攻击连段列表为空引用。");
            for (int index = 0; index < normalAttackAbilities.Count; index++)
            {
                if (normalAttackAbilities[index] == null)
                    throw new InvalidOperationException(
                        $"CharacterConfig '{characterConfigName}' 的普通攻击连段第 {index + 1} 段未配置 GameplayAbilityData。");
            }

            if (skillInputBindings == null)
                throw new InvalidOperationException($"CharacterConfig '{characterConfigName}' 的技能输入绑定列表为空引用。");

            var configuredSkillInputs = new HashSet<PlayerInputType>();
            for (int index = 0; index < skillInputBindings.Count; index++)
            {
                CharacterAbilityInputBinding binding = skillInputBindings[index];
                if (binding == null)
                    throw new InvalidOperationException(
                        $"CharacterConfig '{characterConfigName}' 的技能输入绑定第 {index + 1} 项为空。");
                if (!IsSkillInputType(binding.InputType))
                    throw new InvalidOperationException(
                        $"CharacterConfig '{characterConfigName}' 的技能输入绑定第 {index + 1} 项使用了非技能输入 {binding.InputType}。Primary 只用于普通攻击连段。");
                if (binding.Ability == null)
                    throw new InvalidOperationException(
                        $"CharacterConfig '{characterConfigName}' 的技能输入 {binding.InputType} 未配置 GameplayAbilityData。");
                if (!configuredSkillInputs.Add(binding.InputType))
                    throw new InvalidOperationException(
                        $"CharacterConfig '{characterConfigName}' 的技能输入 {binding.InputType} 被重复配置。");
            }
        }

        /// <summary>判断输入是否属于 CombatSystem 管理的固定技能槽位。</summary>
        /// <param name="inputType">待检查的输入类型。</param>
        /// <returns>输入属于 Secondary 或 Skill1 至 Skill4 时返回 true。</returns>
        private static bool IsSkillInputType(PlayerInputType inputType) =>
            inputType == PlayerInputType.Secondary ||
            inputType == PlayerInputType.Skill1 ||
            inputType == PlayerInputType.Skill2 ||
            inputType == PlayerInputType.Skill3 ||
            inputType == PlayerInputType.Skill4;

        #endregion
    }
}
