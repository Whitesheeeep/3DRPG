using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.GameplayAbilitySystem
{
    /// <summary>保存所有 Gameplay Ability 共用的激活条件、提交 GE、结果 Effects 与 Cue 配置。</summary>
    public abstract class GameplayAbilityData : ScriptableObject
    {
        #region 字段
        /// <summary>表示尚未 Bake 或非法 Ability 资产的保留 ID。</summary>
        public const int InvalidId = -1;

        [SerializeField, HideInInspector]
        private int abilityId = InvalidId;
        [SerializeField, TextArea, Tooltip("用于编辑器和日志显示的能力说明。")]
        private string description;
        [SerializeField, Tooltip("当前 Ability 的分类标签，供其他 Ability 通过 Cancel Tags 匹配并取消。")]
        private GameplayTag[] abilityTags = System.Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("当前 Ability 成功激活后，取消 Ability Tags 与任一标签层级匹配的 Active Ability。")]
        private GameplayTag[] cancelTags = System.Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("Source Tags 必须满足该查询才能激活能力；空查询表示不限制。")]
        private GameplayTagQuery activationTagQuery;
        [SerializeField, Tooltip("激活时应用到 Source 的 Instant Cost GE；可为空。")]
        private GameplayEffectData costEffect;
        [SerializeField, Tooltip("激活时应用到 Source 的 Duration 或 Infinite Cooldown GE；可为空。")]
        private GameplayEffectData cooldownEffect;
        [SerializeField, Tooltip("Ability 的统一结果 GE 列表，由具体 Data 或 Task 决定应用时机。")]
        private List<GameplayEffectData> effects = new();
        [SerializeField, Tooltip("Ability 成功执行后发布的 GameplayCueTag 列表。")]
        private GameplayTag[] cueTags = System.Array.Empty<GameplayTag>();
        #endregion

        #region 属性
        /// <summary>获取由 GameplayAbilityDatabase Bake 的全局稳定 AbilityId。</summary>
        public int AbilityId => abilityId;
        /// <summary>获取能力说明。</summary>
        public string Description => description;
        /// <summary>获取表示当前 Ability 分类身份的标签。</summary>
        public IReadOnlyList<GameplayTag> AbilityTags => abilityTags ?? System.Array.Empty<GameplayTag>();
        /// <summary>获取当前 Ability 成功激活时用于取消其他 Active Ability 的标签。</summary>
        public IReadOnlyList<GameplayTag> CancelTags => cancelTags ?? System.Array.Empty<GameplayTag>();
        /// <summary>获取 Source 在激活前必须满足的 Tag 查询。</summary>
        public GameplayTagQuery ActivationTagQuery => activationTagQuery;
        /// <summary>获取激活时应用到 Source 的 Cost GE。</summary>
        public GameplayEffectData CostEffect => costEffect;
        /// <summary>获取激活时应用到 Source 的 Cooldown GE。</summary>
        public GameplayEffectData CooldownEffect => cooldownEffect;
        /// <summary>获取该 Ability 配置的统一结果 GE 列表。</summary>
        public IReadOnlyList<GameplayEffectData> Effects => effects;
        /// <summary>获取 Ability 配置的 CueTag 列表。</summary>
        public IReadOnlyList<GameplayTag> CueTags => cueTags;
        /// <summary>获取同一 Spec 已有 Active Runtime 时采用的重复激活策略。</summary>
        public virtual GameplayAbilityReactivationPolicy ReactivationPolicy =>
            GameplayAbilityReactivationPolicy.AllowMultiple;

        // 让 Controller 在提交 Cost/Cooldown 前检查具体 Data 的运行时契约。
        internal virtual bool IsRuntimeConfigurationValid => true;
        #endregion

        #region Runtime 工厂
        /// <summary>通过多态工厂为本次激活创建独立 Runtime。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">被激活的已授予 Spec。</param>
        /// <param name="source">拥有并激活 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活的 SetByCaller 数据。</param>
        /// <returns>由具体 Ability Data 创建的 Runtime。</returns>
        internal GameplayAbilityRuntime CreateRuntimeInstance(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller) =>
            CreateRuntime(activationId, spec, source, setByCaller);

        /// <summary>由具体同步或异步 Data 创建对应 Runtime 类型及其私有状态。</summary>
        /// <param name="activationId">当前 Controller 分配的激活标识。</param>
        /// <param name="spec">被激活的已授予 Spec。</param>
        /// <param name="source">拥有并激活 Ability 的 Source ASC。</param>
        /// <param name="setByCaller">本次激活的 SetByCaller 数据。</param>
        /// <returns>新建的 Ability Runtime。</returns>
        protected abstract GameplayAbilityRuntime CreateRuntime(
            int activationId,
            GameplayAbilitySpec spec,
            GameplayAbilitySystemComponent source,
            IReadOnlyDictionary<GameplayTag, float> setByCaller);
        #endregion

        #region 效果与 Cue 提交

        /// <summary>向指定 Target 应用统一结果 GE 列表，但不决定激活、命中等业务时机。</summary>
        /// <param name="source">本次 GE 的来源 ASC。</param>
        /// <param name="target">接收 GE 的目标 ASC。</param>
        /// <param name="level">Ability 激活等级快照。</param>
        /// <param name="setByCaller">本次激活的 SetByCaller 快照。</param>
        /// <param name="retainedEffects">可选的 Active GE Runtime 收集容器。</param>
        /// <returns>成功提交的 GE 数量。</returns>
        internal int ApplyConfiguredEffects(
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            ICollection<GameEffectRuntime> retainedEffects = null)
        {
            if (source == null || target == null || level < 1 || effects == null)
                return 0;

            int appliedCount = 0;
            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null || !target.GameEffectCtrl.TryApply(
                        effect,
                        source,
                        level,
                        setByCaller,
                        out GameEffectRuntime activeEffect))
                    continue;

                appliedCount++;
                if (activeEffect != null) retainedEffects?.Add(activeEffect);
            }

            return appliedCount;
        }

        /// <summary>在具体 Ability 或 Task 确定的业务时机发布所有作者 Cue 配置。</summary>
        /// <param name="eventType">本次 Cue 生命周期事件。</param>
        /// <param name="source">Cue 来源 ASC。</param>
        /// <param name="target">接收并处理 Cue 的 ASC。</param>
        /// <param name="effectRuntime">可选的来源 GE Runtime。</param>
        /// <param name="abilityRuntime">可选的来源 GA Runtime。</param>
        /// <param name="position">可选的显式世界位置。</param>
        /// <param name="rotation">可选的显式世界旋转。</param>
        /// <param name="attachTransform">可选的显式挂点。</param>
        internal void PublishConfiguredCues(
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime effectRuntime = null,
            GameplayAbilityRuntime abilityRuntime = null,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform attachTransform = null)
        {
            if (source == null || target == null || cueTags == null) return;
            for (int i = 0; i < cueTags.Length; i++)
            {
                GameplayCueRequest request;
                if (position.HasValue || attachTransform != null)
                {
                    request = new GameplayCueRequest(
                        cueTags[i], eventType, source, target, effectRuntime, abilityRuntime,
                        position ?? target.transform.position,
                        rotation ?? Quaternion.identity,
                        attachTransform);
                }
                else
                {
                    request = new GameplayCueRequest(
                        cueTags[i], eventType, source, target, effectRuntime, abilityRuntime);
                }
                target.PublishGameplayCue(request);
            }
        }
        #endregion
    }
}
