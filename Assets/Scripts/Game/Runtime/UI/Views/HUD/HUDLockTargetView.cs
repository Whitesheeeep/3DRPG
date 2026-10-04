using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>显示锁定目标屏幕标记，只负责位置、显隐和标记图片旋转。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUD Canvas、全屏 Canvas RectTransform、标记 CanvasGroup、外层标记 RectTransform、旋转图片 RectTransform 和 Image；全部由 HUD Prefab 显式绑定。")]
    public sealed class HUDLockTargetView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private Canvas hudCanvas;
        [SerializeField, Required] private RectTransform canvasRectTransform;
        [SerializeField, Required] private CanvasGroup markerCanvasGroup;
        [SerializeField, Required] private RectTransform markerRectTransform;
        [SerializeField, Required] private RectTransform rotatingIconRectTransform;
        [SerializeField, Required] private Image lockIconImage;
        [SerializeField, MinValue(0f)] private float rotationDegreesPerSecond = 90f;

        #endregion

        #region 属性

        /// <summary>获取标记使用的 Canvas。</summary>
        public Canvas HudCanvas => hudCanvas;

        /// <summary>获取全屏 Canvas 的坐标根节点。</summary>
        public RectTransform CanvasRectTransform => canvasRectTransform;

        #endregion

        #region 初始化与显示

        /// <summary>检查静态 Prefab 引用并将运行时标记初始化为隐藏状态。</summary>
        /// <exception cref="System.InvalidOperationException">HUD Prefab 未绑定所需 UI 引用时抛出。</exception>
        public void Initialize()
        {
            if (hudCanvas == null || canvasRectTransform == null || markerCanvasGroup == null ||
                markerRectTransform == null || rotatingIconRectTransform == null || lockIconImage == null)
                throw new System.InvalidOperationException("[HUDLockTargetView] HUD 锁定标记存在未绑定引用。");

            markerCanvasGroup.interactable = false;
            markerCanvasGroup.blocksRaycasts = false;
            Hide();
        }

        /// <summary>将标记放置到 HUD Canvas 局部坐标并显示。</summary>
        /// <param name="screenPosition">游戏摄像机投影得到的像素坐标。</param>
        /// <param name="gameplayCamera">提供摄像机空间投影的当前摄像机。</param>
        /// <returns>屏幕位置成功转换到 Canvas 坐标时返回 true。</returns>
        public bool ShowAtScreenPosition(Vector2 screenPosition, Camera gameplayCamera)
        {
            Camera canvasCamera = hudCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : hudCanvas.worldCamera != null ? hudCanvas.worldCamera : gameplayCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRectTransform, screenPosition, canvasCamera, out Vector2 localPosition))
            {
                Hide();
                return false;
            }

            markerRectTransform.anchoredPosition = localPosition;
            SetVisible(true);
            return true;
        }

        /// <summary>隐藏标记视觉，不销毁或禁用静态 Prefab 节点。</summary>
        public void Hide()
        {
            SetVisible(false);
        }

        #endregion

        #region 旋转与内部状态

        /// <summary>仅在标记显示时按游戏时间顺时针旋转内层图片。</summary>
        private void Update()
        {
            if (markerCanvasGroup.alpha <= 0f || rotationDegreesPerSecond <= 0f)
                return;

            rotatingIconRectTransform.Rotate(0f, 0f, -rotationDegreesPerSecond * Time.deltaTime);
        }

        /// <summary>只有可见状态变化时才写入 CanvasGroup，避免每帧重复修改 UI。</summary>
        /// <param name="visible">锁定图标是否应显示。</param>
        private void SetVisible(bool visible)
        {
            float alpha = visible ? 1f : 0f;
            if (Mathf.Approximately(markerCanvasGroup.alpha, alpha))
                return;

            markerCanvasGroup.alpha = alpha;
            Debug.Log($"[HUDLockTargetView] 锁定标记{(visible ? "显示" : "隐藏")}。", this);
        }

        #endregion
    }
}
