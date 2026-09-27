using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>刷新 HUDWindow Prefab 中一个固定队伍行的名称、头像、高亮和血量。</summary>
    [DisallowMultipleComponent]
    [InfoBox("此 View 挂在 HUDWindow Prefab 中预先摆放的 PartySlot 行上；渐隐背景、角色编号、头像、名称和血条均由 Inspector 显式绑定。")]
    public sealed class HUDPartySlotView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private Image gradientBackgroundImage;
        [SerializeField, Required] private Image slotMarkImage;
        [SerializeField, Required] private Image portraitImage;
        [SerializeField, Required] private Image healthFillImage;
        [SerializeField, Required] private TMP_Text characterNameText;

        #endregion

        #region 生命周期

        /// <summary>确认固定队伍行的图片和文本已经在 Prefab 中配置。</summary>
        private void Awake()
        {
            ValidateConfiguration();
        }

        #endregion

        #region 绑定与校验

        /// <summary>校验渐隐底衬、角色编号图、头像、名称及血条填充引用。</summary>
        public void ValidateConfiguration()
        {
            if (gradientBackgroundImage == null || slotMarkImage == null || portraitImage == null ||
                healthFillImage == null || characterNameText == null)
                throw new System.InvalidOperationException($"[HUDPartySlotView] HUDWindow Prefab 未完整绑定角色行，row={name}。");
            if (gradientBackgroundImage.sprite == null || slotMarkImage.sprite == null)
                throw new System.InvalidOperationException($"[HUDPartySlotView] 队伍行缺少渐隐或编号 Sprite，row={name}。");
        }

        #endregion

        #region 状态刷新

        /// <summary>用 ASC 与角色数据完整覆盖当前固定槽位的可变显示状态。</summary>
        /// <param name="characterName">队伍角色名称。</param>
        /// <param name="portrait">角色侧面头像。</param>
        /// <param name="health">ASC 当前生命值。</param>
        /// <param name="maxHealth">ASC 生命值上限。</param>
        /// <param name="isActive">槽位角色是否为当前 Active 角色。</param>
        public void Bind(string characterName, Sprite portrait, float health, float maxHealth, bool isActive)
        {
            gameObject.SetActive(true);
            characterNameText.text = characterName;
            characterNameText.color = isActive ? new Color(1f, 0.88f, 0.56f, 1f) : Color.white;
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait != null;
            healthFillImage.fillAmount = GetHealthRatio(health, maxHealth);
            gradientBackgroundImage.color = isActive
                ? new Color(0.16f, 0.18f, 0.16f, 0.42f)
                : new Color(0.035f, 0.055f, 0.07f, 0.25f);
        }

        /// <summary>隐藏空槽并清除之前绑定的角色名称、头像和血量。</summary>
        public void Clear()
        {
            characterNameText.text = string.Empty;
            portraitImage.sprite = null;
            portraitImage.enabled = false;
            healthFillImage.fillAmount = 0f;
            gameObject.SetActive(false);
        }

        #endregion

        #region 内部辅助

        /// <summary>将有效生命值映射为零到一之间的 Image 填充比例。</summary>
        /// <param name="health">当前生命值。</param>
        /// <param name="maxHealth">生命值上限。</param>
        /// <returns>适用于 Image.fillAmount 的比例。</returns>
        private static float GetHealthRatio(float health, float maxHealth)
        {
            return maxHealth > 0f ? Mathf.Clamp01(health / maxHealth) : 0f;
        }

        #endregion
    }
}
