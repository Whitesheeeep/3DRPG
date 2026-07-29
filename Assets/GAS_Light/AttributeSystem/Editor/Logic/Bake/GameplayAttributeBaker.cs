#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>校验 Attribute Specs、分配全局稳定 ID，并原子生成运行时代码。</summary>
    public static class GameplayAttributeBaker
    {
        #region Bake

        /// <summary>验证并 Bake 指定 Registry。</summary>
        /// <param name="registry">待 Bake 的 Editor Registry。</param>
        /// <param name="message">返回成功摘要或完整错误列表。</param>
        /// <returns>运行时代码与 Registry 均已提交时返回 true。</returns>
        public static bool TryBake(GameplayAttributeRegistry registry, out string message)
        {
            List<string> errors = ValidateRegistry(registry);
            if (errors.Count > 0)
            {
                message = string.Join("\n", errors);
                return false;
            }

            var oldByGuid = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < registry.IdRecords.Count; i++)
            {
                GameplayAttributeIdRecord record = registry.IdRecords[i];
                if (record != null && !string.IsNullOrEmpty(record.Guid))
                    oldByGuid[record.Guid] = record.Id;
            }

            int nextId = registry.NextId;
            var records = new List<GameplayAttributeIdRecord>(registry.Nodes.Count);
            var generated = new Dictionary<string, GameplayAttribute>(StringComparer.Ordinal);
            var activeGuids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode node = registry.Nodes[i];
                int id;
                if (!oldByGuid.TryGetValue(node.Guid, out id)) id = nextId++;
                activeGuids.Add(node.Guid);
                records.Add(new GameplayAttributeIdRecord(node.Guid, id));
                generated.Add(node.Name, new GameplayAttribute(id));
            }

            var retired = new HashSet<int>(registry.RetiredIds);
            foreach (KeyValuePair<string, int> pair in oldByGuid)
                if (!activeGuids.Contains(pair.Key))
                    retired.Add(pair.Value);

            string source = RuntimeGameplayAttributeGenerator.GenerateSource(generated);
            try
            {
                RuntimeGameplayAttributeGenerator.WriteAtomically(source);
            }
            catch (Exception exception)
            {
                message = $"生成 Attribute 常量失败，保留上一次有效数据：{exception.Message}";
                return false;
            }

            Undo.RecordObject(registry, "Bake Gameplay Attributes");
            registry.ApplyBake(records, retired.OrderBy(id => id).ToList(), nextId);
            EditorUtility.SetDirty(registry);
            AssetDatabase.ImportAsset(RuntimeGameplayAttributeGenerator.GeneratedAssetPath);
            AssetDatabase.SaveAssets();
            message = $"Bake 成功：{records.Count} 个 Attribute，NextId={nextId}。";
            return true;
        }

        #endregion

        #region 校验

        /// <summary>验证 Registry 作者数据和生成标识符，不修改任何资产。</summary>
        /// <param name="registry">待验证 Registry。</param>
        /// <returns>错误文本列表；空列表表示可 Bake。</returns>
        public static List<string> ValidateRegistry(GameplayAttributeRegistry registry)
        {
            var errors = new List<string>();
            if (registry == null)
            {
                errors.Add("未选择 GameplayAttributeRegistry。");
                return errors;
            }

            var guids = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var identifiers = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode node = registry.Nodes[i];
                if (node == null)
                {
                    errors.Add($"第 {i} 个 Attribute Spec 为 null。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Guid) || !guids.Add(node.Guid))
                    errors.Add($"Attribute '{node.Name}' 的 Guid 为空或重复。");

                string name = node.Name?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add($"Guid {node.Guid} 的名称为空。");
                    continue;
                }

                if (!names.Add(name)) errors.Add($"Attribute 名称重复：{name}。");
                string identifier = RuntimeGameplayAttributeGenerator.CreateIdentifier(name);
                if (identifiers.TryGetValue(identifier, out string other))
                    errors.Add($"'{other}' 与 '{name}' 生成相同 C# 标识符：{identifier}。");
                else
                    identifiers.Add(identifier, name);
            }

            ValidateIdHistory(registry, errors);
            return errors;
        }

        // 校验已分配和废弃 ID 的唯一性与 nextId 单调性，防止损坏历史导致 ID 复用。
        private static void ValidateIdHistory(
            GameplayAttributeRegistry registry,
            ICollection<string> errors)
        {
            var recordGuids = new HashSet<string>(StringComparer.Ordinal);
            var allocatedIds = new HashSet<int>();
            int highestId = -1;
            for (int i = 0; i < registry.IdRecords.Count; i++)
            {
                GameplayAttributeIdRecord record = registry.IdRecords[i];
                if (record == null)
                {
                    errors.Add($"第 {i} 项 Attribute ID 历史为 null。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(record.Guid) || !recordGuids.Add(record.Guid))
                    errors.Add($"Attribute ID 历史包含空或重复 Guid：{record.Guid}。");
                if (record.Id < 0 || !allocatedIds.Add(record.Id))
                    errors.Add($"Attribute ID 历史包含非法或重复 ID：{record.Id}。");
                highestId = Math.Max(highestId, record.Id);
            }

            var retiredIds = new HashSet<int>();
            for (int i = 0; i < registry.RetiredIds.Count; i++)
            {
                int retiredId = registry.RetiredIds[i];
                if (retiredId < 0 || !retiredIds.Add(retiredId))
                    errors.Add($"废弃 ID 列表包含非法或重复 ID：{retiredId}。");
                if (allocatedIds.Contains(retiredId))
                    errors.Add($"AttributeId {retiredId} 同时处于有效历史和废弃列表。");
                highestId = Math.Max(highestId, retiredId);
            }

            if (registry.NextId < 0 || registry.NextId <= highestId)
                errors.Add($"nextId {registry.NextId} 必须大于全部已分配或废弃 ID（当前最大 {highestId}）。");
        }

        /// <summary>验证全部 GameplayAttributeSet 的 Definition 配置与 Registry 引用。</summary>
        /// <param name="registry">用于解析稳定 ID 的 Registry。</param>
        /// <returns>带资产路径的错误列表。</returns>
        public static List<string> ValidateAllSets(GameplayAttributeRegistry registry)
        {
            var errors = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:GameplayAttributeSet");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameplayAttributeSet set = AssetDatabase.LoadAssetAtPath<GameplayAttributeSet>(path);
                if (set == null) continue;

                var ids = new HashSet<int>();
                for (int i = 0; i < set.Definitions.Count; i++)
                {
                    GameplayAttributeDefinition definition = set.Definitions[i];
                    if (definition == null)
                    {
                        errors.Add($"{path}: 第 {i} 项 Definition 为 null。");
                        continue;
                    }

                    if (!definition.TryValidateTemplate(out string error))
                        errors.Add($"{path}: {error}");
                    if (!ids.Add(definition.Attribute.Id))
                        errors.Add($"{path}: AttributeId {definition.Attribute.Id} 重复。");
                    if (registry == null ||
                        !registry.TryGetNodeById(definition.Attribute.Id, out _))
                        errors.Add($"{path}: AttributeId {definition.Attribute.Id} 未在当前 Registry 中烘焙。");
                }
            }

            return errors;
        }

        #endregion
    }
}
#endif
