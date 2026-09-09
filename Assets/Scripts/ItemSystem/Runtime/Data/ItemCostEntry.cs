using System;
using System.Collections.Generic;
using RPG.CurrencySystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>成长阶段消耗的一种物品及数量。</summary>
    [Serializable]
    public sealed class ItemCostEntry
    {
        [SerializeField, LabelText("物品标识"), ItemIdDropdown] private ItemId itemId;
        [SerializeField, MinValue(1), LabelText("数量")] private int quantity = 1;

        /// <summary>获取消耗物品标识。</summary>
        public ItemId ItemId => itemId;

        /// <summary>获取消耗数量。</summary>
        public int Quantity => quantity;

        /// <summary>验证物品消耗的标识和数量。</summary>
        /// <exception cref="InvalidOperationException">消耗数据不合法时抛出。</exception>
        public void Validate()
        {
            if (!itemId.IsValid) throw new InvalidOperationException("物品消耗缺少有效 ItemId。");
            if (quantity < 1) throw new InvalidOperationException($"物品 '{itemId}' 的消耗数量必须大于零。");
        }
    }

    /// <summary>成长阶段消耗的一种货币及数量。</summary>
    [Serializable]
    public sealed class CurrencyCostEntry
    {
        [SerializeField, LabelText("货币标识")] private CurrencyId currencyId;
        [SerializeField, MinValue(1), LabelText("金额")] private int amount = 1;

        /// <summary>获取货币标识。</summary>
        public CurrencyId CurrencyId => currencyId;

        /// <summary>获取消耗数量。</summary>
        public int Amount => amount;

        /// <summary>验证货币消耗的标识和金额。</summary>
        /// <exception cref="InvalidOperationException">消耗数据不合法时抛出。</exception>
        public void Validate()
        {
            if (currencyId == CurrencyId.None) throw new InvalidOperationException("货币消耗缺少有效 CurrencyId。");
            if (amount < 1) throw new InvalidOperationException($"货币 '{currencyId}' 的消耗金额必须大于零。");
        }
    }

    /// <summary>一个等级、突破或精炼阶段的通用消耗集合。</summary>
    [Serializable]
    public sealed class GrowthCost
    {
        [SerializeField, LabelText("物品消耗")] private ItemCostEntry[] itemCosts = Array.Empty<ItemCostEntry>();
        [SerializeField, LabelText("货币消耗")] private CurrencyCostEntry[] currencyCosts = Array.Empty<CurrencyCostEntry>();

        /// <summary>获取物品消耗列表。</summary>
        public IReadOnlyList<ItemCostEntry> ItemCosts => itemCosts;

        /// <summary>获取货币消耗列表。</summary>
        public IReadOnlyList<CurrencyCostEntry> CurrencyCosts => currencyCosts;

        /// <summary>验证物品和货币消耗列表。</summary>
        /// <exception cref="InvalidOperationException">消耗列表包含空项或非法数据时抛出。</exception>
        public void Validate()
        {
            if (itemCosts == null || currencyCosts == null) throw new InvalidOperationException("成长消耗列表不能为 null。");
            for (int index = 0; index < itemCosts.Length; index++)
            {
                if (itemCosts[index] == null) throw new InvalidOperationException($"成长消耗的物品项 {index} 为空。");
                itemCosts[index].Validate();
            }

            for (int index = 0; index < currencyCosts.Length; index++)
            {
                if (currencyCosts[index] == null) throw new InvalidOperationException($"成长消耗的货币项 {index} 为空。");
                currencyCosts[index].Validate();
            }
        }
    }
}
