using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>进入养成道具页签并按具体用途筛选的可堆叠 Definition。</summary>
    [CreateAssetMenu(fileName = "DevelopmentItemDefinition", menuName = "RPG/ItemSystem/Development Item", order = 2)]
    public sealed class DevelopmentItemDefinition : StackableItemDefinition
    {
        [SerializeField, LabelText("养成用途")] private DevelopmentItemType developmentType;
        [SerializeField, Min(0), LabelText("提供经验值")] private int experienceValue;

        /// <summary>获取养成道具的具体用途。</summary>
        public DevelopmentItemType DevelopmentType => developmentType;

        /// <summary>获取经验类养成道具提供的经验值。</summary>
        public int ExperienceValue => experienceValue;

        /// <summary>验证养成用途和经验值之间的契约。</summary>
        /// <exception cref="InvalidOperationException">分类、用途或经验值不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category != ItemCategory.Material)
            {
                throw new InvalidOperationException($"养成道具 '{name}' 必须使用 Material 分类。");
            }

            // 养成道具复用可堆叠数量校验，但不走普通 Stackable 对专属分类的限制。
            this.ValidateSpecificForDevelopmentItem();
            bool isExperienceItem = developmentType == DevelopmentItemType.CharacterExperience ||
                                     developmentType == DevelopmentItemType.WeaponExperience ||
                                     developmentType == DevelopmentItemType.ArtifactExperience;
            if (isExperienceItem && experienceValue <= 0)
            {
                throw new InvalidOperationException($"养成道具 '{name}' 的经验类用途必须配置大于零的提供经验值。");
            }

            if (!isExperienceItem && experienceValue != 0)
            {
                throw new InvalidOperationException($"养成道具 '{name}' 的非经验类用途不能配置提供经验值。");
            }

            ValidateEffectList(UseEffects, "useEffects");
        }
    }

    /// <summary>为养成道具跳过普通 Stackable 分类限制的内部验证扩展。</summary>
    internal static class StackableItemDefinitionValidationExtensions
    {
        /// <summary>验证养成道具共享的数量上限。</summary>
        /// <param name="definition">待验证的养成道具。</param>
        /// <exception cref="InvalidOperationException">数量上限无效时抛出。</exception>
        internal static void ValidateSpecificForDevelopmentItem(this StackableItemDefinition definition)
        {
            if (definition.MaxQuantity < 1)
            {
                throw new InvalidOperationException($"养成道具 '{definition.name}' 的最大堆叠数量必须大于零。");
            }
        }
    }
}
