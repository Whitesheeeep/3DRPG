using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.CurrencySystemNS;
using RPG.Game.Runtime.EquipmentDevelopment;
using RPG.Game.UI.Character;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.LogModule;

namespace RPG.Game.Runtime.CharacterDevelopment
{
    /// <summary>协调角色经验升级与突破的静态校验、预览、资源扣除和进度提交。</summary>
    internal sealed class CharacterDevelopmentService
    {
        private const int MaxSelectedExperienceMaterialCount = 99;

        #region 依赖字段

        // 依赖字段：各 Manager 是角色进度、可堆叠道具与货币余额的唯一写入入口。
        private readonly CharacterRosterManager rosterManager;
        private readonly StackableInventoryManager stackableInventoryManager;
        private readonly CurrencyManager currencyManager;
        private readonly CharacterEquipmentSystem equipmentSystem;
        private readonly CharacterAttributeProgressionResolver attributeProgressionResolver;

        #endregion

        #region 生命周期

        /// <summary>创建角色培养服务并绑定角色、材料、货币与装备属性数据源。</summary>
        /// <param name="rosterManagerValue">角色实例管理器。</param>
        /// <param name="stackableInventoryManagerValue">可堆叠库存管理器。</param>
        /// <param name="currencyManagerValue">货币钱包管理器。</param>
        /// <param name="equipmentSystemValue">角色装备关系系统。</param>
        /// <param name="attributeProgressionResolverValue">角色烘焙基础属性解析器。</param>
        public CharacterDevelopmentService(CharacterRosterManager rosterManagerValue,
            StackableInventoryManager stackableInventoryManagerValue, CurrencyManager currencyManagerValue,
            CharacterEquipmentSystem equipmentSystemValue,
            CharacterAttributeProgressionResolver attributeProgressionResolverValue)
        {
            rosterManager = rosterManagerValue ?? throw new ArgumentNullException(nameof(rosterManagerValue));
            stackableInventoryManager = stackableInventoryManagerValue ??
                                        throw new ArgumentNullException(nameof(stackableInventoryManagerValue));
            currencyManager = currencyManagerValue ?? throw new ArgumentNullException(nameof(currencyManagerValue));
            equipmentSystem = equipmentSystemValue ?? throw new ArgumentNullException(nameof(equipmentSystemValue));
            attributeProgressionResolver = attributeProgressionResolverValue ??
                                           throw new ArgumentNullException(nameof(attributeProgressionResolverValue));
        }

        #endregion

        #region 升级预览与提交

        /// <summary>按当前库存、角色等级上限和已选数量生成一次升级预览。</summary>
        /// <param name="characterId">当前角色标识。</param>
        /// <param name="selectedQuantityByItemIdMap">按经验素材 ItemId 聚合的选择数量。</param>
        /// <returns>当前库存快照下的升级预览。</returns>
        public CharacterLevelUpPreview BuildLevelUpPreview(CharacterId characterId,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap)
        {
            if (!rosterManager.TryGetInstance(characterId, out CharacterInstance instance))
                return CreateLevelPreviewFailure(CharacterDevelopmentStatus.InvalidTarget, "当前角色已不在拥有列表中。", null);
            if (!TryResolveLevelCap(instance.Config, instance.AscensionRank, out int currentCap))
                return CreateLevelPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration, "角色等级上限配置无效。", instance);

            if (instance.Level >= currentCap)
                return CreateLevelPreviewFailure(CharacterDevelopmentStatus.ProgressAtCap, "已达到当前等级上限，请先突破。", instance);

            if (!TryBuildMaterialSelections(selectedQuantityByItemIdMap, out List<EnhancementMaterialSelection> selections,
                    out CharacterDevelopmentStatus selectionStatus, out string selectionMessage))
                return CreateLevelPreviewFailure(selectionStatus, selectionMessage, instance);

            try
            {
                BakedCharacterLevelProgression currentProgression = GetProgression(instance.Config, instance.Level);
                BakedCharacterLevelProgression capProgression = GetProgression(instance.Config, currentCap);
                if (currentProgression == null || capProgression == null)
                    return CreateLevelPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                        "角色经验成长表缺少当前等级或等级上限数据。", instance);

                long requiredExperience = Math.Max(0L,
                    (long)capProgression.CumulativeExperience - currentProgression.CumulativeExperience -
                    instance.CurrentExperience);
                EnhancementMaterialConsumptionPlan consumptionPlan =
                    EnhancementMaterialConsumptionPlanner.Build(requiredExperience, selections,
                        MaxSelectedExperienceMaterialCount);
                // 显示规划器实际吸收的经验，超出当前等级上限而保留在背包中的素材不计入预览。
                long selectedExperience = consumptionPlan.ConsumedExperience;

                int projectedLevel = instance.Level;
                int projectedExperience = instance.CurrentExperience;
                long currencyCost = 0L;
                if (consumptionPlan.ConsumedExperience > 0L &&
                    !TryProjectLevelProgress(instance, currentCap, consumptionPlan.ConsumedExperience,
                        out projectedLevel, out projectedExperience, out currencyCost))
                    return CreateLevelPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                        "无法从烘焙成长表计算预计等级。", instance);

                IReadOnlyList<CharacterDevelopmentAttributeValue> attributes = BuildAttributeValues(
                    instance, projectedLevel);
                CharacterDevelopmentStatus status = consumptionPlan.ConsumedQuantityByItemIdMap.Count == 0
                    ? CharacterDevelopmentStatus.InvalidSelection
                    : currencyCost > currencyManager.GetBalance(CurrencyId.Mola)
                        ? CharacterDevelopmentStatus.InsufficientCurrency
                        : CharacterDevelopmentStatus.Succeeded;
                string message = status switch
                {
                    CharacterDevelopmentStatus.InvalidSelection => "选择角色经验素材以预览升级。",
                    CharacterDevelopmentStatus.InsufficientCurrency => "摩拉不足，无法提交当前升级。",
                    _ => string.Empty
                };

                int projectedNextExperience = projectedLevel < currentCap
                    ? GetProgression(instance.Config, projectedLevel)?.NextExperience ?? 0
                    : 0;
                return new CharacterLevelUpPreview(status, message, instance.Level, projectedLevel,
                    instance.CurrentExperience, projectedExperience, projectedNextExperience,
                    selectedExperience, currencyCost, consumptionPlan.ConsumedQuantityByItemIdMap, attributes);
            }
            catch (OverflowException)
            {
                return CreateLevelPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                    "角色经验或摩拉计算超出支持范围。", instance);
            }
        }

        /// <summary>重新校验预览、扣除实际需要的经验素材与摩拉，并提交稳定角色实例进度。</summary>
        /// <param name="characterId">当前角色标识。</param>
        /// <param name="selectedQuantityByItemIdMap">按经验素材 ItemId 聚合的选择数量。</param>
        /// <returns>升级事务结果。</returns>
        public CharacterDevelopmentOperationResult LevelUp(CharacterId characterId,
            IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap)
        {
            CharacterLevelUpPreview preview = BuildLevelUpPreview(characterId, selectedQuantityByItemIdMap);
            if (!preview.CanSubmit)
                return new CharacterDevelopmentOperationResult(preview.Status, preview.Message);
            if (!rosterManager.TryGetInstance(characterId, out CharacterInstance instance))
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.InvalidTarget,
                    "当前角色已不在拥有列表中。");

            List<ItemQuantity> itemCosts = ToItemQuantities(preview.ConsumedQuantityByItemIdMap);
            StackableItemOperationResult itemResult = stackableInventoryManager.ConsumeItems(itemCosts);
            if (!itemResult.Succeeded)
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    $"扣除角色经验素材失败：{itemResult.Status}。");

            bool currencyConsumed = preview.CurrencyCost <= 0L ||
                                    (preview.CurrencyCost <= int.MaxValue &&
                                     currencyManager.ConsumeCurrencies(new[]
                                     {
                                         new CurrencyAmount(CurrencyId.Mola, (int)preview.CurrencyCost)
                                     }).Succeeded);
            if (!currencyConsumed)
            {
                CompensateMaterials(itemCosts, "摩拉扣除失败");
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    "扣除升级摩拉失败，已尝试返还经验素材。");
            }

            CharacterProgressOperationResult progressResult = rosterManager.UpdateCharacterProgress(characterId,
                new CharacterProgressUpdate(preview.ProjectedLevel, preview.ProjectedExperience, instance.AscensionRank));
            if (!progressResult.Succeeded)
            {
                CompensateMaterials(itemCosts, $"角色进度更新失败：{progressResult.Status}");
                CompensateCurrency(preview.CurrencyCost, $"角色进度更新失败：{progressResult.Status}");
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    $"角色进度更新失败：{progressResult.Status}，已尝试返还本次消耗。");
            }

            WSLog.Log($"[CharacterDevelopmentService] 角色升级完成，character={characterId}, " +
                      $"level={instance.Level}->{preview.ProjectedLevel}, consumedTypes={itemCosts.Count}, " +
                      $"mola={preview.CurrencyCost}。");
            return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.Succeeded, "角色升级完成。");
        }

        #endregion

        #region 突破预览与提交

        /// <summary>读取当前角色下一阶段突破门槛、材料拥有量和摩拉余额。</summary>
        /// <param name="characterId">当前角色标识。</param>
        /// <returns>突破条件快照。</returns>
        public CharacterAscensionPreview BuildAscensionPreview(CharacterId characterId)
        {
            if (!rosterManager.TryGetInstance(characterId, out CharacterInstance instance))
                return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidTarget,
                    "当前角色已不在拥有列表中。", null);
            if (!TryGetNextAscensionStage(instance, out CharacterAscensionStage stage, out int currentCap))
                return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.ProgressAtCap,
                    "该角色已经完成全部突破阶段。", instance);

            GrowthCost cost = stage.Cost;
            if (cost == null || cost.ItemCosts.Count == 0)
                return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                    "突破阶段没有配置突破材料。", instance);

            var materials = new List<CharacterAscensionMaterialRequirement>(cost.ItemCosts.Count);
            var requiredQuantityByItemIdMap = new Dictionary<ItemId, int>();
            var requiredItemOrder = new List<ItemId>(cost.ItemCosts.Count);
            long molaCost = 0L;
            for (int index = 0; index < cost.ItemCosts.Count; index++)
            {
                ItemCostEntry entry = cost.ItemCosts[index];
                if (entry == null || !entry.ItemId.IsValid || entry.Quantity <= 0 ||
                    !ItemManager.Instance.TryGetDefinition(entry.ItemId, out ItemDefinition item) ||
                    !(item is DevelopmentItemDefinition developmentItem) ||
                    !developmentItem.SupportsDevelopmentType(DevelopmentItemType.CharacterAscension))
                    return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                        $"角色突破材料配置无效：index={index}。", instance);

                try
                {
                    if (!requiredQuantityByItemIdMap.ContainsKey(entry.ItemId))
                        requiredItemOrder.Add(entry.ItemId);
                    requiredQuantityByItemIdMap[entry.ItemId] = checked(
                        requiredQuantityByItemIdMap.TryGetValue(entry.ItemId, out int currentQuantity)
                            ? currentQuantity + entry.Quantity
                            : entry.Quantity);
                }
                catch (OverflowException)
                {
                    return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                        $"角色突破材料数量溢出：item={entry.ItemId}。", instance);
                }
            }

            try
            {
                for (int index = 0; index < cost.CurrencyCosts.Count; index++)
                {
                    CurrencyCostEntry entry = cost.CurrencyCosts[index];
                    if (entry == null || entry.CurrencyId != CurrencyId.Mola || entry.Amount <= 0)
                        return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                            "角色突破目前只支持有效的摩拉成本配置。", instance);
                    molaCost = checked(molaCost + entry.Amount);
                }
            }
            catch (OverflowException)
            {
                return CreateAscensionPreviewFailure(CharacterDevelopmentStatus.InvalidConfiguration,
                    "角色突破摩拉成本超出支持范围。", instance);
            }

            for (int index = 0; index < requiredItemOrder.Count; index++)
            {
                ItemId itemId = requiredItemOrder[index];
                materials.Add(new CharacterAscensionMaterialRequirement(itemId,
                    requiredQuantityByItemIdMap[itemId], stackableInventoryManager.GetQuantity(itemId)));
            }

            bool atLevelRequirement = instance.Level >= stage.RequiredLevel;
            bool enoughMaterials = true;
            for (int index = 0; index < materials.Count; index++)
                enoughMaterials &= materials[index].OwnedQuantity >= materials[index].RequiredQuantity;

            CharacterDevelopmentStatus status = !atLevelRequirement
                ? CharacterDevelopmentStatus.ProgressAtCap
                : !enoughMaterials
                    ? CharacterDevelopmentStatus.InsufficientMaterials
                    : currencyManager.GetBalance(CurrencyId.Mola) < molaCost
                        ? CharacterDevelopmentStatus.InsufficientCurrency
                        : CharacterDevelopmentStatus.Succeeded;
            string message = status switch
            {
                CharacterDevelopmentStatus.ProgressAtCap => $"角色需要达到 Lv.{stage.RequiredLevel} 才能突破。",
                CharacterDevelopmentStatus.InsufficientMaterials => "突破材料不足。",
                CharacterDevelopmentStatus.InsufficientCurrency => "摩拉不足，无法突破。",
                _ => string.Empty
            };

            var preview = new CharacterAscensionPreview(status, message, instance.AscensionRank,
                instance.AscensionRank + 1, currentCap, stage.MaxLevelAfter,
                currencyManager.GetBalance(CurrencyId.Mola), molaCost, materials, atLevelRequirement);
            return preview;
        }

        /// <summary>复核当前阶段成本并提交角色突破阶数。</summary>
        /// <param name="characterId">当前角色标识。</param>
        /// <returns>突破事务结果。</returns>
        public CharacterDevelopmentOperationResult Ascend(CharacterId characterId)
        {
            CharacterAscensionPreview preview = BuildAscensionPreview(characterId);
            if (!preview.CanSubmit)
                return new CharacterDevelopmentOperationResult(preview.Status, preview.Message);
            if (!rosterManager.TryGetInstance(characterId, out CharacterInstance instance) ||
                !TryGetNextAscensionStage(instance, out _, out _))
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.InvalidTarget,
                    "当前角色或突破阶段已经变化。");

            var itemCosts = new List<ItemQuantity>(preview.Materials.Count);
            for (int index = 0; index < preview.Materials.Count; index++)
                itemCosts.Add(new ItemQuantity(preview.Materials[index].ItemId,
                    preview.Materials[index].RequiredQuantity));
            if (preview.CurrencyCost > int.MaxValue)
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.InvalidConfiguration,
                    "突破摩拉成本超出支持范围。");
            var currencyCosts = new List<CurrencyAmount>
            {
                new(CurrencyId.Mola, (int)preview.CurrencyCost)
            };
            StackableItemOperationResult itemResult = stackableInventoryManager.ConsumeItems(itemCosts);
            if (!itemResult.Succeeded)
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    $"扣除突破材料失败：{itemResult.Status}。");

            CurrencyOperationResult currencyResult = currencyManager.ConsumeCurrencies(currencyCosts);
            if (!currencyResult.Succeeded)
            {
                CompensateMaterials(itemCosts, "突破摩拉扣除失败");
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    "扣除突破摩拉失败，已尝试返还突破材料。");
            }

            CharacterProgressOperationResult progressResult = rosterManager.UpdateCharacterProgress(characterId,
                new CharacterProgressUpdate(instance.Level, instance.CurrentExperience, preview.NextRank));
            if (!progressResult.Succeeded)
            {
                CompensateMaterials(itemCosts, $"突破进度更新失败：{progressResult.Status}");
                CompensateCurrencyCosts(currencyCosts, $"突破进度更新失败：{progressResult.Status}");
                return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.ManagerRejected,
                    $"角色突破失败：{progressResult.Status}，已尝试返还本次消耗。");
            }

            WSLog.Log($"[CharacterDevelopmentService] 角色突破完成，character={characterId}, " +
                      $"rank={preview.CurrentRank}->{preview.NextRank}, cap={preview.CurrentLevelCap}->{preview.NextLevelCap}。");
            return new CharacterDevelopmentOperationResult(CharacterDevelopmentStatus.Succeeded, "角色突破完成。");
        }

        #endregion

        #region 成长与资源计算

        /// <summary>验证当前经验材料种类、选择数量和库存并生成规划器输入。</summary>
        /// <param name="selectedQuantityByItemIdMap">按 ItemId 聚合的用户选择数量。</param>
        /// <param name="selections">验证通过的经验材料选择。</param>
        /// <param name="status">校验失败状态。</param>
        /// <param name="message">校验失败说明。</param>
        /// <returns>所有选择有效时返回 true。</returns>
        private bool TryBuildMaterialSelections(IReadOnlyDictionary<ItemId, int> selectedQuantityByItemIdMap,
            out List<EnhancementMaterialSelection> selections, out CharacterDevelopmentStatus status,
            out string message)
        {
            selections = new List<EnhancementMaterialSelection>();
            status = CharacterDevelopmentStatus.Succeeded;
            message = string.Empty;
            if (selectedQuantityByItemIdMap == null || selectedQuantityByItemIdMap.Count == 0)
            {
                status = CharacterDevelopmentStatus.InvalidSelection;
                message = "选择角色经验素材以预览升级。";
                return true;
            }

            IReadOnlyList<StackableInventoryEntry> inventoryEntries =
                stackableInventoryManager.GetDevelopmentExperienceItems(DevelopmentExperienceItemType.Character);
            var availableQuantityByItemIdMap = new Dictionary<ItemId, int>(inventoryEntries.Count);
            for (int index = 0; index < inventoryEntries.Count; index++)
                availableQuantityByItemIdMap.Add(inventoryEntries[index].ItemId, inventoryEntries[index].Quantity);

            int selectedCount = 0;
            foreach (KeyValuePair<ItemId, int> pair in selectedQuantityByItemIdMap)
            {
                if (pair.Value <= 0 || !availableQuantityByItemIdMap.TryGetValue(pair.Key, out int availableQuantity) ||
                    availableQuantity < pair.Value ||
                    !ItemManager.Instance.TryGetDefinition(pair.Key, out ItemDefinition item) ||
                    !(item is DevelopmentExperienceItemDefinition experience) ||
                    !experience.SupportsExperienceType(DevelopmentExperienceItemType.Character))
                {
                    status = CharacterDevelopmentStatus.InvalidSelection;
                    message = $"角色经验素材选择已失效：{pair.Key}。";
                    return false;
                }

                if (pair.Value > MaxSelectedExperienceMaterialCount - selectedCount)
                {
                    status = CharacterDevelopmentStatus.InvalidSelection;
                    message = $"单次最多选择 {MaxSelectedExperienceMaterialCount} 个角色经验素材。";
                    return false;
                }

                selectedCount += pair.Value;
                selections.Add(new EnhancementMaterialSelection(pair.Key, pair.Value, experience.ExperienceValue));
            }

            return true;
        }

        /// <summary>按烘焙累计经验和当前阶段上限计算预计等级、等级内经验及摩拉。</summary>
        /// <param name="instance">稳定角色实例。</param>
        /// <param name="levelCap">当前突破阶段等级上限。</param>
        /// <param name="selectedExperience">本次实际吸收的经验。</param>
        /// <param name="projectedLevel">预计等级。</param>
        /// <param name="projectedExperience">预计等级内经验。</param>
        /// <param name="currencyCost">跨越等级对应的摩拉成本。</param>
        /// <returns>经验表完整且计算成功时返回 true。</returns>
        private static bool TryProjectLevelProgress(CharacterInstance instance, int levelCap,
            long selectedExperience, out int projectedLevel, out int projectedExperience, out long currencyCost)
        {
            projectedLevel = instance.Level;
            projectedExperience = instance.CurrentExperience;
            currencyCost = 0L;
            BakedCharacterLevelProgression current = GetProgression(instance.Config, instance.Level);
            BakedCharacterLevelProgression cap = GetProgression(instance.Config, levelCap);
            if (current == null || cap == null) return false;

            long absoluteExperience = Math.Min((long)cap.CumulativeExperience,
                (long)current.CumulativeExperience + instance.CurrentExperience + Math.Max(0L, selectedExperience));
            while (projectedLevel < levelCap)
            {
                BakedCharacterLevelProgression progression = GetProgression(instance.Config, projectedLevel);
                if (progression == null || progression.NextExperience <= 0 ||
                    absoluteExperience < (long)progression.CumulativeExperience + progression.NextExperience)
                    break;
                projectedLevel++;
            }

            BakedCharacterLevelProgression projected = GetProgression(instance.Config, projectedLevel);
            if (projected == null) return false;
            projectedExperience = projectedLevel >= levelCap ? 0 : Math.Max(0, (int)Math.Min(int.MaxValue,
                absoluteExperience - projected.CumulativeExperience));
            for (int level = instance.Level; level < projectedLevel; level++)
            {
                BakedCharacterLevelProgression progression = GetProgression(instance.Config, level);
                if (progression == null) return false;
                currencyCost = checked(currencyCost + progression.CurrencyCost);
            }

            return true;
        }

        /// <summary>在当前等级与预计等级之间应用同一套静态装备效果，生成可对比属性。</summary>
        /// <param name="instance">当前角色实例。</param>
        /// <param name="projectedLevel">预计等级。</param>
        /// <returns>角色初始 Stat 配置顺序中的属性对比值。</returns>
        private IReadOnlyList<CharacterDevelopmentAttributeValue> BuildAttributeValues(CharacterInstance instance,
            int projectedLevel)
        {
            IReadOnlyList<CharacterAttributeProjectionValue> currentValues =
                CharacterEquipmentAttributeProjection.ResolveStatValues(instance, instance.Level, equipmentSystem,
                    attributeProgressionResolver);
            IReadOnlyList<CharacterAttributeProjectionValue> projectedValues =
                CharacterEquipmentAttributeProjection.ResolveStatValues(instance, projectedLevel, equipmentSystem,
                    attributeProgressionResolver);
            var projectedValueByAttributeIdMap = new Dictionary<int, float>(projectedValues.Count);
            for (int index = 0; index < projectedValues.Count; index++)
                projectedValueByAttributeIdMap.Add(projectedValues[index].Attribute.Id, projectedValues[index].TotalValue);

            var result = new List<CharacterDevelopmentAttributeValue>(currentValues.Count);
            for (int index = 0; index < currentValues.Count; index++)
            {
                CharacterAttributeProjectionValue current = currentValues[index];
                if (projectedValueByAttributeIdMap.TryGetValue(current.Attribute.Id, out float projectedValue))
                    result.Add(new CharacterDevelopmentAttributeValue(current.Attribute, current.TotalValue, projectedValue));
            }

            return result;
        }

        /// <summary>获取指定等级的烘焙经验数据。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="level">目标等级。</param>
        /// <returns>成长表中匹配的条目。</returns>
        private static BakedCharacterLevelProgression GetProgression(CharacterConfig config, int level)
        {
            IReadOnlyList<BakedCharacterLevelProgression> progressions = config?.GrowthProfile?.BakedLevelProgressions;
            return progressions != null && level >= 1 && level <= progressions.Count
                ? progressions[level - 1]
                : null;
        }

        /// <summary>按突破阶数解析当前角色等级上限。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="ascensionRank">角色当前突破阶数。</param>
        /// <param name="levelCap">解析出的等级上限。</param>
        /// <returns>配置阶段完整有效时返回 true。</returns>
        private static bool TryResolveLevelCap(CharacterConfig config, int ascensionRank, out int levelCap)
        {
            levelCap = 0;
            if (config == null || ascensionRank < 0 || ascensionRank > config.AscensionStages.Count) return false;
            if (config.AscensionStages.Count == 0)
            {
                levelCap = config.MaxLevel;
                return levelCap > 0;
            }

            CharacterAscensionStage stage = ascensionRank == 0
                ? config.AscensionStages[0]
                : config.AscensionStages[ascensionRank - 1];
            if (stage == null) return false;
            levelCap = ascensionRank == 0 ? stage.RequiredLevel : stage.MaxLevelAfter;
            return levelCap >= 1 && levelCap <= config.MaxLevel;
        }

        /// <summary>解析角色实例当前对应的下一突破阶段及当前等级上限。</summary>
        /// <param name="instance">角色实例。</param>
        /// <param name="stage">下一突破阶段。</param>
        /// <param name="currentLevelCap">当前等级上限。</param>
        /// <returns>下一阶段配置完整时返回 true。</returns>
        private static bool TryGetNextAscensionStage(CharacterInstance instance,
            out CharacterAscensionStage stage, out int currentLevelCap)
        {
            stage = null;
            currentLevelCap = 0;
            if (instance == null || instance.Config.AscensionStages == null ||
                instance.AscensionRank < 0 || instance.AscensionRank >= instance.Config.AscensionStages.Count ||
                !TryResolveLevelCap(instance.Config, instance.AscensionRank, out currentLevelCap)) return false;
            stage = instance.Config.AscensionStages[instance.AscensionRank];
            return stage != null && stage.RequiredLevel == currentLevelCap &&
                   stage.MaxLevelAfter > currentLevelCap && stage.MaxLevelAfter <= instance.Config.MaxLevel;
        }

        /// <summary>把经验规划器的 ItemId 映射转换成库存批次消耗列表。</summary>
        /// <param name="quantityByItemIdMap">按 ItemId 聚合的材料数。</param>
        /// <returns>库存管理器使用的批次成本。</returns>
        private static List<ItemQuantity> ToItemQuantities(IReadOnlyDictionary<ItemId, int> quantityByItemIdMap)
        {
            var result = new List<ItemQuantity>(quantityByItemIdMap.Count);
            foreach (KeyValuePair<ItemId, int> pair in quantityByItemIdMap)
                result.Add(new ItemQuantity(pair.Key, pair.Value));
            return result;
        }

        /// <summary>构造无有效升级计划时的稳定预览数据。</summary>
        /// <param name="status">失败或不可操作状态。</param>
        /// <param name="message">具体原因。</param>
        /// <param name="instance">当前角色实例；未找到时为 null。</param>
        /// <returns>可供 UI 显示的空升级计划。</returns>
        private CharacterLevelUpPreview CreateLevelPreviewFailure(CharacterDevelopmentStatus status,
            string message, CharacterInstance instance)
        {
            IReadOnlyList<CharacterDevelopmentAttributeValue> attributes = instance == null
                ? Array.Empty<CharacterDevelopmentAttributeValue>()
                : BuildAttributeValues(instance, instance.Level);
            int cap = instance != null && TryResolveLevelCap(instance.Config, instance.AscensionRank, out int value)
                ? value
                : instance?.Level ?? 0;
            int nextExperience = instance != null && instance.Level < cap
                ? GetProgression(instance.Config, instance.Level)?.NextExperience ?? 0
                : 0;
            return new CharacterLevelUpPreview(status, message, instance?.Level ?? 0,
                instance?.Level ?? 0, instance?.CurrentExperience ?? 0,
                instance?.CurrentExperience ?? 0, nextExperience, 0L, 0L,
                new Dictionary<ItemId, int>(), attributes);
        }

        /// <summary>构造突破不可用时展示当前进度和空材料列表。</summary>
        /// <param name="status">失败或终态。</param>
        /// <param name="message">不可用原因。</param>
        /// <param name="instance">当前角色实例；未找到时为 null。</param>
        /// <returns>可供 UI 显示的突破预览。</returns>
        private CharacterAscensionPreview CreateAscensionPreviewFailure(CharacterDevelopmentStatus status,
            string message, CharacterInstance instance)
        {
            int levelCap = instance != null && TryResolveLevelCap(instance.Config, instance.AscensionRank, out int cap)
                ? cap
                : 0;
            return new CharacterAscensionPreview(status, message, instance?.AscensionRank ?? 0,
                instance?.AscensionRank ?? 0, levelCap, levelCap,
                currencyManager.GetBalance(CurrencyId.Mola), 0L,
                Array.Empty<CharacterAscensionMaterialRequirement>(), false);
        }

        /// <summary>尝试按反向资源接口返还升级或突破扣除的物品，并报告补偿结果。</summary>
        /// <param name="itemCosts">需要返还的物品数量。</param>
        /// <param name="reason">触发补偿的失败原因。</param>
        private void CompensateMaterials(IReadOnlyList<ItemQuantity> itemCosts, string reason)
        {
            StackableItemOperationResult result = stackableInventoryManager.AddItems(itemCosts);
            if (!result.Succeeded)
                WSLog.LogError($"[CharacterDevelopmentService] 材料补偿失败，reason={reason}, status={result.Status}, item={result.ItemId}。");
        }

        /// <summary>尝试返还升级扣除的摩拉并报告补偿结果。</summary>
        /// <param name="amount">需要返还的摩拉。</param>
        /// <param name="reason">触发补偿的失败原因。</param>
        private void CompensateCurrency(long amount, string reason)
        {
            if (amount <= 0L) return;
            if (amount > int.MaxValue ||
                !currencyManager.AddCurrencies(new[] { new CurrencyAmount(CurrencyId.Mola, (int)amount) }).Succeeded)
                WSLog.LogError($"[CharacterDevelopmentService] 摩拉补偿失败，reason={reason}, amount={amount}。");
        }

        /// <summary>尝试返还突破扣除的摩拉并报告补偿结果。</summary>
        /// <param name="currencyCosts">需要返还的货币金额。</param>
        /// <param name="reason">触发补偿的失败原因。</param>
        private void CompensateCurrencyCosts(IReadOnlyList<CurrencyAmount> currencyCosts, string reason)
        {
            CurrencyOperationResult result = currencyManager.AddCurrencies(currencyCosts);
            if (!result.Succeeded)
                WSLog.LogError($"[CharacterDevelopmentService] 突破货币补偿失败，reason={reason}, status={result.Status}, currency={result.CurrencyId}。");
        }

        #endregion
    }
}
