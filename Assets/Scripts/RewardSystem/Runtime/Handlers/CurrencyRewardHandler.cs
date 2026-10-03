using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using UnityEngine;

namespace RPG.RewardSystemNS
{
    /// <summary>把货币奖励定义准备为钱包批量增量。</summary>
    public sealed class CurrencyRewardHandler : IRewardHandler
    {
        #region 依赖字段

        private readonly CurrencyManager currencyManager;

        #endregion

        #region 构造与类型

        /// <summary>创建使用项目货币钱包的奖励 Handler。</summary>
        /// <param name="currencyManager">持有货币余额的 Manager。</param>
        /// <exception cref="ArgumentNullException">钱包为空时抛出。</exception>
        public CurrencyRewardHandler(CurrencyManager currencyManager)
        {
            this.currencyManager = currencyManager ?? throw new ArgumentNullException(nameof(currencyManager));
        }

        /// <summary>获取精确处理的奖励定义类型。</summary>
        public Type DefinitionType => typeof(CurrencyRewardDefinition);

        #endregion

        #region 批次准备

        /// <summary>合并货币奖励并准备一次钱包提交。</summary>
        /// <param name="definitions">货币奖励定义。</param>
        /// <param name="preparedParts">成功时返回钱包批次。</param>
        /// <param name="result">成功或钱包拒绝结果。</param>
        /// <returns>准备成功时返回 true。</returns>
        public bool TryPrepare(
            IReadOnlyList<RewardDefinition> definitions,
            out IReadOnlyList<IPreparedRewardPart> preparedParts,
            out RewardGrantResult result)
        {
            // 合并重复货币奖励，按定义顺序保留货币顺序。
            var amountByCurrencyIdMap = new Dictionary<CurrencyId, int>();
            var currencyOrder = new List<CurrencyId>();
            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                var definition = (CurrencyRewardDefinition)definitions[definitionIndex];
                for (int amountIndex = 0; amountIndex < definition.Amounts.Count; amountIndex++)
                {
                    CurrencyRewardEntry entry = definition.Amounts[amountIndex];
                    if (!amountByCurrencyIdMap.TryGetValue(entry.CurrencyId, out int current))
                    {
                        amountByCurrencyIdMap.Add(entry.CurrencyId, entry.Amount);
                        currencyOrder.Add(entry.CurrencyId);
                        continue;
                    }

                    try { amountByCurrencyIdMap[entry.CurrencyId] = checked(current + entry.Amount); }
                    catch (OverflowException exception)
                    {
                        Debug.LogError($"[CurrencyRewardHandler] 重复货币奖励合并溢出，currencyId={entry.CurrencyId}。");
                        throw new ArgumentException($"奖励货币总额超出整数范围：{entry.CurrencyId}。", nameof(definitions), exception);
                    }
                }
            }

            var deltas = new List<CurrencyDelta>(currencyOrder.Count);
            for (int index = 0; index < currencyOrder.Count; index++)
            {
                CurrencyId currencyId = currencyOrder[index];
                deltas.Add(new CurrencyDelta(currencyId, amountByCurrencyIdMap[currencyId]));
            }

            // 尝试准备钱包批量增量，失败时返回钱包拒绝结果。
            if (!currencyManager.TryPrepareRewardChanges(
                    deltas,
                    out CurrencyOperationResult walletResult,
                    out Func<bool> canCommit,
                    out Action commitState,
                    out Action publishNotifications))
            {
                preparedParts = Array.Empty<IPreparedRewardPart>();
                result = new RewardGrantResult(
                    RewardGrantFailureDomain.Currency,
                    walletResult.Status,
                    currencyId: walletResult.CurrencyId);
                return false;
            }

            preparedParts = new IPreparedRewardPart[]
            {
                new PreparedRewardGrantPart(canCommit, commitState, publishNotifications)
            };
            result = RewardGrantResult.Success();
            return true;
        }

        #endregion
    }
}
