using System;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>背包右侧详情的共用品质外壳，承载主图、名称和装备者状态。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖详情面板下已绑定的 DetailsRoot、NameBK、DetailBK、主图、标题和装备者节点。")]
    public sealed class BagDetailShellView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Image nameBackground;
        [SerializeField] private Image detailBackground;
        [SerializeField] private Image mainIcon;
        [SerializeField] private Image ownerIcon;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField, Required] private GameObject detailsRoot;
        [SerializeField] private GameObject ownerRoot;
        [SerializeField] private Color[] nameBackgroundColors = new Color[5];
        [SerializeField] private Color[] detailBackgroundColors = new Color[5];

        #endregion

        #region Unity 生命周期

        /// <summary>校验详情外壳由 Prefab 直接绑定的必需引用。</summary>
        private void Awake()
        {
            if (nameBackground == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定 NameBK 背景。 ");
            if (detailBackground == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定 DetailBK 背景。 ");
            if (mainIcon == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定主图。 ");
            if (ownerIcon == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定装备者头像。 ");
            if (titleText == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定标题文本。 ");
            if (ownerText == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定装备者文本。 ");
            if (detailsRoot == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定 DetailsRoot。 ");
            if (ownerRoot == null) throw new InvalidOperationException("[BagDetailShellView] 未绑定装备者节点。 ");
        }

        #endregion

        #region 绑定

        /// <summary>绑定一个分类详情快照并刷新主图、品质颜色和装备者区域。</summary>
        /// <param name="details">详情快照。</param>
        public void Bind(BagDetailViewData details)
        {
            if (details == null)
            {
                Clear();
                return;
            }

            // 详情区域是否可见由真实 Prefab 节点控制；图标资源尚未加载时仍保留文字和布局。
            detailsRoot.SetActive(true);
            titleText.text = details.DisplayName;
            mainIcon.sprite = details.Icon;
            ApplyRarityColors(details.Rarity);
            ownerIcon.sprite = details.OwnerIcon;
            ownerText.text = details.OwnerText;
            ownerRoot.SetActive(details.IsEquipped && details.OwnerIcon != null);
        }

        /// <summary>清除旧详情并隐藏真实的详情区域。</summary>
        public void Clear()
        {
            mainIcon.sprite = null;
            ownerIcon.sprite = null;
            titleText.text = string.Empty;
            ownerText.text = string.Empty;
            ownerRoot.SetActive(false);
            // 空分类和无选择状态按产品约定直接不显示，不引入额外 EmptyRoot/EmptyText 节点。
            detailsRoot.SetActive(false);
        }

        /// <summary>按品质切换 NameBK 与 DetailBK 的颜色，不替换两个节点的背景 Sprite。</summary>
        /// <param name="rarity">一至五星品质。</param>
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
