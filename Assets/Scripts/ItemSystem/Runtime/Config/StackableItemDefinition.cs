using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>使用 ItemId 数量堆叠保存的普通物品定义。</summary>
    [CreateAssetMenu(fileName = "StackableItemDefinition", menuName = "RPG/ItemSystem/Stackable Item", order = 0)]
    public class StackableItemDefinition : ItemDefinition
    {
        [SerializeField, Min(1), LabelText("单种最大数量")] private int maxQuantity = 9999;
        [SerializeField, LabelText("使用时效果")] private List<GameplayEffectData> useEffects = new();

        /// <summary>获取单种物品最大持有数量。</summary>
        public int MaxQuantity => maxQuantity;

        /// <summary>获取未来使用物品时应用的 GE 配置。</summary>
        public IReadOnlyList<GameplayEffectData> UseEffects => useEffects;

        /// <summary>验证可堆叠物品字段。</summary>
        /// <exception cref="InvalidOperationException">堆叠配置不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category == ItemCategory.Weapon || Category == ItemCategory.Artifact ||
                Category == ItemCategory.DevelopmentItem)
            {
                throw new InvalidOperationException($"普通可堆叠物品 '{name}' 不能使用装备或养成道具分类。");
            }
            if (MaxQuantity < 1) throw new InvalidOperationException($"可堆叠物品 '{name}' 的 MaxQuantity 必须大于零。");
            ValidateEffectList(useEffects, "useEffects");
        }

        /// <summary>校验列表中的 GE 引用，保留作者顺序并拒绝空元素。</summary>
        /// <param name="effects">待校验的 GE 列表。</param>
        /// <param name="fieldName">用于错误信息的字段名称。</param>
        protected void ValidateEffectList(IReadOnlyList<GameplayEffectData> effects, string fieldName)
        {
            if (effects == null) throw new InvalidOperationException($"物品定义 '{name}' 的效果字段 '{fieldName}' 不能为 null。");
            for (int index = 0; index < effects.Count; index++)
                if (effects[index] == null) throw new InvalidOperationException($"物品定义 '{name}' 的效果字段 '{fieldName}' 第 {index} 项为空。");
        }
    }
}
