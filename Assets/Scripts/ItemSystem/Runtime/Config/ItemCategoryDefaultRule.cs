using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>为新建或重新应用默认值提供的分类规则。</summary>
    [Serializable]
    public sealed class ItemCategoryDefaultRule
    {
        [SerializeField, LabelText("物品类型")] private ItemCategory category;
        [SerializeField, LabelText("默认稀有度")] private ItemRarity defaultRarity = ItemRarity.One;
        [SerializeField, LabelText("默认排序优先级")] private int defaultSortPriority;
        [SerializeField, MinValue(1), LabelText("默认最大堆叠数量")] private int defaultMaxQuantity = 9999;

        /// <summary>获取适用分类。</summary>
        public ItemCategory Category => category;

        /// <summary>获取默认稀有度。</summary>
        public ItemRarity DefaultRarity => defaultRarity;

        /// <summary>获取默认排序优先级。</summary>
        public int DefaultSortPriority => defaultSortPriority;

        /// <summary>获取可堆叠物品默认上限。</summary>
        public int DefaultMaxQuantity => defaultMaxQuantity;

        /// <summary>验证分类默认值。</summary>
        /// <exception cref="InvalidOperationException">默认值不合法时抛出。</exception>
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ItemCategory), Category)) throw new InvalidOperationException("ItemCategoryDefaultRule 的分类无效。");
            if (!Enum.IsDefined(typeof(ItemRarity), DefaultRarity)) throw new InvalidOperationException($"分类 {Category} 的默认稀有度无效。");
            if (DefaultMaxQuantity < 1) throw new InvalidOperationException($"分类 {Category} 的默认数量上限必须大于零。");
        }
    }
}
