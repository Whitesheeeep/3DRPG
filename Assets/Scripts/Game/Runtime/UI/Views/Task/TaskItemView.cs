using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Task
{
    /// <summary>显示任务列表中的标题、分类状态、追踪状态和未读状态。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 TaskItem Prefab 显式绑定的 Button、标题、元信息、选中、未读、追踪图层；Button 的 TargetGraphic 必须覆盖整行。")]
    public sealed class TaskItemView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private Button button;
        [SerializeField, Required] private TMP_Text titleText;
        [SerializeField, Required] private TMP_Text detailText;
        // [SerializeField, Required] private GameObject normalMarker;
        [SerializeField, Required] private GameObject selectedMarker;
        [SerializeField, Required] private GameObject unreadMarker;
        [SerializeField, Required] private GameObject trackedMarker;
        private Action clicked;
        #endregion

        #region 生命周期
        /// <summary>绑定 Prefab 上唯一 Button 的点击回调。</summary>
        private void Awake()
        {
            if (button == null || titleText == null || detailText == null ||  selectedMarker == null ||
                unreadMarker == null || trackedMarker == null)
                throw new InvalidOperationException("[TaskItemView] TaskItem Prefab 存在未绑定的必需引用。");
            button.onClick.AddListener(HandleClicked);
        }

        /// <summary>移除点击回调，避免动态列表实例销毁时保留 Controller 引用。</summary>
        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClicked);
            clicked = null;
        }
        #endregion

        #region 展示
        /// <summary>刷新任务条目的可见状态和选择回调。</summary>
        /// <param name="title">任务标题。</param>
        /// <param name="categoryLabel">分类及进度摘要。</param>
        /// <param name="selected">条目是否选中。</param>
        /// <param name="unread">任务是否未读。</param>
        /// <param name="tracked">任务是否正在追踪。</param>
        /// <param name="onClicked">条目点击回调。</param>
        public void Bind(string title, string categoryLabel, bool selected, bool unread, bool tracked, Action onClicked)
        {
            titleText.text = title;
            detailText.text = categoryLabel;
            // normalMarker.SetActive(!selected);
            selectedMarker.SetActive(selected);
            unreadMarker.SetActive(unread);
            trackedMarker.SetActive(tracked);
            clicked = onClicked;
        }

        /// <summary>转发列表条目点击意图。</summary>
        /// <remarks>按钮在 Awake 中只绑定一次；Bind 只替换当前选择回调。</remarks>
        private void HandleClicked() => clicked?.Invoke();
        #endregion
    }
}
