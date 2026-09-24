using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Game.UI.Character;
using RPG.Game.UI.EquipmentDevelopment;
using RPG.Game.UI.Services;
using RPG.Game.UI.Views.Character;
using RPG.Game.UI.WeaponDevelopment;
using RPG.ItemSystem;
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
        private WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private CharacterWindowPresentationBuilder presentationBuilder;
        private IUnRegister characterInstanceChangedUnregister;
        private IUnRegister characterRosterRestoredUnregister;
        private IUnRegister characterEquipmentRestoredUnregister;
        private IUnRegister weaponChangedUnregister;
        private IUnRegister weaponRestoredUnregister;
        private IUnRegister artifactChangedUnregister;
        private IUnRegister artifactRestoredUnregister;

        #endregion

        #region 状态字段

        private CharacterId selectedCharacterId;
        private bool hasSelectedCharacter;
        private CharacterWindowPage currentPage = CharacterWindowPage.Attribute;
        private int selectedArtifactIndex;
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
            spriteAtlasLeaseService = new WindowSpriteAtlasLeaseService(
                data.DynamicAtlasAddresses, data.AtlasReleaseDelaySeconds);
            presentationBuilder = new CharacterWindowPresentationBuilder(equipmentSystem, spriteAtlasLeaseService,
                data.PartyMarkSprites);

            view.CharacterSelected += HandleCharacterSelected;
            view.CharacterCycleRequested += HandleCharacterCycleRequested;
            view.PageRequested += HandlePageRequested;
            view.CloseRequested += HandleCloseRequested;
            view.WeaponDevelopmentRequested += HandleWeaponDevelopmentRequested;
            view.ArtifactSlotSelected += HandleArtifactSlotSelected;
            view.ArtifactDevelopmentRequested += HandleArtifactDevelopmentRequested;
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
            spriteAtlasLeaseService.Released -= HandleAtlasReleased;
            characterInstanceChangedUnregister?.UnRegister();
            characterRosterRestoredUnregister?.UnRegister();
            characterEquipmentRestoredUnregister?.UnRegister();
            weaponChangedUnregister?.UnRegister();
            weaponRestoredUnregister?.UnRegister();
            artifactChangedUnregister?.UnRegister();
            artifactRestoredUnregister?.UnRegister();
            if (partyManager != null) partyManager.Changed -= HandlePartyChanged;
            characterInstanceChangedUnregister = null;
            characterRosterRestoredUnregister = null;
            characterEquipmentRestoredUnregister = null;
            weaponChangedUnregister = null;
            weaponRestoredUnregister = null;
            artifactChangedUnregister = null;
            artifactRestoredUnregister = null;
            spriteAtlasLeaseService?.Dispose();
            spriteAtlasLeaseService = null;
            presentationBuilder = null;
            view = null;
            partyManager = null;
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
            if (!initialized || disposed || !windowShown) return;
            List<CharacterInstance> instances = GetSortedInstances();
            if (instances.Count == 0)
            {
                hasSelectedCharacter = false;
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

        /// <summary>图集释放后清空旧 Sprite；重新打开会重新绑定。</summary>
        private void HandleAtlasReleased()
        {
            if (!windowShown) return;
            Refresh();
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
