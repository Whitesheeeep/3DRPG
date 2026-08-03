using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存 Gameplay Ability 的激活条件、Cost、Cooldown 与基础 GE 执行配置。</summary>
    [CreateAssetMenu(fileName = "GameplayAbilityData", menuName = "WSFrame/GAS/Gameplay Ability")]
    public sealed class GameplayAbilityData : ScriptableObject
    {
        #region 字段
        [SerializeField, TextArea, Tooltip("用于编辑器和日志显示的能力说明。")]
        private string description;
        [SerializeField, Tooltip("Source Tags 必须满足该查询才能激活能力；空查询表示不限制。")]
        private GameplayTagQuery activationTagQuery;
        [SerializeField, Tooltip("激活时应用到 Source 的单个 Instant Cost GE；可为空。")]
        private GameplayEffectData costEffect;
        [SerializeField, Tooltip("激活时应用到 Source 的 Duration 或 Infinite Cooldown GE；可为空。")]
        private GameplayEffectData cooldownEffect;
        [SerializeField, Tooltip("执行时以 Source 同时作为 Source 与 Target 应用的 GE。")]
        private List<GameplayEffectData> selfEffects = new();
        [SerializeField, Tooltip("执行时对外部 Targeting 提供的每个 Target 应用的 GE。")]
        private List<GameplayEffectData> targetEffects = new();
        #endregion

        #region 属性
        /// <summary>获取供编辑器和日志显示的能力说明。</summary>
        public string Description => description;
        /// <summary>获取 Source 在激活前必须满足的 Tag 查询。</summary>
        public GameplayTagQuery ActivationTagQuery => activationTagQuery;
        /// <summary>获取激活时应用到 Source 的 Cost GE。</summary>
        public GameplayEffectData CostEffect => costEffect;
        /// <summary>获取激活时应用到 Source 的 Cooldown GE。</summary>
        public GameplayEffectData CooldownEffect => cooldownEffect;
        /// <summary>获取执行时应用到 Source 自身的 GE 列表。</summary>
        public IReadOnlyList<GameplayEffectData> SelfEffects => selfEffects;
        /// <summary>获取执行时应用到外部目标集合的 GE 列表。</summary>
        public IReadOnlyList<GameplayEffectData> TargetEffects => targetEffects;
        #endregion
    }
}
