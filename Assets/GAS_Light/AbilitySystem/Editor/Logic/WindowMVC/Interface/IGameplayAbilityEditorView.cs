#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>标识 GA Editor 校验问题的显示级别。</summary>
    public enum GameplayAbilityValidationSeverity
    {
        /// <summary>不会阻止运行，但用于解释合法的特殊配置。</summary>
        Info,
        /// <summary>配置违反第一阶段运行时契约。</summary>
        Error
    }

    /// <summary>保存一条可直接由 GA View 渲染的校验结果。</summary>
    public readonly struct GameplayAbilityValidationIssue
    {
        /// <summary>获取问题级别。</summary>
        public GameplayAbilityValidationSeverity Severity { get; }
        /// <summary>获取包含具体字段或索引的说明。</summary>
        public string Message { get; }

        /// <summary>创建一条不可变校验结果。</summary>
        public GameplayAbilityValidationIssue(
            GameplayAbilityValidationSeverity severity,
            string message)
        {
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>保存一次 GA 资产行内重命名意图。</summary>
    public readonly struct GameplayAbilityRenameRequest
    {
        /// <summary>获取需要重命名的 GA。</summary>
        public GameplayAbilityData Ability { get; }
        /// <summary>获取用户提交的新资产名称。</summary>
        public string Name { get; }

        /// <summary>创建携带明确资产身份的重命名请求。</summary>
        public GameplayAbilityRenameRequest(GameplayAbilityData ability, string name)
        {
            Ability = ability;
            Name = name;
        }
    }

    /// <summary>定义 GA Editor 的用户意图和渲染能力，不暴露 UI Toolkit 类型。</summary>
    public interface IGameplayAbilityEditorView : IDisposable
    {
        /// <summary>用户修改搜索文本时触发。</summary>
        event Action<string> SearchChanged;
        /// <summary>用户选择 GA 资产时触发。</summary>
        event Action<GameplayAbilityData> AbilitySelected;
        /// <summary>用户请求创建 GA 时触发。</summary>
        event Action CreateRequested;
        /// <summary>用户请求复制当前 GA 时触发。</summary>
        event Action DuplicateRequested;
        /// <summary>用户请求删除当前 GA 时触发。</summary>
        event Action DeleteRequested;
        /// <summary>用户请求在 Project 中定位 GA 时触发。</summary>
        event Action<GameplayAbilityData> PingRequested;
        /// <summary>用户提交资产重命名时触发。</summary>
        event Action<GameplayAbilityRenameRequest> RenameSubmitted;
        /// <summary>当前 GA 的序列化字段发生变化时触发。</summary>
        event Action AbilityChanged;

        /// <summary>设置搜索文本而不产生新的用户意图。</summary>
        void SetSearch(string search);
        /// <summary>渲染稳定的 GA 资产引用列表并恢复选择。</summary>
        void RenderAbilities(IReadOnlyList<GameplayAbilityData> abilities, GameplayAbilityData selected);
        /// <summary>绑定当前 GA 的 SerializedObject 详情。</summary>
        void BindAbility(GameplayAbilityData ability);
        /// <summary>渲染当前 GA 的 Validation。</summary>
        void RenderValidation(IReadOnlyList<GameplayAbilityValidationIssue> issues);
        /// <summary>渲染各 GA 资产在列表中的最高校验级别。</summary>
        void RenderAbilityValidationStates(
            IReadOnlyDictionary<GameplayAbilityData, GameplayAbilityValidationSeverity> states);
        /// <summary>显示不可恢复的 Editor 操作错误。</summary>
        void ShowError(string message);
        /// <summary>请求用户确认删除当前 GA。</summary>
        bool ConfirmDelete(GameplayAbilityData ability);
        /// <summary>重命名失败后恢复指定资产的行内输入。</summary>
        void RestoreRename(GameplayAbilityData ability, string attemptedName);
    }
}
#endif
