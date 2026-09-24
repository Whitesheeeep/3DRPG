using System;
using System.Collections.Generic;
using RPG.Game.UI.Character;
using RPG.ItemSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色已装备武器页面 View。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterWeaponPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private Image weaponIcon;
        [SerializeField] private Image rarityStars;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text refinementText;
        [SerializeField] private TMP_Text detailLinesText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Button replaceButton;
        [SerializeField] private Button developmentButton;
        [SerializeField] private float rarityStarWidth = 6f;
        private EquipmentInstanceId currentInstanceId;

        #endregion

        #region 事件

        /// <summary>请求打开当前武器培养窗口。</summary>
        public event Action<EquipmentInstanceId> DevelopmentRequested;

        #endregion

        #region 生命周期

        /// <summary>校验武器页面的显式依赖并关闭不可用的换装按钮。</summary>
        private void Awake()
        {
            if (detailsRoot == null || emptyRoot == null || weaponIcon == null || rarityStars == null ||
                nameText == null || typeText == null || levelText == null || refinementText == null ||
                detailLinesText == null || descriptionText == null || replaceButton == null || developmentButton == null)
                throw new InvalidOperationException("[CharacterWeaponPageView] 武器页面绑定不完整。");
            replaceButton.interactable = false;
            replaceButton.onClick.AddListener(HandleReplaceClicked);
            developmentButton.onClick.AddListener(HandleDevelopmentClicked);
            rarityStars.type = Image.Type.Tiled;
            rarityStars.raycastTarget = false;
        }

        /// <summary>注销按钮监听。</summary>
        private void OnDestroy()
        {
            if (replaceButton != null) replaceButton.onClick.RemoveListener(HandleReplaceClicked);
            if (developmentButton != null) developmentButton.onClick.RemoveListener(HandleDevelopmentClicked);
        }

        #endregion

        #region 绑定

        /// <summary>绑定已装备武器或显示空槽。</summary>
        /// <param name="data">武器显示数据。</param>
        public void Bind(CharacterWeaponViewData data)
        {
            bool hasWeapon = data != null && data.HasWeapon;
            detailsRoot.SetActive(hasWeapon);
            emptyRoot.SetActive(!hasWeapon);
            developmentButton.interactable = hasWeapon;
            developmentButton.gameObject.SetActive(hasWeapon);
            if (!hasWeapon)
            {
                currentInstanceId = default;
                weaponIcon.sprite = null;
                weaponIcon.enabled = false;
                weaponIcon.gameObject.SetActive(false);
                rarityStars.gameObject.SetActive(false);
                nameText.text = typeText.text = levelText.text = refinementText.text =
                    detailLinesText.text = descriptionText.text = string.Empty;
                detailLinesText.gameObject.SetActive(false);
                descriptionText.gameObject.SetActive(false);
                return;
            }

            currentInstanceId = data.InstanceId;
            weaponIcon.sprite = data.Icon;
            weaponIcon.enabled = data.Icon != null;
            weaponIcon.gameObject.SetActive(data.Icon != null);
            nameText.text = data.Name;
            typeText.text = data.Type;
            levelText.text = data.LevelText;
            refinementText.text = data.RefinementText;
            detailLinesText.text = string.Join("\n", data.DetailLines ?? Array.Empty<string>());
            detailLinesText.gameObject.SetActive(data.DetailLines != null && data.DetailLines.Count > 0);
            descriptionText.text = data.Description;
            descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(data.Description));
            rarityStars.gameObject.SetActive(data.Rarity > 0);
            Vector2 size = rarityStars.rectTransform.sizeDelta;
            size.x = rarityStarWidth * Mathf.Clamp(data.Rarity, 0, 5);
            rarityStars.rectTransform.sizeDelta = size;
        }

        /// <summary>清空武器页面。</summary>
        public void Clear()
        {
            Bind(null);
        }

        #endregion

        #region 内部事件

        /// <summary>处理不可用的换装按钮点击。</summary>
        private void HandleReplaceClicked()
        {
        }

        /// <summary>转发武器培养意图。</summary>
        private void HandleDevelopmentClicked()
        {
            if (currentInstanceId.IsValid) DevelopmentRequested?.Invoke(currentInstanceId);
        }

        #endregion
    }
}
