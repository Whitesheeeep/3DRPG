using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 任务窗口的序列化依赖容器，集中保存关闭按钮与任务列表模板引用。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 TaskWindow 根节点上的关闭按钮、任务列表 Content 和 TaskItem Prefab 显式绑定。")]
    public sealed class TaskWindowDataComponent : MonoBehaviour
    {
        #region 依赖字段

        // WindowBase 根据该配置决定窗口遮挡策略和显示过渡；列表模板供后续数据接入复用。
        [SerializeField] private bool isFullWindow = true;
        [SerializeField] private bool doAnimation = true;
        [SerializeField, Required] private Button closeButton;
        [SerializeField, Required] private RectTransform taskListContent;
        [SerializeField, Required] private GameObject taskItemPrefab;

        #endregion

        #region 属性

        /// <summary>获取窗口是否参与全屏窗口层级行为。</summary>
        public bool IsFullWindow => isFullWindow;

        /// <summary>获取窗口是否播放 WindowBase 标准过渡动画。</summary>
        public bool DoAnimation => doAnimation;

        /// <summary>获取显式绑定的关闭按钮。</summary>
        public Button CloseButton => closeButton;

        /// <summary>获取放置任务条目实例的列表 Content。</summary>
        public RectTransform TaskListContent => taskListContent;

        /// <summary>获取可复用的任务条目 Prefab。</summary>
        public GameObject TaskItemPrefab => taskItemPrefab;

        #endregion

        #region 配置校验

        /// <summary>校验任务窗口必需的序列化引用。</summary>
        /// <exception cref="InvalidOperationException">必需的 UI 对象未在 Prefab 中绑定时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (closeButton == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定关闭按钮。");
            if (taskListContent == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定任务列表 Content。");
            if (taskItemPrefab == null)
                throw new InvalidOperationException("[TaskWindowDataComponent] 未绑定 TaskItem Prefab。");
        }

        #endregion
    }
}
