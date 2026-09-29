using System;
using RPG.Game.Runtime.CharacterDevelopment;
using RPG.Game.UI.Character;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.Views.WeaponDevelopment;
using RPG.Game.UI.WeaponDevelopment;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>协调角色升级共用页面与角色专用突破页面并转发培养意图。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 AscensionRoot/AscensionComparisonLines 中恰好配置两条 UpgradeComparisonLine Prefab 实例（突破阶数、等级上限），以及显式绑定的共用升级页、只读材料横向列表、摩拉和操作控件；缺少对比行会被视为 Prefab 配置错误。")]
    public sealed class CharacterDevelopmentPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private DevelopmentEnhancementPageView enhancementPageView;
        [SerializeField] private GameObject ascensionRoot;
        [SerializeField] private TMP_Text ascensionTitleText;
        [SerializeField] private TMP_Text ascensionStatusText;
        [SerializeField] private HorizontalBagItemListView requiredMaterialsView;
        [SerializeField] private GameObject ascensionCurrencyCostRoot;
        [SerializeField] private TMP_Text ascensionCurrencyCostText;
        [SerializeField] private Button ascensionActionButton;
        [SerializeField] private TMP_Text ascensionActionButtonText;

        #endregion

        #region 状态字段

        private EquipmentAttributeUpgradeLineView[] ascensionComparisonLines;

        #endregion

        #region 事件

        /// <summary>角色升级或突破提交请求。</summary>
        public event Action SubmitRequested;
        /// <summary>升级页请求自动添加经验素材。</summary>
        public event Action AutoFillRequested;
        /// <summary>请求打开当前培养模式对应的左侧材料选择面板。</summary>
        public event Action SelectMaterialsRequested;

        #endregion

        #region 生命周期与校验

        /// <summary>校验培养页面显式引用并注册子页和突破按钮事件。</summary>
        private void Awake()
        {
            ValidateConfiguration();
            enhancementPageView.SelectMaterialRequested += HandleSelectMaterialsRequested;
            enhancementPageView.AutoAddRequested += HandleAutoFillRequested;
            enhancementPageView.ActionRequested += HandleSubmitRequested;
            ascensionActionButton.onClick.AddListener(HandleSubmitRequested);
            Clear();
            Debug.Log("[CharacterDevelopmentPageView] 角色升级与突破页面初始化完成。", this);
        }

        /// <summary>对称解除页面事件绑定并清理池化材料内容。</summary>
        private void OnDestroy()
        {
            if (enhancementPageView != null)
            {
                enhancementPageView.SelectMaterialRequested -= HandleSelectMaterialsRequested;
                enhancementPageView.AutoAddRequested -= HandleAutoFillRequested;
                enhancementPageView.ActionRequested -= HandleSubmitRequested;
            }
            if (ascensionActionButton != null) ascensionActionButton.onClick.RemoveListener(HandleSubmitRequested);
            ClearAscensionComparisonLines();
            Debug.Log("[CharacterDevelopmentPageView] 角色培养页面事件解绑完成。", this);
        }

        /// <summary>校验角色共用升级页、突破页面和突破材料列表的绑定。</summary>
        public void ValidateConfiguration()
        {
            if (enhancementPageView == null || ascensionRoot == null || ascensionTitleText == null ||
                ascensionStatusText == null || requiredMaterialsView == null || ascensionCurrencyCostRoot == null ||
                ascensionCurrencyCostText == null || ascensionActionButton == null || ascensionActionButtonText == null)
                throw new InvalidOperationException("[CharacterDevelopmentPageView] 角色培养页面序列化引用未绑定完整。");

            // 突破结果固定由两个 Prefab 子项呈现；显式校验避免运行时静默漏掉阶数或等级上限。
            ascensionComparisonLines = ascensionRoot.GetComponentsInChildren<EquipmentAttributeUpgradeLineView>(true);
            if (ascensionComparisonLines.Length != 2)
                throw new InvalidOperationException(
                    $"[CharacterDevelopmentPageView] AscensionComparisonLines 必须包含两条 UpgradeComparisonLine，actual={ascensionComparisonLines.Length}。");

            enhancementPageView.ValidateConfiguration();
            requiredMaterialsView.ValidateConfiguration();
        }

        #endregion

        #region 页面绑定

        /// <summary>根据培养模式显示共用升级页或固定配方突破页。</summary>
        /// <param name="data">当前角色培养页面展示快照。</param>
        public void Bind(CharacterDevelopmentPanelViewData data)
        {
            if (data == null)
            {
                Clear();
                return;
            }

            bool levelUp = data.Mode == CharacterDevelopmentMode.LevelUp;
            enhancementPageView.gameObject.SetActive(levelUp);
            ascensionRoot.SetActive(!levelUp);
            ascensionActionButton.gameObject.SetActive(!levelUp);
            if (levelUp)
            {
                ClearAscensionComparisonLines();
                if (data.EnhancementData == null)
                    throw new InvalidOperationException("[CharacterDevelopmentPageView] 角色升级展示数据缺少共用培养页快照。");
                enhancementPageView.Bind(data.EnhancementData);
                requiredMaterialsView.Bind(Array.Empty<RPG.Game.UI.Bag.BagItemViewData>());
                return;
            }

            ascensionTitleText.text = "角色突破";
            ascensionStatusText.text = data.StatusText;
            ascensionStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(data.StatusText));
            BindAscensionComparisonLines(data);
            requiredMaterialsView.Bind(data.RequiredMaterials);
            ascensionCurrencyCostRoot.SetActive(true);
            ascensionCurrencyCostText.text = data.CurrencyCost.ToString("N0");
            ascensionCurrencyCostText.color = data.CurrencyCost > data.CurrencyOwned
                ? new Color32(0xE3, 0x6B, 0x6B, 0xFF)
                : Color.white;
            ascensionActionButtonText.text = data.ActionLabel;
            ascensionActionButton.interactable = data.ActionInteractable;
        }

        /// <summary>显示培养事务的即时状态说明。</summary>
        /// <param name="message">操作结果或失败原因。</param>
        public void SetStatusMessage(string message)
        {
            if (enhancementPageView.gameObject.activeSelf) enhancementPageView.SetStatusMessage(message);
            else if (ascensionRoot.activeSelf)
            {
                ascensionStatusText.text = message ?? string.Empty;
                ascensionStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
            }
        }

        /// <summary>清理升级和突破快照并归还当前属性与材料池对象。</summary>
        public void Clear()
        {
            if (enhancementPageView != null)
            {
                enhancementPageView.Clear();
                enhancementPageView.gameObject.SetActive(false);
            }
            if (ascensionRoot != null) ascensionRoot.SetActive(false);
            if (ascensionActionButton != null) ascensionActionButton.gameObject.SetActive(false);
            if (ascensionTitleText != null) ascensionTitleText.text = string.Empty;
            if (ascensionStatusText != null) ascensionStatusText.text = string.Empty;
            if (ascensionStatusText != null) ascensionStatusText.gameObject.SetActive(false);
            if (ascensionCurrencyCostText != null) ascensionCurrencyCostText.text = string.Empty;
            if (ascensionActionButton != null) ascensionActionButton.interactable = false;
            ClearAscensionComparisonLines();
            if (requiredMaterialsView != null)
                requiredMaterialsView.Bind(Array.Empty<RPG.Game.UI.Bag.BagItemViewData>());
        }

        #endregion

        #region 用户意图

        /// <summary>使用共用对比行 Prefab 呈现当前突破阶数与等级上限的变化。</summary>
        /// <param name="data">当前角色突破展示快照。</param>
        private void BindAscensionComparisonLines(CharacterDevelopmentPanelViewData data)
        {
            ascensionComparisonLines[0].BindComparison("突破阶数", data.CurrentRank.ToString(),
                data.ProjectedRank.ToString(), EquipmentAttributeUpgradeDirection.Increase);
            ascensionComparisonLines[1].BindComparison("等级上限", $"Lv.{data.CurrentLevelCap}",
                $"Lv.{data.ProjectedLevelCap}", EquipmentAttributeUpgradeDirection.Increase);
            Debug.Log("[CharacterDevelopmentPageView] 突破结果对比行已绑定，rowCount=2。", this);
        }

        /// <summary>隐藏固定的突破对比行，避免关闭或切换页面时显示旧快照。</summary>
        private void ClearAscensionComparisonLines()
        {
            if (ascensionComparisonLines == null) return;
            for (int index = 0; index < ascensionComparisonLines.Length; index++)
                ascensionComparisonLines[index].gameObject.SetActive(false);
        }

        /// <summary>转发角色升级或突破提交请求。</summary>
        private void HandleSubmitRequested()
        {
            WSLog.Log("[CharacterDevelopmentPageView] 用户提交角色升级／突破请求。");
            SubmitRequested?.Invoke();
        }

        /// <summary>转发自动填充经验素材请求。</summary>
        private void HandleAutoFillRequested() => AutoFillRequested?.Invoke();

        /// <summary>转发查看或选择培养材料的请求。</summary>
        private void HandleSelectMaterialsRequested() => SelectMaterialsRequested?.Invoke();

        #endregion
    }
}
