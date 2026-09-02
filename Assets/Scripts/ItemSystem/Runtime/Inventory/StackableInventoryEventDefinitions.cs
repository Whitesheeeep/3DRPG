namespace RPG.ItemSystem
{
    /// <summary>一个可堆叠物品数量变化事件。</summary>
    public readonly struct StackableItemChangedEvent
    {
        /// <summary>创建数量变化事件。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="previousQuantity">变化前数量。</param>
        /// <param name="currentQuantity">变化后数量。</param>
        /// <param name="isNew">变化后新获得标记。</param>
        public StackableItemChangedEvent(ItemId itemId, int previousQuantity, int currentQuantity, bool isNew)
        {
            ItemId = itemId;
            PreviousQuantity = previousQuantity;
            CurrentQuantity = currentQuantity;
            IsNew = isNew;
        }

        /// <summary>获取物品标识。</summary>
        public ItemId ItemId { get; }

        /// <summary>获取变化前数量。</summary>
        public int PreviousQuantity { get; }

        /// <summary>获取变化后数量。</summary>
        public int CurrentQuantity { get; }

        /// <summary>获取新获得状态。</summary>
        public bool IsNew { get; }
    }

    /// <summary>可堆叠背包恢复完成事件。</summary>
    public readonly struct StackableInventoryRestoredEvent
    {
    }
}
