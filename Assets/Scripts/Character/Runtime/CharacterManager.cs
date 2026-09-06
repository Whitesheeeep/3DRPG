using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RPG.Character.State;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.ResLoadModule;

namespace RPG.Character
{
    /// <summary>管理角色配置的异步加载、原子队伍提交与当前角色阶段门禁。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖同一 CharacterRoot 子树中的 CharacterActor；角色 Prefab 由 CharacterConfig 的 Addressables 地址异步加载。")]
    public sealed class CharacterManager : MonoBehaviour
    {
        #region 配置与依赖字段

        [SerializeField] private Transform actorContainer;
        [SerializeField, CharacterIdDropdown] private CharacterId[] initialCharacterIds = Array.Empty<CharacterId>();
        [SerializeField, CharacterIdDropdown] private CharacterId initialCharacterId;
        [SerializeField, ReadOnly, Tooltip("已通过配置和 Marker 校验的队伍角色；仅用于调试。")]
        private readonly List<CharacterActor> characters = new();
        // 每个元素对应一次成功的 LoadAsync；相同地址不能合并，否则会破坏底层引用计数。
        private readonly List<string> loadedPrefabAddresses = new();
        private readonly List<CharacterActor> spawnedCharacters = new();
        private CharacterInitializationState initializationState = CharacterInitializationState.Uninitialized;
        private bool cancellationRequested;

        // 固定槽位输入只在 Ready 阶段查询，不把 PlayerInputController 保存为 Manager 生命周期依赖。
        private static readonly PlayerInputType[] characterSlotInputTypes =
        {
            PlayerInputType.CharacterSlot1,
            PlayerInputType.CharacterSlot2,
            PlayerInputType.CharacterSlot3,
            PlayerInputType.CharacterSlot4
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
        public IReadOnlyList<CharacterActor> Characters => characters;
        /// <summary>获取当前由玩家操控的角色。</summary>
        public CharacterActor ActiveCharacter { get; private set; }

        #endregion

        #region 异步初始化

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
                if (initialCharacterIds == null || initialCharacterIds.Length == 0)
                    throw new InvalidOperationException("[CharacterManager] 未配置初始 CharacterId。");

                var configManager = CharacterConfigManager.Instance;
                var configs = new CharacterConfig[initialCharacterIds.Length];
                var ids = new HashSet<CharacterId>();
                for (int index = 0; index < initialCharacterIds.Length; index++)
                {
                    CharacterId characterId = initialCharacterIds[index];
                    if (!characterId.IsValid || !ids.Add(characterId))
                        throw new InvalidOperationException($"[CharacterManager] 初始 CharacterId 无效或重复：{characterId}。");
                    configs[index] = configManager.GetRequiredConfig(characterId);
                    configs[index].Validate();
                }

                // 所有请求并发发出，完成顺序不影响后续按 initialCharacterIds 排列的队伍顺序。
                var loadTasks = new UniTask<GameObject>[configs.Length];
                for (int index = 0; index < configs.Length; index++)
                    loadTasks[index] = ResSystem.Instance.LoadAsync<GameObject>(configs[index].PrefabAddress);
                GameObject[] prefabs = await UniTask.WhenAll(loadTasks);

                // 查看外部是否请求取消，或者在加载过程中被销毁。
                for (int index = 0; index < prefabs.Length; index++)
                {
                    cancellationRequested |= cancellationToken.IsCancellationRequested;
                    if (prefabs[index] != null) loadedPrefabAddresses.Add(configs[index].PrefabAddress);
                }
                if (cancellationRequested || cancellationToken.IsCancellationRequested)
                {
                    RollbackFailedInitialization();
                    return;
                }

                for (int index = 0; index < prefabs.Length; index++)
                    if (prefabs[index] == null)
                        throw new InvalidOperationException($"[CharacterManager] 角色 '{configs[index].CharacterId}' 的 Prefab 加载失败。");

                Transform container = actorContainer != null ? actorContainer : root;
                for (int index = 0; index < prefabs.Length; index++)
                {
                    GameObject instanceObject = Instantiate(prefabs[index], container);
                    CharacterActor[] actors = instanceObject.GetComponentsInChildren<CharacterActor>(true);
                    if (actors.Length != 1)
                        throw new InvalidOperationException($"[CharacterManager] Prefab '{configs[index].PrefabAddress}' 必须包含唯一 CharacterActor。");
                    CharacterActor actor = actors[0];
                    if (!ReferenceEquals(actor.Config, configs[index]))
                        throw new InvalidOperationException($"[CharacterManager] Actor '{actor.name}' 的 Config 与 CharacterId '{configs[index].CharacterId}' 不一致。");

                    // 绑定和初始化在提交到 characters 之前完成，失败时可整批销毁。
                    actor.BindRuntime(root, driver, controller, blackboard);
                    actor.InitializeFromConfig();
                    actor.PrimeIdlePose();
                    actor.SetActivePresentation(false);
                    spawnedCharacters.Add(actor);
                    characters.Add(actor);
                }

                CharacterActor initial = Find(initialCharacterId);
                if (initial == null) initial = characters[0];
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
            CancelInitialization();
            for (int index = 0; index < spawnedCharacters.Count; index++)
                if (spawnedCharacters[index] != null) Destroy(spawnedCharacters[index].gameObject);
            spawnedCharacters.Clear();
            ReleaseLoadedPrefabReferences();
            characters.Clear();
            ActiveCharacter = null;
            initializationState = CharacterInitializationState.Destroyed;
        }

        /// <summary>失败或取消时销毁本批实例并释放每一次成功加载引用。</summary>
        private void RollbackFailedInitialization()
        {
            for (int index = 0; index < spawnedCharacters.Count; index++)
                if (spawnedCharacters[index] != null) Destroy(spawnedCharacters[index].gameObject);
            spawnedCharacters.Clear();
            characters.Clear();
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

        #endregion

        #region 队伍查询与切换

        /// <summary>按角色标识查找已提交队伍中的角色。</summary>
        /// <param name="characterId">稳定角色标识。</param>
        /// <returns>找到时返回角色，否则返回空。</returns>
        public CharacterActor Find(CharacterId characterId)
        {
            for (int index = 0; index < characters.Count; index++)
                if (characters[index].CharacterId == characterId) return characters[index];
            return null;
        }

        /// <summary>按队伍顺序读取指定槽位角色。</summary>
        /// <param name="slotIndex">零基槽位下标。</param>
        /// <returns>槽位存在时返回角色，否则返回空。</returns>
        public CharacterActor GetCharacterAtSlot(int slotIndex) =>
            slotIndex >= 0 && slotIndex < characters.Count ? characters[slotIndex] : null;

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

        #region 阶段门禁

        /// <summary>推进全部角色 ASC；未 Ready 时静默不推进。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickCharacters(float deltaTime)
        {
            if (!IsReady) return;
            for (int index = 0; index < characters.Count; index++) characters[index].TickAbility(deltaTime);
        }

        /// <summary>推进当前角色输入与 Locomotion；未 Ready 时不消费输入。</summary>
        /// <param name="inputRequests">输入请求缓冲区。</param>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void TickActiveCharacter(IPlayerInputRequestBuffer inputRequests, float deltaTime)
        {
            if (!IsReady) return;
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            active.ProcessAbilityInputRequests(inputRequests);
            active.Locomotion.Tick(deltaTime);
        }

        /// <summary>推进当前角色物理阶段；未 Ready 时返回 false，阻止 MotionDriver 结算。</summary>
        /// <param name="fixedDeltaTime">本物理步时长。</param>
        /// <returns>实际推进角色阶段时返回 true。</returns>
        internal bool FixedTickActiveCharacter(float fixedDeltaTime)
        {
            if (!IsReady) return false;
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            active.FixedTickAbility(fixedDeltaTime);
            active.Locomotion.FixedTick(fixedDeltaTime);
            return true;
        }

        /// <summary>推进全队 ASC 和当前 Locomotion 延迟阶段；未 Ready 时静默返回。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTickCharacters(float deltaTime)
        {
            if (!IsReady) return;
            for (int index = 0; index < characters.Count; index++) characters[index].LateTickAbility(deltaTime);
            CharacterActor active = ActiveCharacter ?? throw new InvalidOperationException("[CharacterManager] Ready 状态缺少 ActiveCharacter。");
            active.Locomotion.LateTick(deltaTime);
        }

        /// <summary>处理角色槽位输入；Loading/Failed 阶段不消费缓冲请求。</summary>
        /// <param name="inputRequests">输入请求缓冲区。</param>
        internal void ProcessSwitchInputRequests(IPlayerInputRequestBuffer inputRequests)
        {
            if (!IsReady) return;
            if (inputRequests == null) throw new ArgumentNullException(nameof(inputRequests));
            for (int slotIndex = 0; slotIndex < characterSlotInputTypes.Length; slotIndex++)
            {
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
        internal bool TryUpdateAnimationMove(CharacterActor source, Vector3 deltaPosition, Quaternion deltaRotation, float evaluationDeltaTime)
        {
            if (!IsReady || !ReferenceEquals(source, ActiveCharacter)) return false;
            source.UpdateAnimationMoveAbility(deltaPosition, deltaRotation);
            source.Locomotion.UpdateAnimationMove(deltaPosition, deltaRotation, evaluationDeltaTime);
            return true;
        }

        #endregion
    }
}
