using System;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>所有背包分类共用的右侧详情 View。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖详情面板下的 DetailsRoot、品质背景、主图、星级和各详情文本节点；所有依赖必须由 Prefab 显式绑定。")]
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
        [SerializeField] private TMP_Text detailLinesText;
        [SerializeField] private TMP_Text descriptionText;
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
            if (detailLinesText == null) throw new InvalidOperationException("[BagDetailView] 未绑定详情行文本。");
            if (descriptionText == null) throw new InvalidOperationException("[BagDetailView] 未绑定描述文本。");
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
            detailLinesText.text = string.Join("\n", details.DetailLines);
            descriptionText.text = details.Description;
            mainIcon.sprite = details.Icon;
            ownerIcon.sprite = details.OwnerIcon;
            ownerText.text = details.OwnerText;
            categoryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.CategoryText));
            primaryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.PrimaryText));
            secondaryText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.SecondaryText));
            detailLinesText.gameObject.SetActive(details.DetailLines.Count > 0);
            descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.Description));
            ownerRoot.SetActive(details.ShowOwner && details.OwnerIcon != null);
            ApplyRarity(details.Rarity);
            ApplyRarityColors(details.Rarity);
        }

        /// <summary>清除文本、图标和品质状态，避免分类切换残留旧内容。</summary>
        public void Clear()
        {
            if (detailsRoot == null) return;
            titleText.text = string.Empty;
            categoryText.text = string.Empty;
            primaryText.text = string.Empty;
            secondaryText.text = string.Empty;
            detailLinesText.text = string.Empty;
            descriptionText.text = string.Empty;
            ownerText.text = string.Empty;
            mainIcon.sprite = null;
            ownerIcon.sprite = null;
            categoryText.gameObject.SetActive(false);
            primaryText.gameObject.SetActive(false);
            secondaryText.gameObject.SetActive(false);
            detailLinesText.gameObject.SetActive(false);
            descriptionText.gameObject.SetActive(false);
            ownerRoot.SetActive(false);
            ApplyRarity(0);
            detailsRoot.SetActive(false);
        }

        #endregion

        #region 内部辅助

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
