using System;
using System.Collections.Generic;
using RPG.RewardSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 将 Choice 配置的物品和货币奖励作为同一批次交给 RewardSystem 发放。
    /// </summary>
    [Serializable]
    public sealed class DialogueRewardExecution : DialogueAction
    {
        #region 序列化配置

        [SerializeReference, LabelText("奖励配置")]
        private List<RewardDefinition> rewards = new List<RewardDefinition>();

        #endregion

        #region 构造

        /// <summary>
        /// 创建供 Unity SerializeReference 和 Dialogue 命令 Drawer 使用的奖励命令。
        /// </summary>
        public DialogueRewardExecution()
        {
        }

        #endregion

        #region 配置校验

        /// <summary>
        /// 校验奖励列表至少包含一项有效定义，且每项定义自身配置正确。
        /// </summary>
        /// <exception cref="ArgumentException">奖励列表为空、定义为空或定义配置无效时抛出。</exception>
        public override void Validate()
        {
            if (rewards == null || rewards.Count == 0)
                throw new ArgumentException("奖励执行至少需要配置一个奖励定义。", nameof(rewards));

            for (int index = 0; index < rewards.Count; index++)
            {
                RewardDefinition reward = rewards[index];
                if (reward == null)
                    throw new ArgumentException($"奖励执行的定义列表包含空项，index={index}。", nameof(rewards));

                reward.Validate();
            }
        }

        #endregion

        #region 奖励执行

        /// <summary>
        /// 通过当前 BusinessArchitecture 一次性预检并提交全部物品与货币奖励。
        /// </summary>
        /// <param name="context">当前对话会话及其 Choice 执行上下文。</param>
        /// <exception cref="InvalidOperationException">奖励批次被库存、钱包或状态变更拒绝时抛出。</exception>
        public override void Execute(DialogueCommandContext context)
        {
            RewardSystem rewardSystem = context.Architecture.GetSystem<RewardSystem>();
            RewardGrantResult result = rewardSystem.TryGrant(rewards);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"[DialogueRewardExecution] 奖励批次发放失败，sessionId={context.Session.SessionId}, " +
                    $"choiceNodeId={context.Choice.NodeId}, failureDomain={result.FailureDomain}, " +
                    $"currencyStatus={result.CurrencyStatus?.ToString() ?? "<none>"}, " +
                    $"currencyId={result.CurrencyId}, inventoryStatus={result.InventoryStatus?.ToString() ?? "<none>"}, " +
                    $"itemId={result.ItemId}。");
            }

            // RewardSystem 已完成批次提交和通知；带上对话上下文便于追踪奖励来源。
            Debug.Log(
                $"[DialogueRewardExecution] 奖励批次发放成功，sessionId={context.Session.SessionId}, " +
                $"choiceNodeId={context.Choice.NodeId}, definitionCount={rewards.Count}。");
        }

        #endregion
    }
}
