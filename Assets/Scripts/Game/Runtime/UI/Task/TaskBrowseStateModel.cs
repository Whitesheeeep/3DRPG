using RPG.TaskSystemNS;

namespace RPG.Game.UI.Task
{
    /// <summary>保存任务窗口当前选中任务等纯浏览状态，不持有任务进度事实。</summary>
    public sealed class TaskBrowseStateModel
    {
        #region 浏览状态
        /// <summary>获取或设置当前选中的活动任务。</summary>
        public TaskId SelectedTaskId { get; set; }

        /// <summary>获取当前任务分类筛选；窗口关闭再打开时保留该选择。</summary>
        public E_TaskCategoryFilter CategoryFilter { get; private set; } = E_TaskCategoryFilter.All;
        #endregion

        #region 状态操作
        /// <summary>清除窗口的当前选中任务。</summary>
        public void ClearSelection() => SelectedTaskId = default;

        /// <summary>设置分类筛选，选择状态只属于当前任务窗口浏览会话。</summary>
        /// <param name="categoryFilter">新的任务分类筛选。</param>
        public void SelectCategoryFilter(E_TaskCategoryFilter categoryFilter)
        {
            CategoryFilter = categoryFilter;
        }
        #endregion
    }
}
