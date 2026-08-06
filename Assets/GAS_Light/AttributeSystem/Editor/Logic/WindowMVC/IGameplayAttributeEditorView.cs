#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 Attribute Editor 用户意图和渲染能力，不暴露 UI Toolkit 控件。</summary>
    public interface IGameplayAttributeEditorView : IDisposable
    {
        #region 用户意图事件

        /// <summary>用户切换子页面时触发。</summary>
        event Action<GameplayAttributeEditorPage> PageChanged;
        /// <summary>用户选择 Registry 时触发。</summary>
        event Action<GameplayAttributeRegistry> RegistryChanged;
        /// <summary>用户选择 AttributeSet 时触发。</summary>
        event Action<GameplayAttributeSet> SetChanged;
        /// <summary>搜索文本变化时触发。</summary>
        event Action<string> SearchChanged;
        /// <summary>Spec 选择变化时触发。</summary>
        event Action<string> SpecSelectionChanged;
        /// <summary>Definition 选择变化时触发。</summary>
        event Action<int> DefinitionSelectionChanged;
        /// <summary>请求创建 Spec 时触发。</summary>
        event Action CreateSpecRequested;
        /// <summary>请求删除 Spec 时触发。</summary>
        event Action DeleteSpecRequested;
        /// <summary>提交 Spec 名称时触发。</summary>
        event Action<string> SpecNameSubmitted;
        /// <summary>提交 Spec 说明时触发。</summary>
        event Action<string> SpecDescriptionSubmitted;
        /// <summary>请求 Bake Specs 时触发。</summary>
        event Action BakeRequested;
        /// <summary>请求创建 AttributeSet 资产时触发。</summary>
        event Action CreateSetRequested;
        /// <summary>请求添加 Definition 时触发。</summary>
        event Action<GameplayAttributeType> AddDefinitionRequested;
        /// <summary>请求删除选中 Definition 时触发。</summary>
        event Action DeleteDefinitionRequested;
        /// <summary>提交 Definition 修改时触发。</summary>
        event Action<GameplayAttributeDefinitionEditRequest> DefinitionSubmitted;

        #endregion

        #region 状态同步与渲染

        /// <summary>同步当前子页面。</summary>
        /// <param name="page">目标子页面。</param>
        void SetPage(GameplayAttributeEditorPage page);
        /// <summary>同步当前 Registry。</summary>
        /// <param name="registry">当前 Registry。</param>
        void SetRegistry(GameplayAttributeRegistry registry);
        /// <summary>同步当前 AttributeSet。</summary>
        /// <param name="set">当前 AttributeSet。</param>
        void SetAttributeSet(GameplayAttributeSet set);
        /// <summary>同步搜索文本。</summary>
        /// <param name="search">当前搜索文本。</param>
        void SetSearch(string search);
        /// <summary>渲染经过筛选和排序的 Spec Model。</summary>
        /// <param name="specs">当前可见 Editor Node。</param>
        /// <param name="selectedGuid">当前选择 Guid。</param>
        void RenderSpecs(
            IReadOnlyList<GameplayAttributeEditorNode> specs,
            string selectedGuid);
        /// <summary>渲染选中 Spec；null 表示没有选择。</summary>
        /// <param name="node">当前 Editor Node。</param>
        void RenderSpecDetails(GameplayAttributeEditorNode node);
        /// <summary>渲染经过筛选和排序的 Definition Model。</summary>
        /// <param name="definitions">当前可见运行时 Definition 配置。</param>
        /// <param name="selectedAttributeId">当前选择 AttributeId。</param>
        void RenderDefinitions(
            IReadOnlyList<GameplayAttributeDefinition> definitions,
            int selectedAttributeId);
        /// <summary>渲染选中 Definition 和可选择的已烘焙 Spec。</summary>
        /// <param name="definition">当前 Definition；null 表示没有选择。</param>
        /// <param name="selectableNodes">当前 Registry 中已烘焙的 Editor Node。</param>
        void RenderDefinitionDetails(
            GameplayAttributeDefinition definition,
            IReadOnlyList<GameplayAttributeEditorNode> selectableNodes);
        /// <summary>渲染校验与 Bake 状态文本。</summary>
        /// <param name="message">状态或问题列表。</param>
        /// <param name="isError">是否使用错误视觉状态。</param>
        void RenderStatus(string message, bool isError);
        /// <summary>显示确认对话框。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="message">确认内容。</param>
        /// <returns>用户确认时返回 true。</returns>
        bool Confirm(string title, string message);
        /// <summary>显示错误对话框。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="message">错误内容。</param>
        void ShowError(string title, string message);
        /// <summary>显示操作结果。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="message">结果内容。</param>
        void ShowResult(string title, string message);

        #endregion
    }

    /// <summary>描述 View 向 Controller 提交的一次完整 Definition 编辑意图。</summary>
    public readonly struct GameplayAttributeDefinitionEditRequest
    {
        #region 构造与属性

        /// <summary>创建 Definition 编辑请求。</summary>
        /// <param name="originalAttributeId">提交前用于稳定定位的 AttributeId。</param>
        /// <param name="attribute">提交后的 Attribute。</param>
        /// <param name="type">提交后的作者分类。</param>
        /// <param name="defaultValue">默认值。</param>
        /// <param name="minValue">固定最小值。</param>
        /// <param name="maxValue">固定最大值。</param>
        public GameplayAttributeDefinitionEditRequest(
            int originalAttributeId,
            GameplayAttribute attribute,
            GameplayAttributeType type,
            float defaultValue,
            float minValue,
            float maxValue)
        {
            OriginalAttributeId = originalAttributeId;
            Attribute = attribute;
            Type = type;
            DefaultValue = defaultValue;
            MinValue = minValue;
            MaxValue = maxValue;
        }

        /// <summary>获取提交前用于稳定定位的 AttributeId。</summary>
        public int OriginalAttributeId { get; }
        /// <summary>获取新 Attribute。</summary>
        public GameplayAttribute Attribute { get; }
        /// <summary>获取新分类。</summary>
        public GameplayAttributeType Type { get; }
        /// <summary>获取新默认值。</summary>
        public float DefaultValue { get; }
        /// <summary>获取新最小值。</summary>
        public float MinValue { get; }
        /// <summary>获取新最大值。</summary>
        public float MaxValue { get; }

        #endregion
    }
}
#endif
