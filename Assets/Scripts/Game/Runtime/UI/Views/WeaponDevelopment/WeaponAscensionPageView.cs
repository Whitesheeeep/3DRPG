using System;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>突破页面的阶数星星、等级上限、属性和所需素材表现。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖突破页面 Root 内的两组 Tiled 星星、当前与下一等级文本、箭头、属性文本、独立摩拉费用组、按钮和横向素材列表。")]
    public sealed class WeaponAscensionPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text currentLevelText;
        [SerializeField] private TMP_Text nextLevelText;
        [SerializeField] private TMP_Text linesText;
        [SerializeField] private GameObject currencyCostRoot;
        [SerializeField] private TMP_Text currencyCostText;
        [SerializeField] private Image currentRankStars;
        [SerializeField] private Image nextStageArrow;
        [SerializeField] private Image nextRankStars;
        [SerializeField] private HorizontalBagItemListView requiredMaterialsView;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabelText;
        [SerializeField, MinValue(1f)] private float starTileWidth = 17.6f;

        #endregion

        #region 事件

        /// <summary>用户请求执行突破。</summary>
        public event Action ActionRequested;

        #endregion

        #region 生命周期与校验

        /// <summary>校验突破页面内部的显式绑定并准备星星 Image。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            currentRankStars.type = Image.Type.Tiled;
            nextRankStars.type = Image.Type.Tiled;
            currentRankStars.raycastTarget = false;
            nextRankStars.raycastTarget = false;
            nextStageArrow.raycastTarget = false;
            actionButton.onClick.AddListener(HandleActionClicked);
        }

        /// <summary>校验突破页面的星星、文本、按钮和横向材料列表。</summary>
        public void ValidateConfiguration()
        {
            if (subtitleText == null || currentLevelText == null || nextLevelText == null || linesText == null ||
                currencyCostRoot == null || currencyCostText == null || currentRankStars == null ||
                nextStageArrow == null || nextRankStars == null || requiredMaterialsView == null ||
                actionButton == null || actionLabelText == null)
                throw new InvalidOperationException("[WeaponAscensionPageView] 突破页面存在未绑定控件。");
            requiredMaterialsView.ValidateConfiguration();
        }

        /// <summary>移除突破按钮监听。</summary>
        private void OnDestroy()
        {
            if (actionButton != null) actionButton.onClick.RemoveListener(HandleActionClicked);
        }

        #endregion

        #region 展示

        /// <summary>绑定突破页面数据并刷新星级、等级上限和所需材料。</summary>
        /// <param name="data">突破页面数据。</param>
        public void Bind(WeaponAscensionViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            subtitleText.text = data.Subtitle;
            currentLevelText.text = $"Lv.{data.CurrentLevel}/{data.CurrentCap}";
            nextLevelText.text = $"Lv.{data.CurrentLevel}/{data.NextCap}";
            nextLevelText.color = new Color32(0xFB, 0xB0, 0x00, 0xFF);
            nextLevelText.gameObject.SetActive(data.ShowNextStage);
            nextStageArrow.gameObject.SetActive(data.ShowNextStage);
            linesText.text = string.Join("\n", data.Lines);
            currencyCostRoot.SetActive(data.ShowNextStage);
            currencyCostText.text = data.CurrencyCost.ToString("N0");
            currencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color32(0xE3, 0x6B, 0x6B, 0xFF)
                : Color.white;
            actionButton.interactable = data.ActionInteractable;
            actionLabelText.text = data.ActionLabel;

            SetStars(currentRankStars, data.CurrentRank);
            SetStars(nextRankStars, data.ShowNextStage ? data.NextRank : 0);
            requiredMaterialsView.gameObject.SetActive(data.ShowNextStage);
            requiredMaterialsView.Bind(data.ShowNextStage ? data.RequiredMaterials : Array.Empty<BagItemViewData>());
        }

        /// <summary>按突破阶数设置 Tiled 星星的宽度和可见状态。</summary>
        /// <param name="starImage">目标星星 Image。</param>
        /// <param name="rank">突破阶数。</param>
        private void SetStars(Image starImage, int rank)
        {
            RectTransform rectTransform = starImage.rectTransform;
            rectTransform.sizeDelta = new Vector2(Mathf.Max(0, rank) * starTileWidth, rectTransform.sizeDelta.y);
            starImage.enabled = rank > 0;
        }

        /// <summary>转发突破动作意图。</summary>
        private void HandleActionClicked() => ActionRequested?.Invoke();

        #endregion
    }
}
