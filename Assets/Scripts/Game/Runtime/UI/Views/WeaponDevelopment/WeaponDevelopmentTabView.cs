using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>武器培养左侧 Tab 的按钮、文案和选中态图像表现。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同一节点上的 Button、Label、SelectedIcon 和 NoSelectIcon。")]
    public sealed class WeaponDevelopmentTabView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Button button;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private Image selectedIcon;
        [SerializeField] private Image noSelectIcon;

        #endregion

        #region 事件

        /// <summary>用户点击该 Tab 时触发。</summary>
        public event Action Clicked;

        #endregion

        #region 生命周期与校验

        /// <summary>校验显式绑定并注册按钮监听。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            button.onClick.AddListener(HandleClicked);
            selectedIcon.raycastTarget = false;
            noSelectIcon.raycastTarget = false;
            SetSelected(false);
        }

        /// <summary>移除按钮监听。</summary>
        private void OnDestroy()
        {
            if (button != null) button.onClick.RemoveListener(HandleClicked);
        }

        /// <summary>校验 Tab 的 Button、文案和两种状态图像。</summary>
        public void ValidateConfiguration()
        {
            if (button == null || labelText == null || selectedIcon == null || noSelectIcon == null)
                throw new InvalidOperationException("[WeaponDevelopmentTabView] Tab 存在未绑定控件。");
        }

        #endregion

        #region 展示

        /// <summary>设置 Tab 文案。</summary>
        /// <param name="label">显示文案。</param>
        public void SetLabel(string label) => labelText.text = label ?? string.Empty;

        /// <summary>切换选中和未选中 Image，重复点击不会在 View 内反转业务状态。</summary>
        /// <param name="selected">是否选中。</param>
        public void SetSelected(bool selected)
        {
            selectedIcon.gameObject.SetActive(selected);
            noSelectIcon.gameObject.SetActive(!selected);
        }

        /// <summary>设置 Tab 是否可交互。</summary>
        /// <param name="interactable">是否可交互。</param>
        public void SetInteractable(bool interactable) => button.interactable = interactable;

        #endregion

        #region 用户意图

        /// <summary>转发按钮点击意图，不修改当前选中状态。</summary>
        private void HandleClicked() => Clicked?.Invoke();

        #endregion
    }
}
