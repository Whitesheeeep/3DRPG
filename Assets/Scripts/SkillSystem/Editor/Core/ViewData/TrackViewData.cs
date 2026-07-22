#region 一个轨道数据
using System.Collections.Generic;
using RPG.SkillSystem;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{

/// <summary>
/// 提供所有轨道行渲染所需的公共只读数据。
/// </summary>
internal abstract class TrackViewData : IViewData
{
    public string Id { get; }
    public string DisplayName { get; }
    public bool Muted { get; }
    public bool Locked { get; }
    public IReadOnlyList<ItemViewData> Items { get; }

    // 从运行时轨道头复制只读显示字段，不保留可变 Config 或序列化路由引用。
    protected TrackViewData(SkillTrackHeader header, IReadOnlyList<ItemViewData> items)
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
internal sealed class AnimationTrackViewData : TrackViewData
{
    /// <summary>
    /// 创建动画轨道显示投影。
    /// </summary>
    public AnimationTrackViewData(AnimationTrackConfig config, IReadOnlyList<ItemViewData> items)
        : base(config.Header, items)
    {
    }
}

/// <summary>
/// 提供特效轨道的显示数据。
/// </summary>
internal sealed class VfxTrackViewData : TrackViewData
{
    /// <summary>
    /// 创建特效轨道显示投影。
    /// </summary>
    public VfxTrackViewData(VfxTrackConfig config, IReadOnlyList<ItemViewData> items)
        : base(config.Header, items)
    {
    }
}

/// <summary>
/// 提供音频轨道的显示数据。
/// </summary>
internal sealed class AudioTrackViewData : TrackViewData
{
    /// <summary>
    /// 创建音频轨道显示投影。
    /// </summary>
    public AudioTrackViewData(AudioTrackConfig config, IReadOnlyList<ItemViewData> items)
        : base(config.Header, items)
    {
    }
}

/// <summary>
/// 提供事件轨道的显示数据。
/// </summary>
internal sealed class EventTrackViewData : TrackViewData
{
    /// <summary>
    /// 创建事件轨道显示投影。
    /// </summary>
    public EventTrackViewData(EventTrackConfig config, IReadOnlyList<ItemViewData> items)
        : base(config.Header, items)
    {
    }
}
#endregion
}
