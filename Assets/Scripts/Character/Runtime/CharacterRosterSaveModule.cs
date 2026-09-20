using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;

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
                    !characterIdSet.Add(entry.CharacterId) || entry.AcquisitionSequence <= 0 ||
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
    }

    /// <summary>将角色实例状态接入 SaveSystem。</summary>
    public sealed class CharacterRosterSaveModule : SaveModule<CharacterRosterSaveSnapshot>
    {
        #region 依赖字段

        private readonly CharacterRosterManager manager;

        #endregion

        #region 生命周期

        /// <summary>角色实例存档模块的稳定标识。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("character-roster");

        /// <summary>创建角色实例 v2 存档模块。</summary>
        /// <param name="manager">角色实例 Manager。</param>
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

        /// <summary>采集按获得顺序排列的角色实例状态。</summary>
        /// <returns>角色实例快照。</returns>
        protected override CharacterRosterSaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CharacterRosterSaveSnapshot
            {
                NextAcquisitionSequence = manager.NextAcquisitionSequence
            };
            IReadOnlyList<CharacterInstance> instances = manager.GetInstances();
            for (int index = 0; index < instances.Count; index++)
            {
                CharacterInstance instance = instances[index];
                snapshot.Characters.Add(new CharacterInstanceSaveEntry
                {
                    CharacterId = instance.CharacterId.ToString(),
                    Level = instance.Level,
                    CurrentExperience = instance.CurrentExperience,
                    AscensionRank = instance.AscensionRank,
                    AcquisitionSequence = instance.AcquisitionSequence
                });
            }

            return snapshot;
        }

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

                CharacterInstance instance = new CharacterInstance(
                    characterId,
                    entry.Level,
                    entry.CurrentExperience,
                    entry.AscensionRank,
                    entry.AcquisitionSequence);
                CharacterProgressOperationStatus status = manager.ValidateRestoredInstance(instance);
                if (status != CharacterProgressOperationStatus.Succeeded)
                    throw new InvalidOperationException($"角色实例快照包含非法进度：character={characterId}, status={status}。");
            }
        }

        /// <summary>整体恢复角色实例，不触发角色获得或默认武器装配事件。</summary>
        /// <param name="snapshot">已经完成验证的快照。</param>
        protected override void RestoreTypedSnapshot(CharacterRosterSaveSnapshot snapshot)
        {
            var instances = new List<CharacterInstance>(snapshot.Characters.Count);
            for (int index = 0; index < snapshot.Characters.Count; index++)
            {
                CharacterInstanceSaveEntry entry = snapshot.Characters[index];
                instances.Add(new CharacterInstance(
                    new CharacterId(entry.CharacterId),
                    entry.Level,
                    entry.CurrentExperience,
                    entry.AscensionRank,
                    entry.AcquisitionSequence));
            }

            manager.RestoreState(instances, snapshot.NextAcquisitionSequence);
            manager.PublishRestored();
        }

        /// <summary>为缺少角色实例模块的新存档创建空状态。</summary>
        /// <returns>空角色实例快照。</returns>
        protected override CharacterRosterSaveSnapshot CreateDefaultTypedSnapshot() =>
            new CharacterRosterSaveSnapshot();

        #endregion
    }
}
