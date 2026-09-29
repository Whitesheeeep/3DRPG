using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystem
{
    #region 货币奖励配置

    /// <summary>
    /// 描述一种货币及其奖励数量。
    /// </summary>
    [Serializable]
    public sealed class TaskCurrencyRewardEntry
    {
        [SerializeField] private CurrencyId currencyId;
        [SerializeField, MinValue(1)] private int amount = 1;

        /// <summary>
        /// 创建供 Unity 序列化使用的空货币奖励项。
        /// </summary>
        public TaskCurrencyRewardEntry()
        {
        }

        /// <summary>
        /// 创建指定货币和数量的奖励项。
        /// </summary>
        /// <param name="currencyId">奖励货币。</param>
        /// <param name="amount">正数奖励数量。</param>
        public TaskCurrencyRewardEntry(CurrencyId currencyId, int amount)
        {
            this.currencyId = currencyId;
            this.amount = amount;
        }

        /// <summary>
        /// 获取货币标识。
        /// </summary>
        public CurrencyId CurrencyId => currencyId;

        /// <summary>
        /// 获取奖励数量。
        /// </summary>
        public int Amount => amount;

        /// <summary>
        /// 校验货币和数量。
        /// </summary>
        /// <exception cref="ArgumentException">货币 ID 或数量非法时抛出。</exception>
        public void Validate()
        {
            if (currencyId == CurrencyId.None || amount <= 0)
            {
                throw new ArgumentException("任务货币奖励必须包含有效货币和正数数量。");
            }
        }
    }

    /// <summary>
    /// 配置一项或多项将在任务提交时原子发放的货币。
    /// </summary>
    [Serializable]
    public sealed class TaskCurrencyRewardDefinition : TaskRewardDefinition
    {
        [SerializeField] private List<TaskCurrencyRewardEntry> amounts =
            new List<TaskCurrencyRewardEntry>();

        /// <summary>
        /// 创建供 Unity 多态序列化使用的空奖励。
        /// </summary>
        public TaskCurrencyRewardDefinition()
        {
        }

        /// <summary>
        /// 创建指定货币奖励列表。
        /// </summary>
        /// <param name="currencyAmounts">任务完成后发放的货币金额。</param>
        public TaskCurrencyRewardDefinition(IEnumerable<TaskCurrencyRewardEntry> currencyAmounts)
        {
            amounts = currencyAmounts == null
                ? new List<TaskCurrencyRewardEntry>()
                : new List<TaskCurrencyRewardEntry>(currencyAmounts);
        }

        /// <summary>
        /// 获取奖励金额只读列表。
        /// </summary>
        public IReadOnlyList<TaskCurrencyRewardEntry> Amounts => amounts;

        /// <summary>
        /// 校验奖励列表中的每个金额。
        /// </summary>
        /// <exception cref="ArgumentException">奖励列表为空或包含非法金额时抛出。</exception>
        public void Validate()
        {
            amounts ??= new List<TaskCurrencyRewardEntry>();
            if (amounts.Count == 0)
            {
                throw new ArgumentException("任务货币奖励至少需要一个奖励项。");
            }

            for (int index = 0; index < amounts.Count; index++)
            {
                if (amounts[index] == null)
                {
                    throw new ArgumentException("任务货币奖励包含空金额项。");
                }

                amounts[index].Validate();
            }
        }
    }

    #endregion

    #region 货币奖励执行

    /// <summary>
    /// 汇总任务的货币奖励并通过钱包原子预检、发放。
    /// </summary>
    public sealed class TaskCurrencyRewardHandler
    {
        #region 依赖字段

        // 依赖字段：钱包负责校验上限和一次性提交多种货币变化。
        private readonly ICurrencyWallet currencyWallet;

        #endregion

        /// <summary>
        /// 创建货币奖励 Handler。
        /// </summary>
        /// <param name="currencyWallet">货币钱包。</param>
        /// <exception cref="ArgumentNullException">钱包为空时抛出。</exception>
        public TaskCurrencyRewardHandler(ICurrencyWallet currencyWallet)
        {
            this.currencyWallet = currencyWallet ?? throw new ArgumentNullException(nameof(currencyWallet));
        }

        /// <summary>
        /// 对任务全部货币奖励执行无副作用预检。
        /// </summary>
        /// <param name="rewards">任务奖励配置。</param>
        /// <returns>钱包校验状态。</returns>
        public CurrencyOperationResult CanGrant(IReadOnlyList<TaskRewardDefinition> rewards)
        {
            return currencyWallet.CanAddCurrencies(BuildAmounts(rewards));
        }

        /// <summary>
        /// 将已经预检的任务货币奖励一次性提交给钱包。
        /// </summary>
        /// <param name="rewards">任务奖励配置。</param>
        /// <returns>钱包的原子批量发放状态。</returns>
        /// <remarks>调用方先通过 <see cref="CanGrant(IReadOnlyList{TaskRewardDefinition})"/>；钱包仍会在批量提交时复核上限。</remarks>
        /// <exception cref="ArgumentException">奖励列表包含未支持的奖励类型或溢出总额时抛出。</exception>
        public CurrencyOperationResult Grant(IReadOnlyList<TaskRewardDefinition> rewards)
        {
            return currencyWallet.AddCurrencies(BuildAmounts(rewards));
        }

        /// <summary>
        /// 合并重复货币项，避免分步发放造成同一任务部分到账。
        /// </summary>
        /// <param name="rewards">任务奖励配置。</param>
        /// <returns>每种货币各一项的确定顺序金额列表。</returns>
        /// <exception cref="ArgumentException">出现未支持的奖励类型时抛出。</exception>
        private static IReadOnlyList<CurrencyAmount> BuildAmounts(IReadOnlyList<TaskRewardDefinition> rewards)
        {
            // key：CurrencyId；value：此任务所有奖励配置合并后的货币总额。
            var amountByCurrencyIdMap = new Dictionary<CurrencyId, int>();
            var currencyOrder = new List<CurrencyId>();
            for (int rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
            {
                if (!(rewards[rewardIndex] is TaskCurrencyRewardDefinition currencyReward))
                {
                    throw new ArgumentException("任务包含未支持的奖励类型。", nameof(rewards));
                }

                for (int amountIndex = 0; amountIndex < currencyReward.Amounts.Count; amountIndex++)
                {
                    TaskCurrencyRewardEntry entry = currencyReward.Amounts[amountIndex];
                    if (!amountByCurrencyIdMap.ContainsKey(entry.CurrencyId))
                    {
                        currencyOrder.Add(entry.CurrencyId);
                        amountByCurrencyIdMap.Add(entry.CurrencyId, entry.Amount);
                        continue;
                    }

                    try
                    {
                        amountByCurrencyIdMap[entry.CurrencyId] = checked(
                            amountByCurrencyIdMap[entry.CurrencyId] + entry.Amount);
                    }
                    catch (OverflowException exception)
                    {
                        throw new ArgumentException(
                            $"任务货币奖励总额超出整数范围：{entry.CurrencyId}。",
                            nameof(rewards),
                            exception);
                    }
                }
            }

            var amounts = new List<CurrencyAmount>(currencyOrder.Count);
            for (int index = 0; index < currencyOrder.Count; index++)
            {
                CurrencyId currencyId = currencyOrder[index];
                amounts.Add(new CurrencyAmount(currencyId, amountByCurrencyIdMap[currencyId]));
            }

            return amounts;
        }
    }

    #endregion
}
