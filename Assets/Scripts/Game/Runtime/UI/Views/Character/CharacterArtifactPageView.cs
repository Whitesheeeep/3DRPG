using System;
using System.Collections.Generic;
using RPG.Game.UI.Character;
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
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private Image artifactIcon;
        [SerializeField] private Image rarityStars;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text slotText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text detailLinesText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text summaryTitleText;
        [SerializeField] private TMP_Text summaryLinesText;
        [SerializeField] private Button replaceButton;
        [SerializeField] private Button developmentButton;
        private EquipmentInstanceId currentInstanceId;

        #endregion

        #region 事件

        /// <summary>请求切换选中的圣遗物槽位。</summary>
        public event Action<ArtifactSlot> SlotSelected;
        /// <summary>请求打开当前圣遗物培养窗口。</summary>
        public event Action<EquipmentInstanceId> DevelopmentRequested;

        #endregion

        #region 生命周期

        /// <summary>校验圣遗物页面依赖并注册五个槽位。</summary>
        private void Awake()
        {
            if (slotItems == null || slotItems.Length != 5 || detailsRoot == null || emptyText == null ||
                artifactIcon == null || rarityStars == null || nameText == null || slotText == null ||
                levelText == null || detailLinesText == null || descriptionText == null ||
                summaryTitleText == null || summaryLinesText == null ||
                replaceButton == null || developmentButton == null)
                throw new InvalidOperationException("[CharacterArtifactPageView] 圣遗物页面绑定不完整。");
            for (int index = 0; index < slotItems.Length; index++)
            {
                if (slotItems[index] == null) throw new InvalidOperationException($"[CharacterArtifactPageView] 槽位 {index} 为空。");
                slotItems[index].Clicked += HandleSlotSelected;
            }
            replaceButton.interactable = false;
            replaceButton.onClick.AddListener(HandleReplaceClicked);
            developmentButton.onClick.AddListener(HandleDevelopmentClicked);
            rarityStars.type = Image.Type.Tiled;
            rarityStars.raycastTarget = false;
        }

        /// <summary>注销槽位和按钮事件。</summary>
        private void OnDestroy()
        {
            if (slotItems != null)
                for (int index = 0; index < slotItems.Length; index++)
                    if (slotItems[index] != null) slotItems[index].Clicked -= HandleSlotSelected;
            if (replaceButton != null) replaceButton.onClick.RemoveListener(HandleReplaceClicked);
            if (developmentButton != null) developmentButton.onClick.RemoveListener(HandleDevelopmentClicked);
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
            summaryTitleText.text = "圣遗物属性总览";
            summaryLinesText.text = summary != null && summary.AttributeLines.Count > 0
                ? string.Join("\n", summary.AttributeLines)
                : "暂无已装备圣遗物属性";
            bool hasArtifact = details != null && details.HasArtifact;
            emptyText.gameObject.SetActive(!hasArtifact);
            emptyText.text = details == null ? "请选择圣遗物部位" : $"{details.SlotName}尚未装备圣遗物";
            developmentButton.interactable = hasArtifact;
            developmentButton.gameObject.SetActive(hasArtifact);
            if (!hasArtifact)
            {
                currentInstanceId = default;
                artifactIcon.sprite = null;
                artifactIcon.enabled = false;
                artifactIcon.gameObject.SetActive(false);
                rarityStars.gameObject.SetActive(false);
                nameText.text = slotText.text = levelText.text = detailLinesText.text = descriptionText.text = string.Empty;
                nameText.gameObject.SetActive(false);
                slotText.gameObject.SetActive(false);
                levelText.gameObject.SetActive(false);
                detailLinesText.gameObject.SetActive(false);
                descriptionText.gameObject.SetActive(false);
                return;
            }

            currentInstanceId = details.InstanceId;
            artifactIcon.sprite = details.Icon;
            artifactIcon.enabled = details.Icon != null;
            artifactIcon.gameObject.SetActive(details.Icon != null);
            nameText.gameObject.SetActive(true);
            slotText.gameObject.SetActive(true);
            levelText.gameObject.SetActive(true);
            nameText.text = details.Name;
            slotText.text = details.SlotName;
            levelText.text = details.LevelText;
            detailLinesText.text = string.Join("\n", details.DetailLines ?? Array.Empty<string>());
            descriptionText.text = details.Description;
            detailLinesText.gameObject.SetActive(details.DetailLines != null && details.DetailLines.Count > 0);
            descriptionText.gameObject.SetActive(!string.IsNullOrWhiteSpace(details.Description));
            rarityStars.gameObject.SetActive(details.Rarity > 0);
            Vector2 starSize = rarityStars.rectTransform.sizeDelta;
            starSize.x = 6f * Mathf.Clamp(details.Rarity, 0, 5);
            rarityStars.rectTransform.sizeDelta = starSize;
        }

        /// <summary>清空圣遗物页面。</summary>
        public void Clear()
        {
            Bind(Array.Empty<CharacterArtifactSlotItemViewData>(),
                new CharacterArtifactSummaryViewData(0, Array.Empty<string>()), null);
        }

        #endregion

        #region 内部事件

        /// <summary>转发圣遗物槽位选择。</summary>
        /// <param name="slot">选中的部位。</param>
        private void HandleSlotSelected(ArtifactSlot slot)
        {
            SlotSelected?.Invoke(slot);
        }

        /// <summary>处理不可用的换装按钮。</summary>
        private void HandleReplaceClicked()
        {
        }

        /// <summary>转发圣遗物培养意图。</summary>
        private void HandleDevelopmentClicked()
        {
            if (currentInstanceId.IsValid) DevelopmentRequested?.Invoke(currentInstanceId);
        }

        #endregion
    }
}
