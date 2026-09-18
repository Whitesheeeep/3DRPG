using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem
{
    /// <summary>食物定义；合并原食材与料理并统一配置使用时 GE。</summary>
    [CreateAssetMenu(fileName = "FoodItemDefinition", menuName = "RPG/ItemSystem/Food Item", order = 1)]
    public sealed class FoodItemDefinition : StackableItemDefinition
    {
        [SerializeField, LabelText("使用时效果列表")] private List<GameplayEffectData> useEffects = new();

        /// <summary>获取食物使用时应用的 GE 配置。</summary>
        public IReadOnlyList<GameplayEffectData> UseEffects => useEffects;

        /// <summary>验证食物分类和使用效果。</summary>
        /// <exception cref="InvalidOperationException">食物配置不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            base.ValidateSpecific();
            if (Category != ItemCategory.Food)
                throw new InvalidOperationException($"食物定义 '{name}' 必须使用食物分类。");
            if (useEffects == null)
                throw new InvalidOperationException($"食物定义 '{name}' 的使用时效果列表不能为 null。");
            if (useEffects.Count == 0)
                throw new InvalidOperationException($"食物定义 '{name}' 至少需要配置一个使用时 GE。");
            for (int index = 0; index < useEffects.Count; index++)
                if (useEffects[index] == null)
                    throw new InvalidOperationException($"食物定义 '{name}' 的使用时效果第 {index + 1} 项为空。");
        }
    }
}
