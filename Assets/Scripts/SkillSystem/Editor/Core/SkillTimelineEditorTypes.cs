#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 标识 Document 内显式保存的轨道列表类型，仅用于数据层路由。
    /// </summary>
    internal enum SkillTrackKind { Animation, Vfx, Event }

    /// <summary>
    /// 标识时间轴内容拖拽时采用的移动或裁剪方式。
    /// </summary>
    internal enum SkillTimelineDragMode { Move, ResizeLeft, ResizeRight }

    #region Selection types

    /// <summary>
    /// 表示可跨投影重建恢复的时间轴选择状态。
    /// </summary>
    internal abstract class SkillTimelineSelection : IEquatable<SkillTimelineSelection>
    {
        public static readonly SkillTimelineSelection None = new NoneSkillTimelineSelection();
        public virtual string TrackId => string.Empty;
        public virtual string ItemId => string.Empty;

        // 使用具体选择类型和稳定 GUID 判断是否指向同一个对象。
        public bool Equals(SkillTimelineSelection other) =>
            other != null && GetType() == other.GetType() && TrackId == other.TrackId && ItemId == other.ItemId;

        // 把 object 相等比较转交给强类型实现。
        public override bool Equals(object obj) => obj is SkillTimelineSelection other && Equals(other);

        // 将具体类型与稳定 GUID 一起纳入哈希值。
        public override int GetHashCode() => HashCode.Combine(GetType(), TrackId, ItemId);
    }

    /// <summary>
    /// 表示时间轴当前没有选择对象。
    /// </summary>
    internal sealed class NoneSkillTimelineSelection : SkillTimelineSelection { }

    /// <summary>
    /// 表示动画轨道分组选择。
    /// </summary>
    internal sealed class AnimationGroupTimelineSelection : SkillTimelineSelection { }

    /// <summary>
    /// 表示特效轨道分组选择。
    /// </summary>
    internal sealed class VfxGroupTimelineSelection : SkillTimelineSelection { }

    /// <summary>
    /// 表示事件轨道分组选择。
    /// </summary>
    internal sealed class EventGroupTimelineSelection : SkillTimelineSelection { }

    /// <summary>
    /// 表示通过轨道 GUID 恢复的轨道选择基类。
    /// </summary>
    internal abstract class TrackTimelineSelection : SkillTimelineSelection
    {
        private readonly string trackId;
        public override string TrackId => trackId;

        /// <summary>
        /// 使用稳定轨道 GUID 创建选择。
        /// </summary>
        protected TrackTimelineSelection(string trackId) => this.trackId = trackId ?? string.Empty;
    }

    /// <summary>
    /// 表示动画轨道选择。
    /// </summary>
    internal sealed class AnimationTrackTimelineSelection : TrackTimelineSelection
    {
        /// <summary>
        /// 使用动画轨道 GUID 创建选择。
        /// </summary>
        public AnimationTrackTimelineSelection(string trackId) : base(trackId) { }
    }

    /// <summary>
    /// 表示特效轨道选择。
    /// </summary>
    internal sealed class VfxTrackTimelineSelection : TrackTimelineSelection
    {
        /// <summary>
        /// 使用特效轨道 GUID 创建选择。
        /// </summary>
        public VfxTrackTimelineSelection(string trackId) : base(trackId) { }
    }

    /// <summary>
    /// 表示事件轨道选择。
    /// </summary>
    internal sealed class EventTrackTimelineSelection : TrackTimelineSelection
    {
        /// <summary>
        /// 使用事件轨道 GUID 创建选择。
        /// </summary>
        public EventTrackTimelineSelection(string trackId) : base(trackId) { }
    }

    /// <summary>
    /// 表示通过轨道与内容 GUID 恢复的内容选择基类。
    /// </summary>
    internal abstract class ItemTimelineSelection : SkillTimelineSelection
    {
        private readonly string trackId;
        private readonly string itemId;
        public override string TrackId => trackId;
        public override string ItemId => itemId;

        /// <summary>
        /// 使用稳定轨道与内容 GUID 创建选择。
        /// </summary>
        protected ItemTimelineSelection(string trackId, string itemId)
        {
            this.trackId = trackId ?? string.Empty;
            this.itemId = itemId ?? string.Empty;
        }
    }

    /// <summary>
    /// 表示动画片段选择。
    /// </summary>
    internal sealed class AnimationClipTimelineSelection : ItemTimelineSelection
    {
        /// <summary>
        /// 使用动画轨道与片段 GUID 创建选择。
        /// </summary>
        public AnimationClipTimelineSelection(string trackId, string itemId) : base(trackId, itemId) { }
    }

    /// <summary>
    /// 表示特效片段选择。
    /// </summary>
    internal sealed class VfxClipTimelineSelection : ItemTimelineSelection
    {
        /// <summary>
        /// 使用特效轨道与片段 GUID 创建选择。
        /// </summary>
        public VfxClipTimelineSelection(string trackId, string itemId) : base(trackId, itemId) { }
    }

    /// <summary>
    /// 表示事件标记选择。
    /// </summary>
    internal sealed class EventMarkerTimelineSelection : ItemTimelineSelection
    {
        /// <summary>
        /// 使用事件轨道与标记 GUID 创建选择。
        /// </summary>
        public EventMarkerTimelineSelection(string trackId, string itemId) : base(trackId, itemId) { }
    }

    #endregion

    #region ViewData types

    /// <summary>
    /// 提供所有时间轴内容项渲染所需的公共只读数据。
    /// </summary>
    internal abstract class SkillTimelineItemViewData : IViewData
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int StartFrame { get; }
        public int DurationFrames { get; }
        public bool IsResizable { get; }

        /// <summary>
        /// 创建内容项的公共显示投影。
        /// </summary>
        protected SkillTimelineItemViewData(string id, string displayName, int startFrame,
            int durationFrames, bool isResizable)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            StartFrame = startFrame;
            DurationFrames = durationFrames;
            IsResizable = isResizable;
        }
    }

    /// <summary>
    /// 提供动画片段在时间轴与 Inspector 中显示所需的数据。
    /// </summary>
    internal sealed class AnimationClipTimelineItemViewData : SkillTimelineItemViewData
    {
        public AnimationSkillClipConfig Config { get; }

        /// <summary>
        /// 创建动画片段显示投影。
        /// </summary>
        public AnimationClipTimelineItemViewData(AnimationSkillClipConfig config, string displayName)
            : base(config.Id, displayName, config.StartFrame, config.DurationFrames, true) => Config = config;
    }

    /// <summary>
    /// 提供特效片段在时间轴与 Inspector 中显示所需的数据。
    /// </summary>
    internal sealed class VfxClipTimelineItemViewData : SkillTimelineItemViewData
    {
        public VfxSkillClipConfig Config { get; }

        /// <summary>
        /// 创建特效片段显示投影。
        /// </summary>
        public VfxClipTimelineItemViewData(VfxSkillClipConfig config, string displayName)
            : base(config.Id, displayName, config.StartFrame, config.DurationFrames, true) => Config = config;
    }

    /// <summary>
    /// 提供事件标记在时间轴与 Inspector 中显示所需的数据。
    /// </summary>
    internal sealed class EventMarkerTimelineItemViewData : SkillTimelineItemViewData
    {
        public SkillEventMarkerConfig Config { get; }

        /// <summary>
        /// 创建事件标记显示投影。
        /// </summary>
        public EventMarkerTimelineItemViewData(SkillEventMarkerConfig config)
            : base(config.Id, config.DisplayName, config.Frame, 1, false) => Config = config;
    }

    /// <summary>
    /// 提供所有轨道行渲染所需的公共只读数据。
    /// </summary>
    internal abstract class SkillTimelineTrackViewData : IViewData
    {
        public string Id { get; }
        public string DisplayName { get; }
        public bool Muted { get; }
        public bool Locked { get; }
        public IReadOnlyList<SkillTimelineItemViewData> Items { get; }

        /// <summary>
        /// 创建轨道行的公共显示投影。
        /// </summary>
        protected SkillTimelineTrackViewData(SkillTrackHeader header, IReadOnlyList<SkillTimelineItemViewData> items)
        {
            Id = header.Id;
            DisplayName = header.DisplayName;
            Muted = header.Muted;
            Locked = header.EditorLocked;
            Items = items;
        }
    }

    /// <summary>
    /// 提供动画轨道的显示数据。
    /// </summary>
    internal sealed class AnimationTimelineTrackViewData : SkillTimelineTrackViewData
    {
        public AnimationTrackConfig Config { get; }

        /// <summary>
        /// 创建动画轨道显示投影。
        /// </summary>
        public AnimationTimelineTrackViewData(AnimationTrackConfig config, IReadOnlyList<SkillTimelineItemViewData> items)
            : base(config.Header, items) => Config = config;
    }

    /// <summary>
    /// 提供特效轨道的显示数据。
    /// </summary>
    internal sealed class VfxTimelineTrackViewData : SkillTimelineTrackViewData
    {
        public VfxTrackConfig Config { get; }

        /// <summary>
        /// 创建特效轨道显示投影。
        /// </summary>
        public VfxTimelineTrackViewData(VfxTrackConfig config, IReadOnlyList<SkillTimelineItemViewData> items)
            : base(config.Header, items) => Config = config;
    }

    /// <summary>
    /// 提供事件轨道的显示数据。
    /// </summary>
    internal sealed class EventTimelineTrackViewData : SkillTimelineTrackViewData
    {
        public EventTrackConfig Config { get; }

        /// <summary>
        /// 创建事件轨道显示投影。
        /// </summary>
        public EventTimelineTrackViewData(EventTrackConfig config, IReadOnlyList<SkillTimelineItemViewData> items)
            : base(config.Header, items) => Config = config;
    }

    /// <summary>
    /// 提供轨道分组标题和子轨道列表的公共显示数据。
    /// </summary>
    internal abstract class SkillTimelineGroupViewData : IViewData
    {
        public string DisplayName { get; }
        public IReadOnlyList<SkillTimelineTrackViewData> Tracks { get; }

        /// <summary>
        /// 创建轨道分组的公共显示投影。
        /// </summary>
        protected SkillTimelineGroupViewData(string displayName, IReadOnlyList<SkillTimelineTrackViewData> tracks)
        {
            DisplayName = displayName;
            Tracks = tracks;
        }
    }

    /// <summary>
    /// 提供动画轨道分组显示数据。
    /// </summary>
    internal sealed class AnimationTimelineGroupViewData : SkillTimelineGroupViewData
    {
        /// <summary>
        /// 创建动画分组显示投影。
        /// </summary>
        public AnimationTimelineGroupViewData(IReadOnlyList<SkillTimelineTrackViewData> tracks)
            : base("动画配置", tracks) { }
    }

    /// <summary>
    /// 提供特效轨道分组显示数据。
    /// </summary>
    internal sealed class VfxTimelineGroupViewData : SkillTimelineGroupViewData
    {
        /// <summary>
        /// 创建特效分组显示投影。
        /// </summary>
        public VfxTimelineGroupViewData(IReadOnlyList<SkillTimelineTrackViewData> tracks)
            : base("特效配置", tracks) { }
    }

    /// <summary>
    /// 提供事件轨道分组显示数据。
    /// </summary>
    internal sealed class EventTimelineGroupViewData : SkillTimelineGroupViewData
    {
        /// <summary>
        /// 创建事件分组显示投影。
        /// </summary>
        public EventTimelineGroupViewData(IReadOnlyList<SkillTimelineTrackViewData> tracks)
            : base("事件配置", tracks) { }
    }

    #endregion

    #region Edit results and interaction state

    /// <summary>
    /// 表示一次 Document 编辑操作的成功状态和失败原因。
    /// </summary>
    internal readonly struct TimelineEditResult
    {
        public bool Succeeded { get; }
        public string Message { get; }

        // 统一限制结果只能通过成功或失败工厂创建。
        private TimelineEditResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        /// <summary>
        /// 创建表示编辑成功的结果。
        /// </summary>
        public static TimelineEditResult Success() => new(true, string.Empty);

        /// <summary>
        /// 创建表示编辑失败及其原因的结果。
        /// </summary>
        public static TimelineEditResult Failure(string message) => new(false, message);
    }

    /// <summary>
    /// 描述技能配置内容变更影响的稳定内容标识和变更范围。
    /// </summary>
    internal sealed class SkillTimelineContentChangedEventArgs : EventArgs
    {
        public string ItemId { get; }
        public bool IsBroadChange { get; }

        /// <summary>
        /// 创建技能时间轴内容变更参数。
        /// </summary>
        public SkillTimelineContentChangedEventArgs(string itemId = "", bool isBroadChange = true)
        {
            ItemId = itemId ?? string.Empty;
            IsBroadChange = isBroadChange;
        }
    }

    /// <summary>
    /// 保存一次 Clip 或 Marker 拖拽期间的 UI 草稿状态。
    /// </summary>
    internal sealed class TimelineDragState
    {
        public SkillTimelineDragMode Mode;
        public SkillTimelineTrackViewData Track;
        public SkillTimelineItemViewData Item;
        public int OriginalStartFrame;
        public int OriginalDurationFrames;
        public int DraftStartFrame;
        public int DraftDurationFrames;
        public float PointerStartX;
    }

    #endregion
}
#endif