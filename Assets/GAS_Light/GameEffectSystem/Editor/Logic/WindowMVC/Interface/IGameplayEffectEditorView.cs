#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 GE Editor 的用户意图与渲染边界，不暴露 UI Toolkit 类型。</summary>
    public interface IGameplayEffectEditorView : IDisposable
    {
        #region 用户意图事件

        /// <summary>搜索文本变化时触发。</summary>
        event Action<string> SearchChanged;
        /// <summary>用户选择 GE 资产时触发。</summary>
        event Action<GameplayEffectData> EffectSelectionChanged;
        /// <summary>用户请求在 Project 窗口定位指定 GE 资产时触发。</summary>
        event Action<GameplayEffectData> PingEffectRequested;
        /// <summary>用户提交 GE 资产新名称时触发。</summary>
        event Action<GameplayEffectRenameRequest> RenameEffectSubmitted;
        /// <summary>用户选择新资产路径后触发。</summary>
        event Action<string> CreateEffectRequested;
        /// <summary>请求复制当前 GE 时触发。</summary>
        event Action DuplicateEffectRequested;
        /// <summary>请求删除当前 GE 时触发。</summary>
        event Action DeleteEffectRequested;
        /// <summary>原生绑定已提交序列化字段时触发。</summary>
        event Action EffectSerializedChanged;
        /// <summary>Modifier 选择索引变化时触发。</summary>
        event Action<int> ModifierSelectionChanged;
        /// <summary>请求创建指定派生类型的 Modifier。</summary>
        event Action<Type> AddModifierRequested;
        /// <summary>请求删除当前 Modifier。</summary>
        event Action RemoveModifierRequested;
        /// <summary>请求移动 Modifier 索引。</summary>
        event Action<GameplayEffectModifierMoveRequest> MoveModifierRequested;
        /// <summary>请求重新校验当前 GE。</summary>
        event Action ValidateRequested;
        /// <summary>请求烘焙当前 GE 的 Curve 预览。</summary>
        event Action BakeCurvePreviewRequested;
        /// <summary>请求打开当前 GE 的通用烘焙结果窗口。</summary>
        event Action ViewBakedResultRequested;

        #endregion

        #region 状态与渲染

        /// <summary>同步搜索文本。</summary>
        /// <param name="search">当前搜索内容。</param>
        void SetSearch(string search);
        /// <summary>渲染经过搜索和排序的 GE Model 引用。</summary>
        /// <param name="effects">当前可见 GE 资产。</param>
        /// <param name="selected">当前选中资产。</param>
        void RenderEffects(IReadOnlyList<GameplayEffectData> effects, GameplayEffectData selected);
        /// <summary>渲染 GE 资产列表的最高校验严重程度；Info 与无问题资产不包含在字典中。</summary>
        /// <param name="states">以 GE 资产引用为键的 Error/Warning 状态只读字典。</param>
        void RenderEffectValidationStates(
            IReadOnlyDictionary<GameplayEffectData, GameplayEffectValidationSeverity> states);
        /// <summary>重命名失败后恢复指定资产的行内输入和焦点。</summary>
        /// <param name="effect">需要继续编辑的 GE 资产。</param>
        /// <param name="attemptedName">上一次被拒绝的输入。</param>
        void RestoreEffectRename(GameplayEffectData effect, string attemptedName);
        /// <summary>设置用于把稳定 AttributeId 解析为作者名称的 Registry。</summary>
        /// <param name="registry">当前明确的 Registry；null 表示无法解析名称。</param>
        /// <param name="unavailableReason">Registry 不明确时显示在 Tooltip 中的原因。</param>
        void SetAttributeRegistry(
            GameplayAttributeRegistry registry,
            string unavailableReason);
        /// <summary>建立或解除当前 GE 的原生序列化绑定。</summary>
        /// <param name="effect">当前 GE；null 表示清空详情。</param>
        void BindEffect(GameplayEffectData effect);
        /// <summary>只切换当前 GE 中选定 Modifier 的 managed-reference 详情绑定。</summary>
        /// <param name="selectedModifierIndex">目标 Modifier 索引；负数表示清空详情。</param>
        void BindModifier(int selectedModifierIndex);
        /// <summary>渲染 Modifier Model 引用和可创建类型。</summary>
        /// <param name="modifiers">当前 GE Modifier。</param>
        /// <param name="selectedIndex">当前选中索引。</param>
        /// <param name="availableTypes">可创建的派生类型。</param>
        void RenderModifiers(
            IReadOnlyList<GameplayEffectModifier> modifiers,
            int selectedIndex,
            IReadOnlyList<Type> availableTypes);
        /// <summary>刷新当前策略字段的显隐状态。</summary>
        /// <param name="effect">当前 GE；null 时禁用详情。</param>
        void RefreshPolicyVisibility(GameplayEffectData effect);
        /// <summary>渲染当前校验问题。</summary>
        /// <param name="issues">错误、警告和提示列表。</param>
        void RenderValidation(IReadOnlyList<GameplayEffectValidationIssue> issues);
        /// <summary>显示确认对话框。</summary>
        /// <param name="title">标题。</param>
        /// <param name="message">确认内容。</param>
        /// <returns>用户确认时返回 true。</returns>
        bool Confirm(string title, string message);
        /// <summary>显示明确的 Editor 错误。</summary>
        /// <param name="title">标题。</param>
        /// <param name="message">错误内容。</param>
        void ShowError(string title, string message);

        #endregion
    }

    /// <summary>描述一次 GE 资产重命名提交。</summary>
    public readonly struct GameplayEffectRenameRequest
    {
        /// <summary>创建 GE 资产重命名请求。</summary>
        /// <param name="effect">需要重命名的 GE 资产。</param>
        /// <param name="name">用户提交的新名称。</param>
        public GameplayEffectRenameRequest(GameplayEffectData effect, string name)
        {
            Effect = effect;
            Name = name ?? string.Empty;
        }

        /// <summary>获取需要重命名的 GE 资产。</summary>
        public GameplayEffectData Effect { get; }
        /// <summary>获取用户提交的新名称。</summary>
        public string Name { get; }
    }

    /// <summary>描述一次 Modifier 列表索引移动意图。</summary>
    public readonly struct GameplayEffectModifierMoveRequest
    {
        /// <summary>创建 Modifier 移动请求。</summary>
        /// <param name="fromIndex">移动前索引。</param>
        /// <param name="toIndex">移动后索引。</param>
        public GameplayEffectModifierMoveRequest(int fromIndex, int toIndex)
        {
            FromIndex = fromIndex;
            ToIndex = toIndex;
        }

        /// <summary>获取移动前索引。</summary>
        public int FromIndex { get; }
        /// <summary>获取移动后索引。</summary>
        public int ToIndex { get; }
    }

    /// <summary>定义 GE Editor 校验问题的严重程度。</summary>
    public enum GameplayEffectValidationSeverity
    {
        /// <summary>仅用于说明当前行为。</summary>
        Info,
        /// <summary>配置可以运行，但作者需要注意。</summary>
        Warning,
        /// <summary>配置不符合当前 GE 运行契约。</summary>
        Error
    }

    /// <summary>保存一条可直接渲染的 GE Editor 校验结果。</summary>
    public readonly struct GameplayEffectValidationIssue
    {
        /// <summary>创建校验结果。</summary>
        /// <param name="severity">严重程度。</param>
        /// <param name="message">包含具体资产或字段的说明。</param>
        public GameplayEffectValidationIssue(
            GameplayEffectValidationSeverity severity,
            string message)
        {
            Severity = severity;
            Message = message ?? string.Empty;
        }

        /// <summary>获取严重程度。</summary>
        public GameplayEffectValidationSeverity Severity { get; }
        /// <summary>获取问题说明。</summary>
        public string Message { get; }
    }
}
#endif
