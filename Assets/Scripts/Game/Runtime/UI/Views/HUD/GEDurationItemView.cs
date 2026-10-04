using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.TAG;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>显示一个 GE Runtime 与 GrantedTag 对应的图标及持续时间圆环。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 GEDurationItem Prefab 中静态配置的 BK、BuffUI、DurationIcon Image；父级 HUDGEListView 负责实例化和释放。")]
    public sealed class GEDurationItemView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("底板")]
        private Image backgroundImage;
        [SerializeField, Required, LabelText("效果图标")]
        private Image effectIconImage;
        [SerializeField, Required, LabelText("持续时间进度")]
        private Image durationFillImage;

        #endregion

        #region 显示状态

        private GameplayTag displayedTag;
        private GameEffectRuntime runtime;
        private float progressDuration;
        private bool infinite;

        #endregion

        #region 生命周期

        /// <summary>校验模板引用并禁用所有图片的射线检测。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            backgroundImage.raycastTarget = false;
            effectIconImage.raycastTarget = false;
            durationFillImage.raycastTarget = false;
            durationFillImage.type = Image.Type.Filled;
            durationFillImage.fillMethod = Image.FillMethod.Radial360;
        }

        /// <summary>按 GE Runtime 的最新剩余时间刷新圆形进度。</summary>
        private void Update()
        {
            if (runtime == null || !runtime.IsActive || infinite)
            {
                durationFillImage.fillAmount = 0f;
                return;
            }

            durationFillImage.fillAmount = progressDuration > 0f
                ? Mathf.Clamp01(runtime.RemainingDuration / progressDuration)
                : 0f;
        }

        #endregion

        #region 配置与显示

        /// <summary>获取当前条目绑定的 Active GE Runtime。</summary>
        public GameEffectRuntime Runtime => runtime;

        /// <summary>确认 GEDurationItem 模板的三个 Image 均已显式绑定。</summary>
        public void ValidateConfiguration()
        {
            if (backgroundImage == null || effectIconImage == null || durationFillImage == null)
                throw new System.InvalidOperationException(
                    $"[GEDurationItemView] Prefab '{name}' 未绑定 BK、BuffUI 或 DurationIcon Image。");
        }

        /// <summary>绑定一个 Runtime 与 Tag 图标映射；重应用时更新现有条目。</summary>
        /// <param name="tag">该图标表示的精确 GrantedTag。</param>
        /// <param name="effectRuntime">持续 GE Runtime。</param>
        /// <param name="icon">配置表中的效果 Sprite。</param>
        /// <param name="duration">当前表现进度使用的总持续时间。</param>
        /// <param name="isInfinite">该效果是否为 Infinite GE。</param>
        public void Bind(GameplayTag tag, GameEffectRuntime effectRuntime, Sprite icon, float duration, bool isInfinite)
        {
            displayedTag = tag;
            runtime = effectRuntime;
            progressDuration = duration;
            infinite = isInfinite;
            backgroundImage.enabled = true;
            effectIconImage.sprite = icon;
            effectIconImage.enabled = icon != null;
            durationFillImage.enabled = !infinite;
            durationFillImage.fillAmount = infinite || duration <= 0f
                ? 0f
                : Mathf.Clamp01(effectRuntime.RemainingDuration / duration);
        }

        /// <summary>清除绑定数据与视觉状态，供列表项复用和销毁前调用。</summary>
        public void Clear()
        {
            displayedTag = GameplayTag.Empty;
            runtime = null;
            progressDuration = 0f;
            infinite = false;
            effectIconImage.sprite = null;
            effectIconImage.enabled = false;
            durationFillImage.fillAmount = 0f;
            durationFillImage.enabled = false;
        }

        /// <summary>判断该实例是否展示指定 Runtime 与 Tag 的组合。</summary>
        /// <param name="effectRuntime">需要匹配的 GE Runtime。</param>
        /// <param name="tag">需要匹配的 GrantedTag。</param>
        /// <returns>Runtime 和 Tag 引用均相同则返回 true。</returns>
        public bool Matches(GameEffectRuntime effectRuntime, GameplayTag tag) =>
            ReferenceEquals(runtime, effectRuntime) && displayedTag == tag;

        #endregion
    }
}
