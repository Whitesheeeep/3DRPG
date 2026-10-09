using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.NPC.Editor
{
    /// <summary>
    /// 在 Editor 导入后初始化新 NPC 身份，并只为可确认来源的复制资产生成新身份。
    /// </summary>
    internal sealed class NPCIdentityDefinitionAssetProcessor : AssetPostprocessor
    {
        #region 导入回调

        /// <summary>有资产导入、移动或删除时延后扫描身份，避免在 AssetDatabase 导入事务中写资源。</summary>
        /// <param name="importedAssets">本次导入的资产路径。</param>
        /// <param name="deletedAssets">本次删除的资产路径。</param>
        /// <param name="movedAssets">本次移动后的资产路径。</param>
        /// <param name="movedFromAssetPaths">本次移动前的资产路径。</param>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!ContainsAssetFile(importedAssets) &&
                !ContainsAssetFile(deletedAssets) &&
                !ContainsAssetFile(movedAssets))
            {
                return;
            }

            // SaveAssets 和身份修复必须在当前 AssetPostprocessor 回调结束后执行。
            EditorApplication.delayCall += ProcessAllIdentityAssets;
        }

        /// <summary>判断一批路径中是否包含需要触发身份扫描的 Unity 资产文件。</summary>
        /// <param name="assetPaths">AssetDatabase 提供的导入、删除或移动路径。</param>
        /// <returns>存在 .asset 文件时返回 true。</returns>
        private static bool ContainsAssetFile(string[] assetPaths)
        {
            for (int index = 0; index < assetPaths.Length; index++)
            {
                if (assetPaths[index].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        #endregion

        #region 身份扫描与修复

        /// <summary>扫描所有身份资产，初始化空 ID、隔离可确认副本并维护 Editor 所属记录。</summary>
        private static void ProcessAllIdentityAssets()
        {
            List<IdentityAssetRecord> identityAssetRecords = LoadIdentityAssetRecords();
            if (identityAssetRecords.Count == 0)
                return;

            var usedIdentityIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < identityAssetRecords.Count; index++)
            {
                string existingIdentityId = identityAssetRecords[index].IdentityId;
                if (!string.IsNullOrWhiteSpace(existingIdentityId))
                    usedIdentityIds.Add(existingIdentityId);
            }

            int initializedCount = InitializeMissingIdentityIds(identityAssetRecords, usedIdentityIds);
            Dictionary<string, List<IdentityAssetRecord>> identityAssetsByIdMap =
                BuildIdentityAssetsByIdMap(identityAssetRecords);

            int copiedIdentityCount = 0;
            int ownerGuidUpdateCount = 0;
            int ambiguousDuplicateAssetCount = 0;
            foreach (KeyValuePair<string, List<IdentityAssetRecord>> identityGroup in identityAssetsByIdMap)
            {
                List<IdentityAssetRecord> records = identityGroup.Value;
                if (records.Count == 1)
                {
                    ownerGuidUpdateCount += UpdateOwnerGuidIfNeeded(records[0]);
                    continue;
                }

                ResolveDuplicateIdentityGroup(
                    identityGroup.Key,
                    records,
                    usedIdentityIds,
                    ref copiedIdentityCount,
                    ref ambiguousDuplicateAssetCount);
            }

            int changedCount = initializedCount + copiedIdentityCount + ownerGuidUpdateCount;
            if (changedCount == 0)
                return;

            // 批量完成全部修复后只保存一次，避免逐资产导入和写回互相触发。
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[NPCIdentityDefinitionAssetProcessor] 身份扫描完成，初始化={initializedCount}，复制品换发={copiedIdentityCount}，所属 GUID 更新={ownerGuidUpdateCount}，未自动修改的歧义重复资产={ambiguousDuplicateAssetCount}。");
        }

        /// <summary>从 AssetDatabase 载入所有身份资产及其当前路径和 meta GUID。</summary>
        /// <returns>按 AssetDatabase 返回顺序排列的身份资产记录。</returns>
        private static List<IdentityAssetRecord> LoadIdentityAssetRecords()
        {
            string[] assetGuids = AssetDatabase.FindAssets("t:NPCIdentityDefinition");
            var records = new List<IdentityAssetRecord>(assetGuids.Length);
            for (int index = 0; index < assetGuids.Length; index++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[index]);
                NPCIdentityDefinition definition =
                    AssetDatabase.LoadAssetAtPath<NPCIdentityDefinition>(assetPath);
                if (definition == null)
                    continue;

                string currentAssetGuid = AssetDatabase.AssetPathToGUID(assetPath);
                records.Add(new IdentityAssetRecord(
                    definition,
                    assetPath,
                    currentAssetGuid,
                    definition.IdentityId));
            }

            return records;
        }

        /// <summary>只为尚无身份值的新资产生成 GUID，并记录当前资产 GUID 作为所有权证据。</summary>
        /// <param name="records">本次扫描到的身份资产。</param>
        /// <param name="usedIdentityIds">已占用的身份字符串，确保随机 ID 不覆盖现有身份。</param>
        /// <returns>完成初始化的资产数量。</returns>
        private static int InitializeMissingIdentityIds(
            List<IdentityAssetRecord> records,
            HashSet<string> usedIdentityIds)
        {
            int initializedCount = 0;
            for (int index = 0; index < records.Count; index++)
            {
                IdentityAssetRecord record = records[index];
                if (!string.IsNullOrWhiteSpace(record.IdentityId))
                    continue;

                string generatedIdentityId = CreateUniqueIdentityId(usedIdentityIds);
                record.Definition.EditorInitializeIdentity(generatedIdentityId, record.AssetGuid);
                record.IdentityId = generatedIdentityId;
                EditorUtility.SetDirty(record.Definition);
                initializedCount++;
                Debug.Log(
                    $"[NPCIdentityDefinitionAssetProcessor] 为新身份资产 '{record.Definition.name}' 初始化专属 ID。",
                    record.Definition);
            }

            return initializedCount;
        }

        /// <summary>构建身份 ID 到所有持有该 ID 的资产记录的映射。</summary>
        /// <param name="records">本次扫描到的身份资产。</param>
        /// <returns>key 为 identityId，value 为持有该身份的全部资产记录。</returns>
        private static Dictionary<string, List<IdentityAssetRecord>> BuildIdentityAssetsByIdMap(
            List<IdentityAssetRecord> records)
        {
            var identityAssetsByIdMap =
                new Dictionary<string, List<IdentityAssetRecord>>(StringComparer.Ordinal);
            for (int index = 0; index < records.Count; index++)
            {
                IdentityAssetRecord record = records[index];
                if (!NPCId.TryCreate(record.IdentityId, out _))
                {
                    Debug.LogError(
                        $"[NPCIdentityDefinitionAssetProcessor] 身份资产 '{record.Definition.name}' 的 identityId 无效；未自动改写。",
                        record.Definition);
                    continue;
                }

                if (!identityAssetsByIdMap.TryGetValue(record.IdentityId, out List<IdentityAssetRecord> recordsForId))
                {
                    recordsForId = new List<IdentityAssetRecord>();
                    identityAssetsByIdMap.Add(record.IdentityId, recordsForId);
                }

                recordsForId.Add(record);
            }

            return identityAssetsByIdMap;
        }

        /// <summary>当唯一资产的 meta GUID 变化时仅更新 Editor 所属记录，不改变其身份 ID。</summary>
        /// <param name="record">唯一持有该 ID 的身份资产记录。</param>
        /// <returns>所属 GUID 确实更新时返回 1，否则返回 0。</returns>
        private static int UpdateOwnerGuidIfNeeded(IdentityAssetRecord record)
        {
            if (string.Equals(record.Definition.EditorAssetGuid, record.AssetGuid, StringComparison.Ordinal))
                return 0;

            record.Definition.EditorUpdateAssetGuid(record.AssetGuid);
            EditorUtility.SetDirty(record.Definition);
            Debug.Log(
                $"[NPCIdentityDefinitionAssetProcessor] 身份资产 '{record.Definition.name}' 的 meta GUID 已变化，保留 identityId 并更新 Editor 所属记录。",
                record.Definition);
            return 1;
        }

        /// <summary>仅在资产所有权记录能唯一确认原件时为复制品换发身份。</summary>
        /// <param name="identityId">重复出现的身份字符串。</param>
        /// <param name="records">所有持有重复身份的资产记录。</param>
        /// <param name="usedIdentityIds">已占用的身份字符串。</param>
        /// <param name="copiedIdentityCount">累计获得新身份的复制品数量。</param>
        /// <param name="ambiguousDuplicateAssetCount">累计无法确认来源的重复资产数量。</param>
        private static void ResolveDuplicateIdentityGroup(
            string identityId,
            List<IdentityAssetRecord> records,
            HashSet<string> usedIdentityIds,
            ref int copiedIdentityCount,
            ref int ambiguousDuplicateAssetCount)
        {
            var ownerRecords = new List<IdentityAssetRecord>();
            for (int index = 0; index < records.Count; index++)
            {
                IdentityAssetRecord record = records[index];
                if (string.Equals(record.Definition.EditorAssetGuid, record.AssetGuid, StringComparison.Ordinal))
                    ownerRecords.Add(record);
            }

            if (ownerRecords.Count != 1)
            {
                LogAmbiguousDuplicateGroup(identityId, records);
                ambiguousDuplicateAssetCount += records.Count;
                return;
            }

            IdentityAssetRecord originalRecord = ownerRecords[0];
            for (int index = 0; index < records.Count; index++)
            {
                IdentityAssetRecord copyRecord = records[index];
                if (ReferenceEquals(copyRecord, originalRecord))
                    continue;

                if (!string.Equals(
                        copyRecord.Definition.EditorAssetGuid,
                        originalRecord.AssetGuid,
                        StringComparison.Ordinal))
                {
                    LogAmbiguousDuplicate(copyRecord, identityId);
                    ambiguousDuplicateAssetCount++;
                    continue;
                }

                string generatedIdentityId = CreateUniqueIdentityId(usedIdentityIds);
                copyRecord.Definition.EditorInitializeIdentity(generatedIdentityId, copyRecord.AssetGuid);
                copyRecord.IdentityId = generatedIdentityId;
                EditorUtility.SetDirty(copyRecord.Definition);
                copiedIdentityCount++;
                Debug.Log(
                    $"[NPCIdentityDefinitionAssetProcessor] 已确认 '{copyRecord.Definition.name}' 是 '{originalRecord.Definition.name}' 的复制资产，已分配独立身份。",
                    copyRecord.Definition);
            }
        }

        #endregion

        #region 校验与辅助

        /// <summary>生成未被项目中任何身份字段占用的 GUID 字符串，并登记到占用集合。</summary>
        /// <param name="usedIdentityIds">已经存在或本轮已生成的身份字符串。</param>
        /// <returns>格式为 32 位小写十六进制的唯一 GUID。</returns>
        private static string CreateUniqueIdentityId(HashSet<string> usedIdentityIds)
        {
            string identityId;
            do
            {
                identityId = Guid.NewGuid().ToString("N");
            }
            while (!usedIdentityIds.Add(identityId));

            return identityId;
        }

        /// <summary>报告无法由所属资产 GUID 确认来源的重复身份组，不修改其中任何资产。</summary>
        /// <param name="identityId">重复的身份字符串。</param>
        /// <param name="records">重复身份对应的资产集合。</param>
        private static void LogAmbiguousDuplicateGroup(
            string identityId,
            List<IdentityAssetRecord> records)
        {
            string[] assetNames = new string[records.Count];
            for (int index = 0; index < records.Count; index++)
                assetNames[index] = $"{records[index].Definition.name} ({records[index].AssetPath})";

            Debug.LogError(
                $"[NPCIdentityDefinitionAssetProcessor] 检测到重复 identityId='{identityId}'，但无法唯一确认原资产；未修改资产：{string.Join(", ", assetNames)}。");
        }

        /// <summary>报告单个无法确认复制来源的重复资产。</summary>
        /// <param name="record">来源证据与原件不匹配的资产记录。</param>
        /// <param name="identityId">重复的身份字符串。</param>
        private static void LogAmbiguousDuplicate(IdentityAssetRecord record, string identityId)
        {
            Debug.LogError(
                $"[NPCIdentityDefinitionAssetProcessor] 资产 '{record.Definition.name}' ({record.AssetPath}) 含有重复 identityId='{identityId}'，但 Editor 所属记录不指向可确认原件；未修改该资产。",
                record.Definition);
        }

        #endregion

        #region 嵌套记录

        /// <summary>保存身份资产在本轮扫描中的对象、路径、meta GUID 与身份值。</summary>
        private sealed class IdentityAssetRecord
        {
            /// <summary>创建资产扫描记录。</summary>
            /// <param name="definition">身份 ScriptableObject。</param>
            /// <param name="assetPath">AssetDatabase 中的资产路径。</param>
            /// <param name="assetGuid">当前 meta GUID。</param>
            /// <param name="identityId">资产序列化的专属身份。</param>
            public IdentityAssetRecord(
                NPCIdentityDefinition definition,
                string assetPath,
                string assetGuid,
                string identityId)
            {
                Definition = definition;
                AssetPath = assetPath;
                AssetGuid = assetGuid;
                IdentityId = identityId;
            }

            /// <summary>身份资产对象。</summary>
            public NPCIdentityDefinition Definition { get; }

            /// <summary>身份资产路径，用于诊断扫描目标。</summary>
            public string AssetPath { get; }

            /// <summary>身份资产当前 meta GUID。</summary>
            public string AssetGuid { get; }

            /// <summary>该资产当前序列化的专属身份字符串。</summary>
            public string IdentityId { get; set; }
        }

        #endregion
    }
}
