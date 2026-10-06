namespace RPG.PlayerInputSystem
{
    /// <summary>表示会进入游戏输入请求缓冲区的离散输入类型。</summary>
    public enum E_PlayerInputType
    {
        Primary = 0,
        Secondary = 1,
        Skill1 = 2,
        Skill2 = 3,
        Skill3 = 4,
        Skill4 = 5,
        Jump = 6,
        // Crouch 的旧数值 7 保留为空位，避免现存序列化输入类型整体偏移。
        Interact = 8,
        InteractionPrevious = 9,
        InteractionNext = 10,
        /// <summary>切换到队伍槽位 1。</summary>
        CharacterSlot1 = 11,
        /// <summary>切换到队伍槽位 2。</summary>
        CharacterSlot2 = 12,
        /// <summary>切换到队伍槽位 3。</summary>
        CharacterSlot3 = 13,
        /// <summary>切换到队伍槽位 4。</summary>
        CharacterSlot4 = 14,
        /// <summary>持续奔跑输入；保持 15 以兼容 Player.prefab 中已有的序列化绑定。</summary>
        Sprint = 15,
        /// <summary>请求打开背包窗口；该输入由 PlayerInputController 即时转发，不进入玩法缓冲。</summary>
        BagWindow = 16,
        /// <summary>请求执行当前 UI 退出命令；该输入由 PlayerInputController 即时转发，不进入玩法缓冲。</summary>
        CancelWindow = 17,
        /// <summary>请求打开或关闭任务窗口；该输入由 UI Map 即时转发。</summary>
        TaskWindow = 18,
        /// <summary>切换当前锁定目标状态；该输入由 Player Map 即时转发。</summary>
        LockToggle = 19,
        /// <summary>向屏幕左侧切换锁定候选；由滚轮向下操作触发。</summary>
        LockPrevious = 20,
        /// <summary>向屏幕右侧切换锁定候选；由滚轮向上操作触发。</summary>
        LockNext = 21,
        /// <summary>请求打开角色窗口；该输入由 UI Map 即时转发。</summary>
        CharacterWindow = 22
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
        Release,
        Click
    }
}
