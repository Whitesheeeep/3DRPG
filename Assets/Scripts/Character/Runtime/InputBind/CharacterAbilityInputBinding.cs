using System;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>配置角色固定技能输入槽位与待激活能力的对应关系。</summary>
    [Serializable]
    public sealed class CharacterAbilityInputBinding
    {
        [SerializeField, LabelText("技能输入")]
        private PlayerInputType inputType;
        [SerializeField, LabelText("Gameplay Ability")]
        private GameplayAbilityData ability;

        /// <summary>获取 Secondary 或 Skill1 至 Skill4 输入槽位。</summary>
        public PlayerInputType InputType => inputType;
        /// <summary>获取由 CharacterCombatSystem 预先授予角色 ASC 的能力配置。</summary>
        public GameplayAbilityData Ability => ability;
    }
}
