using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.ItemSystem;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>所有背包分类共用的右侧详情 View。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖详情面板下的 DetailsRoot、品质背景、主图、分类详情父节点及文本、两个固定武器属性块与装备者区域；所有依赖必须由 Prefab 显式绑定。")]
    public sealed class BagDetailView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required] private GameObject detailsRoot;
        [SerializeField] private Image nameBackground;
        [SerializeField] private Image detailBackground;
        [SerializeField] private Image mainIcon;
        [SerializeField] private Image rarityStars;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text primaryText;
        [SerializeField] private TMP_Text secondaryText;
        [SerializeField, Required] private GameObject weaponDetailsGroup;
        [SerializeField, Required] private GameObject artifactDetailsGroup;
        [SerializeField, Required] private GameObject otherItemDetailsGroup;
        [SerializeField] private TMP_Text artifactDetailLinesText;
        [SerializeField] private TMP_Text otherItemDetailLinesText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField, Required] private GameObject primaryWeaponAttributeRoot;
        [SerializeField] private TMP_Text primaryWeaponAttributeNameText;
        [SerializeField] private TMP_Text primaryWeaponAttributeValueText;
        [SerializeField, Required] private GameObject secondaryWeaponAttributeRoot;
        [SerializeField] private TMP_Text secondaryWeaponAttributeNameText;
        [SerializeField] private TMP_Text secondaryWeaponAttributeValueText;
        [SerializeField, Required] private GameObject ownerRoot;
        [SerializeField] private Image ownerIcon;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField, MinValue(1f)] private float rarityStarWidth = 20f;
        [SerializeField] private Color[] nameBackgroundColors = new Color[5];
        [SerializeField] private Color[] detailBackgroundColors = new Color[5];

        #endregion

        #region Unity 生命周期

        /// <summary>校验详情 View 的显式 Prefab 依赖。</summary>
        private void Awake()
        {
            if (detailsRoot == null) throw new InvalidOperationException("[BagDetailView] 未绑定 DetailsRoot。");
            if (nameBackground == null) throw new InvalidOperationException("[BagDetailView] 未绑定名称背景。");
            if (detailBackground == null) throw new InvalidOperationException("[BagDetailView] 未绑定详情背景。");
            if (mainIcon == null) throw new InvalidOperationException("[BagDetailView] 未绑定主图。");
            if (rarityStars == null) throw new InvalidOperationException("[BagDetailView] 未绑定品质星级 Image。");
            if (titleText == null) throw new InvalidOperationException("[BagDetailView] 未绑定标题文本。");
            if (categoryText == null) throw new InvalidOperationException("[BagDetailView] 未绑定分类文本。");
            if (primaryText == null) throw new InvalidOperationException("[BagDetailView] 未绑定主信息文本。");
            if (secondaryText == null) throw new InvalidOperationException("[BagDetailView] 未绑定次信息文本。");
            if (weaponDetailsGroup == null) throw new InvalidOperationException("[BagDetailView] 未绑定武器详情组。");
            if (artifactDetailsGroup == null) throw new InvalidOperationException("[BagDetailView] 未绑定圣遗物详情组。");
            if (otherItemDetailsGroup == null) throw new InvalidOperationException("[BagDetailView] 未绑定其他物品详情组。");
            if (artifactDetailLinesText == null) throw new InvalidOperationException("[BagDetailView] 未绑定圣遗物详情行文本。");
            if (otherItemDetailLinesText == null) throw new InvalidOperationException("[BagDetailView] 未绑定其他物品详情行文本。");
            if (descriptionText == null) throw new InvalidOperationException("[BagDetailView] 未绑定描述文本。");
            if (primaryWeaponAttributeRoot == null) throw new InvalidOperationException("[BagDetailView] 未绑定第一武器属性块。");
            if (primaryWeaponAttributeNameText == null) throw new InvalidOperationException("[BagDetailView] 未绑定第一武器属性名称。");
            if (primaryWeaponAttributeValueText == null) throw new InvalidOperationException("[BagDetailView] 未绑定第一武器属性数值。");
            if (secondaryWeaponAttributeRoot == null) throw new InvalidOperationException("[BagDetailView] 未绑定第二武器属性块。");
            if (secondaryWeaponAttributeNameText == null) throw new InvalidOperationException("[BagDetailView] 未绑定第二武器属性名称。");
            if (secondaryWeaponAttributeValueText == null) throw new InvalidOperationException("[BagDetailView] 未绑定第二武器属性数值。");
            if (ownerRoot == null) throw new InvalidOperationException("[BagDetailView] 未绑定装备者节点。");
            if (ownerIcon == null) throw new InvalidOperationException("[BagDetailView] 未绑定装备者头像。");
            if (ownerText == null) throw new InvalidOperationException("[BagDetailView] 未绑定装备者文本。");
            rarityStars.type = Image.Type.Tiled;
            rarityStars.raycastTarget = false;
        }

        #endregion

        #region 绑定

        /// <summary>完整覆盖当前详情内容并按内容显隐可选节点。</summary>
        /// <param name="details">分类详情快照；为空时清除并隐藏。</param>
        public void Bind(BagDetailViewData details)
        {
            if (details == null)
            {
                Clear();
                return;
            }

            Debug.Log($"[BagDetailView] 绑定详情，entry={details.EntryKey}，category={details.CategoryText}。", this);
            detailsRoot.SetActive(true);
            titleText.text = details.DisplayName;
            categoryText.text = details.CategoryText;
            primaryText.text = details.PrimaryText;
            secondaryText.text = details.SecondaryText;
            mainIcon.sprite = details.Icon;
            ownerIcon.sprite = details.OwnerIcon;
            ownerText.text = details.OwnerText;
            categoryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.CategoryText));
            primaryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.PrimaryText));
            secondaryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.SecondaryText));
            BindCategoryDetails(details);
            BindWeaponAttributes(details.WeaponAttributes);
            ownerRoot.SetActive(details.ShowOwner && details.OwnerIcon != null);
            ApplyRarity(details.Rarity);
            ApplyRarityColors(details.Rarity);
        }

        /// <summary>清除文本、图标和品质状态，避免分类切换残留旧内容。</summary>
        public void Clear()
        {
            if (detailsRoot == null) return;
            Debug.Log("[BagDetailView] 清空背包详情并重置固定武器属性块。", this);
            titleText.text = string.Empty;
            categoryText.text = string.Empty;
            primaryText.text = string.Empty;
            secondaryText.text = string.Empty;
            artifactDetailLinesText.text = string.Empty;
            otherItemDetailLinesText.text = string.Empty;
            descriptionText.text = string.Empty;
            ownerText.text = string.Empty;
            primaryWeaponAttributeNameText.text = string.Empty;
            primaryWeaponAttributeValueText.text = string.Empty;
            secondaryWeaponAttributeNameText.text = string.Empty;
            secondaryWeaponAttributeValueText.text = string.Empty;
            mainIcon.sprite = null;
            ownerIcon.sprite = null;
            categoryText.gameObject.SetActive(false);
            primaryText.gameObject.SetActive(false);
            secondaryText.gameObject.SetActive(false);
            weaponDetailsGroup.SetActive(false);
            artifactDetailsGroup.SetActive(false);
            otherItemDetailsGroup.SetActive(false);
            artifactDetailLinesText.gameObject.SetActive(false);
            otherItemDetailLinesText.gameObject.SetActive(false);
            descriptionText.gameObject.SetActive(false);
            primaryWeaponAttributeRoot.SetActive(false);
            secondaryWeaponAttributeRoot.SetActive(false);
            ownerRoot.SetActive(false);
            ApplyRarity(0);
            detailsRoot.SetActive(false);
        }

        #endregion

        #region 内部辅助

        /// <summary>按条目分类切换对应详情父节点，并绑定圣遗物或其他物品的文字行。</summary>
        /// <param name="details">当前背包条目详情。</param>
        private void BindCategoryDetails(BagDetailViewData details)
        {
            bool isWeapon = details.EntryKey.Category == ItemCategory.Weapon;
            bool isArtifact = details.EntryKey.Category == ItemCategory.Artifact;
            bool isOtherItem = !isWeapon && !isArtifact;

            // 分类根节点跟随当前 Tab 切换；无详情数据时只隐藏组内文本，仍保留清楚的层级状态。
            weaponDetailsGroup.SetActive(isWeapon);
            artifactDetailsGroup.SetActive(isArtifact);
            otherItemDetailsGroup.SetActive(isOtherItem);

            artifactDetailLinesText.text = isArtifact ? string.Join("\n", details.DetailLines) : string.Empty;
            otherItemDetailLinesText.text = isOtherItem ? string.Join("\n", details.DetailLines) : string.Empty;
            descriptionText.text = details.Description;
            artifactDetailLinesText.gameObject.SetActive(isArtifact && details.DetailLines.Count > 0);
            otherItemDetailLinesText.gameObject.SetActive(isOtherItem && details.DetailLines.Count > 0);
            descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.Description));
        }

        /// <summary>绑定武器品质面板中的两个固定属性槽位。</summary>
        /// <param name="attributes">武器数据源提供的属性；索引之外的槽位会清空并隐藏。</param>
        private void BindWeaponAttributes(IReadOnlyList<BagWeaponAttributeViewData> attributes)
        {
            BindWeaponAttribute(
                primaryWeaponAttributeRoot,
                primaryWeaponAttributeNameText,
                primaryWeaponAttributeValueText,
                attributes,
                0);
            BindWeaponAttribute(
                secondaryWeaponAttributeRoot,
                secondaryWeaponAttributeNameText,
                secondaryWeaponAttributeValueText,
                attributes,
                1);
        }

        /// <summary>将一项武器属性绑定到固定显示块，缺少数据时清空并关闭该块。</summary>
        /// <param name="attributeRoot">属性块根节点。</param>
        /// <param name="attributeNameText">属性名称文本。</param>
        /// <param name="attributeValueText">属性数值文本。</param>
        /// <param name="attributes">最多两项的武器详情属性。</param>
        /// <param name="index">要绑定的属性索引。</param>
        private static void BindWeaponAttribute(
            GameObject attributeRoot,
            TMP_Text attributeNameText,
            TMP_Text attributeValueText,
            IReadOnlyList<BagWeaponAttributeViewData> attributes,
            int index)
        {
            bool hasAttribute = index < attributes.Count;
            attributeNameText.text = hasAttribute ? attributes[index].AttributeName : string.Empty;
            attributeValueText.text = hasAttribute ? attributes[index].FormattedValue : string.Empty;
            attributeRoot.SetActive(hasAttribute);
        }

        /// <summary>按稀有度调整 Tiled 星级图像宽度。</summary>
        /// <param name="rarity">一至五星稀有度；零表示隐藏。</param>
        private void ApplyRarity(int rarity)
        {
            int normalized = Mathf.Clamp(rarity, 0, 5);
            rarityStars.gameObject.SetActive(normalized > 0);
            RectTransform rectTransform = rarityStars.rectTransform;
            Vector2 size = rectTransform.sizeDelta;
            size.x = rarityStarWidth * normalized;
            rectTransform.sizeDelta = size;
        }

        /// <summary>按稀有度切换现有品质背景颜色。</summary>
        /// <param name="rarity">一至五星稀有度。</param>
        private void ApplyRarityColors(int rarity)
        {
            if (rarity <= 0) return;
            int index = Mathf.Clamp(rarity - 1, 0, 4);
            if (nameBackgroundColors != null && nameBackgroundColors.Length > index)
                nameBackground.color = nameBackgroundColors[index];
            if (detailBackgroundColors != null && detailBackgroundColors.Length > index)
                detailBackground.color = detailBackgroundColors[index];
        }

        #endregion
    }
}
