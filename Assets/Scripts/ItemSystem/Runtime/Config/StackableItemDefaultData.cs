using System;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.ItemSystem
{
    /// <summary>普通物品和养成道具共用的可堆叠默认数据。</summary>
    [Serializable]
    public sealed class StackableItemDefaultData : ItemDefaultData
    {
        #region 默认字段

        [SerializeField, LabelText("物品类型")] private ItemCategory category = ItemCategory.Material;
        [SerializeField, MinValue(1), LabelText("默认最大堆叠数量")] private int defaultMaxQuantity = 9999;

        #endregion

        #region 属性

        /// <summary>获取该条默认数据适用的可堆叠分类。</summary>
        public override ItemCategory Category => category;

        /// <summary>获取可堆叠物品默认上限。</summary>
        public int DefaultMaxQuantity => defaultMaxQuantity;

        #endregion

        #region 校验

        /// <summary>验证可堆叠默认数据。</summary>
        /// <exception cref="InvalidOperationException">分类或数量上限不合法时抛出。</exception>
        public override void Validate()
        {
            base.Validate();
            if (Category == ItemCategory.Weapon || Category == ItemCategory.Artifact)
                throw new InvalidOperationException($"可堆叠默认数据不能使用 {Category} 分类。");
            if (DefaultMaxQuantity < 1)
                throw new InvalidOperationException($"分类 {Category} 的默认数量上限必须大于零。");
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器默认值应用

        /// <summary>将可堆叠默认字段写入普通物品或养成道具 Definition。</summary>
        /// <param name="definitionSerializedObject">目标 Definition 的序列化对象。</param>
        public override void ApplyDefault(SerializedObject definitionSerializedObject)
        {
            Validate();
            StackableItemDefinition definition = RequireDefinition<StackableItemDefinition>(definitionSerializedObject);
            SerializedProperty categoryProperty = RequireProperty(definitionSerializedObject, "category");
            if (categoryProperty.intValue != (int)Category)
                throw new InvalidOperationException($"默认数据分类 {Category} 与物品定义 '{definition.name}' 的分类不一致。");

            ApplyCommonDefaultFields(definitionSerializedObject);
            RequireProperty(definitionSerializedObject, "maxQuantity").intValue = DefaultMaxQuantity;
            definitionSerializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);
        }

        #endregion
#endif
    }
}
