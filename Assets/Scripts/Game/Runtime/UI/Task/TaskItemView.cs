using System;
using RPG.TaskSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace RPG.Game.UI.Task
{
    /// <summary>
    /// 显示一个任务列表 Prefab 实例，并将点击意图回传给窗口控制器。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 TaskItem Prefab 上显式绑定的 Button、图标、标题及普通、选中、未读和追踪节点。")]
    public sealed class TaskItemView : MonoBehaviour
    {
        #region 依赖字段

        // 依赖字段：所有引用均在 TaskItem.prefab 上配置；本 View 不按节点名称搜索。
        [SerializeField, Required] private Button selectButton;
        [SerializeField, Required] private Image categoryIcon;
        [SerializeField, Required] private TMP_Text titleText;
        [SerializeField, Required] private TMP_Text distanceText;
        [SerializeField, Required] private GameObject normalState;
        [SerializeField, Required] private GameObject selectedState;
        [SerializeField, Required] private GameObject unreadState;
        [SerializeField, Required] private GameObject trackedMarker;

        #endregion

        #region 状态

        private TaskId taskId;
        private Action<TaskId> selectionRequested;
        private UnityAction buttonListener;

        #endregion

        #region 生命周期与绑定

        /// <summary>
        /// 校验 TaskItem Prefab 的序列化引用。
        /// </summary>
        /// <exception cref="InvalidOperationException">Prefab 没有配置必要控件时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (selectButton == null || categoryIcon == null || titleText == null || distanceText == null ||
                normalState == null || selectedState == null || unreadState == null || trackedMarker == null)
                throw new InvalidOperationException("[TaskItemView] TaskItem Prefab 的控件绑定不完整。");
        }

        /// <summary>
        /// 为动态条目绑定任务 ID 和单条点击回调。
        /// </summary>
        /// <param name="id">对应任务的稳定 ID。</param>
        /// <param name="onSelectionRequested">条目被点击时执行的窗口意图回调。</param>
        /// <exception cref="ArgumentException">任务 ID 无效时抛出。</exception>
        public void Initialize(TaskId id, Action<TaskId> onSelectionRequested)
        {
            ValidateConfiguration();
            if (!id.IsValid)
                throw new ArgumentException("任务条目必须绑定有效 TaskId。", nameof(id));

            UnbindSelection();
            taskId = id;
            selectionRequested = onSelectionRequested;
            buttonListener = HandleClicked;
            selectButton.onClick.AddListener(buttonListener);
            distanceText.gameObject.SetActive(false);
        }

        /// <summary>
        /// 显示该任务的标题、图标和互相独立的状态节点。
        /// </summary>
        /// <param name="task">任务展示数据。</param>
        /// <param name="isSelected">该任务是否为当前详情选择。</param>
        /// <param name="isTracked">该任务是否正在追踪。</param>
        public void Render(TaskWindowTaskViewData task, bool isSelected, bool isTracked)
        {
            titleText.text = task.Title;
            if (task.CategoryIcon != null)
                categoryIcon.sprite = task.CategoryIcon;

            // 未读条独立叠加，选中背景仍可见；普通态仅在两种强调状态都关闭时显示。
            normalState.SetActive(!isSelected && !task.IsUnread);
            selectedState.SetActive(isSelected);
            unreadState.SetActive(task.IsUnread);
            trackedMarker.SetActive(isTracked);
        }

        /// <summary>
        /// 销毁前注销该条目的 Button 回调，避免控制器被动态实例保留。
        /// </summary>
        public void UnbindSelection()
        {
            if (buttonListener != null)
                selectButton.onClick.RemoveListener(buttonListener);

            buttonListener = null;
            selectionRequested = null;
        }

        /// <summary>条目被销毁时移除 Button 回调，释放对控制器的委托引用。</summary>
        private void OnDestroy()
        {
            UnbindSelection();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 将点击任务 ID 转发给当前窗口控制器。
        /// </summary>
        /// <summary>把当前条目对应的稳定任务 ID 传回窗口控制器。</summary>
        private void HandleClicked()
        {
            selectionRequested?.Invoke(taskId);
        }

        #endregion
    }
}
