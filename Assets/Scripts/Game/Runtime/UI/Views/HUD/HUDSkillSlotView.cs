using System;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>呈现固定技能格的能力图标与真实冷却进度，不负责激活技能。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 Prefab 中显式绑定的 Button、底板 Image、图标 Image、圆形冷却 Image 和冷却 TMP 文本；该 Button 仅作展示，不接收点击。")]
    public sealed class HUDSkillSlotView : MonoBehaviour
    {
        #region 配置字段

        [SerializeField, LabelText("技能输入类型")]
        private PlayerInputType inputType;
        [SerializeField, Required, LabelText("展示按钮")]
        private Button slotButton;
        [SerializeField, Required, LabelText("底板")]
        private Image backgroundImage;
        [SerializeField, Required, LabelText("技能图标")]
        private Image iconImage;
        [SerializeField, Required, LabelText("圆形冷却遮罩")]
        private Image cooldownFillImage;
        [SerializeField, Required, LabelText("冷却数字")]
        private TMP_Text cooldownText;

        #endregion

        #region 属性

        /// <summary>获取该固定技能格对应的玩家输入类型。</summary>
        public PlayerInputType InputType => inputType;

        #endregion

        #region 生命周期

        /// <summary>校验 Prefab 引用并锁定技能格为不可点击的展示状态。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            slotButton.interactable = false;
            slotButton.transition = Selectable.Transition.None;
            slotButton.targetGraphic.raycastTarget = false;
            backgroundImage.raycastTarget = false;
            iconImage.raycastTarget = false;
            cooldownFillImage.raycastTarget = false;
            cooldownText.raycastTarget = false;
            cooldownFillImage.type = Image.Type.Filled;
            cooldownFillImage.fillMethod = Image.FillMethod.Radial360;
            Clear();
        }

        #endregion

        #region 视图刷新

        /// <summary>更新该槽位展示的 Gameplay Ability 图标；空图标表示当前角色未配置该槽位。</summary>
        /// <param name="icon">能力配置上的图标 Sprite。</param>
        public void SetAbilityIcon(Sprite icon)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        /// <summary>刷新圆形遮罩和冷却数字；剩余比例用于表达实际 GE Runtime 进度。</summary>
        /// <param name="isActive">当前槽位是否存在匹配的 Active 冷却。</param>
        /// <param name="remainingDuration">冷却 GE Runtime 的剩余时长。</param>
        /// <param name="duration">冷却 GE Runtime 的完整时长。</param>
        /// <param name="isInfinite">冷却是否为 Infinite GE。</param>
        public void RefreshCooldown(bool isActive, float remainingDuration, float duration, bool isInfinite)
        {
            bool visible = isActive && (isInfinite || duration > 0f);
            cooldownFillImage.enabled = visible;
            cooldownText.enabled = visible;
            if (!visible)
            {
                cooldownFillImage.fillAmount = 1f;
                cooldownText.text = string.Empty;
                return;
            }

            cooldownFillImage.fillAmount = isInfinite
                ? 1f
                : Mathf.Clamp01(remainingDuration / duration);
            cooldownText.text = isInfinite ? "∞" : Mathf.CeilToInt(Mathf.Max(0f, remainingDuration)).ToString();
        }

        /// <summary>清除槽位能力和冷却显示，保留 Prefab 中的固定底板。</summary>
        public void Clear()
        {
            SetAbilityIcon(null);
            RefreshCooldown(false, 0f, 0f, false);
        }

        #endregion

        #region 配置校验

        /// <summary>验证 Inspector 中绑定的展示控件，尽早暴露 Prefab 配置缺失。</summary>
        /// <exception cref="InvalidOperationException">任一必需的静态 UI 引用缺失时抛出。</exception>
        public void ValidateConfiguration()
        {
            if (!Enum.IsDefined(typeof(PlayerInputType), inputType))
                throw new InvalidOperationException(
                    $"[HUDSkillSlotView] '{name}' 配置了未知输入类型 {(int)inputType}。");
            if (slotButton == null || backgroundImage == null || iconImage == null ||
                cooldownFillImage == null || cooldownText == null || slotButton.targetGraphic == null)
                throw new InvalidOperationException(
                    $"[HUDSkillSlotView] '{name}' 缺少 Button、底板、图标、冷却遮罩或冷却文本引用。");
        }

        #endregion
    }
}
