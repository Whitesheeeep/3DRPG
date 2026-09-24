#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.EditorExtensions;

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
            ItemCategory.Weapon => "武器",
            ItemCategory.Artifact => "圣遗物",
            ItemCategory.DevelopmentExperienceItem => "养成经验道具",
            ItemCategory.Food => "食物",
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
            DevelopmentExperienceItemDefinition => "养成经验道具定义",
            DevelopmentItemDefinition => "养成道具定义",
            FoodItemDefinition => "食物定义",
            null => "未知定义",
            _ => "物品定义"
        };

        /// <summary>获取养成道具用途中文名。</summary>
        /// <param name="type">养成用途。</param>
        /// <returns>用途中文名。</returns>
        internal static string GetDevelopmentTypeText(DevelopmentItemType types)
        {
            const DevelopmentItemType definedTypes = DevelopmentItemType.CharacterAscension |
                                                      DevelopmentItemType.CharacterTalent |
                                                      DevelopmentItemType.WeaponAscension |
                                                      DevelopmentItemType.WeaponRefinement;
            if (types == DevelopmentItemType.None) return "未配置";
            var values = new System.Collections.Generic.List<string>();
            if ((types & DevelopmentItemType.CharacterAscension) != 0) values.Add("角色突破");
            if ((types & DevelopmentItemType.CharacterTalent) != 0) values.Add("角色天赋");
            if ((types & DevelopmentItemType.WeaponAscension) != 0) values.Add("武器突破");
            if ((types & DevelopmentItemType.WeaponRefinement) != 0) values.Add("武器精炼");
            return (types & ~definedTypes) == DevelopmentItemType.None ? string.Join("、", values) : "未知养成用途";
        }

        /// <summary>获取养成经验适用对象组合的中文名。</summary>
        /// <param name="types">经验适用对象组合。</param>
        /// <returns>稳定排序的中文对象名称。</returns>
        internal static string GetExperienceTypeText(DevelopmentExperienceItemType types)
        {
            if (types == DevelopmentExperienceItemType.None) return "未配置";
            var values = new System.Collections.Generic.List<string>();
            if ((types & DevelopmentExperienceItemType.Character) != 0) values.Add("角色");
            if ((types & DevelopmentExperienceItemType.Weapon) != 0) values.Add("武器");
            if ((types & DevelopmentExperienceItemType.Artifact) != 0) values.Add("圣遗物");
            const DevelopmentExperienceItemType definedTypes = DevelopmentExperienceItemType.Character |
                                                                DevelopmentExperienceItemType.Weapon |
                                                                DevelopmentExperienceItemType.Artifact;
            return (types & ~definedTypes) == DevelopmentExperienceItemType.None ? string.Join("、", values) : "未知经验对象";
        }

        /// <summary>获取定义对应的稳定用途摘要文本。</summary>
        /// <param name="definition">待展示的物品定义。</param>
        /// <returns>用途摘要；非养成定义返回空字符串。</returns>
        internal static string GetDevelopmentUsageText(ItemDefinition definition) => definition switch
        {
            DevelopmentItemDefinition material => GetDevelopmentTypeText(material.DevelopmentTypes),
            DevelopmentExperienceItemDefinition experience => GetExperienceTypeText(experience.ExperienceTypes),
            _ => string.Empty
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
            => ConfigEditorRarityPresentation.GetRarityStars((int)rarity);

        /// <summary>切换稀有度状态类并清理虚拟化节点的旧状态。</summary>
        /// <param name="element">需要着色的节点。</param>
        /// <param name="prefix">状态类前缀。</param>
        /// <param name="rarity">当前稀有度。</param>
        internal static void EnableRarityClass(VisualElement element, string prefix, ItemRarity? rarity)
            => ConfigEditorRarityPresentation.EnableRarityClass(element, prefix, rarity.HasValue ? (int?)rarity.Value : null);

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

        /// <summary>配置 Unity 原生序列化集合的标题、长度框和操作页脚。</summary>
        /// <param name="propertyField">由 SerializedObject 原生绑定的集合 PropertyField。</param>
        /// <param name="headerText">从对应 UXML PropertyField 缓存的标题。</param>
        /// <param name="expandedListViewSet">记录已显示过标题的 ListView，以保留用户折叠状态。</param>
        internal static void ConfigureNativeCollectionHeader(
            PropertyField propertyField,
            string headerText,
            HashSet<ListView> expandedListViewSet)
        {
            if (propertyField == null || string.IsNullOrEmpty(headerText)) return;

            propertyField.Query<ListView>().ForEach(listView =>
            {
                // 只调整 Unity 公开的列表呈现选项，集合创建、绑定和虚拟化仍由原生序列化系统管理。
                if (listView.showBoundCollectionSize) listView.showBoundCollectionSize = false;
                if (!listView.showFoldoutHeader) listView.showFoldoutHeader = true;
                if (!listView.showAddRemoveFooter) listView.showAddRemoveFooter = true;
                if (!listView.reorderable) listView.reorderable = true;
                if (listView.reorderMode != ListViewReorderMode.Simple)
                    listView.reorderMode = ListViewReorderMode.Simple;

                Foldout foldout = listView.Q<Foldout>();
                if (foldout == null) return;

                if (!string.Equals(foldout.text, headerText, StringComparison.Ordinal))
                    foldout.text = headerText;

                // 只在每个新生成的 ListView 首次出现标题时展开；重绑定和布局刷新保留用户选择。
                if (expandedListViewSet.Add(listView))
                    foldout.SetValueWithoutNotify(true);
            });
        }
    }
}
#endif
