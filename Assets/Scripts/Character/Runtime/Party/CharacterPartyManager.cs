using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using RPG.SaveSystem;

namespace RPG.Character
{
    /// <summary>管理当前唯一队伍的运行时查询和后续编辑边界。</summary>
    public sealed class CharacterPartyManager : AbstractManager
    {
        #region 状态字段

        private CharacterParty party;

        #endregion

        #region 依赖字段

        private readonly SaveManager saveManager;
        private readonly CharacterRosterManager rosterManager;

        #endregion

        #region 事件

        /// <summary>队伍槽位发生变化时发送。</summary>
        public event Action Changed;

        #endregion

        #region 属性

        /// <summary>获取当前唯一队伍；尚未初始化时返回空。</summary>
        public CharacterParty Party => party;

        #endregion

        /// <summary>创建由 GameArchitecture 持有的唯一队伍管理器。</summary>
        public CharacterPartyManager(SaveManager saveManagerValue, CharacterRosterManager rosterManagerValue)
        {
            saveManager = saveManagerValue ?? throw new ArgumentNullException(nameof(saveManagerValue));
            rosterManager = rosterManagerValue ?? throw new ArgumentNullException(nameof(rosterManagerValue));
        }

        /// <summary>初始化当前唯一队伍；重复配置会立即失败。</summary>
        /// <param name="initialCharacterIds">按槽位顺序提供的初始角色。</param>
        public void ConfigureInitialParty(IReadOnlyList<CharacterId> initialCharacterIds)
        {
            if (party != null) throw new InvalidOperationException("[CharacterPartyManager] 唯一队伍已初始化，不能重复配置。");
            party = new CharacterParty(initialCharacterIds);
            Debug.Log($"[CharacterPartyManager] 唯一队伍初始化完成，slotCount={CharacterParty.SlotCount}。", null);
            Changed?.Invoke();
        }

        /// <summary>完成业务架构 Manager 初始化。</summary>
        protected override void OnInit()
        {
            saveManager.RegisterModule(new CharacterPartySaveModule(this));
            Debug.Log("[CharacterPartyManager] 唯一队伍 Manager 已初始化。", null);
        }

        /// <summary>按槽位读取角色标识。</summary>
        /// <param name="slotIndex">零基槽位下标。</param>
        /// <returns>槽位角色标识。</returns>
        public CharacterId GetCharacterIdAtSlot(int slotIndex) => party?.GetCharacterIdAtSlot(slotIndex) ?? default;

        /// <summary>查找角色所在槽位。</summary>
        /// <param name="characterId">待查询角色标识。</param>
        /// <returns>零基槽位；不在队伍时返回负数。</returns>
        public int FindSlot(CharacterId characterId) => party?.FindSlot(characterId) ?? -1;

        /// <summary>返回当前队伍槽位快照。</summary>
        /// <returns>唯一队伍的四个槽位。</returns>
        public IReadOnlyList<CharacterId> CreateSnapshot() => party?.CreateSnapshot() ?? Array.Empty<CharacterId>();

        /// <summary>采集队伍存档快照。</summary>
        /// <returns>固定四槽位角色键快照。</returns>
        public CharacterPartySaveSnapshot CaptureSnapshot() => new CharacterPartySaveSnapshot(CreateSnapshot());

        /// <summary>恢复已校验的队伍存档快照。</summary>
        /// <param name="snapshot">队伍快照。</param>
        public void RestoreSnapshot(CharacterPartySaveSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (party != null) throw new InvalidOperationException("[CharacterPartyManager] 队伍已初始化，不能恢复第二份快照。");
            for (int index = 0; index < snapshot.CharacterIds.Count; index++)
            {
                string characterIdText = snapshot.CharacterIds[index];
                if (!string.IsNullOrWhiteSpace(characterIdText) && !rosterManager.IsOwned(new CharacterId(characterIdText)))
                    throw new InvalidOperationException($"[CharacterPartyManager] 队伍角色尚未拥有：{characterIdText}。");
            }
            party = new CharacterParty(snapshot.CharacterIds.ConvertAll(value => new CharacterId(value)));
            Changed?.Invoke();
            Debug.Log($"[CharacterPartyManager] 已恢复队伍快照，slotCount={snapshot.CharacterIds.Count}。");
        }

        /// <summary>释放静态当前引用，避免场景销毁后 UI 读取旧队伍。</summary>
        protected override void OnDeinit()
        {
            party = null;
            Changed = null;
            Debug.Log("[CharacterPartyManager] 唯一队伍管理器已释放。", null);
        }
    }
}
