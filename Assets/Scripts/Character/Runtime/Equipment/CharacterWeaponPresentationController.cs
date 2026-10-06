using System;
using Cysharp.Threading.Tasks;
using RPG.ItemSystem;
using RPG.Markers;
using RPG.SkillSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using WS_Modules;
using WS_Modules.CustomEventSystem;
using WS_Modules.ResLoadModule;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>读取角色实例的装备权威状态，并异步同步对应的武器模型与技能挂点。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖需由角色 Prefab 显式绑定：武器模型容器、MarkerProvider、SkillRuntimeHost 与 WeaponRoot/WeaponTip MarkerKey。预览武器仅作为编辑器展示；运行时会按 CharacterInstance 装备实例重新解析。加载的武器 Prefab 必须各有一个根与尖端 Marker，其冗余 MultiParentConstraint 会被停用。")]
    public sealed class CharacterWeaponPresentationController : MonoBehaviour
    {
        #region 依赖字段

        // 依赖字段：武器的挂载、Marker 解析和技能节点都由 Prefab 显式绑定，不按名称搜索角色层级。
        [SerializeField, Required, LabelText("武器模型容器")]
        private Transform weaponModelRoot;
        [SerializeField, Required, LabelText("角色 Marker 提供器")]
        private MarkerProvider markerProvider;
        [SerializeField, Required, LabelText("技能运行时宿主")]
        private SkillRuntimeHost skillRuntimeHost;
        [SerializeField, Required, LabelText("武器根 Marker")]
        private MarkerKey weaponRootMarkerKey;
        [SerializeField, Required, LabelText("武器尖端 Marker")]
        private MarkerKey weaponTipMarkerKey;
        [SerializeField, LabelText("编辑器预览武器")]
        private GameObject editorPreviewWeaponModel;

        #endregion

        #region 运行时绑定与资源状态

        // 运行时依赖由 CharacterActor 注入，事件通知只触发从 roster 重新读取权威实例。
        private CharacterInstance characterInstance;
        private CharacterRosterManager characterRosterManager;
        private WeaponInventoryManager weaponInventoryManager;
        private IUnRegister characterInstanceChangedUnregister;
        private IUnRegister rosterRestoredUnregister;
        private GameObject currentWeaponModel;
        private string currentWeaponAddress;
        private string loadedWeaponAddress;
        private EquipmentInstanceId lastSynchronizedWeaponInstanceId;
        private int weaponSyncRequestVersion;
        private bool isRuntimeBound;
        private bool isInitialSynchronizationInProgress;
        private bool hasPendingWeaponSynchronization;
        private bool hasSynchronizedWeaponState;

        #endregion

        #region 事件

        /// <summary>武器模型层级变化后通知 Actor 刷新 Renderer 缓存。</summary>
        public event Action PresentationModelChanged;

        #endregion

        #region Unity 生命周期

        /// <summary>销毁表现控制器时注销事件并释放持有的武器 Addressables 引用。</summary>
        private void OnDestroy()
        {
            StopRuntime();
        }

        #endregion

        #region 运行时绑定与首次同步

        /// <summary>绑定稳定角色实例及解析武器实例所需的运行时管理器。</summary>
        /// <param name="instance">当前 Actor 对应的稳定角色实例。</param>
        /// <param name="rosterManager">提供角色实例权威状态的 Manager。</param>
        /// <param name="inventoryManager">根据装备实例 ID 查询武器实例的 Manager。</param>
        /// <exception cref="InvalidOperationException">Prefab 依赖未绑定、重复绑定或预览模型位置错误时抛出。</exception>
        public void BindRuntime(
            CharacterInstance instance,
            CharacterRosterManager rosterManager,
            WeaponInventoryManager inventoryManager)
        {
            if (isRuntimeBound)
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 已绑定运行时武器表现。");
            if (weaponModelRoot == null || markerProvider == null || skillRuntimeHost == null ||
                weaponRootMarkerKey == null || weaponTipMarkerKey == null)
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 的武器表现 Prefab 依赖未完整绑定。");

            characterInstance = instance ?? throw new ArgumentNullException(nameof(instance));
            characterRosterManager = rosterManager ?? throw new ArgumentNullException(nameof(rosterManager));
            weaponInventoryManager = inventoryManager ?? throw new ArgumentNullException(nameof(inventoryManager));
            if (editorPreviewWeaponModel != null &&
                !editorPreviewWeaponModel.transform.IsChildOf(weaponModelRoot))
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 的预览武器必须位于 WeaponModelRoot 子树内。");

            // Prefab 中的模型仅用于编辑器预览；首次权威同步会按实例 ID 决定保留、替换或清除它。
            currentWeaponModel = editorPreviewWeaponModel;
            if (currentWeaponModel != null)
                DisableEmbeddedConstraints(currentWeaponModel);
            isRuntimeBound = true;
            characterInstanceChangedUnregister = WSEventSystem.Register_Type<CharacterInstanceChangedEvent>(
                typeof(CharacterInstanceChangedEvent), HandleCharacterInstanceChanged);
            rosterRestoredUnregister = WSEventSystem.Register_Type<CharacterRosterRestoredEvent>(
                typeof(CharacterRosterRestoredEvent), HandleRosterRestored);
            skillRuntimeHost.Completed += HandleSkillCompleted;

            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 已绑定武器表现事件与装备权威状态。", this);
        }

        /// <summary>等待首次权威武器模型同步完成后再允许 CharacterManager 提交 Ready。</summary>
        /// <returns>首次装备模型加载、Marker 校验和技能节点绑定任务。</returns>
        /// <exception cref="InvalidOperationException">角色实例、装备定义或模型 Marker 不满足运行时契约时抛出。</exception>
        public async UniTask InitializeWeaponPresentationAsync()
        {
            if (!isRuntimeBound)
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 尚未绑定运行时武器表现。");
            if (isInitialSynchronizationInProgress)
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 正在进行首次武器同步。");

            isInitialSynchronizationInProgress = true;
            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 开始首次武器同步。", this);
            try
            {
                while (isRuntimeBound)
                {
                    RefreshCharacterInstanceFromRoster();
                    EquipmentInstanceId requestedWeaponInstanceId = characterInstance.EquippedWeaponInstanceId;
                    int requestVersion = ++weaponSyncRequestVersion;
                    await SynchronizeWeaponAsync(requestedWeaponInstanceId, requestVersion, true);

                    if (IsCurrentWeaponStateSynchronized())
                    {
                        Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 首次武器同步完成，weaponInstance={requestedWeaponInstanceId}。", this);
                        return;
                    }
                }

                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {name} 在首次武器同步完成前已解除运行时绑定。");
            }
            finally
            {
                isInitialSynchronizationInProgress = false;
            }
        }

        /// <summary>停止当前武器表现，注销事件、隔离异步请求并释放模型资源。</summary>
        public void StopRuntime()
        {
            if (!isRuntimeBound && currentWeaponModel == null && loadedWeaponAddress == null)
                return;

            weaponSyncRequestVersion++;
            isRuntimeBound = false;
            hasPendingWeaponSynchronization = false;
            characterInstanceChangedUnregister?.UnRegister();
            characterInstanceChangedUnregister = null;
            rosterRestoredUnregister?.UnRegister();
            rosterRestoredUnregister = null;
            if (skillRuntimeHost != null)
                skillRuntimeHost.Completed -= HandleSkillCompleted;

            ClearCurrentWeapon();
            characterInstance = null;
            characterRosterManager = null;
            weaponInventoryManager = null;
            Debug.Log($"[CharacterWeaponPresentationController] 角色 {name} 已解除武器表现绑定并释放模型引用。", this);
        }

        #endregion

        #region 武器同步与资源生命周期

        /// <summary>按一次已校验的角色装备实例 ID 加载并原子替换武器模型。</summary>
        /// <param name="weaponInstanceId">从权威 CharacterInstance 读取的武器实例 ID。</param>
        /// <param name="requestVersion">用于隔离后续换装或销毁的请求版本。</param>
        /// <param name="isInitialSync">首次同步失败时让角色队伍初始化回滚。</param>
        /// <returns>完成模型加载、Marker 索引和技能节点切换的异步任务。</returns>
        private async UniTask SynchronizeWeaponAsync(
            EquipmentInstanceId weaponInstanceId,
            int requestVersion,
            bool isInitialSync)
        {
            if (!isRuntimeBound || requestVersion != weaponSyncRequestVersion)
                return;

            if (!weaponInstanceId.IsValid)
            {
                if (skillRuntimeHost.IsPlaying)
                {
                    if (isInitialSync)
                        throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 首次加载期间技能正在执行，无法清除预览武器。");
                    hasPendingWeaponSynchronization = true;
                    Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 技能执行中，延迟清除未装备武器的表现。", this);
                    return;
                }

                if (!IsRequestStillAuthoritative(weaponInstanceId, requestVersion))
                    return;
                ClearCurrentWeapon();
                MarkWeaponStateSynchronized(weaponInstanceId);
                return;
            }

            WeaponDefinition weaponDefinition = ResolveWeaponDefinition(weaponInstanceId);
            string requestedAddress = weaponDefinition.WorldPrefabAddress;
            if (string.IsNullOrWhiteSpace(requestedAddress))
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 武器定义 {weaponDefinition.ItemId} 未配置 WorldPrefabAddress。");

            if (!IsRequestStillAuthoritative(weaponInstanceId, requestVersion))
                return;

            // 同一实例或同地址的外观不重复加载；已同步的 ID 仅是表现缓存，不参与装备决策。
            if (currentWeaponModel != null && string.Equals(currentWeaponAddress, requestedAddress, StringComparison.Ordinal))
            {
                MarkWeaponStateSynchronized(weaponInstanceId);
                return;
            }

            if (skillRuntimeHost.IsPlaying)
            {
                if (isInitialSync)
                    throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 首次加载期间技能正在执行，无法替换武器模型。");
                hasPendingWeaponSynchronization = true;
                Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 技能执行中，延迟装备武器 {weaponInstanceId}。", this);
                return;
            }

            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 开始加载武器模型，weaponInstance={weaponInstanceId}, address={requestedAddress}。", this);
            GameObject loadedWeaponPrefab = await ResSystem.Instance.LoadAsync<GameObject>(requestedAddress);
            bool transferredResourceReference = false;
            try
            {
                if (loadedWeaponPrefab == null)
                    throw new InvalidOperationException($"[CharacterWeaponPresentationController] 无法加载武器模型：address={requestedAddress}。");
                if (!IsRequestStillAuthoritative(weaponInstanceId, requestVersion))
                    return;

                if (skillRuntimeHost.IsPlaying)
                {
                    if (isInitialSync)
                        throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 首次武器模型加载完成时技能已启动。");
                    hasPendingWeaponSynchronization = true;
                    Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 加载武器期间技能启动，等待技能结束后应用 weaponInstance={weaponInstanceId}。", this);
                    return;
                }

                ValidateWeaponPrefab(loadedWeaponPrefab);
                ReplaceCurrentWeapon(loadedWeaponPrefab, requestedAddress, weaponInstanceId);
                transferredResourceReference = true;
            }
            finally
            {
                // 请求过期、验证失败或替换失败时释放本次 LoadAsync 的引用；成功后由当前模型持有。
                if (!transferredResourceReference && loadedWeaponPrefab != null)
                {
                    ResSystem.Instance.UnLoad<GameObject>(requestedAddress);
                    Debug.Log($"[CharacterWeaponPresentationController] 释放未提交的武器模型引用，address={requestedAddress}。", this);
                }
            }
        }

        /// <summary>用已通过 Marker 校验的新模型替换旧模型，并提交技能挂点及 Addressables 所有权。</summary>
        /// <param name="weaponPrefab">Addressables 加载的武器 Prefab。</param>
        /// <param name="weaponAddress">本次加载引用的地址。</param>
        /// <param name="weaponInstanceId">本次同步的装备实例 ID。</param>
        private void ReplaceCurrentWeapon(GameObject weaponPrefab, string weaponAddress, EquipmentInstanceId weaponInstanceId)
        {
            GameObject replacement = Instantiate(weaponPrefab, weaponModelRoot, false);
            replacement.name = weaponPrefab.name;
            DisableEmbeddedConstraints(replacement);

            GameObject previousWeaponModel = currentWeaponModel;
            Transform previousParent = previousWeaponModel != null ? previousWeaponModel.transform.parent : null;
            int previousSiblingIndex = previousWeaponModel != null ? previousWeaponModel.transform.GetSiblingIndex() : -1;
            string previousLoadedAddress = loadedWeaponAddress;
            if (previousWeaponModel != null)
            {
                // MarkerProvider 会扫描 inactive 子节点；先脱离旧模型再重建索引，避免新旧 Marker 冲突。
                previousWeaponModel.transform.SetParent(null, true);
            }

            if (!markerProvider.TryRebuild() ||
                !markerProvider.TryGetMarker(weaponRootMarkerKey, out Transform weaponRoot) ||
                !markerProvider.TryGetMarker(weaponTipMarkerKey, out Transform weaponTip))
            {
                replacement.transform.SetParent(null, true);
                Destroy(replacement);
                RestorePreviousWeapon(previousWeaponModel, previousParent, previousSiblingIndex);
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 无法为武器 {weaponPrefab.name} 建立唯一 WeaponRoot/WeaponTip Marker；保留旧模型。");
            }

            // Marker 完整后才更新技能宿主和运行时状态，避免部分替换留下失效节点。
            skillRuntimeHost.SetWeaponNodes(weaponRoot, weaponTip);
            currentWeaponModel = replacement;
            currentWeaponAddress = weaponAddress;
            loadedWeaponAddress = weaponAddress;
            MarkWeaponStateSynchronized(weaponInstanceId);
            PresentationModelChanged?.Invoke();

            if (previousWeaponModel != null)
            {
                Destroy(previousWeaponModel);
                Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 已销毁旧武器模型 {previousWeaponModel.name}。", this);
            }
            if (!string.IsNullOrWhiteSpace(previousLoadedAddress))
            {
                ResSystem.Instance.UnLoad<GameObject>(previousLoadedAddress);
                Debug.Log($"[CharacterWeaponPresentationController] 释放旧武器模型引用，address={previousLoadedAddress}。", this);
            }

            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 已提交武器模型，weaponInstance={weaponInstanceId}, address={weaponAddress}。", this);
        }

        /// <summary>将旧模型恢复到原父节点并重建 Marker 索引。</summary>
        /// <param name="previousWeaponModel">失败替换前的武器模型。</param>
        /// <param name="previousParent">旧模型原父节点。</param>
        /// <param name="previousSiblingIndex">旧模型原层级序号。</param>
        private void RestorePreviousWeapon(GameObject previousWeaponModel, Transform previousParent, int previousSiblingIndex)
        {
            if (previousWeaponModel == null)
                return;

            previousWeaponModel.transform.SetParent(previousParent, true);
            if (previousSiblingIndex >= 0)
                previousWeaponModel.transform.SetSiblingIndex(previousSiblingIndex);
            if (!markerProvider.TryRebuild())
                Debug.LogError($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 恢复旧武器后 MarkerProvider 仍无法重建。", markerProvider);
        }

        /// <summary>清除当前模型、技能武器节点与当前模型持有的资源引用。</summary>
        private void ClearCurrentWeapon()
        {
            GameObject previousWeaponModel = currentWeaponModel;
            string previousLoadedAddress = loadedWeaponAddress;
            if (previousWeaponModel != null)
                previousWeaponModel.transform.SetParent(null, true);

            if (markerProvider != null && !markerProvider.TryRebuild())
                Debug.LogWarning($"[CharacterWeaponPresentationController] 角色 {name} 清除武器后 MarkerProvider 缺少可选武器 Marker；技能武器节点仍会清空。", markerProvider);
            if (skillRuntimeHost != null)
                skillRuntimeHost.SetWeaponNodes(null, null);

            currentWeaponModel = null;
            currentWeaponAddress = null;
            loadedWeaponAddress = null;
            hasSynchronizedWeaponState = false;
            if (previousWeaponModel != null)
            {
                Destroy(previousWeaponModel);
                PresentationModelChanged?.Invoke();
                Debug.Log($"[CharacterWeaponPresentationController] 角色 {name} 已清除旧武器模型 {previousWeaponModel.name}。", this);
            }
            if (!string.IsNullOrWhiteSpace(previousLoadedAddress))
            {
                ResSystem.Instance.UnLoad<GameObject>(previousLoadedAddress);
                Debug.Log($"[CharacterWeaponPresentationController] 释放当前武器模型引用，address={previousLoadedAddress}。", this);
            }
        }

        #endregion

        #region 事件处理与权威状态

        /// <summary>绑定运行时后从角色实例重新读取武器并处理装备变化通知。</summary>
        /// <param name="changeEvent">角色实例提交后的变化通知。</param>
        private void HandleCharacterInstanceChanged(CharacterInstanceChangedEvent changeEvent)
        {
            if (!isRuntimeBound || changeEvent.ChangeType != CharacterInstanceChangeType.EquipmentUpdated ||
                changeEvent.Instance.CharacterId != characterInstance.CharacterId)
                return;

            HandleAuthoritativeCharacterChange("装备变化事件");
        }

        /// <summary>存档恢复后从 roster 重新读取装备实例，不将事件载荷作为第二份权威数据。</summary>
        /// <param name="restoredEvent">存档恢复事件，仅用于通知重新读取状态。</param>
        private void HandleRosterRestored(CharacterRosterRestoredEvent restoredEvent)
        {
            if (!isRuntimeBound)
                return;

            HandleAuthoritativeCharacterChange("角色存档恢复事件");
        }

        /// <summary>技能完全结束后提交等待中的最新装备模型。</summary>
        /// <param name="completedEvent">技能完成摘要。</param>
        private void HandleSkillCompleted(SkillCompletedEventArgs completedEvent)
        {
            if (!isRuntimeBound || !hasPendingWeaponSynchronization)
                return;

            hasPendingWeaponSynchronization = false;
            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 技能结束，开始应用最新装备武器。", this);
            RequestLatestWeaponSynchronization();
        }

        /// <summary>重新读取角色权威实例；首次同步期间只作废旧请求，由初始化循环继续同步。</summary>
        /// <param name="reason">触发重新读取的业务原因。</param>
        private void HandleAuthoritativeCharacterChange(string reason)
        {
            if (!RefreshCharacterInstanceFromRoster())
            {
                StopRuntime();
                return;
            }

            Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 收到{reason}，从实例重新读取 weaponInstance={characterInstance.EquippedWeaponInstanceId}。", this);
            if (isInitialSynchronizationInProgress)
            {
                weaponSyncRequestVersion++;
                return;
            }

            RequestLatestWeaponSynchronization();
        }

        /// <summary>从 CharacterRosterManager 获取同一角色最新稳定实例。</summary>
        /// <returns>角色仍在 roster 中时返回 true。</returns>
        private bool RefreshCharacterInstanceFromRoster()
        {
            if (characterInstance == null ||
                !characterRosterManager.TryGetInstance(characterInstance.CharacterId, out CharacterInstance latestInstance))
            {
                Debug.LogError($"[CharacterWeaponPresentationController] 角色 {name} 的 CharacterInstance 已不在 Roster 中，取消武器表现绑定。", this);
                return false;
            }

            characterInstance = latestInstance;
            return true;
        }

        /// <summary>按当前权威装备 ID 发出可被后续事件作废的异步模型同步请求。</summary>
        private void RequestLatestWeaponSynchronization()
        {
            if (!isRuntimeBound || !RefreshCharacterInstanceFromRoster())
                return;

            EquipmentInstanceId weaponInstanceId = characterInstance.EquippedWeaponInstanceId;
            int requestVersion = ++weaponSyncRequestVersion;
            if (skillRuntimeHost.IsPlaying)
            {
                hasPendingWeaponSynchronization = true;
                Debug.Log($"[CharacterWeaponPresentationController] 角色 {characterInstance.CharacterId} 当前技能运行中，暂存最新武器实例 {weaponInstanceId}。", this);
                return;
            }

            SynchronizeWeaponAsync(weaponInstanceId, requestVersion, false).Forget(HandleWeaponSyncException);
        }

        /// <summary>记录当前权威装备 ID 已成功映射至正在展示的模型状态。</summary>
        /// <param name="weaponInstanceId">当前 CharacterInstance 的装备实例 ID。</param>
        private void MarkWeaponStateSynchronized(EquipmentInstanceId weaponInstanceId)
        {
            lastSynchronizedWeaponInstanceId = weaponInstanceId;
            hasSynchronizedWeaponState = true;
        }

        /// <summary>判断角色当前权威装备是否已由模型与技能挂点完整展示。</summary>
        /// <returns>装备 ID、资源地址和模型状态一致时返回 true。</returns>
        private bool IsCurrentWeaponStateSynchronized()
        {
            if (!RefreshCharacterInstanceFromRoster() || !hasSynchronizedWeaponState)
                return false;

            EquipmentInstanceId authoritativeWeaponInstanceId = characterInstance.EquippedWeaponInstanceId;
            if (lastSynchronizedWeaponInstanceId != authoritativeWeaponInstanceId)
                return false;
            if (!authoritativeWeaponInstanceId.IsValid)
                return currentWeaponModel == null;

            WeaponDefinition weaponDefinition = ResolveWeaponDefinition(authoritativeWeaponInstanceId);
            return currentWeaponModel != null &&
                   string.Equals(currentWeaponAddress, weaponDefinition.WorldPrefabAddress, StringComparison.Ordinal);
        }

        /// <summary>检查请求版本和角色实例仍对应当前权威装备状态。</summary>
        /// <param name="weaponInstanceId">请求开始时的武器实例 ID。</param>
        /// <param name="requestVersion">请求版本号。</param>
        /// <returns>请求仍最新且装备关系未变化时返回 true。</returns>
        private bool IsRequestStillAuthoritative(EquipmentInstanceId weaponInstanceId, int requestVersion)
        {
            if (!isRuntimeBound || requestVersion != weaponSyncRequestVersion || !RefreshCharacterInstanceFromRoster())
                return false;
            return characterInstance.EquippedWeaponInstanceId == weaponInstanceId;
        }

        /// <summary>记录异步换装期间发生的不可恢复错误。</summary>
        /// <param name="exception">加载或 Marker 校验错误。</param>
        private void HandleWeaponSyncException(Exception exception)
        {
            string characterLabel = characterInstance == null ? name : characterInstance.CharacterId.ToString();
            Debug.LogError($"[CharacterWeaponPresentationController] 角色 {characterLabel} 的运行时武器同步失败，保留当前模型。\n{exception}", this);
        }

        #endregion

        #region 定义与 Prefab 校验

        /// <summary>通过库存实例 ID 解析武器 Definition 和其 Addressables 资源地址。</summary>
        /// <param name="weaponInstanceId">CharacterInstance 当前持有的武器实例 ID。</param>
        /// <returns>对应的武器定义。</returns>
        /// <exception cref="InvalidOperationException">实例、Definition 或资源地址无效时抛出。</exception>
        private WeaponDefinition ResolveWeaponDefinition(EquipmentInstanceId weaponInstanceId)
        {
            if (!weaponInventoryManager.TryGetInstance(weaponInstanceId, out WeaponInstance weaponInstance))
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 武器库存缺少已装备实例 {weaponInstanceId}。");
            if (!ItemManager.Instance.TryGetDefinition(weaponInstance.DefinitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weaponDefinition))
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 找不到武器定义 {weaponInstance.DefinitionId}。");
            return weaponDefinition;
        }

        /// <summary>确认武器资源各自唯一包含配置的 WeaponRoot 与 WeaponTip Marker。</summary>
        /// <param name="weaponPrefab">待实例化的武器模型 Prefab。</param>
        /// <exception cref="InvalidOperationException">Prefab 中缺少或重复必需 Marker 时抛出。</exception>
        private void ValidateWeaponPrefab(GameObject weaponPrefab)
        {
            int rootCount = 0;
            int tipCount = 0;
            TransformMarker[] markers = weaponPrefab.GetComponentsInChildren<TransformMarker>(true);
            for (int index = 0; index < markers.Length; index++)
            {
                if (markers[index].Key == weaponRootMarkerKey) rootCount++;
                if (markers[index].Key == weaponTipMarkerKey) tipCount++;
            }

            if (rootCount != 1 || tipCount != 1)
                throw new InvalidOperationException($"[CharacterWeaponPresentationController] 武器模型 {weaponPrefab.name} 的 Marker 数量无效：WeaponRoot={rootCount}，WeaponTip={tipCount}。");
        }

        /// <summary>停用武器资源内自带的约束，保持角色骨骼 Rig 为唯一姿态控制者。</summary>
        /// <param name="weaponModel">已实例化的新武器模型。</param>
        private static void DisableEmbeddedConstraints(GameObject weaponModel)
        {
            MultiParentConstraint[] embeddedConstraints = weaponModel.GetComponentsInChildren<MultiParentConstraint>(true);
            for (int index = 0; index < embeddedConstraints.Length; index++)
                embeddedConstraints[index].enabled = false;
        }

        #endregion
    }
}
