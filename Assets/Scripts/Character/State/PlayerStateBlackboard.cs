using System;
using System.Collections.Generic;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;
using RPG.PlayerInputSystem;

namespace RPG.Character.State
{
    /// <summary>聚合 ASC、环境与当前帧 Intent 的只读玩家状态，并负责 Intent 消费确认。</summary>
    public sealed class PlayerStateBlackboard
    {
        #region 字段与事件
        private readonly GameplayTagCountContainer environmentTags = new();
        private readonly GameplayTagContainer intentTags = new();
        private readonly Dictionary<GameplayTag, HashSet<InputRequestHandle>> intentSources = new();
        /// <summary>通知 PlayerController 将某个 Intent 来源句柄提交给输入 Controller。</summary>
        internal event Action<GameplayTag, InputRequestHandle> IntentSourceConsumed;
        #endregion

        #region 属性
        /// <summary>获取同对象上的 ASC；所有 GAS 写操作仍应使用 ASC 公开 API。</summary>
        public GameplayAbilitySystemComponent AbilitySystem { get; }
        /// <summary>直接代理 ASC 当前 Tag，不复制也不修改其内容。</summary>
        public IReadOnlyGameplayTagContainer AbilityTags => AbilitySystem.Tags;
        /// <summary>获取由环境监测模块维护的只读 Tag。</summary>
        public IReadOnlyGameplayTagContainer EnvironmentTags => environmentTags;
        /// <summary>获取仅在当前帧有效的仲裁 Intent Tag。</summary>
        public IReadOnlyGameplayTagContainer IntentTags => intentTags;
        #endregion

        #region 构造
        /// <summary>为 PlayerController 创建绑定指定 ASC 的玩家状态黑板。</summary>
        /// <param name="abilitySystem">由所属 PlayerController 长期持有的 ASC。</param>
        internal PlayerStateBlackboard(GameplayAbilitySystemComponent abilitySystem) => AbilitySystem = abilitySystem;
        #endregion

        #region 公开操作
        /// <summary>判断当前帧是否存在可匹配的 Intent。</summary>
        public bool HasIntent(GameplayTag intentTag) => intentTags.HasTag(intentTag);

        /// <summary>在业务成功后移除 Intent，并通知其全部合并来源完成消费。</summary>
        public bool TryConfirmIntentConsumed(GameplayTag intentTag)
        {
            if (!intentSources.TryGetValue(intentTag, out HashSet<InputRequestHandle> handles)) return false;
            intentSources.Remove(intentTag);
            intentTags.RemoveTag(intentTag);

            // 先移除帧级入口再逐一确认，避免消费回调重入时重复提交同一个 Intent。
            foreach (InputRequestHandle handle in handles) IntentSourceConsumed?.Invoke(intentTag, handle);
            return true;
        }

        /// <summary>增加一个环境 Tag 来源计数。</summary>
        public bool AddEnvironmentTag(GameplayTag tag) => environmentTags.UpdateTagCount(tag, 1);

        /// <summary>移除一个环境 Tag 来源计数。</summary>
        public bool RemoveEnvironmentTag(GameplayTag tag) => environmentTags.UpdateTagCount(tag, -1);
        #endregion

        #region 仲裁协调
        /// <summary>发布当前帧 Intent；同名 Tag 会合并并累积全部来源句柄。</summary>
        internal bool PublishFrameIntent(GameplayTag intentTag, InputRequestHandle sourceHandle)
        {
            if (!intentSources.TryGetValue(intentTag, out HashSet<InputRequestHandle> handles))
            {
                handles = new HashSet<InputRequestHandle>();
                intentSources.Add(intentTag, handles);
                if (!intentTags.AddTag(intentTag))
                {
                    intentSources.Remove(intentTag);
                    return false;
                }
            }
            return handles.Add(sourceHandle);
        }

        /// <summary>清理所有帧级 Intent，不发送任何 Request 消费确认。</summary>
        public void ClearFrameIntents()
        {
            intentSources.Clear();
            intentTags.Reset();
        }

        /// <summary>清空帧级 Intent、来源映射和全部环境状态，用于所属 Controller 停用时结束生命周期。</summary>
        internal void Reset()
        {
            ClearFrameIntents();
            environmentTags.Reset();
        }
        #endregion
    }
}
