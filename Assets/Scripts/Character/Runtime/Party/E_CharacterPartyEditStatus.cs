namespace RPG.Character
{
    /// <summary>描述角色队伍槽位编辑事务的最终结果。</summary>
    public enum E_CharacterPartyEditStatus
    {
        /// <summary>槽位编辑及运行时角色同步均已完成。</summary>
        Success = 0,
        /// <summary>目标位置与当前队伍状态相同。</summary>
        NoChange = 1,
        /// <summary>队伍运行时尚未就绪。</summary>
        NotReady = 2,
        /// <summary>目标槽位不在固定槽位范围内。</summary>
        InvalidSlot = 3,
        /// <summary>角色未拥有或标识无效。</summary>
        CharacterNotOwned = 4,
        /// <summary>拒绝退出最后一名队员。</summary>
        LastMember = 5,
        /// <summary>需要离队的角色仍在执行能力。</summary>
        CharacterBusy = 6,
        /// <summary>已有另一个队伍位置事务尚未完成。</summary>
        EditInProgress = 7,
        /// <summary>新角色 Prefab 或其运行时武器表现初始化失败。</summary>
        LoadFailed = 8,
        /// <summary>Player 生命周期结束，未提交队伍变化。</summary>
        Cancelled = 9
    }
}
