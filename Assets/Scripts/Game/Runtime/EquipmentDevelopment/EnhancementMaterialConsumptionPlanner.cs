using System;
using System.Collections.Generic;
using RPG.ItemSystem;

namespace RPG.Game.Runtime.EquipmentDevelopment
{
    /// <summary>一次强化选择中的经验素材数量和单本经验值。</summary>
    internal readonly struct EnhancementMaterialSelection
    {
        /// <summary>创建经验素材选择。</summary>
        /// <param name="itemId">经验素材标识。</param>
        /// <param name="selectedQuantity">玩家选择的数量。</param>
        /// <param name="experienceValue">每本素材提供的经验。</param>
        public EnhancementMaterialSelection(ItemId itemId, int selectedQuantity, int experienceValue)
        {
            if (!itemId.IsValid) throw new ArgumentException("经验素材 ItemId 无效。", nameof(itemId));
            if (selectedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(selectedQuantity));
            if (experienceValue <= 0) throw new ArgumentOutOfRangeException(nameof(experienceValue));
            ItemId = itemId;
            SelectedQuantity = selectedQuantity;
            ExperienceValue = experienceValue;
        }

        /// <summary>经验素材标识。</summary>
        public ItemId ItemId { get; }
        /// <summary>玩家选择的数量。</summary>
        public int SelectedQuantity { get; }
        /// <summary>每本经验素材提供的经验。</summary>
        public int ExperienceValue { get; }
    }

    /// <summary>一次强化中实际消耗与保留的经验素材规划结果。</summary>
    internal sealed class EnhancementMaterialConsumptionPlan
    {
        /// <summary>创建规划结果。</summary>
        /// <param name="consumedQuantityByItemIdMap">实际消耗数量。</param>
        /// <param name="retainedQuantityByItemIdMap">保留在库存中的选择数量。</param>
        /// <param name="consumedExperience">实际吸收的经验。</param>
        /// <param name="overflowExperience">不可避免的溢出经验。</param>
        /// <param name="reachesTarget">是否达到目标经验。</param>
        public EnhancementMaterialConsumptionPlan(
            IReadOnlyDictionary<ItemId, int> consumedQuantityByItemIdMap,
            IReadOnlyDictionary<ItemId, int> retainedQuantityByItemIdMap,
            long consumedExperience,
            long overflowExperience,
            bool reachesTarget)
        {
            ConsumedQuantityByItemIdMap = consumedQuantityByItemIdMap ??
                                          throw new ArgumentNullException(nameof(consumedQuantityByItemIdMap));
            RetainedQuantityByItemIdMap = retainedQuantityByItemIdMap ??
                                          throw new ArgumentNullException(nameof(retainedQuantityByItemIdMap));
            ConsumedExperience = consumedExperience;
            OverflowExperience = overflowExperience;
            ReachesTarget = reachesTarget;
        }

        /// <summary>实际要从库存扣除的数量。</summary>
        public IReadOnlyDictionary<ItemId, int> ConsumedQuantityByItemIdMap { get; }
        /// <summary>玩家选择但本次不扣除的数量。</summary>
        public IReadOnlyDictionary<ItemId, int> RetainedQuantityByItemIdMap { get; }
        /// <summary>实际吸收的经验总量。</summary>
        public long ConsumedExperience { get; }
        /// <summary>因经验面额无法整除而产生的不可避免溢出经验。</summary>
        public long OverflowExperience { get; }
        /// <summary>实际消耗组合是否达到目标经验。</summary>
        public bool ReachesTarget { get; }
    }

    /// <summary>
    /// 使用有界背包求解一次强化的实际素材消耗。
    /// 该算法只决定哪些整本素材被消耗，不会创建或回写多余素材。
    /// </summary>
    internal static class EnhancementMaterialConsumptionPlanner
    {
        /// <summary>构建满足目标经验且溢出最少的实际消耗组合。</summary>
        /// <param name="requiredExperience">达到当前阶段或最大等级还需要的经验。</param>
        /// <param name="selections">玩家选择的经验素材。</param>
        /// <param name="maximumMaterialCount">本次规划最多允许消耗的素材本数。</param>
        /// <returns>实际消耗、保留和溢出经验的规划结果。</returns>
        public static EnhancementMaterialConsumptionPlan Build(
            long requiredExperience,
            IReadOnlyList<EnhancementMaterialSelection> selections,
            int maximumMaterialCount = int.MaxValue)
        {
            if (selections == null) throw new ArgumentNullException(nameof(selections));
            if (maximumMaterialCount < 0) throw new ArgumentOutOfRangeException(nameof(maximumMaterialCount));

            var retainedQuantityByItemIdMap = new Dictionary<ItemId, int>();
            var validSelections = new List<EnhancementMaterialSelection>(selections.Count);
            for (int index = 0; index < selections.Count; index++)
            {
                EnhancementMaterialSelection selection = selections[index];
                if (selection.SelectedQuantity <= 0) continue;
                validSelections.Add(selection);
                retainedQuantityByItemIdMap[selection.ItemId] = selection.SelectedQuantity;
            }

            if (requiredExperience <= 0L || validSelections.Count == 0)
                return new EnhancementMaterialConsumptionPlan(
                    new Dictionary<ItemId, int>(), retainedQuantityByItemIdMap, 0L, 0L, requiredExperience <= 0L);

            validSelections.Sort((left, right) => left.ItemId.CompareTo(right.ItemId));
            long maxSingleExperience = 0L;
            for (int index = 0; index < validSelections.Count; index++)
                maxSingleExperience = Math.Max(maxSingleExperience, validSelections[index].ExperienceValue);

            long maximumSearchExperience = checked(requiredExperience + maxSingleExperience - 1L);
            var bestStateByExperienceMap = new Dictionary<long, PlannerState>
            {
                [0L] = new PlannerState(validSelections.Count)
            };

            // 每种素材只从上一轮状态扩展一次，保证同一种素材不会被重复使用超过选择数量。
            for (int materialIndex = 0; materialIndex < validSelections.Count; materialIndex++)
            {
                EnhancementMaterialSelection selection = validSelections[materialIndex];
                var previousStates = new List<KeyValuePair<long, PlannerState>>(bestStateByExperienceMap);
                // 规划器只允许生成不超过本次上限的组合；即使外部调用传入更大的选择数量，也不能让搜索规模随输入无限增长。
                int quantityLimit = Math.Min(selection.SelectedQuantity, maximumMaterialCount);
                for (int quantity = 1; quantity <= quantityLimit; quantity++)
                {
                    long addedExperience = checked((long)quantity * selection.ExperienceValue);
                    for (int stateIndex = 0; stateIndex < previousStates.Count; stateIndex++)
                    {
                        long nextExperience = checked(previousStates[stateIndex].Key + addedExperience);
                        if (nextExperience > maximumSearchExperience) continue;

                        if (previousStates[stateIndex].Value.ConsumedCount + quantity > maximumMaterialCount)
                            continue;

                        PlannerState candidate = previousStates[stateIndex].Value.CloneWith(materialIndex, quantity);
                        if (!bestStateByExperienceMap.TryGetValue(nextExperience, out PlannerState existing) ||
                            IsPreferredState(candidate, existing))
                            bestStateByExperienceMap[nextExperience] = candidate;
                    }
                }
            }

            bool foundReachableTarget = false;
            long bestExperience = 0L;
            int bestCount = int.MaxValue;
            foreach (KeyValuePair<long, PlannerState> pair in bestStateByExperienceMap)
            {
                if (pair.Key < requiredExperience) continue;
                long overflow = pair.Key - requiredExperience;
                if (!foundReachableTarget || overflow < bestExperience - requiredExperience ||
                    (overflow == bestExperience - requiredExperience &&
                     (pair.Value.ConsumedCount < bestCount ||
                      (pair.Value.ConsumedCount == bestCount &&
                       IsPreferredState(pair.Value, bestStateByExperienceMap[bestExperience])))))
                {
                    foundReachableTarget = true;
                    bestExperience = pair.Key;
                    bestCount = pair.Value.ConsumedCount;
                }
            }

            if (!foundReachableTarget)
            {
                foreach (KeyValuePair<long, PlannerState> pair in bestStateByExperienceMap)
                {
                    if (pair.Key > bestExperience ||
                        (pair.Key == bestExperience &&
                         (pair.Value.ConsumedCount < bestCount ||
                          (pair.Value.ConsumedCount == bestCount &&
                           IsPreferredState(pair.Value, bestStateByExperienceMap[bestExperience])))))
                    {
                        bestExperience = pair.Key;
                        bestCount = pair.Value.ConsumedCount;
                    }
                }
            }

            PlannerState bestState = bestStateByExperienceMap[bestExperience];
            var consumedQuantityByItemIdMap = new Dictionary<ItemId, int>();
            for (int index = 0; index < validSelections.Count; index++)
            {
                int consumedQuantity = bestState.QuantityByMaterialIndex[index];
                if (consumedQuantity <= 0) continue;
                ItemId itemId = validSelections[index].ItemId;
                consumedQuantityByItemIdMap[itemId] = consumedQuantity;
                retainedQuantityByItemIdMap[itemId] -= consumedQuantity;
                if (retainedQuantityByItemIdMap[itemId] <= 0)
                    retainedQuantityByItemIdMap.Remove(itemId);
            }

            return new EnhancementMaterialConsumptionPlan(
                consumedQuantityByItemIdMap,
                retainedQuantityByItemIdMap,
                bestExperience,
                foundReachableTarget ? bestExperience - requiredExperience : 0L,
                foundReachableTarget);
        }

        /// <summary>按素材数量和排序后的 ItemId 顺序比较两个同经验状态。</summary>
        /// <param name="candidate">待比较的候选状态。</param>
        /// <param name="existing">当前保留的状态。</param>
        /// <returns>候选状态应替换当前状态时返回 true。</returns>
        private static bool IsPreferredState(PlannerState candidate, PlannerState existing)
        {
            if (candidate.ConsumedCount != existing.ConsumedCount)
                return candidate.ConsumedCount < existing.ConsumedCount;

            // 经验和本数都相同时优先使用排序更靠前的 ItemId，避免 Dictionary 枚举顺序影响结果。
            for (int index = 0; index < candidate.QuantityByMaterialIndex.Length; index++)
            {
                if (candidate.QuantityByMaterialIndex[index] == existing.QuantityByMaterialIndex[index]) continue;
                return candidate.QuantityByMaterialIndex[index] > existing.QuantityByMaterialIndex[index];
            }

            return false;
        }

        /// <summary>表示某个经验总量下的最少素材数量状态。</summary>
        private sealed class PlannerState
        {
            /// <summary>创建空状态。</summary>
            /// <param name="materialCount">素材种类数量。</param>
            public PlannerState(int materialCount)
            {
                QuantityByMaterialIndex = new int[materialCount];
            }

            /// <summary>每种素材的消耗数量。</summary>
            public int[] QuantityByMaterialIndex { get; }
            /// <summary>该状态实际消耗的总本数。</summary>
            public int ConsumedCount { get; private set; }

            /// <summary>复制状态并增加一种素材的数量。</summary>
            /// <param name="materialIndex">素材种类索引。</param>
            /// <param name="quantity">新增数量。</param>
            /// <returns>扩展后的状态。</returns>
            public PlannerState CloneWith(int materialIndex, int quantity)
            {
                var clone = new PlannerState(QuantityByMaterialIndex.Length);
                Array.Copy(QuantityByMaterialIndex, clone.QuantityByMaterialIndex,
                    QuantityByMaterialIndex.Length);
                clone.QuantityByMaterialIndex[materialIndex] += quantity;
                clone.ConsumedCount = ConsumedCount + quantity;
                return clone;
            }
        }
    }
}
