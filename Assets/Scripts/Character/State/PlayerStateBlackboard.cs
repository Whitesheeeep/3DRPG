using System;
using System.Collections.Generic;
using WS_Modules.GAS.TAG;
using RPG.PlayerInputSystem;
using UnityEngine;

namespace RPG.Character.State
{
    /// <summary>保存玩家输入仲裁产生的连续移动与当前帧 Intent，并负责消费确认。</summary>
    public sealed class PlayerStateBlackboard
    {
        #region 字段与事件
        private readonly GameplayTagContainer intentTags = new();
        private readonly Dictionary<GameplayTag, HashSet<InputRequestHandle>> intentSources = new();
        private readonly Dictionary<InputRequestHandle, GameplayTag> intentTagsBySource = new();
        /// <summary>通知 PlayerController 将某个 Intent 来源句柄提交给输入 Controller。</summary>
        internal event Action<GameplayTag, InputRequestHandle> IntentSourceConsumed;
        #endregion

        #region 属性
        /// <summary>获取仅在当前帧有效的仲裁 Intent Tag。</summary>
        public IReadOnlyGameplayTagContainer IntentTags => intentTags;
        /// <summary>获取当前帧镜头转换后的世界水平移动意图。</summary>
        public Vector3 MoveWorldInput { get; internal set; }
        #endregion

        #region 构造
        /// <summary>为稳定 Player 创建只保存输入仲裁结果的状态黑板。</summary>
        internal PlayerStateBlackboard()
        {
        }
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
            foreach (InputRequestHandle handle in handles)
            {
                intentTagsBySource.Remove(handle);
                IntentSourceConsumed?.Invoke(intentTag, handle);
            }
            return true;
        }

        #endregion

        #region 仲裁协调
        /// <summary>尝试发布当前帧 Intent；同名 Tag 会合并并累积全部来源句柄。</summary>
        /// <param name="intentTag">需要发布的 Intent Tag。</param>
        /// <param name="sourceHandle">产生该意图的输入阶段句柄。</param>
        /// <returns>来源句柄成功登记到该 Intent 时返回 true。</returns>
        internal bool TryPublishFrameIntent(GameplayTag intentTag, InputRequestHandle sourceHandle)
        {
            // 一个输入阶段句柄只能归属一个意图，避免多个 Arbiter 对同一 Request 重复消费。
            if (intentTagsBySource.ContainsKey(sourceHandle)) return false;

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
            if (!handles.Add(sourceHandle)) return false;
            intentTagsBySource.Add(sourceHandle, intentTag);
            return true;
        }

        /// <summary>清理所有帧级 Intent，不发送任何 Request 消费确认。</summary>
        public void ClearFrameIntents()
        {
            intentSources.Clear();
            intentTagsBySource.Clear();
            intentTags.Reset();
        }

        /// <summary>仅清理连续移动意图，用于 PlayerController 整体停用的生命周期边界。</summary>
        internal void ClearMoveInput()
        {
            MoveWorldInput = Vector3.zero;
        }

        #endregion
    }
}
