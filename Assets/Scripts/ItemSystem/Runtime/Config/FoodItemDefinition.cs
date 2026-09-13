using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>料理定义；料理是可堆叠物品中唯一拥有使用时 GE 列表的类型。</summary>
    [CreateAssetMenu(fileName = "FoodItemDefinition", menuName = "RPG/ItemSystem/Food Item", order = 1)]
    public sealed class FoodItemDefinition : StackableItemDefinition
    {
        [SerializeField, LabelText("使用时效果列表")] private List<GameplayEffectData> useEffects = new();

        /// <summary>获取料理使用时应用的 GE 配置。</summary>
        public IReadOnlyList<GameplayEffectData> UseEffects => useEffects;

        /// <summary>验证料理分类和使用效果。</summary>
        /// <exception cref="InvalidOperationException">料理配置不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category != ItemCategory.Food)
                throw new InvalidOperationException($"料理定义 '{name}' 必须使用料理分类。");
            if (MaxQuantity < 1)
                throw new InvalidOperationException($"料理定义 '{name}' 的最大堆叠数量必须大于零。");
            if (useEffects == null)
                throw new InvalidOperationException($"料理定义 '{name}' 的使用时效果列表不能为 null。");
            for (int index = 0; index < useEffects.Count; index++)
                if (useEffects[index] == null)
                    throw new InvalidOperationException($"料理定义 '{name}' 的使用时效果第 {index + 1} 项为空。");
        }
    }
}
