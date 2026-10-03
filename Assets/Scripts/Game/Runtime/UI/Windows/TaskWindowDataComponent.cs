using System;
using System.Collections.Generic;
using RPG.Game.UI.Task;
using RPG.Game.UI.Views.Task;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>保存 TaskWindow Prefab 的窗口、分类列表和详情 View 显式引用。</summary>
    [DisallowMultipleComponent]
    [InfoBox("窗口根节点绑定分类列表和三个页签；任务标题、目标、奖励与追踪领奖操作由 TaskDetailsPanelView 管理。")]
    public sealed class TaskWindowDataComponent : MonoBehaviour
    {
        #region 配置与依赖字段
        [SerializeField] private bool isFullWindow = true;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private Button closeButton;
        [SerializeField, Required] private RectTransform mainQuestRoot;
        [SerializeField, Required] private RectTransform sideQuestRoot;
        [SerializeField, Required] private GameObject taskItemPrefab;
        [SerializeField, Required] private TaskDetailsPanelView detailsPanelView;
        [SerializeField, Required] private TaskCategoryTabView[] categoryTabViews = Array.Empty<TaskCategoryTabView>();
        #endregion

        #region 属性
        /// <summary>获取窗口是否参与全屏窗口层级行为。</summary>
        public bool IsFullWindow => isFullWindow;
        /// <summary>获取窗口是否播放 WindowBase 标准过渡动画。</summary>
        public bool DoAnimation => doAnimation;
        /// <summary>获取关闭按钮。</summary>
        public Button CloseButton => closeButton;
        /// <summary>获取主线活动任务列表根节点。</summary>
        public RectTransform MainQuestRoot => mainQuestRoot;
        /// <summary>获取支线活动任务列表根节点。</summary>
        public RectTransform SideQuestRoot => sideQuestRoot;
        /// <summary>获取用于动态列表条目的 TaskItem Prefab。</summary>
        public GameObject TaskItemPrefab => taskItemPrefab;
        /// <summary>获取挂在 TaskDetailsPanel 上的详情 View。</summary>
        public TaskDetailsPanelView DetailsPanelView => detailsPanelView;
        /// <summary>获取全部、主线与支线三个分类页签。</summary>
        public TaskCategoryTabView[] CategoryTabViews => categoryTabViews;
        #endregion

        #region 配置校验
        /// <summary>校验窗口列表、详情 View 及唯一的三个分类页签。</summary>
        /// <exception cref="InvalidOperationException">Prefab 缺少引用或页签配置重复时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (closeButton == null || mainQuestRoot == null || sideQuestRoot == null || taskItemPrefab == null ||
                detailsPanelView == null || categoryTabViews == null || categoryTabViews.Length != 3)
                throw new InvalidOperationException("[TaskWindowDataComponent] TaskWindow 缺少必需的窗口、详情、列表或页签引用。");

            detailsPanelView.ValidateConfiguration();
            var filters = new HashSet<E_TaskCategoryFilter>();
            for (int index = 0; index < categoryTabViews.Length; index++)
            {
                TaskCategoryTabView tabView = categoryTabViews[index];
                if (tabView == null) throw new InvalidOperationException("[TaskWindowDataComponent] 分类页签列表含有空引用。");
                tabView.ValidateConfiguration();
                if (!filters.Add(tabView.CategoryFilter))
                    throw new InvalidOperationException($"[TaskWindowDataComponent] 分类页签重复配置：{tabView.CategoryFilter}。");
            }

            if (!filters.Contains(E_TaskCategoryFilter.All) || !filters.Contains(E_TaskCategoryFilter.Main) ||
                !filters.Contains(E_TaskCategoryFilter.Side))
                throw new InvalidOperationException("[TaskWindowDataComponent] 必须分别配置全部、主线和支线页签。");
            if (taskItemPrefab.GetComponent<TaskItemView>() == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] TaskItem Prefab 缺少 TaskItemView。");
        }
        #endregion
    }
}
