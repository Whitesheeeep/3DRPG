using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>使用 ItemId 数量堆叠保存的物品定义基类。</summary>
    public abstract class StackableItemDefinition : ItemDefinition
    {
        [SerializeField, MinValue(1), LabelText("单种最大数量")] private int maxQuantity = 9999;

        /// <summary>获取单种物品最大持有数量。</summary>
        public int MaxQuantity => maxQuantity;

        /// <summary>验证可堆叠物品字段，供具体 Definition 继续扩展。</summary>
        /// <exception cref="InvalidOperationException">堆叠配置不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (MaxQuantity < 1) throw new InvalidOperationException($"可堆叠物品 '{name}' 的 MaxQuantity 必须大于零。");
        }
    }
}
