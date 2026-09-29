using System;
using RPG.Game.UI.Common;
using RPG.Game.UI.Views.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Common
{
    /// <summary>显示角色、武器或圣遗物共用的等级培养界面并转发操作意图。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖页面内的等级文本、只读经验 Slider、属性对比列表、横向素材列表及其同节点透明 Button、货币区和操作按钮。")]
    public sealed class DevelopmentEnhancementPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private EquipmentAttributeUpgradeListView attributeUpgradeListView;
        [SerializeField] private Slider experienceSlider;
        [SerializeField] private HorizontalBagItemListView selectedMaterialsView;
        private Button selectedMaterialsButton;
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

        /// <summary>用户请求打开培养素材选择面板。</summary>
        public event Action SelectMaterialRequested;
        /// <summary>用户请求自动添加培养素材。</summary>
        public event Action AutoAddRequested;
        /// <summary>用户请求执行等级提升。</summary>
        public event Action ActionRequested;

        #endregion

        #region 生命周期与配置

        /// <summary>校验序列化依赖并注册页面按钮事件。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            experienceSlider.interactable = false;
            experienceSlider.minValue = 0f;
            experienceSlider.maxValue = 1f;
            selectMaterialButton.onClick.AddListener(HandleSelectMaterialClicked);
            selectedMaterialsButton.onClick.AddListener(HandleSelectMaterialClicked);
            autoAddButton.onClick.AddListener(HandleAutoAddClicked);
            actionButton.onClick.AddListener(HandleActionClicked);
            Clear();
            Debug.Log("[DevelopmentEnhancementPageView] 共用等级培养页初始化并注册交互。", this);
        }

        /// <summary>移除页面事件并归还属性行池对象。</summary>
        private void OnDestroy()
        {
            if (selectMaterialButton != null) selectMaterialButton.onClick.RemoveListener(HandleSelectMaterialClicked);
            if (selectedMaterialsButton != null)
                selectedMaterialsButton.onClick.RemoveListener(HandleSelectMaterialClicked);
            if (autoAddButton != null) autoAddButton.onClick.RemoveListener(HandleAutoAddClicked);
            if (actionButton != null) actionButton.onClick.RemoveListener(HandleActionClicked);
            attributeUpgradeListView?.Clear();
            Debug.Log("[DevelopmentEnhancementPageView] 共用等级培养页解绑并清理池化内容。", this);
        }

        /// <summary>校验升级页的显式引用，避免窗口启动后才发现 Prefab 未配置。</summary>
        public void ValidateConfiguration()
        {
            // 素材列表卡片只作为打开选择面板的入口，数量变更仍由左侧选择面板处理。
            if (selectedMaterialsButton == null && selectedMaterialsView != null)
                selectedMaterialsButton = selectedMaterialsView.GetComponent<Button>();
            if (titleText == null || subtitleText == null || attributeUpgradeListView == null || experienceSlider == null ||
                selectedMaterialsView == null || selectedMaterialsButton == null || currencyCostRoot == null || currencyCostText == null ||
                actionButton == null || actionLabelText == null || selectMaterialButton == null || autoAddButton == null ||
                selectMaterialLabelText == null || autoAddLabelText == null)
                throw new InvalidOperationException("[DevelopmentEnhancementPageView] 共用等级培养页存在未绑定控件。");
            selectedMaterialsView.ValidateConfiguration();
            attributeUpgradeListView.ValidateConfiguration();
        }

        #endregion

        #region 展示数据

        /// <summary>绑定培养预览并同步进度、属性、素材、费用和可操作状态。</summary>
        /// <param name="data">共用培养页面展示快照。</param>
        public void Bind(DevelopmentEnhancementViewData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            titleText.text = $"Lv.{data.CurrentLevel}";
            subtitleText.text = FormatSubtitle(data);
            attributeUpgradeListView.Bind(data.AttributeLines);
            // 每次预览都由业务投影提供归一化值，避免 Slider 的交互或旧值反向污染数据。
            experienceSlider.SetValueWithoutNotify(data.Progress);
            selectedMaterialsView.Bind(data.SelectedMaterials);

            currencyCostRoot.SetActive(true);
            currencyCostText.text = data.CurrencyCost.ToString("N0");
            currencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color(0.89f, 0.42f, 0.42f, 1f)
                : Color.white;

            actionLabelText.text = data.ActionLabel;
            actionButton.interactable = data.ActionInteractable;
            selectMaterialButton.interactable = data.SelectMaterialInteractable;
            autoAddButton.interactable = data.AutoAddInteractable;
            selectMaterialLabelText.text = "选择素材";
            autoAddLabelText.text = "自动添加";
        }

        /// <summary>清空页面文字、进度、素材和属性池对象，避免复用窗口时显示旧数据。</summary>
        public void Clear()
        {
            if (titleText != null) titleText.text = string.Empty;
            if (subtitleText != null) subtitleText.text = string.Empty;
            if (experienceSlider != null) experienceSlider.SetValueWithoutNotify(0f);
            if (attributeUpgradeListView != null) attributeUpgradeListView.Clear();
            if (selectedMaterialsView != null) selectedMaterialsView.Bind(Array.Empty<RPG.Game.UI.Bag.BagItemViewData>());
            if (currencyCostText != null) currencyCostText.text = string.Empty;
            if (currencyCostRoot != null) currencyCostRoot.SetActive(false);
            if (actionButton != null) actionButton.interactable = false;
            if (selectMaterialButton != null) selectMaterialButton.interactable = false;
            if (autoAddButton != null) autoAddButton.interactable = false;
        }

        /// <summary>更新副标题区域以反馈自动添加或提交失败结果。</summary>
        /// <param name="message">需要显示的状态文字。</param>
        public void SetStatusMessage(string message)
        {
            subtitleText.text = message ?? string.Empty;
        }

        /// <summary>将等级、经验和状态组合成独立的副标题文案。</summary>
        /// <param name="data">培养页面展示快照。</param>
        /// <returns>适合紧凑培养栏的单行状态。</returns>
        private static string FormatSubtitle(DevelopmentEnhancementViewData data)
        {
            string experienceText = data.ProjectedNextExperience <= 0
                ? "已达阶段上限"
                : $"{data.ProjectedExperience:N0}/{data.ProjectedNextExperience:N0} EXP";
            string levelDelta = data.ProjectedLevel > data.CurrentLevel
                ? $"Lv.{data.CurrentLevel} → Lv.{data.ProjectedLevel}"
                : $"Lv.{data.CurrentLevel}";
            string previewText = $"{levelDelta}    +{data.SelectedExperience:N0} EXP    {experienceText}";
            if (string.IsNullOrWhiteSpace(data.Subtitle) ||
                data.Subtitle.StartsWith("使用", StringComparison.Ordinal))
                return previewText;

            return $"{previewText}    ·    {data.Subtitle}";
        }

        #endregion

        #region 用户意图

        /// <summary>转发打开培养素材选择面板的用户请求。</summary>
        private void HandleSelectMaterialClicked() => SelectMaterialRequested?.Invoke();

        /// <summary>转发自动添加培养素材的用户请求。</summary>
        private void HandleAutoAddClicked() => AutoAddRequested?.Invoke();

        /// <summary>转发等级提升确认请求。</summary>
        private void HandleActionClicked() => ActionRequested?.Invoke();

        #endregion
    }
}
