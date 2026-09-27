using System;
using System.Collections.Generic;
using RPG.Game.UI.Bag;
using RPG.Character;
using RPG.Game.UI.Character;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace RPG.Game.UI.Views.Character
{
    /// <summary>角色窗口顶层 View，只绑定显示节点并转发用户意图。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterWindowView : MonoBehaviour
    {
        #region 常量

        #endregion

        #region 依赖字段

        [SerializeField] private CharacterRosterStripView rosterStrip;
        [FormerlySerializedAs("characterAvatarImage")]
        [SerializeField] private Image fullBodyPortraitImage;
        [SerializeField] private Image centralWeaponImage;
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
        [SerializeField] private GameObject leftNavigationRoot;
        [SerializeField] private CharacterAttributePageView attributePage;
        [SerializeField] private CharacterWeaponPageView weaponPage;
        [SerializeField] private CharacterArtifactPageView artifactPage;
        private bool selectionMode;
        private CharacterWindowPage boundPage;

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
        /// <summary>武器页装备/交换操作意图。</summary>
        public event Action WeaponReplaceRequested;
        /// <summary>圣遗物页装备/交换操作意图。</summary>
        public event Action ArtifactReplaceRequested;

        #endregion

        #region 生命周期

        /// <summary>校验 View 依赖并注册所有按钮事件。</summary>
        private void Awake()
        {
            if (rosterStrip == null || fullBodyPortraitImage == null || centralWeaponImage == null ||
                previousCharacterButton == null || nextCharacterButton == null || closeButton == null ||
                attributeButton == null || weaponButton == null || artifactButton == null ||
                attributeSelectedIcon == null || attributeUnselectedIcon == null ||
                weaponSelectedIcon == null || weaponUnselectedIcon == null ||
                artifactSelectedIcon == null || artifactUnselectedIcon == null ||
                attributePageRoot == null || weaponPageRoot == null || artifactPageRoot == null ||
                leftNavigationRoot == null || attributePage == null || weaponPage == null || artifactPage == null)
                throw new InvalidOperationException("[CharacterWindowView] 角色窗口绑定不完整。");
            rosterStrip.CharacterSelected += HandleCharacterSelected;
            previousCharacterButton.onClick.AddListener(HandlePreviousClicked);
            nextCharacterButton.onClick.AddListener(HandleNextClicked);
            closeButton.onClick.AddListener(HandleCloseClicked);
            attributeButton.onClick.AddListener(HandleAttributeClicked);
            weaponButton.onClick.AddListener(HandleWeaponClicked);
            artifactButton.onClick.AddListener(HandleArtifactClicked);
            weaponPage.DevelopmentRequested += HandleWeaponDevelopmentRequested;
            weaponPage.ReplaceRequested += HandleWeaponReplaceRequested;
            artifactPage.SlotSelected += HandleArtifactSlotSelected;
            artifactPage.DevelopmentRequested += HandleArtifactDevelopmentRequested;
            artifactPage.ReplaceRequested += HandleArtifactReplaceRequested;
            fullBodyPortraitImage.preserveAspect = true;
            fullBodyPortraitImage.raycastTarget = false;
            centralWeaponImage.preserveAspect = true;
            centralWeaponImage.raycastTarget = false;
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
            if (weaponPage != null) weaponPage.ReplaceRequested -= HandleWeaponReplaceRequested;
            if (artifactPage != null)
            {
                artifactPage.SlotSelected -= HandleArtifactSlotSelected;
                artifactPage.DevelopmentRequested -= HandleArtifactDevelopmentRequested;
                artifactPage.ReplaceRequested -= HandleArtifactReplaceRequested;
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
            attributePageRoot.SetActive(data.Page == CharacterWindowPage.Attribute);
            weaponPageRoot.SetActive(data.Page == CharacterWindowPage.Weapon);
            artifactPageRoot.SetActive(data.Page == CharacterWindowPage.Artifact);
            SetNavigationState(attributeSelectedIcon, attributeUnselectedIcon, data.Page == CharacterWindowPage.Attribute);
            SetNavigationState(weaponSelectedIcon, weaponUnselectedIcon, data.Page == CharacterWindowPage.Weapon);
            SetNavigationState(artifactSelectedIcon, artifactUnselectedIcon, data.Page == CharacterWindowPage.Artifact);
            if (data.Page == CharacterWindowPage.Attribute)
                attributePage.Bind(data.Header, data.Attributes);
            else
                attributePage.Clear();
            weaponPage.Bind(data.Weapon);
            centralWeaponImage.sprite = data.Weapon != null && data.Weapon.HasWeapon ? data.Weapon.Icon : null;
            artifactPage.Bind(data.Artifacts, data.ArtifactSummary, data.SelectedArtifact);
            boundPage = data.Page;
            ApplySelectionModeState();
        }

        /// <summary>清空所有角色内容并隐藏页面。</summary>
        public void Clear()
        {
            rosterStrip.Clear();
            fullBodyPortraitImage.sprite = null;
            fullBodyPortraitImage.enabled = false;
            centralWeaponImage.sprite = null;
            centralWeaponImage.enabled = false;
            attributePageRoot.SetActive(false);
            weaponPageRoot.SetActive(false);
            artifactPageRoot.SetActive(false);
            SetNavigationState(attributeSelectedIcon, attributeUnselectedIcon, false);
            SetNavigationState(weaponSelectedIcon, weaponUnselectedIcon, false);
            SetNavigationState(artifactSelectedIcon, artifactUnselectedIcon, false);
            attributePage.Clear();
            weaponPage.Clear();
            artifactPage.Clear();
            ApplySelectionModeState();
        }

        /// <summary>在角色更换模式下隐藏导航与角色切换控件，但保留立绘和关闭按钮。</summary>
        /// <param name="selecting">是否显示装备选择状态。</param>
        public void SetSelectionMode(bool selecting)
        {
            selectionMode = selecting;
            ApplySelectionModeState();
        }

        /// <summary>将被点击的背包候选详情绑定到当前装备页面的右侧详情区。</summary>
        /// <param name="category">候选装备分类。</param>
        /// <param name="details">候选装备详情；为空时显示提示。</param>
        /// <param name="canEquip">当前是否允许交换。</param>
        /// <param name="statusText">装备限制原因或附加状态。</param>
        public void BindSelectionCandidate(RPG.ItemSystem.ItemCategory category, BagItemViewData itemData,
            BagDetailViewData details, IReadOnlyList<CharacterEquipmentAttributeLineViewData> attributeLines,
            bool canEquip, string statusText)
        {
            if (category == RPG.ItemSystem.ItemCategory.Weapon)
            {
                weaponPage.BindCandidate(itemData, details, attributeLines, canEquip, statusText);
                centralWeaponImage.sprite = details?.Icon;
                centralWeaponImage.enabled = details?.Icon != null;
                centralWeaponImage.gameObject.SetActive(details?.Icon != null);
            }
            else if (category == RPG.ItemSystem.ItemCategory.Artifact)
                artifactPage.BindCandidate(itemData, details, attributeLines, canEquip, statusText);
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

        /// <summary>同步选择模式对角色条、翻页按钮、左导航和两个装备页操作的显隐影响。</summary>
        private void ApplySelectionModeState()
        {
            if (rosterStrip != null) rosterStrip.gameObject.SetActive(!selectionMode);
            if (previousCharacterButton != null) previousCharacterButton.gameObject.SetActive(!selectionMode);
            if (nextCharacterButton != null) nextCharacterButton.gameObject.SetActive(!selectionMode);
            if (leftNavigationRoot != null) leftNavigationRoot.SetActive(!selectionMode);
            if (centralWeaponImage != null)
            {
                bool showWeapon = boundPage == CharacterWindowPage.Weapon && centralWeaponImage.sprite != null;
                centralWeaponImage.gameObject.SetActive(showWeapon);
                centralWeaponImage.enabled = showWeapon;
            }
            if (weaponPage != null) weaponPage.SetSelectionMode(selectionMode && boundPage == CharacterWindowPage.Weapon);
            if (artifactPage != null) artifactPage.SetSelectionMode(selectionMode && boundPage == CharacterWindowPage.Artifact);
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
        /// <summary>转发武器页装备/交换按钮意图。</summary>
        private void HandleWeaponReplaceRequested() => WeaponReplaceRequested?.Invoke();
        /// <summary>转发圣遗物槽位。</summary>
        /// <param name="slot">圣遗物部位。</param>
        private void HandleArtifactSlotSelected(RPG.ItemSystem.ArtifactSlot slot) => ArtifactSlotSelected?.Invoke(slot);
        /// <summary>转发圣遗物培养。</summary>
        /// <param name="instanceId">圣遗物实例。</param>
        private void HandleArtifactDevelopmentRequested(RPG.ItemSystem.EquipmentInstanceId instanceId) => ArtifactDevelopmentRequested?.Invoke(instanceId);
        /// <summary>转发圣遗物页装备/交换按钮意图。</summary>
        private void HandleArtifactReplaceRequested() => ArtifactReplaceRequested?.Invoke();

        #endregion
    }
}
