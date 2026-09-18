using System;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>
    /// 持有武器三维预览输出控件的轻量 View。
    /// 它只管理 RawImage 的绑定和尺寸读取，不负责加载模型或创建 RenderTexture。
    /// </summary>
    public sealed class WeaponPreviewViewportView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private RawImage weaponPreviewRawImage;

        #endregion

        #region 生命周期与校验

        /// <summary>
        /// 初始化预览输出控件的默认视觉状态。
        /// </summary>
        private void Awake()
        {
            ValidateConfiguration();
            Clear();
        }

        /// <summary>
        /// 校验 RawImage 已由 Prefab 显式绑定。
        /// </summary>
        /// <exception cref="InvalidOperationException">RawImage 未绑定时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (weaponPreviewRawImage == null)
                throw new InvalidOperationException("[WeaponPreviewViewportView] 未绑定武器预览 RawImage。");
        }

        /// <summary>
        /// 窗口销毁时解除输出纹理引用；RenderTexture 的所有权由预览运行时释放。
        /// </summary>
        private void OnDestroy()
        {
            if (weaponPreviewRawImage != null)
            {
                weaponPreviewRawImage.texture = null;
                weaponPreviewRawImage.enabled = false;
            }
        }

        #endregion

        #region 输出绑定

        /// <summary>
        /// 进入加载状态。当前设计不显示加载文字，只清空旧模型输出。
        /// </summary>
        public void SetLoading()
        {
            weaponPreviewRawImage.texture = null;
            weaponPreviewRawImage.enabled = false;
            weaponPreviewRawImage.color = Color.white;
            weaponPreviewRawImage.raycastTarget = false;
        }

        /// <summary>
        /// 将专用摄像机生成的 RenderTexture 绑定到 RawImage。
        /// </summary>
        /// <param name="renderTexture">待显示的离屏纹理。</param>
        public void SetTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null)
                throw new ArgumentNullException(nameof(renderTexture), "[WeaponPreviewViewportView] 不能绑定空的武器预览 RenderTexture。");

            weaponPreviewRawImage.texture = renderTexture;
            weaponPreviewRawImage.color = Color.white;
            weaponPreviewRawImage.raycastTarget = false;
            // RawImage 没有纹理时会回退绘制 Unity 默认白纹理，因此必须在绑定有效 RT 后才启用组件。
            weaponPreviewRawImage.enabled = true;
        }

        /// <summary>
        /// 清空预览输出。失败和没有模型地址时保持完全空白。
        /// </summary>
        /// <param name="message">保留该参数以兼容统一预览调用契约；本轮不在 UI 上显示文字。</param>
        public void SetUnavailable(string message)
        {
            _ = message;
            Clear();
        }

        /// <summary>
        /// 解除 RawImage 与 RenderTexture 的引用。
        /// </summary>
        public void Clear()
        {
            if (weaponPreviewRawImage == null) return;
            weaponPreviewRawImage.texture = null;
            weaponPreviewRawImage.enabled = false;
            weaponPreviewRawImage.color = Color.white;
            weaponPreviewRawImage.raycastTarget = false;
        }

        /// <summary>
        /// 读取 RawImage 当前的目标像素尺寸，供 RenderTexture 计算分辨率。
        /// </summary>
        /// <returns>按 Canvas 缩放因子换算后的像素宽高。</returns>
        public Vector2Int GetTargetPixelSize()
        {
            RectTransform rectTransform = weaponPreviewRawImage.rectTransform;
            float canvasScale = weaponPreviewRawImage.canvas == null
                ? 1f
                : Mathf.Max(1f, weaponPreviewRawImage.canvas.scaleFactor);
            int width = Mathf.Max(1, Mathf.RoundToInt(rectTransform.rect.width * canvasScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(rectTransform.rect.height * canvasScale));
            return new Vector2Int(width, height);
        }

        #endregion
    }
}
