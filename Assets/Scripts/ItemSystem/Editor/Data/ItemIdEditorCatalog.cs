#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using WS_Modules.EditorExtensions;

namespace RPG.ItemSystem.Editor
{
    /// <summary>缓存 ItemId 下拉框在一次数据库版本中的候选值与显示标签。</summary>
    internal sealed class ItemIdDropdownOptionSet
    {
        #region 缓存数据

        /// <summary>获取按显示顺序排列的序列化值。</summary>
        internal IReadOnlyList<string> Values { get; }

        /// <summary>获取按显示顺序排列的用户可读标签。</summary>
        internal IReadOnlyList<string> Labels { get; }

        /// <summary>获取 IMGUI 可直接复用的标签数组。</summary>
        internal GUIContent[] DisplayOptions { get; }

        /// <summary>获取候选是否来自唯一的可用数据库。</summary>
        internal bool HasDatabase { get; }

        // key：序列化 ItemId；value：该 ItemId 在下拉选项中的索引。
        private readonly Dictionary<string, int> indexByItemIdMap;
        // key：用户看到的唯一显示标签；value：对应的序列化 ItemId。
        private readonly Dictionary<string, string> itemIdByLabelMap;

        #endregion

        #region 构造与查询

        /// <summary>为一组已准备好的下拉选项建立常驻索引。</summary>
        /// <param name="values">与标签一一对应的序列化值。</param>
        /// <param name="labels">与序列化值一一对应的显示标签。</param>
        internal ItemIdDropdownOptionSet(List<string> values, List<string> labels, bool hasDatabase)
        {
            Values = values;
            Labels = labels;
            HasDatabase = hasDatabase;
            DisplayOptions = new GUIContent[labels.Count];
            indexByItemIdMap = new Dictionary<string, int>(values.Count, StringComparer.Ordinal);
            itemIdByLabelMap = new Dictionary<string, string>(labels.Count, StringComparer.Ordinal);

            for (int index = 0; index < labels.Count; index++)
            {
                DisplayOptions[index] = new GUIContent(labels[index]);
                string value = values[index];
                if (!string.IsNullOrEmpty(value)) indexByItemIdMap.TryAdd(value, index);
                itemIdByLabelMap.TryAdd(labels[index], value);
            }
        }

        /// <summary>通过字典索引查找当前值，无候选时选择“无”项。</summary>
        /// <param name="itemId">当前序列化 ItemId。</param>
        /// <returns>当前值的候选索引；未知值回退到首项。</returns>
        internal int FindIndex(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && indexByItemIdMap.TryGetValue(itemId, out int index)
                ? index
                : 0;
        }

        /// <summary>根据下拉标签读取其对应的序列化值。</summary>
        /// <param name="label">用户选择的显示标签。</param>
        /// <param name="itemId">标签对应的 ItemId；“无”项对应空字符串。</param>
        /// <returns>标签存在时返回 true。</returns>
        internal bool TryGetItemId(string label, out string itemId)
        {
            return itemIdByLabelMap.TryGetValue(label, out itemId);
        }

        #endregion
    }

    /// <summary>事件驱动地解析 ItemDatabase 并缓存 ItemId 下拉候选，避免 Inspector 重绘扫描资产。</summary>
    [InitializeOnLoad]
    internal static class ItemIdEditorCatalog
    {
        #region 缓存状态

        // 候选顺序沿用 ItemDatabase.Definitions；重复 ID 保留最先出现的定义，与原 Drawer 行为一致。
        private static readonly List<string> itemIdValueList = new();
        private static readonly List<string> displayLabelList = new();
        // key：稳定 ItemId；value：首个定义在候选表中的索引。
        private static readonly Dictionary<string, int> indexByItemIdMap = new(StringComparer.Ordinal);
        // key：未出现在数据库中的历史 ItemId；value：为该引用增加 Missing 项后的完整候选集。
        private static readonly Dictionary<string, ItemIdDropdownOptionSet> optionSetByMissingItemIdMap = new(StringComparer.Ordinal);
        private static ItemIdDropdownOptionSet baseOptionSet;
        private static ItemDatabase sourceDatabase;
        private static bool cacheDirty = true;
        private static bool notificationScheduled;

        #endregion

        #region 事件

        /// <summary>通知已挂载的 UI Toolkit 字段重新读取候选项。</summary>
        internal static event Action Changed;

        #endregion

        #region 初始化与查询

        /// <summary>订阅项目资源和序列化对象变化，使数据库扫描只在失效后执行。</summary>
        static ItemIdEditorCatalog()
        {
            EditorApplication.projectChanged += OnProjectChanged;
            Undo.undoRedoEvent += OnUndoRedo;
            Undo.postprocessModifications += OnPostprocessModifications;
            Debug.Log("[ItemIdEditorCatalog] 已连接项目资源与序列化修改监听。");
        }

        /// <summary>获取当前序列化值所需的缓存候选集。</summary>
        /// <param name="currentItemId">字段当前保存的 ItemId。</param>
        /// <returns>包含当前有效或历史无效引用的候选集。</returns>
        internal static ItemIdDropdownOptionSet GetOptions(string currentItemId)
        {
            EnsureCache();
            if (string.IsNullOrEmpty(currentItemId) || indexByItemIdMap.ContainsKey(currentItemId)) return baseOptionSet;
            if (optionSetByMissingItemIdMap.TryGetValue(currentItemId, out ItemIdDropdownOptionSet cachedOptionSet))
                return cachedOptionSet;

            optionSetByMissingItemIdMap.Add(currentItemId, BuildMissingReferenceOptionSet(currentItemId));
            return optionSetByMissingItemIdMap[currentItemId];
        }

        /// <summary>标记候选缓存失效，并合并同一编辑器周期内的刷新通知。</summary>
        internal static void Invalidate()
        {
            cacheDirty = true;
            if (notificationScheduled) return;

            notificationScheduled = true;
            EditorApplication.delayCall += NotifyChanged;
        }

        #endregion

        #region 缓存建立

        /// <summary>只在首次读取或收到明确变更通知后解析数据库并建立候选项。</summary>
        private static void EnsureCache()
        {
            if (!cacheDirty) return;
            RebuildCache();
        }

        /// <summary>从唯一数据库构建共享候选值、标签和 ItemId 索引。</summary>
        private static void RebuildCache()
        {
            cacheDirty = false;
            itemIdValueList.Clear();
            displayLabelList.Clear();
            indexByItemIdMap.Clear();
            optionSetByMissingItemIdMap.Clear();
            sourceDatabase = ItemConfigEditorSession.ResolveDatabase();

            itemIdValueList.Add(string.Empty);
            displayLabelList.Add(sourceDatabase == null ? "未找到唯一 ItemDatabase" : "无");
            if (sourceDatabase != null)
            {
                IReadOnlyList<ItemDefinition> definitions = sourceDatabase.Definitions;
                for (int index = 0; index < definitions.Count; index++)
                {
                    ItemDefinition definition = definitions[index];
                    if (definition == null) continue;
                    string itemId = definition.ItemId.Value;
                    if (string.IsNullOrEmpty(itemId) || indexByItemIdMap.ContainsKey(itemId)) continue;

                    indexByItemIdMap.Add(itemId, itemIdValueList.Count);
                    itemIdValueList.Add(itemId);
                    displayLabelList.Add(ItemId.TryCreate(itemId, out _)
                        ? ConfigEditorStableIdUtility.FormatReferenceLabel(itemId, definition.DisplayName)
                        : ConfigEditorStableIdUtility.FormatInvalidReferenceLabel(itemId));
                }
            }

            baseOptionSet = new ItemIdDropdownOptionSet(
                new List<string>(itemIdValueList),
                new List<string>(displayLabelList),
                sourceDatabase != null);
            Debug.Log(
                $"[ItemIdEditorCatalog] 已重建物品引用候选，database={(sourceDatabase == null ? "none" : sourceDatabase.name)}，candidateCount={itemIdValueList.Count - 1}。");
        }

        /// <summary>为单个历史无效 ItemId 缓存 Missing 项，不改变共享候选表。</summary>
        /// <param name="missingItemId">数据库中不存在的序列化值。</param>
        /// <returns>包含该无效值的候选集。</returns>
        private static ItemIdDropdownOptionSet BuildMissingReferenceOptionSet(string missingItemId)
        {
            List<string> values = new(baseOptionSet.Values.Count + 1);
            List<string> labels = new(baseOptionSet.Labels.Count + 1);
            values.Add(baseOptionSet.Values[0]);
            labels.Add(baseOptionSet.HasDatabase ? baseOptionSet.Labels[0] : "无");
            values.Add(missingItemId);
            labels.Add(ConfigEditorStableIdUtility.FormatInvalidReferenceLabel(missingItemId));
            for (int index = 1; index < baseOptionSet.Values.Count; index++)
            {
                values.Add(baseOptionSet.Values[index]);
                labels.Add(baseOptionSet.Labels[index]);
            }

            return new ItemIdDropdownOptionSet(values, labels, baseOptionSet.HasDatabase);
        }

        /// <summary>延迟重建候选并通知 UI Toolkit 和 IMGUI Inspector 刷新。</summary>
        private static void NotifyChanged()
        {
            notificationScheduled = false;
            EnsureCache();
            Changed?.Invoke();
            InternalEditorUtility.RepaintAllViews();
        }

        #endregion

        #region 变更监听

        /// <summary>项目资产增删、移动或导入后使候选缓存失效。</summary>
        private static void OnProjectChanged() => Invalidate();

        /// <summary>Undo 或 Redo 后使候选缓存失效。</summary>
        /// <param name="undoRedoInfo">Unity 提供的 Undo/Redo 分组信息。</param>
        private static void OnUndoRedo(in UndoRedoInfo undoRedoInfo) => Invalidate();

        /// <summary>数据库或物品定义的序列化修改应用时使候选缓存失效。</summary>
        /// <param name="modifications">Unity 即将应用的序列化修改。</param>
        /// <returns>保持不变的修改列表。</returns>
        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            for (int index = 0; index < modifications.Length; index++)
            {
                UnityEngine.Object modifiedObject = modifications[index].currentValue?.target;
                if (modifiedObject is ItemDatabase || modifiedObject is ItemDefinition)
                {
                    Invalidate();
                    break;
                }
            }

            return modifications;
        }

        #endregion
    }
}
#endif
