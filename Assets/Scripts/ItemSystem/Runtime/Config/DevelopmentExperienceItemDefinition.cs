using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>提供角色、武器或圣遗物成长经验的可堆叠 Definition。</summary>
    [CreateAssetMenu(fileName = "DevelopmentExperienceItemDefinition", menuName = "RPG/ItemSystem/Development Experience Item", order = 3)]
    public sealed class DevelopmentExperienceItemDefinition : StackableItemDefinition
    {
        #region 配置字段

        [SerializeField, EnumToggleButtons, LabelText("经验适用对象")] private DevelopmentExperienceItemType experienceTypes;
        [SerializeField, MinValue(1), LabelText("提供经验值")] private int experienceValue = 100;

        #endregion

        #region 公开属性

        /// <summary>获取该经验道具支持的成长对象组合。</summary>
        public DevelopmentExperienceItemType ExperienceTypes => experienceTypes;

        /// <summary>获取该经验道具提供的经验值。</summary>
        public int ExperienceValue => experienceValue;

        /// <summary>判断该经验道具是否支持指定的任一成长对象。</summary>
        /// <param name="requestedTypes">待查询成长对象组合。</param>
        /// <returns>存在任意交集时返回 true。</returns>
        public bool SupportsExperienceType(DevelopmentExperienceItemType requestedTypes) =>
            requestedTypes != DevelopmentExperienceItemType.None &&
            (experienceTypes & requestedTypes) != DevelopmentExperienceItemType.None;

        #endregion

        #region 校验

        /// <summary>验证经验道具分类、用途和经验值。</summary>
        /// <exception cref="InvalidOperationException">配置不满足经验道具契约时抛出。</exception>
        protected override void ValidateSpecific()
        {
            base.ValidateSpecific();
            if (Category != ItemCategory.DevelopmentExperienceItem)
                throw new InvalidOperationException($"养成经验道具 '{name}' 必须使用养成经验道具分类。");

            const DevelopmentExperienceItemType definedTypes = DevelopmentExperienceItemType.Character |
                                                                DevelopmentExperienceItemType.Weapon |
                                                                DevelopmentExperienceItemType.Artifact;
            if (experienceTypes == DevelopmentExperienceItemType.None ||
                (experienceTypes & ~definedTypes) != DevelopmentExperienceItemType.None)
                throw new InvalidOperationException($"养成经验道具 '{name}' 必须配置一个或多个有效经验适用对象。");
            if (experienceValue <= 0)
                throw new InvalidOperationException($"养成经验道具 '{name}' 的提供经验值必须大于零。");
        }

        #endregion
    }
}
