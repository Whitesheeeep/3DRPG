using System;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.ItemSystem.Use
{
    /// <summary>将食物配置的 Gameplay Effect 应用到角色，并在至少一个效果成功后消耗一份库存。</summary>
    public sealed class FoodItemUseService
    {
        #region 依赖字段

        // 可堆叠库存由窗口组合根注入；食物使用成功后通过此 Manager 原子扣除一份。
        private readonly StackableInventoryManager stackableInventoryManager;

        #endregion

        #region 构造

        /// <summary>创建食物使用服务。</summary>
        /// <param name="inventoryManager">管理食物数量和库存事件的可堆叠库存 Manager。</param>
        /// <exception cref="ArgumentNullException">库存 Manager 为空时抛出。</exception>
        public FoodItemUseService(StackableInventoryManager inventoryManager)
        {
            stackableInventoryManager = inventoryManager ?? throw new ArgumentNullException(nameof(inventoryManager));
        }

        #endregion

        #region 食物使用

        /// <summary>依次向目标 ASC 应用食物 GE；至少一项成功后消耗一份食物。</summary>
        /// <param name="itemId">要使用的食物稳定标识。</param>
        /// <param name="target">同时作为 GE Source 和 Target 的角色 ASC。</param>
        /// <returns>至少一个 GE 应用成功且库存扣除成功时返回 true。</returns>
        /// <exception cref="ArgumentException">ItemId 无效时抛出。</exception>
        /// <exception cref="InvalidOperationException">库存中的食物定义无效或扣除失败时抛出。</exception>
        public bool TryUseFood(ItemId itemId, GameplayAbilitySystemComponent target)
        {
            if (!itemId.IsValid)
            {
                Debug.LogError("[FoodItemUseService] 拒绝使用食物：ItemId 无效。");
                throw new ArgumentException("食物 ItemId 无效。", nameof(itemId));
            }
            if (target == null || !target.isActiveAndEnabled || !target.IsInitialized)
            {
                Debug.LogWarning($"[FoodItemUseService] 食物使用失败，ItemId={itemId}，目标 ASC 未就绪。");
                return false;
            }

            if (stackableInventoryManager.GetQuantity(itemId) <= 0)
            {
                Debug.LogWarning($"[FoodItemUseService] 食物使用失败，ItemId={itemId}，库存数量不足。");
                return false;
            }

            ItemManager itemManager = ItemManager.Instance;
            if (!itemManager.TryGetDefinition(itemId, out ItemDefinition definition))
            {
                Debug.LogError($"[FoodItemUseService] 库存 ItemId={itemId} 找不到物品定义。");
                throw new InvalidOperationException($"[FoodItemUseService] 库存 ItemId={itemId} 找不到物品定义。");
            }
            if (!(definition is FoodItemDefinition food))
            {
                Debug.LogError($"[FoodItemUseService] ItemId={itemId} 的定义不是食物，definition={definition.name}。");
                throw new InvalidOperationException($"[FoodItemUseService] ItemId={itemId} 的定义不是食物。");
            }

            food.Validate();
            // 复制配置引用快照，确保后续 GE 回调引发库存刷新时不会改变本次处理列表。
            GameplayEffectData[] effects = new GameplayEffectData[food.UseEffects.Count];
            for (int index = 0; index < effects.Length; index++)
                effects[index] = food.UseEffects[index];

            int succeededCount = 0;
            int rejectedCount = 0;
            for (int index = 0; index < effects.Length; index++)
            {
                if (target.TryApplyEffect(effects[index], target, out GameEffectRuntime _))
                    succeededCount++;
                else
                    rejectedCount++;
            }

            if (succeededCount == 0)
            {
                Debug.LogWarning($"[FoodItemUseService] 食物效果全部被 GAS 拒绝，ItemId={itemId}，target={target.gameObject.name}，" +
                                 $"succeeded={succeededCount}，rejected={rejectedCount}，consumed=false。");
                return false;
            }

            StackableItemOperationResult consumeResult = stackableInventoryManager.ConsumeItem(itemId, 1);
            if (!consumeResult.Succeeded)
            {
                Debug.LogError($"[FoodItemUseService] GE 已应用但食物扣除失败，ItemId={itemId}，status={consumeResult.Status}，" +
                               $"target={target.gameObject.name}，succeeded={succeededCount}，rejected={rejectedCount}。");
                throw new InvalidOperationException(
                    $"[FoodItemUseService] GE 已应用但食物扣除失败，ItemId={itemId}，status={consumeResult.Status}，" +
                    $"target={target.gameObject.name}，succeeded={succeededCount}，rejected={rejectedCount}。");
            }

            Debug.Log($"[FoodItemUseService] 食物使用完成，ItemId={itemId}，target={target.gameObject.name}，" +
                      $"succeeded={succeededCount}，rejected={rejectedCount}，consumed=true。");
            return true;
        }

        #endregion
    }
}
