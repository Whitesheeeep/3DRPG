using System;
using System.Collections.Generic;
using WS_Modules;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.AttributeSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>把一个稳定角色实例的等级和 Resource 快照绑定到一个 ASC 生命周期。用于在角色生命周期内同步和管理其属性和资源。桥接角色实例与 ASC 的属性和资源管理。</summary>
    internal sealed class CharacterRuntimeAttributeBinding : IDisposable
    {
        #region 依赖字段

        private readonly CharacterInstance instance;
        private readonly GameplayAbilitySystemComponent abilitySystemComponent;
        private readonly CharacterAttributeProgressionResolver progressionResolver;
        // key：Resource AttributeId；value：角色配置声明的资源规则。
        private readonly Dictionary<int, CharacterResourceRule> resourceRuleByAttributeIdMap = new();
        private IUnRegister instanceChangedUnregister;
        private IUnRegister rosterRestoredUnregister;
        private bool suppressResourceCapture;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建并注册角色实例与 ASC 的运行时绑定。</summary>
        /// <param name="instance">稳定角色实例。</param><param name="abilitySystemComponent">角色 ASC。</param>
        /// <param name="progressionResolver">读取烘焙等级属性的解析器。</param>
        internal CharacterRuntimeAttributeBinding(
            CharacterInstance instance,
            GameplayAbilitySystemComponent abilitySystemComponent,
            CharacterAttributeProgressionResolver progressionResolver)
        {
            this.instance = instance ?? throw new ArgumentNullException(nameof(instance));
            this.abilitySystemComponent = abilitySystemComponent ?? throw new ArgumentNullException(nameof(abilitySystemComponent));
            this.progressionResolver = progressionResolver ?? throw new ArgumentNullException(nameof(progressionResolver));
            IReadOnlyList<CharacterResourceRule> rules = instance.Config.ResourceRules;
            for (int index = 0; index < rules.Count; index++)
            {
                CharacterResourceRule rule = rules[index] ?? throw new InvalidOperationException("角色 ResourceRules 包含空项。");
                resourceRuleByAttributeIdMap.Add(rule.ResourceAttribute.Id, rule);
            }

            // 先完成初始 Resource 提交，再注册长期事件，避免构造失败时留下半成品订阅。
            InitializeResourceValues();
            abilitySystemComponent.AttributeChanged += HandleAttributeChanged;
            // 注册角色实例进度变化事件，确保等级变化时同步 ASC BaseValue 与 Resource CurrentValue。
            instanceChangedUnregister = WSEventSystem.Register_Type<CharacterInstanceChangedEvent>(
                typeof(CharacterInstanceChangedEvent), HandleInstanceChanged);
            // 注册存档恢复事件，确保恢复后重新应用等级 BaseValue 与 Resource 快照。
            rosterRestoredUnregister = WSEventSystem.Register_Type<CharacterRosterRestoredEvent>(
                typeof(CharacterRosterRestoredEvent), HandleRosterRestored);
        }

        /// <summary>注销事件并释放运行时绑定。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            abilitySystemComponent.AttributeChanged -= HandleAttributeChanged;
            instanceChangedUnregister?.UnRegister();
            instanceChangedUnregister = null;
            rosterRestoredUnregister?.UnRegister();
            rosterRestoredUnregister = null;
            UnityEngine.Debug.Log($"[CharacterRuntimeAttributeBinding] 解绑角色 Resource，character={instance.CharacterId}。");
        }

        #endregion

        #region 初始化与等级同步

        /// <summary>按资源规则初始化新角色资源或恢复存档快照。</summary>
        private void InitializeResourceValues()
        {
            var values = new List<GameplayAttributeValue>(resourceRuleByAttributeIdMap.Count);
            foreach (KeyValuePair<int, CharacterResourceRule> pair in resourceRuleByAttributeIdMap)
            {
                CharacterResourceRule rule = pair.Value;
                float value;

                // 优先使用存档快照的 Resource CurrentValue 作为初始值，避免存档恢复后容量下降导致资源溢出。
                if (instance.TryGetResourceCurrentValue(rule.ResourceAttribute, out float savedValue))
                    value = savedValue;
                // 类型为 FullCapacity 的资源在存档恢复时优先使用当前容量作为初始值，避免存档恢复后容量下降导致资源溢出。
                else if (rule.InitialValueMode == CharacterResourceInitialValueMode.FullCapacity)
                {
                    if (!abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out value))
                        throw new InvalidOperationException($"角色 {instance.CharacterId} 的容量 Attribute 不存在：{rule.CapacityAttribute}。");
                }
                // 类型为 DefaultValue 的资源在存档恢复时使用 Attribute 默认值作为初始值，避免存档恢复后容量下降导致资源溢出。
                else if (!abilitySystemComponent.Attributes.TryGetDefinition(rule.ResourceAttribute, out GameplayAttributeDefinition definition))
                    throw new InvalidOperationException($"角色 {instance.CharacterId} 的 Resource Attribute 不存在：{rule.ResourceAttribute}。");
                else
                    value = definition.DefaultValue;
                // 容量关系属于角色资源规则，恢复旧快照时先按当前等级的容量做一次动态上限裁剪。
                if (rule.CapacityAttribute.IsValid &&
                    abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out float capacityValue))
                    value = Math.Min(value, capacityValue);
                values.Add(new GameplayAttributeValue(rule.ResourceAttribute, value));
            }

            suppressResourceCapture = true;
            try
            {
                if (values.Count > 0 && !abilitySystemComponent.TrySetResourceCurrentValues(values))
                    throw new InvalidOperationException($"角色 {instance.CharacterId} 的 Resource 初始值未能通过 ASC 校验。");
            }
            finally
            {
                suppressResourceCapture = false;
            }
            CaptureResourceValues();
            UnityEngine.Debug.Log($"[CharacterRuntimeAttributeBinding] 完成角色 Resource 初始化，character={instance.CharacterId}, resourceCount={values.Count}。");
        }

        /// <summary>响应角色等级变化，保留容量型资源百分比并更新 ASC BaseValue。</summary>
        private void SynchronizeLevel()
        {
            var previousResourceValuesByAttributeIdMap = new Dictionary<int, float>();
            var previousCapacityValuesByAttributeIdMap = new Dictionary<int, float>();
            foreach (CharacterResourceRule rule in resourceRuleByAttributeIdMap.Values)
            {
                if (abilitySystemComponent.TryGetCurrentValue(rule.ResourceAttribute, out float resourceValue))
                    previousResourceValuesByAttributeIdMap[rule.ResourceAttribute.Id] = resourceValue;
                if (rule.CapacityAttribute.IsValid && abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out float capacityValue))
                    previousCapacityValuesByAttributeIdMap[rule.ResourceAttribute.Id] = capacityValue;
            }

            IReadOnlyList<GameplayAttributeValue> baseValues = progressionResolver.ResolveBaseValues(instance.Config, instance.Level);
            suppressResourceCapture = true;
            try
            {
                if (!abilitySystemComponent.TryApplyBaseValues(baseValues))
                    throw new InvalidOperationException($"角色 {instance.CharacterId} 的等级 BaseValue 无法同步到 ASC。");

                // 计算每个资源的新值，保留容量型资源的百分比。
                var resourceValues = new List<GameplayAttributeValue>(resourceRuleByAttributeIdMap.Count);

                foreach (CharacterResourceRule rule in resourceRuleByAttributeIdMap.Values)
                {
                    float oldValue = previousResourceValuesByAttributeIdMap.TryGetValue(rule.ResourceAttribute.Id, out float savedResourceValue)
                        ? savedResourceValue
                        : 0f;
                    float nextValue = oldValue;
                    if (rule.CapacityAttribute.IsValid &&
                        abilitySystemComponent.TryGetCurrentValue(rule.CapacityAttribute, out float newCapacity))
                    {
                        float oldCapacity = previousCapacityValuesByAttributeIdMap.TryGetValue(rule.ResourceAttribute.Id, out float previousCapacity)
                            ? previousCapacity
                            : 0f;
                        float ratio = oldCapacity > 0f ? oldValue / oldCapacity : 0f;
                        nextValue = newCapacity * ratio;
                    }
                    resourceValues.Add(new GameplayAttributeValue(rule.ResourceAttribute, nextValue));
                }
                if (resourceValues.Count > 0 && !abilitySystemComponent.TrySetResourceCurrentValues(resourceValues))
                    throw new InvalidOperationException($"角色 {instance.CharacterId} 的等级资源同步未能通过 ASC 校验。");
            }
            finally
            {
                suppressResourceCapture = false;
            }
            CaptureResourceValues();
            UnityEngine.Debug.Log($"[CharacterRuntimeAttributeBinding] 已按等级刷新角色 ASC，character={instance.CharacterId}, level={instance.Level}。");
        }

        #endregion

        #region 事件处理

        /// <summary>仅把声明的 Resource CurrentValue 镜像到稳定实例。</summary>
        private void HandleAttributeChanged(GameplayAttribute attribute, float oldValue, float newValue)
        {
            if (!suppressResourceCapture && resourceRuleByAttributeIdMap.ContainsKey(attribute.Id))
                instance.CommitResourceCurrentValue(attribute, newValue);
        }

        /// <summary>接收当前角色实例进度事件并保持 ASC 等级属性同步。</summary>
        private void HandleInstanceChanged(CharacterInstanceChangedEvent changeEvent)
        {
            if (disposed || !ReferenceEquals(changeEvent.Instance, instance) ||
                changeEvent.ChangeType != CharacterInstanceChangeType.ProgressUpdated)
                return;
            SynchronizeLevel();
        }

        /// <summary>存档恢复后重新应用等级 BaseValue 与已经保存的 Resource 快照。</summary>
        /// <param name="restoredEvent">角色存档恢复事件。</param>
        private void HandleRosterRestored(CharacterRosterRestoredEvent restoredEvent)
        {
            if (disposed) return;
            IReadOnlyList<GameplayAttributeValue> baseValues = progressionResolver.ResolveBaseValues(instance.Config, instance.Level);
            suppressResourceCapture = true;
            try
            {
                if (!abilitySystemComponent.TryApplyBaseValues(baseValues))
                    throw new InvalidOperationException($"角色 {instance.CharacterId} 的存档恢复 BaseValue 同步失败。");
            }
            finally
            {
                suppressResourceCapture = false;
            }
            InitializeResourceValues();
        }

        /// <summary>从 ASC 读取全部声明资源并更新实例快照。</summary>
        private void CaptureResourceValues()
        {
            foreach (CharacterResourceRule rule in resourceRuleByAttributeIdMap.Values)
                if (abilitySystemComponent.TryGetCurrentValue(rule.ResourceAttribute, out float value))
                    instance.CommitResourceCurrentValue(rule.ResourceAttribute, value);
        }

        #endregion
    }
}
