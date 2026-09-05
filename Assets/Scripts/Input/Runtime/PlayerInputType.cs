namespace RPG.PlayerInputSystem
{
    /// <summary>表示会进入游戏输入请求缓冲区的离散输入类型。</summary>
    public enum PlayerInputType
    {
        Primary,
        Secondary,
        Skill1,
        Skill2,
        Skill3,
        Skill4,
        Jump,
        Crouch,
        Interact,
        InteractionPrevious,
        InteractionNext,
        /// <summary>切换到队伍槽位 1。</summary>
        CharacterSlot1,
        /// <summary>切换到队伍槽位 2。</summary>
        CharacterSlot2,
        /// <summary>切换到队伍槽位 3。</summary>
        CharacterSlot3,
        /// <summary>切换到队伍槽位 4。</summary>
        CharacterSlot4
    }

    /// <summary>表示一次输入手势当前的物理阶段。</summary>
    public enum PlayerInputPhysicalState
    {
        Pressed,
        Held,
        Released
    }

    /// <summary>区分一次输入手势中可独立缓冲和消费的阶段。</summary>
    public enum PlayerInputRequestStage
    {
        Press,
        Release
    }
}
