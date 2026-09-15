using System;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>精炼页面的阶数、效果、已选材料、费用和动作表现。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖精炼页面 Root 内的 Rank 文本、箭头、效果文本、横向材料列表、数量文本、费用区域和按钮。")]
    public sealed class WeaponRefinementPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text currentRankText;
        [SerializeField] private TMP_Text nextRankText;
        [SerializeField] private Image nextRankArrow;
        [SerializeField] private TMP_Text effectComparisonText;
        [SerializeField] private HorizontalBagItemListView selectedMaterialsView;
        [SerializeField] private GameObject currencyCostRoot;
        [SerializeField] private TMP_Text currencyCostText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabelText;
        [SerializeField] private Button addMaterialButton;

        #endregion

        #region 事件

        /// <summary>用户请求打开精炼材料选择面板。</summary>
        public event Action AddMaterialRequested;

        #endregion

        #region 生命周期与校验

        /// <summary>校验精炼页面依赖并注册添加材料按钮。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            addMaterialButton.onClick.AddListener(HandleAddMaterialClicked);
        }

        /// <summary>移除添加材料按钮监听。</summary>
        private void OnDestroy()
        {
            if (addMaterialButton != null) addMaterialButton.onClick.RemoveListener(HandleAddMaterialClicked);
        }

        /// <summary>校验精炼页面的 Rank、效果、材料、费用和按钮绑定。</summary>
        public void ValidateConfiguration()
        {
            if (titleText == null || subtitleText == null || currentRankText == null || nextRankText == null ||
                nextRankArrow == null || effectComparisonText == null || selectedMaterialsView == null ||
                 currencyCostRoot == null || currencyCostText == null ||
                actionButton == null || actionLabelText == null || addMaterialButton == null)
                throw new InvalidOperationException("[WeaponRefinementPageView] 精炼页面存在未绑定控件。");
            selectedMaterialsView.ValidateConfiguration();
        }

        #endregion

        #region 展示

        /// <summary>绑定精炼页面数据并刷新阶数、效果、材料和费用。</summary>
        /// <param name="data">精炼页面数据。</param>
        public void Bind(WeaponRefinementViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            titleText.text = data.Title;
            subtitleText.text = data.Subtitle;
            currentRankText.text = $"R{data.CurrentRank}";
            nextRankText.text = data.ShowNextRank ? $"R{data.NextRank}" : string.Empty;
            nextRankText.gameObject.SetActive(data.ShowNextRank);
            nextRankArrow.gameObject.SetActive(data.ShowNextRank);
            effectComparisonText.text = string.Join("\n", data.EffectComparisonLines);
            selectedMaterialsView.gameObject.SetActive(data.ShowNextRank);
            selectedMaterialsView.Bind(data.SelectedMaterials);

            currencyCostRoot.SetActive(data.ShowNextRank);
            currencyCostText.text = data.CurrencyCost.ToString("N0");
            currencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color(0.89f, 0.42f, 0.42f, 1f)
                : Color.white;
            addMaterialButton.gameObject.SetActive(data.ShowNextRank);
            addMaterialButton.interactable = data.AddMaterialInteractable;
            actionButton.interactable = data.ActionInteractable;
            actionLabelText.text = data.ActionLabel;
        }

        #endregion

        #region 用户意图

        /// <summary>转发添加精炼材料意图。</summary>
        private void HandleAddMaterialClicked() => AddMaterialRequested?.Invoke();

        #endregion
    }
}
