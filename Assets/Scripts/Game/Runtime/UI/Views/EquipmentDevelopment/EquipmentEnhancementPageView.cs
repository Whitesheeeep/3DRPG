using System;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.WeaponDevelopment
{
    /// <summary>装备升级页面的等级、经验、属性、已选素材和费用表现。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖升级页面 Root 内的等级经验文本、细型经验 Slider、属性文本、横向素材列表、费用区域和按钮。")]
    public sealed class EquipmentEnhancementPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text linesText;
        [SerializeField] private Slider experienceSlider;
        [SerializeField] private HorizontalBagItemListView selectedMaterialsView;
        [SerializeField] private GameObject currencyCostRoot;
        [SerializeField] private TMP_Text currencyCostText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabelText;
        [SerializeField] private Button selectMaterialButton;
        [SerializeField] private Button autoAddButton;
        [SerializeField] private TMP_Text selectMaterialLabelText;
        [SerializeField] private TMP_Text autoAddLabelText;

        #endregion

        #region 事件

        /// <summary>用户请求打开升级素材选择面板。</summary>
        public event Action SelectMaterialRequested;
        /// <summary>用户请求自动添加升级素材。</summary>
        public event Action AutoAddRequested;
        /// <summary>用户请求执行升级。</summary>
        public event Action ActionRequested;

        #endregion

        #region 生命周期与校验

        /// <summary>校验页面内部的显式序列化依赖并初始化只读经验条。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            experienceSlider.interactable = false;
            experienceSlider.minValue = 0f;
            experienceSlider.maxValue = 1f;
            selectMaterialButton.onClick.AddListener(HandleSelectMaterialClicked);
            autoAddButton.onClick.AddListener(HandleAutoAddClicked);
            actionButton.onClick.AddListener(HandleActionClicked);
        }

        /// <summary>移除页面按钮监听。</summary>
        private void OnDestroy()
        {
            if (selectMaterialButton != null) selectMaterialButton.onClick.RemoveListener(HandleSelectMaterialClicked);
            if (autoAddButton != null) autoAddButton.onClick.RemoveListener(HandleAutoAddClicked);
            if (actionButton != null) actionButton.onClick.RemoveListener(HandleActionClicked);
        }

        /// <summary>校验升级页面的文本、经验条、费用区域、按钮和横向材料列表。</summary>
        public void ValidateConfiguration()
        {
            if (titleText == null || subtitleText == null || linesText == null || experienceSlider == null ||
                selectedMaterialsView == null || currencyCostRoot == null || currencyCostText == null ||
                actionButton == null || actionLabelText == null || selectMaterialButton == null ||
                autoAddButton == null || selectMaterialLabelText == null || autoAddLabelText == null)
                throw new InvalidOperationException("[EquipmentEnhancementPageView] 升级页面存在未绑定控件。");
            selectedMaterialsView.ValidateConfiguration();
        }

        #endregion

        #region 展示

        /// <summary>绑定升级页面数据并刷新经验、属性、材料和独立费用区域。</summary>
        /// <param name="data">升级页面数据。</param>
        public void Bind(EquipmentEnhancementViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            titleText.text = $"Lv.{data.CurrentLevel}";
            subtitleText.text = $"+{Math.Max(0, data.ProjectedLevel - data.CurrentLevel)}    +{data.SelectedExperience} EXP    " +
                                FormatExperienceText(data);
            linesText.text = string.Join("\n", data.Lines);
            experienceSlider.SetValueWithoutNotify(data.Progress);
            selectedMaterialsView.Bind(data.SelectedMaterials);

            currencyCostRoot.SetActive(true);
            currencyCostText.text = data.CurrencyCost.ToString("N0");
            currencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color(0.89f, 0.42f, 0.42f, 1f)
                : Color.white;

            actionButton.interactable = data.ActionInteractable;
            actionLabelText.text = data.ActionLabel;
            selectMaterialButton.gameObject.SetActive(true);
            selectMaterialButton.interactable = data.SelectMaterialInteractable;
            autoAddButton.gameObject.SetActive(true);
            autoAddButton.interactable = data.AutoAddInteractable;
            selectMaterialLabelText.text = "选择素材";
            autoAddLabelText.text = "自动添加";
        }

        /// <summary>格式化预计等级内经验；达到阶段上限时不显示除数零。</summary>
        /// <param name="data">升级页面数据。</param>
        /// <returns>等级内经验文本。</returns>
        private static string FormatExperienceText(EquipmentEnhancementViewData data)
        {
            return data.ProjectedNextExperience <= 0
                ? "已达阶段上限"
                : $"{data.ProjectedExperience}/{data.ProjectedNextExperience}";
        }

        #endregion

        #region 用户意图

        /// <summary>转发打开升级素材面板意图。</summary>
        private void HandleSelectMaterialClicked() => SelectMaterialRequested?.Invoke();

        /// <summary>转发自动添加升级素材意图。</summary>
        private void HandleAutoAddClicked() => AutoAddRequested?.Invoke();

        /// <summary>转发升级动作意图。</summary>
        private void HandleActionClicked() => ActionRequested?.Invoke();

        #endregion
    }
}
