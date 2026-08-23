using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>ChoiceWindow 使用的可复用选项行，只负责文本、按钮和高亮显示。</summary>
    public sealed class OptionChoice : MonoBehaviour
    {
        #region 组件引用与状态

        // 行 View 依赖 Button 和 TMP 文本组件完成显示与点击转发。
        [SerializeField] private Button optionButton;
        [SerializeField] private TMP_Text optionText;

        private Action<int> clickHandler;
        private int optionIndex;
        private Color normalColor;
        private Color selectedColor;
        private bool colorsCached;

        #endregion

        #region 生命周期

        /// <summary>缓存 Button 原始颜色，供选中状态切换时恢复。</summary>
        private void Awake()
        {
            if (optionButton == null) optionButton = GetComponent<Button>();
            if (optionText == null) optionText = GetComponentInChildren<TMP_Text>();

            if (optionButton != null)
            {
                ColorBlock colors = optionButton.colors;
                normalColor = colors.normalColor;
                selectedColor = colors.selectedColor;
                colorsCached = true;
            }
        }

        /// <summary>销毁前移除本行注册的点击监听。</summary>
        private void OnDestroy()
        {
            if (optionButton != null) optionButton.onClick.RemoveListener(HandleButtonClicked);
            clickHandler = null;
        }

        #endregion

        #region 绑定与刷新

        /// <summary>绑定窗口级点击回调；该监听在行复用期间保持不变。</summary>
        /// <param name="handler">接收行索引的点击回调。</param>
        public void Initialize(Action<int> handler)
        {
            if (optionButton == null || optionText == null)
                throw new InvalidOperationException("OptionChoice 必须绑定 Button 和 TMP_Text。");

            optionButton.onClick.RemoveListener(HandleButtonClicked);
            clickHandler = handler;
            optionButton.onClick.AddListener(HandleButtonClicked);
        }

        /// <summary>设置当前行索引、显示文本和选中状态。</summary>
        /// <param name="index">当前行在 ChoiceWindow 列表中的索引。</param>
        /// <param name="text">待显示的选项名称。</param>
        /// <param name="highlighted">是否显示选中高亮。</param>
        public void SetOption(int index, string text, bool highlighted)
        {
            optionIndex = index;
            optionText.text = text ?? string.Empty;
            SetOptionHighlight(highlighted);
        }

        /// <summary>设置当前行的选中视觉状态。</summary>
        /// <param name="highlighted">是否显示选中高亮。</param>
        public void SetOptionHighlight(bool highlighted)
        {
            if (optionButton == null) return;
            if (!colorsCached)
            {
                ColorBlock initialColors = optionButton.colors;
                normalColor = initialColors.normalColor;
                selectedColor = initialColors.selectedColor;
                colorsCached = true;
            }

            ColorBlock colors = optionButton.colors;
            colors.normalColor = highlighted ? colors.highlightedColor : normalColor;
            colors.selectedColor = highlighted ? colors.highlightedColor : selectedColor;
            optionButton.colors = colors;
        }

        /// <summary>清除当前行文本并恢复未选中颜色。</summary>
        public void ClearOption()
        {
            if (optionText != null) optionText.text = string.Empty;
            SetOptionHighlight(false);
        }

        #endregion

        #region 点击事件

        /// <summary>响应 Button 点击并把当前行索引交给 ChoiceWindow。</summary>
        private void HandleButtonClicked() => clickHandler?.Invoke(optionIndex);

        #endregion
    }
}
