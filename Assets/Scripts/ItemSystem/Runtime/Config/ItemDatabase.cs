using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.ItemSystem
{
    /// <summary>集中保存 ItemDefinition 和分类默认规则的配置数据库。</summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "RPG/ItemSystem/Item Database", order = 0)]
    public sealed class ItemDatabase : ScriptableObject
    {
        [SerializeField, LabelText("物品定义列表")] private List<ItemDefinition> definitions = new();
        [SerializeField, LabelText("分类默认规则")] private List<ItemCategoryDefaultRule> categoryDefaults = new();
        private Dictionary<ItemId, ItemDefinition> definitionIndex;

        /// <summary>获取数据库中的物品定义。</summary>
        public IReadOnlyList<ItemDefinition> Definitions => definitions;

        /// <summary>获取分类默认规则。</summary>
        public IReadOnlyList<ItemCategoryDefaultRule> CategoryDefaults => categoryDefaults;

        /// <summary>尝试按 ItemId 查询定义。</summary>
        /// <param name="itemId">待查询标识。</param>
        /// <param name="definition">找到的定义。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetDefinition(ItemId itemId, out ItemDefinition definition)
        {
            EnsureIndex();
            return definitionIndex.TryGetValue(itemId, out definition);
        }

        /// <summary>尝试读取一个分类的默认规则。</summary>
        /// <param name="category">物品分类。</param>
        /// <param name="rule">找到的默认规则。</param>
        /// <returns>找到时返回 true。</returns>
        public bool TryGetCategoryDefault(ItemCategory category, out ItemCategoryDefaultRule rule)
        {
            for (int index = 0; index < categoryDefaults.Count; index++)
            {
                if (categoryDefaults[index].Category != category) continue;
                rule = categoryDefaults[index];
                return true;
            }

            rule = null;
            return false;
        }

        /// <summary>验证全部定义并建立运行时索引。</summary>
        /// <exception cref="InvalidOperationException">数据库或定义存在错误时抛出。</exception>
        public void ValidateAndBuildIndex()
        {
            definitions ??= new List<ItemDefinition>();
            categoryDefaults ??= new List<ItemCategoryDefaultRule>();
            var index = new Dictionary<ItemId, ItemDefinition>();
            var categories = new HashSet<ItemCategory>();
            for (int indexPosition = 0; indexPosition < categoryDefaults.Count; indexPosition++)
            {
                ItemCategoryDefaultRule rule = categoryDefaults[indexPosition];
                if (rule == null) throw new InvalidOperationException($"ItemDatabase 的分类默认规则第 {indexPosition} 项为空。");
                rule.Validate();
                if (!categories.Add(rule.Category)) throw new InvalidOperationException($"ItemDatabase 重复配置分类默认规则：{rule.Category}。");
            }

            for (int indexPosition = 0; indexPosition < definitions.Count; indexPosition++)
            {
                ItemDefinition definition = definitions[indexPosition];
                if (definition == null) throw new InvalidOperationException($"ItemDatabase 的 Definition 第 {indexPosition} 项为空。");
                definition.Validate();
                if (!index.TryAdd(definition.ItemId, definition)) throw new InvalidOperationException($"ItemDatabase 包含重复 ItemId：{definition.ItemId}。");
            }

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
    }

}
