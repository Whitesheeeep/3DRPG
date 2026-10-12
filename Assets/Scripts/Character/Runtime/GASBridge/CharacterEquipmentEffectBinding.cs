using System;
using System.Collections.Generic;
using RPG.Game;
using RPG.ItemSystem;
using WS_Modules.CustomEventSystem;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>把一名角色当前武器与圣遗物的可撤销 GE 绑定到该角色自己的 ASC。</summary>
    internal sealed class CharacterEquipmentEffectBinding : IDisposable
    {
        #region 依赖字段

        private readonly CharacterId characterId;
        private readonly CharacterRosterManager rosterManager;
        private readonly CharacterEquipmentSystem equipmentSystem;
        private readonly WeaponInventoryManager weaponInventoryManager;
        private readonly ArtifactInventoryManager artifactInventoryManager;
        private readonly ItemManager itemManager;
        private readonly GameplayAbilitySystemComponent abilitySystem;
        private readonly List<AppliedEffectEntry> appliedEffectEntries = new();
        private readonly UnRegisterListContainer equipmentEventUnregisterList = new();
        private bool suspended;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建装备效果绑定并订阅装备、存档及 ASC Runtime 生命周期事件。</summary>
        /// <param name="characterInstance">Actor 初始化时绑定的稳定角色实例。</param>
        /// <param name="abilitySystem">装备属性实际写入的角色 ASC。</param>
        public CharacterEquipmentEffectBinding(
            CharacterInstance characterInstance,
            GameplayAbilitySystemComponent abilitySystem)
        {
            if (characterInstance == null || !characterInstance.CharacterId.IsValid)
                throw new ArgumentException("装备效果绑定必须使用有效 CharacterInstance。", nameof(characterInstance));
            characterId = characterInstance.CharacterId;
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));

            // Binding 自己取得装备业务依赖，避免 Actor 为单个子模块长期持有这些引用。
            this.rosterManager = GameArchitecture.Interface.GetManager<CharacterRosterManager>();
            this.equipmentSystem = GameArchitecture.Interface.GetSystem<CharacterEquipmentSystem>();
            this.weaponInventoryManager = GameArchitecture.Interface.GetManager<WeaponInventoryManager>();
            this.artifactInventoryManager = GameArchitecture.Interface.GetManager<ArtifactInventoryManager>();
            this.itemManager = ItemManager.Instance;
            DebugLog($"角色 {characterId} 已从 GameArchitecture 获取装备效果依赖。");

            try
            {
                abilitySystem.RuntimeCleared += HandleRuntimeCleared;
                WSEventSystem.Register_Type<CharacterInstanceChangedEvent>(
                    typeof(CharacterInstanceChangedEvent), HandleInstanceChanged)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<CharacterRosterRestoredEvent>(
                    typeof(CharacterRosterRestoredEvent), HandleRosterRestored)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<CharacterEquipmentRestoredEvent>(
                    typeof(CharacterEquipmentRestoredEvent), HandleEquipmentRestored)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<WeaponInstanceChangedEvent>(
                    typeof(WeaponInstanceChangedEvent), HandleWeaponChanged)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<WeaponInventoryRestoredEvent>(
                    typeof(WeaponInventoryRestoredEvent), HandleWeaponInventoryRestored)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<ArtifactInstanceChangedEvent>(
                    typeof(ArtifactInstanceChangedEvent), HandleArtifactChanged)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                WSEventSystem.Register_Type<ArtifactInventoryRestoredEvent>(
                    typeof(ArtifactInventoryRestoredEvent), HandleArtifactInventoryRestored)
                    .AddToUnregisterList(equipmentEventUnregisterList);
                DebugLog($"角色 {characterId} 装备变化订阅已建立，count={equipmentEventUnregisterList.UnRegisterList.Count}。");
                abilitySystem.GameEffectCtrl.EffectRemoved += HandleEffectRemoved;
                Refresh();
            }
            catch
            {
                // 构造过程任何一步失败都撤销此前建立的全局订阅，避免半成品绑定泄漏。
                Dispose();
                throw;
            }
        }

        /// <summary>解除装备与 ASC 事件订阅，并释放仍由本绑定持有的 GE Runtime。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            abilitySystem.RuntimeCleared -= HandleRuntimeCleared;
            abilitySystem.GameEffectCtrl.EffectRemoved -= HandleEffectRemoved;
            int registrationCount = equipmentEventUnregisterList.UnRegisterList.Count;
            equipmentEventUnregisterList.UnRegisterAll();
            DebugLog($"角色 {characterId} 装备变化订阅已注销，count={registrationCount}。");
            ClearOwnedEffects();
            DebugLog($"角色 {characterId} 装备 GE 绑定已释放。");
        }

        #endregion

        #region 刷新与复活

        /// <summary>按当前装备快照应用或差量替换本角色持有的装备效果。</summary>
        public void Refresh()
        {
            if (disposed || suspended || abilitySystem.IsClearingRuntime) return;
            CharacterInstance instance = rosterManager.GetRequiredInstance(characterId);
            var desiredEntries = new List<DesiredEffectEntry>();
            CollectWeaponEffects(desiredEntries);
            CollectArtifactEffects(instance, desiredEntries);

            // 先验证整份目标配置，确认合法后才触碰当前已生效的装备数据。
            for (int index = 0; index < desiredEntries.Count; index++)
            {
                DesiredEffectEntry desired = desiredEntries[index];
                ValidateEffect(desired);
                if (!abilitySystem.GameEffectCtrl.CanApply(desired.Data, abilitySystem))
                    throw new InvalidOperationException(
                        $"[CharacterEquipmentEffectBinding] GE {desired.Data.name} 当前无法应用到角色 {characterId} 的 ASC。");
            }

            var nextAppliedEntries = new List<AppliedEffectEntry>(desiredEntries.Count);
            var newlyAppliedEntries = new List<AppliedEffectEntry>();
            try
            {
                for (int index = 0; index < desiredEntries.Count; index++)
                {
                    DesiredEffectEntry desired = desiredEntries[index];
                    AppliedEffectEntry existing = FindMatchingEntry(desired);
                    if (existing != null)
                    {
                        nextAppliedEntries.Add(existing);
                        continue;
                    }

                    if (!abilitySystem.TryApplyEffect(desired.Data, abilitySystem, desired.Level, null,
                            out GameEffectRuntime runtime) || runtime == null)
                        throw new InvalidOperationException(
                            $"[CharacterEquipmentEffectBinding] 角色 {characterId} 的装备 GE 应用失败，effect={desired.Data.name}。");

                    var applied = new AppliedEffectEntry(desired, runtime);
                    newlyAppliedEntries.Add(applied);
                    nextAppliedEntries.Add(applied);
                }
            }
            catch
            {
                for (int index = newlyAppliedEntries.Count - 1; index >= 0; index--)
                    abilitySystem.TryRemoveEffect(newlyAppliedEntries[index].Runtime);
                throw;
            }

            var retainedRuntimeSet = new HashSet<GameEffectRuntime>();
            for (int index = 0; index < nextAppliedEntries.Count; index++)
                retainedRuntimeSet.Add(nextAppliedEntries[index].Runtime);
            // 先切换本地所有权快照，后续移除事件只会清理旧句柄，不会改写正在遍历的旧列表。
            List<AppliedEffectEntry> previousAppliedEntries = new(appliedEffectEntries);
            appliedEffectEntries.Clear();
            appliedEffectEntries.AddRange(nextAppliedEntries);
            for (int index = previousAppliedEntries.Count - 1; index >= 0; index--)
            {
                GameEffectRuntime oldRuntime = previousAppliedEntries[index].Runtime;
                if (!retainedRuntimeSet.Contains(oldRuntime)) abilitySystem.TryRemoveEffect(oldRuntime);
            }
            DebugLog($"角色 {characterId} 装备 GE 已同步，activeEffects={appliedEffectEntries.Count}。");
        }

        /// <summary>复活后解除暂停并按最新装备关系重新建立装备效果。</summary>
        public void ResumeAndRefresh()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CharacterEquipmentEffectBinding));
            suspended = false;
            try
            {
                Refresh();
            }
            catch
            {
                // 复活未能完整恢复装备加成时继续保持死亡期间暂停，避免后续库存事件半途重建 GE。
                suspended = true;
                throw;
            }
        }

        #endregion

        #region 装备解析与校验

        /// <summary>按当前装备武器的等级和精炼阶数加入两组效果。</summary>
        /// <param name="desiredEntries">待应用的目标效果列表。</param>
        private void CollectWeaponEffects(List<DesiredEffectEntry> desiredEntries)
        {
            if (!equipmentSystem.TryGetEquippedWeapon(characterId, out WeaponInstance weapon)) return;
            if (!weaponInventoryManager.TryGetInstance(weapon.InstanceId, out WeaponInstance inventoryWeapon) ||
                !ReferenceEquals(weapon, inventoryWeapon))
                throw new InvalidOperationException(
                    $"[CharacterEquipmentEffectBinding] 装备系统返回的武器未对应到注入库存实例，instance={weapon.InstanceId}。");
            if (!itemManager.TryGetDefinition(weapon.DefinitionId, out ItemDefinition item) ||
                item is not WeaponDefinition definition)
                throw new InvalidOperationException($"[CharacterEquipmentEffectBinding] 武器 {weapon.InstanceId} 缺少有效 Definition。");
            AddEffectList(weapon.InstanceId, E_EquipmentEffectGroup.WeaponLevel, definition.LevelEffects,
                weapon.Level, desiredEntries);
            AddEffectList(weapon.InstanceId, E_EquipmentEffectGroup.WeaponRefinement, definition.RefinementEffects,
                weapon.RefinementRank, desiredEntries);
        }

        /// <summary>按五个圣遗物槽位的等级加入效果。</summary>
        /// <param name="instance">当前角色实例。</param>
        /// <param name="desiredEntries">待应用的目标效果列表。</param>
        private void CollectArtifactEffects(CharacterInstance instance, List<DesiredEffectEntry> desiredEntries)
        {
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                ArtifactSlot slot = (ArtifactSlot)slotIndex;
                if (!equipmentSystem.TryGetEquippedArtifact(characterId, slot, out ArtifactInstance artifact)) continue;
                if (!artifactInventoryManager.TryGetInstance(artifact.InstanceId, out ArtifactInstance inventoryArtifact) ||
                    !ReferenceEquals(artifact, inventoryArtifact))
                    throw new InvalidOperationException(
                        $"[CharacterEquipmentEffectBinding] 装备系统返回的圣遗物未对应到注入库存实例，instance={artifact.InstanceId}。");
                if (!itemManager.TryGetDefinition(artifact.DefinitionId, out ItemDefinition item) ||
                    item is not ArtifactDefinition definition)
                    throw new InvalidOperationException($"[CharacterEquipmentEffectBinding] 圣遗物 {artifact.InstanceId} 缺少有效 Definition。");
                AddEffectList(artifact.InstanceId, E_EquipmentEffectGroup.ArtifactLevel, definition.LevelEffects,
                    artifact.Level, desiredEntries);
            }
        }

        /// <summary>为效果列表建立包含装备实例、列表类型及条目下标的运行时身份。</summary>
        /// <param name="instanceId">武器或圣遗物实例。</param>
        /// <param name="group">效果列表用途。</param>
        /// <param name="effects">静态 GE 列表。</param>
        /// <param name="level">本列表效果等级。</param>
        /// <param name="desiredEntries">目标效果列表。</param>
        private static void AddEffectList(
            EquipmentInstanceId instanceId,
            E_EquipmentEffectGroup group,
            IReadOnlyList<GameplayEffectData> effects,
            int level,
            List<DesiredEffectEntry> desiredEntries)
        {
            if (effects == null)
                throw new InvalidOperationException(
                    $"[CharacterEquipmentEffectBinding] 装备实例 {instanceId} 的 {group} 效果列表未初始化。");
            for (int index = 0; index < effects.Count; index++)
                desiredEntries.Add(new DesiredEffectEntry(
                    new EffectEntryKey(instanceId, group, index), effects[index], Math.Max(1, level)));
        }

        /// <summary>拒绝无法被装备句柄精确撤销的 GE 配置。</summary>
        /// <param name="entry">待检查的装备效果。</param>
        private static void ValidateEffect(DesiredEffectEntry entry)
        {
            GameplayEffectData data = entry.Data;
            if (data == null || data.DurationType != E_GameEffectDurationType.Infinite ||
                data.Period != 0f || data.Executions.Count != 0 ||
                data.StackingType != E_GameEffectStackingType.None)
                throw new InvalidOperationException(
                    $"[CharacterEquipmentEffectBinding] 装备 GE 必须是无限、非周期、无 Execution 且不合并 Runtime 的效果，key={entry.Key}。");
            for (int modifierIndex = 0; modifierIndex < data.Modifiers.Count; modifierIndex++)
                if (data.Modifiers[modifierIndex] is SetByCallerGameplayEffectModifier)
                    throw new InvalidOperationException(
                        $"[CharacterEquipmentEffectBinding] 装备 GE 不允许依赖 SetByCaller，effect={data.name}, key={entry.Key}。");
            if (!data.TryValidateApplicationConfiguration())
                throw new InvalidOperationException($"[CharacterEquipmentEffectBinding] 装备 GE 配置无效，effect={data.name}。");
        }

        /// <summary>查找身份、配置引用和等级均未变化的现有 Runtime。</summary>
        /// <param name="desired">待应用的条目。</param>
        /// <returns>可保留的条目；不存在时返回 null。</returns>
        private AppliedEffectEntry FindMatchingEntry(DesiredEffectEntry desired)
        {
            for (int index = 0; index < appliedEffectEntries.Count; index++)
            {
                AppliedEffectEntry existing = appliedEffectEntries[index];
                if (existing.Key.Equals(desired.Key) && ReferenceEquals(existing.Data, desired.Data) &&
                    existing.Level == desired.Level && existing.Runtime.IsActive)
                    return existing;
            }
            return null;
        }

        #endregion

        #region 事件处理与内部状态

        /// <summary>在装备或实例成长提交后按当前快照执行差量更新。</summary>
        /// <param name="changeEvent">角色实例变化。</param>
        private void HandleInstanceChanged(CharacterInstanceChangedEvent changeEvent)
        {
            if (changeEvent.Instance.CharacterId == characterId) Refresh();
        }

        /// <summary>存档恢复后重新读取装备实例、等级和精炼效果。</summary>
        /// <param name="restoredEvent">角色存档恢复事件。</param>
        private void HandleRosterRestored(CharacterRosterRestoredEvent restoredEvent) => Refresh();
        /// <summary>装备关系存档恢复后同步当前槽位。</summary>
        /// <param name="restoredEvent">装备关系恢复事件。</param>
        private void HandleEquipmentRestored(CharacterEquipmentRestoredEvent restoredEvent) => Refresh();
        /// <summary>武器实例变化仅在当前装备实例或本地失效条目涉及该实例时刷新。</summary>
        /// <param name="changedEvent">武器库存变化。</param>
        private void HandleWeaponChanged(WeaponInstanceChangedEvent changedEvent)
        {
            bool equippedInstanceChanged = equipmentSystem.TryGetEquippedWeapon(
                characterId, out WeaponInstance equippedWeapon) &&
                equippedWeapon.InstanceId == changedEvent.Instance.InstanceId;
            bool appliedInstanceChanged =
                HasAppliedEntry(changedEvent.Instance.InstanceId, E_EquipmentEffectGroup.WeaponLevel) ||
                HasAppliedEntry(changedEvent.Instance.InstanceId, E_EquipmentEffectGroup.WeaponRefinement);
            if (equippedInstanceChanged || appliedInstanceChanged) Refresh();
        }
        /// <summary>武器库存恢复后重新读取当前装备。</summary>
        /// <param name="restoredEvent">武器库存恢复事件。</param>
        private void HandleWeaponInventoryRestored(WeaponInventoryRestoredEvent restoredEvent) => Refresh();
        /// <summary>圣遗物实例变化仅在当前装备槽位或本地失效条目涉及该实例时刷新。</summary>
        /// <param name="changedEvent">圣遗物库存变化。</param>
        private void HandleArtifactChanged(ArtifactInstanceChangedEvent changedEvent)
        {
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                if (!equipmentSystem.TryGetEquippedArtifact(characterId, (ArtifactSlot)slotIndex,
                        out ArtifactInstance equippedArtifact) ||
                    equippedArtifact.InstanceId != changedEvent.Instance.InstanceId)
                    continue;

                Refresh();
                return;
            }

            if (HasAppliedEntry(changedEvent.Instance.InstanceId, E_EquipmentEffectGroup.ArtifactLevel))
                Refresh();
        }

        /// <summary>判断已应用条目是否属于发生变化的装备实例和效果列表类别。</summary>
        /// <param name="instanceId">变化事件中的装备实例标识。</param>
        /// <param name="group">待匹配的效果列表类别。</param>
        /// <returns>存在关联 Runtime 时返回 true。</returns>
        private bool HasAppliedEntry(EquipmentInstanceId instanceId, E_EquipmentEffectGroup group)
        {
            for (int index = 0; index < appliedEffectEntries.Count; index++)
                if (appliedEffectEntries[index].Key.Matches(instanceId, group)) return true;
            return false;
        }
        /// <summary>圣遗物库存恢复后重新读取当前装备。</summary>
        /// <param name="restoredEvent">圣遗物库存恢复事件。</param>
        private void HandleArtifactInventoryRestored(ArtifactInventoryRestoredEvent restoredEvent) => Refresh();

        /// <summary>当所属 ASC 的装备 Runtime 被其他系统移除时同步丢弃对应本地句柄。</summary>
        /// <param name="removedRuntime">已经从 ASC 移除的效果 Runtime。</param>
        private void HandleEffectRemoved(GameEffectRuntime removedRuntime)
        {
            for (int index = appliedEffectEntries.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(appliedEffectEntries[index].Runtime, removedRuntime)) continue;
                appliedEffectEntries.RemoveAt(index);
                DebugLog($"角色 {characterId} 的装备 GE Runtime 已失效，effect={removedRuntime.Data.name}。");
                return;
            }
        }

        /// <summary>ASC 完成全部运行时清理后丢弃已失效句柄并暂停自动应用。</summary>
        /// <param name="clearedAbilitySystem">刚完成清理的 ASC。</param>
        private void HandleRuntimeCleared(GameplayAbilitySystemComponent clearedAbilitySystem)
        {
            appliedEffectEntries.Clear();
            suspended = true;
            DebugLog($"角色 {characterId} 收到 ASC RuntimeCleared，装备效果等待显式复活刷新。");
        }

        /// <summary>移除本绑定仍有效的全部 Runtime 句柄。</summary>
        private void ClearOwnedEffects()
        {
            GameEffectRuntime[] ownedRuntimes = new GameEffectRuntime[appliedEffectEntries.Count];
            for (int index = 0; index < appliedEffectEntries.Count; index++)
                ownedRuntimes[index] = appliedEffectEntries[index].Runtime;
            appliedEffectEntries.Clear();
            for (int index = ownedRuntimes.Length - 1; index >= 0; index--)
                abilitySystem.TryRemoveEffect(ownedRuntimes[index]);
        }

        /// <summary>输出带稳定角色上下文的装备效果生命周期日志。</summary>
        /// <param name="message">操作和结果摘要。</param>
        private void DebugLog(string message) =>
            UnityEngine.Debug.Log($"[CharacterEquipmentEffectBinding] {message}");

        #endregion

        #region 嵌套类型

        /// <summary>区分同一装备实例中不同的效果列表来源。</summary>
        private enum E_EquipmentEffectGroup
        {
            /// <summary>武器等级效果。</summary>
            WeaponLevel,
            /// <summary>武器精炼效果。</summary>
            WeaponRefinement,
            /// <summary>圣遗物等级效果。</summary>
            ArtifactLevel
        }

        /// <summary>标识一个装备实例中的单独 GE 列表条目。</summary>
        private readonly struct EffectEntryKey : IEquatable<EffectEntryKey>
        {
            private readonly EquipmentInstanceId instanceId;
            private readonly E_EquipmentEffectGroup group;
            private readonly int index;

            /// <summary>创建稳定条目身份。</summary>
            /// <param name="instanceId">装备实例。</param>
            /// <param name="group">列表类别。</param>
            /// <param name="index">列表内位置。</param>
            public EffectEntryKey(EquipmentInstanceId instanceId, E_EquipmentEffectGroup group, int index)
            {
                this.instanceId = instanceId;
                this.group = group;
                this.index = index;
            }

            /// <summary>比较装备实例、列表类别与条目顺序。</summary>
            /// <param name="other">待比较键。</param>
            /// <returns>三个身份部分均相同时返回 true。</returns>
            public bool Equals(EffectEntryKey other) =>
                instanceId == other.instanceId && group == other.group && index == other.index;

            /// <summary>判断条目是否属于指定装备实例及效果列表类别。</summary>
            /// <param name="candidateInstanceId">变化事件中的装备实例。</param>
            /// <param name="candidateGroup">需要匹配的效果列表类别。</param>
            /// <returns>实例与类别均匹配时返回 true。</returns>
            public bool Matches(EquipmentInstanceId candidateInstanceId, E_EquipmentEffectGroup candidateGroup) =>
                instanceId == candidateInstanceId && group == candidateGroup;

            /// <summary>生成与实例、效果列表类别及条目位置一致的稳定散列码。</summary>
            /// <returns>供集合查询使用的散列值。</returns>
            public override int GetHashCode() => HashCode.Combine(instanceId, group, index);
        }

        /// <summary>尚待应用的装备 GE 请求。</summary>
        private sealed class DesiredEffectEntry
        {
            /// <summary>创建待应用条目。</summary>
            /// <param name="key">所属装备及列表索引。</param>
            /// <param name="data">GE 配置。</param>
            /// <param name="level">应用等级。</param>
            public DesiredEffectEntry(EffectEntryKey key, GameplayEffectData data, int level)
            {
                Key = key;
                Data = data;
                Level = level;
            }

            /// <summary>获取条目身份。</summary>
            public EffectEntryKey Key { get; }
            /// <summary>获取 GE 配置。</summary>
            public GameplayEffectData Data { get; }
            /// <summary>获取应用等级。</summary>
            public int Level { get; }
        }

        /// <summary>本绑定已经成功应用并持有的装备 GE Runtime。</summary>
        private sealed class AppliedEffectEntry
        {
            /// <summary>创建已应用条目。</summary>
            /// <param name="desired">对应目标配置。</param>
            /// <param name="runtime">ASC 返回的可撤销 Runtime。</param>
            public AppliedEffectEntry(DesiredEffectEntry desired, GameEffectRuntime runtime)
            {
                Key = desired.Key;
                Data = desired.Data;
                Level = desired.Level;
                Runtime = runtime;
            }

            /// <summary>获取条目身份。</summary>
            public EffectEntryKey Key { get; }
            /// <summary>获取配置资源。</summary>
            public GameplayEffectData Data { get; }
            /// <summary>获取应用等级。</summary>
            public int Level { get; }
            /// <summary>获取可撤销 Runtime。</summary>
            public GameEffectRuntime Runtime { get; }
        }

        #endregion
    }
}
