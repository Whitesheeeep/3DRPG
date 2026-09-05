using System;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.ItemSystem
{
    /// <summary>武器定义使用的类型固有默认数据。</summary>
    [Serializable]
    public sealed class WeaponItemDefaultData : ItemDefaultData
    {
        #region 默认字段

        [SerializeField, MinValue(1), LabelText("默认最大等级")] private int defaultMaxLevel = 90;
        [SerializeField, MinValue(0), LabelText("默认最大突破阶数")] private int defaultMaxAscensionRank = 6;
        [SerializeField, MinValue(1), LabelText("默认最大精炼阶数")] private int defaultMaxRefinementRank = 5;

        #endregion

        #region 属性

        /// <summary>获取武器固定分类。</summary>
        public override ItemCategory Category => ItemCategory.Weapon;

        /// <summary>获取默认最大等级。</summary>
        public int DefaultMaxLevel => defaultMaxLevel;

        /// <summary>获取默认最大突破阶数。</summary>
        public int DefaultMaxAscensionRank => defaultMaxAscensionRank;

        /// <summary>获取默认最大精炼阶数。</summary>
        public int DefaultMaxRefinementRank => defaultMaxRefinementRank;

        #endregion

        #region 校验

        /// <summary>验证武器默认数据的等级和阶段上限。</summary>
        /// <exception cref="InvalidOperationException">任一上限不合法时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (DefaultMaxLevel < 1)
                throw new InvalidOperationException("武器默认最大等级必须大于零。");
            if (DefaultMaxAscensionRank < 0)
                throw new InvalidOperationException("武器默认最大突破阶数不能为负数。");
            if (DefaultMaxRefinementRank < 1)
                throw new InvalidOperationException("武器默认最大精炼阶数必须大于零。");
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器默认值应用

        /// <summary>将武器默认字段写入 Definition，并同步成长配置最大等级。</summary>
        /// <param name="definitionSerializedObject">目标武器 Definition 的序列化对象。</param>
        public override void ApplyDefault(SerializedObject definitionSerializedObject)
        {
            Validate();
            WeaponDefinition definition = RequireDefinition<WeaponDefinition>(definitionSerializedObject);
            SerializedProperty categoryProperty = RequireProperty(definitionSerializedObject, "category");
            if (categoryProperty.intValue != (int)Category)
                throw new InvalidOperationException($"武器默认数据与物品定义 '{definition.name}' 的分类不一致。");

            // 先取得全部 Definition 字段，保证后续 Profile 写入前已经完成序列化结构检查。
            SerializedProperty rarityProperty = RequireProperty(definitionSerializedObject, "rarity");
            SerializedProperty sortPriorityProperty = RequireProperty(definitionSerializedObject, "sortPriority");
            SerializedProperty maxLevelProperty = RequireProperty(definitionSerializedObject, "maxLevel");
            SerializedProperty maxAscensionRankProperty = RequireProperty(definitionSerializedObject, "maxAscensionRank");
            SerializedProperty maxRefinementRankProperty = RequireProperty(definitionSerializedObject, "maxRefinementRank");
            SerializedProperty profileProperty = RequireProperty(definitionSerializedObject, "growthProfile");
            UnityEngine.Object profile = profileProperty.objectReferenceValue ?? definition.GrowthProfile;
            // 先校验并记录 Profile，再提交 Definition，确保一次默认值操作可整体撤销。
            SynchronizeProfileMaxLevel(definition, profile, DefaultMaxLevel, "WeaponGrowthProfile", nameof(WeaponDefinition));
            rarityProperty.intValue = (int)DefaultRarity;
            sortPriorityProperty.intValue = DefaultSortPriority;
            maxLevelProperty.intValue = DefaultMaxLevel;
            maxAscensionRankProperty.intValue = DefaultMaxAscensionRank;
            maxRefinementRankProperty.intValue = DefaultMaxRefinementRank;
            definitionSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
        }

        #endregion
#endif
    }
}
