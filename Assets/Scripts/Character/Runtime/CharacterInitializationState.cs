namespace RPG.Character
{
    /// <summary>描述 CharacterManager 的异步队伍加载阶段。</summary>
    public enum CharacterInitializationState
    {
        /// <summary>尚未开始加载。</summary>
        Uninitialized,
        /// <summary>正在并发加载角色 Prefab。</summary>
        Loading,
        /// <summary>队伍已原子提交并可推进。</summary>
        Ready,
        /// <summary>加载或校验失败，当前队伍不可推进。</summary>
        Failed,
        /// <summary>管理器已销毁。</summary>
        Destroyed
    }
}
