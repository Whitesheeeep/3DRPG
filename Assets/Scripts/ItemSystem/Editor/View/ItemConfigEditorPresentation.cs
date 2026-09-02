#if UNITY_EDITOR
using System;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.ItemSystem.Editor
{
    /// <summary>提供物品编辑器所有无状态文本、样式和原生列表呈现规则。</summary>
    internal static class ItemConfigEditorPresentation
    {
        /// <summary>获取分类中文名。</summary>
        /// <param name="category">分类。</param>
        /// <returns>中文分类名称。</returns>
        internal static string GetCategoryText(ItemCategory category) => category switch
        {
            ItemCategory.Material => "养成素材",
            ItemCategory.Ingredient => "食材",
            ItemCategory.Food => "料理",
            ItemCategory.Furnishing => "摆设",
            ItemCategory.Weapon => "武器",
            ItemCategory.Artifact => "圣遗物",
            ItemCategory.DevelopmentItem => "养成道具",
            _ => "未知"
        };

        /// <summary>获取定义类型中文名。</summary>
        /// <param name="definition">物品定义。</param>
        /// <returns>定义类型中文名。</returns>
        internal static string GetDefinitionKindText(ItemDefinition definition) => definition switch
        {
            WeaponDefinition => "武器定义",
            ArtifactDefinition => "圣遗物定义",
            DevelopmentItemDefinition => "养成道具定义",
            StackableItemDefinition => "可堆叠物品",
            null => "未知定义",
            _ => "物品定义"
        };

        /// <summary>获取养成道具用途中文名。</summary>
        /// <param name="type">养成用途。</param>
        /// <returns>用途中文名。</returns>
        internal static string GetDevelopmentTypeText(DevelopmentItemType type) => type switch
        {
            DevelopmentItemType.CharacterExperience => "角色经验素材",
            DevelopmentItemType.CharacterAscension => "角色突破素材",
            DevelopmentItemType.CharacterTalent => "角色天赋素材",
            DevelopmentItemType.WeaponExperience => "武器强化素材",
            DevelopmentItemType.WeaponAscension => "武器突破素材",
            DevelopmentItemType.WeaponRefinement => "武器精炼素材",
            DevelopmentItemType.ArtifactExperience => "圣遗物强化素材",
            _ => "未知养成用途"
        };

        /// <summary>获取圣遗物部位中文名。</summary>
        /// <param name="slot">圣遗物部位。</param>
        /// <returns>部位中文名。</returns>
        internal static string GetArtifactSlotText(ArtifactSlot slot) => slot switch
        {
            ArtifactSlot.FlowerOfLife => "生之花",
            ArtifactSlot.PlumeOfDeath => "死之羽",
            ArtifactSlot.SandsOfEon => "时之沙",
            ArtifactSlot.GobletOfEonothem => "空之杯",
            ArtifactSlot.CircletOfLogos => "理之冠",
            _ => "未知部位"
        };

        /// <summary>生成五格星级文本。</summary>
        /// <param name="rarity">稀有度。</param>
        /// <returns>填充和空心星组成的界面文本。</returns>
        internal static string GetRarityStars(ItemRarity rarity)
        {
            int count = Mathf.Clamp((int)rarity, 1, 5);
            return new string('★', count) + new string('☆', 5 - count);
        }

        /// <summary>切换稀有度状态类并清理虚拟化节点的旧状态。</summary>
        /// <param name="element">需要着色的节点。</param>
        /// <param name="prefix">状态类前缀。</param>
        /// <param name="rarity">当前稀有度。</param>
        internal static void EnableRarityClass(VisualElement element, string prefix, ItemRarity? rarity)
        {
            string[] names = { "one", "two", "three", "four", "five" };
            for (int index = 1; index <= 5; index++)
                element.EnableInClassList($"{prefix}--rarity-{names[index - 1]}", rarity.HasValue && (int)rarity.Value == index);
        }

        /// <summary>将 PropertyField 生成的序列化集合配置为可展开、可增删和可重排的 GE 列表。</summary>
        /// <param name="propertyField">绑定集合的 PropertyField。</param>
        /// <param name="emptyText">集合为空时显示的中文提示。</param>
        /// <param name="elementLabel">集合元素的中文标题前缀。</param>
        /// <param name="expandInitially">是否在首次生成控件时展开列表。</param>
        internal static void ConfigureGameplayEffectList(
            PropertyField propertyField,
            string emptyText,
            string elementLabel,
            bool expandInitially)
        {
            if (propertyField == null) return;
            propertyField.Query<ListView>().ForEach(listView =>
            {
                // 隐藏原生 Size 输入框，避免空列表只显示一个“0”；长度由增删按钮和序列化 ListView 管理。
                listView.showBoundCollectionSize = false;
                listView.showFoldoutHeader = true;
                listView.showAddRemoveFooter = true;
                listView.reorderable = true;
                listView.reorderMode = ListViewReorderMode.Simple;
                if (expandInitially)
                {
                    Foldout foldout = listView.Q<Foldout>();
                    if (foldout != null) foldout.SetValueWithoutNotify(true);
                }

                listView.Query<Label>().ForEach(label =>
                {
                    string text = label.text ?? string.Empty;
                    if (text == "List is empty")
                    {
                        label.text = emptyText;
                    }
                    else if (text.StartsWith("Element ", StringComparison.Ordinal) &&
                             int.TryParse(text.Substring("Element ".Length), out int index))
                    {
                        label.text = $"{elementLabel} {index + 1}";
                    }
                });
            });
        }
    }
}
#endif
