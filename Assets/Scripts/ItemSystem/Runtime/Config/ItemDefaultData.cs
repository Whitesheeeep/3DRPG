using System;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RPG.ItemSystem
{
    /// <summary>
    /// 物品数据库中可按分类复用的默认数据基类。
    /// </summary>
    [Serializable]
    public abstract class ItemDefaultData
    {
        #region 公共默认字段

        [SerializeField, LabelText("默认稀有度")] private ItemRarity defaultRarity = ItemRarity.One;
        [SerializeField, LabelText("默认排序优先级")] private int defaultSortPriority;

        #endregion

        #region 公共属性

        /// <summary>获取该默认数据适用的物品分类。</summary>
        public abstract ItemCategory Category { get; }

        /// <summary>获取默认稀有度。</summary>
        public ItemRarity DefaultRarity => defaultRarity;

        /// <summary>获取默认排序优先级。</summary>
        public int DefaultSortPriority => defaultSortPriority;

        #endregion

        #region 校验

        /// <summary>验证默认数据的公共字段。</summary>
        /// <exception cref="InvalidOperationException">默认数据不满足配置契约时抛出。</exception>
        public virtual void Validate()
        {
            if (!Enum.IsDefined(typeof(ItemCategory), Category))
                throw new InvalidOperationException("ItemDefaultData 的分类无效。");
            if (!Enum.IsDefined(typeof(ItemRarity), DefaultRarity))
                throw new InvalidOperationException($"分类 {Category} 的默认稀有度无效。");
        }

        #endregion

#if UNITY_EDITOR
        #region 编辑器默认值应用

        /// <summary>
        /// 将当前默认数据写入目标 Definition，并在需要时同步其关联配置资产。
        /// </summary>
        /// <param name="definitionSerializedObject">目标 Definition 的序列化对象。</param>
        /// <exception cref="ArgumentNullException">序列化对象为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">目标类型或序列化字段不满足默认数据契约时抛出。</exception>
        public abstract void ApplyDefault(SerializedObject definitionSerializedObject);

        /// <summary>将基类默认字段写入 Definition 序列化对象。</summary>
        /// <param name="serializedObject">目标 Definition 的序列化对象。</param>
        protected void ApplyCommonDefaultFields(SerializedObject serializedObject)
        {
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            RequireProperty(serializedObject, "rarity").intValue = (int)DefaultRarity;
            RequireProperty(serializedObject, "sortPriority").intValue = DefaultSortPriority;
        }

        /// <summary>取得必须存在的序列化字段。</summary>
        /// <param name="serializedObject">目标序列化对象。</param>
        /// <param name="propertyName">字段名。</param>
        /// <returns>找到的字段。</returns>
        /// <exception cref="InvalidOperationException">字段不存在时抛出。</exception>
        protected static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"{serializedObject.targetObject.name} 缺少序列化字段 '{propertyName}'。");
            return property;
        }

        /// <summary>验证序列化对象的目标类型。</summary>
        /// <typeparam name="TDefinition">所需的 Definition 类型。</typeparam>
        /// <param name="serializedObject">目标序列化对象。</param>
        /// <returns>经过类型校验的 Definition。</returns>
        /// <exception cref="ArgumentNullException">序列化对象为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">目标不是所需 Definition 类型时抛出。</exception>
        protected static TDefinition RequireDefinition<TDefinition>(SerializedObject serializedObject)
            where TDefinition : ItemDefinition
        {
            if (serializedObject == null) throw new ArgumentNullException(nameof(serializedObject));
            if (!(serializedObject.targetObject is TDefinition definition))
                throw new InvalidOperationException($"默认数据要求目标为 {typeof(TDefinition).Name}，实际为 {serializedObject.targetObject.GetType().Name}。");
            return definition;
        }

        /// <summary>
        /// 将 Profile 的最大等级与默认值同步，并拒绝会破坏其他 Definition 的共享配置。
        /// </summary>
        /// <param name="definition">当前 Definition。</param>
        /// <param name="profile">当前 Definition 引用的成长配置。</param>
        /// <param name="maxLevel">目标最大等级。</param>
        /// <param name="profileFieldName">成长配置字段名，用于错误上下文。</param>
        /// <param name="definitionTypeName">Definition 类型名，用于资产扫描。</param>
        protected static void SynchronizeProfileMaxLevel(
            ItemDefinition definition,
            UnityEngine.Object profile,
            int maxLevel,
            string profileFieldName,
            string definitionTypeName)
        {
            if (profile == null)
                throw new InvalidOperationException($"物品定义 '{definition.name}' 缺少 {profileFieldName}。");
            if (maxLevel < 1)
                throw new InvalidOperationException($"物品定义 '{definition.name}' 的默认最大等级必须大于零。");

            // Profile 可能被多个 Definition 共享；先检查所有已落盘引用，避免写入后造成其他定义失效。
            string[] definitionGuids = AssetDatabase.FindAssets($"t:{definitionTypeName}");
            for (int index = 0; index < definitionGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(definitionGuids[index]);
                ItemDefinition other = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (other == null || other == definition) continue;
                SerializedObject otherSerialized = new SerializedObject(other);
                SerializedProperty otherProfile = otherSerialized.FindProperty("growthProfile");
                if (otherProfile == null || otherProfile.objectReferenceValue != profile) continue;
                SerializedProperty otherMaxLevel = otherSerialized.FindProperty("maxLevel");
                if (otherMaxLevel != null && otherMaxLevel.intValue != maxLevel)
                    throw new InvalidOperationException($"成长配置“{profile.name}”被不同最大等级的 {definitionTypeName} 共享，请先为当前定义创建独立成长配置。");
            }

            SerializedObject profileSerialized = new SerializedObject(profile);
            SerializedProperty profileMaxLevel = RequireProperty(profileSerialized, "maxLevel");
            if (profileMaxLevel.intValue == maxLevel) return;
            Undo.RecordObject(profile, "应用物品类型默认值");
            profileMaxLevel.intValue = maxLevel;
            profileSerialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        #endregion
#endif
    }
}
