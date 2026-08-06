using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存所有 Gameplay Ability 共用的激活条件、Cost 与 Cooldown 配置。</summary>
    public abstract class GameplayAbilityData : ScriptableObject
    {
        #region 字段
        [SerializeField, TextArea, Tooltip("用于编辑器和日志显示的能力说明。")]
        private string description;
        [SerializeField, Tooltip("Source Tags 必须满足该查询才能激活能力；空查询表示不限制。")]
        private GameplayTagQuery activationTagQuery;
        [SerializeField, Tooltip("激活时应用到 Source 的 Instant Cost GE；可为空。")]
        private GameplayEffectData costEffect;
        [SerializeField, Tooltip("激活时应用到 Source 的 Duration 或 Infinite Cooldown GE；可为空。")]
        private GameplayEffectData cooldownEffect;
        #endregion

        #region 属性
        /// <summary>获取能力说明。</summary>
        public string Description => description;
        /// <summary>获取 Source 在激活前必须满足的 Tag 查询。</summary>
        public GameplayTagQuery ActivationTagQuery => activationTagQuery;
        /// <summary>获取激活时应用到 Source 的 Cost GE。</summary>
        public GameplayEffectData CostEffect => costEffect;
        /// <summary>获取激活时应用到 Source 的 Cooldown GE。</summary>
        public GameplayEffectData CooldownEffect => cooldownEffect;

        // 让 Controller 在提交 Cost/Cooldown 前检查具体 Data 的运行时契约。
        internal virtual bool IsRuntimeConfigurationValid => true;
        #endregion

        #region Runtime 工厂
        // 统一 Controller 到多态工厂的入口，避免 Controller 判断具体 Ability 类型。
        internal GameplayAbilityRuntime CreateRuntimeInstance(
            int activationId,
            GameplayAbilitySpec spec,
            AbilitySystemComponentBase source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            CreateRuntime(activationId, spec, source, setByCaller);

        // 具体同步或异步 Data 决定 Runtime 类型及其私有状态。
        protected abstract GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            AbilitySystemComponentBase source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller);
        #endregion
    }
}
