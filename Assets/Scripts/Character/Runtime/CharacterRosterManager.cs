using System;
using System.Collections.Generic;
using RPG.SaveSystem;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WSEventSystem = WS_Modules.CustomEventSystem.EventSystem;

namespace RPG.Character
{
    /// <summary>持有玩家已经获得的角色标识，并为角色装备系统提供可信的拥有事实。</summary>
    /// <remarks>
    /// 角色拥有状态只保存稳定 CharacterId；场景中的 CharacterActor 仍由 CharacterManager 管理。
    /// 角色拥有状态的存档模块由 SaveManager 注册，在存档恢复时会调用 RestoreState() 恢复状态。
    /// 新角色获得后的拥有变化事件由 AcquireCharacter() 在写入拥有事实后同步发布；事件只表达拥有变化，不承担武器事务回滚。
    /// </remarks>
    public sealed class CharacterRosterManager : AbstractManager
    {
        #region 状态字段

        // 角色拥有状态只保存稳定 CharacterId；场景中的 CharacterActor 仍由 CharacterManager 管理。
        private readonly HashSet<CharacterId> ownedCharacterIds = new HashSet<CharacterId>();

        #endregion

        #region 依赖字段

        private readonly SaveManager saveManager;

        #endregion

        #region 构造

        /// <summary>创建由 GameArchitecture 持有的角色拥有 Manager。</summary>
        /// <param name="saveManager">用于注册角色拥有存档模块的 Manager。</param>
        public CharacterRosterManager(SaveManager saveManager)
        {
            this.saveManager = saveManager ?? throw new ArgumentNullException(nameof(saveManager));
        }

        #endregion

        #region 生命周期

        /// <summary>初始化角色拥有状态 Manager。</summary>
        protected override void OnInit()
        {
            saveManager.RegisterModule(new CharacterRosterSaveModule(this));
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

        #region 角色获得与存档

        /// <summary>获得一个角色并发布角色拥有变化事件。</summary>
        /// <param name="characterId">待获得的角色稳定标识。</param>
        /// <returns>角色获得结果；角色已经拥有时返回 AlreadyOwned，不会重复发布事件。</returns>
        public CharacterAcquisitionResult AcquireCharacter(CharacterId characterId)
        {
            if (!characterId.IsValid)
                return Fail(CharacterAcquisitionStatus.InvalidCharacterId, characterId);

            if (!CharacterConfigManager.Instance.TryGetConfig(characterId, out CharacterConfig config))
                return Fail(CharacterAcquisitionStatus.CharacterNotFound, characterId);

            // 角色拥有入口只验证角色配置本身；默认武器的解析和创建由装备系统响应事件完成。
            config.Validate();
            if (!ownedCharacterIds.Add(characterId))
            {
                Debug.Log($"[CharacterRosterManager] 角色已经拥有，跳过重复获得事件，character={characterId}, " +
                          $"ownedCount={ownedCharacterIds.Count}。 ");
                return new CharacterAcquisitionResult(CharacterAcquisitionStatus.AlreadyOwned, characterId);
            }

            // 先写入角色拥有事实，再同步通知装备系统；自动装配失败时保留该拥有事实，供后续修复入口处理。
            Debug.Log($"[CharacterRosterManager] 写入角色拥有事实，准备发布获得事件，character={characterId}, " +
                      $"ownedCount={ownedCharacterIds.Count}。 ");
            WSEventSystem.EventTrigger_Type(
                typeof(CharacterOwnershipChangedEvent),
                new CharacterOwnershipChangedEvent(characterId, true));
            Debug.Log($"[CharacterRosterManager] 完成角色获得并发布拥有事件，character={characterId}, " +
                      $"ownedCount={ownedCharacterIds.Count}。 ");
            return new CharacterAcquisitionResult(CharacterAcquisitionStatus.Succeeded, characterId);
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

        #region 内部辅助

        /// <summary>记录角色获得失败并构造不携带武器数据的结果。</summary>
        /// <param name="status">角色获得失败状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        /// <returns>角色获得失败结果。</returns>
        private static CharacterAcquisitionResult Fail(
            CharacterAcquisitionStatus status,
            CharacterId characterId)
        {
            Debug.LogWarning($"[CharacterRosterManager] 角色获得失败，character={characterId}, status={status}。 ");
            return new CharacterAcquisitionResult(status, characterId);
        }

        #endregion
    }

    /// <summary>角色拥有入口的结果状态。</summary>
    public enum CharacterAcquisitionStatus
    {
        /// <summary>本次成功新增角色拥有事实。</summary>
        Succeeded = 0,
        /// <summary>角色已经拥有，不会重复发布获得事件。</summary>
        AlreadyOwned,
        /// <summary>角色标识无效。</summary>
        InvalidCharacterId,
        /// <summary>角色配置不存在。</summary>
        CharacterNotFound
    }

    /// <summary>角色拥有入口的不可变结果。</summary>
    public readonly struct CharacterAcquisitionResult
    {
        /// <summary>创建角色拥有结果。</summary>
        /// <param name="status">角色获得状态。</param>
        /// <param name="characterId">相关角色标识。</param>
        public CharacterAcquisitionResult(
            CharacterAcquisitionStatus status,
            CharacterId characterId)
        {
            Status = status;
            CharacterId = characterId;
        }

        /// <summary>获取角色获得状态。</summary>
        public CharacterAcquisitionStatus Status { get; }

        /// <summary>获取相关角色标识。</summary>
        public CharacterId CharacterId { get; }

        /// <summary>判断角色已经拥有或本次成功新增。</summary>
        public bool Succeeded => Status == CharacterAcquisitionStatus.Succeeded ||
                                  Status == CharacterAcquisitionStatus.AlreadyOwned;
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
