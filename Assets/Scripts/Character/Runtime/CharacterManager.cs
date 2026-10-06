using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character.State;
using RPG.Game;
using RPG.ItemSystem;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.ResLoadModule;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>管理角色配置的异步加载、原子队伍提交与当前角色阶段门禁。</summary>
    [DisallowMultipleComponent]
    [InfoBox("CharacterConfig 指向的每个角色 Prefab 必须在根或子层级（含 inactive）中包含且仅包含一个 CharacterActor；Manager 递归查找该 Prefab 实例。运行时需由 PlayerController 注入 CharacterRoot、MotionDriver、PlayerController 和 PlayerStateBlackboard；actorContainer 可选，未绑定时挂到 CharacterRoot。角色 Prefab 地址由 CharacterConfig 提供并异步加载。")]
    public sealed class CharacterManager : MonoBehaviour
    {
        #region 配置字段

        [SerializeField] private Transform actorContainer;
        [SerializeField, CharacterIdDropdown] private CharacterId[] initialCharacterIds = Array.Empty<CharacterId>();
        [SerializeField, CharacterIdDropdown] private CharacterId initialCharacterId;

        #endregion

        #region 依赖字段

        // 依赖字段：新入队 Actor 复用 Player 初始化时的稳定运行时依赖与业务 Manager。
        private CharacterPartyManager partyManager;
        private CharacterRosterManager rosterManager;
        private WeaponInventoryManager weaponInventoryManager;
        private Transform runtimeRoot;
        private IMotionDriver runtimeMotionDriver;
        private PlayerController runtimePlayerController;
        private PlayerStateBlackboard runtimeBlackboard;
        private CancellationToken runtimeLifetimeToken;

        #endregion

        #region 运行时状态

        [SerializeField, ReadOnly, Tooltip("已通过配置和 Marker 校验的队伍角色；仅用于调试。")]
        private readonly List<CharacterActor> characterActors = new();
        // 每个元素对应一次成功的 LoadAsync；相同地址不能合并，否则会破坏底层引用计数。
        private readonly List<string> loadedPrefabAddresses = new();
        // key：已准备 Actor；value：承载该 Actor 的实例化 Prefab 根对象，支持 Actor 位于子层级的 Prefab。
        private readonly Dictionary<CharacterActor, GameObject> instanceRootByCharacterActorMap = new();
        private CharacterInitializationState initializationState = CharacterInitializationState.Uninitialized;
        private bool cancellationRequested;
        // 队伍编辑事务标记，避免在异步加载期间重复发起请求。
        private bool partyEditInProgress;
        private IUnRegister rosterRestoredUnregister;

        // 固定槽位输入只在 Ready 阶段查询，不把 PlayerInputController 保存为 Manager 生命周期依赖。
        private static readonly E_PlayerInputType[] characterSlotInputTypes =
        {
            E_PlayerInputType.CharacterSlot1,
            E_PlayerInputType.CharacterSlot2,
            E_PlayerInputType.CharacterSlot3,
            E_PlayerInputType.CharacterSlot4
        };

        #endregion

        #region 事件与属性

        /// <summary>在队伍完成原子提交后发送一次。</summary>
        public event Action Initialized;
        /// <summary>在加载或校验失败后发送一次；取消不会发送失败事件。</summary>
        public event Action<Exception> InitializationFailed;
        /// <summary>在 ActiveCharacter 完成同步切换后发送一次。</summary>
        public event Action<CharacterActor, CharacterActor> ActiveCharacterChanged;
        /// <summary>获取当前异步初始化状态。</summary>
        public CharacterInitializationState InitializationState => initializationState;
        /// <summary>获取队伍是否已完成初始化。</summary>
        public bool IsInitialized => initializationState == CharacterInitializationState.Ready;
        /// <summary>获取角色阶段门禁是否已打开。</summary>
        public bool IsReady => initializationState == CharacterInitializationState.Ready;
        /// <summary>获取已提交的队伍角色。</summary>
        public IReadOnlyList<CharacterActor> CharacterActors => characterActors;
        /// <summary>获取当前由玩家操控的角色。</summary>
        public CharacterActor ActiveCharacter { get; private set; }

        #endregion

        #region 异步初始化

        /// <summary>注册角色存档恢复校验事件。</summary>
        private void Awake()
        {
            rosterRestoredUnregister = WSEventSystem.Register_Type<CharacterRosterRestoredEvent>(
                typeof(CharacterRosterRestoredEvent), HandleRosterRestored);
        }

        /// <summary>并发加载配置中的角色 Prefab，并在全部成功后按配置顺序原子提交。</summary>
        /// <param name="root">稳定 Player 持有的角色根节点。</param>
        /// <param name="driver">Player 持有的统一运动请求出口。</param>
        /// <param name="controller">稳定 PlayerController。</param>
        /// <param name="blackboard">稳定 PlayerStateBlackboard。</param>
        /// <param name="cancellationToken">销毁 Player 时使用的协作式取消令牌。</param>
        public async UniTask InitializeAsync(
            Transform root,
            IMotionDriver driver,
            PlayerController controller,
            PlayerStateBlackboard blackboard,
            CancellationToken cancellationToken)
        {
            switch (initializationState)
            {
                case CharacterInitializationState.Ready or CharacterInitializationState.Loading:
                    return;
                case CharacterInitializationState.Destroyed:
                    throw new InvalidOperationException("[CharacterManager] 已销毁，不能重新初始化。");
            }

            initializationState = CharacterInitializationState.Loading;
            cancellationRequested = false;
            try
            {
                if (root == null || driver == null || controller == null || blackboard == null)
                    throw new ArgumentNullException(nameof(CharacterManager), "CharacterManager 初始化依赖不能为空。");
                runtimeRoot = root;
                runtimeMotionDriver = driver;
                runtimePlayerController = controller;
                runtimeBlackboard = blackboard;
                runtimeLifetimeToken = cancellationToken;
                partyManager = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
                if (initialCharacterIds == null || initialCharacterIds.Length == 0)
                    throw new InvalidOperationException("[CharacterManager] 未配置初始 CharacterId。");

                bool hasConfiguredParty = false;
                // 如果当前已有 Party 存档，则不覆盖；否则按配置创建初始队伍。
                if (partyManager.Party != null)
                {
                    // 校验当前 Party 是否有有效角色，避免空新档在发现重复角色后留下部分已获得实例。
                    IReadOnlyList<CharacterId> restoredPartyIds = partyManager.CreateSnapshot();
                    for (int slotIndex = 0; slotIndex < restoredPartyIds.Count; slotIndex++)
                        hasConfiguredParty |= restoredPartyIds[slotIndex].IsValid;
                }
                // 如果当前没有 Party 存档，则按配置创建初始队伍。
                if (!hasConfiguredParty) partyManager.ConfigureInitialParty(initialCharacterIds);

                IReadOnlyList<CharacterId> partyCharacterIds = partyManager.CreateSnapshot();
                var configuredCharacterIds = new List<CharacterId>(partyCharacterIds.Count);
                for (int slotIndex = 0; slotIndex < partyCharacterIds.Count; slotIndex++)
                    if (partyCharacterIds[slotIndex].IsValid) configuredCharacterIds.Add(partyCharacterIds[slotIndex]);
                if (configuredCharacterIds.Count == 0)
                    throw new InvalidOperationException("[CharacterManager] 唯一队伍没有有效角色。");

                // 先校验队伍输入，避免空新档在发现重复角色后留下部分已获得实例。
                var ids = new HashSet<CharacterId>();
                for (int index = 0; index < configuredCharacterIds.Count; index++)
                {
                    CharacterId characterId = configuredCharacterIds[index];
                    if (!characterId.IsValid || !ids.Add(characterId))
                        throw new InvalidOperationException($"[CharacterManager] 初始 CharacterId 无效或重复：{characterId}。");
                }

                rosterManager = GameArchitecture.Interface.GetManager<CharacterRosterManager>();
                weaponInventoryManager = GameArchitecture.Interface.GetManager<WeaponInventoryManager>();
                if (rosterManager.GetInstances().Count == 0)
                {
                    // 当前尚无独立 Party 存档时，初始队伍同时承担空新档的角色获得入口。
                    for (int index = 0; index < configuredCharacterIds.Count; index++)
                    {
                        CharacterAcquisitionResult acquisition = rosterManager.AcquireCharacter(configuredCharacterIds[index]);
                        if (!acquisition.Succeeded && acquisition.Status != CharacterAcquisitionStatus.AlreadyOwned)
                            throw new InvalidOperationException($"[CharacterManager] 无法初始化初始角色 {configuredCharacterIds[index]}：{acquisition.Status}。");
                    }
                    Debug.Log($"[CharacterManager] 空角色档已按初始队伍创建，count={initialCharacterIds.Length}。");
                }
                else
                {
                    for (int index = 0; index < configuredCharacterIds.Count; index++)
                        if (!rosterManager.IsOwned(configuredCharacterIds[index]))
                            throw new InvalidOperationException($"[CharacterManager] 已有角色档缺少队伍角色：{configuredCharacterIds[index]}。");
                }

                var characterInstances = new CharacterInstance[configuredCharacterIds.Count];
                for (int index = 0; index < configuredCharacterIds.Count; index++)
                {
                    CharacterId characterId = configuredCharacterIds[index];
                    // Prefab 地址和静态配置从稳定 Instance 读取，避免队伍加载绕过 Roster 的 Config 权威。
                    characterInstances[index] = rosterManager.GetRequiredInstance(characterId);
                }

                // 所有 Prefab 请求并发发出；Actor 准备仍按固定槽位顺序执行。
                var loadTasks = new UniTask<GameObject>[characterInstances.Length];
                for (int index = 0; index < characterInstances.Length; index++)
                    loadTasks[index] = LoadCharacterPrefabAsync(characterInstances[index]);
                GameObject[] prefabs = await AwaitAllCharacterPrefabLoadsAsync(loadTasks);

                // 查看外部是否请求取消，或者在加载过程中被销毁。
                cancellationRequested |= cancellationToken.IsCancellationRequested;
                if (cancellationRequested || cancellationToken.IsCancellationRequested)
                {
                    RollbackFailedInitialization();
                    return;
                }

                var preparedActors = new List<CharacterActor>(prefabs.Length);
                for (int index = 0; index < prefabs.Length; index++)
                {
                    CharacterActor actor = await PrepareCharacterActorAsync(characterInstances[index], prefabs[index]);
                    preparedActors.Add(actor);
                }

                // 准备阶段全部成功后一次性公开队伍 Actor，避免 Ready 前暴露半支队伍。
                characterActors.AddRange(preparedActors);
                CharacterActor initial = Find(initialCharacterId);
                if (initial == null) initial = characterActors[0];
                SwitchInternal(initial);
                initializationState = CharacterInitializationState.Ready;
                Initialized?.Invoke();
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested || cancellationRequested)
                {
                    RollbackFailedInitialization();
                    return;
                }
                RollbackFailedInitialization();
                initializationState = CharacterInitializationState.Failed;
                InitializationFailed?.Invoke(exception);
            }
        }

        /// <summary>请求协作式取消；已发出的 Addressables 请求完成后会对称释放。</summary>
        internal void CancelInitialization()
        {
            cancellationRequested = true;
            if (initializationState == CharacterInitializationState.Loading)
                initializationState = CharacterInitializationState.Destroyed;
        }

        /// <summary>销毁时清理已创建实例和每一次成功加载的资源引用。</summary>
        private void OnDestroy()
        {
            rosterRestoredUnregister?.UnRegister();
            rosterRestoredUnregister = null;
            CancelInitialization();
            DestroyPreparedCharacterActors();
            ReleaseLoadedPrefabReferences();
            characterActors.Clear();
            ActiveCharacter = null;
            partyManager = null;
            rosterManager = null;
            weaponInventoryManager = null;
            runtimeRoot = null;
            runtimeMotionDriver = null;
            runtimePlayerController = null;
            runtimeBlackboard = null;
            initializationState = CharacterInitializationState.Destroyed;
        }

        /// <summary>失败或取消时销毁本批实例并释放每一次成功加载引用。</summary>
        private void RollbackFailedInitialization()
        {
            DestroyPreparedCharacterActors();
            characterActors.Clear();
            ActiveCharacter = null;
            ReleaseLoadedPrefabReferences();
        }

        /// <summary>按照 LoadAsync 成功次数逐项释放地址引用。</summary>
        private void ReleaseLoadedPrefabReferences()
        {
            for (int index = 0; index < loadedPrefabAddresses.Count; index++)
                ResSystem.Instance.UnLoad<GameObject>(loadedPrefabAddresses[index]);
            loadedPrefabAddresses.Clear();
        }

        /// <summary>销毁当前尚由 Manager 持有的 Actor 根对象；资源引用由调用方统一释放。</summary>
        private void DestroyPreparedCharacterActors()
        {
            int actorCount = instanceRootByCharacterActorMap.Count;
            foreach (KeyValuePair<CharacterActor, GameObject> actorAndRoot in instanceRootByCharacterActorMap)
            {
                if (actorAndRoot.Key != null) actorAndRoot.Key.StopRuntime();
                if (actorAndRoot.Value != null) Destroy(actorAndRoot.Value);
            }
            instanceRootByCharacterActorMap.Clear();
            if (actorCount > 0)
                Debug.Log($"[CharacterManager] 已销毁并清理角色 Actor 实例，count={actorCount}。", this);
        }

        #endregion

        #region 存档恢复

        /// <summary>存档恢复后校验已经加载的 Actor 是否仍对应有效稳定实例。</summary>
        /// <param name="restoredEvent">角色存档恢复事件。</param>
        private void HandleRosterRestored(CharacterRosterRestoredEvent restoredEvent)
        {
            if (!IsInitialized) return;
            for (int index = characterActors.Count - 1; index >= 0; index--)
            {
                CharacterActor actor = characterActors[index];
                if (actor == null || !rosterManager.TryGetInstance(actor.CharacterId, out CharacterInstance instance))
                {
                    if (ReferenceEquals(ActiveCharacter, actor)) ActiveCharacter = null;
                    actor?.StopRuntime();
                    characterActors.RemoveAt(index);
                    if (actor != null) DestroyTrackedCharacterActorRoot(actor);
                    Debug.LogError($"[CharacterManager] 存档恢复后角色 Actor 缺少对应实例，character={actor?.CharacterId}。");
                    continue;
                }
                if (!ReferenceEquals(actor.Instance, instance))
                    Debug.LogError($"[CharacterManager] 存档恢复破坏了稳定 CharacterInstance 引用，character={actor.CharacterId}。");
            }
            if (ActiveCharacter == null && characterActors.Count > 0)
                SwitchInternal(characterActors[0]);
        }

        #endregion

        #region 队伍查询与切换

        /// <summary>按角色标识查找已提交队伍中的角色。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <returns>找到时返回角色，否则返回空。</returns>
        public CharacterActor Find(CharacterId characterId)
        {
            for (int index = 0; index < characterActors.Count; index++)
                if (characterActors[index] != null && characterActors[index].CharacterId == characterId) return characterActors[index];
            return null;
        }

        /// <summary>按队伍顺序读取指定槽位角色。</summary>
        /// <param name="slotIndex">零基槽位下标。</param>
        /// <returns>槽位存在时返回角色，否则返回空。</returns>
        public CharacterActor GetCharacterAtSlot(int slotIndex)
        {
            if (!IsReady || partyManager == null || slotIndex < 0 || slotIndex >= CharacterParty.SlotCount)
                return null;
            CharacterId characterId = partyManager.GetCharacterIdAtSlot(slotIndex);
            return characterId.IsValid ? Find(characterId) : null;
        }

        /// <summary>只根据队伍内部状态尝试切换角色。</summary>
        /// <param name="characterId">目标角色标识。</param>
        /// <returns>明确的切换状态。</returns>
        public CharacterSwitchStatus TrySwitch(CharacterId characterId)
        {
            if (!IsReady) return CharacterSwitchStatus.NotInitialized;
            CharacterActor target = Find(characterId);
            if (target == null) return CharacterSwitchStatus.CharacterNotFound;
            if (ReferenceEquals(target, ActiveCharacter)) return CharacterSwitchStatus.AlreadyActive;
            if (target.IsBusy || ActiveCharacter != null && ActiveCharacter.IsBusy) return CharacterSwitchStatus.CharacterBusy;
            SwitchInternal(target);
            return CharacterSwitchStatus.Success;
        }

        /// <summary>按槽位尝试切换角色。</summary>
        /// <param name="slotIndex">零基槽位下标。</param>
        /// <returns>明确的切换状态。</returns>
        public CharacterSwitchStatus TrySwitchSlot(int slotIndex)
        {
            if (!IsReady) return CharacterSwitchStatus.NotInitialized;
            CharacterActor target = GetCharacterAtSlot(slotIndex);
            return target == null ? CharacterSwitchStatus.CharacterNotFound : TrySwitch(target.CharacterId);
        }

        /// <summary>完成表现停用、当前引用更新与同步事件发送。</summary>
        /// <param name="target">已经校验的目标角色。</param>
        private void SwitchInternal(CharacterActor target)
        {
            CharacterActor previous = ActiveCharacter;
            previous?.SetActivePresentation(false);
            ActiveCharacter = target;
            target.SetActivePresentation(true);
            ActiveCharacterChanged?.Invoke(previous, target);
        }

        #endregion

        #region 队伍槽位编辑事务

        /// <summary>异步编辑当前角色的队伍槽位，并在提交前完成新 Actor 的准备。</summary>
        /// <param name="characterId">要调整位置的已拥有角色。</param>
        /// <param name="slotIndex">零基目标槽位；负一表示退出队伍。</param>
        /// <returns>包含校验、忙碌和资源加载结果的编辑状态。</returns>
        public async UniTask<E_CharacterPartyEditStatus> ChangePartySlotAsync(CharacterId characterId, int slotIndex)
        {
            if (!IsReady || partyManager == null || rosterManager == null)
            {
                Debug.LogWarning($"[CharacterManager] 队伍位置请求被拒绝，Manager 尚未 Ready，character={characterId}, slot={slotIndex}。", this);
                return E_CharacterPartyEditStatus.NotReady;
            }
            if (partyEditInProgress)
            {
                Debug.LogWarning($"[CharacterManager] 队伍位置请求被拒绝，已有事务处理中，character={characterId}, slot={slotIndex}。", this);
                return E_CharacterPartyEditStatus.EditInProgress;
            }

            partyEditInProgress = true;
            CharacterActor preparedActor = null;
            try
            {
                if (IsCharacterWorkCancelled()) return E_CharacterPartyEditStatus.Cancelled;
                E_CharacterPartyEditStatus validationStatus =
                    partyManager.TryCreateSlotEdit(characterId, slotIndex, out CharacterParty candidateParty);
                if (validationStatus != E_CharacterPartyEditStatus.Success)
                {
                    Debug.LogWarning($"[CharacterManager] 队伍位置请求未进入提交，character={characterId}, slot={slotIndex}, status={validationStatus}。", this);
                    return validationStatus;
                }

                IReadOnlyList<CharacterId> currentSlots = partyManager.CreateSnapshot();
                IReadOnlyList<CharacterId> candidateSlots = candidateParty.CreateSnapshot();
                var removedActors = new List<CharacterActor>(1);
                for (int slot = 0; slot < currentSlots.Count; slot++)
                {
                    CharacterId currentId = currentSlots[slot];
                    if (!currentId.IsValid) continue;
                    CharacterActor currentActor = Find(currentId);
                    if (currentActor == null)
                        return E_CharacterPartyEditStatus.NotReady;
                    if (candidateParty.FindSlot(currentId) >= 0) continue;
                    if (currentActor.IsBusy)
                        return E_CharacterPartyEditStatus.CharacterBusy;
                    removedActors.Add(currentActor);
                }

                CharacterId missingCandidateId = default;
                for (int slot = 0; slot < candidateSlots.Count; slot++)
                {
                    CharacterId candidateId = candidateSlots[slot];
                    // 已经加载的角色不需要异步准备，且不能重复加载。
                    if (!candidateId.IsValid || Find(candidateId) != null) continue;
                    if (missingCandidateId.IsValid)
                        throw new InvalidOperationException("[CharacterManager] 单次槽位编辑产生了多个未加载队伍角色。");
                    missingCandidateId = candidateId;
                }

                if (missingCandidateId.IsValid)
                {
                    try
                    {
                        CharacterInstance instance = rosterManager.GetRequiredInstance(missingCandidateId);
                        GameObject prefab = await LoadCharacterPrefabAsync(instance);
                        preparedActor = await PrepareCharacterActorAsync(instance, prefab);
                    }
                    catch (OperationCanceledException)
                    {
                        return E_CharacterPartyEditStatus.Cancelled;
                    }
                    catch (Exception exception)
                    {
                        if (this == null || IsCharacterWorkCancelled())
                            return E_CharacterPartyEditStatus.Cancelled;
                        Debug.LogError($"[CharacterManager] 新队伍角色准备失败，character={missingCandidateId}，原因={exception.Message}。", this);
                        return E_CharacterPartyEditStatus.LoadFailed;
                    }

                    if (this == null || IsCharacterWorkCancelled())
                    {
                        if (this != null && preparedActor != null)
                            DiscardPreparedCharacterActor(preparedActor);
                        return E_CharacterPartyEditStatus.Cancelled;
                    }
                }

                // 异步加载期间技能和角色拥有状态可能变化，提交前重新校验将被移出角色与候选成员。
                for (int index = 0; index < removedActors.Count; index++)
                {
                    CharacterActor removedActor = removedActors[index];
                    if (removedActor == null || !rosterManager.IsOwned(removedActor.CharacterId))
                    {
                        DiscardPreparedCharacterActor(preparedActor);
                        return E_CharacterPartyEditStatus.NotReady;
                    }
                    if (removedActor.IsBusy)
                    {
                        DiscardPreparedCharacterActor(preparedActor);
                        return E_CharacterPartyEditStatus.CharacterBusy;
                    }
                }
                for (int slot = 0; slot < candidateSlots.Count; slot++)
                {
                    CharacterId candidateId = candidateSlots[slot];
                    if (!candidateId.IsValid) continue;
                    if (!rosterManager.IsOwned(candidateId))
                    {
                        DiscardPreparedCharacterActor(preparedActor);
                        return E_CharacterPartyEditStatus.CharacterNotOwned;
                    }
                    if (ResolveCandidateActor(candidateId, preparedActor) == null)
                    {
                        DiscardPreparedCharacterActor(preparedActor);
                        return E_CharacterPartyEditStatus.NotReady;
                    }
                }

                // 当前操控角色可能被移出队伍，必须在提交前找到可接替的角色，否则拒绝提交。
                CharacterActor activeCharacter = ActiveCharacter;
                bool activeCharacterLeavesParty = activeCharacter != null && candidateParty.FindSlot(activeCharacter.CharacterId) < 0;
                CharacterActor nextActiveCharacter = activeCharacterLeavesParty
                    ? ResolveReplacementActiveCharacter(activeCharacter, candidateParty, preparedActor)
                    : activeCharacter;
                if (activeCharacterLeavesParty && nextActiveCharacter == null)
                {
                    DiscardPreparedCharacterActor(preparedActor);
                    Debug.LogError("[CharacterManager] 队伍编辑候选没有可接替的操控角色，拒绝提交。", this);
                    return E_CharacterPartyEditStatus.NotReady;
                }
                if (ActiveCharacter == null)
                {
                    DiscardPreparedCharacterActor(preparedActor);
                    Debug.LogError("[CharacterManager] 当前操控角色缺失，拒绝提交队伍编辑。", this);
                    return E_CharacterPartyEditStatus.NotReady;
                }

                // 所有可能异步失败的步骤已结束；以下同步更新运行时列表、Party 与操控对象，再发布领域事件。
                if (preparedActor != null) characterActors.Add(preparedActor);
                for (int index = 0; index < removedActors.Count; index++)
                    characterActors.Remove(removedActors[index]);
                partyManager.CommitSlotEdit(candidateParty);
                if (activeCharacterLeavesParty) SwitchInternal(nextActiveCharacter);
                for (int index = 0; index < removedActors.Count; index++)
                    ReleaseCharacterActor(removedActors[index]);
                partyManager.PublishSlotEditChanged();
                Debug.Log($"[CharacterManager] 队伍位置编辑完成，character={characterId}, slot={slotIndex}, " +
                          $"activeCharacter={ActiveCharacter.CharacterId}, memberCount={CountValidSlots(candidateSlots)}。", this);
                return E_CharacterPartyEditStatus.Success;
            }
            finally
            {
                if (this != null) partyEditInProgress = false;
            }
        }

        #endregion

        #region Prefab 加载与 Actor 准备

        // 资源获取：每次成功 LoadAsync 都记录独立引用，确保重复地址也能逐次 UnLoad。

        /// <summary>按稳定角色实例加载其配置指定的 Prefab 并登记资源引用。</summary>
        /// <param name="instance">已拥有且具有有效角色配置的实例。</param>
        /// <returns>加载完成并通过 Player 生命周期校验的角色 Prefab。</returns>
        private async UniTask<GameObject> LoadCharacterPrefabAsync(CharacterInstance instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            CharacterConfig config = instance.Config;
            config.Validate();
            string prefabAddress = config.PrefabAddress;
            if (this == null || IsCharacterWorkCancelled())
                throw new OperationCanceledException("Player 生命周期已结束。");

            Debug.Log($"[CharacterManager] 开始加载角色 Prefab，character={instance.CharacterId}, prefab={prefabAddress}。", this);
            GameObject prefab = await ResSystem.Instance.LoadAsync<GameObject>(prefabAddress);
            if (prefab == null) throw new InvalidOperationException($"角色 Prefab 加载失败：{prefabAddress}。");
            if (this == null)
            {
                ResSystem.Instance.UnLoad<GameObject>(prefabAddress);
                throw new OperationCanceledException("CharacterManager 已销毁。");
            }

            loadedPrefabAddresses.Add(prefabAddress);
            if (IsCharacterWorkCancelled())
            {
                ReleaseLoadedPrefabReference(prefabAddress);
                throw new OperationCanceledException("Player 生命周期已结束。");
            }
            Debug.Log($"[CharacterManager] 角色 Prefab 加载完成，character={instance.CharacterId}, prefab={prefabAddress}。", this);
            return prefab;
        }

        /// <summary>等待所有已并发发出的 Prefab 请求结束，再传播首个加载错误。</summary>
        /// <param name="loadTasks">已经启动的并发角色 Prefab 加载任务。</param>
        /// <returns>与任务输入顺序一致的 Prefab 数组。</returns>
        private static async UniTask<GameObject[]> AwaitAllCharacterPrefabLoadsAsync(
            IReadOnlyList<UniTask<GameObject>> loadTasks)
        {
            var prefabs = new GameObject[loadTasks.Count];
            Exception firstLoadException = null;
            for (int index = 0; index < loadTasks.Count; index++)
            {
                try
                {
                    prefabs[index] = await loadTasks[index];
                }
                catch (Exception exception)
                {
                    // 逐个观察已启动任务，确保其他成功请求也完成引用登记后再由初始化统一回滚。
                    firstLoadException ??= exception;
                }
            }

            if (firstLoadException != null) ExceptionDispatchInfo.Capture(firstLoadException).Throw();
            return prefabs;
        }

        // Actor 准备：初始化和队伍编辑共用同一套绑定、武器同步及隐藏预热顺序。

        /// <summary>实例化并准备隐藏角色 Actor；不修改正式队伍或当前操控角色。</summary>
        /// <param name="instance">Actor 对应的稳定角色实例。</param>
        /// <param name="prefab">由 LoadCharacterPrefabAsync 成功加载并登记引用的 Prefab。</param>
        /// <returns>完成配置校验、运行时绑定、武器同步和待机预热的 Actor。</returns>
        private async UniTask<CharacterActor> PrepareCharacterActorAsync(CharacterInstance instance, GameObject prefab)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            CharacterConfig config = instance.Config;
            string prefabAddress = config.PrefabAddress;
            GameObject actorObject = null;
            CharacterActor actor = null;
            try
            {
                if (this == null || IsCharacterWorkCancelled())
                    throw new OperationCanceledException("Player 生命周期已结束。");
                Transform container = actorContainer != null ? actorContainer : runtimeRoot;
                actorObject = Instantiate(prefab, container);
                CharacterActor[] actors = actorObject.GetComponentsInChildren<CharacterActor>(true);
                if (actors.Length != 1)
                    throw new InvalidOperationException($"角色 Prefab '{prefabAddress}' 必须包含唯一 CharacterActor。");

                actor = actors[0];
                if (!ReferenceEquals(actor.Config, config))
                    throw new InvalidOperationException($"Actor '{actor.name}' 的 Config 与 CharacterId '{instance.CharacterId}' 不一致。");
                instanceRootByCharacterActorMap.Add(actor, actorObject);
                actor.BindRuntime(runtimeRoot, runtimeMotionDriver, runtimePlayerController, runtimeBlackboard,
                    instance, rosterManager, weaponInventoryManager);
                actor.InitializeFromInstance();
                actor.SetActivePresentation(false);
                await actor.InitializeWeaponPresentationAsync();
                if (this == null) throw new OperationCanceledException("CharacterManager 已销毁。");
                if (IsCharacterWorkCancelled()) throw new OperationCanceledException("Player 生命周期已结束。");
                actor.PrimeIdlePose();
                actor.SetActivePresentation(false);
                Debug.Log($"[CharacterManager] 角色 Actor 准备完成并保持隐藏，character={instance.CharacterId}, prefab={prefabAddress}。", this);
                return actor;
            }
            catch
            {
                // Manager 销毁时 OnDestroy 已清理已登记根对象与资源引用；存活时由本方法对称回滚本次准备。
                if (this != null) DestroyCharacterActorAndReleasePrefab(actor, actorObject, prefabAddress);
                throw;
            }
        }

        /// <summary>销毁追踪中的 Actor 根对象，供存档恢复清理失效成员。</summary>
        /// <param name="actor">已从角色列表移除的 Actor。</param>
        private void DestroyTrackedCharacterActorRoot(CharacterActor actor)
        {
            if (!instanceRootByCharacterActorMap.TryGetValue(actor, out GameObject actorRoot))
                throw new InvalidOperationException($"[CharacterManager] 缺少 Actor 根对象追踪记录：{actor.CharacterId}。");
            instanceRootByCharacterActorMap.Remove(actor);
            if (actorRoot != null) Destroy(actorRoot);
        }

        #endregion

        #region 队伍提交辅助

        // 角色替换选择：主动角色离队时优先使用原槽位角色，再回退到位置最靠前的成员。

        /// <summary>为主动队伍成员离队时选择同槽替换者，否则选择位置最靠前的剩余成员。</summary>
        /// <param name="leavingActor">即将离队的当前操控角色。</param>
        /// <param name="candidateParty">待提交队伍。</param>
        /// <param name="preparedActor">本次刚加载的角色；可能为空。</param>
        /// <returns>接替操控的队伍 Actor。</returns>
        private CharacterActor ResolveReplacementActiveCharacter(
            CharacterActor leavingActor, CharacterParty candidateParty, CharacterActor preparedActor)
        {
            int previousSlot = partyManager.FindSlot(leavingActor.CharacterId);
            CharacterActor replacement = ResolveCandidateActor(
                candidateParty.GetCharacterIdAtSlot(previousSlot), preparedActor);
            if (replacement != null) return replacement;

            for (int slot = 0; slot < CharacterParty.SlotCount; slot++)
            {
                replacement = ResolveCandidateActor(candidateParty.GetCharacterIdAtSlot(slot), preparedActor);
                if (replacement != null) return replacement;
            }
            return null;
        }

        /// <summary>通过稳定角色标识解析现有角色，必要时匹配本次预备实例。</summary>
        /// <param name="characterId">候选槽位中的角色标识。</param>
        /// <param name="preparedActor">本次新增的隐藏 Actor。</param>
        /// <returns>找到的队伍 Actor。</returns>
        private CharacterActor ResolveCandidateActor(CharacterId characterId, CharacterActor preparedActor)
        {
            if (!characterId.IsValid) return null;
            if (preparedActor != null && preparedActor.CharacterId == characterId) return preparedActor;
            return Find(characterId);
        }

        /// <summary>计算队伍快照中的有效角色数，用于提交摘要日志。</summary>
        /// <param name="slotIds">固定四槽位快照。</param>
        /// <returns>非空角色槽位数量。</returns>
        private static int CountValidSlots(IReadOnlyList<CharacterId> slotIds)
        {
            int count = 0;
            for (int index = 0; index < slotIds.Count; index++)
                if (slotIds[index].IsValid) count++;
            return count;
        }

        #endregion

        #region Actor 生命周期与资源回收

        // 取消检查：Prefab 加载与 Actor 准备阶段共用 Player 和 Manager 生命周期状态。

        /// <summary>判断 Player 或 CharacterManager 是否已请求取消当前角色准备工作。</summary>
        /// <returns>取消请求已生效时返回 true。</returns>
        private bool IsCharacterWorkCancelled() => cancellationRequested || runtimeLifetimeToken.IsCancellationRequested;

        // 对象回收：准备失败或候选被拒绝时销毁预备根对象；正式退队时释放已提交 Actor。

        /// <summary>销毁 Actor 根对象并释放该实例对应的单次 Prefab 引用。</summary>
        /// <param name="actor">已经找到的角色组件；可能为空。</param>
        /// <param name="actorObject">已实例化的根对象；可能为空。</param>
        /// <param name="prefabAddress">本次成功加载且需要释放的 Addressables 地址。</param>
        private void DestroyCharacterActorAndReleasePrefab(CharacterActor actor, GameObject actorObject, string prefabAddress)
        {
            if (actor != null && instanceRootByCharacterActorMap.Remove(actor))
            {
                actor.StopRuntime();
            }

            if (actorObject != null) Destroy(actorObject);
            if (!string.IsNullOrWhiteSpace(prefabAddress)) ReleaseLoadedPrefabReference(prefabAddress);
        }

        /// <summary>回滚已经准备但尚未提交到正式队伍的 Actor。</summary>
        /// <param name="actor">未提交的新 Actor；可能为空。</param>
        private void DiscardPreparedCharacterActor(CharacterActor actor)
        {
            if (actor == null) return;
            if (!instanceRootByCharacterActorMap.TryGetValue(actor, out GameObject actorRoot))
                throw new InvalidOperationException($"[CharacterManager] 缺少预备 Actor 根对象追踪记录：{actor.CharacterId}。");
            DestroyCharacterActorAndReleasePrefab(actor, actorRoot, actor.Config.PrefabAddress);
        }

        /// <summary>销毁已从当前队伍移除的 Actor 并释放其 Prefab 加载引用。</summary>
        /// <param name="actor">已经从当前队伍列表移除的角色。</param>
        private void ReleaseCharacterActor(CharacterActor actor)
        {
            if (actor == null) return;
            string prefabAddress = actor.Config.PrefabAddress;
            CharacterId characterId = actor.CharacterId;
            if (!instanceRootByCharacterActorMap.TryGetValue(actor, out GameObject actorRoot))
                throw new InvalidOperationException($"[CharacterManager] 缺少离队 Actor 根对象追踪记录：{characterId}。");
            DestroyCharacterActorAndReleasePrefab(actor, actorRoot, prefabAddress);
            Debug.Log($"[CharacterManager] 已释放退队角色 Actor，character={characterId}, prefab={prefabAddress}。", this);
        }

        /// <summary>按地址释放一次成功加载记录，保留重复地址的独立引用计数。</summary>
        /// <param name="prefabAddress">已加载角色 Prefab 地址。</param>
        private void ReleaseLoadedPrefabReference(string prefabAddress)
        {
            int loadedIndex = loadedPrefabAddresses.LastIndexOf(prefabAddress);
            if (loadedIndex < 0)
                throw new InvalidOperationException($"[CharacterManager] 缺少待释放的 Prefab 引用记录：{prefabAddress}。");
            loadedPrefabAddresses.RemoveAt(loadedIndex);
            ResSystem.Instance.UnLoad<GameObject>(prefabAddress);
            Debug.Log($"[CharacterManager] 已释放一次角色 Prefab 引用，prefab={prefabAddress}。", this);
        }

        #endregion

        #region 阶段门禁

        /// <summary>推进全部角色 ASC；未 Ready 时静默不推进。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void AdvanceAbilityFrame(float deltaTime)
        {
            if (!IsReady) return;
            for (int index = 0; index < characterActors.Count; index++) characterActors[index].TickAbility(deltaTime);
        }

        /// <summary>推进当前角色输入与 Locomotion；未 Ready 时不消费输入。</summary>
        /// <param name="inputRequests">输入请求缓冲区。</param>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void AdvanceActiveFrame(IPlayerInputRequestBuffer inputRequests, float deltaTime)
        {
            if (!IsReady) return;
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            if (active.IsActionPaused) return;
            // Action Arbiter 必须先于 Locomotion Tick；Jump 取消 GA 后，本帧原有 FSM Transition 即可提交目标路径。
            active.AdvanceActionFrame(inputRequests, deltaTime);
            active.Locomotion.Tick(deltaTime);
        }

        /// <summary>推进当前角色物理阶段；未 Ready 时返回 false，阻止 MotionDriver 结算。</summary>
        /// <param name="fixedDeltaTime">本物理步时长。</param>
        /// <returns>实际推进角色阶段时返回 true。</returns>
        internal bool AdvanceFixedStep(float fixedDeltaTime)
        {
            if (!IsReady) return false;
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            if (active.IsActionPaused) return false;
            active.FixedTickAbility(fixedDeltaTime);
            active.Locomotion.FixedTick(fixedDeltaTime);
            return true;
        }

        /// <summary>推进全队 ASC 和当前 Locomotion 延迟阶段；未 Ready 时静默返回。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void AdvanceLateFrame(float deltaTime)
        {
            if (!IsReady) return;
            for (int index = 0; index < characterActors.Count; index++) characterActors[index].LateTickAbility(deltaTime);
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            if (!active.IsActionPaused) active.Locomotion.LateTick(deltaTime);
        }

        /// <summary>处理角色槽位输入；Loading/Failed 阶段不消费缓冲请求。</summary>
        /// <param name="inputRequests">输入请求缓冲区。</param>
        internal void ProcessSwitchInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            if (!IsReady) return;
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            for (int slotIndex = 0; slotIndex < characterSlotInputTypes.Length; slotIndex++)
            {
                // 查询是否有缓冲按下请求，未按下或已被其他系统消费时跳过。
                if (!inputRequests.TryGetRequest(characterSlotInputTypes[slotIndex], out IReadOnlyPlayerInputRequest request) || !request.HasBufferedPress)
                    continue;
                CharacterSwitchStatus status = TrySwitchSlot(slotIndex);
                if (status != CharacterSwitchStatus.CharacterBusy)
                    inputRequests.TryConfirmConsumed(request.PressHandle);
                return;
            }
        }

        /// <summary>推进当前角色 Animator 阶段；未 Ready 或来源非当前角色时返回 false。</summary>
        /// <param name="source">产生 AnimatorMove 的角色。</param>
        /// <param name="deltaPosition">根位移增量。</param>
        /// <param name="deltaRotation">根旋转增量。</param>
        /// <param name="evaluationDeltaTime">Animator 求值时长。</param>
        /// <returns>角色阶段实际推进时返回 true。</returns>
        internal bool TryAdvanceAnimatorStep(CharacterActor source, Vector3 deltaPosition, Quaternion deltaRotation, float evaluationDeltaTime)
        {
            if (!IsReady || !ReferenceEquals(source, ActiveCharacter) || source.IsActionPaused) return false;
            source.UpdateAnimationMoveAbility(deltaPosition, deltaRotation);
            source.Locomotion.UpdateAnimationMove(deltaPosition, deltaRotation, evaluationDeltaTime);
            return true;
        }

        #endregion
    }
}
