using System;
using System.Collections.Generic;

namespace RPG.TaskSystemNS
{
    #region 分类选项

    /// <summary>
    /// 描述静态任务分类表中的稳定 ID 与 Inspector 显示名称。
    /// </summary>
    public readonly struct TaskCategoryOption
    {
        private readonly TaskCategoryId id;
        private readonly string displayName;

        /// <summary>
        /// 创建供静态分类表使用的选项。
        /// </summary>
        /// <param name="id">分类稳定 ID。</param>
        /// <param name="displayName">面向编辑器和界面的显示名称。</param>
        internal TaskCategoryOption(TaskCategoryId id, string displayName)
        {
            this.id = id;
            this.displayName = displayName;
        }

        /// <summary>
        /// 获取分类稳定 ID。
        /// </summary>
        public TaskCategoryId Id => id;

        /// <summary>
        /// 获取分类显示名称。
        /// </summary>
        public string DisplayName => displayName;
    }

    #endregion

    #region 静态分类表

    /// <summary>
    /// 集中维护任务系统允许使用的固定分类 ID 和显示名称。
    /// </summary>
    public static class TaskCategoryCatalog
    {
        /// <summary>
        /// 主线任务的稳定分类 ID。
        /// </summary>
        public const string MainIdValue = "main";

        /// <summary>
        /// 支线任务的稳定分类 ID。
        /// </summary>
        public const string SideIdValue = "side";

        private static readonly IReadOnlyList<TaskCategoryOption> categoryOptions = Array.AsReadOnly(
            new[]
            {
                new TaskCategoryOption(new TaskCategoryId(MainIdValue), "主线"),
                new TaskCategoryOption(new TaskCategoryId(SideIdValue), "支线")
            });

        /// <summary>
        /// 获取按 Inspector 显示顺序排列的只读分类选项。
        /// </summary>
        public static IReadOnlyList<TaskCategoryOption> Options => categoryOptions;

        /// <summary>
        /// 判断类型化分类 ID 是否登记在静态表中。
        /// </summary>
        /// <param name="categoryId">待查询分类 ID。</param>
        /// <returns>已登记时返回 true。</returns>
        public static bool IsDefined(TaskCategoryId categoryId)
        {
            return IsDefined(categoryId.Value);
        }

        /// <summary>
        /// 判断字符串分类 ID 是否登记在静态表中。
        /// </summary>
        /// <param name="categoryId">待查询分类 ID。</param>
        /// <returns>已登记时返回 true。</returns>
        public static bool IsDefined(string categoryId)
        {
            return TryGetDisplayName(categoryId, out _);
        }

        /// <summary>
        /// 按类型化 ID 查询分类显示名称。
        /// </summary>
        /// <param name="categoryId">待查询分类 ID。</param>
        /// <param name="displayName">找到时返回显示名称。</param>
        /// <returns>分类存在时返回 true。</returns>
        public static bool TryGetDisplayName(TaskCategoryId categoryId, out string displayName)
        {
            return TryGetDisplayName(categoryId.Value, out displayName);
        }

        /// <summary>
        /// 按字符串 ID 查询分类显示名称。
        /// </summary>
        /// <param name="categoryId">待查询分类 ID。</param>
        /// <param name="displayName">找到时返回显示名称。</param>
        /// <returns>分类存在时返回 true。</returns>
        public static bool TryGetDisplayName(string categoryId, out string displayName)
        {
            for (int index = 0; index < categoryOptions.Count; index++)
            {
                TaskCategoryOption option = categoryOptions[index];
                if (string.Equals(option.Id.Value, categoryId, StringComparison.Ordinal))
                {
                    displayName = option.DisplayName;
                    return true;
                }
            }

            displayName = null;
            return false;
        }
    }

    #endregion
}
