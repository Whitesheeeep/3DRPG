#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 缓存并实例化时间轴 UXML 模板，只负责元素创建，不判断具体轨道或内容类型。
    /// </summary>
    internal sealed class ElementFactory
    {
        #region 常量与字段

        private const string TemplateRoot =
            "Assets/Scripts/SkillSystem/Editor/SkillTimelineEditorWindow/Templates/";
        private readonly Dictionary<string, VisualTreeAsset> templates = new();

        #endregion

        #region 公共行元素

        /// <summary>
        /// 创建普通轨道左侧标题行。
        /// </summary>
        public VisualElement CreateTrackHeader() =>
            Instantiate("TimelineRow/SkillTimelineTrackHeaderRow.uxml", "TrackHeaderRoot");

        /// <summary>
        /// 创建右侧轨道背景行。
        /// </summary>
        public VisualElement CreateLaneBackground() =>
            Instantiate("TimelineRow/SkillTimelineLaneBackgroundRow.uxml", "LaneBackgroundRoot");

        /// <summary>
        /// 创建右侧内容承载行。
        /// </summary>
        public VisualElement CreateLaneItemRow() =>
            Instantiate("Item/SkillTimelineLaneItemRow.uxml", "LaneItemRoot");

        /// <summary>
        /// 从统一 Item 模板创建元素，并添加具体轨道负责的外观样式类。
        /// </summary>
        /// <param name="styleClass">具体 Item 类型对应的稳定 USS class。</param>
        /// <returns>已经应用通用结构与类型样式类的 Item 根元素。</returns>
        public VisualElement CreateItem(string styleClass)
        {
            if (string.IsNullOrWhiteSpace(styleClass))
                throw new ArgumentException("Item 样式类不能为空。", nameof(styleClass));

            // 所有轨道复用同一视觉树，具体 Factory 只决定外观 class。
            VisualElement element = Instantiate("Item/SkillTimelineItem.uxml", "TimelineItemRoot");
            element.AddToClassList(styleClass);
            return element;
        }

        #endregion

        #region 模板实例化

        // 加载并缓存模板，随后移除 TemplateContainer 包装以保持 USS 行布局稳定。
        internal VisualElement Instantiate(string fileName, string rootName)
        {
            if (!templates.TryGetValue(fileName, out VisualTreeAsset template))
            {
                string path = TemplateRoot + fileName;
                template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
                if (template == null) throw new InvalidOperationException($"缺少时间轴 UXML 模板：{path}");
                templates[fileName] = template;
            }

            TemplateContainer container = template.Instantiate();
            VisualElement element = container.Q<VisualElement>(rootName);
            if (element == null) throw new InvalidOperationException($"模板 {fileName} 缺少节点 {rootName}。");
            element.RemoveFromHierarchy();
            return element;
        }

        #endregion
    }
}
#endif
