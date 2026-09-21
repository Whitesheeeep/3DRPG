using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;
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
        [SerializeField, Tooltip("面向技能栏、冷却 UI 和其他业务界面的显示名称；为空时回退到资产名。")]
        private string abilityName;
        [SerializeField, Tooltip("面向技能栏和其他业务界面的技能图标；可为空。")]
        private Sprite icon;
        [SerializeField, TextArea, Tooltip("用于编辑器和日志显示的能力说明。")]
        private string description;
        [SerializeField, Tooltip("当前 Ability 的分类标签，供其他 Ability 通过 Cancel Tags 匹配并取消。")]
        private GameplayTag[] abilityTags = System.Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("当前 Ability 成功激活后，取消 Ability Tags 与任一标签层级匹配的 Active Ability。")]
        private GameplayTag[] cancelTags = System.Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("当前 Ability Active 时阻止匹配 AbilityTags 的其他 Ability 激活；仅作用于 Ability Controller，不写入 ASC Owner Tags。")]
        private GameplayTag[] blockAbilityTags = System.Array.Empty<GameplayTag>();
        [SerializeField, Tooltip("当前 Ability 是否接受普通取消请求；系统清理仍可强制回收。")]
        private bool isCancelable = true;
        [SerializeField, Tooltip("Source Tags 必须满足该查询才能激活能力；空查询表示不限制。")]
        private GameplayTagQuery activationTagQuery;
        [SerializeField, Tooltip("激活时应用到 Source 的 Instant Cost GE；可为空。")]
        private GameplayEffectData costEffect;
        [SerializeField, Tooltip("激活时应用到 Source 的 Duration 或 Infinite Cooldown GE；可为空。")]
        private GameplayEffectData cooldownEffect;
        [SerializeField, Tooltip("根据 Ability Level 求值的技能伤害倍率；最终会写入 Data.Damage.Multiplier。")]
        private GameplayScalableFloat damageMultiplier = new(1f);
        [SerializeField, Tooltip("Ability 的统一结果 GE 列表，由具体 Data 或 Task 决定应用时机。")]
        private List<GameplayEffectData> effects = new();
        [SerializeField, Tooltip("Ability 成功执行后发布的 GameplayCueTag 列表。")]
        private GameplayTag[] cueTags = System.Array.Empty<GameplayTag>();
        #endregion

        #region 属性
        /// <summary>获取由 GameplayAbilityDatabase Bake 的全局稳定 AbilityId。</summary>
        public int AbilityId => abilityId;
        /// <summary>获取面向业务界面的技能显示名称；未填写时回退到 Unity 资产名。</summary>
        public string Name => string.IsNullOrWhiteSpace(abilityName) ? name : abilityName;
        /// <summary>获取面向业务界面的技能图标；未配置时返回 null。</summary>
        public Sprite Icon => icon;
        /// <summary>获取能力说明。</summary>
        public string Description => description;
        /// <summary>获取表示当前 Ability 分类身份的标签。</summary>
        public IReadOnlyList<GameplayTag> AbilityTags => abilityTags ?? System.Array.Empty<GameplayTag>();
        /// <summary>获取当前 Ability 成功激活时用于取消其他 Active Ability 的标签。</summary>
        public IReadOnlyList<GameplayTag> CancelTags => cancelTags ?? System.Array.Empty<GameplayTag>();
        /// <summary>获取当前 Ability Active 时阻止其他 Ability 激活的标签。</summary>
        public IReadOnlyList<GameplayTag> BlockAbilityTags =>
            blockAbilityTags ?? System.Array.Empty<GameplayTag>();
        /// <summary>获取当前 Ability 是否接受普通取消请求。</summary>
        public bool IsCancelable => isCancelable;
        /// <summary>获取 Source 在激活前必须满足的 Tag 查询。</summary>
        public GameplayTagQuery ActivationTagQuery => activationTagQuery;
        /// <summary>获取激活时应用到 Source 的 Cost GE。</summary>
        public GameplayEffectData CostEffect => costEffect;
        /// <summary>获取激活时应用到 Source 的 Cooldown GE。</summary>
        public GameplayEffectData CooldownEffect => cooldownEffect;
        /// <summary>获取根据 Ability Level 求值的技能伤害倍率配置。</summary>
        public GameplayScalableFloat DamageMultiplier => damageMultiplier;
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

        /// <summary>使用 Ability Level 求出本次激活的最终伤害倍率。</summary>
        /// <param name="abilityLevel">本次 Ability 激活等级。</param>
        /// <param name="multiplier">求值成功时返回最终倍率。</param>
        /// <returns>配置存在、求值有限且不小于零时返回 true。</returns>
        public bool TryEvaluateDamageMultiplier(int abilityLevel, out float multiplier)
        {
            if (damageMultiplier == null)
            {
                multiplier = default;
                return false;
            }

            if (!damageMultiplier.TryEvaluate(abilityLevel, out multiplier) ||
                float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
            {
                multiplier = default;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 根据本次 Ability 的等级和 SetByCaller 创建并封存所有结果 GE Spec。
        /// </summary>
        /// <param name="source">本次 Ability 的 Source ASC。</param>
        /// <param name="abilityLevel">本次 Ability 的等级快照。</param>
        /// <param name="activationSetByCaller">Ability 激活时的 SetByCaller 数据。</param>
        /// <param name="specs">成功时返回所有已封存的结果 GE Spec。</param>
        /// <returns>全部配置 GE 都成功创建和封存时返回 true。</returns>
        internal bool TryCreateConfiguredEffectSpecs(
            GameplayAbilitySystemComponent source,
            int abilityLevel,
            IReadOnlyDictionary<GameplayTag, float> activationSetByCaller,
            out IReadOnlyList<GameplayEffectSpec> specs)
        {
            specs = Array.Empty<GameplayEffectSpec>();
            if (source == null || abilityLevel < 1 ||
                !TryEvaluateDamageMultiplier(abilityLevel, out float multiplier))
                return false;

            // key：SetByCaller GameplayTag；value：本次激活冻结的输入值。
            var setByCallerMagnitudeByTagMap = new Dictionary<GameplayTag, float>();
            if (activationSetByCaller != null)
                foreach (KeyValuePair<GameplayTag, float> pair in activationSetByCaller)
                    setByCallerMagnitudeByTagMap[pair.Key] = pair.Value;
            setByCallerMagnitudeByTagMap[GameplayTags.Tag_Data_Damage_Multiplier] = multiplier;

            var createdSpecs = new List<GameplayEffectSpec>(effects?.Count ?? 0);
            if (effects == null)
            {
                specs = createdSpecs;
                return true;
            }

            for (int i = 0; i < effects.Count; i++)
            {
                GameplayEffectData effect = effects[i];
                if (effect == null ||
                    !source.TryCreateOutgoingEffectSpec(
                        effect,
                        abilityLevel,
                        setByCallerMagnitudeByTagMap,
                        out GameplayEffectSpec spec) ||
                    !spec.TrySeal())
                {
                    specs = Array.Empty<GameplayEffectSpec>();
                    return false;
                }

                createdSpecs.Add(spec);
            }

            specs = createdSpecs;
            return true;
        }

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
            if (source == null || target == null || level < 1)
                return 0;

            if (!TryCreateConfiguredEffectSpecs(source, level, setByCaller, out IReadOnlyList<GameplayEffectSpec> specs))
                return 0;

            int appliedCount = 0;
            for (int i = 0; i < specs.Count; i++)
            {
                if (!target.GameEffectCtrl.TryApply(
                        specs[i],
                        out GameplayEffectApplicationResult applicationResult))
                    continue;

                appliedCount++;
                if (applicationResult.ActiveEffect != null)
                    retainedEffects?.Add(applicationResult.ActiveEffect);
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
        /// <param name="effectSpec">可选的本次命中 GE Spec。</param>
        /// <param name="applicationResult">可选的本次命中 GE 应用结果。</param>
        internal void PublishConfiguredCues(
            GameplayCueEventType eventType,
            GameplayAbilitySystemComponent source,
            GameplayAbilitySystemComponent target,
            GameEffectRuntime effectRuntime = null,
            GameplayAbilityRuntime abilityRuntime = null,
            Vector3? position = null,
            Quaternion? rotation = null,
            Transform attachTransform = null,
            GameplayEffectSpec effectSpec = null,
            GameplayEffectApplicationResult applicationResult = null)
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
                        attachTransform,
                        effectSpec,
                        applicationResult);
                }
                else
                {
                    request = new GameplayCueRequest(
                        cueTags[i], eventType, source, target, effectRuntime, abilityRuntime,
                        effectSpec,
                        applicationResult);
                }
                target.PublishGameplayCue(request);
            }
        }
        #endregion
    }
}
