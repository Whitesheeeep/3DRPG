using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>持有玩家已经获得的角色标识，并为角色装备关系提供拥有事实。</summary>
    public sealed class CharacterRosterManager : AbstractManager
    {
        #region 状态字段

        // 角色拥有状态只保存稳定 CharacterId；场景中的 CharacterActor 仍由 CharacterManager 管理。
        private readonly HashSet<CharacterId> ownedCharacterIds = new HashSet<CharacterId>();

        #endregion

        #region 生命周期

        /// <summary>初始化角色拥有状态 Manager。</summary>
        protected override void OnInit()
        {
            Debug.Log("[CharacterRosterManager] 角色拥有状态已初始化。 ");
        }

        /// <summary>注销时清空当前存档对应的角色拥有事实。</summary>
        protected override void OnDeinit()
        {
            int previousCount = ownedCharacterIds.Count;
            ownedCharacterIds.Clear();
            Debug.Log($"[CharacterRosterManager] 角色拥有状态已清理，previousCount={previousCount}。 ");
        }

        #endregion

        #region 查询

        /// <summary>判断玩家是否已经拥有指定角色。</summary>
        /// <param name="characterId">角色稳定标识。</param>
        /// <returns>已拥有时返回 true。</returns>
        public bool IsOwned(CharacterId characterId) => ownedCharacterIds.Contains(characterId);

        /// <summary>获取角色拥有状态的稳定排序副本。</summary>
        /// <returns>按 CharacterId 文本排序的拥有角色列表。</returns>
        public IReadOnlyList<CharacterId> GetOwnedCharacterIds()
        {
            var result = new List<CharacterId>(ownedCharacterIds);
            result.Sort((left, right) => string.Compare(
                left.ToString(), right.ToString(), StringComparison.Ordinal));
            return result;
        }

        #endregion

        #region 状态修改与存档

        /// <summary>写入一个新的角色拥有事实，等待跨业务事务成功后再发布变化事件。</summary>
        /// <param name="characterId">待拥有的角色标识。</param>
        /// <returns>本次确实新增拥有事实时返回 true。</returns>
        /// <exception cref="ArgumentException">角色标识无效时抛出。</exception>
        internal bool TryAddOwnedCharacter(CharacterId characterId)
        {
            if (!characterId.IsValid) throw new ArgumentException("角色标识无效。", nameof(characterId));
            if (!ownedCharacterIds.Add(characterId)) return false;

            Debug.Log($"[CharacterRosterManager] 写入角色拥有事实，等待装备事务提交，character={characterId}, " +
                      $"ownedCount={ownedCharacterIds.Count}。 ");
            return true;
        }

        /// <summary>发布已经与默认武器创建共同提交的角色拥有事件。</summary>
        /// <param name="characterId">已经写入拥有集合的角色标识。</param>
        /// <exception cref="InvalidOperationException">角色尚未写入拥有集合时抛出。</exception>
        internal void PublishOwnershipChanged(CharacterId characterId)
        {
            if (!ownedCharacterIds.Contains(characterId))
                throw new InvalidOperationException($"角色拥有事件不能发布未写入的角色：{characterId}。 ");
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterOwnershipChangedEvent),
                new CharacterOwnershipChangedEvent(characterId, true));
            Debug.Log($"[CharacterRosterManager] 提交角色拥有事件，character={characterId}, ownedCount={ownedCharacterIds.Count}。 ");
        }

        /// <summary>回滚一个角色拥有事实，不向外发布未完成获取事件。</summary>
        /// <param name="characterId">待移除的角色标识。</param>
        /// <returns>本次确实移除拥有事实时返回 true。</returns>
        internal bool RemoveOwnedCharacter(CharacterId characterId)
        {
            if (!ownedCharacterIds.Remove(characterId)) return false;
            Debug.Log($"[CharacterRosterManager] 回滚角色拥有事实，character={characterId}, ownedCount={ownedCharacterIds.Count}。 ");
            return true;
        }

        /// <summary>用已完成校验的角色拥有列表替换当前状态。</summary>
        /// <param name="restoredCharacterIds">存档恢复的角色标识列表。</param>
        /// <exception cref="ArgumentNullException">列表为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">列表包含空项或重复角色时抛出。</exception>
        internal void RestoreState(IReadOnlyList<CharacterId> restoredCharacterIds)
        {
            if (restoredCharacterIds == null) throw new ArgumentNullException(nameof(restoredCharacterIds));

            var restoredIds = new HashSet<CharacterId>();
            for (int index = 0; index < restoredCharacterIds.Count; index++)
            {
                CharacterId characterId = restoredCharacterIds[index];
                if (!characterId.IsValid || !restoredIds.Add(characterId))
                    throw new InvalidOperationException("角色拥有存档包含无效或重复角色标识。 ");
            }

            ownedCharacterIds.Clear();
            foreach (CharacterId characterId in restoredIds)
                ownedCharacterIds.Add(characterId);
            Debug.Log($"[CharacterRosterManager] 恢复角色拥有状态，ownedCount={ownedCharacterIds.Count}。 ");
        }

        /// <summary>发布角色拥有状态已经恢复完成事件。</summary>
        internal void PublishRestored()
        {
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterRosterRestoredEvent),
                new CharacterRosterRestoredEvent());
        }

        #endregion
    }

    /// <summary>单个角色拥有状态变化事件。</summary>
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

    /// <summary>角色拥有状态存档恢复完成事件。</summary>
    public readonly struct CharacterRosterRestoredEvent
    {
    }
}
