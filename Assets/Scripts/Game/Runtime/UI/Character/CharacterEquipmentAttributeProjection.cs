using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Character
{
    /// <summary>保存角色基础 Stat 与装备结算结果，供角色界面拆分显示。</summary>
    internal readonly struct CharacterAttributeProjectionValue
    {
        /// <summary>创建角色属性投影值。</summary>
        /// <param name="attribute">属性定义。</param>
        /// <param name="baseValue">角色当前等级烘焙基础值。</param>
        /// <param name="totalValue">应用静态装备效果后的总值。</param>
        public CharacterAttributeProjectionValue(GameplayAttribute attribute, float baseValue, float totalValue)
        {
            Attribute = attribute;
            BaseValue = baseValue;
            TotalValue = totalValue;
        }

        /// <summary>获取属性定义。</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>获取角色当前等级烘焙基础值。</summary>
        public float BaseValue { get; }
        /// <summary>获取静态装备效果结算后的总值。</summary>
        public float TotalValue { get; }
    }

    /// <summary>将角色等级烘焙 Stat 与静态装备效果投影为角色窗口总值。</summary>
    internal static class CharacterEquipmentAttributeProjection
    {
        #region 静态诊断状态

        // 同一 GE 的上下文提示只记录一次，避免角色窗口常规刷新反复刷屏。
        private static readonly HashSet<int> contextWarningEffectInstanceIds = new();

        #endregion

        #region 公开投影

        /// <summary>按角色配置顺序结算角色 Stat 与当前装备的静态 GE。</summary>
        /// <param name="instance">当前稳定角色实例。</param>
        /// <param name="equipmentSystem">读取已装备物品的权威系统。</param>
        /// <param name="attributeResolver">读取角色当前等级烘焙基础值的解析器。</param>
        /// <returns>只包含角色配置中定义的 Stat，且保持 AttributeSet 顺序的总值。</returns>
        internal static IReadOnlyList<CharacterAttributeProjectionValue> ResolveStatValues(
            CharacterInstance instance,
            CharacterEquipmentSystem equipmentSystem,
            CharacterAttributeProgressionResolver attributeResolver)
        {
            IReadOnlyList<GameplayAttributeValue> baseValues =
                attributeResolver.ResolveBaseValues(instance.Config, instance.Level);
            var definitionByAttributeIdMap = new Dictionary<int, GameplayAttributeDefinition>();
            var modifierListByAttributeIdMap = new Dictionary<int, List<GameplayEffectStaticModifierResult>>();

            // 只允许修改当前角色配置声明的 Stat；Resource 和外来 Attribute 不进入角色属性页。
            for (int setIndex = 0; setIndex < instance.Config.InitialAttributeSets.Count; setIndex++)
            {
                IReadOnlyList<GameplayAttributeDefinition> definitions =
                    instance.Config.InitialAttributeSets[setIndex].Definitions;
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    GameplayAttributeDefinition definition = definitions[definitionIndex];
                    if (definition.Type == GameplayAttributeType.Stat)
                        definitionByAttributeIdMap.Add(definition.Attribute.Id, definition);
                }
            }

            // 装备槽关系来自 CharacterInstance；不会访问战斗 Actor、ASC 或运行时 GE。
            CollectWeaponModifiers(instance, equipmentSystem, definitionByAttributeIdMap, modifierListByAttributeIdMap);
            CollectArtifactModifiers(instance, equipmentSystem, definitionByAttributeIdMap, modifierListByAttributeIdMap);

            var projectedValues = new List<CharacterAttributeProjectionValue>(definitionByAttributeIdMap.Count);
            for (int valueIndex = 0; valueIndex < baseValues.Count; valueIndex++)
            {
                GameplayAttributeValue baseValue = baseValues[valueIndex];
                if (!definitionByAttributeIdMap.TryGetValue(baseValue.Attribute.Id, out GameplayAttributeDefinition definition))
                    continue;

                float totalValue = baseValue.Value;
                if (modifierListByAttributeIdMap.TryGetValue(baseValue.Attribute.Id,
                        out List<GameplayEffectStaticModifierResult> modifiers))
                    totalValue = ApplyModifiers(baseValue.Value, definition, modifiers, instance.CharacterId);

                projectedValues.Add(new CharacterAttributeProjectionValue(
                    baseValue.Attribute, baseValue.Value, totalValue));
            }

            return projectedValues;
        }

        #endregion

        #region 装备效果收集

        /// <summary>收集角色当前武器的等级与精炼静态效果。</summary>
        /// <param name="instance">角色实例。</param>
        /// <param name="equipmentSystem">装备关系查询系统。</param>
        /// <param name="statDefinitionByAttributeIdMap">允许显示和修改的 Stat 定义索引。</param>
        /// <param name="modifierListByAttributeIdMap">按 Attribute 汇集的静态 Modifier。</param>
        private static void CollectWeaponModifiers(CharacterInstance instance,
            CharacterEquipmentSystem equipmentSystem,
            IReadOnlyDictionary<int, GameplayAttributeDefinition> statDefinitionByAttributeIdMap,
            Dictionary<int, List<GameplayEffectStaticModifierResult>> modifierListByAttributeIdMap)
        {
            if (!equipmentSystem.TryGetEquippedWeapon(instance.CharacterId, out WeaponInstance weapon)) return;
            if (!ItemManager.Instance.TryGetDefinition(weapon.DefinitionId, out ItemDefinition item) ||
                !(item is WeaponDefinition definition))
            {
                Debug.LogError($"[CharacterWindow] 角色 {instance.CharacterId} 的已装备武器 {weapon.InstanceId} 缺少有效 WeaponDefinition。", instance.Config);
                return;
            }

            CollectEffects(definition.LevelEffects, weapon.Level,
                $"角色 {instance.CharacterId} 武器等级效果 {weapon.InstanceId}",
                statDefinitionByAttributeIdMap, modifierListByAttributeIdMap);
            CollectEffects(definition.RefinementEffects, weapon.RefinementRank,
                $"角色 {instance.CharacterId} 武器精炼效果 {weapon.InstanceId}",
                statDefinitionByAttributeIdMap, modifierListByAttributeIdMap);
        }

        /// <summary>收集角色五个圣遗物槽位的等级静态效果。</summary>
        /// <param name="instance">角色实例。</param>
        /// <param name="equipmentSystem">装备关系查询系统。</param>
        /// <param name="statDefinitionByAttributeIdMap">允许显示和修改的 Stat 定义索引。</param>
        /// <param name="modifierListByAttributeIdMap">按 Attribute 汇集的静态 Modifier。</param>
        private static void CollectArtifactModifiers(CharacterInstance instance,
            CharacterEquipmentSystem equipmentSystem,
            IReadOnlyDictionary<int, GameplayAttributeDefinition> statDefinitionByAttributeIdMap,
            Dictionary<int, List<GameplayEffectStaticModifierResult>> modifierListByAttributeIdMap)
        {
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                ArtifactSlot slot = (ArtifactSlot)slotIndex;
                if (!equipmentSystem.TryGetEquippedArtifact(instance.CharacterId, slot, out ArtifactInstance artifact))
                    continue;
                if (!ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out ItemDefinition item) ||
                    !(item is ArtifactDefinition definition))
                {
                    Debug.LogError($"[CharacterWindow] 角色 {instance.CharacterId} 的圣遗物槽 {slot} 中实例 {artifact.InstanceId} 缺少有效 ArtifactDefinition。", instance.Config);
                    continue;
                }

                CollectEffects(definition.LevelEffects, artifact.Level,
                    $"角色 {instance.CharacterId} 圣遗物 {slot} 等级效果 {artifact.InstanceId}",
                    statDefinitionByAttributeIdMap, modifierListByAttributeIdMap);
            }
        }

        /// <summary>把一组装备 GE 的可静态计算 Modifier 收集到角色 Stat 索引。</summary>
        /// <param name="effects">武器或圣遗物静态效果。</param>
        /// <param name="level">用于求值的装备等级或精炼阶数。</param>
        /// <param name="context">日志业务上下文。</param>
        /// <param name="statDefinitionByAttributeIdMap">允许显示和修改的 Stat 定义索引。</param>
        /// <param name="modifierListByAttributeIdMap">按 Attribute 汇集的静态 Modifier。</param>
        private static void CollectEffects(IReadOnlyList<GameplayEffectData> effects, int level, string context,
            IReadOnlyDictionary<int, GameplayAttributeDefinition> statDefinitionByAttributeIdMap,
            Dictionary<int, List<GameplayEffectStaticModifierResult>> modifierListByAttributeIdMap)
        {
            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                GameplayEffectData effect = effects[effectIndex];
                GameplayEffectStaticEvaluationStatus status = GameplayEffectStaticEvaluation.Evaluate(
                    effect, Math.Max(1, level), out IReadOnlyList<GameplayEffectStaticModifierResult> results);
                if (status == GameplayEffectStaticEvaluationStatus.InvalidConfiguration)
                {
                    Debug.LogError($"[CharacterWindow] {context} 的 GE '{effect.name}' 静态数值无效，已跳过该效果。", effect);
                    continue;
                }

                if (status == GameplayEffectStaticEvaluationStatus.RequiresRuntimeContext &&
                    contextWarningEffectInstanceIds.Add(effect.GetInstanceID()))
                    Debug.LogWarning($"[CharacterWindow] {context} 的 GE '{effect.name}' 含有运行时上下文 Modifier；仅显示其余可静态计算部分。", effect);

                for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    GameplayEffectStaticModifierResult result = results[resultIndex];
                    if (!result.Attribute.IsValid || !statDefinitionByAttributeIdMap.ContainsKey(result.Attribute.Id))
                        continue;
                    if (!modifierListByAttributeIdMap.TryGetValue(result.Attribute.Id,
                            out List<GameplayEffectStaticModifierResult> modifiers))
                    {
                        modifiers = new List<GameplayEffectStaticModifierResult>();
                        modifierListByAttributeIdMap.Add(result.Attribute.Id, modifiers);
                    }
                    modifiers.Add(result);
                }
            }
        }

        #endregion

        #region Modifier 结算

        /// <summary>按 GAS Aggregator 的 Priority、Add、Multiply、Override 顺序计算一个 Stat。</summary>
        /// <param name="baseValue">角色当前等级的烘焙基础值。</param>
        /// <param name="definition">Stat 的定义和固定数值边界。</param>
        /// <param name="modifiers">所有已装备物品提供的静态 Modifier。</param>
        /// <param name="characterId">所属角色，用于错误诊断。</param>
        /// <returns>完成优先级结算并按 Attribute 边界 Clamp 后的静态总值。</returns>
        private static float ApplyModifiers(float baseValue, GameplayAttributeDefinition definition,
            List<GameplayEffectStaticModifierResult> modifiers, CharacterId characterId)
        {
            modifiers.Sort((left, right) => left.Priority.CompareTo(right.Priority));
            double value = baseValue;
            int modifierIndex = 0;
            while (modifierIndex < modifiers.Count)
            {
                int priority = modifiers[modifierIndex].Priority;
                double additive = 0d;
                double multiplier = 1d;
                bool hasOverride = false;
                float overrideValue = default;
                int overrideCount = 0;

                // 单个 Priority 层先累计 Add/Multiply，再由唯一 Override 覆盖，和运行时 Aggregator 保持一致。
                while (modifierIndex < modifiers.Count && modifiers[modifierIndex].Priority == priority)
                {
                    GameplayEffectStaticModifierResult modifier = modifiers[modifierIndex++];
                    switch (modifier.Type)
                    {
                        case AttributeModifierType.Add:
                            additive += modifier.Magnitude;
                            break;
                        case AttributeModifierType.Multiply:
                            multiplier *= modifier.Magnitude;
                            break;
                        case AttributeModifierType.Override:
                            overrideCount++;
                            overrideValue = modifier.Magnitude;
                            hasOverride = true;
                            break;
                        default:
                            Debug.LogError($"[CharacterWindow] 角色 {characterId} 的 Attribute {definition.Attribute} 出现未知 Modifier 类型 {modifier.Type}。", null);
                            return baseValue;
                    }
                }

                if (overrideCount > 1)
                {
                    Debug.LogError($"[CharacterWindow] 角色 {characterId} 的 Attribute {definition.Attribute} 在 Priority {priority} 有多个 Override，显示角色基础值。", null);
                    return baseValue;
                }

                value = (value + additive) * multiplier;
                if (hasOverride) value = overrideValue;
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    Debug.LogError($"[CharacterWindow] 角色 {characterId} 的 Attribute {definition.Attribute} 结算产生非有限值，显示角色基础值。", null);
                    return baseValue;
                }
            }

            return Mathf.Clamp((float)value, definition.MinValue, definition.MaxValue);
        }

        #endregion
    }
}
