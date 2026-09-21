using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>
    /// 持有玩家角色实例，并为角色装备系统提供可信的拥有事实。
    /// </summary>
    /// <remarks>
    /// 角色实例存在即表示角色已拥有，避免把拥有状态和成长状态拆成两套可能不一致的 Manager。
    /// 角色实例是持久化业务状态；场景中的 CharacterActor 仍由 CharacterManager 管理，
    /// Actor 通过 CharacterRuntimeAttributeBinding 消费同一个实例并同步等级与资源。
    /// </remarks>
    public sealed class CharacterRosterManager : AbstractManager
    {
        #region 状态字段

        // key：角色稳定标识；value：玩家对该角色的唯一持久化实例。
        private readonly Dictionary<CharacterId, CharacterInstance> characterByIdMap =
            new Dictionary<CharacterId, CharacterInstance>();
        // key：装备实例 ID；value：角色装备槽位置。该索引由角色实例状态反向派生并随提交原子更新。
        private readonly Dictionary<EquipmentInstanceId, CharacterEquipmentLocation> equipmentLocationByInstanceIdMap =
            new Dictionary<EquipmentInstanceId, CharacterEquipmentLocation>();
        private long nextAcquisitionSequence = 1;

        #endregion

        #region 依赖字段

        private readonly SaveManager saveManager;

        #endregion

        #region 构造

        /// <summary>创建由 GameArchitecture 持有的角色实例 Manager。</summary>
        /// <param name="saveManager">用于注册角色实例存档模块的 Manager。</param>
        public CharacterRosterManager(SaveManager saveManager)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
        }

        #endregion

        #region 生命周期

        /// <summary>初始化角色实例状态并注册当前版本存档模块。</summary>
        protected override void OnInit()
        {
            saveManager.RegisterModule(new CharacterRosterSaveModule(this));
            Debug.Log("[CharacterRosterManager] 角色实例状态已初始化。");
        }

        /// <summary>注销时清空当前存档对应的角色实例状态。</summary>
        protected override void OnDeinit()
        {
            int previousCount = characterByIdMap.Count;
            characterByIdMap.Clear();
            equipmentLocationByInstanceIdMap.Clear();
            nextAcquisitionSequence = 1;
            Debug.Log($"[CharacterRosterManager] 角色实例状态已清理，previousCount={previousCount}。");
        }

        #endregion

        #region 查询

        /// <summary>判断玩家是否已经拥有指定角色。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <returns>已拥有时返回 true。</returns>
        public bool IsOwned(CharacterId characterId) => characterByIdMap.ContainsKey(characterId);

        /// <summary>获取角色拥有状态的 CharacterId 稳定排序副本。</summary>
        /// <returns>按 CharacterId 文本排序的拥有角色列表。</returns>
        public IReadOnlyList<CharacterId> GetOwnedCharacterIds()
        {
            var result = new List<CharacterId>(characterByIdMap.Keys);
            result.Sort((left, right) => string.Compare(
                left.ToString(), right.ToString(), StringComparison.Ordinal));
            return result;
        }

        /// <summary>尝试读取指定角色实例。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <param name="instance">找到的角色实例。</param>
        /// <returns>实例存在时返回 true。</returns>
        public bool TryGetInstance(CharacterId characterId, out CharacterInstance instance) =>
            characterByIdMap.TryGetValue(characterId, out instance);

        /// <summary>读取指定角色的必需实例。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <returns>对应角色实例。</returns>
        /// <exception cref="InvalidOperationException">角色尚未拥有时抛出。</exception>
        public CharacterInstance GetRequiredInstance(CharacterId characterId)
        {
            if (characterByIdMap.TryGetValue(characterId, out CharacterInstance instance))
                return instance;
            throw new InvalidOperationException($"[CharacterRosterManager] 角色尚未拥有：{characterId}。");
        }

        /// <summary>获取按获得顺序稳定排列的角色实例副本。</summary>
        /// <returns>不暴露内部字典的角色实例列表。</returns>
        public IReadOnlyList<CharacterInstance> GetInstances()
        {
            var result = new List<CharacterInstance>(characterByIdMap.Values);
            result.Sort(CompareInstances);
            return result;
        }

        /// <summary>获取当前保存计数器的下一个角色获得顺序。</summary>
        internal long NextAcquisitionSequence => nextAcquisitionSequence;

        /// <summary>判断装备实例是否已经被任意角色装备。</summary>
        /// <param name="instanceId">装备实例标识。</param>
        /// <returns>已被角色引用时返回 true。</returns>
        public bool IsEquipmentEquipped(EquipmentInstanceId instanceId) =>
            instanceId.IsValid && equipmentLocationByInstanceIdMap.ContainsKey(instanceId);

        /// <summary>尝试读取装备实例所属角色。</summary>
        /// <param name="instanceId">装备实例标识。</param>
        /// <param name="characterId">装备者角色标识。</param>
        /// <returns>装备实例被引用时返回 true。</returns>
        public bool TryGetEquipmentOwner(EquipmentInstanceId instanceId, out CharacterId characterId)
        {
            if (equipmentLocationByInstanceIdMap.TryGetValue(instanceId, out CharacterEquipmentLocation location))
            {
                characterId = location.CharacterId;
                return true;
            }

            characterId = default(CharacterId);
            return false;
        }

        /// <summary>尝试读取装备实例的完整角色槽位置。</summary>
        /// <param name="instanceId">装备实例标识。</param>
        /// <param name="location">装备位置。</param>
        /// <returns>装备实例被引用时返回 true。</returns>
        public bool TryGetEquipmentLocation(EquipmentInstanceId instanceId, out CharacterEquipmentLocation location) =>
            equipmentLocationByInstanceIdMap.TryGetValue(instanceId, out location);

        /// <summary>尝试读取角色当前装备的武器实例。</summary>
        /// <param name="characterId">角色标识。</param>
        /// <param name="instanceId">武器实例标识。</param>
        /// <returns>角色有武器时返回 true。</returns>
        public bool TryGetEquippedWeaponInstanceId(CharacterId characterId, out EquipmentInstanceId instanceId)
        {
            if (characterByIdMap.TryGetValue(characterId, out CharacterInstance instance) &&
                instance.EquippedWeaponInstanceId.IsValid)
            {
                instanceId = instance.EquippedWeaponInstanceId;
                return true;
            }

            instanceId = default(EquipmentInstanceId);
            return false;
        }

        /// <summary>尝试读取角色指定圣遗物槽的实例。</summary>
        /// <param name="characterId">角色标识。</param>
        /// <param name="slot">圣遗物部位。</param>
        /// <param name="instanceId">圣遗物实例标识。</param>
        /// <returns>槽位有圣遗物时返回 true。</returns>
        public bool TryGetEquippedArtifactInstanceId(CharacterId characterId, ArtifactSlot slot, out EquipmentInstanceId instanceId)
        {
            if (characterByIdMap.TryGetValue(characterId, out CharacterInstance instance) &&
                instance.TryGetEquippedArtifactInstanceId(slot, out instanceId))
                return true;

            instanceId = default(EquipmentInstanceId);
            return false;
        }

        /// <summary>获取当前被角色装备的武器数量。</summary>
        /// <returns>已装备武器数量。</returns>
        public int GetEquippedWeaponCount()
        {
            int count = 0;
            foreach (CharacterEquipmentLocation location in equipmentLocationByInstanceIdMap.Values)
                if (location.SlotKind == CharacterEquipmentSlotKind.Weapon) count++;
            return count;
        }

        #endregion

        #region 角色获得与进度

        /// <summary>获得一个角色并创建一级实例。</summary>
        /// <param name="characterId">待获得的角色稳定标识。</param>
        /// <returns>角色获得结果；重复获得时返回已有实例。</returns>
        public CharacterAcquisitionResult AcquireCharacter(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return Fail(CharacterAcquisitionStatus.InvalidCharacterId, characterId);

            if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out _))
                return Fail(CharacterAcquisitionStatus.CharacterNotFound, characterId);

            if (characterByIdMap.TryGetValue(characterId, out CharacterInstance existingInstance))
            {
                Debug.Log($"[CharacterRosterManager] 角色已经拥有，跳过重复获得事件，character={characterId}, " +
                          $"level={existingInstance.Level}, ownedCount={characterByIdMap.Count}。");
                return new CharacterAcquisitionResult(
                    CharacterAcquisitionStatus.AlreadyOwned,
                    characterId,
                    existingInstance);
            }

            // 先提交角色实例，再发布两个事件；订阅方收到事件时可以立即查询完整实例和拥有状态。
            CharacterConfig config = CharacterConfigManager.Instance.GetRequiredConfig(characterId);
            CharacterInstance instance = new CharacterInstance(
                config,
                1,
                0,
                0,
                nextAcquisitionSequence);
            CharacterProgressOperationStatus initialStatus = ValidateProgress(
                characterId,
                new CharacterProgressUpdate(instance.Level, instance.CurrentExperience, instance.AscensionRank));
            if (initialStatus != CharacterProgressOperationStatus.Succeeded)
                throw new InvalidOperationException($"[CharacterRosterManager] 角色初始进度无效：character={characterId}, status={initialStatus}。");

            nextAcquisitionSequence++;
            characterByIdMap.Add(characterId, instance);
            Debug.Log($"[CharacterRosterManager] 写入角色实例，character={characterId}, " +
                      $"level={instance.Level}, acquisitionSequence={instance.AcquisitionSequence}。");
            PublishInstanceChanged(CharacterInstanceChangeType.Added, instance);
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterOwnershipChangedEvent),
                new CharacterOwnershipChangedEvent(characterId, true));
            Debug.Log($"[CharacterRosterManager] 完成角色获得并发布拥有事件，character={characterId}, " +
                      $"ownedCount={characterByIdMap.Count}。");
            return new CharacterAcquisitionResult(
                CharacterAcquisitionStatus.Succeeded,
                characterId,
                instance);
        }

        /// <summary>更新已拥有角色的等级、经验和突破状态。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <param name="update">目标进度。</param>
        /// <returns>进度更新结果。</returns>
        public CharacterProgressOperationResult UpdateCharacterProgress(
            CharacterId characterId,
            CharacterProgressUpdate update)
        {
            if (!characterByIdMap.TryGetValue(characterId, out CharacterInstance currentInstance))
                return new CharacterProgressOperationResult(
                    CharacterProgressOperationStatus.CharacterNotFound,
                    null);

            CharacterProgressOperationStatus validationStatus = ValidateProgress(characterId, update);
            if (validationStatus != CharacterProgressOperationStatus.Succeeded)
            {
                Debug.LogWarning($"[CharacterRosterManager] 角色进度更新校验失败，character={characterId}, " +
                                 $"level={update.Level}, experience={update.CurrentExperience}, " +
                                 $"ascensionRank={update.AscensionRank}, status={validationStatus}。");
                return new CharacterProgressOperationResult(validationStatus, null);
            }

            if (currentInstance.Level == update.Level &&
                currentInstance.CurrentExperience == update.CurrentExperience &&
                currentInstance.AscensionRank == update.AscensionRank)
            {
                return new CharacterProgressOperationResult(
                    CharacterProgressOperationStatus.Succeeded,
                    currentInstance);
            }

            // 校验已经完成后原地提交，Actor、Binding 和 UI 持有的引用继续指向同一个领域实例。
            currentInstance.CommitProgress(update);
            PublishInstanceChanged(CharacterInstanceChangeType.ProgressUpdated, currentInstance);
            Debug.Log($"[CharacterRosterManager] 角色进度已更新，character={characterId}, " +
                      $"level={currentInstance.Level}, experience={currentInstance.CurrentExperience}, " +
                      $"ascensionRank={currentInstance.AscensionRank}。");
            return new CharacterProgressOperationResult(
                CharacterProgressOperationStatus.Succeeded,
                currentInstance);
        }

        #endregion

        #region 存档恢复

        /// <summary>提交一个角色新的装备状态并维护反向索引。</summary>
        /// <param name="characterId">目标角色。</param>
        /// <param name="equipment">已经由协调系统校验类型的装备状态。</param>
        /// <exception cref="InvalidOperationException">角色不存在或装备实例被其他角色占用时抛出。</exception>
        internal void CommitEquipmentState(CharacterId characterId, CharacterEquipmentState equipment)
        {
            if (!characterByIdMap.TryGetValue(characterId, out CharacterInstance currentInstance))
                throw new InvalidOperationException($"[CharacterRosterManager] 不能为未拥有角色提交装备：{characterId}。");
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));

            var nextLocationByInstanceIdMap = new Dictionary<EquipmentInstanceId, CharacterEquipmentLocation>(equipmentLocationByInstanceIdMap);
            RemoveLocationsForCharacter(nextLocationByInstanceIdMap, characterId);
            AddEquipmentLocation(nextLocationByInstanceIdMap, equipment.EquippedWeaponInstanceId,
                new CharacterEquipmentLocation(characterId, CharacterEquipmentSlotKind.Weapon));
            // 圣遗物槽位按枚举顺序提交，避免出现不同角色装备同一实例的冲突。
            foreach (ArtifactSlot slot in Enum.GetValues(typeof(ArtifactSlot)))
            {
                if (!equipment.TryGetArtifactInstanceId(slot, out EquipmentInstanceId instanceId)) continue;
                AddEquipmentLocation(nextLocationByInstanceIdMap, instanceId,
                    new CharacterEquipmentLocation(characterId, CharacterEquipmentSlotKind.Artifact, slot));
            }

            if (AreEquipmentStatesEqual(currentInstance.Equipment, equipment)) return;
            // 反向索引和实例状态在同一校验结果下提交，避免出现半更新状态。
            currentInstance.CommitEquipment(equipment);
            equipmentLocationByInstanceIdMap.Clear();
            foreach (KeyValuePair<EquipmentInstanceId, CharacterEquipmentLocation> pair in nextLocationByInstanceIdMap)
                equipmentLocationByInstanceIdMap.Add(pair.Key, pair.Value);
            PublishInstanceChanged(CharacterInstanceChangeType.EquipmentUpdated, currentInstance);
            Debug.Log($"[CharacterRosterManager] 角色装备状态已提交，character={characterId}, weapon={equipment.EquippedWeaponInstanceId}, " +
                      $"equipmentCount={equipmentLocationByInstanceIdMap.Count}。");
        }

        /// <summary>整体恢复所有角色装备状态，不逐条发布普通装备事件。</summary>
        /// <param name="equipmentByCharacterIdMap">按角色建立的已验证装备状态。</param>
        internal void RestoreEquipmentState(IReadOnlyDictionary<CharacterId, CharacterEquipmentState> equipmentByCharacterIdMap)
        {
            if (equipmentByCharacterIdMap == null) throw new ArgumentNullException(nameof(equipmentByCharacterIdMap));
            var restoredLocations = new Dictionary<EquipmentInstanceId, CharacterEquipmentLocation>();
            foreach (KeyValuePair<CharacterId, CharacterEquipmentState> pair in equipmentByCharacterIdMap)
            {
                if (!characterByIdMap.ContainsKey(pair.Key))
                    throw new InvalidOperationException($"装备存档引用了未拥有角色：{pair.Key}。");
                AddEquipmentLocation(restoredLocations, pair.Value.EquippedWeaponInstanceId,
                    new CharacterEquipmentLocation(pair.Key, CharacterEquipmentSlotKind.Weapon));
                foreach (ArtifactSlot slot in Enum.GetValues(typeof(ArtifactSlot)))
                {
                    if (!pair.Value.TryGetArtifactInstanceId(slot, out EquipmentInstanceId instanceId)) continue;
                    AddEquipmentLocation(restoredLocations, instanceId,
                        new CharacterEquipmentLocation(pair.Key, CharacterEquipmentSlotKind.Artifact, slot));
                }
            }

            foreach (CharacterId characterId in characterByIdMap.Keys)
            {
                CharacterEquipmentState equipment = equipmentByCharacterIdMap.TryGetValue(characterId, out CharacterEquipmentState state)
                    ? state
                    : new CharacterEquipmentState();
                CharacterInstance currentInstance = characterByIdMap[characterId];
                currentInstance.CommitEquipment(equipment);
            }

            equipmentLocationByInstanceIdMap.Clear();
            foreach (KeyValuePair<EquipmentInstanceId, CharacterEquipmentLocation> pair in restoredLocations)
                equipmentLocationByInstanceIdMap.Add(pair.Key, pair.Value);
            Debug.Log($"[CharacterRosterManager] 整体恢复角色装备关系，characterCount={characterByIdMap.Count}, equipmentCount={equipmentLocationByInstanceIdMap.Count}。");
        }

        /// <summary>用已经完成快照校验的实例列表替换运行时状态。</summary>
        /// <param name="restoredInstances">已验证角色实例列表。</param>
        /// <param name="restoredNextAcquisitionSequence">已验证的下一个获得顺序。</param>
        internal void RestoreState(
            IReadOnlyList<CharacterInstance> restoredInstances,
            long restoredNextAcquisitionSequence)
        {
            if (restoredInstances == null) throw new ArgumentNullException(nameof(restoredInstances));
            if (restoredNextAcquisitionSequence <= 0)
                throw new ArgumentOutOfRangeException(nameof(restoredNextAcquisitionSequence));

            var restoredCharacterByIdMap = new Dictionary<CharacterId, CharacterInstance>();
            for (int index = 0; index < restoredInstances.Count; index++)
            {
                CharacterInstance instance = restoredInstances[index] ??
                    throw new InvalidOperationException("角色实例恢复列表不能包含空项。");
                if (!restoredCharacterByIdMap.TryAdd(instance.CharacterId, instance))
                    throw new InvalidOperationException($"角色实例恢复列表包含重复角色：{instance.CharacterId}。");
            }

            // 先完成候选集合校验，再复用同 ID 的旧实例，避免场景 Actor 持有孤立引用。
            var existingCharacterIdsToRemove = new List<CharacterId>();
            foreach (CharacterId characterId in characterByIdMap.Keys)
                if (!restoredCharacterByIdMap.ContainsKey(characterId)) existingCharacterIdsToRemove.Add(characterId);
            for (int index = 0; index < existingCharacterIdsToRemove.Count; index++)
                characterByIdMap.Remove(existingCharacterIdsToRemove[index]);
            foreach (KeyValuePair<CharacterId, CharacterInstance> pair in restoredCharacterByIdMap)
            {
                if (characterByIdMap.TryGetValue(pair.Key, out CharacterInstance existingInstance))
                {
                    existingInstance.CommitProgress(new CharacterProgressUpdate(
                        pair.Value.Level, pair.Value.CurrentExperience, pair.Value.AscensionRank));
                    existingInstance.RestoreResourceCurrentValues(pair.Value.GetResourceCurrentValues());
                    existingInstance.CommitEquipment(pair.Value.Equipment);
                }
                else
                {
                    characterByIdMap.Add(pair.Key, pair.Value);
                }
            }
            equipmentLocationByInstanceIdMap.Clear();
            foreach (CharacterInstance instance in characterByIdMap.Values)
            {
                AddEquipmentLocation(equipmentLocationByInstanceIdMap, instance.EquippedWeaponInstanceId,
                    new CharacterEquipmentLocation(instance.CharacterId, CharacterEquipmentSlotKind.Weapon));
                foreach (ArtifactSlot slot in Enum.GetValues(typeof(ArtifactSlot)))
                    if (instance.TryGetEquippedArtifactInstanceId(slot, out EquipmentInstanceId artifactInstanceId))
                        AddEquipmentLocation(equipmentLocationByInstanceIdMap, artifactInstanceId,
                            new CharacterEquipmentLocation(instance.CharacterId, CharacterEquipmentSlotKind.Artifact, slot));
            }
            nextAcquisitionSequence = restoredNextAcquisitionSequence;
            Debug.Log($"[CharacterRosterManager] 恢复角色实例状态，count={characterByIdMap.Count}, " +
                      $"nextAcquisitionSequence={nextAcquisitionSequence}。");
        }

        /// <summary>发布角色拥有状态已经恢复完成事件。</summary>
        internal void PublishRestored()
        {
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterRosterRestoredEvent),
                new CharacterRosterRestoredEvent());
        }

        /// <summary>校验恢复实例的进度和获得顺序，但不修改运行时状态。</summary>
        /// <param name="instance">待校验角色实例。</param>
        /// <returns>校验状态。</returns>
        internal CharacterProgressOperationStatus ValidateRestoredInstance(CharacterInstance instance)
        {
            if (instance == null || instance.AcquisitionSequence <= 0)
                return CharacterProgressOperationStatus.AcquisitionSequenceInvalid;
            return ValidateProgress(
                instance.CharacterId,
                new CharacterProgressUpdate(instance.Level, instance.CurrentExperience, instance.AscensionRank));
        }

        #endregion

        #region 内部校验与事件

        /// <summary>根据角色配置校验一组进度字段。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <param name="update">待校验进度。</param>
        /// <returns>校验状态。</returns>
        private CharacterProgressOperationStatus ValidateProgress(
            CharacterId characterId,
            CharacterProgressUpdate update)
        {
            if (!characterId.IsValid || !CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
                return CharacterProgressOperationStatus.CharacterNotFound;
            config.Validate();
            if (update.Level < 1 || update.Level > config.MaxLevel)
                return CharacterProgressOperationStatus.LevelOutOfRange;
            if (update.CurrentExperience < 0)
                return CharacterProgressOperationStatus.ExperienceOutOfRange;
            if (update.AscensionRank < 0 || update.AscensionRank > config.MaxAscensionRank)
                return CharacterProgressOperationStatus.AscensionRankOutOfRange;

            int levelCap = GetLevelCap(config, update.AscensionRank);
            if (update.Level > levelCap)
                return CharacterProgressOperationStatus.LevelExceedsAscensionCap;
            if (update.Level >= levelCap || update.Level >= config.MaxLevel)
                return update.CurrentExperience == 0
                    ? CharacterProgressOperationStatus.Succeeded
                    : CharacterProgressOperationStatus.ExperienceExceedsCurrentLevel;

            BakedCharacterLevelProgression progression = FindLevelProgression(config, update.Level);
            if (progression == null || progression.NextExperience <= 0 ||
                update.CurrentExperience >= progression.NextExperience)
                return update.CurrentExperience == 0 && progression != null && progression.NextExperience > 0
                    ? CharacterProgressOperationStatus.Succeeded
                    : CharacterProgressOperationStatus.ExperienceExceedsCurrentLevel;

            return CharacterProgressOperationStatus.Succeeded;
        }

        /// <summary>获取指定突破阶数允许的等级上限。</summary>
        /// <param name="config">角色静态配置。</param>
        /// <param name="ascensionRank">突破阶数。</param>
        /// <returns>当前突破阶数的等级上限。</returns>
        private static int GetLevelCap(CharacterConfig config, int ascensionRank)
        {
            IReadOnlyList<CharacterAscensionStage> stages = config.AscensionStages;
            if (ascensionRank == 0)
                return stages.Count == 0 ? config.MaxLevel : stages[0].RequiredLevel;
            if (ascensionRank <= stages.Count)
                return stages[ascensionRank - 1].MaxLevelAfter;
            return config.MaxLevel;
        }

        /// <summary>按等级查找角色成长烘焙项。</summary>
        /// <param name="config">角色静态配置。</param>
        /// <param name="level">目标等级。</param>
        /// <returns>找到的成长项；不存在时返回 null。</returns>
        private static BakedCharacterLevelProgression FindLevelProgression(CharacterConfig config, int level)
        {
            IReadOnlyList<BakedCharacterLevelProgression> progressions =
                config.GrowthProfile.BakedLevelProgressions;
            for (int index = 0; index < progressions.Count; index++)
                if (progressions[index] != null && progressions[index].Level == level)
                    return progressions[index];
            return null;
        }

        /// <summary>发布角色实例变化事件。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">变化后的实例。</param>
        private static void PublishInstanceChanged(
            CharacterInstanceChangeType changeType,
            CharacterInstance instance)
        {
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterInstanceChangedEvent),
                new CharacterInstanceChangedEvent(changeType, instance));
        }

        /// <summary>从候选反向索引中移除一个角色的旧装备。</summary>
        private static void RemoveLocationsForCharacter(
            Dictionary<EquipmentInstanceId, CharacterEquipmentLocation> locationByInstanceIdMap,
            CharacterId characterId)
        {
            var removeInstanceIds = new List<EquipmentInstanceId>();
            foreach (KeyValuePair<EquipmentInstanceId, CharacterEquipmentLocation> pair in locationByInstanceIdMap)
                if (pair.Value.CharacterId == characterId) removeInstanceIds.Add(pair.Key);
            for (int index = 0; index < removeInstanceIds.Count; index++)
                locationByInstanceIdMap.Remove(removeInstanceIds[index]);
        }

        /// <summary>向候选反向索引写入装备位置并拒绝跨角色重复引用。</summary>
        private static void AddEquipmentLocation(
            Dictionary<EquipmentInstanceId, CharacterEquipmentLocation> locationByInstanceIdMap,
            EquipmentInstanceId instanceId,
            CharacterEquipmentLocation location)
        {
            if (!instanceId.IsValid) return;
            if (locationByInstanceIdMap.ContainsKey(instanceId))
                throw new InvalidOperationException($"装备实例 {instanceId} 已经被其他角色或槽位引用。");
            locationByInstanceIdMap.Add(instanceId, location);
        }

        /// <summary>比较两个不可变装备状态是否完全一致。</summary>
        private static bool AreEquipmentStatesEqual(CharacterEquipmentState left, CharacterEquipmentState right)
        {
            return left.EquippedWeaponInstanceId == right.EquippedWeaponInstanceId &&
                   left.FlowerOfLifeInstanceId == right.FlowerOfLifeInstanceId &&
                   left.PlumeOfDeathInstanceId == right.PlumeOfDeathInstanceId &&
                   left.SandsOfEonInstanceId == right.SandsOfEonInstanceId &&
                   left.GobletOfEonothemInstanceId == right.GobletOfEonothemInstanceId &&
                   left.CircletOfLogosInstanceId == right.CircletOfLogosInstanceId;
        }

        /// <summary>按获得顺序和角色标识稳定比较实例。</summary>
        /// <param name="left">左侧实例。</param>
        /// <param name="right">右侧实例。</param>
        /// <returns>稳定排序结果。</returns>
        private static int CompareInstances(CharacterInstance left, CharacterInstance right)
        {
            int sequenceComparison = left.AcquisitionSequence.CompareTo(right.AcquisitionSequence);
            return sequenceComparison != 0
                ? sequenceComparison
                : string.Compare(left.CharacterId.ToString(), right.CharacterId.ToString(), StringComparison.Ordinal);
        }

        /// <summary>记录角色获得失败并构造不携带实例数据的结果。</summary>
        /// <param name="status">角色获得状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <returns>角色获得失败结果。</returns>
        private static CharacterAcquisitionResult Fail(
            CharacterAcquisitionStatus status,
            CharacterId characterId)
        {
            Debug.LogWarning($"[CharacterRosterManager] 角色获得失败，character={characterId}, status={status}。");
            return new CharacterAcquisitionResult(status, characterId, null);
        }

        #endregion
    }

    /// <summary>角色拥有入口的结果状态。</summary>
    public enum CharacterAcquisitionStatus
    {
        /// <summary>本次成功新增角色实例。</summary>
        Succeeded = 0,
        /// <summary>角色已经拥有，不会重复发布事件。</summary>
        AlreadyOwned,
        /// <summary>角色标识无效。</summary>
        InvalidCharacterId,
        /// <summary>角色配置不存在。</summary>
        CharacterNotFound
    }

    /// <summary>角色获得操作的不可变结果。</summary>
    public readonly struct CharacterAcquisitionResult
    {
        /// <summary>创建角色获得结果。</summary>
        /// <param name="status">角色获得状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <param name="instance">已有或新创建的角色实例。</param>
        public CharacterAcquisitionResult(
            CharacterAcquisitionStatus status,
            CharacterId characterId,
            CharacterInstance instance)
        {
            Status = status;
            CharacterId = characterId;
            Instance = instance;
        }

        /// <summary>获取角色获得状态。</summary>
        public CharacterAcquisitionStatus Status { get; }

        /// <summary>获取相关角色标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取已有或新创建的角色实例。</summary>
        public CharacterInstance Instance { get; }

        /// <summary>判断角色已经拥有或本次成功新增。</summary>
        public bool Succeeded => Status == CharacterAcquisitionStatus.Succeeded ||
                                  Status == CharacterAcquisitionStatus.AlreadyOwned;
    }

    /// <summary>单个角色拥有状态变化事件，供默认武器装配使用。</summary>
    public readonly struct CharacterOwnershipChangedEvent
    {
        /// <summary>创建角色拥有状态变化事件。</summary>
        /// <param name="characterId">发生变化的角色。</param>
        /// <param name="isOwned">变化后的拥有状态。</param>
        public CharacterOwnershipChangedEvent(CharacterId characterId, bool isOwned)
        {
            CharacterId = characterId;
            IsOwned = isOwned;
        }

        /// <summary>获取发生变化的角色标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>获取变化后的拥有状态。</summary>
        public bool IsOwned { get; }
    }

    /// <summary>角色实例存档恢复完成事件。</summary>
    public readonly struct CharacterRosterRestoredEvent
    {
    }
}
