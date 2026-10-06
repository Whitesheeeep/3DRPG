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

        /// <summary>按队伍规则构造槽位编辑候选，不修改当前队伍。</summary>
        /// <param name="characterId">要编辑位置的已拥有角色。</param>
        /// <param name="slotIndex">目标零基槽位；负一表示退出队伍。</param>
        /// <param name="candidateParty">校验成功后的不可变候选队伍。</param>
        /// <returns>候选状态；只有 Success 表示存在待提交候选。</returns>
        internal E_CharacterPartyEditStatus TryCreateSlotEdit(
            CharacterId characterId, int slotIndex, out CharacterParty candidateParty)
        {
            candidateParty = null;
            if (party == null) return E_CharacterPartyEditStatus.NotReady;
            if (slotIndex < -1 || slotIndex >= CharacterParty.SlotCount)
                return E_CharacterPartyEditStatus.InvalidSlot;
            if (!characterId.IsValid || !rosterManager.IsOwned(characterId))
                return E_CharacterPartyEditStatus.CharacterNotOwned;

            int previousSlot = party.FindSlot(characterId);
            if (previousSlot == slotIndex) return E_CharacterPartyEditStatus.NoChange;

            CharacterId[] candidateIds = new CharacterId[CharacterParty.SlotCount];
            IReadOnlyList<CharacterId> currentIds = party.CreateSnapshot();
            int memberCount = 0;
            for (int index = 0; index < CharacterParty.SlotCount; index++)
            {
                candidateIds[index] = currentIds[index];
                if (currentIds[index].IsValid) memberCount++;
            }

            if (slotIndex < 0)
            {
                if (previousSlot < 0) return E_CharacterPartyEditStatus.NoChange;
                if (memberCount <= 1) return E_CharacterPartyEditStatus.LastMember;
                candidateIds[previousSlot] = default;
            }
            else
            {
                CharacterId displacedCharacterId = candidateIds[slotIndex];
                candidateIds[slotIndex] = characterId;
                // 队内角色与目标槽位互换；队外角色进入占用位时，被替换者离队。
                if (previousSlot >= 0) candidateIds[previousSlot] = displacedCharacterId;
            }

            candidateParty = new CharacterParty(candidateIds);
            return E_CharacterPartyEditStatus.Success;
        }

        /// <summary>替换当前队伍快照；由运行时协调器在同一同步提交阶段调用。</summary>
        /// <param name="candidateParty">由本 Manager 创建并经运行时同步的候选队伍。</param>
        internal void CommitSlotEdit(CharacterParty candidateParty)
        {
            if (party == null) throw new InvalidOperationException("[CharacterPartyManager] 队伍尚未初始化，不能提交槽位编辑。");
            if (candidateParty == null) throw new ArgumentNullException(nameof(candidateParty));
            IReadOnlyList<CharacterId> candidateIds = candidateParty.CreateSnapshot();
            for (int index = 0; index < candidateIds.Count; index++)
                if (candidateIds[index].IsValid && !rosterManager.IsOwned(candidateIds[index]))
                    throw new InvalidOperationException($"[CharacterPartyManager] 候选队伍包含未拥有角色：{candidateIds[index]}。");

            party = candidateParty;
        }

        /// <summary>运行时 Actor 与操控对象同步完成后，向订阅者发送一次队伍变化通知。</summary>
        internal void PublishSlotEditChanged()
        {
            Debug.Log("[CharacterPartyManager] 队伍槽位编辑已完成运行时同步，变化通知发送一次。", null);
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
