namespace RPG.SaveSystem
{
    /// <summary>
    /// 集中保存本地存档目录的稳定命名约定，避免运行时装配和手动测试各自拼接路径。
    /// </summary>
    public static class SaveStorageDefaults
    {
        /// <summary>
        /// 位于 Application.persistentDataPath 下的正式存档子目录名称。
        /// </summary>
        public const string LocalDirectoryName = "Saves";
    }
}
