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
    /// <summary>角色五件圣遗物页面 View。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterArtifactPageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private CharacterArtifactSlotItemView[] slotItems = Array.Empty<CharacterArtifactSlotItemView>();
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private GameObject summaryCardRoot;
        [SerializeField] private GameObject detailCardRoot;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private BagItemView itemCardView;
        [SerializeField] private CharacterEquipmentAttributeListView summaryAttributeListView;
        [SerializeField] private CharacterEquipmentAttributeListView detailAttributeListView;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text slotText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text summaryTitleText;
        [SerializeField] private Button replaceButton;
        [SerializeField] private TMP_Text replaceButtonLabel;
        [SerializeField] private Button developmentButton;
        private EquipmentInstanceId currentInstanceId;
        private bool hasEquippedArtifact;
        private bool selectionMode;

        #endregion

        #region 事件

        /// <summary>请求切换选中的圣遗物槽位。</summary>
        public event Action<ArtifactSlot> SlotSelected;
        /// <summary>请求打开当前圣遗物培养窗口。</summary>
        public event Action<EquipmentInstanceId> DevelopmentRequested;
        /// <summary>请求进入圣遗物选择或提交当前预览的候选圣遗物。</summary>
        public event Action ReplaceRequested;

        #endregion

        #region 生命周期

        /// <summary>校验圣遗物页面依赖并注册五个槽位。</summary>
        private void Awake()
        {
            if (slotItems == null || slotItems.Length != 5 || detailsRoot == null ||
                summaryCardRoot == null || detailCardRoot == null || emptyText == null ||
                itemCardView == null || summaryAttributeListView == null || detailAttributeListView == null ||
                nameText == null || slotText == null || levelText == null || descriptionText == null ||
                summaryTitleText == null ||
                replaceButton == null || replaceButtonLabel == null || developmentButton == null)
                throw new InvalidOperationException("[CharacterArtifactPageView] 圣遗物页面绑定不完整。");
            for (int index = 0; index < slotItems.Length; index++)
            {
                if (slotItems[index] == null) throw new InvalidOperationException($"[CharacterArtifactPageView] 槽位 {index} 为空。");
                slotItems[index].Clicked += HandleSlotSelected;
            }
            replaceButton.onClick.AddListener(HandleReplaceClicked);
            developmentButton.onClick.AddListener(HandleDevelopmentClicked);
        }

        /// <summary>注销槽位和按钮事件。</summary>
        private void OnDestroy()
        {
            if (slotItems != null)
                for (int index = 0; index < slotItems.Length; index++)
                    if (slotItems[index] != null) slotItems[index].Clicked -= HandleSlotSelected;
            if (replaceButton != null) replaceButton.onClick.RemoveListener(HandleReplaceClicked);
            if (developmentButton != null) developmentButton.onClick.RemoveListener(HandleDevelopmentClicked);
            ReplaceRequested = null;
            DevelopmentRequested = null;
        }

        #endregion

        #region 绑定

        /// <summary>绑定五个槽位、全套静态属性汇总和当前选中槽位详情。</summary>
        /// <param name="slots">槽位数据。</param>
        /// <param name="summary">五件已装备圣遗物的静态属性汇总。</param>
        /// <param name="details">当前槽位详情。</param>
        public void Bind(IReadOnlyList<CharacterArtifactSlotItemViewData> slots,
            CharacterArtifactSummaryViewData summary, CharacterArtifactViewData details)
        {
            for (int index = 0; index < slotItems.Length; index++)
                if (slots != null && index < slots.Count) slotItems[index].Bind(slots[index]);
                else slotItems[index].Clear();

            // 总览卡片独立于当前选中槽位；空槽仍然允许查看其他部位的已装备属性。
            detailsRoot.SetActive(true);
            summaryCardRoot.SetActive(true);
            summaryTitleText.text = $"已装备总加成（{summary?.EquippedCount ?? 0}/5）";
            summaryAttributeListView.Bind(summary?.AttributeLines);
            bool hasArtifact = details != null && details.HasArtifact;
            detailCardRoot.SetActive(hasArtifact);
            hasEquippedArtifact = hasArtifact;
            summaryTitleText.gameObject.SetActive(true);
            replaceButton.interactable = !selectionMode;
            replaceButtonLabel.text = selectionMode ? GetSlotActionLabel() : (hasArtifact ? "交换" : "装备");
            emptyText.gameObject.SetActive(!hasArtifact);
            emptyText.text = details == null ? "请选择圣遗物部位" : $"{details.SlotName}尚未装备圣遗物";
            developmentButton.interactable = hasArtifact;
            developmentButton.gameObject.SetActive(hasArtifact);
            if (!hasArtifact)
            {
                currentInstanceId = default;
                itemCardView.gameObject.SetActive(false);
                detailAttributeListView.Clear();
                nameText.text = slotText.text = levelText.text = descriptionText.text = string.Empty;
                nameText.gameObject.SetActive(false);
                slotText.gameObject.SetActive(false);
                levelText.gameObject.SetActive(false);
                descriptionText.gameObject.SetActive(false);
                return;
            }

            currentInstanceId = details.InstanceId;
            BindItemCard(details.ItemCardData);
            nameText.gameObject.SetActive(true);
            slotText.gameObject.SetActive(true);
            levelText.gameObject.SetActive(true);
            nameText.text = details.Name;
            slotText.text = $"当前选中 · {details.SlotName}（单件属性）";
            levelText.text = details.LevelText;
            detailAttributeListView.Bind(details.DetailLines);
            descriptionText.text = details.Description;
            descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.Description));
        }

        /// <summary>清空圣遗物页面。</summary>
        public void Clear()
        {
            Bind(Array.Empty<CharacterArtifactSlotItemViewData>(),
                new CharacterArtifactSummaryViewData(0, Array.Empty<CharacterEquipmentAttributeLineViewData>()), null);
        }

        /// <summary>切换圣遗物页面的候选选择表现，并按当前槽位状态设置操作文案。</summary>
        /// <param name="selecting">是否正在选择圣遗物。</param>
        public void SetSelectionMode(bool selecting)
        {
            selectionMode = selecting;
            replaceButtonLabel.text = selecting ? GetSlotActionLabel() : (hasEquippedArtifact ? "交换" : "装备");
            replaceButton.interactable = !selecting;
            if (!selecting) developmentButton.interactable = hasEquippedArtifact;
        }

        /// <summary>将当前槽位详情替换为背包候选预览，且不改变装备关系。</summary>
        /// <param name="itemData">候选物品卡显示数据。</param>
        /// <param name="details">候选圣遗物详情；为空时显示选择提示。</param>
        /// <param name="attributeLines">候选圣遗物结构化属性行。</param>
        /// <param name="canEquip">当前是否允许装备该候选。</param>
        /// <param name="statusText">不能装备时的原因；可装备时为空。</param>
        public void BindCandidate(BagItemViewData itemData, BagDetailViewData details,
            IReadOnlyList<CharacterEquipmentAttributeLineViewData> attributeLines, bool canEquip,
            string statusText)
        {
            selectionMode = true;
            detailsRoot.SetActive(true);
            summaryCardRoot.SetActive(false);
            detailCardRoot.SetActive(true);
            summaryTitleText.gameObject.SetActive(false);
            summaryAttributeListView.Clear();
            bool hasCandidate = details != null;
            currentInstanceId = hasCandidate ? ParseInstanceId(details.EntryKey.Value) : default;
            emptyText.gameObject.SetActive(false);
            BindItemCard(hasCandidate ? itemData : null);
            nameText.gameObject.SetActive(true);
            slotText.gameObject.SetActive(true);
            levelText.gameObject.SetActive(true);
            nameText.text = hasCandidate ? details.DisplayName : "请选择圣遗物";
            slotText.text = hasCandidate
                ? $"候选圣遗物属性 · {details.CategoryText}"
                : "候选圣遗物属性";
            levelText.text = hasCandidate ? details.PrimaryText : string.Empty;
            detailAttributeListView.Bind(hasCandidate ? attributeLines : Array.Empty<CharacterEquipmentAttributeLineViewData>());
            descriptionText.text = hasCandidate
                ? JoinStatus(details.Description, statusText)
                : string.IsNullOrWhiteSpace(statusText) ? "从左侧列表选择当前部位的圣遗物进行预览。" : statusText;
            descriptionText.gameObject.SetActive(true);
            replaceButtonLabel.text = GetSlotActionLabel();
            replaceButton.interactable = hasCandidate && canEquip;
            developmentButton.gameObject.SetActive(hasCandidate && details.ShowDetailsAction);
            developmentButton.interactable = hasCandidate && details.ShowDetailsAction;
        }

        #endregion

        #region 内部事件

        /// <summary>使用背包统一物品卡渲染圣遗物图标、品质与等级，禁用卡片点击。</summary>
        /// <param name="data">圣遗物物品卡数据；为空时隐藏。</param>
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

        /// <summary>转发圣遗物槽位选择。</summary>
        /// <param name="slot">选中的部位。</param>
        private void HandleSlotSelected(ArtifactSlot slot)
        {
            SlotSelected?.Invoke(slot);
        }

        /// <summary>将按钮请求转交 Controller，由其区分进入选择与提交装备。</summary>
        private void HandleReplaceClicked() => ReplaceRequested?.Invoke();

        /// <summary>转发圣遗物培养意图。</summary>
        private void HandleDevelopmentClicked()
        {
            if (currentInstanceId.IsValid) DevelopmentRequested?.Invoke(currentInstanceId);
        }

        /// <summary>按当前圣遗物目标槽是否已有物品选择“装备”或“交换”文案。</summary>
        private string GetSlotActionLabel() => hasEquippedArtifact ? "交换" : "装备";

        /// <summary>组合原始描述和候选装备状态说明。</summary>
        /// <param name="description">物品描述。</param>
        /// <param name="statusText">装备限制说明。</param>
        /// <returns>供详情区显示的说明文本。</returns>
        private static string JoinStatus(string description, string statusText) =>
            string.IsNullOrWhiteSpace(statusText) ? description :
                string.IsNullOrWhiteSpace(description) ? statusText : $"{description}\n{statusText}";

        /// <summary>从圣遗物候选条目键读取稳定实例标识。</summary>
        /// <param name="value">条目键文本。</param>
        /// <returns>实例标识。</returns>
        private static EquipmentInstanceId ParseInstanceId(string value) =>
            string.IsNullOrWhiteSpace(value) ? default : new EquipmentInstanceId(value);

        #endregion
    }
}
