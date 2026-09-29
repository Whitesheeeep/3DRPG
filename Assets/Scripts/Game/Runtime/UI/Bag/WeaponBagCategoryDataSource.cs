using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.ItemSystem;

namespace RPG.Game.UI.Bag
{
    /// <summary>将 WeaponInventoryManager 转换为背包武器列表和详情快照。</summary>
    public sealed class WeaponBagCategoryDataSource : IBagCategoryDataSource
    {
        #region 依赖字段

        private readonly WeaponInventoryManager manager;
        private readonly CharacterRosterManager characterRosterManager;
        private readonly Func<string, string, UnityEngine.Sprite> spriteResolver;

        #endregion

        /// <summary>创建武器分类数据源。</summary>
        /// <param name="manager">由 GameArchitecture 持有的武器 Manager。</param>
        /// <param name="spriteResolver">按 Atlas Address 和 SpriteName 查找 Sprite 的函数。</param>
        public WeaponBagCategoryDataSource(
            WeaponInventoryManager manager,
            Func<string, string, UnityEngine.Sprite> spriteResolver,
            CharacterRosterManager characterRosterManager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.spriteResolver = spriteResolver ?? throw new ArgumentNullException(nameof(spriteResolver));
            this.characterRosterManager = characterRosterManager ?? throw new ArgumentNullException(nameof(characterRosterManager));
        }

        /// <inheritdoc />
        public ItemCategory Category => ItemCategory.Weapon;

        /// <inheritdoc />
        public string PrimarySortLabel => "等级";

        /// <inheritdoc />
        public IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection)
        {
            // 背包窗口可能在库存或物品数据库注入前预加载；此时保持空列表，避免预加载阶段抛出配置异常。
            if (!WeaponInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured)
                return Array.Empty<BagItemViewData>();
            IReadOnlyList<WeaponInstance> instances = manager.GetInstances();
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
                bool isEquipped = characterRosterManager.TryGetEquipmentOwner(value.Instance.InstanceId, out CharacterId ownerId);
                if (isEquipped && CharacterConfigManager.Instance.IsConfigured && CharacterConfigManager.Instance.TryGetConfig(
                        ownerId, out CharacterConfig character))
                {
                    ownerText = character.Name;
                    SpriteParts(character.SideIconAddress, character.SideIconSpriteName, out ownerIcon);
                }

                // Definition New 是业务级状态；列表投影只让当前排序结果中的首个实例显示图标。
                bool showNew = manager.IsDefinitionNew(value.Instance.DefinitionId) &&
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
                    isEquipped));
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
                !manager.TryGetInstance(instanceId, out WeaponInstance instance) ||
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
            bool isEquipped = characterRosterManager.TryGetEquipmentOwner(instance.InstanceId, out CharacterId ownerId);
            if (isEquipped && CharacterConfigManager.Instance.IsConfigured && CharacterConfigManager.Instance.TryGetConfig(
                    ownerId, out CharacterConfig character))
            {
                ownerText = character.Name;
                SpriteParts(character.SideIconAddress, character.SideIconSpriteName, out ownerIcon);
            }

            IReadOnlyList<BagWeaponAttributeViewData> weaponAttributes = BuildWeaponAttributeViews(weapon, instance);
            details = new BagDetailViewData(
                entryKey,
                weapon.DisplayName,
                (int)weapon.Rarity,
                icon,
                weapon.WeaponType.ToString(),
                $"Lv.{instance.Level}/{weapon.MaxLevel}",
                $"精炼 {instance.RefinementRank}",
                Array.Empty<string>(),
                weapon.Description,
                ownerText,
                ownerIcon,
                isEquipped && ownerIcon != null,
                true,
                true,
                weaponAttributes);
            return true;
        }

        #region 详情与排序辅助

        // 详情属性投影

        /// <summary>将合并后的武器静态属性限制为上半详情区的两个固定槽位。</summary>
        /// <param name="weapon">武器定义。</param>
        /// <param name="instance">武器实例。</param>
        /// <returns>按等级效果优先顺序排列的最多两条展示属性。</returns>
        private static IReadOnlyList<BagWeaponAttributeViewData> BuildWeaponAttributeViews(
            WeaponDefinition weapon, WeaponInstance instance)
        {
            IReadOnlyList<StaticGameplayAttributePresentationValue> values =
                BagGameplayEffectPresentationBuilder.BuildWeaponAttributeValues(
                    weapon, instance, $"背包武器 {weapon.DisplayName}");
            int count = Math.Min(2, values.Count);
            var result = new List<BagWeaponAttributeViewData>(count);
            for (int index = 0; index < count; index++)
            {
                StaticGameplayAttributePresentationValue value = values[index];
                string attributeName = string.IsNullOrWhiteSpace(value.Attribute.DisplayName)
                    ? value.Attribute.Name
                    : value.Attribute.DisplayName;
                string formattedValue = BagGameplayEffectPresentationBuilder.FormatStaticAttributeValue(
                    value.Type, value.Value);
                result.Add(new BagWeaponAttributeViewData(attributeName, formattedValue));
            }

            return result;
        }

        // 排序、资源和实例标识处理

        /// <summary>按武器数据和当前排序设置比较两个条目。</summary>
        private static int Compare(WeaponEntry left, WeaponEntry right, BagSortMode mode, BagSortDirection direction)
        {
            int primary;
            switch (mode)
            {
                case BagSortMode.PrimaryValue:
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

        #endregion

    }
}
