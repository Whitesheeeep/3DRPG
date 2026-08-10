#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>描述 Cue 编辑器校验问题的严重程度。</summary>
    public enum GameplayCueValidationSeverity
    {
        /// <summary>仅提供配置提示。</summary>
        Info,
        /// <summary>配置可以运行但需要作者注意。</summary>
        Warning,
        /// <summary>配置无法可靠运行。</summary>
        Error
    }

    /// <summary>描述一个 CueData 或 Database 校验问题。</summary>
    public readonly struct GameplayCueValidationIssue
    {
        /// <summary>创建校验问题。</summary>
        /// <param name="severity">问题严重程度。</param>
        /// <param name="cue">关联的 CueData，可以为空表示数据库问题。</param>
        /// <param name="message">问题说明。</param>
        public GameplayCueValidationIssue(
            GameplayCueValidationSeverity severity,
            GameplayCueData cue,
            string message)
        {
            Severity = severity;
            Cue = cue;
            Message = message ?? string.Empty;
        }

        /// <summary>获取问题严重程度。</summary>
        public GameplayCueValidationSeverity Severity { get; }

        /// <summary>获取关联的 CueData。</summary>
        public GameplayCueData Cue { get; }

        /// <summary>获取问题文本。</summary>
        public string Message { get; }
    }

    /// <summary>描述 CueData 行内重命名请求。</summary>
    public readonly struct GameplayCueRenameRequest
    {
        /// <summary>创建重命名请求。</summary>
        /// <param name="cue">要重命名的 CueData。</param>
        /// <param name="name">提交的新名称。</param>
        public GameplayCueRenameRequest(GameplayCueData cue, string name)
        {
            Cue = cue;
            Name = name ?? string.Empty;
        }

        /// <summary>获取目标 CueData。</summary>
        public GameplayCueData Cue { get; }

        /// <summary>获取提交名称。</summary>
        public string Name { get; }
    }

    /// <summary>隔离 Cue 编辑器 View 与 UI Toolkit 控件实现。</summary>
    public interface IGameplayCueEditorView : IDisposable
    {
        #region 用户意图事件

        /// <summary>数据库选择发生变化时触发。</summary>
        event Action<GameplayCueDatabase> DatabaseChanged;
        /// <summary>搜索文本发生变化时触发。</summary>
        event Action<string> SearchChanged;
        /// <summary>Cue 选择发生变化时触发。</summary>
        event Action<GameplayCueData> CueSelectionChanged;
        /// <summary>请求创建 CueData 并注册到当前数据库。</summary>
        event Action CreateCueRequested;
        /// <summary>请求添加已有 CueData。</summary>
        event Action AddExistingCueRequested;
        /// <summary>请求复制当前 CueData。</summary>
        event Action DuplicateCueRequested;
        /// <summary>请求从数据库移除 CueData。</summary>
        event Action<GameplayCueData> RemoveFromDatabaseRequested;
        /// <summary>请求将 CueData 移入回收站。</summary>
        event Action<GameplayCueData> DeleteCueRequested;
        /// <summary>请求定位 CueData 资产。</summary>
        event Action<GameplayCueData> PingCueRequested;
        /// <summary>请求定位 Fallback Prefab。</summary>
        event Action<GameplayCueData> PingPrefabRequested;
        /// <summary>提交 CueData 资产名称。</summary>
        event Action<GameplayCueRenameRequest> RenameCueSubmitted;
        /// <summary>CueData 的序列化字段完成写回时触发。</summary>
        event Action CueSerializedChanged;
        /// <summary>请求立即刷新和校验。</summary>
        event Action RefreshRequested;
        /// <summary>请求创建新的 Cue Database。</summary>
        event Action CreateDatabaseRequested;

        #endregion

        #region 状态渲染

        /// <summary>设置当前数据库对象字段。</summary>
        /// <param name="database">当前数据库。</param>
        void SetDatabase(GameplayCueDatabase database);
        /// <summary>设置用于解析 CueTag 显示名称的 Tag Database。</summary>
        /// <param name="database">当前 Tag Database；为空时只能显示稳定 ID。</param>
        void SetTagDatabase(GameplayTagDatabase database);
        /// <summary>设置搜索框文本。</summary>
        /// <param name="search">搜索文本。</param>
        void SetSearch(string search);
        /// <summary>渲染当前数据库中的 Cue 引用列表。</summary>
        /// <param name="cues">可见 Cue 引用。</param>
        /// <param name="selected">当前选中 Cue。</param>
        void RenderCues(IReadOnlyList<GameplayCueData> cues, GameplayCueData selected);
        /// <summary>渲染每个 Cue 的最高校验严重程度。</summary>
        /// <param name="states">只读校验状态字典。</param>
        void RenderValidationStates(
            IReadOnlyDictionary<GameplayCueData, GameplayCueValidationSeverity> states);
        /// <summary>绑定当前 CueData 的原生序列化详情。</summary>
        /// <param name="cue">当前 CueData，空表示清空详情。</param>
        void BindCue(GameplayCueData cue);
        /// <summary>局部刷新指定 Cue 的列表行和非绑定派生信息，不重建 PropertyField。</summary>
        /// <param name="cue">完成字段写回的 CueData。</param>
        void RefreshCuePresentation(GameplayCueData cue);
        /// <summary>渲染当前 Cue 的校验问题。</summary>
        /// <param name="issues">校验问题列表。</param>
        void RenderValidation(IReadOnlyList<GameplayCueValidationIssue> issues);
        /// <summary>重命名失败后恢复输入框并继续编辑。</summary>
        /// <param name="cue">重命名失败的 CueData。</param>
        /// <param name="attemptedName">失败的输入内容。</param>
        void RestoreCueRename(GameplayCueData cue, string attemptedName);
        /// <summary>显示错误对话框。</summary>
        /// <param name="title">对话框标题。</param>
        /// <param name="message">错误内容。</param>
        void ShowError(string title, string message);
        /// <summary>显示确认对话框。</summary>
        /// <param name="title">标题。</param>
        /// <param name="message">内容。</param>
        /// <returns>用户确认时返回 true。</returns>
        bool Confirm(string title, string message);

        #endregion
    }
}
#endif
