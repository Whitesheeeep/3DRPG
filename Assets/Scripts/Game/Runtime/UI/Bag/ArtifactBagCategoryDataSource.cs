using System;
using System.Collections.Generic;
using RPG.ItemSystem;
using UnityEngine;

namespace RPG.Game.UI.Bag
{
    /// <summary>将圣遗物实例库存转换为背包网格和通用详情快照。</summary>
    public sealed class ArtifactBagCategoryDataSource : IBagCategoryDataSource
    {
        #region 依赖字段

        private readonly ArtifactInventoryManager manager;
        private readonly Func<string, string, Sprite> spriteResolver;

        #endregion

        #region 初始化

        /// <summary>创建圣遗物分类数据源。</summary>
        /// <param name="manager">由 GameArchitecture 持有的圣遗物 Manager。</param>
        /// <param name="spriteResolver">按图集地址和 Sprite 名称解析图标。</param>
        public ArtifactBagCategoryDataSource(
            ArtifactInventoryManager manager,
            Func<string, string, Sprite> spriteResolver)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.spriteResolver = spriteResolver ?? throw new ArgumentNullException(nameof(spriteResolver));
        }

        #endregion

        #region 属性

        /// <inheritdoc />
        public ItemCategory Category => ItemCategory.Artifact;

        /// <inheritdoc />
        public string PrimarySortLabel => "等级";

        #endregion

        #region 构建投影

        /// <inheritdoc />
        public IReadOnlyList<BagItemViewData> BuildEntries(BagSortMode sortMode, BagSortDirection sortDirection)
        {
            if (!ArtifactInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured)
                return Array.Empty<BagItemViewData>();

            IReadOnlyList<ArtifactInstance> instances = manager.GetInstances();
            var values = new List<ArtifactEntry>(instances.Count);
            for (int index = 0; index < instances.Count; index++)
            {
                ArtifactInstance instance = instances[index];
                if (ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition) &&
                    definition is ArtifactDefinition artifact)
                    values.Add(new ArtifactEntry(instance, artifact));
            }

            values.Sort((left, right) => Compare(left, right, sortMode, sortDirection));
            var result = new List<BagItemViewData>(values.Count);
            var displayedNewDefinitionIds = new HashSet<ItemId>();
            for (int index = 0; index < values.Count; index++)
            {
                ArtifactEntry value = values[index];
                Sprite icon = ResolveSprite(value.Definition.IconAddress, value.Definition.IconSpriteName);
                bool showNew = manager.IsDefinitionNew(value.Instance.DefinitionId) &&
                               displayedNewDefinitionIds.Add(value.Instance.DefinitionId);
                result.Add(new BagItemViewData(
                    new BagEntryKey(ItemCategory.Artifact, value.Instance.InstanceId.ToString()),
                    value.Definition.DisplayName,
                    (int)value.Definition.Rarity,
                    $"+{value.Instance.Level}",
                    icon,
                    null,
                    string.Empty,
                    showNew,
                    value.Instance.IsLocked,
                    false));
            }

            return result;
        }

        /// <inheritdoc />
        public bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details)
        {
            details = null;
            if (!ArtifactInventoryManager.IsConfigured || !ItemManager.Instance.IsConfigured ||
                entryKey.Category != ItemCategory.Artifact ||
                !TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId) ||
                !manager.TryGetInstance(instanceId, out ArtifactInstance instance) ||
                !ItemManager.Instance.TryGetDefinition(instance.DefinitionId, out ItemDefinition definition) ||
                !(definition is ArtifactDefinition artifact))
                return false;

            Sprite icon = ResolveSprite(artifact.IconAddress, artifact.IconSpriteName);
            IReadOnlyList<string> lines = BagGameplayEffectPresentationBuilder.BuildStaticAttributeLines(
                artifact.LevelEffects,
                instance.Level,
                $"圣遗物 {artifact.DisplayName}");
            details = new BagDetailViewData(
                entryKey,
                artifact.DisplayName,
                (int)artifact.Rarity,
                icon,
                $"圣遗物 · {GetSlotLabel(artifact.Slot)}",
                $"Lv.{instance.Level}/{artifact.MaxLevel}",
                string.Empty,
                lines,
                artifact.Description,
                string.Empty,
                null,
                false,
                false,
                true);
            return true;
        }

        #endregion

        #region 内部辅助

        /// <summary>按分类、方向和稳定字段比较两个圣遗物。</summary>
        /// <param name="left">左侧条目。</param>
        /// <param name="right">右侧条目。</param>
        /// <param name="mode">排序字段。</param>
        /// <param name="direction">排序方向。</param>
        /// <returns>比较结果。</returns>
        private static int Compare(ArtifactEntry left, ArtifactEntry right, BagSortMode mode, BagSortDirection direction)
        {
            int primary = mode switch
            {
                BagSortMode.PrimaryValue => left.Instance.Level.CompareTo(right.Instance.Level),
                BagSortMode.AcquisitionSequence => left.Instance.AcquisitionSequence.CompareTo(right.Instance.AcquisitionSequence),
                _ => ((int)left.Definition.Rarity).CompareTo((int)right.Definition.Rarity)
            };
            if (direction == BagSortDirection.Descending) primary = -primary;
            if (primary != 0) return primary;
            int rarity = ((int)right.Definition.Rarity).CompareTo((int)left.Definition.Rarity);
            if (rarity != 0) return rarity;
            int level = right.Instance.Level.CompareTo(left.Instance.Level);
            if (level != 0) return level;
            int sequence = left.Instance.AcquisitionSequence.CompareTo(right.Instance.AcquisitionSequence);
            return sequence != 0 ? sequence : string.Compare(left.Instance.InstanceId.ToString(), right.Instance.InstanceId.ToString(), StringComparison.Ordinal);
        }

        /// <summary>解析圣遗物部位的中文展示名称。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>部位名称。</returns>
        private static string GetSlotLabel(ArtifactSlot slot) => slot switch
        {
            ArtifactSlot.FlowerOfLife => "生之花",
            ArtifactSlot.PlumeOfDeath => "死之羽",
            ArtifactSlot.SandsOfEon => "时之沙",
            ArtifactSlot.GobletOfEonothem => "空之杯",
            ArtifactSlot.CircletOfLogos => "理之冠",
            _ => "未知部位"
        };

        /// <summary>按配置地址解析图标，图集未准备好时返回空 Sprite。</summary>
        /// <param name="address">图集地址。</param>
        /// <param name="spriteName">Sprite 名称。</param>
        /// <returns>解析到的 Sprite。</returns>
        private Sprite ResolveSprite(string address, string spriteName) =>
            string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(spriteName)
                ? null
                : spriteResolver(address, spriteName);

        /// <summary>解析装备实例标识。</summary>
        /// <param name="value">稳定标识文本。</param>
        /// <param name="instanceId">解析后的实例标识。</param>
        /// <returns>解析成功时返回 true。</returns>
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

        /// <summary>缓存一个圣遗物实例和定义的排序输入。</summary>
        private readonly struct ArtifactEntry
        {
            /// <summary>创建排序输入。</summary>
            /// <param name="instance">圣遗物实例。</param>
            /// <param name="definition">圣遗物定义。</param>
            public ArtifactEntry(ArtifactInstance instance, ArtifactDefinition definition)
            {
                Instance = instance;
                Definition = definition;
            }

            /// <summary>获取实例。</summary>
            public ArtifactInstance Instance { get; }
            /// <summary>获取定义。</summary>
            public ArtifactDefinition Definition { get; }
        }

        #endregion
    }
}
