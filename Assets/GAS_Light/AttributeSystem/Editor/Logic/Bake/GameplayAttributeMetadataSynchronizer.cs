#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>将 Attribute Registry 的名称元数据同步到项目内已序列化的 Attribute 副本。</summary>
    public static class GameplayAttributeMetadataSynchronizer
    {
        /// <summary>扫描 AttributeSet 与 GameplayEffectData 并原子更新名称副本。</summary>
        /// <param name="attributeByNameMap">按作者名称提供完整 Attribute 元数据的映射。</param>
        public static void Synchronize(IReadOnlyDictionary<string, GameplayAttribute> attributeByNameMap)
        {
            var attributeByIdMap = new Dictionary<int, GameplayAttribute>();
            foreach (GameplayAttribute attribute in attributeByNameMap.Values)
                attributeByIdMap[attribute.Id] = attribute;

            SynchronizeAssets("t:GameplayAttributeSet", attributeByIdMap);
            SynchronizeAssets("t:GameplayEffectData", attributeByIdMap);
        }

        /// <summary>同步一个 Unity 类型过滤器命中的全部资源。</summary>
        /// <param name="filter">AssetDatabase 类型过滤器。</param>
        /// <param name="attributeByIdMap">按稳定 ID 查询名称的映射。</param>
        private static void SynchronizeAssets(
            string filter,
            IReadOnlyDictionary<int, GameplayAttribute> attributeByIdMap)
        {
            string[] assetGuids = AssetDatabase.FindAssets(filter);
            for (int index = 0; index < assetGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) continue;
                var serializedObject = new SerializedObject(asset);
                bool changed = SynchronizeSerializedObject(serializedObject, attributeByIdMap);
                if (!changed) continue;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
        }

        /// <summary>递归查找 GameplayAttribute 字段并改写其名称元数据。</summary>
        /// <param name="serializedObject">待同步对象。</param>
        /// <param name="attributeByIdMap">按稳定 ID 查询名称的映射。</param>
        /// <returns>至少改写一项时返回 true。</returns>
        private static bool SynchronizeSerializedObject(
            SerializedObject serializedObject,
            IReadOnlyDictionary<int, GameplayAttribute> attributeByIdMap)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool changed = false;
            while (iterator.NextVisible(true))
            {
                if (!string.Equals(iterator.name, "attribute", StringComparison.Ordinal)) continue;
                SerializedProperty idProperty = iterator.FindPropertyRelative("id");
                SerializedProperty nameProperty = iterator.FindPropertyRelative("name");
                SerializedProperty displayNameProperty = iterator.FindPropertyRelative("displayName");
                if (idProperty == null || nameProperty == null || displayNameProperty == null ||
                    !attributeByIdMap.TryGetValue(idProperty.intValue, out GameplayAttribute attribute))
                    continue;
                if (nameProperty.stringValue == attribute.Name &&
                    displayNameProperty.stringValue == attribute.DisplayName)
                    continue;
                nameProperty.stringValue = attribute.Name;
                displayNameProperty.stringValue = attribute.DisplayName;
                changed = true;
            }

            return changed;
        }
    }
}
#endif
