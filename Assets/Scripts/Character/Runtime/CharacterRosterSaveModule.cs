using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>角色拥有状态的版本化存档快照。</summary>
    [Serializable]
    public sealed class CharacterRosterSaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空角色拥有快照。</summary>
        public CharacterRosterSaveSnapshot()
        {
            OwnedCharacterIds = new List<string>();
        }

        /// <summary>玩家已经拥有的角色稳定标识列表。</summary>
        public List<string> OwnedCharacterIds { get; set; }

        /// <summary>验证角色拥有快照的结构约束。</summary>
        /// <exception cref="InvalidOperationException">快照包含空、非法或重复角色标识时抛出。</exception>
        public void ValidateShape()
        {
            if (OwnedCharacterIds == null)
                throw new InvalidOperationException("角色拥有快照的角色列表不能为 null。 ");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < OwnedCharacterIds.Count; index++)
            {
                string characterId = OwnedCharacterIds[index];
                if (string.IsNullOrWhiteSpace(characterId) || !ids.Add(characterId))
                    throw new InvalidOperationException("角色拥有快照包含空、非法或重复角色标识。 ");
            }
        }
    }

    /// <summary>将角色拥有 Manager 状态接入 SaveSystem。</summary>
    public sealed class CharacterRosterSaveModule : SaveModule<CharacterRosterSaveSnapshot>
    {
        #region 依赖字段

        private readonly CharacterRosterManager manager;

        #endregion

        #region 生命周期与存档操作

        /// <summary>创建角色拥有存档模块。</summary>
        /// <param name="manager">角色拥有状态 Manager。</param>
        public CharacterRosterSaveModule(CharacterRosterManager manager)
            : base(
                new SaveModuleId("character-roster"),
                1,
                SaveMissingModulePolicy.CreateDefault)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>采集当前角色拥有状态。</summary>
        /// <returns>角色拥有快照。</returns>
        protected override CharacterRosterSaveSnapshot CaptureTypedSnapshot()
        {
            var snapshot = new CharacterRosterSaveSnapshot();
            IReadOnlyList<CharacterId> characterIds = manager.GetOwnedCharacterIds();
            for (int index = 0; index < characterIds.Count; index++)
                snapshot.OwnedCharacterIds.Add(characterIds[index].ToString());
            return snapshot;
        }

        /// <summary>验证角色拥有快照引用的角色配置存在。</summary>
        /// <param name="snapshot">待验证快照。</param>
        protected override void ValidateTypedSnapshot(CharacterRosterSaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
            for (int index = 0; index < snapshot.OwnedCharacterIds.Count; index++)
            {
                CharacterId characterId = new CharacterId(snapshot.OwnedCharacterIds[index]);
                if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
                    throw new InvalidOperationException($"角色拥有快照引用了不存在的角色：{characterId}。 ");
                config.Validate();
            }
        }

        /// <summary>恢复已经验证的角色拥有状态。</summary>
        /// <param name="snapshot">已验证快照。</param>
        protected override void RestoreTypedSnapshot(CharacterRosterSaveSnapshot snapshot)
        {
            var characterIds = new List<CharacterId>(snapshot.OwnedCharacterIds.Count);
            for (int index = 0; index < snapshot.OwnedCharacterIds.Count; index++)
                characterIds.Add(new CharacterId(snapshot.OwnedCharacterIds[index]));

            manager.RestoreState(characterIds);
            manager.PublishRestored();
        }

        /// <summary>为缺少角色拥有模块的旧存档创建空拥有列表。</summary>
        /// <returns>空角色拥有快照。</returns>
        protected override CharacterRosterSaveSnapshot CreateDefaultTypedSnapshot() =>
            new CharacterRosterSaveSnapshot();

        #endregion
    }
}
