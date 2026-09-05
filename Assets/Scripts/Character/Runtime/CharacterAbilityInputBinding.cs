using System;
using RPG.PlayerInputSystem;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.Character
{
    /// <summary>配置角色固定战斗输入槽位与待激活能力的对应关系。</summary>
    [Serializable]
    public sealed class CharacterAbilityInputBinding
    {
        [SerializeField] private PlayerInputType inputType;
        [SerializeField] private GameplayAbilityData ability;

        /// <summary>获取来源输入类型。</summary>
        public PlayerInputType InputType => inputType;
        /// <summary>获取必须预先授予角色 ASC 的能力配置。</summary>
        public GameplayAbilityData Ability => ability;
    }
}
