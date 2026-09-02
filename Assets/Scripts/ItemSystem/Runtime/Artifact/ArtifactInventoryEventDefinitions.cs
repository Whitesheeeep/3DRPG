namespace RPG.ItemSystem
{
    /// <summary>圣遗物实例状态变化事件。</summary>
    public readonly struct ArtifactInstanceChangedEvent
    {
        /// <summary>创建圣遗物变化事件。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">变化后的或删除前的实例。</param>
        public ArtifactInstanceChangedEvent(EquipmentInstanceChangeType changeType, ArtifactInstance instance)
        {
            ChangeType = changeType;
            Instance = instance;
        }

        /// <summary>获取变化类型。</summary>
        public EquipmentInstanceChangeType ChangeType { get; }

        /// <summary>获取实例快照。</summary>
        public ArtifactInstance Instance { get; }
    }

    /// <summary>圣遗物背包恢复完成事件。</summary>
    public readonly struct ArtifactInventoryRestoredEvent
    {
    }
}
