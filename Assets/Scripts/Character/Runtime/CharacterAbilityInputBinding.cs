using System;
using RPG.PlayerInputSystem;
using UnityEngine;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>配置角色离散输入、Intent Tag 与待激活能力的对应关系。</summary>
    [Serializable]
    public sealed class CharacterAbilityInputBinding
    {
        [SerializeField] private PlayerInputType inputType;
        [SerializeField] private GameplayTag intentTag;
        [SerializeField] private GameplayAbilityData ability;
        // 阻断当前输入的 Tag 列表，暂时只支持 AbilityTags 与 EnvironmentTags，后续可扩展到其他来源。
        [SerializeField] private GameplayTag[] blockedTags = Array.Empty<GameplayTag>();

        /// <summary>获取来源输入类型。</summary>
        public PlayerInputType InputType => inputType;
        /// <summary>获取业务消费使用的 Intent。</summary>
        public GameplayTag IntentTag => intentTag;
        /// <summary>获取必须预先授予角色 ASC 的能力配置。</summary>
        public GameplayAbilityData Ability => ability;

        /// <summary>在 AbilityTags 和稳定玩家 EnvironmentTags 上判断当前输入是否被阻断。</summary>
        /// <param name="blackboard">当前玩家状态。</param>
        /// <returns>配置合法且没有阻断 Tag 时返回 true。</returns>
        internal bool CanPublish(State.PlayerStateBlackboard blackboard)
        {
            if (!intentTag.IsValid || ability == null)
                throw new InvalidOperationException("[CharacterInput] 能力输入绑定必须配置有效 Intent Tag 和 Ability。");
            foreach (GameplayTag tag in blockedTags)
                if (blackboard.AbilityTags.HasTag(tag) || blackboard.EnvironmentTags.HasTag(tag)) return false;
            return true;
        }
    }
}
