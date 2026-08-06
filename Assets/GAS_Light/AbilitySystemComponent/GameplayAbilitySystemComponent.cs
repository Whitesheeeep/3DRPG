using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 聚合单个 Owner 的 Attribute、Tag、GE 与 GA 运行时服务。
    /// ASC 不主动更新，由外部 Owner 负责初始化、Tick 和正常清理。
    /// </summary>
    public sealed class GameplayAbilitySystemComponent : MonoBehaviour
    {
        #region 字段
        private GameplayAbilityCtrl abilityController;
        private bool initialized;
        #endregion

        #region 属性
        /// <summary>获取 ASC 是否已经成功导入 AttributeSet。</summary>
        public bool IsInitialized => initialized;

        /// <summary>获取当前 Owner 的只读 GameplayTag 容器。</summary>
        public IReadOnlyGameplayTagContainer Tags { get; private set; }

        /// <summary>获取当前 Owner 的只读 Attribute 容器。</summary>
        public IReadOnlyGameplayAttributeContainer Attributes { get; private set; }

        /// <summary>获取当前实例的 GE Controller。</summary>
        public IGameEffectCtrl GameEffectCtrl { get; private set; }

        /// <summary>获取当前实例的 GA Controller。</summary>
        public IGameplayAbilityCtrl Abilities { get; private set; }

        /// <summary>获取当前 Target ASC 上 Active GE 的只读列表。</summary>
        public IReadOnlyList<GameEffectRuntime> ActiveEffects => GameEffectCtrl.ActiveEffects;

        /// <summary>获取当前 ASC 已授予 Ability Spec 的只读列表。</summary>
        public IReadOnlyList<GameplayAbilitySpec> GrantedAbilities => abilityController.GrantedAbilities;

        /// <summary>获取当前 ASC 中仍处于 Active 状态的 Ability Runtime 只读列表。</summary>
        public IReadOnlyList<GameplayAbilityRuntime> ActiveAbilities => abilityController.ActiveRuntimes;

        // GE Controller 需要通过 ASC 取得可变容器，但外部业务只能看到只读接口。
        internal GameplayTagCountContainer MutableTags { get; private set; }

        // Attribute 结算必须绕过只读门面，由 ASC 内部统一持有可变实例。
        internal GameplayAttributeContainer MutableAttributes { get; private set; }
        #endregion

        #region Unity 生命周期
        // 创建运行时容器与 Controller，但不导入任何作者配置。
        private void Awake()
        {
            MutableTags = new GameplayTagCountContainer();
            Tags = MutableTags;
            MutableAttributes = new GameplayAttributeContainer();
            Attributes = MutableAttributes;
            GameEffectCtrl = new GameEffectCtrl(this);
            abilityController = new GameplayAbilityCtrl(this);
            Abilities = abilityController;
        }

        // 组件销毁时释放 Tick 注册、Active GA、GE 和容器运行状态。
        private void OnDestroy() => Clear();
        #endregion

        #region 公开生命周期
        /// <summary>
        /// 导入一个或多个 AttributeSet。成功初始化后重复调用会直接返回。
        /// 初始化失败只记录日志，不进入初始化状态。
        /// </summary>
        /// <param name="attributeSets">由外部 Owner 提供的 AttributeSet 集合。</param>
        public void Initialize(IEnumerable<GameplayAttributeSet> attributeSets)
        {
            if (initialized) return;

            if (!MutableAttributes.TryInitialize(attributeSets, out string error))
            {
                Debug.Log($"[ASC] 初始化失败：{error}", this);
                return;
            }

            initialized = true;
        }

        /// <summary>
        /// 按固定顺序清理 Ability、GE、Tag 和 Attribute，使 ASC 回到可重新初始化状态。
        /// </summary>
        public void Clear()
        {
            abilityController.Clear();
            GameEffectCtrl.Clear();
            MutableTags.Reset();
            MutableAttributes.Clear();
            initialized = false;
        }

        /// <summary>
        /// 推进当前 ASC 的 GE 与 GA。GE 先更新，Ability Task 后更新。
        /// </summary>
        /// <param name="deltaTime">本次推进的秒数。</param>
        public void Tick(float deltaTime)
        {
            GameEffectCtrl.Tick(deltaTime);
            abilityController.Tick(deltaTime);
        }
        #endregion

        #region 只读快捷查询
        /// <summary>判断当前 ASC 是否拥有指定 Tag 或其层级匹配 Tag。</summary>
        /// <param name="tag">待查询的 GameplayTag。</param>
        /// <returns>拥有或匹配时返回 true。</returns>
        public bool HasTag(GameplayTag tag) => Tags.HasTag(tag);

        /// <summary>判断当前 ASC 是否显式拥有指定 Tag。</summary>
        /// <param name="tag">待查询的 GameplayTag。</param>
        /// <returns>显式拥有时返回 true。</returns>
        public bool HasTagExact(GameplayTag tag) => Tags.HasTagExact(tag);

        /// <summary>读取当前 ASC 中指定 Attribute 的 CurrentValue。</summary>
        /// <param name="attribute">待查询的 GameplayAttribute。</param>
        /// <param name="value">找到时返回 CurrentValue。</param>
        /// <returns>Attribute 存在时返回 true。</returns>
        public bool TryGetCurrentValue(GameplayAttribute attribute, out float value) =>
            Attributes.TryGetCurrentValue(attribute, out value);

        /// <summary>判断当前 ASC 是否存在指定 GE 的 Active Runtime。</summary>
        /// <param name="data">待查询的 GE 资产。</param>
        /// <returns>存在时返回 true。</returns>
        public bool HasActiveEffect(GameplayEffectData data) =>
            GameEffectCtrl.HasActiveEffect(data);
        #endregion

        #region GE 快捷操作
        /// <summary>将 Gameplay Effect 应用到当前 ASC；当前 ASC 作为 Target，Source 由参数指定。</summary>
        /// <param name="data">要应用的 Gameplay Effect 配置。</param>
        /// <param name="source">提供效果来源的 ASC。</param>
        /// <param name="level">本次应用等级，必须至少为 1。</param>
        /// <param name="setByCaller">本次应用的 SetByCaller 数据，可以为 null。</param>
        /// <param name="runtime">持续效果成功时返回 Active Runtime；Instant 效果成功时为 null。</param>
        /// <returns>底层 GE Controller 应用成功时返回 true。</returns>
        public bool TryApplyEffect(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            int level,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameEffectRuntime runtime) =>
            GameEffectCtrl.TryApply(data, source, level, setByCaller, out runtime);

        /// <summary>使用默认等级 1 且不携带 SetByCaller 数据应用 Gameplay Effect。</summary>
        /// <param name="data">要应用的 Gameplay Effect 配置。</param>
        /// <param name="source">提供效果来源的 ASC。</param>
        /// <param name="runtime">持续效果成功时返回 Active Runtime；Instant 效果成功时为 null。</param>
        /// <returns>底层 GE Controller 应用成功时返回 true。</returns>
        public bool TryApplyEffect(
            GameplayEffectData data,
            GameplayAbilitySystemComponent source,
            out GameEffectRuntime runtime) =>
            TryApplyEffect(data, source, 1, null, out runtime);

        /// <summary>移除属于当前 Target ASC 的 Active Gameplay Effect Runtime。</summary>
        /// <param name="runtime">要移除的 Active Runtime。</param>
        /// <returns>Runtime 属于当前 ASC 且成功移除时返回 true。</returns>
        public bool TryRemoveEffect(GameEffectRuntime runtime) =>
            GameEffectCtrl.TryRemove(runtime);
        #endregion

        #region GA 快捷操作
        /// <summary>向当前 ASC 授予指定等级的 Ability Spec。</summary>
        /// <param name="data">要授予的 Ability 作者数据。</param>
        /// <param name="level">初始 Ability 等级，必须至少为 1。</param>
        /// <returns>授予成功时返回有效 Handle，否则返回非法 Handle。</returns>
        public GameplayAbilityHandle GiveAbility(GameplayAbilityData data, int level) =>
            abilityController.GiveAbility(data, level);

        /// <summary>修改已授予 Ability Spec 后续激活使用的等级。</summary>
        /// <param name="handle">要修改的 Ability Spec Handle。</param>
        /// <param name="level">新等级，必须至少为 1。</param>
        /// <returns>Handle 属于当前 ASC 且等级修改成功时返回 true。</returns>
        public bool TrySetAbilityLevel(GameplayAbilityHandle handle, int level) =>
            abilityController.TrySetAbilityLevel(handle, level);

        /// <summary>根据 Handle 查询当前 ASC 已授予的 Ability Spec。</summary>
        /// <param name="handle">要查询的 Ability Spec Handle。</param>
        /// <param name="spec">找到时返回匹配的 Spec。</param>
        /// <returns>Spec 仍由当前 ASC 授予时返回 true。</returns>
        public bool TryGetAbilitySpec(GameplayAbilityHandle handle, out GameplayAbilitySpec spec) =>
            abilityController.TryGetAbilitySpec(handle, out spec);

        /// <summary>移除没有 Active Runtime 的已授予 Ability Spec。</summary>
        /// <param name="handle">要移除的 Ability Spec Handle。</param>
        /// <returns>Spec 存在且成功移除时返回 true。</returns>
        public bool TryRemoveAbility(GameplayAbilityHandle handle) =>
            abilityController.TryRemoveAbility(handle);

        /// <summary>使用可选 SetByCaller 数据激活已授予的 Ability。</summary>
        /// <param name="handle">要激活的 Ability Spec Handle。</param>
        /// <param name="setByCaller">本次激活的 SetByCaller 数据，可以为 null。</param>
        /// <param name="runtime">激活成功时返回创建的 Ability Runtime。</param>
        /// <returns>底层 Ability Controller 完成激活时返回 true。</returns>
        public bool TryActivateAbility(
            GameplayAbilityHandle handle,
            IReadOnlyDictionary<GameplayTag, float> setByCaller,
            out GameplayAbilityRuntime runtime) =>
            abilityController.TryActivate(handle, setByCaller, out runtime);

        /// <summary>不携带 SetByCaller 数据激活已授予的 Ability。</summary>
        /// <param name="handle">要激活的 Ability Spec Handle。</param>
        /// <param name="runtime">激活成功时返回创建的 Ability Runtime。</param>
        /// <returns>底层 Ability Controller 完成激活时返回 true。</returns>
        public bool TryActivateAbility(
            GameplayAbilityHandle handle,
            out GameplayAbilityRuntime runtime) =>
            TryActivateAbility(handle, null, out runtime);

        /// <summary>正常结束属于当前 ASC 的 Active Ability Runtime。</summary>
        /// <param name="runtime">要结束的 Ability Runtime。</param>
        /// <returns>Runtime 属于当前 ASC 且成功结束时返回 true。</returns>
        public bool TryEndAbility(GameplayAbilityRuntime runtime) =>
            abilityController.TryEnd(runtime);

        /// <summary>取消属于当前 ASC 的 Active Ability Runtime。</summary>
        /// <param name="runtime">要取消的 Ability Runtime。</param>
        /// <returns>Runtime 属于当前 ASC 且成功取消时返回 true。</returns>
        public bool TryCancelAbility(GameplayAbilityRuntime runtime) =>
            abilityController.TryCancel(runtime);
        #endregion

        #region Ability Tick 内部入口
        /// <summary>获取当前 Ability Controller 的 Tick 注册数量，仅供测试和诊断使用。</summary>
        internal int TickRegistrationCount => abilityController.TickRegistrationCount;

        // TickTask 通过 ASC 注册到所属 Ability Controller，避免依赖外部调度器实现。
        internal IUnRegister RegisterAbilityTick(Action<float> callback) =>
            abilityController.RegisterTick(callback);
        #endregion
    }
}