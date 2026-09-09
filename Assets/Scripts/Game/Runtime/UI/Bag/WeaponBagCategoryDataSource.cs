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

                result.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.Weapon, value.Instance.InstanceId.ToString()),
                    value.Weapon.DisplayName,
                    (int)value.Weapon.Rarity,
                    $"Lv.{value.Instance.Level}",
                    icon,
                    ownerIcon,
                    ownerText,
                    value.Instance.IsNew,
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

            SpriteParts(weapon.IconAddress, weapon.IconSpriteName, out UnityEngine.Sprite icon);
            string ownerText = string.Empty;
            UnityEngine.Sprite ownerIcon = null;
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

        /// <summary>生成最多两个静态 Add 属性文本。</summary>
        private static IReadOnlyList<string> BuildAttributeTexts(WeaponDetails details)
        {
            var contributions = new List<AttributeValue>();
            AppendAttributes(details.LevelEffects, contributions);
            AppendAttributes(details.RefinementEffects, contributions);
            var result = new List<string>(2);
            for (int index = 0; index < contributions.Count && result.Count < 2; index++)
            {
                AttributeValue value = contributions[index];
                result.Add($"{value.Attribute.DisplayName}: {value.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            }

            return result;
        }

        /// <summary>合并同一 Attribute 的 Add 贡献并保持首次出现顺序。</summary>
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
                    if (result.Type != AttributeModifierType.Add || !result.Attribute.IsValid) continue;
                    int existingIndex = values.FindIndex(item => item.Attribute.Id == result.Attribute.Id);
                    if (existingIndex < 0) values.Add(new AttributeValue(result.Attribute, result.Magnitude));
                    else values[existingIndex] = new AttributeValue(values[existingIndex].Attribute, values[existingIndex].Value + result.Magnitude);
                }
            }
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

        /// <summary>缓存一个按 Attribute Id 合并后的显示值。</summary>
        private readonly struct AttributeValue
        {
            /// <summary>创建一个属性显示值。</summary>
            /// <param name="attribute">属性定义。</param>
            /// <param name="value">合并后的数值。</param>
            public AttributeValue(GameplayAttribute attribute, float value)
            {
                Attribute = attribute;
                Value = value;
            }

            public GameplayAttribute Attribute { get; }
            public float Value { get; }
        }
    }
}
