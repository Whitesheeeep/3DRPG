#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Game;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.RewardSystemNS.Tests
{
    /// <summary>通过 Odin 按钮校验和发放可堆叠物品、武器、圣遗物与货币组合奖励。</summary>
    public sealed class RewardSystemOdinTester : MonoBehaviour
    {
        #region 测试配置与结果

        [SerializeReference, ListDrawerSettings(Expanded = true)]
        private List<RewardDefinition> rewardDefinitions = new List<RewardDefinition>();

        [ShowInInspector, ReadOnly]
        private string lastStatus = "Idle";

        #endregion

        #region 手动验证

        /// <summary>通过架构中注册的 RewardSystem 无副作用预检配置奖励。</summary>
        [Button("预检配置奖励", ButtonSizes.Large)]
        public void ValidateConfiguredRewards()
        {
            RewardGrantResult result = GetRewardSystem().CanGrant(rewardDefinitions);
            lastStatus = result.Succeeded
                ? "预检通过：奖励配置可以完整发放。"
                : $"预检拒绝：domain={result.FailureDomain}, currency={result.CurrencyStatus}, item={result.ItemId}, inventory={result.InventoryStatus}";
            if (result.Succeeded) Debug.Log($"[RewardSystemOdinTester] {lastStatus}", this);
            else Debug.LogWarning($"[RewardSystemOdinTester] {lastStatus}", this);
        }

        /// <summary>真实发放 Inspector 中配置的奖励，用于验证各领域到账和通知。</summary>
        [Button("发放配置奖励", ButtonSizes.Large)]
        public void GrantConfiguredRewards()
        {
            RewardGrantResult result = GetRewardSystem().TryGrant(rewardDefinitions);
            lastStatus = result.Succeeded
                ? "发放成功：请检查钱包、各类库存和 New 红点。"
                : $"发放拒绝：domain={result.FailureDomain}, currency={result.CurrencyStatus}, item={result.ItemId}, inventory={result.InventoryStatus}";
            if (result.Succeeded) Debug.Log($"[RewardSystemOdinTester] {lastStatus}", this);
            else Debug.LogWarning($"[RewardSystemOdinTester] {lastStatus}", this);
        }

        /// <summary>取得当前运行中的通用奖励 System。</summary>
        /// <returns>架构注册的奖励系统。</returns>
        /// <exception cref="InvalidOperationException">不在 Play Mode 时抛出。</exception>
        private static RewardSystem GetRewardSystem()
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("[RewardSystemOdinTester] 请在 Play Mode 且 GameArchitecture 已启动后运行测试。");
            return GameArchitecture.Interface.GetSystem<RewardSystem>();
        }

        #endregion
    }
}
#endif
