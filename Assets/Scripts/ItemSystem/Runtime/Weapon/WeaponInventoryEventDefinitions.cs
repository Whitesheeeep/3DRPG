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
}
