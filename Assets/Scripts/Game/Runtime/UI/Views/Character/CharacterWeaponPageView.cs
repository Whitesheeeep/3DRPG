using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Character;
using RPG.Game.UI.Views.Bag;
using RPG.ItemSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色已装备武器页面 View。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterWeaponPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private GameObject emptyRoot;
        [SerializeField] private BagItemView itemCardView;
        [SerializeField] private CharacterEquipmentAttributeListView attributeListView;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text refinementText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Button replaceButton;
        [SerializeField] private TMP_Text replaceButtonLabel;
        [SerializeField] private Button developmentButton;
        private EquipmentInstanceId currentInstanceId;
        private bool hasEquippedWeapon;
        private bool selectionMode;

        #endregion

        #region 事件

        /// <summary>请求打开当前武器培养窗口。</summary>
        public event Action<EquipmentInstanceId> DevelopmentRequested;
        /// <summary>请求进入武器选择或提交当前预览的候选武器。</summary>
        public event Action ReplaceRequested;

        #endregion

        #region 生命周期

        /// <summary>校验武器页面的显式依赖并注册换装、培养按钮。</summary>
        private void Awake()
        {
            if (detailsRoot == null || emptyRoot == null || itemCardView == null || attributeListView == null ||
                nameText == null || typeText == null || levelText == null || refinementText == null ||
                descriptionText == null || replaceButton == null ||
                replaceButtonLabel == null || developmentButton == null)
                throw new InvalidOperationException("[CharacterWeaponPageView] 武器页面绑定不完整。");
            replaceButton.onClick.AddListener(HandleReplaceClicked);
            developmentButton.onClick.AddListener(HandleDevelopmentClicked);
        }

        /// <summary>注销按钮监听。</summary>
        private void OnDestroy()
        {
            if (replaceButton != null) replaceButton.onClick.RemoveListener(HandleReplaceClicked);
            if (developmentButton != null) developmentButton.onClick.RemoveListener(HandleDevelopmentClicked);
            ReplaceRequested = null;
            DevelopmentRequested = null;
        }

        #endregion

        #region 绑定

        /// <summary>绑定已装备武器或显示空槽。</summary>
        /// <param name="data">武器显示数据。</param>
        public void Bind(CharacterWeaponViewData data)
        {
            bool hasWeapon = data != null && data.HasWeapon;
            hasEquippedWeapon = hasWeapon;
            // 详情容器同时承载空槽“装备”入口，因此即使没有武器也保持可交互。
            detailsRoot.SetActive(true);
            emptyRoot.SetActive(!hasWeapon);
            replaceButton.interactable = !selectionMode;
            replaceButtonLabel.text = selectionMode ? GetSlotActionLabel() : (hasWeapon ? "交换" : "装备");
            developmentButton.interactable = hasWeapon;
            developmentButton.gameObject.SetActive(hasWeapon);
            if (!hasWeapon)
            {
                currentInstanceId = default;
                itemCardView.gameObject.SetActive(false);
                attributeListView.Clear();
                nameText.text = typeText.text = levelText.text = refinementText.text =
                    descriptionText.text = string.Empty;
                descriptionText.gameObject.SetActive(false);
                return;
            }

            currentInstanceId = data.InstanceId;
            BindItemCard(data.ItemCardData);
            nameText.text = data.Name;
            typeText.text = data.Type;
            levelText.text = data.LevelText;
            refinementText.text = data.RefinementText;
            attributeListView.Bind(data.DetailLines);
            descriptionText.text = string.IsNullOrWhiteSpace(data.Description) ? "暂无介绍" : data.Description;
            descriptionText.gameObject.SetActive(true);
        }

        /// <summary>清空武器页面。</summary>
        public void Clear()
        {
            Bind(null);
        }

        /// <summary>切换武器页面的候选选择表现，选择中只有确认按钮由有效候选启用。</summary>
        /// <param name="selecting">是否正在选择武器。</param>
        public void SetSelectionMode(bool selecting)
        {
            selectionMode = selecting;
            replaceButtonLabel.text = selecting ? GetSlotActionLabel() : (hasEquippedWeapon ? "交换" : "装备");
            replaceButton.interactable = !selecting;
            if (!selecting) developmentButton.interactable = hasEquippedWeapon;
        }

        /// <summary>用所选背包武器覆盖右侧详情，并显示明确的装备可用状态。</summary>
        /// <param name="details">候选武器背包详情。</param>
        /// <param name="canEquip">当前是否允许装备。</param>
        /// <param name="statusText">不能装备时的原因；可装备时为空。</param>
        public void BindCandidate(BagItemViewData itemData, BagDetailViewData details,
            IReadOnlyList<CharacterEquipmentAttributeLineViewData> attributeLines, bool canEquip,
            string statusText)
        {
            selectionMode = true;
            detailsRoot.SetActive(true);
            emptyRoot.SetActive(false);
            bool hasCandidate = details != null;
            currentInstanceId = hasCandidate ? ParseInstanceId(details.EntryKey.Value) : default;
            BindItemCard(hasCandidate ? itemData : null);
            nameText.text = hasCandidate ? details.DisplayName : "请选择武器";
            typeText.text = hasCandidate ? details.CategoryText : string.Empty;
            levelText.text = hasCandidate ? details.PrimaryText : string.Empty;
            refinementText.text = hasCandidate ? details.SecondaryText : string.Empty;
            attributeListView.Bind(hasCandidate ? attributeLines : Array.Empty<CharacterEquipmentAttributeLineViewData>());
            descriptionText.text = hasCandidate
                ? JoinStatus(string.IsNullOrWhiteSpace(details.Description) ? "暂无介绍" : details.Description, statusText)
                : string.IsNullOrWhiteSpace(statusText) ? "从左侧列表选择一把武器进行预览。" : statusText;
            descriptionText.gameObject.SetActive(true);
            replaceButtonLabel.text = GetSlotActionLabel();
            replaceButton.interactable = hasCandidate && canEquip;
            developmentButton.gameObject.SetActive(hasCandidate && details.ShowDetailsAction);
            developmentButton.interactable = hasCandidate && details.ShowDetailsAction;
        }

        #endregion

        #region 内部事件

        /// <summary>使用背包统一物品卡渲染图标、品质和等级，并关闭详情卡点击。</summary>
        /// <param name="data">装备物品卡数据；为空时隐藏卡片。</param>
        private void BindItemCard(BagItemViewData data)
        {
            if (data == null)
            {
                itemCardView.gameObject.SetActive(false);
                return;
            }
            itemCardView.gameObject.SetActive(true);
            itemCardView.Bind(data, null);
            itemCardView.SetInteractable(false);
        }

        /// <summary>将按钮请求转交 Controller，由其区分进入选择与提交装备。</summary>
        private void HandleReplaceClicked() => ReplaceRequested?.Invoke();

        /// <summary>转发武器培养意图。</summary>
        private void HandleDevelopmentClicked()
        {
            if (currentInstanceId.IsValid) DevelopmentRequested?.Invoke(currentInstanceId);
        }

        /// <summary>按目标槽位当前状态返回“装备”或“交换”。</summary>
        private string GetSlotActionLabel() => hasEquippedWeapon ? "交换" : "装备";

        /// <summary>组合原始描述和候选装备状态说明。</summary>
        /// <param name="description">物品描述。</param>
        /// <param name="statusText">装备限制说明。</param>
        /// <returns>供详情区显示的说明文本。</returns>
        private static string JoinStatus(string description, string statusText) =>
            string.IsNullOrWhiteSpace(statusText) ? description :
                string.IsNullOrWhiteSpace(description) ? statusText : $"{description}\n{statusText}";

        /// <summary>从武器候选条目键读取稳定实例标识。</summary>
        /// <param name="value">条目键文本。</param>
        /// <returns>实例标识。</returns>
        private static EquipmentInstanceId ParseInstanceId(string value) =>
            string.IsNullOrWhiteSpace(value) ? default : new EquipmentInstanceId(value);

        #endregion
    }
}
