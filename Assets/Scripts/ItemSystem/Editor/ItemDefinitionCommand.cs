#if UNITY_EDITOR
namespace RPG.ItemSystem.Editor
{
    /// <summary>物品列表右键菜单和工具栏共享的定义操作。</summary>
    internal enum ItemDefinitionCommand
    {
        /// <summary>复制定义资产。</summary>
        Duplicate,
        /// <summary>应用当前物品类型默认值。</summary>
        ApplyTypeDefaults,
        /// <summary>在 Project 窗口定位资产。</summary>
        PingAsset,
        /// <summary>只从数据库移除定义。</summary>
        RemoveFromDatabase,
        /// <summary>将定义资产移入回收站。</summary>
        DeleteAsset
    }
}
#endif
