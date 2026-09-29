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
    /// <summary>突破页面的阶数、等级上限、固定消耗和提交状态表现。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖突破页面 Root 内的当前／下一等级 TMP、突破星级 Image、箭头 Image、恰好两条 UpgradeComparisonLine、状态文本、摩拉费用组、按钮和横向素材列表。")]
    public sealed class WeaponAscensionPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text currentLevelText;
        [SerializeField] private TMP_Text nextLevelText;
        [SerializeField] private Image currentRankStars;
        [SerializeField] private Image nextStageArrow;
        [SerializeField] private Image nextRankStars;
        [SerializeField, MinValue(1f)] private float starTileWidth = 17.6f;
        [SerializeField] private GameObject currencyCostRoot;
        [SerializeField] private TMP_Text currencyCostText;
        [SerializeField] private HorizontalBagItemListView requiredMaterialsView;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabelText;

        #endregion

        #region 状态字段

        private EquipmentAttributeUpgradeLineView[] comparisonLines;

        #endregion

        #region 事件

        /// <summary>用户请求执行突破。</summary>
        public event Action ActionRequested;

        #endregion

        #region 生命周期与校验

        /// <summary>校验突破页面引用并注册突破按钮事件。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            // 突破星级使用 Image 的 Tiled 模式，运行时只调整宽度以匹配阶数。
            currentRankStars.type = Image.Type.Tiled;
            currentRankStars.raycastTarget = false;
            nextRankStars.type = Image.Type.Tiled;
            nextRankStars.raycastTarget = false;
            nextStageArrow.raycastTarget = false;
            actionButton.onClick.AddListener(HandleActionClicked);
        }

        /// <summary>校验突破页面的对比行、状态文本、按钮和横向材料列表。</summary>
        public void ValidateConfiguration()
        {
            if (subtitleText == null || currentLevelText == null || nextLevelText == null ||
                currentRankStars == null || nextStageArrow == null || nextRankStars == null ||
                currencyCostRoot == null || currencyCostText == null || requiredMaterialsView == null ||
                actionButton == null || actionLabelText == null)
                throw new InvalidOperationException("[WeaponAscensionPageView] 突破页面存在未绑定控件。");

            // 子 Prefab 提供统一的成长结果视觉；包含 inactive 行以兼容外层页面尚未激活的初始化顺序。
            comparisonLines = GetComponentsInChildren<EquipmentAttributeUpgradeLineView>(true);
            if (comparisonLines.Length != 2)
                throw new InvalidOperationException(
                    $"[WeaponAscensionPageView] 突破页必须包含两条 UpgradeComparisonLine，actual={comparisonLines.Length}。");
            requiredMaterialsView.ValidateConfiguration();
        }

        /// <summary>移除突破按钮监听。</summary>
        private void OnDestroy()
        {
            if (actionButton != null) actionButton.onClick.RemoveListener(HandleActionClicked);
        }

        #endregion

        #region 展示

        /// <summary>绑定突破页面数据并刷新结果对比、所需材料和费用。</summary>
        /// <param name="data">突破页面数据。</param>
        public void Bind(WeaponAscensionViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            subtitleText.text = data.Subtitle;
            subtitleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(data.Subtitle));
            BindHeader(data);
            BindComparisonLines(data);
            currencyCostRoot.SetActive(data.ShowNextStage);
            currencyCostText.text = data.CurrencyCost.ToString("N0");
            currencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color32(0xE3, 0x6B, 0x6B, 0xFF)
                : Color.white;
            actionButton.interactable = data.ActionInteractable;
            actionLabelText.text = data.ActionLabel;

            requiredMaterialsView.gameObject.SetActive(data.ShowNextStage);
            requiredMaterialsView.Bind(data.ShowNextStage ? data.RequiredMaterials : Array.Empty<BagItemViewData>());
        }

        /// <summary>恢复等级与突破星级概览，并在终态隐藏下一阶段部分。</summary>
        /// <param name="data">当前武器突破快照。</param>
        private void BindHeader(WeaponAscensionViewData data)
        {
            currentLevelText.text = $"Lv.{data.CurrentLevel}/{data.CurrentCap}";
            nextLevelText.text = $"Lv.{data.CurrentLevel}/{data.NextCap}";
            nextLevelText.color = new Color32(0xFB, 0xB0, 0x00, 0xFF);
            nextLevelText.gameObject.SetActive(data.ShowNextStage);
            nextStageArrow.gameObject.SetActive(data.ShowNextStage);

            SetStars(currentRankStars, data.CurrentRank);
            SetStars(nextRankStars, data.ShowNextStage ? data.NextRank : 0);
        }

        /// <summary>用共享对比行清楚展示突破阶数和等级上限变化。</summary>
        /// <param name="data">当前武器突破快照。</param>
        private void BindComparisonLines(WeaponAscensionViewData data)
        {
            for (int index = 0; index < comparisonLines.Length; index++)
                comparisonLines[index].gameObject.SetActive(false);
            if (!data.ShowNextStage) return;

            comparisonLines[0].BindComparison("突破阶数", data.CurrentRank.ToString(),
                data.NextRank.ToString(), EquipmentAttributeUpgradeDirection.Increase);
            comparisonLines[1].BindComparison("等级上限", $"Lv.{data.CurrentCap}",
                $"Lv.{data.NextCap}", EquipmentAttributeUpgradeDirection.Increase);
        }

        /// <summary>按阶数调整 Tiled 星级图宽度；零阶时不绘制星星。</summary>
        /// <param name="starImage">突破星级 Image。</param>
        /// <param name="rank">要显示的突破阶数。</param>
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
