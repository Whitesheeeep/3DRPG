using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>仅作为成长成本使用且不提供经验的可堆叠养成 Definition。</summary>
    [CreateAssetMenu(fileName = "DevelopmentItemDefinition", menuName = "RPG/ItemSystem/Development Item", order = 2)]
    public sealed class DevelopmentItemDefinition : StackableItemDefinition
    {
        [SerializeField, EnumToggleButtons, LabelText("养成用途")] private DevelopmentItemType developmentTypes;

        /// <summary>获取养成道具支持的用途组合。</summary>
        public DevelopmentItemType DevelopmentTypes => developmentTypes;

        /// <summary>判断该道具是否支持指定的任一用途。</summary>
        /// <param name="requestedTypes">待查询用途组合。</param>
        /// <returns>存在任意交集时返回 true。</returns>
        public bool SupportsDevelopmentType(DevelopmentItemType requestedTypes) =>
            requestedTypes != DevelopmentItemType.None &&
            (developmentTypes & requestedTypes) != DevelopmentItemType.None;

        /// <summary>验证分类和养成用途契约。</summary>
        /// <exception cref="InvalidOperationException">分类或用途不合法时抛出。</exception>
        protected override void ValidateSpecific()
        {
            if (Category != ItemCategory.DevelopmentItem)
            {
                throw new InvalidOperationException($"养成道具 '{name}' 必须使用养成道具分类。");
            }

            base.ValidateSpecific();
            const DevelopmentItemType definedTypes = DevelopmentItemType.CharacterAscension |
                                                      DevelopmentItemType.CharacterTalent |
                                                      DevelopmentItemType.WeaponAscension |
                                                      DevelopmentItemType.WeaponRefinement;
            if (developmentTypes == DevelopmentItemType.None ||
                (developmentTypes & ~definedTypes) != DevelopmentItemType.None)
                throw new InvalidOperationException($"养成道具 '{name}' 必须配置一个或多个有效养成用途。");
        }
    }
}
