#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.EditorExtensions
{
    /// <summary>提供物品和角色配置编辑器共用的星级文本与状态类呈现。</summary>
    public static class ConfigEditorRarityPresentation
    {
        /// <summary>将一至五星整数格式化为五格填充/空心星。</summary>
        /// <param name="rarity">星级整数。</param>
        /// <returns>五格星级文本。</returns>
        public static string GetRarityStars(int rarity)
        {
            int count = Mathf.Clamp(rarity, 1, 5);
            return new string('★', count) + new string('☆', 5 - count);
        }

        /// <summary>清理并设置配置编辑器的星级状态类。</summary>
        /// <param name="element">需要更新状态的 VisualElement。</param>
        /// <param name="classPrefix">状态类前缀。</param>
        /// <param name="rarity">可为空的星级整数。</param>
        public static void EnableRarityClass(VisualElement element, string classPrefix, int? rarity)
        {
            if (element == null) return;
            string[] names = { "one", "two", "three", "four", "five" };
            for (int index = 1; index <= names.Length; index++)
                element.EnableInClassList($"{classPrefix}--rarity-{names[index - 1]}", rarity.HasValue && rarity.Value == index);
        }
    }
}
#endif
