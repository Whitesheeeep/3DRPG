#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>集中执行 Attribute Specs 与 Set Definition 的校验、Undo 和资产修改。</summary>
    public sealed class GameplayAttributeEditorService
    {
        #region Spec 操作

        /// <summary>创建具有唯一默认名称的 Attribute Spec。</summary>
        /// <param name="registry">目标 Registry。</param>
        /// <returns>新建节点；Registry 为空时返回 null。</returns>
        public GameplayAttributeEditorNode CreateSpec(GameplayAttributeRegistry registry)
        {
            if (registry == null) return null;
            string name = CreateUniqueSpecName(registry);
            Undo.RecordObject(registry, "Create Gameplay Attribute Spec");
            GameplayAttributeEditorNode node = registry.CreateNode(name);
            EditorUtility.SetDirty(registry);
            return node;
        }

        /// <summary>校验并重命名 Attribute Spec。</summary>
        /// <param name="registry">目标 Registry。</param>
        /// <param name="node">目标节点。</param>
        /// <param name="name">候选名称。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>名称合法并已提交时返回 true。</returns>
        public bool TryRenameSpec(
            GameplayAttributeRegistry registry,
            GameplayAttributeEditorNode node,
            string name,
            out string error)
        {
            string normalized = name?.Trim() ?? string.Empty;
            if (registry == null || node == null)
            {
                error = "未选择 Attribute Spec。";
                return false;
            }

            if (string.IsNullOrEmpty(normalized))
            {
                error = "Attribute 名称不能为空。";
                return false;
            }

            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode other = registry.Nodes[i];
                if (other != null && other != node &&
                    string.Equals(other.Name, normalized, StringComparison.Ordinal))
                {
                    error = $"Attribute 名称 '{normalized}' 已存在。";
                    return false;
                }
            }

            string identifier = RuntimeGameplayAttributeGenerator.CreateIdentifier(normalized);
            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode other = registry.Nodes[i];
                if (other != null && other != node &&
                    RuntimeGameplayAttributeGenerator.CreateIdentifier(other.Name) == identifier)
                {
                    error = $"名称会与 '{other.Name}' 生成相同 C# 标识符。";
                    return false;
                }
            }

            Undo.RecordObject(registry, "Rename Gameplay Attribute Spec");
            registry.RenameNode(node, normalized);
            EditorUtility.SetDirty(registry);
            error = string.Empty;
            return true;
        }

        /// <summary>修改 Attribute Spec 说明。</summary>
        /// <param name="registry">目标 Registry。</param>
        /// <param name="node">目标 Spec。</param>
        /// <param name="description">新的作者说明。</param>
        public void SetSpecDescription(
            GameplayAttributeRegistry registry,
            GameplayAttributeEditorNode node,
            string description)
        {
            if (registry == null || node == null) return;
            Undo.RecordObject(registry, "Edit Gameplay Attribute Description");
            registry.SetDescription(node, description ?? string.Empty);
            EditorUtility.SetDirty(registry);
        }

        /// <summary>删除 Attribute Spec；已烘焙 ID 在下次 Bake 时进入废弃列表。</summary>
        /// <param name="registry">目标 Registry。</param>
        /// <param name="node">待删除 Spec。</param>
        /// <returns>实际删除时返回 true。</returns>
        public bool DeleteSpec(GameplayAttributeRegistry registry, GameplayAttributeEditorNode node)
        {
            if (registry == null || node == null) return false;
            Undo.RecordObject(registry, "Delete Gameplay Attribute Spec");
            bool removed = registry.RemoveNode(node.Guid);
            if (removed) EditorUtility.SetDirty(registry);
            return removed;
        }

        #endregion

        #region Set 操作

        /// <summary>在指定项目路径创建基础 GameplayAttributeSet 资产。</summary>
        /// <param name="assetPath">必须位于 Assets 下的目标路径。</param>
        /// <returns>创建成功的 Set；路径无效时返回 null。</returns>
        public GameplayAttributeSet CreateSetAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            var set = ScriptableObject.CreateInstance<GameplayAttributeSet>();
            AssetDatabase.CreateAsset(set, assetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = set;
            return set;
        }

        /// <summary>向 Set 添加 Definition，并通过 SerializedObject 保证 Undo 与序列化一致。</summary>
        /// <param name="set">目标 AttributeSet。</param>
        /// <param name="attribute">已烘焙 Attribute。</param>
        /// <param name="type">作者分类。</param>
        /// <param name="error">失败时返回原因。</param>
        /// <returns>Definition 已完整写入时返回 true。</returns>
        public bool AddDefinition(
            GameplayAttributeSet set,
            GameplayAttribute attribute,
            GameplayAttributeType type,
            out string error)
        {
            if (set == null || !attribute.IsValid)
            {
                error = "未选择 Set 或没有可用的已烘焙 Attribute。";
                return false;
            }

            if (FindDefinitionIndex(set, attribute.Id) >= 0)
            {
                error = $"AttributeId {attribute.Id} 已存在于当前 Set。";
                return false;
            }

            Undo.RecordObject(set, "Add Gameplay Attribute Definition");
            var serializedObject = new SerializedObject(set);
            SerializedProperty definitions = serializedObject.FindProperty("definitions");
            int index = definitions.arraySize;
            definitions.arraySize++;
            SerializedProperty element = definitions.GetArrayElementAtIndex(index);
            SetDefinitionProperties(
                element,
                attribute,
                type,
                0f,
                float.NegativeInfinity,
                float.PositiveInfinity);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            error = string.Empty;
            return true;
        }

        /// <summary>校验并整体提交一个 Definition 的配置字段。</summary>
        /// <param name="set">目标 AttributeSet。</param>
        /// <param name="request">一次完整编辑请求。</param>
        /// <param name="error">失败时返回原因。</param>
        /// <returns>全部字段合法且已单步提交时返回 true。</returns>
        public bool TryUpdateDefinition(
            GameplayAttributeSet set,
            GameplayAttributeDefinitionEditRequest request,
            out string error)
        {
            if (set == null)
            {
                error = "未选择 GameplayAttributeSet。";
                return false;
            }

            if (!TryValidateValues(
                    request.Attribute,
                    request.DefaultValue,
                    request.MinValue,
                    request.MaxValue,
                    out error))
                return false;

            int index = FindDefinitionIndex(set, request.OriginalAttributeId);
            if (index < 0)
            {
                error = "原 Definition 已不存在，请刷新后重试。";
                return false;
            }

            int duplicateIndex = FindDefinitionIndex(set, request.Attribute.Id);
            if (duplicateIndex >= 0 && duplicateIndex != index)
            {
                error = $"AttributeId {request.Attribute.Id} 已存在于当前 Set。";
                return false;
            }

            Undo.RecordObject(set, "Edit Gameplay Attribute Definition");
            var serializedObject = new SerializedObject(set);
            SerializedProperty definitions = serializedObject.FindProperty("definitions");
            SetDefinitionProperties(
                definitions.GetArrayElementAtIndex(index),
                request.Attribute,
                request.Type,
                request.DefaultValue,
                request.MinValue,
                request.MaxValue);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            error = string.Empty;
            return true;
        }

        /// <summary>从 Set 删除指定 AttributeId 的 Definition。</summary>
        /// <param name="set">目标 AttributeSet。</param>
        /// <param name="attributeId">用于稳定定位 Definition 的 AttributeId。</param>
        /// <returns>实际删除时返回 true。</returns>
        public bool DeleteDefinition(GameplayAttributeSet set, int attributeId)
        {
            int index = FindDefinitionIndex(set, attributeId);
            if (index < 0) return false;

            Undo.RecordObject(set, "Delete Gameplay Attribute Definition");
            var serializedObject = new SerializedObject(set);
            SerializedProperty definitions = serializedObject.FindProperty("definitions");
            definitions.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(set);
            return true;
        }

        #endregion

        #region 查询与校验

        /// <summary>查找项目中引用指定已烘焙 Attribute 的 Set 资产路径。</summary>
        /// <param name="attribute">待反查的已烘焙 Attribute。</param>
        /// <returns>按 AssetDatabase 枚举得到的引用资产路径。</returns>
        public List<string> FindSetReferences(GameplayAttribute attribute)
        {
            var paths = new List<string>();
            if (!attribute.IsValid) return paths;
            foreach (string guid in AssetDatabase.FindAssets("t:GameplayAttributeSet"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayAttributeSet set = AssetDatabase.LoadAssetAtPath<GameplayAttributeSet>(path);
                if (FindDefinitionIndex(set, attribute.Id) >= 0) paths.Add(path);
            }

            return paths;
        }

        /// <summary>验证当前 Set 的全部 Definition。</summary>
        /// <param name="set">待验证 Set；null 表示没有 Set 错误。</param>
        /// <param name="registry">用于解析已烘焙 ID 的 Registry。</param>
        /// <returns>所有 Definition 错误；空列表表示验证通过。</returns>
        public List<string> ValidateSet(
            GameplayAttributeSet set,
            GameplayAttributeRegistry registry)
        {
            var errors = new List<string>();
            if (set == null) return errors;
            var ids = new HashSet<int>();
            for (int i = 0; i < set.Definitions.Count; i++)
            {
                GameplayAttributeDefinition definition = set.Definitions[i];
                if (definition == null)
                {
                    errors.Add($"第 {i} 项 Definition 为 null。");
                    continue;
                }

                if (!TryValidateValues(
                        definition.Attribute,
                        definition.DefaultValue,
                        definition.MinValue,
                        definition.MaxValue,
                        out string error))
                    errors.Add(error);
                if (!ids.Add(definition.Attribute.Id))
                    errors.Add($"AttributeId {definition.Attribute.Id} 重复。");
                if (registry == null ||
                    !registry.TryGetNodeById(definition.Attribute.Id, out _))
                    errors.Add($"AttributeId {definition.Attribute.Id} 未在当前 Registry 中烘焙。");
            }

            return errors;
        }

        // 按 AttributeId 线性查找 Set Definition，保持与运行时小数据量策略一致。
        internal static int FindDefinitionIndex(GameplayAttributeSet set, int attributeId)
        {
            if (set == null || attributeId < 0) return -1;
            for (int i = 0; i < set.Definitions.Count; i++)
            {
                GameplayAttributeDefinition definition = set.Definitions[i];
                if (definition != null && definition.Attribute.Id == attributeId) return i;
            }

            return -1;
        }

        // 校验 Definition 的公共配置边界，拒绝把非法浮点值写入资产。
        private static bool TryValidateValues(
            GameplayAttribute attribute,
            float defaultValue,
            float minValue,
            float maxValue,
            out string error)
        {
            if (!attribute.IsValid)
            {
                error = "必须选择一个已烘焙 Attribute。";
                return false;
            }

            if (float.IsNaN(defaultValue) || float.IsInfinity(defaultValue))
            {
                error = "DefaultValue 必须是有限数值。";
                return false;
            }

            if (float.IsNaN(minValue) || float.IsPositiveInfinity(minValue))
            {
                error = "MinValue 只能是有限值或 NegativeInfinity。";
                return false;
            }

            if (float.IsNaN(maxValue) || float.IsNegativeInfinity(maxValue))
            {
                error = "MaxValue 只能是有限值或 PositiveInfinity。";
                return false;
            }

            if (minValue > maxValue || defaultValue < minValue || defaultValue > maxValue)
            {
                error = "必须满足 MinValue ≤ DefaultValue ≤ MaxValue。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        #endregion

        #region SerializedProperty 辅助

        // 将一次 Definition 编辑写入同一 SerializedProperty，保证单步 Undo。
        private static void SetDefinitionProperties(
            SerializedProperty element,
            GameplayAttribute attribute,
            GameplayAttributeType type,
            float defaultValue,
            float minValue,
            float maxValue)
        {
            element.FindPropertyRelative("attribute").FindPropertyRelative("id").intValue = attribute.Id;
            element.FindPropertyRelative("attribute").FindPropertyRelative("name").stringValue = attribute.Name;
            element.FindPropertyRelative("type").enumValueIndex = (int)type;
            element.FindPropertyRelative("defaultValue").floatValue = defaultValue;
            element.FindPropertyRelative("minValue").floatValue = minValue;
            element.FindPropertyRelative("maxValue").floatValue = maxValue;
        }

        // 生成不会与现有 Spec 冲突的默认名称。
        private static string CreateUniqueSpecName(GameplayAttributeRegistry registry)
        {
            const string root = "NewAttribute";
            string candidate = root;
            int suffix = 1;
            while (ContainsName(registry, candidate)) candidate = root + suffix++;
            return candidate;
        }

        // 按 Ordinal 规则检查全局名称。
        private static bool ContainsName(GameplayAttributeRegistry registry, string name)
        {
            for (int i = 0; i < registry.Nodes.Count; i++)
                if (registry.Nodes[i] != null &&
                    string.Equals(registry.Nodes[i].Name, name, StringComparison.Ordinal))
                    return true;
            return false;
        }

        #endregion
    }
}
#endif
