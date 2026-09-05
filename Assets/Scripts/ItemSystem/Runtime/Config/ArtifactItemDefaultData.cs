using System;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.ItemSystem
{
    /// <summary>圣遗物定义使用的类型固有默认数据。</summary>
    [Serializable]
    public sealed class ArtifactItemDefaultData : ItemDefaultData
    {
        #region 默认字段

        [SerializeField, MinValue(1), LabelText("默认最大等级")] private int defaultMaxLevel = 20;

        #endregion

        #region 属性

        /// <summary>获取圣遗物固定分类。</summary>
        public override ItemCategory Category => ItemCategory.Artifact;

        /// <summary>获取默认最大等级。</summary>
        public int DefaultMaxLevel => defaultMaxLevel;

        #endregion

        #region 校验

        /// <summary>验证圣遗物默认最大等级。</summary>
        /// <exception cref="InvalidOperationException">最大等级不合法时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (DefaultMaxLevel < 1)
                throw new InvalidOperationException("圣遗物默认最大等级必须大于零。");
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器默认值应用

        /// <summary>将圣遗物默认字段写入 Definition，并同步成长配置最大等级。</summary>
        /// <param name="definitionSerializedObject">目标圣遗物 Definition 的序列化对象。</param>
        public override void ApplyDefault(SerializedObject definitionSerializedObject)
        {
            Validate();
            ArtifactDefinition definition = RequireDefinition<ArtifactDefinition>(definitionSerializedObject);
            SerializedProperty categoryProperty = RequireProperty(definitionSerializedObject, "category");
            if (categoryProperty.intValue != (int)Category)
                throw new InvalidOperationException($"圣遗物默认数据与物品定义 '{definition.name}' 的分类不一致。");

            // 先确认 Definition 的公共字段和等级字段均存在，再触碰独立 GrowthProfile。
            SerializedProperty rarityProperty = RequireProperty(definitionSerializedObject, "rarity");
            SerializedProperty sortPriorityProperty = RequireProperty(definitionSerializedObject, "sortPriority");
            SerializedProperty maxLevelProperty = RequireProperty(definitionSerializedObject, "maxLevel");
            SerializedProperty profileProperty = RequireProperty(definitionSerializedObject, "growthProfile");
            UnityEngine.Object profile = profileProperty.objectReferenceValue ?? definition.GrowthProfile;
            // 先处理 Profile 的只读最大等级，再提交 Definition 的类型固有字段。
            SynchronizeProfileMaxLevel(definition, profile, DefaultMaxLevel, "ArtifactGrowthProfile", nameof(ArtifactDefinition));
            rarityProperty.intValue = (int)DefaultRarity;
            sortPriorityProperty.intValue = DefaultSortPriority;
            maxLevelProperty.intValue = DefaultMaxLevel;
            definitionSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
        }

        #endregion
#endif
    }
}
