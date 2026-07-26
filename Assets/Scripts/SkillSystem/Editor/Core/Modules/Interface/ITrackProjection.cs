#if UNITY_EDITOR
using System;
using RPG.SkillSystem;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 负责把一种运行时轨道配置投影为 ViewData，并创建可跨刷新恢复的具体选择状态 SelectionState。
    /// </summary>
    internal interface ITrackProjection
    {
        Type GroupType { get; }
        Type TrackType { get; }
        Type ItemType { get; }
        Type GroupSelectionType { get; }
        Type TrackSelectionType { get; }
        Type ItemSelectionType { get; }

        /// <summary>
        /// 从技能配置创建该模块的完整分组投影。
        /// </summary>
        GroupViewData CreateGroup(SkillConfig config);

        /// <summary>
        /// 创建该模块的分组选择状态。
        /// </summary>
        SelectionState CreateGroupSelection();

        /// <summary>
        /// 使用稳定轨道 GUID 创建轨道选择状态。
        /// </summary>
        SelectionState CreateTrackSelection(string trackId);

        /// <summary>
        /// 使用稳定轨道与内容 GUID 创建内容选择状态。
        /// </summary>
        SelectionState CreateItemSelection(string trackId, string itemId);

        /// <summary>
        /// 使用新内容 GUID 克隆同类型内容选择。
        /// </summary>
        SelectionState CloneItemSelection(SelectionState selection, string itemId);

        /// <summary>
        /// 在该模块分组中查找选择对应的显示投影。
        /// </summary>
        IViewData FindSelection(GroupViewData group, SelectionState selection);
    }
}
#endif
