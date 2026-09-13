using System;
using System.Collections.Generic;
using System.Globalization;
using RPG.Character;
using RPG.ItemSystem;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Bag
{
    /// <summary>将 WeaponInventoryManager 转换为背包武器列表和详情快照。</summary>
    public sealed class WeaponBagCategoryDataSource : IBagCategoryDataSource
    {
        private readonly Func<string, string, UnityEngine.Sprite> spriteResolver;

        /// <summary>创建武器分类数据源。</summary>
        /// <param name="spriteResolver">按 Atlas Address 和 SpriteName 查找 Sprite 的函数。</param>
        public WeaponBagCategoryDataSource(Func<string, string, UnityEngine.Sprite> spriteResolver)
        {
            this.spriteResolver = spriteResolver ?? throw new ArgumentNullException(nameof(spriteResolver));
        }

        /// <inheritdoc />
        public ItemCategory Category => ItemCategory.Weapon;

        /// <inheritdoc />
        public IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection)
        {
            // 背包窗口可能在库存或物品数据库注入前预加载；此时保持空列表，避免预加载阶段抛出配置异常。
            if (!WeaponInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured)
                return Array.Empty<BagItemViewData>();
            IReadOnlyList<WeaponInstance> instances = WeaponInventoryManager.Instance.GetInstances();
            var values = new List<WeaponEntry>(instances.Count);
            for (int index = 0; index < instances.Count; index++)
            {
                WeaponInstance instance = instances[index];
                if (!ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition) ||
                    !(definition is WeaponDefinition weapon))
                {
                    continue;
                }

                values.Add(new WeaponEntry(instance, weapon));
            }

            values.Sort((left, right) => Compare(left, right, sortMode, sortDirection));
            var result = new List<BagItemViewData>(values.Count);
            var displayedNewDefinitionIds = new HashSet<ItemId>();
            for (int index = 0; index < values.Count; index++)
            {
                WeaponEntry value = values[index];
                SpriteParts(value.Weapon.IconAddress, value.Weapon.IconSpriteName,
                    out UnityEngine.Sprite icon);
                UnityEngine.Sprite ownerIcon = null;
                string ownerText = string.Empty;
                if (value.Instance.IsEquipped && CharacterConfigManager.Instance.IsConfigured && CharacterConfigManager.Instance.TryGetConfig(
                        value.Instance.EquippedCharacterId, out CharacterConfig character))
                {
                    ownerText = character.Name;
                    SpriteParts(character.SideIconAddress, character.SideIconSpriteName, out ownerIcon);
                }

                // Definition New 是业务级状态；列表投影只让当前排序结果中的首个实例显示图标。
                bool showNew = WeaponInventoryManager.Instance.IsDefinitionNew(value.Instance.DefinitionId) &&
                    displayedNewDefinitionIds.Add(value.Instance.DefinitionId);

                result.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.Weapon, value.Instance.InstanceId.ToString()),
                    value.Weapon.DisplayName,
                    (int)value.Weapon.Rarity,
                    $"Lv.{value.Instance.Level}",
                    icon,
                    ownerIcon,
                    ownerText,
                    showNew,
                    value.Instance.IsLocked,
                    value.Instance.IsEquipped));
            }

            return result;
        }

        /// <inheritdoc />
        public bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details)
        {
            details = null;
            if (!WeaponInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured) return false;
            if (entryKey.Category != ItemCategory.Weapon ||
                !TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId) ||
                !WeaponInventoryManager.Instance.TryGetInstance(instanceId, out WeaponInstance instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition) ||
                !(definition is WeaponDefinition weapon))
            {
                return false;
            }

            //  解析内容由于 BagItem 的
            // 解析武器图标
            SpriteParts(weapon.IconAddress, weapon.IconSpriteName, out UnityEngine.Sprite icon);
            string ownerText = string.Empty;
            UnityEngine.Sprite ownerIcon = null;
            // 解析装备者图标和名称
            if (instance.IsEquipped && CharacterConfigManager.Instance.IsConfigured && CharacterConfigManager.Instance.TryGetConfig(
                    instance.EquippedCharacterId, out CharacterConfig character))
            {
                ownerText = character.Name;
                SpriteParts(character.SideIconAddress, character.SideIconSpriteName, out ownerIcon);
            }

            WeaponDetails weaponDetails = WeaponDetailsQuery.Create(weapon, instance);
            IReadOnlyList<string> attributes = BuildAttributeTexts(weaponDetails);
            details = new BagDetailViewData(
                entryKey,
                weapon.DisplayName,
                (int)weapon.Rarity,
                icon,
                ownerText,
                ownerIcon,
                weapon.Description,
                weapon.WeaponType.ToString(),
                $"Lv.{instance.Level}/{weapon.MaxLevel}",
                $"精炼 {instance.RefinementRank}",
                attributes as string[] ?? new List<string>(attributes).ToArray(),
                instance.IsLocked,
                instance.IsEquipped);
            return true;
        }

        /// <summary>按武器数据和当前排序设置比较两个条目。</summary>
        private static int Compare(WeaponEntry left, WeaponEntry right, BagSortMode mode, BagSortDirection direction)
        {
            int primary;
            switch (mode)
            {
                case BagSortMode.Level:
                    primary = left.Instance.Level.CompareTo(right.Instance.Level);
                    break;
                case BagSortMode.AcquisitionSequence:
                    primary = left.Instance.AcquisitionSequence.CompareTo(right.Instance.AcquisitionSequence);
                    break;
                default:
                    primary = ((int)left.Weapon.Rarity).CompareTo((int)right.Weapon.Rarity);
                    break;
            }

            if (direction == BagSortDirection.Descending) primary = -primary;
            if (primary != 0) return primary;

            int rarity = ((int)right.Weapon.Rarity).CompareTo((int)left.Weapon.Rarity);
            if (rarity != 0) return rarity;
            int level = right.Instance.Level.CompareTo(left.Instance.Level);
            if (level != 0) return level;
            int sequence = left.Instance.AcquisitionSequence.CompareTo(right.Instance.AcquisitionSequence);
            if (sequence != 0) return sequence;
            return string.Compare(left.Instance.InstanceId.ToString(), right.Instance.InstanceId.ToString(), StringComparison.Ordinal);
        }

        /// <summary>生成最多两个静态属性文本，并按 Modifier 类型选择数值格式。</summary>
        /// <param name="details">当前武器的详情快照。</param>
        /// <returns>按首次出现顺序排列的属性文本。</returns>
        private static IReadOnlyList<string> BuildAttributeTexts(WeaponDetails details)
        {
            var contributions = new List<AttributeValue>();
            AppendAttributes(details.LevelEffects, contributions);
            AppendAttributes(details.RefinementEffects, contributions);

            // 同一 Attribute 同时出现 Add 与 Multiply 是配置冲突；聚合仍继续，保证 UI 能显示其余详情。
            for (int index = 0; index < contributions.Count; index++)
            {
                AttributeValue value = contributions[index];
                if (!value.HasTypeConflict) continue;
                UnityEngine.Debug.LogError(
                    $"[WeaponBag] 武器 '{details.DisplayName}' ({details.DefinitionId}) 的 Attribute " +
                    $"'{value.Attribute.DisplayName}' 同时配置了 Add 和 Multiply；详情优先显示 Multiply。", details.Definition);
            }

            var result = new List<string>(2);
            for (int index = 0; index < contributions.Count && result.Count < 2; index++)
            {
                AttributeValue value = contributions[index];
                result.Add($"{value.Attribute.DisplayName}: {FormatAttributeValue(value)}");
            }

            return result;
        }

        /// <summary>合并同一 Attribute 的 Add 或 Multiply 贡献并保持首次出现顺序。</summary>
        /// <param name="evaluations">按效果顺序排列的静态 Modifier 结果。</param>
        /// <param name="values">接收按 Attribute 合并后的显示值。</param>
        private static void AppendAttributes(
            IReadOnlyList<WeaponEffectEvaluation> evaluations,
            List<AttributeValue> values)
        {
            for (int effectIndex = 0; effectIndex < evaluations.Count; effectIndex++)
            {
                IReadOnlyList<WeaponEffectContribution> contributions = evaluations[effectIndex].Contributions;
                for (int contributionIndex = 0; contributionIndex < contributions.Count; contributionIndex++)
                {
                    GameplayEffectStaticModifierResult result = contributions[contributionIndex].Result;
                    if ((result.Type != AttributeModifierType.Add && result.Type != AttributeModifierType.Multiply) ||
                        !result.Attribute.IsValid)
                    {
                        continue;
                    }

                    int existingIndex = values.FindIndex(item => item.Attribute.Id == result.Attribute.Id);

                    // 首次出现的 Attribute 决定列表顺序；Multiply 的中性聚合值由实际首项提供。
                    if (existingIndex < 0)
                    {
                        values.Add(new AttributeValue(result.Attribute, result.Type, result.Magnitude, false));
                        continue;
                    }

                    AttributeValue existing = values[existingIndex];
                    if (existing.Type != result.Type)
                    {
                        // 原神式武器词条不应同时对同一 Attribute 配置 Add 与 Multiply；发生冲突时保留 Multiply。
                        if (result.Type == AttributeModifierType.Multiply)
                        {
                            values[existingIndex] = new AttributeValue(
                                existing.Attribute,
                                AttributeModifierType.Multiply,
                                result.Magnitude,
                                true);
                        }
                        else
                        {
                            values[existingIndex] = new AttributeValue(
                                existing.Attribute,
                                existing.Type,
                                existing.Value,
                                true);
                        }

                        continue;
                    }

                    float aggregatedValue = existing.Type == AttributeModifierType.Add
                        ? existing.Value + result.Magnitude
                        : existing.Value * result.Magnitude;
                    values[existingIndex] = new AttributeValue(
                        existing.Attribute,
                        existing.Type,
                        aggregatedValue,
                        existing.HasTypeConflict);
                }
            }
        }

        /// <summary>按 Modifier 类型格式化一个武器详情属性值。</summary>
        /// <param name="value">已完成聚合的属性值。</param>
        /// <returns>适合详情面板显示的数值文本。</returns>
        private static string FormatAttributeValue(AttributeValue value)
        {
            if (value.Type == AttributeModifierType.Add &&
                value.Value >= 0f &&
                value.Value <= 1f)
            {
                // Add 数值落在闭区间 [0, 1] 时按归一化百分比显示，避免绑定某个具体 Attribute。
                return value.Value.ToString("0.##%", CultureInfo.InvariantCulture);
            }

            if (value.Type == AttributeModifierType.Multiply)
            {
                // GAS 的 Multiply 以 1 为中性倍率，因此详情显示相对增益而不是原始倍率。
                float relativePercentage = value.Value - 1f;
                return relativePercentage.ToString("+0.##%;-0.##%;0%", CultureInfo.InvariantCulture);
            }

            return value.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>按 Address 和 SpriteName 解析一个图标；缺失时返回空。</summary>
        private void SpriteParts(string address, string spriteName, out UnityEngine.Sprite sprite)
        {
            sprite = string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(spriteName)
                ? null
                : spriteResolver(address, spriteName);
        }

        /// <summary>将实例 ID 文本转换为装备实例标识。</summary>
        private static bool TryParseInstanceId(string value, out EquipmentInstanceId instanceId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                instanceId = default;
                return false;
            }

            instanceId = new EquipmentInstanceId(value);
            return true;
        }

        /// <summary>缓存一次武器定义和实例的排序输入。</summary>
        private readonly struct WeaponEntry
        {
            /// <summary>创建武器排序输入。</summary>
            /// <param name="instance">武器实例。</param>
            /// <param name="weapon">武器定义。</param>
            public WeaponEntry(WeaponInstance instance, WeaponDefinition weapon)
            {
                Instance = instance;
                Weapon = weapon;
            }

            public WeaponInstance Instance { get; }
            public WeaponDefinition Weapon { get; }
        }

        /// <summary>缓存一个按 Attribute Id 合并后的显示值及其 Modifier 类型。</summary>
        private readonly struct AttributeValue
        {
            /// <summary>创建一个属性显示值。</summary>
            /// <param name="attribute">属性定义。</param>
            /// <param name="type">当前采用的 Modifier 类型。</param>
            /// <param name="value">按类型合并后的数值或倍率。</param>
            /// <param name="hasTypeConflict">是否曾同时遇到 Add 与 Multiply。</param>
            public AttributeValue(
                GameplayAttribute attribute,
                AttributeModifierType type,
                float value,
                bool hasTypeConflict)
            {
                Attribute = attribute;
                Type = type;
                Value = value;
                HasTypeConflict = hasTypeConflict;
            }

            public GameplayAttribute Attribute { get; }
            public AttributeModifierType Type { get; }
            public float Value { get; }
            public bool HasTypeConflict { get; }
        }
    }
}
