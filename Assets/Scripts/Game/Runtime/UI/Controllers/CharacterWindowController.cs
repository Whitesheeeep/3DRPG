using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.CurrencySystemNS;
using RPG.Game.Runtime.CharacterDevelopment;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Character;
using RPG.Game.UI.EquipmentDevelopment;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Character;
using RPG.Game.UI.Views.Common;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
using WS_Modules.GAS.AttributeSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>管理角色窗口的选中角色、页面状态、动态图集和装备培养跳转。</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterWindowController : MonoBehaviour
    {
        #region 依赖字段

        // 依赖字段：View 只负责显示，角色与装备数据由架构 Manager 提供。
        private CharacterWindowDataComponent data;
        private CharacterWindowView view;
        private CharacterRosterManager rosterManager;
        private CharacterPartyManager partyManager;
        private CharacterEquipmentSystem equipmentSystem;
        private WeaponInventoryManager weaponInventoryManager;
        private ArtifactInventoryManager artifactInventoryManager;
        private StackableInventoryManager stackableInventoryManager;
        private CurrencyManager currencyManager;
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private CharacterWindowPresentationBuilder presentationBuilder;
        private CharacterEquipmentSelectionPresentationBuilder selectionPresentationBuilder;
        private CharacterDevelopmentService characterDevelopmentService;
        private CharacterDevelopmentPresentationBuilder characterDevelopmentPresentationBuilder;
        private ItemSelectionPanelView selectionPanel;
        private IUnRegister characterInstanceChangedUnregister;
        private IUnRegister characterRosterRestoredUnregister;
        private IUnRegister characterEquipmentRestoredUnregister;
        private IUnRegister weaponChangedUnregister;
        private IUnRegister weaponRestoredUnregister;
        private IUnRegister artifactChangedUnregister;
        private IUnRegister artifactRestoredUnregister;
        private IUnRegister stackableChangedUnregister;
        private IUnRegister stackableRestoredUnregister;
        private IUnRegister currencyChangedUnregister;
        private IUnRegister currencyRestoredUnregister;

        #endregion

        #region 状态字段

        private CharacterId selectedCharacterId;
        private bool hasSelectedCharacter;
        private CharacterWindowPage currentPage = CharacterWindowPage.Attribute;
        private int selectedArtifactIndex;
        private ItemCategory selectionCategory;
        private BagEntryKey? selectedCandidateEntryKey;
        private IReadOnlyList<BagItemViewData> currentSelectionEntries = Array.Empty<BagItemViewData>();
        private BagSortMode selectionSortMode = BagSortMode.Quality;
        private BagSortDirection selectionSortDirection = BagSortDirection.Descending;
        private bool selectionMode;
        private CharacterDevelopmentMode characterDevelopmentMode;
        private bool characterDevelopmentSelectionOpen;
        // key：角色经验素材 ItemId；value：当前选择消耗数量，每类材料一项。
        private readonly Dictionary<ItemId, int> selectedCharacterExperienceQuantityByItemIdMap = new();
        private bool submittingCharacterDevelopment;
        private string developmentStatusOverride;
        private bool initialized;
        private bool windowShown;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>绑定角色窗口依赖并注册领域变化事件。</summary>
        /// <param name="windowData">窗口序列化数据。</param>
        public void Initialize(CharacterWindowDataComponent windowData)
        {
            if (initialized)
            {
                if (!ReferenceEquals(data, windowData))
                    throw new InvalidOperationException("[CharacterWindowController] 不允许替换窗口数据。");
                return;
            }

            data = windowData ?? throw new ArgumentNullException(nameof(windowData));
            data.ValidateConfiguration();
            view = data.View;
            rosterManager = GameArchitecture.Interface.GetManager<CharacterRosterManager>();
            partyManager = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
            equipmentSystem = GameArchitecture.Interface.GetSystem<CharacterEquipmentSystem>();
            weaponInventoryManager = GameArchitecture.Interface.GetManager<WeaponInventoryManager>();
            artifactInventoryManager = GameArchitecture.Interface.GetManager<ArtifactInventoryManager>();
            stackableInventoryManager = GameArchitecture.Interface.GetManager<StackableInventoryManager>();
            currencyManager = CurrencyManager.Instance;
            selectionPanel = data.SelectionPanel;
            spriteAtlasLeaseService = new WindowSpriteAtlasLeaseService(
                data.DynamicAtlasAddresses, data.AtlasReleaseDelaySeconds);
            presentationBuilder = new CharacterWindowPresentationBuilder(equipmentSystem, spriteAtlasLeaseService,
                data.PartyMarkSprites);
            selectionPresentationBuilder = new CharacterEquipmentSelectionPresentationBuilder(
                weaponInventoryManager, artifactInventoryManager, rosterManager, ResolveSprite);
            characterDevelopmentService = new CharacterDevelopmentService(rosterManager, stackableInventoryManager,
                currencyManager, equipmentSystem, new CharacterAttributeProgressionResolver());
            characterDevelopmentPresentationBuilder = new CharacterDevelopmentPresentationBuilder(
                characterDevelopmentService, stackableInventoryManager, currencyManager, ResolveSprite);

            view.CharacterSelected += HandleCharacterSelected;
            view.CharacterCycleRequested += HandleCharacterCycleRequested;
            view.PageRequested += HandlePageRequested;
            view.CloseRequested += HandleCloseRequested;
            view.WeaponDevelopmentRequested += HandleWeaponDevelopmentRequested;
            view.ArtifactSlotSelected += HandleArtifactSlotSelected;
            view.ArtifactDevelopmentRequested += HandleArtifactDevelopmentRequested;
            view.WeaponReplaceRequested += HandleWeaponReplaceRequested;
            view.ArtifactReplaceRequested += HandleArtifactReplaceRequested;
            view.CharacterDevelopmentRequested += HandleCharacterDevelopmentRequested;
            view.CharacterDevelopmentSubmitRequested += HandleCharacterDevelopmentSubmitRequested;
            view.CharacterExperienceAutoFillRequested += HandleCharacterExperienceAutoFillRequested;
            view.CharacterDevelopmentMaterialsRequested += HandleCharacterDevelopmentMaterialsRequested;
            selectionPanel.EntryClicked += HandleCandidateEntryClicked;
            selectionPanel.QuantityChangeRequested += HandleCharacterExperienceQuantityChanged;
            selectionPanel.ReturnRequested += HandleSelectionReturnRequested;
            selectionPanel.SortModeChanged += HandleSelectionSortModeChanged;
            selectionPanel.SortDirectionRequested += HandleSelectionSortDirectionRequested;
            selectionPanel.HideImmediateAndReset();
            selectionPanel.SetSortControl(false, selectionSortMode, selectionSortDirection, "等级");
            spriteAtlasLeaseService.Released += HandleAtlasReleased;

            characterInstanceChangedUnregister = EventSystem.Register_Type<CharacterInstanceChangedEvent>(
                typeof(CharacterInstanceChangedEvent), HandleCharacterInstanceChanged);
            characterRosterRestoredUnregister = EventSystem.Register_Type<CharacterRosterRestoredEvent>(
                typeof(CharacterRosterRestoredEvent), HandleCharacterRosterRestored);
            characterEquipmentRestoredUnregister = EventSystem.Register_Type<CharacterEquipmentRestoredEvent>(
                typeof(CharacterEquipmentRestoredEvent), HandleCharacterEquipmentRestored);
            weaponChangedUnregister = EventSystem.Register_Type<WeaponInstanceChangedEvent>(
                typeof(WeaponInstanceChangedEvent), HandleWeaponChanged);
            weaponRestoredUnregister = EventSystem.Register_Type<WeaponInventoryRestoredEvent>(
                typeof(WeaponInventoryRestoredEvent), HandleWeaponRestored);
            artifactChangedUnregister = EventSystem.Register_Type<ArtifactInstanceChangedEvent>(
                typeof(ArtifactInstanceChangedEvent), HandleArtifactChanged);
            artifactRestoredUnregister = EventSystem.Register_Type<ArtifactInventoryRestoredEvent>(
                typeof(ArtifactInventoryRestoredEvent), HandleArtifactRestored);
            stackableChangedUnregister = EventSystem.Register_Type<StackableItemChangedEvent>(
                typeof(StackableItemChangedEvent), HandleStackableChanged);
            stackableRestoredUnregister = EventSystem.Register_Type<StackableInventoryRestoredEvent>(
                typeof(StackableInventoryRestoredEvent), HandleStackableRestored);
            currencyChangedUnregister = EventSystem.Register_Type<CurrencyBalanceChangedEvent>(
                typeof(CurrencyBalanceChangedEvent), HandleCurrencyBalanceChanged);
            currencyRestoredUnregister = EventSystem.Register_Type<CurrencyWalletRestoredEvent>(
                typeof(CurrencyWalletRestoredEvent), HandleCurrencyWalletRestored);
            partyManager.Changed += HandlePartyChanged;
            initialized = true;
            WSLog.Log("[CharacterWindowController] CharacterWindow MVC 初始化完成。");
        }

        /// <summary>窗口显示时选择角色、启动图集加载并绑定属性页。</summary>
        public void OnWindowShown()
        {
            if (!initialized || disposed) return;
            windowShown = true;
            currentPage = CharacterWindowPage.Attribute;
            ResetSelectionModeImmediately();
            spriteAtlasLeaseService.CancelRelease();
            SelectDefaultCharacterIfNeeded();
            Refresh();
            PrepareOpen();
            WSLog.Log("[CharacterWindowController] CharacterWindow 已显示并刷新属性页。");
        }

        /// <summary>窗口隐藏时清空 Sprite 并安排动态图集延迟释放。</summary>
        public void OnWindowHidden()
        {
            if (!initialized || disposed) return;
            windowShown = false;
            ResetSelectionModeImmediately();
            view.Clear();
            spriteAtlasLeaseService.ScheduleRelease();
            WSLog.Log("[CharacterWindowController] CharacterWindow 已隐藏，已清理页面并安排图集释放。");
        }

        /// <summary>窗口销毁时对称注销所有事件和动态图集租约。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.CharacterSelected -= HandleCharacterSelected;
            view.CharacterCycleRequested -= HandleCharacterCycleRequested;
            view.PageRequested -= HandlePageRequested;
            view.CloseRequested -= HandleCloseRequested;
            view.WeaponDevelopmentRequested -= HandleWeaponDevelopmentRequested;
            view.ArtifactSlotSelected -= HandleArtifactSlotSelected;
            view.ArtifactDevelopmentRequested -= HandleArtifactDevelopmentRequested;
            view.WeaponReplaceRequested -= HandleWeaponReplaceRequested;
            view.ArtifactReplaceRequested -= HandleArtifactReplaceRequested;
            view.CharacterDevelopmentRequested -= HandleCharacterDevelopmentRequested;
            view.CharacterDevelopmentSubmitRequested -= HandleCharacterDevelopmentSubmitRequested;
            view.CharacterExperienceAutoFillRequested -= HandleCharacterExperienceAutoFillRequested;
            view.CharacterDevelopmentMaterialsRequested -= HandleCharacterDevelopmentMaterialsRequested;
            if (selectionPanel != null)
            {
                selectionPanel.EntryClicked -= HandleCandidateEntryClicked;
                selectionPanel.QuantityChangeRequested -= HandleCharacterExperienceQuantityChanged;
                selectionPanel.ReturnRequested -= HandleSelectionReturnRequested;
                selectionPanel.SortModeChanged -= HandleSelectionSortModeChanged;
                selectionPanel.SortDirectionRequested -= HandleSelectionSortDirectionRequested;
                selectionPanel.HideImmediateAndReset();
            }
            spriteAtlasLeaseService.Released -= HandleAtlasReleased;
            characterInstanceChangedUnregister?.UnRegister();
            characterRosterRestoredUnregister?.UnRegister();
            characterEquipmentRestoredUnregister?.UnRegister();
            weaponChangedUnregister?.UnRegister();
            weaponRestoredUnregister?.UnRegister();
            artifactChangedUnregister?.UnRegister();
            artifactRestoredUnregister?.UnRegister();
            stackableChangedUnregister?.UnRegister();
            stackableRestoredUnregister?.UnRegister();
            currencyChangedUnregister?.UnRegister();
            currencyRestoredUnregister?.UnRegister();
            if (partyManager != null) partyManager.Changed -= HandlePartyChanged;
            characterInstanceChangedUnregister = null;
            characterRosterRestoredUnregister = null;
            characterEquipmentRestoredUnregister = null;
            weaponChangedUnregister = null;
            weaponRestoredUnregister = null;
            artifactChangedUnregister = null;
            artifactRestoredUnregister = null;
            stackableChangedUnregister = null;
            stackableRestoredUnregister = null;
            currencyChangedUnregister = null;
            currencyRestoredUnregister = null;
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            presentationBuilder = null;
            selectionPresentationBuilder = null;
            characterDevelopmentService = null;
            characterDevelopmentPresentationBuilder = null;
            stackableInventoryManager = null;
            currencyManager = null;
            selectionPanel = null;
            view = null;
            partyManager = null;
            weaponInventoryManager = null;
            artifactInventoryManager = null;
            WSLog.Log("[CharacterWindowController] CharacterWindow 已释放。");
        }

        #endregion

        #region 图集准备

        /// <summary>启动角色、武器和圣遗物图集的异步加载，不阻塞窗口显示。</summary>
        public void PrepareOpen()
        {
            PrepareOpenAsync().Forget(HandleAsyncException);
        }

        /// <summary>等待本轮动态图集加载尝试完成。</summary>
        /// <returns>图集加载任务。</returns>
        public async UniTask PrepareOpenAsync()
        {
            bool allAtlasesLoaded = await spriteAtlasLeaseService.BeginLoadConfiguredAtlasesAsync();
            if (disposed || !windowShown) return;

            // 首次 Bind 发生在图集加载前；完成后必须重新解析当前角色，才能把立绘和图标绑定到 Image。
            Refresh();
            if (!allAtlasesLoaded)
                WSLog.LogWarning("[CharacterWindowController] 部分动态图集加载失败，相关 Sprite 暂不显示，重新打开窗口时会重试。");
        }

        #endregion

        #region 刷新与选择

        /// <summary>按当前稳定实例和页面完整重建 ViewData。</summary>
        private void Refresh()
        {
            if (!initialized || disposed || !windowShown || submittingCharacterDevelopment) return;
            List<CharacterInstance> instances = GetSortedInstances();
            if (instances.Count == 0)
            {
                hasSelectedCharacter = false;
                ResetSelectionModeImmediately();
                view.Clear();
                return;
            }
            CharacterInstance selected = FindSelected(instances);
            if (selected == null)
            {
                selected = instances[0];
                selectedCharacterId = selected.CharacterId;
                hasSelectedCharacter = true;
            }

            CharacterWindowViewData viewData;
            try
            {
                viewData = presentationBuilder.Build(instances, selected, currentPage, selectedArtifactIndex);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError($"[CharacterWindowController] 角色基础数据无法构建，character={selected.CharacterId}，原因={exception.Message}", this);
                view.Clear();
                return;
            }
            view.Bind(viewData);
            view.SetSelectionMode(selectionMode);
            view.SetCharacterDevelopmentMode(characterDevelopmentMode);
            if (characterDevelopmentMode != CharacterDevelopmentMode.None)
            {
                RefreshCharacterDevelopmentState(selected);
                return;
            }
            if (selectionMode) RefreshSelectionCandidates(selected);
        }

        /// <summary>保证当前选择仍然存在，否则选择排序后的第一名。</summary>
        private void SelectDefaultCharacterIfNeeded()
        {
            List<CharacterInstance> instances = GetSortedInstances();
            if (instances.Count == 0)
            {
                hasSelectedCharacter = false;
                selectedArtifactIndex = 0;
                return;
            }
            if (!hasSelectedCharacter || FindSelected(instances) == null)
            {
                selectedCharacterId = instances[0].CharacterId;
                hasSelectedCharacter = true;
                selectedArtifactIndex = FindFirstEquippedArtifactIndex(selectedCharacterId);
            }
        }

        /// <summary>获取排序后的角色实例副本。</summary>
        /// <returns>按品质、获得顺序和标识排序的列表。</returns>
        private List<CharacterInstance> GetSortedInstances()
        {
            List<CharacterInstance> instances = rosterManager.GetInstances().Where(item => item != null).ToList();
            instances.Sort((left, right) =>
            {
                int rarity = ((int)right.Config.Rarity).CompareTo((int)left.Config.Rarity);
                if (rarity != 0) return rarity;
                int sequence = left.AcquisitionSequence.CompareTo(right.AcquisitionSequence);
                return sequence != 0 ? sequence : string.CompareOrdinal(left.CharacterId.ToString(), right.CharacterId.ToString());
            });
            return instances;
        }

        /// <summary>按稳定角色标识查找当前选中实例。</summary>
        /// <param name="instances">排序后的角色列表。</param>
        /// <returns>当前实例，找不到时返回空。</returns>
        private CharacterInstance FindSelected(IReadOnlyList<CharacterInstance> instances)
        {
            if (!hasSelectedCharacter) return null;
            for (int index = 0; index < instances.Count; index++)
                if (instances[index].CharacterId == selectedCharacterId) return instances[index];
            return null;
        }

        #endregion

        #region 用户意图

        /// <summary>切换当前选中角色。</summary>
        /// <param name="characterId">目标角色。</param>
        private void HandleCharacterSelected(CharacterId characterId)
        {
            if (!characterId.IsValid) return;
            selectedCharacterId = characterId;
            hasSelectedCharacter = true;
            selectedArtifactIndex = FindFirstEquippedArtifactIndex(characterId);
            Refresh();
        }

        /// <summary>按方向循环切换角色，首尾不会越界。</summary>
        /// <param name="direction">-1 表示向左，1 表示向右。</param>
        private void HandleCharacterCycleRequested(int direction)
        {
            List<CharacterInstance> instances = GetSortedInstances();
            if (instances.Count == 0) return;
            int currentIndex = instances.FindIndex(item => item.CharacterId == selectedCharacterId);
            if (currentIndex < 0) currentIndex = 0;
            int nextIndex = (currentIndex + (direction < 0 ? -1 : 1) + instances.Count) % instances.Count;
            selectedCharacterId = instances[nextIndex].CharacterId;
            hasSelectedCharacter = true;
            selectedArtifactIndex = FindFirstEquippedArtifactIndex(selectedCharacterId);
            Refresh();
        }

        /// <summary>切换属性、武器或圣遗物页面。</summary>
        /// <param name="page">目标页面。</param>
        private void HandlePageRequested(CharacterWindowPage page)
        {
            if (characterDevelopmentMode != CharacterDevelopmentMode.None || selectionMode)
                ResetSelectionModeImmediately();
            currentPage = page;
            Refresh();
        }

        /// <summary>请求关闭角色窗口。</summary>
        private void HandleCloseRequested()
        {
            UIManager.Instance.HideWindowAsync<CharacterWindow>().Forget(HandleAsyncException);
        }

        /// <summary>打开当前武器的统一装备培养窗口。</summary>
        /// <param name="instanceId">武器实例。</param>
        private void HandleWeaponDevelopmentRequested(EquipmentInstanceId instanceId)
        {
            OpenEquipmentDevelopmentAsync(EquipmentDevelopmentOpenContext.ForWeapon(instanceId)).Forget(HandleAsyncException);
        }

        /// <summary>更新当前圣遗物选中槽位。</summary>
        /// <param name="slot">选中部位。</param>
        private void HandleArtifactSlotSelected(ArtifactSlot slot)
        {
            selectedArtifactIndex = (int)slot;
            Refresh();
        }

        /// <summary>打开当前圣遗物的统一装备培养窗口。</summary>
        /// <param name="instanceId">圣遗物实例。</param>
        private void HandleArtifactDevelopmentRequested(EquipmentInstanceId instanceId)
        {
            OpenEquipmentDevelopmentAsync(EquipmentDevelopmentOpenContext.ForArtifact(instanceId)).Forget(HandleAsyncException);
        }

        /// <summary>根据当前页面进入武器或圣遗物候选选择状态。</summary>
        /// <param name="category">需要更换的装备类型。</param>
        private void BeginSelectionMode(ItemCategory category)
        {
            if (!windowShown || (category != ItemCategory.Weapon && category != ItemCategory.Artifact)) return;
            selectionCategory = category;
            currentPage = category == ItemCategory.Weapon ? CharacterWindowPage.Weapon : CharacterWindowPage.Artifact;
            selectionMode = true;
            selectedCandidateEntryKey = null;
            selectionSortMode = BagSortMode.Quality;
            selectionSortDirection = BagSortDirection.Descending;
            view.SetSelectionMode(true);
            selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, "等级");
            // 先激活面板，使池化网格完成 Awake 与事件初始化，再绑定候选数据。
            selectionPanel.ShowAnimated();
            Refresh();
            WSLog.Log($"[CharacterWindowController] 打开装备候选面板，character={selectedCharacterId}, category={category}, artifactSlot={(ArtifactSlot)selectedArtifactIndex}。");
        }

        /// <summary>从候选模式返回角色装备页面，并清理临时候选选择。</summary>
        private void ExitSelectionMode()
        {
            if (!selectionMode) return;
            selectionMode = false;
            selectedCandidateEntryKey = null;
            currentSelectionEntries = Array.Empty<BagItemViewData>();
            selectionPanel.SetSortControl(false, selectionSortMode, selectionSortDirection, "等级");
            selectionPanel.HideAnimated();
            view.SetSelectionMode(false);
            Refresh();
            WSLog.Log($"[CharacterWindowController] 退出装备候选模式，character={selectedCharacterId}, category={selectionCategory}。");
        }

        /// <summary>窗口关闭或重新打开时立即终止所有子选择层并清空临时培养状态。</summary>
        private void ResetSelectionModeImmediately()
        {
            selectionMode = false;
            characterDevelopmentMode = CharacterDevelopmentMode.None;
            characterDevelopmentSelectionOpen = false;
            selectedCandidateEntryKey = null;
            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            developmentStatusOverride = null;
            currentSelectionEntries = Array.Empty<BagItemViewData>();
            selectionPanel?.SetSortControl(false, selectionSortMode, selectionSortDirection, "等级");
            selectionPanel?.HideImmediateAndReset();
            view?.SetSelectionMode(false);
            view?.SetCharacterDevelopmentMode(CharacterDevelopmentMode.None);
        }

        /// <summary>根据装备页按钮进入选择层，或提交已选择的武器候选。</summary>
        private void HandleWeaponReplaceRequested()
        {
            if (!selectionMode)
            {
                BeginSelectionMode(ItemCategory.Weapon);
                return;
            }
            if (selectionCategory == ItemCategory.Weapon) CommitSelectedCandidate();
        }

        /// <summary>根据圣遗物页按钮进入选择层，或提交已选择的部位候选。</summary>
        private void HandleArtifactReplaceRequested()
        {
            if (!selectionMode)
            {
                BeginSelectionMode(ItemCategory.Artifact);
                return;
            }
            if (selectionCategory == ItemCategory.Artifact) CommitSelectedCandidate();
        }

        /// <summary>按当前角色成长状态进入升级或突破面板，并打开共用材料选择层。</summary>
        private void HandleCharacterDevelopmentRequested()
        {
            if (!windowShown || selectionMode || characterDevelopmentMode != CharacterDevelopmentMode.None ||
                !rosterManager.TryGetInstance(selectedCharacterId, out CharacterInstance character)) return;

            if (character.Level >= character.Config.MaxLevel)
                return;

            if (!TryGetCurrentLevelCap(character.Config, character.AscensionRank, out int currentLevelCap))
            {
                Debug.LogError($"[CharacterWindowController] 无法解析角色等级上限，character={character.CharacterId}, rank={character.AscensionRank}。", this);
                return;
            }

            CharacterDevelopmentMode mode = character.Level >= currentLevelCap
                ? CharacterDevelopmentMode.Ascension
                : CharacterDevelopmentMode.LevelUp;
            if (mode == CharacterDevelopmentMode.Ascension &&
                character.AscensionRank >= character.Config.AscensionStages.Count) return;

            currentPage = CharacterWindowPage.Attribute;
            characterDevelopmentMode = mode;
            characterDevelopmentSelectionOpen = false;
            selectionMode = false;
            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            developmentStatusOverride = null;
            selectionSortMode = mode == CharacterDevelopmentMode.LevelUp
                ? BagSortMode.AcquisitionSequence
                : BagSortMode.Quality;
            selectionSortDirection = BagSortDirection.Ascending;
            view.SetSelectionMode(false);
            view.SetCharacterDevelopmentMode(mode);
            selectionPanel.SetSortControl(false, selectionSortMode, selectionSortDirection, "数量");
            // 培养预览先完整显示；共用候选面板只在用户明确点选素材入口后展开。
            selectionPanel.HideImmediateAndReset();
            Refresh();
            WSLog.Log($"[CharacterWindowController] 打开角色培养面板，character={selectedCharacterId}, mode={mode}, level={character.Level}, cap={currentLevelCap}。");
        }

        /// <summary>仅在角色升级时展开经验素材选择面板。</summary>
        private void HandleCharacterDevelopmentMaterialsRequested()
        {
            if (!windowShown || characterDevelopmentMode != CharacterDevelopmentMode.LevelUp ||
                characterDevelopmentSelectionOpen) return;

            characterDevelopmentSelectionOpen = true;
            selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, "数量");
            selectionPanel.ShowAnimated();
            Refresh();
            WSLog.Log($"[CharacterWindowController] 展开角色经验素材面板，character={selectedCharacterId}。");
        }

        /// <summary>关闭角色培养左侧材料面板，同时保留当前升级或突破预览状态。</summary>
        private void CloseCharacterDevelopmentSelection()
        {
            if (!characterDevelopmentSelectionOpen) return;
            characterDevelopmentSelectionOpen = false;
            selectionPanel.SetSortControl(false, selectionSortMode, selectionSortDirection, "数量");
            selectionPanel.HideAnimated();
            Refresh();
            WSLog.Log($"[CharacterWindowController] 收起角色培养素材面板，character={selectedCharacterId}, mode={characterDevelopmentMode}。");
        }

        /// <summary>退出角色升级或突破状态并清理材料选择与池化属性行。</summary>
        private void ExitCharacterDevelopmentMode()
        {
            if (characterDevelopmentMode == CharacterDevelopmentMode.None) return;
            CharacterDevelopmentMode previousMode = characterDevelopmentMode;
            bool selectionWasOpen = characterDevelopmentSelectionOpen;
            characterDevelopmentMode = CharacterDevelopmentMode.None;
            characterDevelopmentSelectionOpen = false;
            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            developmentStatusOverride = null;
            selectionPanel.SetSortControl(false, selectionSortMode, selectionSortDirection, "数量");
            if (selectionWasOpen) selectionPanel.HideAnimated();
            else selectionPanel.HideImmediateAndReset();
            view.SetCharacterDevelopmentMode(CharacterDevelopmentMode.None);
            Refresh();
            WSLog.Log($"[CharacterWindowController] 退出角色培养面板，character={selectedCharacterId}, previousMode={previousMode}。");
        }

        /// <summary>供窗口 Esc 栈使用；培养或装备选择层优先于关闭整个角色窗口。</summary>
        /// <returns>本次是否收起了窗口内子状态。</returns>
        public bool CloseSelectionModeFromCommand()
        {
            if (characterDevelopmentMode != CharacterDevelopmentMode.None)
            {
                if (characterDevelopmentSelectionOpen)
                {
                    CloseCharacterDevelopmentSelection();
                    return true;
                }
                ExitCharacterDevelopmentMode();
                return true;
            }
            if (!selectionMode) return false;
            ExitSelectionMode();
            return true;
        }

        /// <summary>根据当前等级和突破阶数解析升级入口使用的等级上限。</summary>
        /// <param name="config">角色成长配置。</param>
        /// <param name="ascensionRank">当前突破阶数。</param>
        /// <param name="levelCap">解析后的当前等级上限。</param>
        /// <returns>配置合法时返回 true。</returns>
        private static bool TryGetCurrentLevelCap(CharacterConfig config, int ascensionRank, out int levelCap)
        {
            levelCap = 0;
            if (config == null || ascensionRank < 0 || ascensionRank > config.AscensionStages.Count) return false;
            if (config.AscensionStages.Count == 0)
            {
                levelCap = config.MaxLevel;
                return levelCap > 0;
            }

            CharacterAscensionStage stage = ascensionRank == 0
                ? config.AscensionStages[0]
                : config.AscensionStages[ascensionRank - 1];
            if (stage == null) return false;
            levelCap = ascensionRank == 0 ? stage.RequiredLevel : stage.MaxLevelAfter;
            return levelCap > 0 && levelCap <= config.MaxLevel;
        }

        /// <summary>只更新候选预览与选中框，不在网格点击时执行装备事务。</summary>
        /// <param name="entryKey">候选条目键。</param>
        private void HandleCandidateEntryClicked(BagEntryKey entryKey)
        {
            if (!selectionMode || entryKey.Category != selectionCategory || !ContainsSelectionEntry(entryKey)) return;
            selectedCandidateEntryKey = entryKey;
            Refresh();
        }

        /// <summary>接收排序字段变更并重建候选顺序，保留当前稳定的装备实例选择。</summary>
        /// <param name="sortMode">目标排序字段。</param>
        private void HandleSelectionSortModeChanged(BagSortMode sortMode)
        {
            if (!selectionMode && (!characterDevelopmentSelectionOpen ||
                                   characterDevelopmentMode != CharacterDevelopmentMode.LevelUp)) return;
            selectionSortMode = sortMode;
            string primaryLabel = characterDevelopmentMode == CharacterDevelopmentMode.LevelUp ? "数量" : "等级";
            selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, primaryLabel);
            WSLog.Log($"[CharacterWindowController] 候选排序字段变更，mode={selectionSortMode}, direction={selectionSortDirection}, development={characterDevelopmentMode}。");
            Refresh();
        }

        /// <summary>切换装备候选升降序并保留当前稳定的装备实例选择。</summary>
        private void HandleSelectionSortDirectionRequested()
        {
            if (!selectionMode && (!characterDevelopmentSelectionOpen ||
                                   characterDevelopmentMode != CharacterDevelopmentMode.LevelUp)) return;
            selectionSortDirection = selectionSortDirection == BagSortDirection.Descending
                ? BagSortDirection.Ascending
                : BagSortDirection.Descending;
            string primaryLabel = characterDevelopmentMode == CharacterDevelopmentMode.LevelUp ? "数量" : "等级";
            selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, primaryLabel);
            WSLog.Log($"[CharacterWindowController] 候选排序方向变更，mode={selectionSortMode}, direction={selectionSortDirection}, development={characterDevelopmentMode}。");
            Refresh();
        }

        /// <summary>通用候选面板返回按钮关闭当前选择层。</summary>
        private void HandleSelectionReturnRequested()
        {
            if (characterDevelopmentMode != CharacterDevelopmentMode.None && characterDevelopmentSelectionOpen)
                CloseCharacterDevelopmentSelection();
            else if (characterDevelopmentMode != CharacterDevelopmentMode.None) ExitCharacterDevelopmentMode();
            else ExitSelectionMode();
        }

        /// <summary>将当前武器或目标圣遗物槽的库存过滤、排序并投影到共用网格。</summary>
        /// <param name="character">当前角色实例。</param>
        private void RefreshSelectionCandidates(CharacterInstance character)
        {
            currentSelectionEntries = selectionPresentationBuilder.BuildEntries(character, selectionCategory,
                (ArtifactSlot)selectedArtifactIndex, selectionSortMode, selectionSortDirection);
            if (selectedCandidateEntryKey.HasValue && !ContainsSelectionEntry(selectedCandidateEntryKey.Value))
                selectedCandidateEntryKey = null;
            if (!selectedCandidateEntryKey.HasValue && currentSelectionEntries.Count > 0)
                selectedCandidateEntryKey = currentSelectionEntries[0].EntryKey;

            BagEntryKey[] selectedKeys = selectedCandidateEntryKey.HasValue
                ? new[] { selectedCandidateEntryKey.Value }
                : Array.Empty<BagEntryKey>();
            selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, "等级");
            selectionPanel.Bind(currentSelectionEntries, selectedKeys, null);
            if (!selectedCandidateEntryKey.HasValue ||
                !selectionPresentationBuilder.TryBuildDetails(selectedCandidateEntryKey.Value,
                    out BagDetailViewData details))
            {
                string emptyStatus = currentSelectionEntries.Count == 0
                    ? (selectionCategory == ItemCategory.Weapon
                        ? "没有符合该角色武器类型的库存装备。"
                        : "当前部位没有可用圣遗物。")
                    : "候选装备已失效，请重新选择。";
                BindSelectionCandidate(null, null, false, emptyStatus);
                return;
            }

            bool canEquip = TryGetCandidateEquipability(character, selectedCandidateEntryKey.Value,
                out string statusText);
            BindSelectionCandidate(selectedCandidateEntryKey, details, canEquip, statusText);
        }

        /// <summary>刷新角色培养右侧预览和左侧经验素材或固定突破配方候选。</summary>
        /// <param name="character">当前角色实例。</param>
        private void RefreshCharacterDevelopmentState(CharacterInstance character)
        {
            CharacterDevelopmentPanelViewData panelData;
            if (characterDevelopmentMode == CharacterDevelopmentMode.LevelUp)
            {
                ClampSelectedExperienceQuantities();
                panelData = characterDevelopmentPresentationBuilder.BuildLevelUp(character,
                    selectedCharacterExperienceQuantityByItemIdMap, selectionSortMode, selectionSortDirection,
                    out IReadOnlyList<BagItemViewData> entries);
                if (characterDevelopmentSelectionOpen)
                {
                    var selectedKeys = new List<BagEntryKey>(selectedCharacterExperienceQuantityByItemIdMap.Count);
                    foreach (KeyValuePair<ItemId, int> pair in selectedCharacterExperienceQuantityByItemIdMap)
                        if (pair.Value > 0)
                            selectedKeys.Add(new BagEntryKey(ItemCategory.DevelopmentExperienceItem, pair.Key.ToString()));
                    selectionPanel.SetSortControl(true, selectionSortMode, selectionSortDirection, "数量");
                    selectionPanel.BindQuantitySelection(entries, selectedKeys);
                }
            }
            else
            {
                panelData = characterDevelopmentPresentationBuilder.BuildAscension(character);
            }

            view.BindCharacterDevelopment(panelData);
            if (!string.IsNullOrWhiteSpace(developmentStatusOverride))
                view.SetCharacterDevelopmentStatus(developmentStatusOverride);
        }

        /// <summary>在库存变化后保留仍合法的经验材料数量，并限制总选中数为 99。</summary>
        private void ClampSelectedExperienceQuantities()
        {
            IReadOnlyList<StackableInventoryEntry> inventoryEntries = stackableInventoryManager
                .GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Character);
            var ownedQuantityByItemIdMap = new Dictionary<ItemId, int>(inventoryEntries.Count);
            for (int index = 0; index < inventoryEntries.Count; index++)
                ownedQuantityByItemIdMap[inventoryEntries[index].ItemId] = inventoryEntries[index].Quantity;

            int remainingSelectionCount = 99;
            var clampedQuantityByItemIdMap = new Dictionary<ItemId, int>(selectedCharacterExperienceQuantityByItemIdMap.Count);
            foreach (KeyValuePair<ItemId, int> pair in selectedCharacterExperienceQuantityByItemIdMap)
            {
                if (!ownedQuantityByItemIdMap.TryGetValue(pair.Key, out int ownedQuantity) || ownedQuantity <= 0)
                    continue;

                int clampedQuantity = Mathf.Clamp(pair.Value, 0, Math.Min(ownedQuantity, remainingSelectionCount));
                if (clampedQuantity <= 0)
                    continue;

                clampedQuantityByItemIdMap.Add(pair.Key, clampedQuantity);
                remainingSelectionCount -= clampedQuantity;
            }

            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            foreach (KeyValuePair<ItemId, int> pair in clampedQuantityByItemIdMap)
                selectedCharacterExperienceQuantityByItemIdMap.Add(pair.Key, pair.Value);
        }

        /// <summary>按网格左右键意图调整角色经验素材数量并重建同一份升级预览。</summary>
        /// <param name="intent">物品数量调整意图。</param>
        private void HandleCharacterExperienceQuantityChanged(BagItemQuantityIntent intent)
        {
            if (characterDevelopmentMode != CharacterDevelopmentMode.LevelUp ||
                intent.EntryKey.Category != ItemCategory.DevelopmentExperienceItem ||
                !ItemId.TryCreate(intent.EntryKey.Value, out ItemId itemId) ||
                !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition item) ||
                !(item is DevelopmentExperienceItemDefinition definition) ||
                !definition.SupportsExperienceType(DevelopmentExperienceItemType.Character)) return;

            int ownedQuantity = stackableInventoryManager.GetQuantity(itemId);
            selectedCharacterExperienceQuantityByItemIdMap.TryGetValue(itemId, out int selectedQuantity);
            int totalSelected = 0;
            foreach (KeyValuePair<ItemId, int> pair in selectedCharacterExperienceQuantityByItemIdMap)
                totalSelected += pair.Value;

            int requestedStep = Math.Max(1, intent.Step);
            int nextQuantity = intent.Direction == BagItemQuantityDirection.Increase
                ? Math.Min(ownedQuantity, selectedQuantity + Math.Min(requestedStep, 99 - totalSelected))
                : Math.Max(0, selectedQuantity - requestedStep);
            if (nextQuantity <= 0)
                selectedCharacterExperienceQuantityByItemIdMap.Remove(itemId);
            else
                selectedCharacterExperienceQuantityByItemIdMap[itemId] = nextQuantity;

            developmentStatusOverride = null;
            Refresh();
        }

        /// <summary>按当前角色等级上限自动选取最多 99 个经验素材，再收敛到实际消耗计划。</summary>
        private void HandleCharacterExperienceAutoFillRequested()
        {
            if (characterDevelopmentMode != CharacterDevelopmentMode.LevelUp ||
                !rosterManager.TryGetInstance(selectedCharacterId, out CharacterInstance character)) return;

            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            IReadOnlyList<StackableInventoryEntry> inventoryEntries = stackableInventoryManager
                .GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Character);
            int remainingSelectionCount = 99;
            for (int index = 0; index < inventoryEntries.Count && remainingSelectionCount > 0; index++)
            {
                StackableInventoryEntry entry = inventoryEntries[index];
                int quantity = Math.Min(entry.Quantity, remainingSelectionCount);
                if (quantity <= 0) continue;
                selectedCharacterExperienceQuantityByItemIdMap[entry.ItemId] = quantity;
                remainingSelectionCount -= quantity;
            }

            CharacterLevelUpPreview preview = characterDevelopmentService.BuildLevelUpPreview(
                character.CharacterId, selectedCharacterExperienceQuantityByItemIdMap);
            selectedCharacterExperienceQuantityByItemIdMap.Clear();
            foreach (KeyValuePair<ItemId, int> pair in preview.ConsumedQuantityByItemIdMap)
                selectedCharacterExperienceQuantityByItemIdMap.Add(pair.Key, pair.Value);
            developmentStatusOverride = null;
            Refresh();
            WSLog.Log($"[CharacterWindowController] 自动填入角色经验素材，character={character.CharacterId}, materialTypes={selectedCharacterExperienceQuantityByItemIdMap.Count}。");
        }

        /// <summary>重新校验已选材料与费用后提交升级或突破事务。</summary>
        private void HandleCharacterDevelopmentSubmitRequested()
        {
            if (characterDevelopmentMode == CharacterDevelopmentMode.None || !hasSelectedCharacter) return;

            submittingCharacterDevelopment = true;
            CharacterDevelopmentOperationResult result;
            try
            {
                result = characterDevelopmentMode == CharacterDevelopmentMode.LevelUp
                    ? characterDevelopmentService.LevelUp(selectedCharacterId,
                        selectedCharacterExperienceQuantityByItemIdMap)
                    : characterDevelopmentService.Ascend(selectedCharacterId);
            }
            finally
            {
                submittingCharacterDevelopment = false;
            }

            if (result.Succeeded)
            {
                WSLog.Log($"[CharacterWindowController] 角色培养事务完成，character={selectedCharacterId}, mode={characterDevelopmentMode}。");
                ExitCharacterDevelopmentMode();
                return;
            }

            developmentStatusOverride = result.Message;
            Refresh();
            view.SetCharacterDevelopmentStatus(result.Message);
            WSLog.LogWarning($"[CharacterWindowController] 角色培养事务未完成，character={selectedCharacterId}, mode={characterDevelopmentMode}, status={result.Status}。");
        }

        /// <summary>确认候选仍存在且属于当前槽位，并拒绝已被其他角色装备的实例。</summary>
        /// <param name="character">当前角色实例。</param>
        /// <param name="entryKey">要检查的候选条目。</param>
        /// <param name="statusText">不可装备原因；可装备时为空。</param>
        /// <returns>允许提交装备关系时返回 true。</returns>
        private bool TryGetCandidateEquipability(CharacterInstance character, BagEntryKey entryKey,
            out string statusText)
        {
            statusText = string.Empty;
            if (!selectionPresentationBuilder.IsValidCandidate(character, selectionCategory,
                    (ArtifactSlot)selectedArtifactIndex, entryKey) ||
                !TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId))
            {
                statusText = "该候选已不存在或不符合当前装备槽位。";
                return false;
            }

            if (!rosterManager.TryGetEquipmentLocation(instanceId, out CharacterEquipmentLocation location))
                return true;

            string ownerName = rosterManager.TryGetInstance(location.CharacterId, out CharacterInstance owner)
                ? owner.Config.Name
                : location.CharacterId.ToString();
            if (location.CharacterId != character.CharacterId)
            {
                statusText = $"已装备给 {ownerName}，不能直接转移。";
                return false;
            }

            bool alreadyInTargetSlot = selectionCategory == ItemCategory.Weapon
                ? location.SlotKind == CharacterEquipmentSlotKind.Weapon
                : location.SlotKind == CharacterEquipmentSlotKind.Artifact &&
                  location.ArtifactSlot == (ArtifactSlot)selectedArtifactIndex;
            if (alreadyInTargetSlot)
            {
                statusText = "该装备已经位于当前槽位。";
                return false;
            }

            statusText = $"该装备已属于 {ownerName}，不能重复装备。";
            return false;
        }

        /// <summary>在点击交换时重新验证候选并调用角色装备系统事务。</summary>
        private void CommitSelectedCandidate()
        {
            if (!selectionMode || !selectedCandidateEntryKey.HasValue ||
                !rosterManager.TryGetInstance(selectedCharacterId, out CharacterInstance character)) return;
            BagEntryKey entryKey = selectedCandidateEntryKey.Value;
            BagDetailViewData details = null;
            string statusText = string.Empty;
            if (!ContainsSelectionEntry(entryKey))
            {
                statusText = "候选装备已变化，请重新选择。";
                BindSelectionCandidate(entryKey, details, false, statusText);
                WSLog.LogWarning($"[CharacterWindowController] 装备候选已不在当前库存列表，character={selectedCharacterId}, candidate={entryKey}。");
                return;
            }
            if (!selectionPresentationBuilder.TryBuildDetails(entryKey, out details))
            {
                statusText = "候选装备已不在库存中，请重新选择。";
                BindSelectionCandidate(entryKey, details, false, statusText);
                WSLog.LogWarning($"[CharacterWindowController] 装备候选提交前复核失败，character={selectedCharacterId}, candidate={entryKey}, reason={statusText}。");
                return;
            }

            if (!TryGetCandidateEquipability(character, entryKey, out statusText))
            {
                BindSelectionCandidate(entryKey, details, false, statusText);
                WSLog.LogWarning($"[CharacterWindowController] 装备候选提交前复核失败，character={selectedCharacterId}, candidate={entryKey}, reason={statusText}。");
                return;
            }

            TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId);
            EquipmentOperationResult result = selectionCategory == ItemCategory.Weapon
                ? equipmentSystem.EquipWeapon(character.CharacterId, instanceId)
                : equipmentSystem.EquipArtifact(character.CharacterId, instanceId);
            if (!result.Succeeded)
            {
                statusText = GetOperationFailureText(result.Status);
                BindSelectionCandidate(entryKey, details, false, statusText);
                WSLog.LogWarning($"[CharacterWindowController] 装备交换事务失败，character={character.CharacterId}, candidate={instanceId}, status={result.Status}。");
                return;
            }

            WSLog.Log($"[CharacterWindowController] 装备交换事务成功，character={character.CharacterId}, category={selectionCategory}, instance={instanceId}。");
            ExitSelectionMode();
            Refresh();
        }

        /// <summary>判断条目是否仍属于本轮筛选结果。</summary>
        /// <param name="entryKey">候选稳定键。</param>
        /// <returns>仍存在时返回 true。</returns>
        private bool ContainsSelectionEntry(BagEntryKey entryKey)
        {
            for (int index = 0; index < currentSelectionEntries.Count; index++)
                if (currentSelectionEntries[index].EntryKey == entryKey) return true;
            return false;
        }

        /// <summary>为候选详情、BagItem 卡片及结构化属性行提供同一实例快照。</summary>
        /// <param name="entryKey">候选稳定键；为空时显示候选空状态。</param>
        /// <param name="details">背包详情快照。</param>
        /// <param name="canEquip">是否可以提交装备关系。</param>
        /// <param name="statusText">不可装备说明。</param>
        private void BindSelectionCandidate(BagEntryKey? entryKey, BagDetailViewData details,
            bool canEquip, string statusText)
        {
            BagItemViewData itemData = null;
            IReadOnlyList<CharacterEquipmentAttributeLineViewData> attributeLines =
                Array.Empty<CharacterEquipmentAttributeLineViewData>();
            if (entryKey.HasValue)
            {
                for (int index = 0; index < currentSelectionEntries.Count; index++)
                {
                    if (currentSelectionEntries[index].EntryKey != entryKey.Value) continue;
                    itemData = currentSelectionEntries[index];
                    break;
                }
                attributeLines = selectionPresentationBuilder.BuildAttributeLines(
                    entryKey.Value, selectedCharacterId);
            }

            view.BindSelectionCandidate(selectionCategory, itemData, details, attributeLines,
                canEquip, statusText);
        }

        /// <summary>把装备事务枚举转换为候选详情区的用户可读提示。</summary>
        /// <param name="status">装备事务结果。</param>
        /// <returns>中文失败说明。</returns>
        private static string GetOperationFailureText(InventoryOperationStatus status) => status switch
        {
            InventoryOperationStatus.InstanceEquipped => "该装备已被其他角色穿戴，不能直接转移。",
            InventoryOperationStatus.WeaponTypeNotAllowed => "该角色不能装备此类型的武器。",
            InventoryOperationStatus.InstanceNotFound => "该装备已不在库存中，请重新选择。",
            InventoryOperationStatus.CharacterNotOwned => "当前角色已不在拥有列表中。",
            InventoryOperationStatus.DefinitionTypeMismatch => "装备配置类型不匹配，无法交换。",
            _ => $"装备交换失败：{status}。"
        };

        /// <summary>解析背包选择键中的装备实例标识。</summary>
        /// <param name="value">稳定 ID 文本。</param>
        /// <param name="instanceId">实例标识。</param>
        /// <returns>解析得到有效 ID 时返回 true。</returns>
        private static bool TryParseInstanceId(string value, out EquipmentInstanceId instanceId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                instanceId = default;
                return false;
            }
            instanceId = new EquipmentInstanceId(value);
            return instanceId.IsValid;
        }

        /// <summary>角色切换时默认选中第一件已装备圣遗物；全空时回到生之花槽位。</summary>
        /// <param name="characterId">需要查询圣遗物槽位的角色标识。</param>
        /// <returns>第一个已装备槽位下标；没有装备时返回零。</returns>
        private int FindFirstEquippedArtifactIndex(CharacterId characterId)
        {
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                if (equipmentSystem.TryGetEquippedArtifact(characterId, (ArtifactSlot)slotIndex, out _))
                    return slotIndex;
            }
            return 0;
        }

        /// <summary>通过 UIManager 打开统一装备培养窗口。</summary>
        /// <param name="context">装备培养上下文。</param>
        private async UniTask OpenEquipmentDevelopmentAsync(EquipmentDevelopmentOpenContext context)
        {
            EquipmentDevelopmentWindow window = await UIManager.Instance.PopUpWindowAsync<EquipmentDevelopmentWindow,
                EquipmentDevelopmentOpenContext>(context);
            if (window == null || !window.Visible)
                throw new InvalidOperationException("[CharacterWindowController] 装备培养窗口打开失败。");
            WSLog.Log($"[CharacterWindowController] 已打开装备培养窗口，target={context.TargetKind}, instance={context.InstanceId}。");
        }

        #endregion

        #region 外部变化

        /// <summary>角色实例变化后刷新当前角色或顶部列表。</summary>
        /// <param name="changeEvent">角色变化事件。</param>
        private void HandleCharacterInstanceChanged(CharacterInstanceChangedEvent changeEvent)
        {
            if (!windowShown) return;
            if (!hasSelectedCharacter || changeEvent.Instance.CharacterId == selectedCharacterId ||
                changeEvent.ChangeType == CharacterInstanceChangeType.Added)
                Refresh();
        }

        /// <summary>角色存档恢复后重新确认选择并刷新。</summary>
        /// <param name="restoredEvent">恢复事件。</param>
        private void HandleCharacterRosterRestored(CharacterRosterRestoredEvent restoredEvent)
        {
            if (!windowShown) return;
            SelectDefaultCharacterIfNeeded();
            Refresh();
        }

        /// <summary>队伍槽位变化后刷新顶部全部角色列表的队伍标记。</summary>
        private void HandlePartyChanged()
        {
            if (windowShown) Refresh();
        }

        /// <summary>装备关系恢复后刷新武器和圣遗物页。</summary>
        /// <param name="restoredEvent">装备恢复事件。</param>
        private void HandleCharacterEquipmentRestored(CharacterEquipmentRestoredEvent restoredEvent) => Refresh();

        /// <summary>武器实例变化后刷新当前武器页。</summary>
        /// <param name="changedEvent">武器变化事件。</param>
        private void HandleWeaponChanged(WeaponInstanceChangedEvent changedEvent) => Refresh();

        /// <summary>武器库存恢复后刷新显示。</summary>
        /// <param name="restoredEvent">武器恢复事件。</param>
        private void HandleWeaponRestored(WeaponInventoryRestoredEvent restoredEvent) => Refresh();

        /// <summary>圣遗物实例变化后刷新当前圣遗物页。</summary>
        /// <param name="changedEvent">圣遗物变化事件。</param>
        private void HandleArtifactChanged(ArtifactInstanceChangedEvent changedEvent) => Refresh();

        /// <summary>圣遗物库存恢复后刷新显示。</summary>
        /// <param name="restoredEvent">圣遗物恢复事件。</param>
        private void HandleArtifactRestored(ArtifactInventoryRestoredEvent restoredEvent) => Refresh();

        /// <summary>角色培养所需的材料变化后刷新数量、费用和候选卡片。</summary>
        /// <param name="eventArgs">可堆叠物品变化事件。</param>
        private void HandleStackableChanged(StackableItemChangedEvent eventArgs)
        {
            if (!windowShown || characterDevelopmentMode == CharacterDevelopmentMode.None ||
                !ItemManager.Instance.TryGetDefinition(eventArgs.ItemId, out ItemDefinition item)) return;

            bool relevant = characterDevelopmentMode == CharacterDevelopmentMode.LevelUp
                ? item is DevelopmentExperienceItemDefinition experience &&
                  experience.SupportsExperienceType(DevelopmentExperienceItemType.Character)
                : item is DevelopmentItemDefinition development &&
                  development.SupportsDevelopmentType(DevelopmentItemType.CharacterAscension);
            if (!relevant) return;

            developmentStatusOverride = null;
            Refresh();
        }

        /// <summary>背包库存恢复后刷新当前升级或突破的资源投影。</summary>
        /// <param name="restoredEvent">可堆叠库存恢复事件。</param>
        private void HandleStackableRestored(StackableInventoryRestoredEvent restoredEvent)
        {
            if (!windowShown || characterDevelopmentMode == CharacterDevelopmentMode.None) return;
            developmentStatusOverride = null;
            Refresh();
        }

        /// <summary>摩拉变化后刷新培养费用条件。</summary>
        /// <param name="eventArgs">货币变化事件。</param>
        private void HandleCurrencyBalanceChanged(CurrencyBalanceChangedEvent eventArgs)
        {
            if (!windowShown || characterDevelopmentMode == CharacterDevelopmentMode.None ||
                eventArgs.CurrencyId != CurrencyId.Mola) return;
            developmentStatusOverride = null;
            Refresh();
        }

        /// <summary>货币钱包恢复后刷新培养费用与可提交状态。</summary>
        /// <param name="restoredEvent">货币钱包恢复事件。</param>
        private void HandleCurrencyWalletRestored(CurrencyWalletRestoredEvent restoredEvent)
        {
            if (!windowShown || characterDevelopmentMode == CharacterDevelopmentMode.None) return;
            developmentStatusOverride = null;
            Refresh();
        }

        /// <summary>图集释放后清空旧 Sprite；重新打开会重新绑定。</summary>
        private void HandleAtlasReleased()
        {
            if (!windowShown) return;
            Refresh();
        }

        /// <summary>通过角色窗口当前持有的动态图集租约解析候选物品 Sprite。</summary>
        /// <param name="address">Sprite 所属图集地址。</param>
        /// <param name="spriteName">图集内 Sprite 名称。</param>
        /// <returns>图集已加载且找到 Sprite 时返回资源，否则返回空。</returns>
        private Sprite ResolveSprite(string address, string spriteName)
        {
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(spriteName) ||
                spriteAtlasLeaseService == null)
                return null;
            return spriteAtlasLeaseService.TryGetSprite(address, spriteName, out Sprite sprite) ? sprite : null;
        }

        #endregion

        #region 异常

        /// <summary>记录跨窗口异步流程的非取消异常。</summary>
        /// <param name="exception">异步异常。</param>
        private static void HandleAsyncException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception);
        }

        #endregion
    }
}
