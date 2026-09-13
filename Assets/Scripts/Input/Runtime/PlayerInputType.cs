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
        CharacterSlot4,
        /// <summary>持续奔跑输入；保持 15 以兼容 Player.prefab 中已有的序列化绑定。</summary>
        Sprint = 15,
        /// <summary>请求打开背包窗口；该输入由 PlayerInputController 即时转发，不进入玩法缓冲。</summary>
        BagWindow = 16,
        /// <summary>请求执行当前 UI 退出命令；该输入由 PlayerInputController 即时转发，不进入玩法缓冲。</summary>
        CancelWindow = 17
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
