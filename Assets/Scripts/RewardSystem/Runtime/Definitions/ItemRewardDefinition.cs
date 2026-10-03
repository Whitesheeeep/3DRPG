using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.RewardSystemNS
{
    /// <summary>描述一次物品奖励中的单个物品数量。</summary>
    [Serializable]
    public sealed class ItemRewardEntry
    {
        #region 配置字段

        [SerializeField, LabelText("物品"), ItemIdDropdown]
        private ItemId itemId;
        [SerializeField, MinValue(1), LabelText("数量")]
        private int quantity = 1;

        #endregion

        #region 创建与查询

        /// <summary>创建供 Unity 序列化使用的物品奖励项。</summary>
        public ItemRewardEntry() { }

        /// <summary>创建指定物品和数量的奖励项。</summary>
        /// <param name="itemId">物品定义标识。</param>
        /// <param name="quantity">正数数量。</param>
        public ItemRewardEntry(ItemId itemId, int quantity)
        {
            this.itemId = itemId;
            this.quantity = quantity;
        }

        /// <summary>获取物品定义标识。</summary>
        public ItemId ItemId => itemId;

        /// <summary>获取奖励数量。</summary>
        public int Quantity => quantity;

        #endregion

        #region 配置校验

        /// <summary>校验物品标识和数量。</summary>
        /// <exception cref="ArgumentException">标识无效或数量不是正数时抛出。</exception>
        public void Validate()
        {
            if (!itemId.IsValid || quantity <= 0)
                throw new ArgumentException("物品奖励项必须使用有效 ItemId 和正数数量。");
        }

        #endregion
    }

    /// <summary>描述一组可堆叠物品、武器或圣遗物奖励。</summary>
    [Serializable]
    public sealed class ItemRewardDefinition : RewardDefinition
    {
        #region 配置字段

        [SerializeField] private List<ItemRewardEntry> items = new List<ItemRewardEntry>();

        #endregion

        #region 创建与查询

        /// <summary>创建供 Unity 多态序列化使用的物品奖励定义。</summary>
        public ItemRewardDefinition() { }

        /// <summary>创建指定物品列表的奖励定义。</summary>
        /// <param name="rewardItems">物品奖励项。</param>
        public ItemRewardDefinition(IEnumerable<ItemRewardEntry> rewardItems)
        {
            items = rewardItems == null ? new List<ItemRewardEntry>() : new List<ItemRewardEntry>(rewardItems);
        }

        /// <summary>获取物品奖励项只读列表。</summary>
        public IReadOnlyList<ItemRewardEntry> Items => items;

        #endregion

        #region 配置校验

        /// <summary>校验奖励列表不为空且每一项均有效。</summary>
        /// <exception cref="ArgumentException">列表为空或包含无效项时抛出。</exception>
        public override void Validate()
        {
            if (items == null) throw new ArgumentException("物品奖励列表不能为 null。", nameof(items));
            if (items.Count == 0) throw new ArgumentException("物品奖励至少需要一个物品项。");
            for (int index = 0; index < items.Count; index++)
            {
                if (items[index] == null) throw new ArgumentException("物品奖励包含空物品项。");
                items[index].Validate();
            }
        }

        #endregion
    }
}
