using System;

namespace RPG.ItemSystem
{
    /// <summary>可堆叠物品在玩家背包中的只读数量条目。</summary>
    [Serializable]
    public sealed class StackableInventoryEntry
    {
        /// <summary>创建数量条目。</summary>
        /// <param name="itemId">物品标识。</param>
        /// <param name="quantity">当前数量。</param>
        /// <param name="isNew">是否为新获得。</param>
        /// <param name="acquisitionSequence">首次获得顺序。</param>
        public StackableInventoryEntry(ItemId itemId, int quantity, bool isNew, long acquisitionSequence)
        {
            ItemId = itemId;
            Quantity = quantity;
            IsNew = isNew;
            AcquisitionSequence = acquisitionSequence;
        }

        /// <summary>获取物品标识。</summary>
        public ItemId ItemId { get; }

        /// <summary>获取当前数量。</summary>
        public int Quantity { get; }

        /// <summary>获取新获得标记。</summary>
        public bool IsNew { get; }

        /// <summary>获取首次获得顺序。</summary>
        public long AcquisitionSequence { get; }
    }

    /// <summary>可堆叠背包操作结果。</summary>
    public readonly struct StackableItemOperationResult
    {
        /// <summary>创建可堆叠操作结果。</summary>
        /// <param name="status">操作状态。</param>
        /// <param name="itemId">关联物品。</param>
        public StackableItemOperationResult(InventoryOperationStatus status, ItemId itemId)
        {
            Status = status;
            ItemId = itemId;
        }

        /// <summary>获取状态。</summary>
        public InventoryOperationStatus Status { get; }

        /// <summary>获取关联物品。</summary>
        public ItemId ItemId { get; }

        /// <summary>判断是否成功。</summary>
        public bool Succeeded => Status == InventoryOperationStatus.Succeeded;
    }
}
