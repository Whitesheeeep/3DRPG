using System;
using RPG.Character;
using RPG.Game.UI.Character;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色窗口顶层 View，只绑定显示节点并转发用户意图。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterWindowView : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField] private CharacterRosterStripView rosterStrip;
        [FormerlySerializedAs("characterAvatarImage")]
        [SerializeField] private Image fullBodyPortraitImage;
        [SerializeField] private Image rarityStars;
        [SerializeField] private TMP_Text characterNameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text experienceText;
        [SerializeField] private TMP_Text capStateText;
        [SerializeField] private Image experienceProgressImage;
        [SerializeField] private Button previousCharacterButton;
        [SerializeField] private Button nextCharacterButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button attributeButton;
        [SerializeField] private Button weaponButton;
        [SerializeField] private Button artifactButton;
        [SerializeField] private GameObject attributeSelectedIcon;
        [SerializeField] private GameObject attributeUnselectedIcon;
        [SerializeField] private GameObject weaponSelectedIcon;
        [SerializeField] private GameObject weaponUnselectedIcon;
        [SerializeField] private GameObject artifactSelectedIcon;
        [SerializeField] private GameObject artifactUnselectedIcon;
        [SerializeField] private GameObject attributePageRoot;
        [SerializeField] private GameObject weaponPageRoot;
        [SerializeField] private GameObject artifactPageRoot;
        [SerializeField] private CharacterAttributePageView attributePage;
        [SerializeField] private CharacterWeaponPageView weaponPage;
        [SerializeField] private CharacterArtifactPageView artifactPage;
        [SerializeField] private float rarityStarWidth = 6f;

        #endregion

        #region 事件

        /// <summary>角色头像选择意图。</summary>
        public event Action<CharacterId> CharacterSelected;
        /// <summary>左右切换意图。</summary>
        public event Action<int> CharacterCycleRequested;
        /// <summary>页面切换意图。</summary>
        public event Action<CharacterWindowPage> PageRequested;
        /// <summary>关闭窗口意图。</summary>
        public event Action CloseRequested;
        /// <summary>武器培养意图。</summary>
        public event Action<RPG.ItemSystem.EquipmentInstanceId> WeaponDevelopmentRequested;
        /// <summary>圣遗物槽位选择意图。</summary>
        public event Action<RPG.ItemSystem.ArtifactSlot> ArtifactSlotSelected;
        /// <summary>圣遗物培养意图。</summary>
        public event Action<RPG.ItemSystem.EquipmentInstanceId> ArtifactDevelopmentRequested;

        #endregion

        #region 生命周期

        /// <summary>校验 View 依赖并注册所有按钮事件。</summary>
        private void Awake()
        {
            if (rosterStrip == null || fullBodyPortraitImage == null || rarityStars == null ||
                characterNameText == null || levelText == null || experienceText == null || capStateText == null ||
                experienceProgressImage == null ||
                previousCharacterButton == null || nextCharacterButton == null || closeButton == null ||
                attributeButton == null || weaponButton == null || artifactButton == null ||
                attributeSelectedIcon == null || attributeUnselectedIcon == null ||
                weaponSelectedIcon == null || weaponUnselectedIcon == null ||
                artifactSelectedIcon == null || artifactUnselectedIcon == null ||
                attributePageRoot == null || weaponPageRoot == null || artifactPageRoot == null ||
                attributePage == null || weaponPage == null || artifactPage == null)
                throw new InvalidOperationException("[CharacterWindowView] 角色窗口绑定不完整。");
            rosterStrip.CharacterSelected += HandleCharacterSelected;
            previousCharacterButton.onClick.AddListener(HandlePreviousClicked);
            nextCharacterButton.onClick.AddListener(HandleNextClicked);
            closeButton.onClick.AddListener(HandleCloseClicked);
            attributeButton.onClick.AddListener(HandleAttributeClicked);
            weaponButton.onClick.AddListener(HandleWeaponClicked);
            artifactButton.onClick.AddListener(HandleArtifactClicked);
            weaponPage.DevelopmentRequested += HandleWeaponDevelopmentRequested;
            artifactPage.SlotSelected += HandleArtifactSlotSelected;
            artifactPage.DevelopmentRequested += HandleArtifactDevelopmentRequested;
            rarityStars.type = Image.Type.Tiled;
            rarityStars.raycastTarget = false;
            fullBodyPortraitImage.preserveAspect = true;
            fullBodyPortraitImage.raycastTarget = false;
            experienceProgressImage.type = Image.Type.Filled;
            experienceProgressImage.fillMethod = Image.FillMethod.Horizontal;
            experienceProgressImage.raycastTarget = false;
        }

        /// <summary>注销 View 内部事件。</summary>
        private void OnDestroy()
        {
            if (rosterStrip != null) rosterStrip.CharacterSelected -= HandleCharacterSelected;
            if (previousCharacterButton != null) previousCharacterButton.onClick.RemoveListener(HandlePreviousClicked);
            if (nextCharacterButton != null) nextCharacterButton.onClick.RemoveListener(HandleNextClicked);
            if (closeButton != null) closeButton.onClick.RemoveListener(HandleCloseClicked);
            if (attributeButton != null) attributeButton.onClick.RemoveListener(HandleAttributeClicked);
            if (weaponButton != null) weaponButton.onClick.RemoveListener(HandleWeaponClicked);
            if (artifactButton != null) artifactButton.onClick.RemoveListener(HandleArtifactClicked);
            if (weaponPage != null) weaponPage.DevelopmentRequested -= HandleWeaponDevelopmentRequested;
            if (artifactPage != null)
            {
                artifactPage.SlotSelected -= HandleArtifactSlotSelected;
                artifactPage.DevelopmentRequested -= HandleArtifactDevelopmentRequested;
            }
        }

        #endregion

        #region 绑定

        /// <summary>完整绑定角色窗口快照并切换页面显隐。</summary>
        /// <param name="data">角色窗口数据。</param>
        public void Bind(CharacterWindowViewData data)
        {
            if (data == null || data.Header == null)
            {
                Clear();
                return;
            }
            rosterStrip.Bind(data.Roster);
            fullBodyPortraitImage.sprite = data.FullBodyPortrait;
            // 未导入角色全身立绘时不回退成方形头像，透明保留中央角色展示区。
            fullBodyPortraitImage.enabled = data.FullBodyPortrait != null;
            characterNameText.text = data.Header.Name;
            levelText.text = data.Header.LevelText;
            experienceText.text = data.Header.ExperienceText;
            experienceText.gameObject.SetActive(string.IsNullOrWhiteSpace(data.Header.CapStateText));
            capStateText.text = data.Header.CapStateText;
            capStateText.gameObject.SetActive(!string.IsNullOrWhiteSpace(data.Header.CapStateText));
            experienceProgressImage.fillAmount = data.Header.ExperienceProgress;
            experienceProgressImage.gameObject.SetActive(data.Header.ShowExperienceProgress);
            rarityStars.gameObject.SetActive(data.Header.Rarity > 0);
            Vector2 starSize = rarityStars.rectTransform.sizeDelta;
            starSize.x = rarityStarWidth * Mathf.Clamp(data.Header.Rarity, 0, 5);
            rarityStars.rectTransform.sizeDelta = starSize;
            attributePageRoot.SetActive(data.Page == CharacterWindowPage.Attribute);
            weaponPageRoot.SetActive(data.Page == CharacterWindowPage.Weapon);
            artifactPageRoot.SetActive(data.Page == CharacterWindowPage.Artifact);
            SetNavigationState(attributeSelectedIcon, attributeUnselectedIcon, data.Page == CharacterWindowPage.Attribute);
            SetNavigationState(weaponSelectedIcon, weaponUnselectedIcon, data.Page == CharacterWindowPage.Weapon);
            SetNavigationState(artifactSelectedIcon, artifactUnselectedIcon, data.Page == CharacterWindowPage.Artifact);
            attributePage.Bind(data.Attributes);
            weaponPage.Bind(data.Weapon);
            artifactPage.Bind(data.Artifacts, data.ArtifactSummary, data.SelectedArtifact);
        }

        /// <summary>清空所有角色内容并隐藏页面。</summary>
        public void Clear()
        {
            rosterStrip.Clear();
            fullBodyPortraitImage.sprite = null;
            fullBodyPortraitImage.enabled = false;
            characterNameText.text = levelText.text = experienceText.text = capStateText.text = string.Empty;
            experienceText.gameObject.SetActive(false);
            capStateText.gameObject.SetActive(false);
            experienceProgressImage.fillAmount = 0f;
            experienceProgressImage.gameObject.SetActive(false);
            rarityStars.gameObject.SetActive(false);
            attributePageRoot.SetActive(false);
            weaponPageRoot.SetActive(false);
            artifactPageRoot.SetActive(false);
            SetNavigationState(attributeSelectedIcon, attributeUnselectedIcon, false);
            SetNavigationState(weaponSelectedIcon, weaponUnselectedIcon, false);
            SetNavigationState(artifactSelectedIcon, artifactUnselectedIcon, false);
            attributePage.Clear();
            weaponPage.Clear();
            artifactPage.Clear();
        }

        #endregion

        #region 内部事件

        /// <summary>显示当前页签状态，避免导航按键只触发内容切换却没有选中反馈。</summary>
        /// <param name="selectedIcon">选中装饰节点。</param>
        /// <param name="unselectedIcon">普通装饰节点。</param>
        /// <param name="selected">是否为当前页面。</param>
        private static void SetNavigationState(GameObject selectedIcon, GameObject unselectedIcon, bool selected)
        {
            selectedIcon.SetActive(selected);
            unselectedIcon.SetActive(!selected);
        }

        /// <summary>转发头像选择。</summary>
        /// <param name="characterId">角色标识。</param>
        private void HandleCharacterSelected(CharacterId characterId) => CharacterSelected?.Invoke(characterId);
        /// <summary>转发左侧循环切换。</summary>
        private void HandlePreviousClicked() => CharacterCycleRequested?.Invoke(-1);
        /// <summary>转发右侧循环切换。</summary>
        private void HandleNextClicked() => CharacterCycleRequested?.Invoke(1);
        /// <summary>转发关闭。</summary>
        private void HandleCloseClicked() => CloseRequested?.Invoke();
        /// <summary>转发属性页请求。</summary>
        private void HandleAttributeClicked() => PageRequested?.Invoke(CharacterWindowPage.Attribute);
        /// <summary>转发武器页请求。</summary>
        private void HandleWeaponClicked() => PageRequested?.Invoke(CharacterWindowPage.Weapon);
        /// <summary>转发圣遗物页请求。</summary>
        private void HandleArtifactClicked() => PageRequested?.Invoke(CharacterWindowPage.Artifact);
        /// <summary>转发武器培养。</summary>
        /// <param name="instanceId">武器实例。</param>
        private void HandleWeaponDevelopmentRequested(RPG.ItemSystem.EquipmentInstanceId instanceId) => WeaponDevelopmentRequested?.Invoke(instanceId);
        /// <summary>转发圣遗物槽位。</summary>
        /// <param name="slot">圣遗物部位。</param>
        private void HandleArtifactSlotSelected(RPG.ItemSystem.ArtifactSlot slot) => ArtifactSlotSelected?.Invoke(slot);
        /// <summary>转发圣遗物培养。</summary>
        /// <param name="instanceId">圣遗物实例。</param>
        private void HandleArtifactDevelopmentRequested(RPG.ItemSystem.EquipmentInstanceId instanceId) => ArtifactDevelopmentRequested?.Invoke(instanceId);

        #endregion
    }
}
