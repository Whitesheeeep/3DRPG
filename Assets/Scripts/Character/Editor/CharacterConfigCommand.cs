#if UNITY_EDITOR
namespace RPG.Character.Editor
{
    /// <summary>角色配置列表右键菜单和工具栏共享的操作类型。</summary>
    internal enum CharacterConfigCommand
    {
        /// <summary>复制当前角色配置资产。</summary>
        Duplicate,
        /// <summary>在 Project 窗口定位当前资产。</summary>
        PingAsset,
        /// <summary>验证当前角色配置。</summary>
        Validate,
        /// <summary>仅从角色数据库移除配置。</summary>
        RemoveFromDatabase,
        /// <summary>将角色配置资产移入 Unity 回收站。</summary>
        DeleteAsset
    }
}
#endif
