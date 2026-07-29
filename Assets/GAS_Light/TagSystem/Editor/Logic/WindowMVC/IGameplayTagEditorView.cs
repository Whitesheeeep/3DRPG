#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>定义 Gameplay Tag Editor 的用户意图与展示能力，不暴露任何 UI Toolkit 控件。</summary>
    public interface IGameplayTagEditorView : IDisposable
    {
        /// <summary>数据库选择发生变化时触发。</summary>
        event Action<GameplayTagDatabase> DatabaseChanged;
        /// <summary>搜索文本发生变化时触发。</summary>
        event Action<string> SearchChanged;
        /// <summary>Tree 单选节点发生变化时触发，空字符串表示取消选择。</summary>
        event Action<string> SelectionChanged;
        /// <summary>请求创建根节点时触发。</summary>
        event Action AddRootRequested;
        /// <summary>请求创建当前节点的子节点时触发。</summary>
        event Action AddChildRequested;
        /// <summary>请求删除当前节点时触发。</summary>
        event Action DeleteRequested;
        /// <summary>请求烘焙数据库时触发。</summary>
        event Action BakeRequested;
        /// <summary>请求指定 Guid 的节点进入行内重命名时触发。</summary>
        event Action<string> RenameRequested;
        /// <summary>取消行内重命名时触发。</summary>
        event Action<string> RenameCancelled;
        /// <summary>提交名称编辑时触发。</summary>
        event Action<GameplayTagTextEditRequest> NameEditRequested;
        /// <summary>提交完整路径编辑时触发。</summary>
        event Action<GameplayTagTextEditRequest> PathEditRequested;
        /// <summary>提交描述编辑时触发。</summary>
        event Action<GameplayTagTextEditRequest> DescriptionEditRequested;
        /// <summary>提交拖放移动时触发。</summary>
        event Action<GameplayTagMoveRequest> MoveRequested;

        /// <summary>同步数据库选择，不触发用户意图事件。</summary>
        void SetDatabase(GameplayTagDatabase database);
        /// <summary>同步搜索文本，不触发用户意图事件。</summary>
        void SetSearchText(string search);
        /// <summary>渲染树并恢复选择、展开和行内重命名状态。</summary>
        void RenderTree(IReadOnlyList<GameplayTagTreeViewData> roots, string selectedGuid,
            IReadOnlyCollection<string> expandedGuids, string renamingGuid);
        /// <summary>渲染当前节点详情。</summary>
        void RenderDetails(GameplayTagDetailsViewData details);
        /// <summary>渲染作者数据校验结果。</summary>
        void RenderValidation(IReadOnlyList<GameplayTagValidationIssue> issues);
        /// <summary>渲染数据库烘焙状态。</summary>
        void RenderBakeState(GameplayTagBakeViewState state);
        /// <summary>取得当前可见树中已展开节点的 Guid。</summary>
        IReadOnlyCollection<string> GetExpandedNodeGuids();
        /// <summary>显示操作失败提示。</summary>
        void ShowError(string title, string message);
        /// <summary>显示删除确认并返回用户选择。</summary>
        bool ConfirmDelete(string message);
        /// <summary>显示烘焙结果。</summary>
        void ShowBakeResult(string message);
    }

    /// <summary>描述针对指定节点提交的文本编辑意图。</summary>
    public readonly struct GameplayTagTextEditRequest
    {
        /// <summary>获取目标作者节点 Guid。</summary>
        public string NodeGuid { get; }
        /// <summary>获取用户提交的文本。</summary>
        public string Value { get; }

        /// <summary>创建文本编辑请求。</summary>
        public GameplayTagTextEditRequest(string nodeGuid, string value)
        {
            NodeGuid = nodeGuid ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }

    /// <summary>描述拖放完成后的节点移动意图。</summary>
    public readonly struct GameplayTagMoveRequest
    {
        /// <summary>获取待移动节点 Guid。</summary>
        public string NodeGuid { get; }
        /// <summary>获取目标父节点 Guid；空字符串表示根级。</summary>
        public string NewParentGuid { get; }

        /// <summary>创建节点移动请求。</summary>
        public GameplayTagMoveRequest(string nodeGuid, string newParentGuid)
        {
            NodeGuid = nodeGuid ?? string.Empty;
            NewParentGuid = newParentGuid ?? string.Empty;
        }
    }

    /// <summary>提供不依赖 UI Toolkit 的只读树节点投影。</summary>
    public sealed class GameplayTagTreeViewData
    {
        /// <summary>获取作者节点 Guid。</summary>
        public string Guid { get; }
        /// <summary>获取局部名称。</summary>
        public string Name { get; }
        /// <summary>获取完整路径。</summary>
        public string Path { get; }
        /// <summary>获取父节点 Guid。</summary>
        public string ParentGuid { get; }
        /// <summary>获取节点深度；根节点为 0。</summary>
        public int Depth { get; }
        /// <summary>获取按名称排序的子节点。</summary>
        public IReadOnlyList<GameplayTagTreeViewData> Children { get; }

        /// <summary>创建只读树节点投影。</summary>
        public GameplayTagTreeViewData(string guid, string name, string path, string parentGuid, int depth,
            IReadOnlyList<GameplayTagTreeViewData> children)
        {
            Guid = guid ?? string.Empty;
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            ParentGuid = parentGuid ?? string.Empty;
            Depth = depth;
            Children = children ?? Array.Empty<GameplayTagTreeViewData>();
        }
    }

    /// <summary>提供当前选择的只读详情投影。</summary>
    public readonly struct GameplayTagDetailsViewData
    {
        /// <summary>获取是否存在有效选择。</summary>
        public bool HasSelection { get; }
        /// <summary>获取作者节点 Guid。</summary>
        public string Guid { get; }
        /// <summary>获取局部名称。</summary>
        public string Name { get; }
        /// <summary>获取完整路径。</summary>
        public string Path { get; }
        /// <summary>获取已烘焙 TagId；未烘焙时为 -1。</summary>
        public int TagId { get; }
        /// <summary>获取描述。</summary>
        public string Description { get; }

        /// <summary>创建详情投影。</summary>
        public GameplayTagDetailsViewData(bool hasSelection, string guid, string name, string path,
            int tagId, string description)
        {
            HasSelection = hasSelection;
            Guid = guid ?? string.Empty;
            Name = name ?? string.Empty;
            Path = path ?? string.Empty;
            TagId = tagId;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>描述 Gameplay Tag 数据库在窗口中的烘焙状态。</summary>
    public enum GameplayTagBakeViewState
    {
        /// <summary>尚未选择数据库。</summary>
        NoDatabase,
        /// <summary>作者数据存在未烘焙修改。</summary>
        BakeRequired,
        /// <summary>运行时数据与作者数据一致。</summary>
        Baked
    }
}
#endif
