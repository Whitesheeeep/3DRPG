using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.RewardSystemNS
{
    /// <summary>把物品奖励按 Definition 类型准备到三类现有库存。</summary>
    public sealed class ItemRewardHandler : IRewardHandler
    {
        #region 依赖字段

        private readonly StackableInventoryManager stackableInventoryManager;
        private readonly WeaponInventoryManager weaponInventoryManager;
        private readonly ArtifactInventoryManager artifactInventoryManager;

        #endregion

        #region 构造与类型

        /// <summary>创建物品奖励 Handler。</summary>
        /// <param name="stackableManager">可堆叠背包 Manager。</param>
        /// <param name="weaponManager">武器实例 Manager。</param>
        /// <param name="artifactManager">圣遗物实例 Manager。</param>
        /// <exception cref="ArgumentNullException">任一 Manager 为空时抛出。</exception>
        public ItemRewardHandler(
            StackableInventoryManager stackableManager,
            WeaponInventoryManager weaponManager,
            ArtifactInventoryManager artifactManager)
        {
            stackableInventoryManager = stackableManager ?? throw new ArgumentNullException(nameof(stackableManager));
            weaponInventoryManager = weaponManager ?? throw new ArgumentNullException(nameof(weaponManager));
            artifactInventoryManager = artifactManager ?? throw new ArgumentNullException(nameof(artifactManager));
        }

        /// <summary>获取精确处理的奖励定义类型。</summary>
        public Type DefinitionType => typeof(ItemRewardDefinition);

        #endregion

        #region 批次准备

        /// <summary>合并物品奖励并准备全部涉及的库存批次。</summary>
        /// <param name="definitions">物品奖励定义。</param>
        /// <param name="preparedParts">成功时返回涉及库存的批次。</param>
        /// <param name="result">成功或库存拒绝结果。</param>
        /// <returns>全部库存准备成功时返回 true。</returns>
        /// <exception cref="InvalidOperationException">奖励引用未知或不支持的物品定义时抛出。</exception>
        public bool TryPrepare(
            IReadOnlyList<RewardDefinition> definitions,
            out IReadOnlyList<IPreparedRewardPart> preparedParts,
            out RewardGrantResult result)
        {
            var quantityByItemIdMap = new Dictionary<ItemId, int>();
            var itemOrder = new List<ItemId>();
            for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                var definition = (ItemRewardDefinition)definitions[definitionIndex];
                for (int itemIndex = 0; itemIndex < definition.Items.Count; itemIndex++)
                {
                    ItemRewardEntry entry = definition.Items[itemIndex];
                    if (!quantityByItemIdMap.TryGetValue(entry.ItemId, out int current))
                    {
                        quantityByItemIdMap.Add(entry.ItemId, entry.Quantity);
                        itemOrder.Add(entry.ItemId);
                        continue;
                    }

                    try { quantityByItemIdMap[entry.ItemId] = checked(current + entry.Quantity); }
                    catch (OverflowException exception)
                    {
                        Debug.LogError($"[ItemRewardHandler] 重复物品奖励数量合并溢出，itemId={entry.ItemId}。");
                        throw new ArgumentException($"奖励物品总量超出整数范围：{entry.ItemId}。", nameof(definitions), exception);
                    }
                }
            }

            var stackableItems = new List<ItemQuantity>();
            var weaponDefinitionIds = new List<ItemId>();
            var artifactDefinitionIds = new List<ItemId>();
            for (int index = 0; index < itemOrder.Count; index++)
            {
                ItemId itemId = itemOrder[index];
                if (!ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition itemDefinition) || itemDefinition == null)
                {
                    Debug.LogError($"[ItemRewardHandler] 奖励引用未知 ItemDefinition，itemId={itemId}。");
                    throw new InvalidOperationException($"[ItemRewardHandler] 奖励引用未知 ItemDefinition：{itemId}。");
                }

                int quantity = quantityByItemIdMap[itemId];
                if (itemDefinition is StackableItemDefinition)
                {
                    stackableItems.Add(new ItemQuantity(itemId, quantity));
                }
                else if (itemDefinition is WeaponDefinition)
                {
                    if (quantity > weaponInventoryManager.RemainingStorageCapacity)
                        return RejectInventory(InventoryOperationStatus.CapacityExceeded, itemId, out preparedParts, out result);
                    for (int itemCount = 0; itemCount < quantity; itemCount++) weaponDefinitionIds.Add(itemId);
                }
                else if (itemDefinition is ArtifactDefinition)
                {
                    if (quantity > artifactInventoryManager.Capacity - artifactInventoryManager.Count)
                        return RejectInventory(InventoryOperationStatus.CapacityExceeded, itemId, out preparedParts, out result);
                    for (int itemCount = 0; itemCount < quantity; itemCount++) artifactDefinitionIds.Add(itemId);
                }
                else
                {
                    Debug.LogError(
                        $"[ItemRewardHandler] 奖励引用了不支持的 ItemDefinition 类型，itemId={itemId}, definitionType={itemDefinition.GetType().FullName}。");
                    throw new InvalidOperationException(
                        $"[ItemRewardHandler] 暂不支持该 ItemDefinition 类型，itemId={itemId}, type={itemDefinition.GetType().FullName}。");
                }
            }

            var parts = new List<IPreparedRewardPart>(3);
            if (stackableItems.Count > 0)
            {
                StackableItemOperationResult inventoryResult = stackableInventoryManager.TryPrepareRewardAddition(
                    stackableItems,
                    out Func<bool> canCommit,
                    out Action commitState,
                    out Action publishNotifications);
                if (!inventoryResult.Succeeded)
                    return RejectInventory(inventoryResult.Status, inventoryResult.ItemId, out preparedParts, out result);
                parts.Add(new PreparedRewardGrantPart(canCommit, commitState, publishNotifications));
            }

            if (weaponDefinitionIds.Count > 0)
            {
                if (!weaponInventoryManager.TryPrepareRewardAddition(
                        weaponDefinitionIds,
                        out InventoryOperationStatus status,
                        out ItemId failedItemId,
                        out _,
                        out Func<bool> canCommit,
                        out Action commitState,
                        out Action publishNotifications))
                    return RejectInventory(status, failedItemId, out preparedParts, out result);
                parts.Add(new PreparedRewardGrantPart(canCommit, commitState, publishNotifications));
            }

            if (artifactDefinitionIds.Count > 0)
            {
                if (!artifactInventoryManager.TryPrepareRewardAddition(
                        artifactDefinitionIds,
                        out InventoryOperationStatus status,
                        out ItemId failedItemId,
                        out _,
                        out Func<bool> canCommit,
                        out Action commitState,
                        out Action publishNotifications))
                    return RejectInventory(status, failedItemId, out preparedParts, out result);
                parts.Add(new PreparedRewardGrantPart(canCommit, commitState, publishNotifications));
            }

            preparedParts = parts;
            result = RewardGrantResult.Success();
            return true;
        }

        /// <summary>建立带库存状态上下文的业务拒绝结果。</summary>
        /// <param name="status">库存操作状态。</param>
        /// <param name="itemId">失败物品标识。</param>
        /// <param name="preparedParts">空的准备批次。</param>
        /// <param name="result">结构化拒绝结果。</param>
        /// <returns>始终返回 false。</returns>
        private static bool RejectInventory(
            InventoryOperationStatus status,
            ItemId itemId,
            out IReadOnlyList<IPreparedRewardPart> preparedParts,
            out RewardGrantResult result)
        {
            preparedParts = Array.Empty<IPreparedRewardPart>();
            result = new RewardGrantResult(
                RewardGrantFailureDomain.Item,
                inventoryStatus: status,
                itemId: itemId);
            return false;
        }

        #endregion
    }
}
