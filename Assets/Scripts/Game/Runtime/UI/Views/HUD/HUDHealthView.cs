using System.Globalization;
using RPG.Character;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Views.HUD
{
    /// <summary>刷新 HUDWindow Prefab 内预先摆放的 Active 血条和固定队伍行。</summary>
    [DisallowMultipleComponent]
    [InfoBox("本 View 只引用 HUDWindow Prefab 内静态摆放的 Active 血条与四个 PartySlot View；所有控件通过 Inspector 显式绑定。")]
    public sealed class HUDHealthView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private GameObject activeRootObject;
        [SerializeField, Required] private Image activeHealthFillImage;
        [SerializeField, Required] private TMP_Text activeLevelText;
        [SerializeField, Required] private TMP_Text activeHealthText;
        [SerializeField, Required] private HUDPartySlotView[] partySlotViews;

        #endregion

        #region 生命周期

        /// <summary>校验 Prefab 静态控件绑定，避免窗口显示后才发现缺少血条对象。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            WSLog.Log("[HUDHealthView] Prefab 静态血量控件绑定完成。");
        }

        #endregion

        #region 绑定与校验

        /// <summary>校验 Active 血条与四个固定角色行均已由 HUDWindow Prefab 绑定。</summary>
        public void ValidateConfiguration()
        {
            if (activeRootObject == null || activeHealthFillImage == null ||
                activeLevelText == null || activeHealthText == null)
                throw new System.InvalidOperationException("[HUDHealthView] HUDWindow Prefab 未绑定 Active 等级、血条或血量文本。");
            if (partySlotViews == null || partySlotViews.Length != CharacterParty.SlotCount)
                throw new System.InvalidOperationException("[HUDHealthView] HUDWindow Prefab 必须绑定四个固定 PartySlot View。");

            for (int slotIndex = 0; slotIndex < partySlotViews.Length; slotIndex++)
            {
                if (partySlotViews[slotIndex] == null)
                    throw new System.InvalidOperationException($"[HUDHealthView] HUDWindow Prefab 未绑定 PartySlot View，slot={slotIndex + 1}。");
                partySlotViews[slotIndex].ValidateConfiguration();
            }
        }

        #endregion

        #region 状态刷新

        /// <summary>刷新底部 Active 角色的等级与 ASC 血量。</summary>
        /// <param name="level">Active 角色等级。</param>
        /// <param name="health">Active ASC 当前生命值。</param>
        /// <param name="maxHealth">Active ASC 生命值上限。</param>
        public void SetActiveCharacter(int level, float health, float maxHealth)
        {
            activeRootObject.SetActive(true);
            activeLevelText.text = $"Lv.{level}";
            activeHealthFillImage.fillAmount = GetHealthRatio(health, maxHealth);
            activeHealthText.text = $"{FormatHealth(health)} / {FormatHealth(maxHealth)}";
        }

        /// <summary>将当前角色数据绑定到固定队伍槽位。</summary>
        /// <param name="slotIndex">零基固定槽位下标。</param>
        /// <param name="characterName">队伍角色名称。</param>
        /// <param name="portrait">角色侧面头像。</param>
        /// <param name="health">ASC 当前生命值。</param>
        /// <param name="maxHealth">ASC 生命值上限。</param>
        /// <param name="isActive">槽位角色是否为当前 Active 角色。</param>
        public void SetPartySlot(
            int slotIndex,
            string characterName,
            Sprite portrait,
            float health,
            float maxHealth,
            bool isActive)
        {
            partySlotViews[slotIndex].Bind(characterName, portrait, health, maxHealth, isActive);
        }

        /// <summary>隐藏指定空槽，清除上次绑定的名称、头像和血条填充。</summary>
        /// <param name="slotIndex">零基固定槽位下标。</param>
        public void ClearPartySlot(int slotIndex)
        {
            partySlotViews[slotIndex].Clear();
        }

        /// <summary>在队伍尚未 Ready 时隐藏 Active 信息并清空全部队伍行。</summary>
        public void Clear()
        {
            activeLevelText.text = string.Empty;
            activeHealthText.text = string.Empty;
            activeHealthFillImage.fillAmount = 0f;
            activeRootObject.SetActive(false);
            for (int slotIndex = 0; slotIndex < partySlotViews.Length; slotIndex++)
                partySlotViews[slotIndex].Clear();
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

        /// <summary>固定使用千位分隔的整数格式显示血量。</summary>
        /// <param name="value">生命值。</param>
        /// <returns>不依赖系统区域设置的血量文本。</returns>
        private static string FormatHealth(float value)
        {
            return Mathf.Max(0f, value).ToString("N0", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
