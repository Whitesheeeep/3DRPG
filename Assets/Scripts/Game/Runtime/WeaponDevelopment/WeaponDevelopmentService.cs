using System;
using System.Collections.Generic;
using RPG.CurrencySystem;
using RPG.ItemSystem;

namespace RPG.Game.Runtime.WeaponDevelopment
{
    /// <summary>
    /// 按武器培养窗口生命周期创建的业务服务，负责升级、突破和精炼的校验与提交。
    /// </summary>
    public sealed class WeaponDevelopmentService
    {
        #region 依赖字段

        private readonly WeaponInventoryManager weaponInventoryManager;
        private readonly StackableInventoryManager stackableInventoryManager;
        private readonly CurrencyManager currencyManager;

        #endregion

        #region 构造与公开操作

        /// <summary>创建一个绑定当前窗口库存上下文的武器培养服务。</summary>
        /// <param name="weaponInventoryManager">武器实例库存。</param>
        /// <param name="stackableInventoryManager">堆叠材料库存。</param>
        /// <param name="currencyManager">货币钱包。</param>
        /// <exception cref="ArgumentNullException">依赖为空时抛出。</exception>
        public WeaponDevelopmentService(WeaponInventoryManager weaponInventoryManager,
            StackableInventoryManager stackableInventoryManager, CurrencyManager currencyManager)
        {
            this.weaponInventoryManager = weaponInventoryManager ??
                                          throw new ArgumentNullException(nameof(weaponInventoryManager));
            this.stackableInventoryManager = stackableInventoryManager ??
                                             throw new ArgumentNullException(nameof(stackableInventoryManager));
            this.currencyManager = currencyManager ?? throw new ArgumentNullException(nameof(currencyManager));
        }

        /// <summary>提交当前窗口选择的经验素材，更新武器等级和等级内经验。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        /// <param name="selectedQuantityByItemIdMap">按经验素材 ItemId 聚合的选择数量。</param>
        /// <returns>升级结果。</returns>
        public WeaponDevelopmentOperationResult Enhance(EquipmentInstanceId instanceId,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap)
        {
            if (!TryGetTarget(instanceId, out WeaponDefinition definition, out WeaponInstance instance))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidTarget, "目标武器不存在或定义类型不匹配。");
            if (selectedQuantityByItemIdMap == null || selectedQuantityByItemIdMap.Count == 0)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidSelection, "没有选择武器经验素材。");
            if (!TryResolveCurrentCap(definition, instance, out int currentCap, out _))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "无法解析武器当前阶段等级上限。");

            long selectedExperience = 0L;
            var itemQuantities = new List<ItemQuantity>(selectedQuantityByItemIdMap.Count);
            foreach (KeyValuePair<ItemId, int> pair in selectedQuantityByItemIdMap)
            {
                if (pair.Value <= 0 || !ItemManager.Instance.TryGetDefinition(pair.Key, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition experience) ||
                    !experience.SupportsExperienceType(DevelopmentExperienceItemType.Weapon))
                    return WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidConfiguration,
                        $"经验素材配置无效：{pair.Key}。");
                if (stackableInventoryManager.GetQuantity(pair.Key) < pair.Value)
                    return WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InsufficientMaterials,
                        $"经验素材数量不足：{pair.Key}。");

                try
                {
                    selectedExperience = checked(selectedExperience + (long)pair.Value * experience.ExperienceValue);
                }
                catch (OverflowException)
                {
                    return WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidSelection, "经验总量超出可计算范围。");
                }

                itemQuantities.Add(new ItemQuantity(pair.Key, pair.Value));
            }

            if (!TryBuildProjectedProgress(definition, instance, currentCap, selectedExperience,
                    out int projectedLevel, out int projectedExperience, out long currencyCost) ||
                projectedLevel == instance.Level && projectedExperience == instance.CurrentExperience)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidSelection, "所选经验不足以产生等级进度变化。");

            if (currencyCost > 0L && currencyCost > int.MaxValue)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "升级货币消耗超出整数范围。");
            if (!CanAfford(CurrencyId.Mola, currencyCost))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InsufficientCurrency, "摩拉余额不足。");

            // 所有输入已完成校验后再依次提交，避免正常单线程 UI 流程出现中间状态失败。
            StackableItemOperationResult materialResult = stackableInventoryManager.ConsumeItems(itemQuantities);
            if (!materialResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"消耗经验素材失败：{materialResult.Status}。");
            if (!ConsumeCurrency(CurrencyId.Mola, currencyCost))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, "消耗升级货币失败。");

            EquipmentOperationResult updateResult = weaponInventoryManager.UpdateWeaponProgress(instanceId,
                new WeaponProgressUpdate(projectedLevel, projectedExperience, instance.AscensionRank,
                    instance.RefinementRank));
            if (!updateResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"更新武器进度失败：{updateResult.Status}。");
            return WeaponDevelopmentOperationResult.Success();
        }

        /// <summary>提交当前突破阶段的材料和摩拉，提升武器突破阶数。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        /// <returns>突破结果。</returns>
        public WeaponDevelopmentOperationResult Ascend(EquipmentInstanceId instanceId)
        {
            if (!TryGetTarget(instanceId, out WeaponDefinition definition, out WeaponInstance instance))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidTarget, "目标武器不存在或定义类型不匹配。");
            int stageIndex = instance.AscensionRank;
            if (stageIndex < 0 || stageIndex >= definition.AscensionStages.Count)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "未配置当前突破阶数的下一阶段。");
            WeaponAscensionStage stage = definition.AscensionStages[stageIndex];
            if (stage == null || instance.Level < stage.RequiredLevel)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidSelection, "武器尚未达到当前突破等级上限。");
            if (!TryBuildCost(stage.Cost, DevelopmentItemType.WeaponAscension,
                    out List<ItemQuantity> itemCosts, out List<CurrencyAmount> currencyCosts,
                    out WeaponDevelopmentOperationResult costResult))
                return costResult;

            StackableItemOperationResult materialResult = ConsumeMaterials(itemCosts);
            if (!materialResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"消耗突破材料失败：{materialResult.Status}。");
            if (!ConsumeCurrencies(currencyCosts))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, "消耗突破货币失败。");

            EquipmentOperationResult updateResult = weaponInventoryManager.UpdateWeaponProgress(instanceId,
                new WeaponProgressUpdate(instance.Level, instance.CurrentExperience,
                    instance.AscensionRank + 1, instance.RefinementRank));
            if (!updateResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"更新突破阶数失败：{updateResult.Status}。");
            return WeaponDevelopmentOperationResult.Success();
        }

        /// <summary>提交已选择的同名武器和摩拉，提升目标武器精炼阶数。</summary>
        /// <param name="instanceId">目标武器实例。</param>
        /// <param name="selectedMaterialIds">已选择的同名武器实例。</param>
        /// <returns>精炼结果。</returns>
        public WeaponDevelopmentOperationResult Refine(EquipmentInstanceId instanceId,
            IReadOnlyCollection<EquipmentInstanceId> selectedMaterialIds)
        {
            if (!TryGetTarget(instanceId, out WeaponDefinition definition, out WeaponInstance instance))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidTarget, "目标武器不存在或定义类型不匹配。");
            WeaponRefinementStage stage = FindNextRefinementStage(definition, instance.RefinementRank);
            if (stage == null || stage.RequiredDuplicateCount <= 0)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "未配置当前精炼阶数的下一阶段。");
            if (selectedMaterialIds == null || selectedMaterialIds.Count != stage.RequiredDuplicateCount)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidSelection, "已选择的同名武器数量不足。");

            var materialInstances = new List<EquipmentInstanceId>(selectedMaterialIds.Count);
            var seenMaterialIds = new HashSet<EquipmentInstanceId>();
            foreach (EquipmentInstanceId materialId in selectedMaterialIds)
            {
                if (!seenMaterialIds.Add(materialId) || materialId == instanceId ||
                    !weaponInventoryManager.TryGetInstance(materialId, out WeaponInstance material) ||
                    material.DefinitionId != instance.DefinitionId || material.IsLocked || material.IsEquipped)
                    return WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidSelection, "精炼材料必须是未锁定且未装备的同名武器。");
                materialInstances.Add(materialId);
            }

            if (!TryBuildCurrencyCosts(stage.Cost, out List<CurrencyAmount> currencyCosts,
                    out WeaponDevelopmentOperationResult currencyResult))
                return currencyResult;
            EquipmentOperationResult removeResult = weaponInventoryManager.RemoveWeapons(materialInstances);
            if (!removeResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"移除精炼材料失败：{removeResult.Status}。");
            if (!ConsumeCurrencies(currencyCosts))
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, "消耗精炼货币失败。");

            EquipmentOperationResult updateResult = weaponInventoryManager.UpdateWeaponProgress(instanceId,
                new WeaponProgressUpdate(instance.Level, instance.CurrentExperience, instance.AscensionRank,
                    stage.Rank));
            if (!updateResult.Succeeded)
                return WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.ManagerRejected, $"更新精炼阶数失败：{updateResult.Status}。");
            return WeaponDevelopmentOperationResult.Success();
        }

        #endregion

        #region 成本与成长计算

        /// <summary>验证当前阶段的成本并合并相同 ItemId 与 CurrencyId。</summary>
        /// <param name="cost">阶段成本。</param>
        /// <param name="requestedType">普通养成道具用途。</param>
        /// <param name="itemCosts">合并后的物品成本。</param>
        /// <param name="currencyCosts">合并后的货币成本。</param>
        /// <param name="failure">验证失败结果。</param>
        /// <returns>成本合法时返回 true。</returns>
        private bool TryBuildCost(GrowthCost cost, DevelopmentItemType requestedType,
            out List<ItemQuantity> itemCosts, out List<CurrencyAmount> currencyCosts,
            out WeaponDevelopmentOperationResult failure)
        {
            itemCosts = new List<ItemQuantity>();
            currencyCosts = new List<CurrencyAmount>();
            if (cost == null || cost.ItemCosts == null || cost.CurrencyCosts == null ||
                (cost.ItemCosts.Count == 0 && cost.CurrencyCosts.Count == 0))
            {
                failure = WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段成本未配置。");
                return false;
            }
            if (!TryBuildItemCosts(cost, requestedType, out itemCosts, out failure)) return false;
            if (!TryBuildCurrencyCosts(cost, out currencyCosts, out failure)) return false;
            return true;
        }

        /// <summary>验证并合并普通养成道具成本。</summary>
        /// <param name="cost">阶段成本。</param>
        /// <param name="requestedType">养成用途。</param>
        /// <param name="itemCosts">合并后的物品成本。</param>
        /// <param name="failure">验证失败结果。</param>
        /// <returns>成本合法时返回 true。</returns>
        private bool TryBuildItemCosts(GrowthCost cost, DevelopmentItemType requestedType,
            out List<ItemQuantity> itemCosts, out WeaponDevelopmentOperationResult failure)
        {
            itemCosts = new List<ItemQuantity>();
            failure = default;
            if (cost == null || cost.ItemCosts == null)
            {
                failure = WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段物品成本未配置。");
                return false;
            }

            // key：普通养成道具 ItemId；value：该阶段合并后的需求数量。
            var quantityByItemIdMap = new Dictionary<ItemId, int>();
            for (int index = 0; index < cost.ItemCosts.Count; index++)
            {
                ItemCostEntry entry = cost.ItemCosts[index];
                if (entry == null || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentItemDefinition material) ||
                    !material.SupportsDevelopmentType(requestedType))
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段物品成本必须是有效的养成道具。");
                    return false;
                }

                try
                {
                    quantityByItemIdMap[entry.ItemId] = checked(
                        quantityByItemIdMap.TryGetValue(entry.ItemId, out int current)
                            ? current + entry.Quantity
                            : entry.Quantity);
                }
                catch (OverflowException)
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段物品成本数量溢出。");
                    return false;
                }
            }

            foreach (KeyValuePair<ItemId, int> pair in quantityByItemIdMap)
            {
                if (stackableInventoryManager.GetQuantity(pair.Key) < pair.Value)
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InsufficientMaterials, $"突破材料数量不足：{pair.Key}。");
                    return false;
                }

                itemCosts.Add(new ItemQuantity(pair.Key, pair.Value));
            }

            return true;
        }

        /// <summary>验证并合并货币成本，同时检查当前余额。</summary>
        /// <param name="cost">阶段成本。</param>
        /// <param name="currencyCosts">合并后的货币成本。</param>
        /// <param name="failure">验证失败结果。</param>
        /// <returns>成本合法且余额足够时返回 true。</returns>
        private bool TryBuildCurrencyCosts(GrowthCost cost, out List<CurrencyAmount> currencyCosts,
            out WeaponDevelopmentOperationResult failure)
        {
            currencyCosts = new List<CurrencyAmount>();
            failure = default;
            if (cost == null || cost.CurrencyCosts == null)
            {
                failure = WeaponDevelopmentOperationResult.Failure(
                    WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段货币成本未配置。");
                return false;
            }

            // key：货币类型；value：该阶段合并后的需求金额。
            var amountByCurrencyIdMap = new Dictionary<CurrencyId, int>();
            for (int index = 0; index < cost.CurrencyCosts.Count; index++)
            {
                CurrencyCostEntry entry = cost.CurrencyCosts[index];
                if (entry == null || entry.CurrencyId == CurrencyId.None || entry.Amount <= 0)
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段货币成本无效。");
                    return false;
                }

                try
                {
                    amountByCurrencyIdMap[entry.CurrencyId] = checked(
                        amountByCurrencyIdMap.TryGetValue(entry.CurrencyId, out int current)
                            ? current + entry.Amount
                            : entry.Amount);
                }
                catch (OverflowException)
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InvalidConfiguration, "阶段货币成本金额溢出。");
                    return false;
                }
            }

            foreach (KeyValuePair<CurrencyId, int> pair in amountByCurrencyIdMap)
            {
                if (currencyManager.GetBalance(pair.Key) < pair.Value)
                {
                    failure = WeaponDevelopmentOperationResult.Failure(
                        WeaponDevelopmentOperationStatus.InsufficientCurrency, $"货币余额不足：{pair.Key}。");
                    return false;
                }

                currencyCosts.Add(new CurrencyAmount(pair.Key, pair.Value));
            }

            return true;
        }

        /// <summary>检查指定货币金额是否可支付，零金额视为无需支付。</summary>
        /// <param name="currencyId">货币类型。</param>
        /// <param name="amount">需求金额。</param>
        /// <returns>可以支付时返回 true。</returns>
        private bool CanAfford(CurrencyId currencyId, long amount)
        {
            return amount <= 0L || (amount <= int.MaxValue &&
                                    currencyManager.GetBalance(currencyId) >= (int)amount);
        }

        /// <summary>消耗一项可选货币。</summary>
        /// <param name="currencyId">货币类型。</param>
        /// <param name="amount">消耗金额。</param>
        /// <returns>提交成功或无需消耗时返回 true。</returns>
        private bool ConsumeCurrency(CurrencyId currencyId, long amount) =>
            amount <= 0L || currencyManager.ConsumeCurrencies(new[] { new CurrencyAmount(currencyId, (int)amount) }).Succeeded;

        /// <summary>提交合并后的货币成本。</summary>
        /// <param name="costs">货币成本。</param>
        /// <returns>提交成功或成本为空时返回 true。</returns>
        private bool ConsumeCurrencies(IReadOnlyList<CurrencyAmount> costs) =>
            costs == null || costs.Count == 0 || currencyManager.ConsumeCurrencies(costs).Succeeded;

        /// <summary>提交合并后的物品成本。</summary>
        /// <param name="costs">物品成本。</param>
        /// <returns>提交成功或成本为空时返回 true。</returns>
        private StackableItemOperationResult ConsumeMaterials(IReadOnlyList<ItemQuantity> costs) =>
            costs == null || costs.Count == 0
                ? new StackableItemOperationResult(InventoryOperationStatus.Succeeded, default)
                : stackableInventoryManager.ConsumeItems(costs);

        /// <summary>计算经验素材加入后的等级、等级内经验和升级货币消耗。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="currentCap">当前阶段上限。</param>
        /// <param name="selectedExperience">所选素材经验。</param>
        /// <param name="projectedLevel">预计等级。</param>
        /// <param name="projectedExperience">预计等级内经验。</param>
        /// <param name="currencyCost">预计货币消耗。</param>
        /// <returns>成长表完整且计算成功时返回 true。</returns>
        private static bool TryBuildProjectedProgress(WeaponDefinition definition, WeaponInstance instance,
            int currentCap, long selectedExperience, out int projectedLevel, out int projectedExperience,
            out long currencyCost)
        {
            projectedLevel = instance.Level;
            projectedExperience = instance.CurrentExperience;
            currencyCost = 0L;
            if (definition.GrowthProfile == null || currentCap < instance.Level || currentCap < 1)
                return false;
            BakedWeaponLevelProgression current = GetProgression(definition.GrowthProfile, instance.Level);
            BakedWeaponLevelProgression cap = GetProgression(definition.GrowthProfile, currentCap);
            if (current == null || cap == null) return false;

            long absoluteExperience = Math.Min((long)cap.CumulativeExperience,
                (long)current.CumulativeExperience + instance.CurrentExperience + Math.Max(0L, selectedExperience));
            while (projectedLevel < currentCap)
            {
                BakedWeaponLevelProgression progression = GetProgression(definition.GrowthProfile, projectedLevel);
                if (progression == null || progression.NextExperience <= 0 ||
                    absoluteExperience < (long)progression.CumulativeExperience + progression.NextExperience)
                    break;
                projectedLevel++;
            }

            BakedWeaponLevelProgression projected = GetProgression(definition.GrowthProfile, projectedLevel);
            if (projected == null) return false;
            projectedExperience = Math.Max(0, (int)Math.Min(int.MaxValue,
                absoluteExperience - projected.CumulativeExperience));
            for (int level = instance.Level; level < projectedLevel; level++)
            {
                BakedWeaponLevelProgression progression = GetProgression(definition.GrowthProfile, level);
                if (progression == null) return false;
                currencyCost = checked(currencyCost + progression.CurrencyCost);
            }

            return true;
        }

        /// <summary>解析当前突破阶段的等级上限和下一阶段。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <param name="currentCap">当前阶段上限。</param>
        /// <param name="nextStage">下一突破阶段。</param>
        /// <returns>能够解析成长阶段时返回 true。</returns>
        private static bool TryResolveCurrentCap(WeaponDefinition definition, WeaponInstance instance,
            out int currentCap, out WeaponAscensionStage nextStage)
        {
            nextStage = instance.AscensionRank >= 0 && instance.AscensionRank < definition.AscensionStages.Count
                ? definition.AscensionStages[instance.AscensionRank]
                : null;
            if (nextStage != null && nextStage.RequiredLevel > 0)
            {
                currentCap = nextStage.RequiredLevel;
                return true;
            }

            int completedIndex = instance.AscensionRank - 1;
            if (completedIndex >= 0 && completedIndex < definition.AscensionStages.Count)
                currentCap = definition.AscensionStages[completedIndex].MaxLevelAfter;
            else currentCap = instance.Level;
            return currentCap >= instance.Level && currentCap > 0;
        }

        /// <summary>查询武器实例及其 Definition。</summary>
        /// <param name="instanceId">实例标识。</param>
        /// <param name="definition">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <returns>目标有效时返回 true。</returns>
        private bool TryGetTarget(EquipmentInstanceId instanceId, out WeaponDefinition definition,
            out WeaponInstance instance)
        {
            definition = null;
            instance = null;
            return weaponInventoryManager.TryGetInstance(instanceId, out instance) &&
                   ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item) &&
                   (definition = item as WeaponDefinition) != null;
        }

        /// <summary>查找当前精炼阶数后的严格下一阶段。</summary>
        /// <param name="definition">武器定义。</param>
        /// <param name="refinementRank">当前精炼阶数。</param>
        /// <returns>严格下一阶配置；不存在时返回空。</returns>
        private static WeaponRefinementStage FindNextRefinementStage(WeaponDefinition definition, int refinementRank)
        {
            for (int index = 0; index < definition.RefinementStages.Count; index++)
                if (definition.RefinementStages[index] != null &&
                    definition.RefinementStages[index].Rank == refinementRank + 1)
                    return definition.RefinementStages[index];
            return null;
        }

        /// <summary>按等级读取成长烘焙结果。</summary>
        /// <param name="profile">成长配置。</param>
        /// <param name="level">目标等级。</param>
        /// <returns>匹配结果；超出范围时返回空。</returns>
        private static BakedWeaponLevelProgression GetProgression(WeaponGrowthProfile profile, int level)
        {
            if (profile == null || profile.BakedProgressions == null || level < 1 ||
                level > profile.BakedProgressions.Count) return null;
            return profile.BakedProgressions[level - 1];
        }

        #endregion
    }
}
