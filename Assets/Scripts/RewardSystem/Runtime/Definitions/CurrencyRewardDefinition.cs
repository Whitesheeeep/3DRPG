using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.RewardSystemNS
{
    /// <summary>描述一次货币奖励中的单个货币金额。</summary>
    [Serializable]
    public sealed class CurrencyRewardEntry
    {
        #region 配置字段

        [SerializeField, LabelText("货币")]
        private CurrencyId currencyId;
        [SerializeField, MinValue(1), LabelText("金额")]
        private int amount = 1;

        #endregion

        #region 创建与查询

        /// <summary>创建供 Unity 序列化使用的货币奖励项。</summary>
        public CurrencyRewardEntry() { }

        /// <summary>创建指定货币与数量的奖励项。</summary>
        /// <param name="currencyId">货币标识。</param>
        /// <param name="amount">正数数量。</param>
        public CurrencyRewardEntry(CurrencyId currencyId, int amount)
        {
            this.currencyId = currencyId;
            this.amount = amount;
        }

        /// <summary>获取货币标识。</summary>
        public CurrencyId CurrencyId => currencyId;

        /// <summary>获取货币数量。</summary>
        public int Amount => amount;

        #endregion

        #region 配置校验

        /// <summary>校验货币标识和数量。</summary>
        /// <exception cref="ArgumentException">标识无效或数量不是正数时抛出。</exception>
        public void Validate()
        {
            if (currencyId == CurrencyId.None || amount <= 0)
                throw new ArgumentException("货币奖励项必须使用有效货币和正数数量。");
        }

        #endregion
    }

    /// <summary>描述一组将在同一次奖励发放中结算的货币金额。</summary>
    [Serializable]
    public sealed class CurrencyRewardDefinition : RewardDefinition
    {
        #region 配置字段

        [SerializeField] private List<CurrencyRewardEntry> amounts = new List<CurrencyRewardEntry>();

        #endregion

        #region 创建与查询

        /// <summary>创建供 Unity 多态序列化使用的货币奖励定义。</summary>
        public CurrencyRewardDefinition() { }

        /// <summary>创建指定货币金额列表的奖励定义。</summary>
        /// <param name="currencyAmounts">货币奖励项。</param>
        public CurrencyRewardDefinition(IEnumerable<CurrencyRewardEntry> currencyAmounts)
        {
            amounts = currencyAmounts == null
                ? new List<CurrencyRewardEntry>()
                : new List<CurrencyRewardEntry>(currencyAmounts);
        }

        /// <summary>获取货币奖励项只读列表。</summary>
        public IReadOnlyList<CurrencyRewardEntry> Amounts => amounts;

        #endregion

        #region 配置校验

        /// <summary>校验奖励列表不为空且每一项均有效。</summary>
        /// <exception cref="ArgumentException">列表为空或包含无效项时抛出。</exception>
        public override void Validate()
        {
            if (amounts == null) throw new ArgumentException("货币奖励金额列表不能为 null。", nameof(amounts));
            if (amounts.Count == 0) throw new ArgumentException("货币奖励至少需要一个金额项。");
            for (int index = 0; index < amounts.Count; index++)
            {
                if (amounts[index] == null) throw new ArgumentException("货币奖励包含空金额项。");
                amounts[index].Validate();
            }
        }

        #endregion
    }
}
