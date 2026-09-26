using System;
using System.Collections.Generic;
using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace RPG.NPC
{
    /// <summary>保存 NPC 的初始 GAS 属性、可授予技能与首版 Idle/Move 动画。</summary>
    [CreateAssetMenu(menuName = "RPG/NPC/NPC Config", fileName = "NPCConfig")]
    public sealed class NPCConfig : ScriptableObject
    {
        #region 配置字段

        [SerializeField, Required, AssetsOnly, LabelText("初始属性集")]
        private GameplayAttributeSet[] initialAttributeSets = Array.Empty<GameplayAttributeSet>();
        [SerializeField, Required, AssetsOnly, LabelText("可用技能")]
        private GameplayAbilityData[] grantedAbilities = Array.Empty<GameplayAbilityData>();
        [SerializeField, Required, AssetsOnly, LabelText("Idle 动画")]
        private TransitionAsset idleTransition;
        [SerializeField, Required, AssetsOnly, LabelText("Move 动画")]
        private TransitionAsset moveTransition;
        [SerializeField, Required, AssetsOnly, LabelText("Move 混合参数")]
        private StringAsset moveParameterX;

        #endregion

        #region 配置查询

        /// <summary>获取按作者顺序应用的初始属性集。</summary>
        public IReadOnlyList<GameplayAttributeSet> InitialAttributeSets =>
            initialAttributeSets ?? Array.Empty<GameplayAttributeSet>();

        /// <summary>获取初始化时授予 NPC 的技能列表。</summary>
        public IReadOnlyList<GameplayAbilityData> GrantedAbilities =>
            grantedAbilities ?? Array.Empty<GameplayAbilityData>();

        /// <summary>获取 NPC Idle 状态进入时播放的 Animancer Transition。</summary>
        public TransitionAsset IdleTransition => idleTransition;

        /// <summary>获取 NPC Move 状态进入时播放的 Animancer Transition。</summary>
        public TransitionAsset MoveTransition => moveTransition;

        /// <summary>获取 Move Mixer 中用于选择步行采样的参数。</summary>
        public StringAsset MoveParameterX => moveParameterX;

        #endregion

        #region 配置校验

        /// <summary>在 NPC 初始化前校验属性、技能与状态表现资源是否完整。</summary>
        /// <exception cref="InvalidOperationException">配置缺少必需资源或包含空条目。</exception>
        public void Validate()
        {
            if (initialAttributeSets == null || initialAttributeSets.Length == 0)
                throw new InvalidOperationException($"NPCConfig '{name}' 必须配置至少一个初始 AttributeSet。");
            for (int index = 0; index < initialAttributeSets.Length; index++)
            {
                if (initialAttributeSets[index] == null)
                    throw new InvalidOperationException($"NPCConfig '{name}' 的 AttributeSet[{index}] 未配置。");
            }

            if (grantedAbilities == null || grantedAbilities.Length == 0)
                throw new InvalidOperationException($"NPCConfig '{name}' 必须配置至少一个可用 Ability。");
            for (int index = 0; index < grantedAbilities.Length; index++)
            {
                if (grantedAbilities[index] == null)
                    throw new InvalidOperationException($"NPCConfig '{name}' 的 Ability[{index}] 未配置。");
                for (int previousIndex = 0; previousIndex < index; previousIndex++)
                {
                    if (ReferenceEquals(grantedAbilities[index], grantedAbilities[previousIndex]))
                        throw new InvalidOperationException(
                            $"NPCConfig '{name}' 重复配置 Ability '{grantedAbilities[index].name}'。");
                }
            }

            if (idleTransition == null || idleTransition.Transition == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置有效 Idle Transition。");
            if (moveTransition == null || moveTransition.Transition == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置有效 Move Transition。");
            if (moveParameterX == null)
                throw new InvalidOperationException($"NPCConfig '{name}' 未配置 Move Mixer 参数。");
        }

        #endregion
    }
}
