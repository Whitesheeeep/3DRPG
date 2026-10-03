using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.BusinessArchitecture;

namespace RPG.RewardSystemNS
{
    /// <summary>统一预检、提交并通知所有业务奖励。</summary>
    public sealed class RewardSystem : AbstractSystem
    {
        #region 依赖字段

        // 依赖字段：注册表将多态配置路由到唯一的领域 Handler。
        private readonly RewardHandlerRegistry handlerRegistry;

        #endregion

        #region 构造与生命周期

        /// <summary>创建通用奖励 System。</summary>
        /// <param name="handlerRegistry">已登记默认奖励 Handler 的注册表。</param>
        /// <exception cref="ArgumentNullException">注册表为空时抛出。</exception>
        public RewardSystem(RewardHandlerRegistry handlerRegistry)
        {
            this.handlerRegistry = handlerRegistry ?? throw new ArgumentNullException(nameof(handlerRegistry));
        }

        /// <summary>确认奖励 Handler 注册表已装配。</summary>
        protected override void OnInit()
        {
            Debug.Log($"[RewardSystem] 已启动通用奖励流程，handlerCount={handlerRegistry.Count}。");
        }

        /// <summary>记录奖励 System 停止。</summary>
        protected override void OnDeinit()
        {
            Debug.Log("[RewardSystem] 已停止通用奖励流程。");
        }

        #endregion

        #region 查询与发放

        /// <summary>无副作用地预检一组奖励。</summary>
        /// <param name="rewards">多态奖励定义列表。</param>
        /// <returns>预检结果。</returns>
        /// <exception cref="ArgumentNullException">奖励列表为空引用时抛出。</exception>
        /// <exception cref="ArgumentException">奖励定义缺失或非法时抛出。</exception>
        /// <exception cref="InvalidOperationException">奖励类型没有已登记 Handler 时抛出。</exception>
        public RewardGrantResult CanGrant(IReadOnlyList<RewardDefinition> rewards)
        {
            TryPrepareGrant(rewards, out _, out RewardGrantResult result);
            return result;
        }

        /// <summary>校验并发放奖励；失败时不写入任一领域状态。</summary>
        /// <param name="rewards">多态奖励定义列表。</param>
        /// <returns>发放或业务拒绝结果。</returns>
        /// <exception cref="ArgumentNullException">奖励列表为空引用时抛出。</exception>
        /// <exception cref="ArgumentException">奖励定义缺失或非法时抛出。</exception>
        /// <exception cref="InvalidOperationException">奖励类型没有已登记 Handler 时抛出。</exception>
        public RewardGrantResult TryGrant(IReadOnlyList<RewardDefinition> rewards)
        {
            TryPrepareGrant(rewards, out PreparedRewardGrant preparedGrant, out RewardGrantResult result);
            if (!result.Succeeded)
            {
                Debug.LogWarning(
                    $"[RewardSystem] 奖励批次预检被拒绝，domain={result.FailureDomain}, currency={result.CurrencyStatus}, item={result.ItemId}, inventory={result.InventoryStatus}。");
                return result;
            }
            if (!preparedGrant.CanCommit)
            {
                Debug.LogWarning("[RewardSystem] 奖励准备快照已失效，未提交任何领域状态；调用方可以重新尝试。");
                return new RewardGrantResult(RewardGrantFailureDomain.StateChanged);
            }

            preparedGrant.CommitState();
            preparedGrant.PublishNotifications();
            Debug.Log($"[RewardSystem] 奖励批次发放成功，partCount={preparedGrant.PartCount}。");
            return RewardGrantResult.Success();
        }

        /// <summary>为任务等需要追加业务状态的调用方准备批次，通知由调用方在提交后触发。</summary>
        /// <param name="rewards">多态奖励定义列表。</param>
        /// <param name="preparedGrant">准备成功时返回的批次。</param>
        /// <param name="result">准备结果或业务拒绝原因。</param>
        /// <returns>准备成功时返回 true。</returns>
        /// <exception cref="ArgumentNullException">奖励列表为空引用时抛出。</exception>
        /// <exception cref="ArgumentException">奖励定义缺失或非法时抛出。</exception>
        /// <exception cref="InvalidOperationException">奖励类型没有已登记 Handler 时抛出。</exception>
        internal bool TryPrepareGrant(
            IReadOnlyList<RewardDefinition> rewards,
            out PreparedRewardGrant preparedGrant,
            out RewardGrantResult result)
        {
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            var definitionByTypeMap = new Dictionary<Type, List<RewardDefinition>>();
            var definitionTypeOrder = new List<Type>();
            for (int index = 0; index < rewards.Count; index++)
            {
                RewardDefinition definition = rewards[index];
                if (definition == null) throw new ArgumentException($"奖励列表包含空定义，index={index}。", nameof(rewards));
                try
                {
                    definition.Validate();
                }
                catch (ArgumentException exception)
                {
                    Debug.LogError(
                        $"[RewardSystem] 奖励配置校验失败，index={index}, definitionType={definition.GetType().FullName}, error={exception.Message}。");
                    throw;
                }
                Type definitionType = definition.GetType();
                if (!definitionByTypeMap.TryGetValue(definitionType, out List<RewardDefinition> typedDefinitions))
                {
                    typedDefinitions = new List<RewardDefinition>();
                    definitionByTypeMap.Add(definitionType, typedDefinitions);
                    definitionTypeOrder.Add(definitionType);
                }
                typedDefinitions.Add(definition);
            }

            var preparedParts = new List<IPreparedRewardPart>();
            for (int index = 0; index < definitionTypeOrder.Count; index++)
            {
                Type definitionType = definitionTypeOrder[index];
                IRewardHandler handler = handlerRegistry.Resolve(definitionType);
                if (!handler.TryPrepare(
                        definitionByTypeMap[definitionType],
                        out IReadOnlyList<IPreparedRewardPart> handlerParts,
                        out result))
                {
                    preparedGrant = null;
                    return false;
                }
                for (int partIndex = 0; partIndex < handlerParts.Count; partIndex++)
                    preparedParts.Add(handlerParts[partIndex]);
            }

            preparedGrant = new PreparedRewardGrant(preparedParts);
            result = RewardGrantResult.Success();
            return true;
        }

        #endregion
    }
}
