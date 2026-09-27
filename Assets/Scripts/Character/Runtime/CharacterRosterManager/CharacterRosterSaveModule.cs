using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;

namespace RPG.Character
{
    /// <summary>
    /// 角色实例进度的版本化存档快照。
    /// </summary>
    [Serializable]
    public sealed class CharacterRosterSaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空角色实例快照。</summary>
        public CharacterRosterSaveSnapshot()
        {
            Characters = new List<CharacterInstanceSaveEntry>();
        }

        /// <summary>玩家已经拥有的角色实例数据。</summary>
        public List<CharacterInstanceSaveEntry> Characters { get; set; }

        /// <summary>下一个角色获得顺序。</summary>
        public long NextAcquisitionSequence { get; set; } = 1;

        /// <summary>验证快照的集合结构，不读取运行时配置。</summary>
        /// <exception cref="InvalidOperationException">快照结构为空、重复或包含非法获得顺序时抛出。</exception>
        public void ValidateShape()
        {
            if (Characters == null || NextAcquisitionSequence <= 0)
                throw new InvalidOperationException("角色实例快照结构无效。");

            var characterIdSet = new HashSet<string>(StringComparer.Ordinal);
            var acquisitionSequenceSet = new HashSet<long>();
            long maxAcquisitionSequence = 0;
            for (int index = 0; index < Characters.Count; index++)
            {
                CharacterInstanceSaveEntry entry = Characters[index];
                if (entry == null || string.IsNullOrWhiteSpace(entry.CharacterId) ||
                    entry.Resources == null || !characterIdSet.Add(entry.CharacterId) || entry.AcquisitionSequence <= 0 ||
                    !acquisitionSequenceSet.Add(entry.AcquisitionSequence))
                    throw new InvalidOperationException("角色实例快照包含空、非法或重复角色数据。");

                if (entry.AcquisitionSequence > maxAcquisitionSequence)
                    maxAcquisitionSequence = entry.AcquisitionSequence;
            }

            if (NextAcquisitionSequence <= maxAcquisitionSequence)
                throw new InvalidOperationException("角色实例快照的下一个获得顺序必须大于现有实例顺序。");
        }
    }

    /// <summary>角色实例进度的单条存档数据。</summary>
    [Serializable]
    public sealed class CharacterInstanceSaveEntry
    {
        /// <summary>角色稳定标识。</summary>
        public string CharacterId { get; set; } = string.Empty;

        /// <summary>当前等级。</summary>
        public int Level { get; set; }

        /// <summary>当前等级内经验。</summary>
        public int CurrentExperience { get; set; }

        /// <summary>当前突破阶数。</summary>
        public int AscensionRank { get; set; }

        /// <summary>首次获得顺序。</summary>
        public long AcquisitionSequence { get; set; }

        /// <summary>角色 Resource CurrentValue 快照。</summary>
        public List<CharacterResourceSaveEntry> Resources { get; set; } = new();
    }

    /// <summary>角色存档中的单条 Resource CurrentValue 数据。</summary>
    [Serializable]
    public sealed class CharacterResourceSaveEntry
    {
        /// <summary>稳定 AttributeId。</summary>
        public int AttributeId { get; set; }

        /// <summary>Resource 当前值。</summary>
        public float CurrentValue { get; set; }
    }

    /// <summary>将角色实例状态接入 SaveSystem。</summary>
    public sealed class CharacterRosterSaveModule : SaveModule<CharacterRosterSaveSnapshot>
    {
        #region 模块标识

        /// <summary>角色实例存档模块的稳定标识。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("character-roster");

        #endregion

        #region 依赖字段

        private readonly CharacterRosterManager manager;

        #endregion

        #region 生命周期

        /// <summary>创建角色实例 v2 存档模块。</summary>
        /// <param name="manager">角色实例 Manager。</param>
        /// <exception cref="ArgumentNullException">角色实例 Manager 为空时抛出。</exception>
        public CharacterRosterSaveModule(CharacterRosterManager manager)
            : base(
                StableModuleId,
                2,
                SaveMissingModulePolicy.CreateDefault)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        #endregion

        #region 存档操作

        /// <summary>验证角色标识、配置引用和进度约束。</summary>
        /// <param name="snapshot">待验证快照。</param>
        protected override void ValidateTypedSnapshot(CharacterRosterSaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();

            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterInstanceSaveEntry entry = snapshot.Characters[index];
                CharacterId characterId = new CharacterId(entry.CharacterId);
                if (!characterId.IsValid ||
                    !CharacterConfigManager.Instance.TryGetConfig(characterId, out _))
                    throw new InvalidOperationException($"角色实例快照引用了不存在的角色：{entry.CharacterId}。");

                CharacterConfig config = CharacterConfigManager.Instance.GetRequiredConfig(characterId);
                CharacterInstance instance = new CharacterInstance(
                    config,
                    entry.Level,
                    entry.CurrentExperience,
                    entry.AscensionRank,
                    entry.AcquisitionSequence);
                ValidateResourceEntries(config, entry.Resources);
                CharacterProgressOperationStatus status = manager.ValidateRestoredInstance(instance);
                if (status != CharacterProgressOperationStatus.Succeeded)
                    throw new InvalidOperationException($"角色实例快照包含非法进度：character={characterId}, status={status}。");
            }
        }

        /// <summary>为缺少角色实例模块的新存档创建空状态。</summary>
        /// <returns>空角色实例快照。</returns>
        protected override CharacterRosterSaveSnapshot CreateDefaultTypedSnapshot() =>
            new CharacterRosterSaveSnapshot();

        /// <summary>按获得顺序采集角色成长、资源及下一个获得序号。</summary>
        /// <returns>角色实例的当前存档快照。</returns>
        protected override CharacterRosterSaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CharacterRosterSaveSnapshot
            {
                NextAcquisitionSequence = manager.NextAcquisitionSequence
            };
            IReadOnlyList<CharacterInstance> currentInstances = manager.GetInstances();
            // DTO 只记录稳定角色键和资源值，避免保存任何运行时对象引用。
            for (int index = 0; index < currentInstances.Count; index++)
            {
                CharacterInstance instance = currentInstances[index];
                snapshot.Characters.Add(new CharacterInstanceSaveEntry
                {
                    CharacterId = instance.CharacterId.ToString(),
                    Level = instance.Level,
                    CurrentExperience = instance.CurrentExperience,
                    AscensionRank = instance.AscensionRank,
                    AcquisitionSequence = instance.AcquisitionSequence,
                    Resources = BuildResourceEntries(instance)
                });
            }

            return snapshot;
        }

        /// <summary>恢复已校验的角色实例，不触发普通角色获得或默认装备事件。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(CharacterRosterSaveSnapshot snapshot)
        {
            var restoredInstances = new List<CharacterInstance>(snapshot.Characters.Count);
            // 先用当前配置重建领域实例和资源对象，再整体提交角色状态。
            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterInstanceSaveEntry entry = snapshot.Characters[index];
                CharacterId characterId = new CharacterId(entry.CharacterId);
                CharacterConfig config = CharacterConfigManager.Instance.GetRequiredConfig(characterId);
                var instance = new CharacterInstance(config, entry.Level, entry.CurrentExperience,
                    entry.AscensionRank, entry.AcquisitionSequence);
                var resources = new List<CharacterResourceValue>(entry.Resources.Count);
                for (int resourceIndex = 0; resourceIndex < entry.Resources.Count; resourceIndex++)
                {
                    CharacterResourceSaveEntry resource = entry.Resources[resourceIndex];
                    GameplayAttribute attribute = FindAttribute(config, resource.AttributeId);
                    resources.Add(new CharacterResourceValue(attribute, resource.CurrentValue));
                }

                instance.RestoreResourceCurrentValues(resources);
                restoredInstances.Add(instance);
            }

            manager.RestoreState(restoredInstances, snapshot.NextAcquisitionSequence);
            manager.PublishRestored();
        }

        /// <summary>校验 Resource 存档只包含配置声明且数值处于固定边界。</summary>
        /// <param name="config">角色配置。</param><param name="resources">待校验资源条目。</param>
        private static void ValidateResourceEntries(CharacterConfig config, IReadOnlyList<CharacterResourceSaveEntry> resources)
        {
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            for (int setIndex = 0; setIndex < config.InitialAttributeSets.Count; setIndex++)
                for (int definitionIndex = 0; definitionIndex < config.InitialAttributeSets[setIndex].Definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = config.InitialAttributeSets[setIndex].Definitions[definitionIndex];
                    definitionByAttributeIdMap.Add(definition.Attribute.Id, definition);
                }

            var resourceAttributeIdSet = new HashSet<int>();
            for (int ruleIndex = 0; ruleIndex < config.ResourceRules.Count; ruleIndex++)
                resourceAttributeIdSet.Add(config.ResourceRules[ruleIndex].ResourceAttribute.Id);
            var savedAttributeIdSet = new HashSet<int>();
            for (int index = 0; index < resources.Count; index++)
            {
                CharacterResourceSaveEntry entry = resources[index];
                if (entry == null || !savedAttributeIdSet.Add(entry.AttributeId) ||
                    !resourceAttributeIdSet.Contains(entry.AttributeId) ||
                    !definitionByAttributeIdMap.TryGetValue(entry.AttributeId, out GameplayAttributeDefinition definition) ||
                    float.IsNaN(entry.CurrentValue) || float.IsInfinity(entry.CurrentValue) ||
                    entry.CurrentValue < definition.MinValue || entry.CurrentValue > definition.MaxValue)
                    throw new InvalidOperationException($"角色 {config.CharacterId} 的 Resource 存档无效：attributeId={entry?.AttributeId}。");
            }
        }

        /// <summary>把实例内 Resource 当前值转换为存档条目。</summary>
        /// <param name="instance">待采集角色实例。</param>
        /// <returns>按实例提供顺序排列的 Resource 存档条目。</returns>
        private static List<CharacterResourceSaveEntry> BuildResourceEntries(CharacterInstance instance)
        {
            IReadOnlyList<CharacterResourceValue> values = instance.GetResourceCurrentValues();
            var entries = new List<CharacterResourceSaveEntry>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                entries.Add(new CharacterResourceSaveEntry
                {
                    AttributeId = values[index].Attribute.Id,
                    CurrentValue = values[index].CurrentValue
                });
            }

            return entries;
        }

        /// <summary>按稳定 AttributeId 找到角色初始配置中的 Attribute 对象。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="attributeId">Attribute 标识。</param>
        /// <returns>对应配置 Attribute。</returns>
        /// <exception cref="InvalidOperationException">配置中不存在该 Attribute 标识时抛出。</exception>
        private static GameplayAttribute FindAttribute(CharacterConfig config, int attributeId)
        {
            for (int setIndex = 0; setIndex < config.InitialAttributeSets.Count; setIndex++)
            {
                for (int definitionIndex = 0;
                     definitionIndex < config.InitialAttributeSets[setIndex].Definitions.Count;
                     definitionIndex++)
                {
                    GameplayAttribute attribute =
                        config.InitialAttributeSets[setIndex].Definitions[definitionIndex].Attribute;
                    if (attribute.Id == attributeId)
                    {
                        return attribute;
                    }
                }
            }

            throw new InvalidOperationException(
                $"角色 {config.CharacterId} 未配置 AttributeId {attributeId}。");
        }

        #endregion
    }
}
