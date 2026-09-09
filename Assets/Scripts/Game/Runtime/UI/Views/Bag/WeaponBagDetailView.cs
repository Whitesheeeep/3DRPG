using System;
using RPG.Game.UI.Bag;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace RPG.Game.UI.Views.Bag
{
    /// <summary>武器分类详情内容 View，最多展示两个静态 Add 属性。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖详情面板下的分类、等级、精炼、描述和两个属性文本节点。")]
    public sealed class WeaponBagDetailView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text refinementText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text attributeOneText;
        [SerializeField] private TMP_Text attributeTwoText;

        #endregion

        #region Unity 生命周期

        /// <summary>校验武器详情由 Prefab 直接绑定的文本引用。</summary>
        private void Awake()
        {
            if (categoryText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定分类文本。 ");
            if (levelText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定等级文本。 ");
            if (refinementText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定精炼文本。 ");
            if (descriptionText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定描述文本。 ");
            if (attributeOneText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定第一属性文本。 ");
            if (attributeTwoText == null) throw new InvalidOperationException("[WeaponBagDetailView] 未绑定第二属性文本。 ");
        }

        #endregion

        #region 绑定

        /// <summary>绑定武器详情内容并刷新分类、等级、属性和描述。</summary>
        /// <param name="details">武器详情快照。</param>
        public void Bind(BagDetailViewData details)
        {
            if (details == null)
            {
                Clear();
                return;
            }

            categoryText.text = details.CategoryLabel;
            levelText.text = details.LevelText;
            refinementText.text = details.RefinementText;
            descriptionText.text = details.Description;
            attributeOneText.text = details.Attributes.Count > 0 ? details.Attributes[0] : string.Empty;
            attributeTwoText.text = details.Attributes.Count > 1 ? details.Attributes[1] : string.Empty;
            attributeOneText.gameObject.SetActive(details.Attributes.Count > 0);
            attributeTwoText.gameObject.SetActive(details.Attributes.Count > 1);
        }

        /// <summary>清除详情内容，避免分类切换后残留上一件武器。</summary>
        public void Clear()
        {
            categoryText.text = string.Empty;
            levelText.text = string.Empty;
            refinementText.text = string.Empty;
            descriptionText.text = string.Empty;
            attributeOneText.text = string.Empty;
            attributeTwoText.text = string.Empty;
            attributeOneText.gameObject.SetActive(false);
            attributeTwoText.gameObject.SetActive(false);
        }

        #endregion
    }
}
