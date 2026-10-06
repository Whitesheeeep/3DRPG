using System;
using System.Collections.Generic;
using RPG.Game.UI.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.GAS.Generated;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色基础属性页面 View。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAttributePageView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private Image rarityStars;
        [SerializeField] private Image ascensionRankStars;
        [SerializeField] private TMP_Text characterNameText;
        [SerializeField] private TMP_Text currentLevelText;
        [SerializeField] private TMP_Text levelCapText;
        [SerializeField] private TMP_Text experienceText;
        [SerializeField] private TMP_Text experiencePercentText;
        [SerializeField] private TMP_Text capStateText;
        [SerializeField] private Image experienceProgressImage;
        [SerializeField] private Button developmentButton;
        [SerializeField] private TMP_Text developmentButtonText;
        [SerializeField] private TMP_Dropdown partyPositionDropdown;
        [SerializeField] private TMP_Text partyPositionStatusText;
        [SerializeField] private ScrollRect introductionScrollRect;
        [SerializeField] private TMP_Text introductionText;
        [SerializeField] private GameObject introductionRoot;
        [SerializeField] private CharacterAttributeLineView maxHealthLine;
        [SerializeField] private CharacterAttributeLineView attackPowerLine;
        [SerializeField] private CharacterAttributeLineView armorLine;
        [SerializeField] private CharacterAttributeLineView criticalChanceLine;
        [SerializeField] private CharacterAttributeLineView criticalDamageLine;

        #endregion

        #region 页面状态

        private bool partyPositionEditing;

        #endregion

        #region 事件

        /// <summary>角色属性页请求升级或突破培养。</summary>
        public event Action DevelopmentRequested;
        /// <summary>角色请求加入、移动、交换或退出队伍位置；负一表示退出。</summary>
        public event Action<int> PartyPositionRequested;

        #endregion

        #region 生命周期

        /// <summary>校验并初始化角色标题、经验、介绍区与固定属性行依赖。</summary>
        private void Awake()
        {
            if (rarityStars == null || ascensionRankStars == null || characterNameText == null ||
                currentLevelText == null || levelCapText == null || experienceText == null ||
                experiencePercentText == null || capStateText == null || experienceProgressImage == null ||
                developmentButton == null || developmentButtonText == null ||
                partyPositionDropdown == null || partyPositionStatusText == null ||
                introductionScrollRect == null || introductionScrollRect.viewport == null ||
                introductionText == null || introductionRoot == null)
                throw new InvalidOperationException("[CharacterAttributePageView] 角色标题、经验条或介绍区未绑定完整。");

            ValidateLine(maxHealthLine, nameof(maxHealthLine));
            ValidateLine(attackPowerLine, nameof(attackPowerLine));
            ValidateLine(armorLine, nameof(armorLine));
            ValidateLine(criticalChanceLine, nameof(criticalChanceLine));
            ValidateLine(criticalDamageLine, nameof(criticalDamageLine));

            rarityStars.type = Image.Type.Tiled;
            rarityStars.raycastTarget = false;
            ascensionRankStars.type = Image.Type.Tiled;
            ascensionRankStars.raycastTarget = false;
            experienceProgressImage.type = Image.Type.Filled;
            experienceProgressImage.fillMethod = Image.FillMethod.Horizontal;
            experienceProgressImage.raycastTarget = false;
            developmentButton.onClick.AddListener(HandleDevelopmentClicked);
            partyPositionDropdown.onValueChanged.AddListener(HandlePartyPositionChanged);
            partyPositionDropdown.interactable = false;
            partyPositionStatusText.gameObject.SetActive(false);
            Debug.Log("[CharacterAttributePageView] 初始化完成，角色信息、队伍位置控件与五条固定属性行已绑定。");
        }

        /// <summary>销毁时移除角色升级入口按钮回调。</summary>
        private void OnDestroy()
        {
            if (developmentButton != null) developmentButton.onClick.RemoveListener(HandleDevelopmentClicked);
            if (partyPositionDropdown != null)
                partyPositionDropdown.onValueChanged.RemoveListener(HandlePartyPositionChanged);
        }

        #endregion

        #region 绑定

        /// <summary>统一绑定角色标题、成长进度、介绍与固定 Stat 行。</summary>
        /// <param name="header">当前角色的标题与成长进度数据。</param>
        /// <param name="data">当前角色的基础属性列表。</param>
        /// <param name="partyPosition">当前角色的队伍槽位与选项名称。</param>
        public void Bind(CharacterHeaderViewData header, IReadOnlyList<CharacterAttributeViewData> data,
            CharacterPartyPositionViewData partyPosition)
        {
            Clear();
            if (header == null)
                return;

            if (data != null)
            {
                for (int index = 0; index < data.Count; index++)
                    BindFixedLine(data[index]);
            }
            BindHeader(header);
            BindPartyPosition(partyPosition);
            Debug.Log($"[CharacterAttributePageView] 绑定角色属性页，角色={header.Name}，属性行={data?.Count ?? 0}。");
        }

        /// <summary>清空属性页。</summary>
        public void Clear()
        {
            maxHealthLine.Clear();
            attackPowerLine.Clear();
            armorLine.Clear();
            criticalChanceLine.Clear();
            criticalDamageLine.Clear();
            ClosePartyPositionDropdown();
            partyPositionDropdown.ClearOptions();
            partyPositionDropdown.SetValueWithoutNotify(0);
            partyPositionDropdown.interactable = false;
            partyPositionStatusText.text = string.Empty;
            partyPositionStatusText.gameObject.SetActive(false);
            ClearHeader();
        }

        /// <summary>在异步队伍提交期间禁用位置选择下拉框。</summary>
        /// <param name="editing">当前是否存在未完成的编辑请求。</param>
        public void SetPartyPositionEditing(bool editing)
        {
            partyPositionEditing = editing;
            partyPositionDropdown.interactable = !editing && partyPositionDropdown.options.Count > 0;
        }

        /// <summary>显示或清除队伍位置编辑失败原因。</summary>
        /// <param name="message">非空时显示的简短说明。</param>
        public void SetPartyPositionStatus(string message)
        {
            partyPositionStatusText.text = message ?? string.Empty;
            partyPositionStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
        }

        /// <summary>关闭展开中的队伍位置选项，以免它跨页或跨角色悬留。</summary>
        public void ClosePartyPositionDropdown()
        {
            if (partyPositionDropdown == null || !partyPositionDropdown.IsExpanded) return;
            partyPositionDropdown.Hide();
            Debug.Log("[CharacterAttributePageView] 角色队伍位置菜单已关闭。");
        }

        #endregion

        #region 校验与内部绑定

        /// <summary>将当前角色的标题、等级进度和介绍绑定到属性页控件。</summary>
        /// <param name="header">角色标题数据。</param>
        private void BindHeader(CharacterHeaderViewData header)
        {
            characterNameText.text = header.Name;
            currentLevelText.text = header.CurrentLevelText;
            levelCapText.text = header.LevelCapText;
            experienceText.text = header.ExperienceText;
            experienceText.gameObject.SetActive(string.IsNullOrWhiteSpace(header.CapStateText));
            capStateText.text = header.CapStateText;
            capStateText.gameObject.SetActive(!string.IsNullOrWhiteSpace(header.CapStateText));
            bool isMaxLevel = string.Equals(header.CapStateText, "已满级", StringComparison.Ordinal);
            bool canAscend = string.Equals(header.CapStateText, "已达当前等级上限", StringComparison.Ordinal);
            developmentButtonText.text = isMaxLevel ? "已满级" : canAscend ? "突破" : "升级";
            developmentButton.interactable = !isMaxLevel;
            developmentButton.gameObject.SetActive(true);
            experienceProgressImage.fillAmount = header.ExperienceProgress;
            experienceProgressImage.gameObject.SetActive(header.ShowExperienceProgress);
            experiencePercentText.text = header.ExperiencePercentText;
            experiencePercentText.gameObject.SetActive(header.ShowExperienceProgress);
            ApplyStarCount(rarityStars, header.Rarity);
            ascensionRankStars.gameObject.SetActive(header.AscensionRank > 0);
            ApplyStarCount(ascensionRankStars, header.AscensionRank);
            BindIntroduction(header.Introduction);
        }

        /// <summary>按队伍真实快照设置选项与选中值，不触发新的编辑请求。</summary>
        /// <param name="partyPosition">当前角色的队伍位置数据。</param>
        private void BindPartyPosition(CharacterPartyPositionViewData partyPosition)
        {
            if (partyPosition == null)
            {
                partyPositionDropdown.interactable = false;
                return;
            }

            var optionData = new List<TMP_Dropdown.OptionData>(partyPosition.Options.Count);
            for (int index = 0; index < partyPosition.Options.Count; index++)
                optionData.Add(new TMP_Dropdown.OptionData(partyPosition.Options[index]));
            partyPositionDropdown.ClearOptions();
            partyPositionDropdown.AddOptions(optionData);
            partyPositionDropdown.SetValueWithoutNotify(Mathf.Clamp(
                partyPosition.SelectedOptionIndex, 0, optionData.Count - 1));
            partyPositionDropdown.RefreshShownValue();
            partyPositionDropdown.interactable = !partyPositionEditing;
        }

        /// <summary>清空属性页标题和介绍，防止切换角色或清空窗口后残留旧值。</summary>
        private void ClearHeader()
        {
            characterNameText.text = string.Empty;
            currentLevelText.text = string.Empty;
            levelCapText.text = string.Empty;
            experienceText.text = string.Empty;
            experienceText.gameObject.SetActive(false);
            experiencePercentText.text = string.Empty;
            experiencePercentText.gameObject.SetActive(false);
            capStateText.text = string.Empty;
            capStateText.gameObject.SetActive(false);
            developmentButton.interactable = false;
            developmentButtonText.text = string.Empty;
            experienceProgressImage.fillAmount = 0f;
            experienceProgressImage.gameObject.SetActive(false);
            rarityStars.gameObject.SetActive(false);
            ascensionRankStars.gameObject.SetActive(false);
            introductionText.text = string.Empty;
            introductionRoot.SetActive(false);
            introductionScrollRect.verticalNormalizedPosition = 1f;
        }

        /// <summary>根据星数更新平铺星图宽度，统一每颗星的 8×10 UI 单位。</summary>
        /// <param name="starImage">需要更新的星级图像。</param>
        /// <param name="starCount">星级数量。</param>
        private static void ApplyStarCount(Image starImage, int starCount)
        {
            int count = Mathf.Clamp(starCount, 0, 7);
            starImage.type = Image.Type.Tiled;
            starImage.raycastTarget = false;
            starImage.rectTransform.sizeDelta = new Vector2(count * 8f, starImage.rectTransform.sizeDelta.y);
            starImage.gameObject.SetActive(count > 0);
        }

        /// <summary>绑定角色介绍并按文本高度更新滚动内容，避免影响固定属性布局。</summary>
        /// <param name="introduction">角色配置中的静态介绍。</param>
        private void BindIntroduction(string introduction)
        {
            introductionText.text = introduction ?? string.Empty;
            bool hasIntroduction = !string.IsNullOrWhiteSpace(introductionText.text);
            introductionRoot.SetActive(hasIntroduction);
            if (!hasIntroduction)
                return;

            Canvas.ForceUpdateCanvases();
            RectTransform content = introductionText.rectTransform;
            Vector2 contentSize = content.sizeDelta;
            contentSize.y = Mathf.Max(introductionScrollRect.viewport.rect.height, introductionText.preferredHeight);
            content.sizeDelta = contentSize;
            introductionScrollRect.verticalNormalizedPosition = 1f;
        }

        /// <summary>校验固定属性行已在 Prefab 中显式绑定。</summary>
        /// <param name="line">待校验行。</param>
        /// <param name="fieldName">对应序列化字段名。</param>
        private static void ValidateLine(CharacterAttributeLineView line, string fieldName)
        {
            if (line == null)
                throw new InvalidOperationException($"[CharacterAttributePageView] 固定属性行 {fieldName} 未绑定。");
        }

        /// <summary>转发属性页升级或突破按钮请求。</summary>
        private void HandleDevelopmentClicked() => DevelopmentRequested?.Invoke();

        /// <summary>将 Dropdown 选项索引转换为零基队伍槽位请求。</summary>
        /// <param name="optionIndex">TMP_Dropdown 的选项索引；零表示退出队伍。</param>
        private void HandlePartyPositionChanged(int optionIndex) => PartyPositionRequested?.Invoke(optionIndex - 1);

        /// <summary>按 AttributeId 将数据绑定到固定行，避免列表顺序变化导致图标与数值错位。</summary>
        /// <param name="data">待显示属性。</param>
        private void BindFixedLine(CharacterAttributeViewData data)
        {
            if (data.AttributeId == GameplayAttributes.Attribute_MaxHealth.Id)
                maxHealthLine.Bind(data);
            else if (data.AttributeId == GameplayAttributes.Attribute_AttackPower.Id)
                attackPowerLine.Bind(data);
            else if (data.AttributeId == GameplayAttributes.Attribute_Armor.Id)
                armorLine.Bind(data);
            else if (data.AttributeId == GameplayAttributes.Attribute_CriticalChance.Id)
                criticalChanceLine.Bind(data);
            else if (data.AttributeId == GameplayAttributes.Attribute_CriticalDamage.Id)
                criticalDamageLine.Bind(data);
            else
                throw new InvalidOperationException(
                    $"[CharacterAttributePageView] 收到未配置固定行的 AttributeId={data.AttributeId}。");
        }

        #endregion
    }
}
