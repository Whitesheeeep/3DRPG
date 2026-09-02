namespace RPG.Character
{
    /// <summary>描述队伍内部角色切换请求的处理结果。</summary>
    public enum CharacterSwitchStatus
    {
        /// <summary>切换已完成。</summary>
        Success,
        /// <summary>CharacterManager 尚未初始化。</summary>
        NotInitialized,
        /// <summary>目标角色不在当前队伍。</summary>
        CharacterNotFound,
        /// <summary>目标已经是当前角色。</summary>
        AlreadyActive,
        /// <summary>当前角色或目标角色仍有活动能力。</summary>
        CharacterBusy
    }
}
