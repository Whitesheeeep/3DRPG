using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>把当前测试或玩法导航目标投影为 HUD 屏幕标记、方向箭头和距离。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 HUD Canvas、全屏 Canvas RectTransform、标记 CanvasGroup、菱形图标、方向箭头和距离 TMP；所有引用由 HUD Prefab 显式绑定。")]
    public sealed class HUDTaskWorldMarkerView : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private Canvas hudCanvas;
        [SerializeField, Required] private RectTransform canvasRectTransform;
        [SerializeField, Required] private CanvasGroup markerCanvasGroup;
        [SerializeField, Required] private RectTransform markerRectTransform;
        [SerializeField, Required] private Image questMarkImage;
        [SerializeField, Required] private Image directionArrowImage;
        [SerializeField, Required] private TMP_Text distanceText;
        [SerializeField, Required] private Sprite questMarkSprite;
        [SerializeField, MinValue(8f)] private float safeAreaInset = 24f;
        private bool markerVisible;
        private bool hasProjection;
        private bool lastProjectionWasOnScreen;
        private int lastDisplayedDistance = -1;
        #endregion

        #region 初始化与投影
        /// <summary>校验 Prefab 投影目标并初始化为隐藏状态。</summary>
        /// <exception cref="System.InvalidOperationException">Prefab 缺少投影引用时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (hudCanvas == null || canvasRectTransform == null || markerCanvasGroup == null ||
                markerRectTransform == null || questMarkImage == null || directionArrowImage == null ||
                distanceText == null || questMarkSprite == null)
            {
                throw new System.InvalidOperationException("[HUDTaskWorldMarkerView] HUD 世界标记存在未绑定引用。");
            }

            Hide();
        }

        /// <summary>投影目标并更新屏幕位置、屏幕边缘方向及整数米距离。</summary>
        /// <param name="gameplayCamera">缓存的 MainCamera。</param>
        /// <param name="playerTransform">当前活动角色的世界 Transform。</param>
        /// <param name="targetTransform">导航目标 Transform。</param>
        /// <param name="targetOffset">相对目标原点的世界坐标偏移。</param>
        /// <returns>投影有效时返回到目标原点的整数米距离。</returns>
        public int Render(Camera gameplayCamera, Transform playerTransform, Transform targetTransform, Vector3 targetOffset)
        {
            if (gameplayCamera == null || playerTransform == null || targetTransform == null ||
                !targetTransform.gameObject.activeInHierarchy)
            {
                Hide();
                return -1;
            }

            Vector3 screenPosition = gameplayCamera.WorldToScreenPoint(targetTransform.position + targetOffset);
            Rect cameraPixelRect = gameplayCamera.pixelRect;
            bool isInFront = screenPosition.z > 0f;
            Vector2 screenPoint = new Vector2(screenPosition.x, screenPosition.y);
            if (!isInFront)
            {
                // 背后目标翻到视口对侧，随后与普通屏幕外目标使用同一边缘钳制规则。
                screenPoint.x = cameraPixelRect.xMin + cameraPixelRect.xMax - screenPoint.x;
                screenPoint.y = cameraPixelRect.yMin + cameraPixelRect.yMax - screenPoint.y;
            }

            bool isOnScreen = isInFront && cameraPixelRect.Contains(screenPoint);
            int distance = Mathf.RoundToInt(Vector3.Distance(playerTransform.position, targetTransform.position));
            Camera canvasCamera = hudCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : hudCanvas.worldCamera != null ? hudCanvas.worldCamera : gameplayCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRectTransform, screenPoint, canvasCamera, out Vector2 targetLocalPoint))
            {
                Hide();
                return -1;
            }

            Vector2 finalLocalPoint = targetLocalPoint;
            if (!isOnScreen)
            {
                Rect canvasRect = canvasRectTransform.rect;
                float insetX = safeAreaInset + markerRectTransform.rect.width * 0.5f;
                float insetY = safeAreaInset + markerRectTransform.rect.height * 0.5f;
                finalLocalPoint.x = Mathf.Clamp(targetLocalPoint.x, canvasRect.xMin + insetX, canvasRect.xMax - insetX);
                finalLocalPoint.y = Mathf.Clamp(targetLocalPoint.y, canvasRect.yMin + insetY, canvasRect.yMax - insetY);
                Vector2 direction = targetLocalPoint - canvasRect.center;
                if (direction.sqrMagnitude < 0.001f)
                    direction = Vector2.up;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
                directionArrowImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            markerRectTransform.anchoredPosition = finalLocalPoint;
            questMarkImage.sprite = questMarkSprite;
            directionArrowImage.enabled = !isOnScreen;
            if (lastDisplayedDistance != distance)
            {
                lastDisplayedDistance = distance;
                distanceText.text = $"{distance}m";
            }

            bool projectionChanged = hasProjection && lastProjectionWasOnScreen != isOnScreen;
            SetVisible(true);
            if (projectionChanged)
                Debug.Log($"[HUDTaskWorldMarkerView] 目标标记切换为{(isOnScreen ? "屏幕内" : "屏幕边缘")}显示。", this);
            lastProjectionWasOnScreen = isOnScreen;
            hasProjection = true;
            return distance;
        }

        /// <summary>隐藏标记视觉但保留组件，以便 HUD Controller 下一帧继续投影。</summary>
        public void Hide()
        {
            SetVisible(false);
        }
        #endregion

        #region 可见状态
        /// <summary>在状态变化时切换 CanvasGroup，不逐帧重复写入或记录日志。</summary>
        /// <param name="visible">是否显示标记。</param>
        private void SetVisible(bool visible)
        {
            if (markerVisible == visible)
                return;

            markerVisible = visible;
            markerCanvasGroup.alpha = visible ? 1f : 0f;
            markerCanvasGroup.interactable = false;
            markerCanvasGroup.blocksRaycasts = false;
            Debug.Log($"[HUDTaskWorldMarkerView] 世界目标标记{(visible ? "显示" : "隐藏")}。", this);
        }
        #endregion
    }
}
