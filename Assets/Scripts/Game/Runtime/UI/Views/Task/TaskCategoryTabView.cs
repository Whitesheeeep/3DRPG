using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using RPG.Game.UI.Task;

namespace RPG.Game.UI.Views.Task
{
    /// <summary>显示任务分类页签的选中状态并转发分类选择。</summary>
    [DisallowMultipleComponent]
    [InfoBox("挂在任务 TabBar 子物体根节点；需配置 Button、图标、选中背景和选中下划线。")]
    public sealed class TaskCategoryTabView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField] private E_TaskCategoryFilter categoryFilter;
        [SerializeField, Required] private Button button;
        [SerializeField, Required] private Image icon;
        [SerializeField, Required] private GameObject selectedHighlight;
        [SerializeField, Required] private GameObject selectedUnderline;
        private UnityAction clickAction;
        private Action<E_TaskCategoryFilter> categoryRequested;
        #endregion

        #region 属性
        /// <summary>获取该页签表示的任务分类。</summary>
        public E_TaskCategoryFilter CategoryFilter => categoryFilter;
        #endregion

        #region 生命周期与校验
        /// <summary>校验页签 Prefab 的显式控件引用。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            clickAction = HandleClicked;
        }

        /// <summary>对称移除按钮监听，避免页签销毁后保留窗口控制器。</summary>
        private void OnDestroy()
        {
            if (button != null && clickAction != null) button.onClick.RemoveListener(clickAction);
            categoryRequested = null;
        }

        /// <summary>检查分类页签所需的按钮、图标和选中状态节点。</summary>
        public void ValidateConfiguration()
        {
            if (button == null || icon == null || selectedHighlight == null || selectedUnderline == null)
                throw new InvalidOperationException("[TaskCategoryTabView] 分类页签存在未绑定的必需引用。");
        }
        #endregion

        #region 交互与展示
        /// <summary>绑定分类选择回调，并确保按钮只注册一次。</summary>
        /// <param name="onCategoryRequested">用户选择该分类时的回调。</param>
        public void Bind(Action<E_TaskCategoryFilter> onCategoryRequested)
        {
            if (onCategoryRequested == null) throw new ArgumentNullException(nameof(onCategoryRequested));
            categoryRequested = onCategoryRequested;
            if (clickAction == null) clickAction = HandleClicked;
            button.onClick.RemoveListener(clickAction);
            button.onClick.AddListener(clickAction);
        }

        /// <summary>解除分类选择回调。</summary>
        public void Unbind()
        {
            if (button != null && clickAction != null) button.onClick.RemoveListener(clickAction);
            categoryRequested = null;
        }

        /// <summary>切换该页签的高亮背景与下划线。</summary>
        /// <param name="selected">该页签是否为当前分类。</param>
        public void SetSelected(bool selected)
        {
            selectedHighlight.SetActive(selected);
            selectedUnderline.SetActive(selected);
        }

        /// <summary>将按键输入转换成当前页签的分类选择意图。</summary>
        private void HandleClicked() => categoryRequested?.Invoke(categoryFilter);
        #endregion
    }
}
