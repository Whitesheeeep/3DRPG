using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RPG.Character;
using RPG.Game;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Services;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.Generated;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Character
{
    /// <summary>把角色实例、配置和装备查询投影为 CharacterWindow 的纯显示快照。</summary>
    internal sealed class CharacterWindowPresentationBuilder
    {
        #region 固定显示属性

        // 属性页只展示预先确认的五项；固定顺序避免配置顺序或其他 Stat 影响图标对应关系。
        private static readonly int[] DisplayedAttributeIdsInOrder =
        {
            GameplayAttributes.Attribute_MaxHealth.Id,
            GameplayAttributes.Attribute_AttackPower.Id,
            GameplayAttributes.Attribute_Armor.Id,
            GameplayAttributes.Attribute_CriticalChance.Id,
            GameplayAttributes.Attribute_CriticalDamage.Id
        };

        #endregion

        #region 依赖字段

        // 依赖字段：属性只读取 Config 烘焙结果；装备页通过装备系统解析实例关系。
        private readonly CharacterEquipmentSystem equipmentSystem;
        private readonly WindowSpriteAtlasLeaseService spriteAtlasLeaseService;
        private readonly IReadOnlyList<Sprite> partyMarkSprites;
        private readonly CharacterAttributeProgressionResolver attributeResolver = new();

        #endregion

        #region 生命周期

        /// <summary>创建角色窗口显示投影器。</summary>
        /// <param name="equipmentSystemValue">角色装备查询系统。</param>
        /// <param name="spriteAtlasLeaseServiceValue">动态图集租约服务。</param>
        public CharacterWindowPresentationBuilder(CharacterEquipmentSystem equipmentSystemValue,
            WindowSpriteAtlasLeaseService spriteAtlasLeaseServiceValue, IReadOnlyList<Sprite> partyMarkSpritesValue)
        {
            equipmentSystem = equipmentSystemValue ?? throw new ArgumentNullException(nameof(equipmentSystemValue));
            spriteAtlasLeaseService = spriteAtlasLeaseServiceValue ?? throw new ArgumentNullException(nameof(spriteAtlasLeaseServiceValue));
            partyMarkSprites = partyMarkSpritesValue ?? throw new ArgumentNullException(nameof(partyMarkSpritesValue));
        }

        #endregion

        #region 构建

        /// <summary>按当前选中角色构建完整页面数据。</summary>
        /// <param name="instances">全部角色实例。</param>
        /// <param name="selectedInstance">当前选中实例。</param>
        /// <param name="page">当前页面。</param>
        /// <param name="selectedArtifactIndex">当前选中的圣遗物槽位下标。</param>
        /// <returns>角色窗口显示快照。</returns>
        public CharacterWindowViewData Build(IReadOnlyList<CharacterInstance> instances,
            CharacterInstance selectedInstance, CharacterWindowPage page, int selectedArtifactIndex)
        {
            List<CharacterInstance> sortedInstances = SortInstances(instances);
            CharacterPartyManager partyManager = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
            int selectedIndex = FindSelectedIndex(sortedInstances, selectedInstance);
            CharacterInstance selected = selectedIndex >= 0 ? sortedInstances[selectedIndex] : null;
            if (selected == null)
                return new CharacterWindowViewData(Array.Empty<CharacterRosterEntryViewData>(), -1, null, null,
                    new CharacterArtifactSummaryViewData(0, Array.Empty<CharacterEquipmentAttributeLineViewData>()),
                    page, Array.Empty<CharacterAttributeViewData>(), null, BuildEmptyArtifactSlots(), 0, null,
                    default, null);

            var roster = new List<CharacterRosterEntryViewData>(sortedInstances.Count);
            for (int index = 0; index < sortedInstances.Count; index++)
            {
                CharacterInstance instance = sortedInstances[index];
                int partySlotIndex = partyManager.FindSlot(instance.CharacterId);
                Sprite partyMarkSprite = partySlotIndex >= 0 && partySlotIndex < partyMarkSprites.Count
                    ? partyMarkSprites[partySlotIndex] : null;
                roster.Add(new CharacterRosterEntryViewData(instance,
                    ResolveSprite(instance.Config.SideIconAddress, instance.Config.SideIconSpriteName),
                    index == selectedIndex, false, partySlotIndex >= 0, partySlotIndex, partyMarkSprite));
            }

            CharacterHeaderViewData header = BuildHeader(selected);
            IReadOnlyList<CharacterAttributeViewData> attributes = BuildAttributes(selected);
            CharacterWeaponViewData weapon = BuildWeapon(selected);
            IReadOnlyList<CharacterArtifactSlotItemViewData> artifactSlots = BuildArtifactSlots(selected, selectedArtifactIndex);
            int clampedArtifactIndex = Mathf.Clamp(selectedArtifactIndex, 0, 4);
            CharacterArtifactSummaryViewData artifactSummary = BuildArtifactSummary(selected);
            CharacterArtifactViewData selectedArtifact = BuildArtifactDetails(selected, (ArtifactSlot)clampedArtifactIndex);
            CharacterPartyPositionViewData partyPosition = BuildPartyPosition(sortedInstances, selected, partyManager);
            return new CharacterWindowViewData(roster, selectedIndex, header,
                ResolveSprite(selected.Config.FullBodyPortraitAddress, selected.Config.FullBodyPortraitSpriteName), artifactSummary, page,
                attributes, weapon, artifactSlots, clampedArtifactIndex, selectedArtifact,
                selected.CharacterId, partyPosition);
        }

        /// <summary>为当前角色投影固定队伍槽位名称及实际选择项。</summary>
        /// <param name="instances">按显示顺序排列的全部已拥有角色。</param>
        /// <param name="selectedInstance">当前浏览角色。</param>
        /// <param name="partyManager">当前队伍查询 Manager。</param>
        /// <returns>包含未加入项和四个槽位的选择快照。</returns>
        private static CharacterPartyPositionViewData BuildPartyPosition(
            IReadOnlyList<CharacterInstance> instances, CharacterInstance selectedInstance,
            CharacterPartyManager partyManager)
        {
            var options = new List<string>(CharacterParty.SlotCount + 1) { "未加入队伍" };
            int currentSlot = partyManager.FindSlot(selectedInstance.CharacterId);
            for (int slotIndex = 0; slotIndex < CharacterParty.SlotCount; slotIndex++)
            {
                CharacterId slotCharacterId = partyManager.GetCharacterIdAtSlot(slotIndex);
                string memberName = slotCharacterId.IsValid
                    ? ResolveCharacterName(instances, slotCharacterId)
                    : "空位";
                options.Add($"第 {slotIndex + 1} 位 · {memberName}");
            }

            return new CharacterPartyPositionViewData(selectedInstance.CharacterId, currentSlot + 1, options);
        }

        /// <summary>从当前已拥有实例中解析槽位角色名称。</summary>
        /// <param name="instances">已拥有角色实例。</param>
        /// <param name="characterId">队伍槽位角色标识。</param>
        /// <returns>角色配置名称；队伍与名册不一致时返回明确占位文案。</returns>
        private static string ResolveCharacterName(IReadOnlyList<CharacterInstance> instances, CharacterId characterId)
        {
            for (int index = 0; index < instances.Count; index++)
            {
                CharacterInstance instance = instances[index];
                if (instance.CharacterId == characterId) return instance.Config.Name;
            }
            return "角色数据缺失";
        }

        /// <summary>按品质、获得顺序和角色标识稳定排序角色实例。</summary>
        /// <param name="instances">待排序实例。</param>
        /// <returns>排序后的副本。</returns>
        private static List<CharacterInstance> SortInstances(IReadOnlyList<CharacterInstance> instances)
        {
            var sorted = instances == null ? new List<CharacterInstance>() : instances.Where(item => item != null).ToList();
            sorted.Sort((left, right) =>
            {
                int rarity = ((int)right.Config.Rarity).CompareTo((int)left.Config.Rarity);
                if (rarity != 0) return rarity;
                int acquisition = left.AcquisitionSequence.CompareTo(right.AcquisitionSequence);
                return acquisition != 0 ? acquisition : string.CompareOrdinal(left.CharacterId.ToString(), right.CharacterId.ToString());
            });
            return sorted;
        }

        /// <summary>查找当前选中实例在排序列表中的下标。</summary>
        /// <param name="instances">排序后的实例。</param>
        /// <param name="selectedInstance">当前实例。</param>
        /// <returns>找到的下标，找不到返回负数。</returns>
        private static int FindSelectedIndex(IReadOnlyList<CharacterInstance> instances, CharacterInstance selectedInstance)
        {
            if (selectedInstance == null) return -1;
            for (int index = 0; index < instances.Count; index++)
                if (instances[index].CharacterId == selectedInstance.CharacterId) return index;
            return -1;
        }

        /// <summary>构建角色公共标题。</summary>
        /// <param name="instance">目标实例。</param>
        /// <returns>标题数据。</returns>
        private static CharacterHeaderViewData BuildHeader(CharacterInstance instance)
        {
            int cap = GetCurrentLevelCap(instance.Config, instance.AscensionRank);
            string capState = instance.Level >= instance.Config.MaxLevel ? "已满级" :
                instance.Level >= cap ? "已达当前等级上限" : string.Empty;
            int nextExperience = 0;
            if (instance.Level < cap && instance.Config.GrowthProfile.BakedLevelProgressions.Count >= instance.Level)
                nextExperience = instance.Config.GrowthProfile.BakedLevelProgressions[instance.Level - 1].NextExperience;
            string experience = capState.Length > 0 ? capState :
                $"{instance.CurrentExperience.ToString("N0", CultureInfo.InvariantCulture)}/{nextExperience.ToString("N0", CultureInfo.InvariantCulture)}";
            bool atLevelCap = instance.Level >= cap;
            float experienceProgress = atLevelCap ? 1f : nextExperience > 0
                ? Mathf.Clamp01((float)instance.CurrentExperience / nextExperience)
                : 0f;
            string experiencePercent = $"{Mathf.RoundToInt(experienceProgress * 100f)}%";
            return new CharacterHeaderViewData(instance.Config.Name, (int)instance.Config.Rarity, instance.AscensionRank,
                $"Lv.{instance.Level}", $"/ {cap}", experience, capState, experienceProgress,
                experiencePercent, atLevelCap || nextExperience > 0, instance.Config.Introduction);
        }

        /// <summary>解析当前突破阶数对应的等级上限。</summary>
        /// <param name="config">角色配置。</param>
        /// <param name="rank">当前突破阶数。</param>
        /// <returns>当前等级上限。</returns>
        private static int GetCurrentLevelCap(CharacterConfig config, int rank)
        {
            if (rank <= 0) return config.AscensionStages.Count == 0 ? config.MaxLevel : config.AscensionStages[0].RequiredLevel;
            int stageIndex = Mathf.Clamp(rank - 1, 0, config.AscensionStages.Count - 1);
            return config.AscensionStages.Count == 0 ? config.MaxLevel : config.AscensionStages[stageIndex].MaxLevelAfter;
        }

        /// <summary>构建角色基础 Stat 与静态装备净加成拆分后的显示行。</summary>
        /// <param name="instance">目标角色实例。</param>
        /// <returns>按固定 UI 顺序排列的属性行。</returns>
        private IReadOnlyList<CharacterAttributeViewData> BuildAttributes(CharacterInstance instance)
        {
            IReadOnlyList<CharacterAttributeProjectionValue> values = CharacterEquipmentAttributeProjection.ResolveStatValues(
                instance, equipmentSystem, attributeResolver);
            // 投影按 AttributeId 建索引，随后严格按 UI 固定顺序取值，不把 Speed 等其他 Stat 放入页面。
            var valueByAttributeIdMap = new Dictionary<int, CharacterAttributeProjectionValue>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                CharacterAttributeProjectionValue value = values[index];
                valueByAttributeIdMap.Add(value.Attribute.Id, value);
            }

            var lines = new List<CharacterAttributeViewData>(DisplayedAttributeIdsInOrder.Length);
            for (int index = 0; index < DisplayedAttributeIdsInOrder.Length; index++)
            {
                int attributeId = DisplayedAttributeIdsInOrder[index];
                if (!valueByAttributeIdMap.TryGetValue(attributeId, out CharacterAttributeProjectionValue value))
                    continue;

                string name = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                bool isPercentage = IsPercentageAttribute(attributeId);
                string baseValueText = FormatAttributeValue(value.BaseValue, isPercentage);
                string totalValueText = FormatAttributeValue(value.TotalValue, isPercentage);
                string equipmentBonusText = FormatEquipmentBonus(value.BaseValue, value.TotalValue,
                    baseValueText, totalValueText, isPercentage);
                lines.Add(new CharacterAttributeViewData(attributeId, name, baseValueText, equipmentBonusText));
            }

            return lines;
        }

        /// <summary>判断属性页是否将数值按百分比显示。</summary>
        /// <param name="attributeId">属性标识。</param>
        /// <returns>暴击率和暴击伤害返回 true。</returns>
        private static bool IsPercentageAttribute(int attributeId)
        {
            return attributeId == GameplayAttributes.Attribute_CriticalChance.Id ||
                   attributeId == GameplayAttributes.Attribute_CriticalDamage.Id;
        }

        /// <summary>按属性展示规则格式化角色本体值或装备结算总值。</summary>
        /// <param name="value">待格式化数值。</param>
        /// <param name="isPercentage">是否以百分比显示。</param>
        /// <returns>整数或一位小数百分比文本。</returns>
        private static string FormatAttributeValue(float value, bool isPercentage)
        {
            if (isPercentage)
                return (Math.Round(value * 100d, 1, MidpointRounding.AwayFromZero))
                    .ToString("0.0", CultureInfo.InvariantCulture) + "%";

            return Math.Round(value, MidpointRounding.AwayFromZero)
                .ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>根据最终显示精度计算装备净加成，保证本体值与加成相加后等于显示总值。</summary>
        /// <param name="baseValue">角色本体值。</param>
        /// <param name="totalValue">静态装备结算值。</param>
        /// <param name="baseValueText">已经格式化的本体值。</param>
        /// <param name="totalValueText">已经格式化的总值。</param>
        /// <param name="isPercentage">是否以百分点显示差值。</param>
        /// <returns>带符号的装备净加成；没有变化时为空字符串。</returns>
        private static string FormatEquipmentBonus(float baseValue, float totalValue,
            string baseValueText, string totalValueText, bool isPercentage)
        {
            if (isPercentage)
            {
                double displayedBaseValue = Math.Round(baseValue * 100d, 1, MidpointRounding.AwayFromZero);
                double displayedTotalValue = Math.Round(totalValue * 100d, 1, MidpointRounding.AwayFromZero);
                double displayedBonus = displayedTotalValue - displayedBaseValue;
                if (Math.Abs(displayedBonus) < 0.05d)
                    return string.Empty;

                return (displayedBonus > 0d ? "+" : string.Empty) +
                       displayedBonus.ToString("0.0", CultureInfo.InvariantCulture) + "%";
            }

            long displayedBaseValueInteger = long.Parse(baseValueText, CultureInfo.InvariantCulture);
            long displayedTotalValueInteger = long.Parse(totalValueText, CultureInfo.InvariantCulture);
            long displayedBonusInteger = displayedTotalValueInteger - displayedBaseValueInteger;
            if (displayedBonusInteger == 0)
                return string.Empty;

            return (displayedBonusInteger > 0 ? "+" : string.Empty) +
                   displayedBonusInteger.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>构建已装备武器页面。</summary>
        /// <param name="instance">目标角色实例。</param>
        /// <returns>武器显示数据。</returns>
        private CharacterWeaponViewData BuildWeapon(CharacterInstance instance)
        {
            if (!equipmentSystem.TryGetEquippedWeapon(instance.CharacterId, out WeaponInstance weapon) ||
                !ItemManager.Instance.TryGetDefinition(weapon.DefinitionId, out ItemDefinition item) ||
                !(item is WeaponDefinition definition))
                return new CharacterWeaponViewData(false, default, null, string.Empty, string.Empty, 0,
                    string.Empty, string.Empty, Array.Empty<CharacterEquipmentAttributeLineViewData>(), string.Empty, null);

            IReadOnlyList<CharacterEquipmentAttributeLineViewData> lines =
                BuildWeaponAttributeLines(definition, weapon, instance.CharacterId);
            return new CharacterWeaponViewData(true, weapon.InstanceId,
                ResolveSprite(definition.IconAddress, definition.IconSpriteName), definition.DisplayName,
                definition.WeaponType.ToString(), (int)definition.Rarity,
                $"Lv.{weapon.Level}/{definition.MaxLevel}", $"精炼 {weapon.RefinementRank}", lines,
                definition.Description,
                new BagItemViewData(new BagEntryKey(ItemCategory.Weapon, weapon.InstanceId.ToString()),
                    definition.DisplayName, (int)definition.Rarity, $"Lv.{weapon.Level}",
                    ResolveSprite(definition.IconAddress, definition.IconSpriteName), null, string.Empty,
                    false, weapon.IsLocked, true));
        }

        /// <summary>把武器等级与精炼效果按 Attribute 合并成详情行。</summary>
        /// <param name="definition">武器静态定义。</param>
        /// <param name="weapon">当前武器实例。</param>
        /// <param name="characterId">装备该武器的角色标识。</param>
        /// <returns>按等级效果优先顺序合并后的结构化属性行。</returns>
        internal static IReadOnlyList<CharacterEquipmentAttributeLineViewData> BuildWeaponAttributeLines(
            WeaponDefinition definition, WeaponInstance weapon, CharacterId characterId)
        {
            string context = $"CharacterWindow Weapon {characterId} / {weapon.InstanceId}";
            IReadOnlyList<StaticGameplayAttributePresentationValue> values =
                BagGameplayEffectPresentationBuilder.BuildWeaponAttributeValues(definition, weapon, context);
            return ConvertAttributeLines(values);
        }

        /// <summary>把结构化 Modifier 值转换为名称和值分开的详情行。</summary>
        /// <param name="values">已经聚合的静态属性值。</param>
        /// <returns>属性详情行。</returns>
        internal static IReadOnlyList<CharacterEquipmentAttributeLineViewData> ConvertAttributeLines(
            IReadOnlyList<StaticGameplayAttributePresentationValue> values)
        {
            var lines = new List<CharacterEquipmentAttributeLineViewData>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                StaticGameplayAttributePresentationValue value = values[index];
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                string formattedValue = BagGameplayEffectPresentationBuilder.FormatStaticAttributeValue(
                    value.Type, value.Value);
                lines.Add(new CharacterEquipmentAttributeLineViewData(attributeName, formattedValue));
            }
            return lines;
        }

        /// <summary>构建五个固定圣遗物槽位。</summary>
        /// <param name="instance">目标角色实例。</param>
        /// <param name="selectedArtifactIndex">选中的槽位下标。</param>
        /// <returns>五个槽位显示数据。</returns>
        private IReadOnlyList<CharacterArtifactSlotItemViewData> BuildArtifactSlots(CharacterInstance instance, int selectedArtifactIndex)
        {
            var result = new List<CharacterArtifactSlotItemViewData>(5);
            for (int index = 0; index < 5; index++)
            {
                ArtifactSlot slot = (ArtifactSlot)index;
                ItemDefinition item = null;
                bool hasArtifact = equipmentSystem.TryGetEquippedArtifact(instance.CharacterId, slot, out ArtifactInstance artifact) &&
                    ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out item) && item is ArtifactDefinition;
                Sprite icon = null;
                int rarity = 0;
                string levelText = string.Empty;
                EquipmentInstanceId instanceId = default;
                if (hasArtifact)
                {
                    ArtifactDefinition definition = (ArtifactDefinition)item;
                    instanceId = artifact.InstanceId;
                    rarity = (int)definition.Rarity;
                    icon = ResolveSprite(definition.IconAddress, definition.IconSpriteName);
                    levelText = $"+{artifact.Level}";
                }
                result.Add(new CharacterArtifactSlotItemViewData(slot, GetArtifactSlotName(slot), hasArtifact, instanceId,
                    icon, rarity, levelText, index == Mathf.Clamp(selectedArtifactIndex, 0, 4)));
            }
            return result;
        }

        /// <summary>构建空圣遗物槽位列表。</summary>
        /// <returns>五个未装备槽位。</returns>
        private static IReadOnlyList<CharacterArtifactSlotItemViewData> BuildEmptyArtifactSlots()
        {
            var result = new List<CharacterArtifactSlotItemViewData>(5);
            for (int index = 0; index < 5; index++)
                result.Add(new CharacterArtifactSlotItemViewData((ArtifactSlot)index, GetArtifactSlotName((ArtifactSlot)index),
                    false, default, null, 0, string.Empty, index == 0));
            return result;
        }

        /// <summary>解析当前选中圣遗物的静态详情。</summary>
        /// <param name="instance">角色实例。</param>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>详情数据。</returns>
        public CharacterArtifactViewData BuildArtifactDetails(CharacterInstance instance, ArtifactSlot slot)
        {
            if (instance == null || !equipmentSystem.TryGetEquippedArtifact(instance.CharacterId, slot, out ArtifactInstance artifact) ||
                !ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out ItemDefinition item) ||
                !(item is ArtifactDefinition definition))
                return new CharacterArtifactViewData(false, string.Empty, GetArtifactSlotName(slot), 0, null, string.Empty,
                    Array.Empty<CharacterEquipmentAttributeLineViewData>(), string.Empty, default, null);
            IReadOnlyList<StaticGameplayAttributePresentationValue> values = BagGameplayEffectPresentationBuilder.BuildStaticAttributeValues(
                definition.LevelEffects, artifact.Level, $"CharacterWindow Artifact {instance.CharacterId}");
            IReadOnlyList<CharacterEquipmentAttributeLineViewData> lines = ConvertAttributeLines(values);
            Sprite artifactIcon = ResolveSprite(definition.IconAddress, definition.IconSpriteName);
            return new CharacterArtifactViewData(true, definition.DisplayName, GetArtifactSlotName(slot), (int)definition.Rarity,
                artifactIcon, $"+{artifact.Level}/{definition.MaxLevel}", lines, definition.Description,
                artifact.InstanceId,
                new BagItemViewData(new BagEntryKey(ItemCategory.Artifact, artifact.InstanceId.ToString()),
                    definition.DisplayName, (int)definition.Rarity, $"+{artifact.Level}", artifactIcon,
                    null, string.Empty, false, artifact.IsLocked, true));
        }

        /// <summary>按 Attribute 合并五件已装备圣遗物的静态属性总量。</summary>
        /// <param name="instance">目标角色实例。</param>
        /// <returns>角色圣遗物属性汇总数据，不包含套装或随机词条信息。</returns>
        private CharacterArtifactSummaryViewData BuildArtifactSummary(CharacterInstance instance)
        {
            var values = new List<StaticGameplayAttributePresentationValue>();
            int equippedCount = 0;
            for (int slotIndex = 0; slotIndex < 5; slotIndex++)
            {
                ArtifactSlot slot = (ArtifactSlot)slotIndex;
                if (!equipmentSystem.TryGetEquippedArtifact(instance.CharacterId, slot, out ArtifactInstance artifact) ||
                    !ItemManager.Instance.TryGetDefinition(artifact.DefinitionId, out ItemDefinition item) ||
                    !(item is ArtifactDefinition definition)) continue;
                equippedCount++;
                IReadOnlyList<StaticGameplayAttributePresentationValue> artifactValues =
                    BagGameplayEffectPresentationBuilder.BuildStaticAttributeValues(
                    definition.LevelEffects, artifact.Level, $"CharacterWindow Artifact {instance.CharacterId}");
                for (int valueIndex = 0; valueIndex < artifactValues.Count; valueIndex++)
                    AppendArtifactAttribute(values, artifactValues[valueIndex], instance.CharacterId, slot);
            }

            return new CharacterArtifactSummaryViewData(equippedCount, ConvertAttributeLines(values));
        }

        /// <summary>把一件圣遗物的属性合并到角色总览，并记录不同运算类型之间的配置冲突。</summary>
        /// <param name="values">当前累计值，保持 Attribute 首次出现顺序。</param>
        /// <param name="incoming">新装备的属性值。</param>
        /// <param name="characterId">所属角色标识。</param>
        /// <param name="slot">来源圣遗物部位。</param>
        private static void AppendArtifactAttribute(List<StaticGameplayAttributePresentationValue> values,
            StaticGameplayAttributePresentationValue incoming, CharacterId characterId, ArtifactSlot slot)
        {
            int existingIndex = values.FindIndex(value => value.Attribute.Id == incoming.Attribute.Id);
            if (existingIndex < 0)
            {
                values.Add(incoming);
                return;
            }

            StaticGameplayAttributePresentationValue existing = values[existingIndex];
            if (existing.Type != incoming.Type)
            {
                Debug.LogError($"[CharacterWindow] 角色 {characterId} 的圣遗物 {GetArtifactSlotName(slot)} 在 Attribute '{incoming.Attribute.DisplayName}' 上混用了 Add/Multiply，按背包规则采用 Multiply。");
                values[existingIndex] = incoming.Type == AttributeModifierType.Multiply
                    ? new StaticGameplayAttributePresentationValue(existing.Attribute, incoming.Type, incoming.Value, true)
                    : new StaticGameplayAttributePresentationValue(existing.Attribute, existing.Type, existing.Value, true);
                return;
            }

            float aggregate = existing.Type == AttributeModifierType.Add
                ? existing.Value + incoming.Value
                : existing.Value * incoming.Value;
            values[existingIndex] = new StaticGameplayAttributePresentationValue(
                existing.Attribute, existing.Type, aggregate,
                existing.HasTypeConflict || incoming.HasTypeConflict);
        }

        /// <summary>按图集租约查询 Sprite；未加载完成时返回空而不显示白块。</summary>
        /// <param name="address">图集地址。</param>
        /// <param name="spriteName">Sprite 名称。</param>
        /// <returns>查询到的 Sprite 或空。</returns>
        private Sprite ResolveSprite(string address, string spriteName)
        {
            return spriteAtlasLeaseService.TryGetSprite(address, spriteName, out Sprite sprite) ? sprite : null;
        }

        /// <summary>返回五部位中文名称。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>部位名称。</returns>
        private static string GetArtifactSlotName(ArtifactSlot slot)
        {
            return slot switch
            {
                ArtifactSlot.FlowerOfLife => "生之花",
                ArtifactSlot.PlumeOfDeath => "死之羽",
                ArtifactSlot.SandsOfEon => "时之沙",
                ArtifactSlot.GobletOfEonothem => "空之杯",
                ArtifactSlot.CircletOfLogos => "理之冠",
                _ => "圣遗物"
            };
        }

        #endregion
    }
}
