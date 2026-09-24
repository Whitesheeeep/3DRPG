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
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Character
{
    /// <summary>把角色实例、配置和装备查询投影为 CharacterWindow 的纯显示快照。</summary>
    internal sealed class CharacterWindowPresentationBuilder
    {
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
            int selectedIndex = FindSelectedIndex(sortedInstances, selectedInstance);
            CharacterInstance selected = selectedIndex >= 0 ? sortedInstances[selectedIndex] : null;
            if (selected == null)
                return new CharacterWindowViewData(Array.Empty<CharacterRosterEntryViewData>(), -1, null, null,
                    new CharacterArtifactSummaryViewData(0, Array.Empty<string>()),
                    page, Array.Empty<CharacterAttributeViewData>(), null, BuildEmptyArtifactSlots(), 0, null);

            var roster = new List<CharacterRosterEntryViewData>(sortedInstances.Count);
            for (int index = 0; index < sortedInstances.Count; index++)
            {
                CharacterInstance instance = sortedInstances[index];
                CharacterPartyManager partyManager = GameArchitecture.Interface.GetManager<CharacterPartyManager>();
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
            return new CharacterWindowViewData(roster, selectedIndex, header,
                ResolveSprite(selected.Config.FullBodyPortraitAddress, selected.Config.FullBodyPortraitSpriteName), artifactSummary, page,
                attributes, weapon, artifactSlots, clampedArtifactIndex, selectedArtifact);
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
            float experienceProgress = nextExperience > 0
                ? Mathf.Clamp01((float)instance.CurrentExperience / nextExperience)
                : 0f;
            return new CharacterHeaderViewData(instance.Config.Name, (int)instance.Config.Rarity,
                $"Lv.{instance.Level}/{cap}", experience, capState, experienceProgress,
                capState.Length == 0 && nextExperience > 0);
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
        /// <returns>按配置顺序排列的属性行。</returns>
        private IReadOnlyList<CharacterAttributeViewData> BuildAttributes(CharacterInstance instance)
        {
            IReadOnlyList<CharacterAttributeProjectionValue> values = CharacterEquipmentAttributeProjection.ResolveStatValues(
                instance, equipmentSystem, attributeResolver);
            var lines = new List<CharacterAttributeViewData>();
            for (int index = 0; index < values.Count; index++)
            {
                CharacterAttributeProjectionValue value = values[index];
                string name = string.IsNullOrWhiteSpace(value.Attribute.DisplayName) ? value.Attribute.Name : value.Attribute.DisplayName;
                long displayedBaseValue = (long)Math.Round(value.BaseValue, MidpointRounding.AwayFromZero);
                long displayedTotalValue = (long)Math.Round(value.TotalValue, MidpointRounding.AwayFromZero);
                long displayedEquipmentBonus = displayedTotalValue - displayedBaseValue;
                string equipmentBonusText = displayedEquipmentBonus == 0
                    ? string.Empty
                    : (displayedEquipmentBonus > 0 ? "+" : string.Empty) +
                      displayedEquipmentBonus.ToString("0", CultureInfo.InvariantCulture);
                lines.Add(new CharacterAttributeViewData(name,
                    displayedBaseValue.ToString("0", CultureInfo.InvariantCulture), equipmentBonusText));
            }
            return lines;
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
                    string.Empty, string.Empty, Array.Empty<string>(), string.Empty);

            IReadOnlyList<string> lines = BuildWeaponAttributeLines(definition, weapon, instance.CharacterId);
            return new CharacterWeaponViewData(true, weapon.InstanceId,
                ResolveSprite(definition.IconAddress, definition.IconSpriteName), definition.DisplayName,
                definition.WeaponType.ToString(), (int)definition.Rarity,
                $"Lv.{weapon.Level}/{definition.MaxLevel}", $"精炼 {weapon.RefinementRank}", lines,
                definition.Description);
        }

        /// <summary>把武器等级与精炼效果按 Attribute 合并成详情行。</summary>
        /// <param name="definition">武器静态定义。</param>
        /// <param name="weapon">当前武器实例。</param>
        /// <param name="characterId">装备该武器的角色标识。</param>
        /// <returns>按等级效果优先顺序合并后的属性文本。</returns>
        private static IReadOnlyList<string> BuildWeaponAttributeLines(
            WeaponDefinition definition, WeaponInstance weapon, CharacterId characterId)
        {
            string context = $"CharacterWindow Weapon {characterId} / {weapon.InstanceId}";
            IReadOnlyList<StaticGameplayAttributePresentationValue> levelValues =
                BagGameplayEffectPresentationBuilder.BuildStaticAttributeValues(
                    definition.LevelEffects, weapon.Level, $"{context} Level");
            IReadOnlyList<StaticGameplayAttributePresentationValue> refinementValues =
                BagGameplayEffectPresentationBuilder.BuildStaticAttributeValues(
                    definition.RefinementEffects, weapon.RefinementRank, $"{context} Refinement");
            var mergedValues = new List<StaticGameplayAttributePresentationValue>(
                levelValues.Count + refinementValues.Count);

            // 先放等级结果，再并入精炼结果，使详情顺序稳定且与用户阅读顺序一致。
            AppendWeaponAttributeValues(mergedValues, levelValues, definition, weapon);
            AppendWeaponAttributeValues(mergedValues, refinementValues, definition, weapon);

            var lines = new List<string>(mergedValues.Count);
            for (int index = 0; index < mergedValues.Count; index++)
            {
                StaticGameplayAttributePresentationValue value = mergedValues[index];
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                string formattedValue = BagGameplayEffectPresentationBuilder.FormatStaticAttributeValue(
                    value.Type, value.Value);
                lines.Add($"{attributeName}: {formattedValue}");
            }

            return lines;
        }

        /// <summary>将一组武器效果的静态数值并入按 Attribute 聚合的列表。</summary>
        /// <param name="mergedValues">当前合并结果，按首次出现顺序保存。</param>
        /// <param name="incomingValues">本次计算得到的等级或精炼结果。</param>
        /// <param name="definition">武器定义，用于冲突诊断。</param>
        /// <param name="weapon">武器实例，用于冲突诊断。</param>
        private static void AppendWeaponAttributeValues(
            List<StaticGameplayAttributePresentationValue> mergedValues,
            IReadOnlyList<StaticGameplayAttributePresentationValue> incomingValues,
            WeaponDefinition definition,
            WeaponInstance weapon)
        {
            for (int index = 0; index < incomingValues.Count; index++)
            {
                StaticGameplayAttributePresentationValue incoming = incomingValues[index];
                int existingIndex = mergedValues.FindIndex(
                    value => value.Attribute.Id == incoming.Attribute.Id);
                if (existingIndex < 0)
                {
                    mergedValues.Add(incoming);
                    continue;
                }

                StaticGameplayAttributePresentationValue existing = mergedValues[existingIndex];
                if (existing.Type != incoming.Type)
                {
                    Debug.LogError(
                        $"[CharacterWindow] 武器 {definition.DisplayName} ({weapon.InstanceId}) 的 Attribute " +
                        $"'{incoming.Attribute.DisplayName}' 同时配置 Add 与 Multiply，按背包规则采用 Multiply。",
                        definition);
                    mergedValues[existingIndex] = incoming.Type == AttributeModifierType.Multiply
                        ? new StaticGameplayAttributePresentationValue(
                            existing.Attribute, incoming.Type, incoming.Value, true)
                        : new StaticGameplayAttributePresentationValue(
                            existing.Attribute, existing.Type, existing.Value, true);
                    continue;
                }

                float aggregate = existing.Type == AttributeModifierType.Add
                    ? existing.Value + incoming.Value
                    : existing.Value * incoming.Value;
                mergedValues[existingIndex] = new StaticGameplayAttributePresentationValue(
                    existing.Attribute, existing.Type, aggregate,
                    existing.HasTypeConflict || incoming.HasTypeConflict);
            }
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
                    Array.Empty<string>(), string.Empty, default);
            IReadOnlyList<string> lines = BagGameplayEffectPresentationBuilder.BuildStaticAttributeLines(
                definition.LevelEffects, artifact.Level, $"CharacterWindow Artifact {instance.CharacterId}");
            return new CharacterArtifactViewData(true, definition.DisplayName, GetArtifactSlotName(slot), (int)definition.Rarity,
                ResolveSprite(definition.IconAddress, definition.IconSpriteName),
                $"+{artifact.Level}/{definition.MaxLevel}", lines, definition.Description, artifact.InstanceId);
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

            var lines = new List<string>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                StaticGameplayAttributePresentationValue value = values[index];
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                lines.Add($"{attributeName}: {BagGameplayEffectPresentationBuilder.FormatStaticAttributeValue(value.Type, value.Value)}");
            }
            return new CharacterArtifactSummaryViewData(equippedCount, lines);
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
