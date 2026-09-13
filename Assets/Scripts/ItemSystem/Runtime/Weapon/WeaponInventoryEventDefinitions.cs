namespace RPG.ItemSystem
{
    /// <summary>武器实例状态变化事件。</summary>
    public readonly struct WeaponInstanceChangedEvent
    {
        /// <summary>创建武器变化事件。</summary>
        /// <param name="changeType">变化类型。</param>
        /// <param name="instance">变化后的或删除前的实例。</param>
        public WeaponInstanceChangedEvent(EquipmentInstanceChangeType changeType, WeaponInstance instance)
        {
            ChangeType = changeType;
            Instance = instance;
        }

        /// <summary>获取变化类型。</summary>
        public EquipmentInstanceChangeType ChangeType { get; }

        /// <summary>获取实例快照。</summary>
        public WeaponInstance Instance { get; }
    }

    /// <summary>武器背包恢复完成事件。</summary>
    public readonly struct WeaponInventoryRestoredEvent
    {
    }

    /// <summary>武器 Definition 的新获得状态变化事件。</summary>
    public readonly struct WeaponDefinitionNewStateChangedEvent
    {
        /// <summary>创建武器 Definition New 状态变化事件。</summary>
        /// <param name="definitionId">发生变化的武器 Definition。</param>
        /// <param name="isNew">变化后的 New 状态。</param>
        public WeaponDefinitionNewStateChangedEvent(ItemId definitionId, bool isNew)
        {
            DefinitionId = definitionId;
            IsNew = isNew;
        }

        /// <summary>获取发生变化的武器 Definition。</summary>
        public ItemId DefinitionId { get; }

        /// <summary>获取变化后的 New 状态。</summary>
        public bool IsNew { get; }
    }
}
