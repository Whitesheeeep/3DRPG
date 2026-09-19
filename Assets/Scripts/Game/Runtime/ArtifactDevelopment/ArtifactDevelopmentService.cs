using System;
using System.Collections.Generic;
using RPG.CurrencySystem;
using RPG.Game.Runtime.EquipmentDevelopment;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.Game.Runtime.ArtifactDevelopment
{
    /// <summary>在圣遗物培养窗口会话期间负责预览和提交一次圣遗物升级。</summary>
    public sealed class ArtifactDevelopmentService
    {
        private const int MaxSelectedExperienceMaterialCount = 99;

        #region 依赖字段

        private readonly ArtifactInventoryManager artifactInventoryManager;
        private readonly StackableInventoryManager stackableInventoryManager;
        private readonly CurrencyManager currencyManager;

        #endregion

        /// <summary>创建圣遗物培养服务。</summary>
        /// <param name="artifactInventoryManager">圣遗物实例库存。</param>
        /// <param name="stackableInventoryManager">经验素材库存。</param>
        /// <param name="currencyManager">货币钱包。</param>
        public ArtifactDevelopmentService(ArtifactInventoryManager artifactInventoryManager,
            StackableInventoryManager stackableInventoryManager, CurrencyManager currencyManager)
        {
            this.artifactInventoryManager = artifactInventoryManager ??
                                            throw new ArgumentNullException(nameof(artifactInventoryManager));
            this.stackableInventoryManager = stackableInventoryManager ??
                                             throw new ArgumentNullException(nameof(stackableInventoryManager));
            this.currencyManager = currencyManager ?? throw new ArgumentNullException(nameof(currencyManager));
        }

        #region 预览与提交

        /// <summary>根据当前选择构建圣遗物等级、经验和费用投影。</summary>
        /// <param name="instanceId">目标圣遗物实例。</param>
        /// <param name="selectedQuantityByItemIdMap">按经验素材 ItemId 聚合的选择数量。</param>
        /// <param name="projection">成功时返回投影。</param>
        /// <returns>配置和目标有效时返回 true。</returns>
        public bool TryProject(EquipmentInstanceId instanceId,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap,
            out ArtifactDevelopmentProjection projection)
        {
            projection = default;
            if (!TryGetArtifact(instanceId, out ArtifactInstance instance, out ArtifactDefinition definition)) return false;
            if (!TryGetProgression(definition, instance.Level, out BakedArtifactLevelProgression current) ||
                !TryGetProgression(definition, definition.MaxLevel, out BakedArtifactLevelProgression cap)) return false;

            if (!TryBuildConsumptionPlan(instance, definition, selectedQuantityByItemIdMap,
                    out EnhancementMaterialConsumptionPlan consumptionPlan)) return false;

            long totalExperience = Math.Min((long)cap.CumulativeExperience,
                (long)current.CumulativeExperience + instance.CurrentExperience +
                Math.Max(0L, consumptionPlan.ConsumedExperience));
            int projectedLevel = instance.Level;
            while (projectedLevel < definition.MaxLevel &&
                   TryGetProgression(definition, projectedLevel, out BakedArtifactLevelProgression progression) &&
                   progression.NextExperience > 0 &&
                   totalExperience >= (long)progression.CumulativeExperience + progression.NextExperience)
            {
                projectedLevel++;
            }

            if (!TryGetProgression(definition, projectedLevel, out BakedArtifactLevelProgression projected)) return false;
            int projectedExperience = (int)Math.Max(0L, Math.Min(int.MaxValue,
                totalExperience - projected.CumulativeExperience));
            long projectedCost = 0L;
            for (int level = instance.Level; level < projectedLevel; level++)
            {
                if (!TryGetProgression(definition, level, out BakedArtifactLevelProgression levelProgression)) return false;
                projectedCost = checked(projectedCost + levelProgression.CurrencyCost);
            }

            float progress = projected.NextExperience <= 0
                ? (projectedLevel >= definition.MaxLevel ? 1f : 0f)
                : Mathf.Clamp01(projectedExperience / (float)projected.NextExperience);
            projection = new ArtifactDevelopmentProjection(projectedLevel, projectedExperience,
                projected.NextExperience, progress, projectedCost);
            return true;
        }

        /// <summary>校验并提交当前圣遗物的一次真实升级。</summary>
        /// <param name="instanceId">目标圣遗物实例。</param>
        /// <param name="selectedQuantityByItemIdMap">按经验素材 ItemId 聚合的选择数量。</param>
        /// <returns>升级结果。</returns>
        public ArtifactDevelopmentOperationResult Enhance(EquipmentInstanceId instanceId,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap)
        {
            if (!TryGetArtifact(instanceId, out ArtifactInstance instance, out ArtifactDefinition definition))
                return Failure(ArtifactDevelopmentOperationStatus.InvalidTarget, "圣遗物目标不存在或 Definition 类型不匹配。");
            if (selectedQuantityByItemIdMap == null || selectedQuantityByItemIdMap.Count == 0)
                return Failure(ArtifactDevelopmentOperationStatus.InvalidSelection, "没有选择圣遗物经验素材。");
            if (!TryProject(instanceId, selectedQuantityByItemIdMap, out ArtifactDevelopmentProjection projection))
                return Failure(ArtifactDevelopmentOperationStatus.InvalidConfiguration, "圣遗物成长配置或素材选择无效。");
            if (projection.Level == instance.Level && projection.CurrentExperience == instance.CurrentExperience)
                return Failure(ArtifactDevelopmentOperationStatus.InvalidSelection, "选择的经验没有产生等级变化。");

            if (!TryBuildConsumptionPlan(instance, definition, selectedQuantityByItemIdMap,
                    out EnhancementMaterialConsumptionPlan consumptionPlan))
                return Failure(ArtifactDevelopmentOperationStatus.InvalidSelection, "经验素材选择无效。");

            var materialCosts = new List<ItemQuantity>(consumptionPlan.ConsumedQuantityByItemIdMap.Count);
            foreach (KeyValuePair<ItemId, int> pair in consumptionPlan.ConsumedQuantityByItemIdMap)
                materialCosts.Add(new ItemQuantity(pair.Key, pair.Value));

            if (projection.CurrencyCost > int.MaxValue)
                return Failure(ArtifactDevelopmentOperationStatus.InvalidConfiguration, "圣遗物升级货币消耗超出货币 API 范围。");
            int currencyCost = (int)projection.CurrencyCost;
            if (currencyCost > currencyManager.GetBalance(CurrencyId.Mola))
                return Failure(ArtifactDevelopmentOperationStatus.InsufficientCurrency, "摩拉不足。");

            // 先完成所有可验证条件，再按素材、货币、实例进度的顺序提交，避免中途暴露半完成状态。
            StackableItemOperationResult materialResult = stackableInventoryManager.ConsumeItems(materialCosts);
            if (!materialResult.Succeeded)
                return Failure(ArtifactDevelopmentOperationStatus.InsufficientMaterials, $"经验素材扣除失败：{materialResult.Status}。");
            if (currencyCost > 0 && !currencyManager.ConsumeCurrencies(new[] { new CurrencyAmount(CurrencyId.Mola, currencyCost) }).Succeeded)
                return Failure(ArtifactDevelopmentOperationStatus.ManagerRejected, "摩拉扣除失败。");

            EquipmentOperationResult updateResult = artifactInventoryManager.UpdateArtifactProgress(instanceId,
                new ArtifactProgressUpdate(projection.Level, projection.CurrentExperience));
            if (!updateResult.Succeeded)
                return Failure(ArtifactDevelopmentOperationStatus.ManagerRejected, $"圣遗物进度更新失败：{updateResult.Status}。");

            Debug.Log(
                $"[ArtifactDevelopmentService] 圣遗物升级成功：Instance={instanceId}，Level={projection.Level}，" +
                $"Experience={projection.CurrentExperience}，Consumed={consumptionPlan.ConsumedQuantityByItemIdMap.Count}种，" +
                $"Retained={consumptionPlan.RetainedQuantityByItemIdMap.Count}种，" +
                $"Overflow={consumptionPlan.OverflowExperience}。 ");
            return new ArtifactDevelopmentOperationResult(ArtifactDevelopmentOperationStatus.Succeeded, string.Empty);
        }

        #endregion

        #region 校验辅助

        /// <summary>校验玩家选择并构建圣遗物实际消耗的有界背包规划。</summary>
        /// <param name="instance">目标圣遗物实例。</param>
        /// <param name="definition">目标圣遗物定义。</param>
        /// <param name="selectedQuantityByItemIdMap">玩家选择数量。</param>
        /// <param name="consumptionPlan">实际消耗规划。</param>
        /// <returns>选择有效且规划成功时返回 true。</returns>
        private bool TryBuildConsumptionPlan(ArtifactInstance instance, ArtifactDefinition definition,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap,
            out EnhancementMaterialConsumptionPlan consumptionPlan)
        {
            consumptionPlan = null;
            if (selectedQuantityByItemIdMap == null) return false;

            var selections = new List<EnhancementMaterialSelection>(selectedQuantityByItemIdMap.Count);
            long selectedMaterialCount = 0L;
            foreach (KeyValuePair<ItemId, int> pair in selectedQuantityByItemIdMap)
            {
                if (!TryValidateMaterial(pair.Key, pair.Value, out DevelopmentExperienceItemDefinition material))
                    return false;
                if (pair.Value > MaxSelectedExperienceMaterialCount ||
                    selectedMaterialCount > MaxSelectedExperienceMaterialCount - pair.Value)
                    return false;
                selectedMaterialCount += pair.Value;
                if (stackableInventoryManager.GetQuantity(pair.Key) < pair.Value)
                    return false;
                selections.Add(new EnhancementMaterialSelection(pair.Key, pair.Value, material.ExperienceValue));
            }

            if (!TryGetProgression(definition, instance.Level, out BakedArtifactLevelProgression current) ||
                !TryGetProgression(definition, definition.MaxLevel, out BakedArtifactLevelProgression cap)) return false;
            long requiredExperience = Math.Max(0L,
                (long)cap.CumulativeExperience - current.CumulativeExperience - instance.CurrentExperience);
            consumptionPlan = EnhancementMaterialConsumptionPlanner.Build(
                requiredExperience, selections, MaxSelectedExperienceMaterialCount);
            return true;
        }

        /// <summary>查询圣遗物目标及其 Definition。</summary>
        /// <param name="instanceId">目标实例。</param>
        /// <param name="instance">圣遗物实例。</param>
        /// <param name="definition">圣遗物 Definition。</param>
        /// <returns>目标有效时返回 true。</returns>
        private bool TryGetArtifact(EquipmentInstanceId instanceId, out ArtifactInstance instance,
            out ArtifactDefinition definition)
        {
            instance = null;
            definition = null;
            if (!artifactInventoryManager.TryGetInstance(instanceId, out instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition item)) return false;
            definition = item as ArtifactDefinition;
            return definition != null;
        }

        /// <summary>检查圣遗物经验素材类型、数量和库存。</summary>
        /// <param name="itemId">素材标识。</param>
        /// <param name="quantity">选择数量。</param>
        /// <param name="definition">素材 Definition。</param>
        /// <returns>素材有效且库存足够时返回 true。</returns>
        private bool TryValidateMaterial(ItemId itemId, int quantity, out DevelopmentExperienceItemDefinition definition)
        {
            definition = null;
            if (quantity <= 0 || !itemId.IsValid || !ItemManager.Instance.TryGetDefinition(itemId, out ItemDefinition item) ||
                !(item is DevelopmentExperienceItemDefinition experience) ||
                !experience.SupportsExperienceType(DevelopmentExperienceItemType.Artifact) ||
                stackableInventoryManager.GetQuantity(itemId) < quantity) return false;
            definition = experience;
            return true;
        }

        /// <summary>按圣遗物等级读取烘焙进度。</summary>
        /// <param name="definition">圣遗物 Definition。</param>
        /// <param name="level">等级。</param>
        /// <param name="progression">烘焙条目。</param>
        /// <returns>找到时返回 true。</returns>
        private static bool TryGetProgression(ArtifactDefinition definition, int level,
            out BakedArtifactLevelProgression progression)
        {
            progression = null;
            if (definition == null || definition.GrowthProfile == null ||
                definition.GrowthProfile.BakedProgressions == null || level < 0 ||
                level >= definition.GrowthProfile.BakedProgressions.Count) return false;
            progression = definition.GrowthProfile.BakedProgressions[level];
            return progression != null && progression.Level == level;
        }

        /// <summary>创建失败结果并保留统一诊断文本。</summary>
        /// <param name="status">失败状态。</param>
        /// <param name="message">失败原因。</param>
        /// <returns>失败结果。</returns>
        private static ArtifactDevelopmentOperationResult Failure(ArtifactDevelopmentOperationStatus status, string message) =>
            new ArtifactDevelopmentOperationResult(status, message);

        #endregion
    }
}
