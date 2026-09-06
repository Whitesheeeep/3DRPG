using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>集中保存 ItemDefinition 和分类默认数据的配置数据库。</summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "RPG/ItemSystem/Item Database", order = 0)]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField, LabelText("物品定义列表")] private List<ItemDefinition> definitions = new();
        [SerializeReference, LabelText("分类默认数据")] private List<ItemDefaultData> categoryDefaults = new();
        // 这些计数器记录已经分配过的编号，删除定义后也不回退，避免未来复用旧 ID。
        [SerializeField, HideInInspector, MinValue(1)] private int nextMaterialIdNumber = 1;
        [SerializeField, HideInInspector, MinValue(1)] private int nextIngredientIdNumber = 1;
        [SerializeField, HideInInspector, MinValue(1)] private int nextFoodIdNumber = 1;
        [SerializeField, HideInInspector, MinValue(1)] private int nextWeaponIdNumber = 1;
        [SerializeField, HideInInspector, MinValue(1)] private int nextArtifactIdNumber = 1;
        private Dictionary<ItemId, ItemDefinition> definitionIndex;

        /// <summary>获取数据库中的物品定义。</summary>
        public IReadOnlyList<ItemDefinition> Definitions => definitions;

        /// <summary>获取分类默认数据。</summary>
        public IReadOnlyList<ItemDefaultData> CategoryDefaults => categoryDefaults;

        /// <summary>尝试按 ItemId 查询定义。</summary>
        /// <param name="itemId">待查询标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetDefinition(ItemId itemId, out ItemDefinition definition)
        {
            EnsureIndex();
            return definitionIndex.TryGetValue(itemId, out definition);
        }

        /// <summary>尝试读取一个分类的默认数据。</summary>
        /// <param name="category">物品分类。</param>
        /// <param name="defaultData">找到的默认数据。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetCategoryDefault(ItemCategory category, out ItemDefaultData defaultData)
        {
            categoryDefaults ??= new List<ItemDefaultData>();
            for (int index = 0; index < categoryDefaults.Count; index++)
            {
                ItemDefaultData candidate = categoryDefaults[index];
                if (candidate == null)
                    throw new InvalidOperationException($"ItemDatabase 的分类默认数据第 {index} 项为空。");
                if (candidate.Category != category) continue;
                defaultData = candidate;
                return true;
            }

            defaultData = null;
            return false;
        }

        /// <summary>取得指定分类的必需默认数据。</summary>
        /// <param name="category">物品分类。</param>
        /// <returns>对应分类的默认数据。</returns>
        /// <exception cref="InvalidOperationException">数据库未配置该分类时抛出。</exception>
        public ItemDefaultData GetRequiredCategoryDefault(ItemCategory category)
        {
            if (TryGetCategoryDefault(category, out ItemDefaultData defaultData)) return defaultData;
            throw new InvalidOperationException($"ItemDatabase '{name}' 未配置分类 {category} 的默认数据。");
        }

        /// <summary>验证全部定义并建立运行时索引。</summary>
        /// <exception cref="InvalidOperationException">数据库或定义存在错误时抛出。</exception>
        public void ValidateAndBuildIndex()
        {
            definitions ??= new List<ItemDefinition>();
            categoryDefaults ??= new List<ItemDefaultData>();
            var index = new Dictionary<ItemId, ItemDefinition>();
            var categories = new HashSet<ItemCategory>();
            for (int indexPosition = 0; indexPosition < categoryDefaults.Count; indexPosition++)
            {
                ItemDefaultData defaultData = categoryDefaults[indexPosition];
                if (defaultData == null) throw new InvalidOperationException($"ItemDatabase 的分类默认数据第 {indexPosition} 项为空。");
                defaultData.Validate();
                if (!categories.Add(defaultData.Category)) throw new InvalidOperationException($"ItemDatabase 重复配置分类默认数据：{defaultData.Category}。");
            }

            for (int indexPosition = 0; indexPosition < definitions.Count; indexPosition++)
            {
                ItemDefinition definition = definitions[indexPosition];
                if (definition == null) throw new InvalidOperationException($"ItemDatabase 的 Definition 第 {indexPosition} 项为空。");
                definition.Validate();
                ValidateStableId(definition);
                if (!index.TryAdd(definition.ItemId, definition)) throw new InvalidOperationException($"ItemDatabase 包含重复 ItemId：{definition.ItemId}。");
            }

            ValidateCounter("material", nextMaterialIdNumber, definitions);
            ValidateCounter("ingredient", nextIngredientIdNumber, definitions);
            ValidateCounter("food", nextFoodIdNumber, definitions);
            ValidateCounter("weapon", nextWeaponIdNumber, definitions);
            ValidateCounter("artifact", nextArtifactIdNumber, definitions);

            definitionIndex = index;
        }

        /// <summary>编辑器修改资产时执行非阻断校验。</summary>
        private void OnValidate()
        {
            try
            {
                ValidateAndBuildIndex();
            }
            catch (Exception exception)
            {
                definitionIndex = null;
                Debug.LogError($"[ItemDatabase] {name} 校验失败：{exception.Message}", this);
            }
        }

        /// <summary>确保读取前已经建立索引。</summary>
        private void EnsureIndex()
        {
            if (definitionIndex == null) ValidateAndBuildIndex();
        }

        /// <summary>验证定义的 ItemId 与最终物品分类前缀一致。</summary>
        /// <param name="definition">待验证物品定义。</param>
        private static void ValidateStableId(ItemDefinition definition)
        {
            string prefix = definition.Category switch
            {
                ItemCategory.Material => "material",
                ItemCategory.Ingredient => "ingredient",
                ItemCategory.Food => "food",
                ItemCategory.Weapon => "weapon",
                ItemCategory.Artifact => "artifact",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(prefix) || !TryParseStableId(definition.ItemId.Value, prefix, out _))
                throw new InvalidOperationException($"物品定义 '{definition.name}' 的 ItemId '{definition.ItemId}' 必须符合 {prefix}_0001 格式并匹配分类。");
        }

        /// <summary>验证某个类别的持久化计数器没有落后于现有编号。</summary>
        /// <param name="prefix">类别前缀。</param>
        /// <param name="nextNumber">数据库保存的下一个编号。</param>
        /// <param name="allDefinitions">数据库中的全部定义。</param>
        private static void ValidateCounter(string prefix, int nextNumber, IReadOnlyList<ItemDefinition> allDefinitions)
        {
            if (nextNumber < 1) throw new InvalidOperationException($"类别 {prefix} 的下一个 ID 编号必须大于零。");
            for (int index = 0; index < allDefinitions.Count; index++)
            {
                ItemDefinition definition = allDefinitions[index];
                if (definition == null || !TryParseStableId(definition.ItemId.Value, prefix, out int number)) continue;
                if (number >= nextNumber)
                    throw new InvalidOperationException($"类别 {prefix} 的 ID 计数器 {nextNumber} 不得小于等于现有编号 {number}。");
            }
        }

        /// <summary>解析固定四位数字 ItemId，避免运行时程序集依赖 Editor 工具。</summary>
        /// <param name="id">待解析 ID。</param>
        /// <param name="prefix">预期前缀。</param>
        /// <param name="number">解析出的编号。</param>
        /// <returns>格式正确时返回 true。</returns>
        private static bool TryParseStableId(string id, string prefix, out int number)
        {
            number = 0;
            string marker = prefix + "_";
            if (string.IsNullOrEmpty(id) || !id.StartsWith(marker, StringComparison.Ordinal)) return false;
            string suffix = id.Substring(marker.Length);
            if (suffix.Length != 4) return false;
            // 运行时校验与 Editor 分配器保持相同的 ASCII 格式契约。
            for (int index = 0; index < suffix.Length; index++)
                if (suffix[index] < '0' || suffix[index] > '9') return false;
            if (!int.TryParse(suffix, out number)) return false;
            return number >= 1 && number <= 9999;
        }
    }

}
