using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Game.UI.Bag;
using RPG.ItemSystem;
using UnityEngine;
using WS_Modules.GAS.GameplayEffect;

namespace RPG.Game.UI.Character
{
    /// <summary>把背包装备条目筛选为当前角色可查看的武器或指定部位圣遗物候选。</summary>
    internal sealed class CharacterEquipmentSelectionPresentationBuilder
    {
        #region 依赖字段

        // 依赖字段：复用背包分类数据源以保持图标、等级、锁定和装备者展示一致。
        private readonly WeaponInventoryManager weaponInventoryManager;
        private readonly ArtifactInventoryManager artifactInventoryManager;
        private readonly WeaponBagCategoryDataSource weaponDataSource;
        private readonly ArtifactBagCategoryDataSource artifactDataSource;

        #endregion

        #region 生命周期

        /// <summary>创建角色装备候选投影器并复用背包的装备条目样式。</summary>
        /// <param name="weaponInventoryManager">武器实例库存。</param>
        /// <param name="artifactInventoryManager">圣遗物实例库存。</param>
        /// <param name="characterRosterManager">装备关系权威索引。</param>
        /// <param name="spriteResolver">动态图集 Sprite 解析函数。</param>
        public CharacterEquipmentSelectionPresentationBuilder(WeaponInventoryManager weaponInventoryManager,
            ArtifactInventoryManager artifactInventoryManager, CharacterRosterManager characterRosterManager,
            Func<string, string, Sprite> spriteResolver)
        {
            this.weaponInventoryManager = weaponInventoryManager ?? throw new ArgumentNullException(nameof(weaponInventoryManager));
            this.artifactInventoryManager = artifactInventoryManager ?? throw new ArgumentNullException(nameof(artifactInventoryManager));
            weaponDataSource = new WeaponBagCategoryDataSource(weaponInventoryManager, spriteResolver,
                characterRosterManager);
            artifactDataSource = new ArtifactBagCategoryDataSource(artifactInventoryManager, spriteResolver,
                characterRosterManager);
        }

        #endregion

        #region 候选构建

        /// <summary>按角色武器规则或圣遗物当前槽位构建排序后的候选条目。</summary>
        /// <param name="character">当前角色实例。</param>
        /// <param name="category">武器或圣遗物分类。</param>
        /// <param name="artifactSlot">圣遗物目标槽位；武器分类忽略此参数。</param>
        /// <param name="sortMode">背包排序字段。</param>
        /// <param name="sortDirection">排序方向。</param>
        /// <returns>符合角色和槽位规则的候选列表。</returns>
        public IReadOnlyList<BagItemViewData> BuildEntries(CharacterInstance character, ItemCategory category,
            ArtifactSlot artifactSlot, BagSortMode sortMode, BagSortDirection sortDirection)
        {
            if (character == null) return Array.Empty<BagItemViewData>();
            IReadOnlyList<BagItemViewData> sourceEntries = category == ItemCategory.Weapon
                ? weaponDataSource.BuildEntries(sortMode, sortDirection)
                : category == ItemCategory.Artifact
                    ? artifactDataSource.BuildEntries(sortMode, sortDirection)
                    : Array.Empty<BagItemViewData>();

            var result = new List<BagItemViewData>(sourceEntries.Count);
            for (int index = 0; index < sourceEntries.Count; index++)
            {
                BagItemViewData entry = sourceEntries[index];
                if (IsValidCandidate(character, category, artifactSlot, entry.EntryKey)) result.Add(entry);
            }
            return result;
        }

        /// <summary>通过背包详情构建器取得候选装备的完整静态详情。</summary>
        /// <param name="entryKey">候选装备稳定键。</param>
        /// <param name="details">候选详情。</param>
        /// <returns>详情存在时返回 true。</returns>
        public bool TryBuildDetails(BagEntryKey entryKey, out BagDetailViewData details)
        {
            if (entryKey.Category == ItemCategory.Weapon)
                return weaponDataSource.TryBuildDetails(entryKey, out details);
            if (entryKey.Category == ItemCategory.Artifact)
                return artifactDataSource.TryBuildDetails(entryKey, out details);
            details = null;
            return false;
        }

        /// <summary>为右侧动态属性行单独投影候选武器或圣遗物的静态效果。</summary>
        /// <param name="entryKey">候选装备的稳定条目键。</param>
        /// <param name="characterId">当前预览角色，用于武器冲突诊断。</param>
        /// <returns>候选静态效果的结构化属性名和值。</returns>
        public IReadOnlyList<CharacterEquipmentAttributeLineViewData> BuildAttributeLines(
            BagEntryKey entryKey, CharacterId characterId)
        {
            if (!TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId) ||
                !ItemManager.Instance.IsConfigured)
                return Array.Empty<CharacterEquipmentAttributeLineViewData>();

            if (entryKey.Category == ItemCategory.Weapon &&
                weaponInventoryManager.TryGetInstance(instanceId, out WeaponInstance weaponInstance) &&
                ItemManager.Instance.TryGetDefinition(weaponInstance.DefinitionId, out ItemDefinition weaponItem) &&
                weaponItem is WeaponDefinition weaponDefinition)
                return CharacterWindowPresentationBuilder.BuildWeaponAttributeLines(
                    weaponDefinition, weaponInstance, characterId);

            if (entryKey.Category == ItemCategory.Artifact &&
                artifactInventoryManager.TryGetInstance(instanceId, out ArtifactInstance artifactInstance) &&
                ItemManager.Instance.TryGetDefinition(artifactInstance.DefinitionId, out ItemDefinition artifactItem) &&
                artifactItem is ArtifactDefinition artifactDefinition)
            {
                IReadOnlyList<StaticGameplayAttributePresentationValue> values =
                    BagGameplayEffectPresentationBuilder.BuildStaticAttributeValues(
                        artifactDefinition.LevelEffects, artifactInstance.Level,
                        $"CharacterWindow Candidate Artifact {artifactInstance.InstanceId}");
                return CharacterWindowPresentationBuilder.ConvertAttributeLines(values);
            }

            return Array.Empty<CharacterEquipmentAttributeLineViewData>();
        }

        /// <summary>重新检查候选是否仍属于当前角色允许的装备集合。</summary>
        /// <param name="character">当前角色实例。</param>
        /// <param name="category">候选分类。</param>
        /// <param name="artifactSlot">圣遗物目标槽位。</param>
        /// <param name="entryKey">候选稳定键。</param>
        /// <returns>当前仍可作为该装备槽候选时返回 true。</returns>
        public bool IsValidCandidate(CharacterInstance character, ItemCategory category,
            ArtifactSlot artifactSlot, BagEntryKey entryKey)
        {
            if (character == null || entryKey.Category != category ||
                !TryParseInstanceId(entryKey.Value, out EquipmentInstanceId instanceId) ||
                !ItemManager.Instance.IsConfigured)
                return false;

            if (category == ItemCategory.Weapon)
                return weaponInventoryManager.TryGetInstance(instanceId, out WeaponInstance weaponInstance) &&
                       ItemManager.Instance.TryGetDefinition(weaponInstance.DefinitionId, out ItemDefinition weaponItem) &&
                       weaponItem is WeaponDefinition weapon && character.Config.AllowsWeaponType(weapon.WeaponType);
            return category == ItemCategory.Artifact &&
                   artifactInventoryManager.TryGetInstance(instanceId, out ArtifactInstance artifactInstance) &&
                   ItemManager.Instance.TryGetDefinition(artifactInstance.DefinitionId, out ItemDefinition artifactItem) &&
                   artifactItem is ArtifactDefinition artifact &&
                   artifact.Slot == artifactSlot;
        }

        #endregion

        #region 内部校验

        /// <summary>解析候选条目的装备实例标识。</summary>
        /// <param name="value">背包条目保存的稳定 ID。</param>
        /// <param name="instanceId">解析出的装备实例 ID。</param>
        /// <returns>文本非空且 ID 有效时返回 true。</returns>
        private static bool TryParseInstanceId(string value, out EquipmentInstanceId instanceId)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                instanceId = default;
                return false;
            }

            instanceId = new EquipmentInstanceId(value);
            return instanceId.IsValid;
        }

        #endregion
    }
}
