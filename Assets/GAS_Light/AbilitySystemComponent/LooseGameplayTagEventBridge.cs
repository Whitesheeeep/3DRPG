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
        private readonly Dictionary<LooseGameplayTagSourceKey, GameplayAbilitySystemComponent> activeSources = new();
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
            foreach (KeyValuePair<LooseGameplayTagSourceKey, GameplayAbilitySystemComponent> source in activeSources)
                source.Value.RemoveLooseGameplayTag(source.Key.Tag);

            activeSources.Clear();
        }

        /// <inheritdoc />
        public void Dispose() => Disable();

        /// <summary>角色切换后把已登记的 LooseTag 来源迁移到新的 Active ASC。</summary>
        public void RebindActiveAbilitySystem()
        {
            GameplayAbilitySystemComponent current = target.AbilitySystemComponent;
            if (current == null) return;

            // 先从旧 ASC 对称移除，再写入新 ASC；失败来源被丢弃，避免桥接器伪造激活 Tag。
            var sources = new List<LooseGameplayTagSourceKey>(activeSources.Keys);
            foreach (LooseGameplayTagSourceKey source in sources)
            {
                GameplayAbilitySystemComponent previous = activeSources[source];
                if (ReferenceEquals(previous, current)) continue;
                previous.RemoveLooseGameplayTag(source.Tag);
                if (current.AddLooseGameplayTag(source.Tag)) activeSources[source] = current;
                else activeSources.Remove(source);
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
                if (activeSources.ContainsKey(source)) return;
                GameplayAbilitySystemComponent abilitySystem = target.AbilitySystemComponent;
                activeSources.Add(source, abilitySystem);
                if (!abilitySystem.AddLooseGameplayTag(eventArgs.Tag)) activeSources.Remove(source);
                return;
            }

            if (!activeSources.TryGetValue(source, out GameplayAbilitySystemComponent sourceAbilitySystem)) return;
            activeSources.Remove(source);
            if (!sourceAbilitySystem.RemoveLooseGameplayTag(eventArgs.Tag)) activeSources.Add(source, sourceAbilitySystem);
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
