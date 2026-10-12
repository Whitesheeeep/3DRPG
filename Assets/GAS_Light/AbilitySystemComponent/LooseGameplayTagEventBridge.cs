using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace WS_Modules.GAS.AbilitySystemComponent
{
    /// <summary>
    /// 把全局 LooseGameplayTag 请求桥接到明确实现接口的角色 ASC。
    /// </summary>
    public sealed class LooseGameplayTagEventBridge : IDisposable
    {
        #region 字段

        // 提供接收 Tag 事件的 ASC 和事件目标，桥接器不直接依赖具体角色类型。
        private readonly ILooseGameplayTagEventTarget target;
        // 记录本桥接器已经提交给 ASC 的来源，保证 Add/Remove 对称且可安全注销。
        private readonly Dictionary<LooseGameplayTagSourceKey, GameplayAbilitySystemComponent> ascBySourceKeyMap = new();
        private readonly HashSet<GameplayAbilitySystemComponent> runtimeClearSubscriptionSet = new();
        private IUnRegister unregister;

        #endregion

        #region 生命周期

        /// <summary>创建绑定指定角色桥接接口的事件桥。</summary>
        /// <param name="target">提供 ASC 和事件目标的角色接口。</param>
        public LooseGameplayTagEventBridge(ILooseGameplayTagEventTarget target)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>开始监听全局 LooseGameplayTag 请求。</summary>
        public void Enable()
        {
            if (unregister != null) return;
            unregister = EventSystem.Register_Type<LooseGameplayTagChangeRequestedEventArgs>(
                typeof(LooseGameplayTagChangeRequestedEventArgs),
                OnTagChangeRequested);
        }

        /// <summary>
        /// 停止监听并释放该桥接器已添加的全部 Tag 来源。
        /// </summary>
        public void Disable()
        {
            unregister?.UnRegister();
            unregister = null;
            foreach (KeyValuePair<LooseGameplayTagSourceKey, GameplayAbilitySystemComponent> source in ascBySourceKeyMap)
                source.Value.RemoveLooseGameplayTag(source.Key.Tag);

            ascBySourceKeyMap.Clear();
            foreach (GameplayAbilitySystemComponent asc in runtimeClearSubscriptionSet)
                asc.RuntimeCleared -= HandleRuntimeCleared;
            runtimeClearSubscriptionSet.Clear();
        }

        /// <inheritdoc />
        public void Dispose() => Disable();

        /// <summary>角色切换后把已登记的 LooseTag 来源迁移到新的 Active ASC。</summary>
        public void RebindActiveAbilitySystem()
        {
            GameplayAbilitySystemComponent current = target.AbilitySystemComponent;
            if (current == null) return;

            // 先从旧 ASC 对称移除，再写入新 ASC；失败来源被丢弃，避免桥接器伪造激活 Tag。
            var sources = new List<LooseGameplayTagSourceKey>(ascBySourceKeyMap.Keys);
            foreach (LooseGameplayTagSourceKey source in sources)
            {
                GameplayAbilitySystemComponent previous = ascBySourceKeyMap[source];
                if (ReferenceEquals(previous, current)) continue;
                previous.RemoveLooseGameplayTag(source.Tag);
                if (current.AddLooseGameplayTag(source.Tag))
                {
                    ascBySourceKeyMap[source] = current;
                    SubscribeRuntimeClear(current);
                    UnsubscribeRuntimeClearIfUnused(previous);
                }
                else
                {
                    ascBySourceKeyMap.Remove(source);
                    UnsubscribeRuntimeClearIfUnused(previous);
                }
            }
        }

        #endregion

        #region 事件处理

        /// <summary>处理匹配当前角色的 LooseGameplayTag 请求。</summary>
        /// <param name="eventArgs">外部 Tag 请求。</param>
        private void OnTagChangeRequested(LooseGameplayTagChangeRequestedEventArgs eventArgs)
        {
            if (!IsTarget(eventArgs.Target) || target.AbilitySystemComponent == null ||
                string.IsNullOrWhiteSpace(eventArgs.SourceId) || !eventArgs.Tag.IsValid)
                return;

            LooseGameplayTagSourceKey source = new LooseGameplayTagSourceKey(eventArgs.SourceId, eventArgs.Tag);
            if (eventArgs.Operation == LooseGameplayTagChangeOperation.Add)
            {
                if (ascBySourceKeyMap.ContainsKey(source)) return;
                GameplayAbilitySystemComponent abilitySystem = target.AbilitySystemComponent;
                if (!abilitySystem.AddLooseGameplayTag(eventArgs.Tag)) return;
                ascBySourceKeyMap.Add(source, abilitySystem);
                SubscribeRuntimeClear(abilitySystem);
                return;
            }

            if (!ascBySourceKeyMap.TryGetValue(source, out GameplayAbilitySystemComponent sourceAbilitySystem)) return;
            ascBySourceKeyMap.Remove(source);
            if (!sourceAbilitySystem.RemoveLooseGameplayTag(eventArgs.Tag))
                ascBySourceKeyMap.Add(source, sourceAbilitySystem);
            else
                UnsubscribeRuntimeClearIfUnused(sourceAbilitySystem);
        }

        /// <summary>在第一次把外部 Tag 写入 ASC 后订阅其运行时清理事件。</summary>
        /// <param name="abilitySystem">Tag 已应用到的 ASC。</param>
        private void SubscribeRuntimeClear(GameplayAbilitySystemComponent abilitySystem)
        {
            if (runtimeClearSubscriptionSet.Add(abilitySystem))
                abilitySystem.RuntimeCleared += HandleRuntimeCleared;
        }

        /// <summary>当 ASC 不再包含本桥接器来源时解除清理订阅。</summary>
        /// <param name="abilitySystem">待检查 ASC。</param>
        private void UnsubscribeRuntimeClearIfUnused(GameplayAbilitySystemComponent abilitySystem)
        {
            foreach (GameplayAbilitySystemComponent registeredAsc in ascBySourceKeyMap.Values)
                if (ReferenceEquals(registeredAsc, abilitySystem)) return;
            if (runtimeClearSubscriptionSet.Remove(abilitySystem))
                abilitySystem.RuntimeCleared -= HandleRuntimeCleared;
        }

        /// <summary>ASC 清理时同步丢弃其外部 Tag 来源记录，不再对已清零容器执行 Remove。</summary>
        /// <param name="abilitySystem">刚清除运行时的 ASC。</param>
        private void HandleRuntimeCleared(GameplayAbilitySystemComponent abilitySystem)
        {
            var sourceKeys = new List<LooseGameplayTagSourceKey>();
            foreach (KeyValuePair<LooseGameplayTagSourceKey, GameplayAbilitySystemComponent> source in ascBySourceKeyMap)
                if (ReferenceEquals(source.Value, abilitySystem)) sourceKeys.Add(source.Key);
            for (int index = 0; index < sourceKeys.Count; index++)
                ascBySourceKeyMap.Remove(sourceKeys[index]);
            if (runtimeClearSubscriptionSet.Remove(abilitySystem))
                abilitySystem.RuntimeCleared -= HandleRuntimeCleared;
            Debug.Log($"[LooseGameplayTagEventBridge] ASC '{abilitySystem.name}' 运行时已清理，外部 Tag 来源记录同步移除，count={sourceKeys.Count}。");
        }

        /// <summary>判断事件目标是否属于当前角色根对象层级。</summary>
        /// <param name="eventTarget">事件目标对象。</param>
        /// <returns>目标匹配当前角色时返回 true。</returns>
        private bool IsTarget(GameObject eventTarget)
        {
            if (eventTarget == null || target.TagEventTarget == null) return false;
            Transform expected = target.TagEventTarget.transform;
            Transform actual = eventTarget.transform;
            return eventTarget == target.TagEventTarget || actual.IsChildOf(expected) || expected.IsChildOf(actual);
        }

        #endregion

        #region 内部键

        /// <summary>标识一个事件来源对一个 Tag 的唯一引用。</summary>
        private readonly struct LooseGameplayTagSourceKey : IEquatable<LooseGameplayTagSourceKey>
        {
            private readonly string sourceId;

            /// <summary>创建来源键。</summary>
            /// <param name="sourceId">来源标识。</param>
            /// <param name="tag">来源 Tag。</param>
            public LooseGameplayTagSourceKey(string sourceId, WS_Modules.GAS.TAG.GameplayTag tag)
            {
                this.sourceId = sourceId;
                Tag = tag;
            }

            /// <summary>获取来源 Tag。</summary>
            public WS_Modules.GAS.TAG.GameplayTag Tag { get; }

            /// <inheritdoc />
            public bool Equals(LooseGameplayTagSourceKey other) =>
                string.Equals(sourceId, other.sourceId, StringComparison.Ordinal) && Tag == other.Tag;

            /// <inheritdoc />
            public override bool Equals(object obj) => obj is LooseGameplayTagSourceKey other && Equals(other);

            /// <inheritdoc />
            public override int GetHashCode() => HashCode.Combine(sourceId, Tag);
        }

        #endregion
    }
}
