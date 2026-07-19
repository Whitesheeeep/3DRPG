using UnityEngine;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI RectTransform 坐标换算工具。
    /// </summary>
    public static class UIRectUtility
    {
        /// <summary>
        /// 获取 RectTransform 在屏幕坐标中的矩形范围。
        /// </summary>
        /// <param name="rectTransform">目标 RectTransform。</param>
        /// <param name="uiCamera">UI 相机，Overlay Canvas 可传 null。</param>
        /// <returns>目标在屏幕坐标中的矩形范围。</returns>
        public static Rect GetScreenRect(RectTransform rectTransform, Camera uiCamera)
        {
            if (rectTransform == null) return default;

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[i]);
                min = Vector2.Min(min, screenPoint);
                max = Vector2.Max(max, screenPoint);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// 将屏幕尺寸转换为指定 RectTransform 平面下的本地尺寸。
        /// </summary>
        /// <param name="targetRect">目标 RectTransform 平面。</param>
        /// <param name="screenSize">屏幕像素尺寸。</param>
        /// <param name="uiCamera">UI 相机，Overlay Canvas 可传 null。</param>
        /// <param name="referenceScreenPosition">转换参考屏幕点。</param>
        /// <returns>目标平面中的本地尺寸。</returns>
        public static Vector2 ScreenSizeToLocalSize(
            RectTransform targetRect,
            Vector2 screenSize,
            Camera uiCamera,
            Vector2 referenceScreenPosition)
        {
            if (targetRect == null) return Vector2.zero;

            Vector2 xDelta = ScreenDeltaToLocalDelta(
                targetRect,
                new Vector2(screenSize.x, 0f),
                uiCamera,
                referenceScreenPosition);
            Vector2 yDelta = ScreenDeltaToLocalDelta(
                targetRect,
                new Vector2(0f, screenSize.y),
                uiCamera,
                referenceScreenPosition);
            return new Vector2(Mathf.Abs(xDelta.x), Mathf.Abs(yDelta.y));
        }

        /// <summary>
        /// 将屏幕坐标偏移转换为指定 RectTransform 平面下的本地坐标偏移。
        /// </summary>
        /// <param name="targetRect">目标 RectTransform 平面。</param>
        /// <param name="screenDelta">屏幕像素偏移。</param>
        /// <param name="uiCamera">UI 相机，Overlay Canvas 可传 null。</param>
        /// <param name="referenceScreenPosition">转换参考屏幕点。</param>
        /// <returns>目标平面中的本地坐标偏移。</returns>
        public static Vector2 ScreenDeltaToLocalDelta(
            RectTransform targetRect,
            Vector2 screenDelta,
            Camera uiCamera,
            Vector2 referenceScreenPosition)
        {
            if (targetRect == null) return Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    targetRect,
                    referenceScreenPosition,
                    uiCamera,
                    out Vector2 localStart))
                return Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    targetRect,
                    referenceScreenPosition + screenDelta,
                    uiCamera,
                    out Vector2 localEnd))
                return Vector2.zero;

            return localEnd - localStart;
        }
    }
}
