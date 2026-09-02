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
    }

    /// <summary>一个等级、突破或精炼阶段的完整消耗集合。</summary>
    [Serializable]
    public sealed class WeaponGrowthCost
    {
        [SerializeField, LabelText("物品消耗")] private ItemCostEntry[] itemCosts = Array.Empty<ItemCostEntry>();
        [SerializeField, LabelText("货币消耗")] private CurrencyCostEntry[] currencyCosts = Array.Empty<CurrencyCostEntry>();

        /// <summary>获取物品消耗列表。</summary>
        public IReadOnlyList<ItemCostEntry> ItemCosts => itemCosts;

        /// <summary>获取货币消耗列表。</summary>
        public IReadOnlyList<CurrencyCostEntry> CurrencyCosts => currencyCosts;
    }
}
