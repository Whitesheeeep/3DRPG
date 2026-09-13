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

    /// <summary>圣遗物 Definition 的新获得状态变化事件。</summary>
    public readonly struct ArtifactDefinitionNewStateChangedEvent
    {
        /// <summary>创建圣遗物 Definition New 状态变化事件。</summary>
        /// <param name="definitionId">发生变化的圣遗物 Definition。</param>
        /// <param name="isNew">变化后的 New 状态。</param>
        public ArtifactDefinitionNewStateChangedEvent(ItemId definitionId, bool isNew)
        {
            DefinitionId = definitionId;
            IsNew = isNew;
        }

        /// <summary>获取发生变化的圣遗物 Definition。</summary>
        public ItemId DefinitionId { get; }

        /// <summary>获取变化后的 New 状态。</summary>
        public bool IsNew { get; }
    }
}
