using System;
using System.Collections.Generic;
using RPG.SaveSystem;

namespace RPG.Character
{
    /// <summary>唯一队伍的版本化存档快照。</summary>
    [Serializable]
    public sealed class CharacterPartySaveSnapshot : ISaveModuleSnapshot
    {
        /// <summary>创建空队伍快照。</summary>
        public CharacterPartySaveSnapshot()
        {
            CharacterIds = new List<string>(CharacterParty.SlotCount);
            for (int index = 0; index < CharacterParty.SlotCount; index++) CharacterIds.Add(string.Empty);
        }

        /// <summary>创建指定槽位快照。</summary>
        /// <param name="characterIds">角色槽位。</param>
        public CharacterPartySaveSnapshot(IReadOnlyList<CharacterId> characterIds)
        {
            CharacterIds = new List<string>(CharacterParty.SlotCount);
            for (int index = 0; index < CharacterParty.SlotCount; index++)
                CharacterIds.Add(index < characterIds.Count ? characterIds[index].ToString() : string.Empty);
        }

        /// <summary>四个队伍槽位的稳定角色键。</summary>
        public List<string> CharacterIds { get; set; }

        /// <summary>校验快照结构。</summary>
        public void ValidateShape()
        {
            if (CharacterIds == null || CharacterIds.Count != CharacterParty.SlotCount)
                throw new InvalidOperationException("[CharacterPartySaveSnapshot] 队伍快照必须包含四个槽位。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < CharacterIds.Count; index++)
            {
                string value = CharacterIds[index] ?? string.Empty;
                if (value.Length > 0 && !ids.Add(value))
                    throw new InvalidOperationException("[CharacterPartySaveSnapshot] 队伍快照包含重复角色。");
            }
        }
    }

    /// <summary>将唯一队伍状态接入 SaveSystem。</summary>
    public sealed class CharacterPartySaveModule : SaveModule<CharacterPartySaveSnapshot>
    {
        #region 模块标识

        /// <summary>队伍存档模块的稳定 ID。</summary>
        public static readonly SaveModuleId StableModuleId = new SaveModuleId("character-party");

        #endregion

        #region 依赖字段

        // 依赖字段：队伍恢复前需要由名册恢复所引用的角色实例。
        private readonly CharacterPartyManager manager;

        #endregion

        /// <summary>创建队伍存档模块。</summary>
        /// <param name="managerValue">队伍管理器。</param>
        /// <exception cref="ArgumentNullException">队伍 Manager 为空时抛出。</exception>
        public CharacterPartySaveModule(CharacterPartyManager managerValue)
            : base(StableModuleId, 1, SaveMissingModulePolicy.CreateDefault,
                new[] { CharacterRosterSaveModule.StableModuleId })
        {
            manager = managerValue ?? throw new ArgumentNullException(nameof(managerValue));
        }

        /// <summary>通过队伍 Manager 采集固定四槽位快照。</summary>
        /// <returns>队伍当前存档快照。</returns>
        protected override CharacterPartySaveSnapshot CaptureTypedSnapshot() => manager.CaptureSnapshot();

        /// <summary>将已校验的队伍快照交给队伍 Manager 恢复。</summary>
        /// <param name="snapshot">已校验的当前版本快照。</param>
        protected override void RestoreTypedSnapshot(CharacterPartySaveSnapshot snapshot) =>
            manager.RestoreSnapshot(snapshot);

        /// <summary>校验队伍快照。</summary>
        /// <param name="snapshot">队伍快照。</param>
        protected override void ValidateTypedSnapshot(CharacterPartySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.ValidateShape();
        }

        /// <summary>创建空队伍默认快照。</summary>
        /// <returns>四个空槽位快照。</returns>
        protected override CharacterPartySaveSnapshot CreateDefaultTypedSnapshot() => new CharacterPartySaveSnapshot();
    }
}
