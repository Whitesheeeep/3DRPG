#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WS_Modules;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>保存任务编辑器的后缀、编号、引用候选来源和资产目录设置。</summary>
    [FilePath("ProjectSettings/TaskConfigEditorSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TaskConfigEditorSettings : ScriptableSingleton<TaskConfigEditorSettings>
    {
        #region 字段

        internal const string DefaultDefinitionFolder = "Assets/Scripts/TaskSystem/Runtime/Config/Assets/Definitions";

        [SerializeField] private List<TaskCategorySuffixSettings> suffixesByCategory = new();
        [SerializeField] private List<TaskCategorySequenceSettings> sequencesByCategory = new();
        [SerializeField, WSFolderPath] private string definitionFolder = DefaultDefinitionFolder;
        [SerializeField] private TaskDatabase taskIdSourceDatabase;

        #endregion

        #region 属性

        /// <summary>获取新任务资产保存目录。</summary>
        internal string DefinitionFolder => NormalizeFolder(definitionFolder);

        /// <summary>获取为 TaskId 引用下拉框明确配置的候选数据库。</summary>
        internal TaskDatabase TaskIdSourceDatabase => taskIdSourceDatabase;

        #endregion

        #region 后缀配置

        /// <summary>获取指定分类已保存的后缀配置。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <returns>后缀配置只读列表。</returns>
        internal IReadOnlyList<TaskIdSuffixSettings> GetSuffixes(string categoryId)
        {
            return GetOrCreateCategory(categoryId).Suffixes;
        }

        /// <summary>保存分类的后缀配置。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <param name="suffixId">稳定后缀文本。</param>
        /// <param name="displayName">编辑器显示说明。</param>
        /// <exception cref="ArgumentException">后缀文本无效或同分类重复时抛出。</exception>
        internal void AddSuffix(string categoryId, string suffixId, string displayName)
        {
            TaskCategorySuffixSettings category = GetOrCreateCategory(categoryId);
            string normalizedId = NormalizeSuffix(suffixId);
            if (category.Suffixes.Exists(item => item.SuffixId == normalizedId))
            {
                throw new ArgumentException($"分类 {categoryId} 已有后缀 {normalizedId}。", nameof(suffixId));
            }

            category.Suffixes.Add(new TaskIdSuffixSettings(normalizedId, displayName));
            Save(true);
        }

        /// <summary>更新已保存后缀的标识和显示说明。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <param name="oldSuffixId">当前稳定后缀。</param>
        /// <param name="newSuffixId">新的稳定后缀。</param>
        /// <param name="displayName">新的编辑器显示说明。</param>
        /// <exception cref="ArgumentException">后缀不存在、格式无效或新后缀重复时抛出。</exception>
        internal void UpdateSuffix(string categoryId, string oldSuffixId, string newSuffixId, string displayName)
        {
            TaskCategorySuffixSettings category = GetOrCreateCategory(categoryId);
            TaskIdSuffixSettings suffix = category.Suffixes.Find(item => item.SuffixId == oldSuffixId);
            if (suffix == null)
            {
                throw new ArgumentException($"分类 {categoryId} 不包含后缀 {oldSuffixId}。", nameof(oldSuffixId));
            }

            string normalizedId = NormalizeSuffix(newSuffixId);
            if (category.Suffixes.Exists(item => item.SuffixId == normalizedId && item != suffix))
            {
                throw new ArgumentException($"分类 {categoryId} 已有后缀 {normalizedId}。", nameof(newSuffixId));
            }

            suffix.Set(normalizedId, displayName);
            Save(true);
        }

        /// <summary>删除一个后缀选项，不修改已有任务资产中的 TaskId。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <param name="suffixId">待删除的稳定后缀。</param>
        internal void RemoveSuffix(string categoryId, string suffixId)
        {
            TaskCategorySuffixSettings category = GetOrCreateCategory(categoryId);
            category.Suffixes.RemoveAll(item => item.SuffixId == suffixId);
            Save(true);
        }

        #endregion

        #region TaskId 编号

        /// <summary>保留分类中下一个不小于指定值的编号，并返回已保留编号。</summary>
        /// <param name="categoryId">任务分类稳定标识。</param>
        /// <param name="minimumNumber">根据项目现有资产计算的最小可用编号。</param>
        /// <returns>本次分配的编号。</returns>
        internal int ReserveNextNumber(string categoryId, int minimumNumber)
        {
            TaskCategorySequenceSettings sequence = GetOrCreateSequence(categoryId);
            int allocated = Math.Max(sequence.NextNumber, minimumNumber);
            sequence.NextNumber = checked(allocated + 1);
            Save(true);
            return allocated;
        }

        #endregion

        #region 资产目录

        /// <summary>设置新任务和复制资产的目标目录。</summary>
        /// <param name="folder">项目 Assets 下的目录。</param>
        /// <exception cref="ArgumentException">目录不位于 Assets 下时抛出。</exception>
        internal void SetDefinitionFolder(string folder)
        {
            string normalized = NormalizeFolder(folder);
            if ((normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal)) ||
                Array.Exists(normalized.Split('/'), segment => segment == ".." || segment == "."))
            {
                throw new ArgumentException("任务定义目录必须位于 Assets/ 下，且不能通过相对路径离开项目资源目录。", nameof(folder));
            }

            definitionFolder = normalized;
            Save(true);
        }

        #endregion

        #region TaskId 候选来源

        /// <summary>保存 TaskId 引用下拉框的候选数据库。</summary>
        /// <param name="database">被明确指定为任务 ID 候选来源的数据库。</param>
        internal void SetTaskIdSourceDatabase(TaskDatabase database)
        {
            if (taskIdSourceDatabase == database) return;

            taskIdSourceDatabase = database;
            Save(true);
            Debug.Log($"[TaskConfigEditorSettings] 已保存 TaskId 候选数据库：{(database == null ? "未设置" : database.name)}。");
        }

        #endregion

        #region 校验与辅助

        /// <summary>确保当前分类的持久化设置容器存在。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <returns>分类设置对象。</returns>
        private TaskCategorySuffixSettings GetOrCreateCategory(string categoryId)
        {
            TaskCategorySuffixSettings category = suffixesByCategory.Find(item => item.CategoryId == categoryId);
            if (category != null) return category;
            category = new TaskCategorySuffixSettings(categoryId);
            suffixesByCategory.Add(category);
            Save(true);
            return category;
        }

        /// <summary>确保当前分类的编号计数器存在。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        /// <returns>分类编号设置对象。</returns>
        private TaskCategorySequenceSettings GetOrCreateSequence(string categoryId)
        {
            TaskCategorySequenceSettings sequence = sequencesByCategory.Find(item => item.CategoryId == categoryId);
            if (sequence != null) return sequence;
            sequence = new TaskCategorySequenceSettings(categoryId);
            sequencesByCategory.Add(sequence);
            return sequence;
        }

        /// <summary>规范化项目资产目录并提供首次使用的默认目录。</summary>
        /// <param name="folder">待规范化目录。</param>
        /// <returns>Assets 下使用正斜线的目录。</returns>
        private static string NormalizeFolder(string folder)
        {
            string normalized = string.IsNullOrWhiteSpace(folder) ? DefaultDefinitionFolder : folder.Trim().Replace('\\', '/').TrimEnd('/');
            return normalized;
        }

        /// <summary>校验并规范化稳定后缀。</summary>
        /// <param name="suffixId">用户输入的后缀。</param>
        /// <returns>小写、空白以下划线连接的后缀。</returns>
        private static string NormalizeSuffix(string suffixId)
        {
            if (string.IsNullOrWhiteSpace(suffixId)) throw new ArgumentException("后缀不能为空。", nameof(suffixId));
            for (int index = 0; index < suffixId.Length; index++)
                if (char.IsControl(suffixId[index])) throw new ArgumentException("后缀不能包含控制字符。", nameof(suffixId));

            string normalized = string.Join("_", suffixId.Trim().ToLowerInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (normalized.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                throw new ArgumentException("后缀不能包含斜线。", nameof(suffixId));
            }

            return normalized;
        }

        #endregion
    }

    /// <summary>保存单个 TaskId 后缀的稳定标识与编辑器显示说明。</summary>
    [Serializable]
    internal sealed class TaskIdSuffixSettings
    {
        [SerializeField] private string suffixId;
        [SerializeField] private string displayName;

        /// <summary>创建 Unity 可序列化的后缀配置。</summary>
        public TaskIdSuffixSettings() { }

        /// <summary>创建指定稳定标识和显示说明的后缀配置。</summary>
        /// <param name="suffixId">稳定后缀文本。</param>
        /// <param name="displayName">编辑器显示说明。</param>
        public TaskIdSuffixSettings(string suffixId, string displayName)
        {
            this.suffixId = suffixId;
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? suffixId : displayName.Trim();
        }

        /// <summary>获取稳定后缀文本。</summary>
        public string SuffixId => suffixId ?? string.Empty;

        /// <summary>获取面向用户的后缀说明。</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? SuffixId : displayName;

        /// <summary>更新此配置，保留列表位置。</summary>
        /// <param name="newSuffixId">新的稳定文本。</param>
        /// <param name="newDisplayName">新的显示说明。</param>
        public void Set(string newSuffixId, string newDisplayName)
        {
            suffixId = newSuffixId;
            displayName = string.IsNullOrWhiteSpace(newDisplayName) ? newSuffixId : newDisplayName.Trim();
        }
    }

    /// <summary>保存一个任务分类对应的后缀选项。</summary>
    [Serializable]
    internal sealed class TaskCategorySuffixSettings
    {
        [SerializeField] private string categoryId;
        [SerializeField] private List<TaskIdSuffixSettings> suffixes = new();

        /// <summary>创建 Unity 可序列化的空分类后缀配置。</summary>
        public TaskCategorySuffixSettings() { }

        /// <summary>创建指定分类的后缀配置。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        public TaskCategorySuffixSettings(string categoryId) { this.categoryId = categoryId; }

        /// <summary>获取分类稳定标识。</summary>
        public string CategoryId => categoryId ?? string.Empty;

        /// <summary>获取分类的后缀选项。</summary>
        public List<TaskIdSuffixSettings> Suffixes => suffixes ??= new List<TaskIdSuffixSettings>();
    }

    /// <summary>保存一个任务分类的下一个 TaskId 编号。</summary>
    [Serializable]
    internal sealed class TaskCategorySequenceSettings
    {
        [SerializeField] private string categoryId;
        [SerializeField] private int nextNumber = 1;

        /// <summary>创建 Unity 可序列化的空分类计数器。</summary>
        public TaskCategorySequenceSettings() { }

        /// <summary>创建从编号一开始的分类计数器。</summary>
        /// <param name="categoryId">分类稳定标识。</param>
        public TaskCategorySequenceSettings(string categoryId) { this.categoryId = categoryId; }

        /// <summary>获取分类稳定标识。</summary>
        public string CategoryId => categoryId ?? string.Empty;

        /// <summary>获取或设置下一个尚未分配的编号。</summary>
        public int NextNumber { get => Math.Max(nextNumber, 1); set => nextNumber = Math.Max(value, 1); }
    }
}
#endif
