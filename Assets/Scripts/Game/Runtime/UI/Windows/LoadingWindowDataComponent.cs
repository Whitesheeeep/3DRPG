using System;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>保存 LoadingWindow Prefab 的显式视图引用并刷新蒙版进度。</summary>
    [DisallowMultipleComponent]
    [InfoBox("LoadingWindow 的 UIMask 与 UIContent 遵循 TemplateWindow 结构；本组件必须绑定七个灰色元素图标、七个彩色元素图标、进度裁剪 RectTransform 和提示控件。")]
    public sealed class LoadingWindowDataComponent : MonoBehaviour
    {
        #region 窗口配置与依赖字段

        [SerializeField, Tooltip("加载界面以全屏窗口参与 WSFrame 窗口遮挡管理。")]
        private bool isFullWindow = true;
        [SerializeField, Tooltip("加载遮罩不播放普通窗口缩放动画。")]
        private bool doAnimation;
        [SerializeField, Required] private RectTransform progressRow;
        [SerializeField, Required] private RectTransform coloredProgressMask;
        [SerializeField, Required] private Image[] grayscaleElementImages = Array.Empty<Image>();
        [SerializeField, Required] private Image[] coloredElementImages = Array.Empty<Image>();
        [SerializeField, Required] private TextMeshProUGUI progressPercentText;
        [SerializeField, Required] private TextMeshProUGUI loadingTitleText;
        [SerializeField, Required] private TextMeshProUGUI loadingDescriptionText;
        [SerializeField, Required] private GameObject failureRoot;
        [SerializeField, Required] private TextMeshProUGUI failureMessageText;
        [SerializeField, Required] private Button closeButton;
        [SerializeField, Required] private TextMeshProUGUI closeButtonText;

        #endregion

        #region 窗口配置与视图属性

        /// <summary>获取窗口是否参与全屏窗口层级行为。</summary>
        public bool IsFullWindow => isFullWindow;
        /// <summary>获取窗口是否使用 WSFrame 标准过渡动画。</summary>
        public bool DoAnimation => doAnimation;
        /// <summary>获取终态错误提示关闭按钮。</summary>
        public Button CloseButton => closeButton;

        #endregion

        #region 配置校验与进度刷新

        /// <summary>检查 Prefab 中静态绑定的七元素进度行和终态提示控件。</summary>
        /// <exception cref="InvalidOperationException">必需引用缺失或元素图标数量不是七个时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (progressRow == null || coloredProgressMask == null || progressPercentText == null ||
                loadingTitleText == null || loadingDescriptionText == null || failureRoot == null ||
                failureMessageText == null || closeButton == null || closeButtonText == null ||
                grayscaleElementImages == null || grayscaleElementImages.Length != 7 ||
                coloredElementImages == null || coloredElementImages.Length != 7)
                throw new InvalidOperationException("[LoadingWindowDataComponent] Prefab 必须绑定进度行、蒙版、七组灰色和彩色元素图标及提示控件。");

            for (int index = 0; index < 7; index++)
            {
                if (grayscaleElementImages[index] == null || coloredElementImages[index] == null)
                    throw new InvalidOperationException($"[LoadingWindowDataComponent] 第 {index + 1} 个灰色或彩色元素图标未绑定。");
            }
        }

        /// <summary>刷新提示文案、真实进度百分比和彩色元素行的裁剪宽度。</summary>
        /// <param name="progress">场景加载快照的整体加权进度，范围为零到一。</param>
        /// <param name="failureMessage">失败或取消状态的原因。</param>
        /// <param name="isTerminalError">是否显示失败或取消提示及关闭按钮。</param>
        /// <param name="closeLabel">终态关闭按钮文案。</param>
        public void Render(float progress, string failureMessage, bool isTerminalError, string closeLabel)
        {
            float normalizedProgress = Mathf.Clamp01(progress);
            // 只调整裁剪窗口宽度，图标行内的灰色和彩色图案始终保持同一尺寸和位置。
            coloredProgressMask.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                progressRow.rect.width * normalizedProgress);
            progressPercentText.text = $"{Mathf.RoundToInt(normalizedProgress * 100f)}%";
            failureMessageText.text = failureMessage;
            closeButtonText.text = closeLabel;
            failureRoot.SetActive(isTerminalError);
            loadingTitleText.gameObject.SetActive(!isTerminalError);
            loadingDescriptionText.gameObject.SetActive(!isTerminalError);
        }

        #endregion
    }
}
