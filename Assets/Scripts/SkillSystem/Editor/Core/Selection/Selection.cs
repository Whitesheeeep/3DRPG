#region Selection types
using System;

namespace RPG.SkillSystem.Editor
{

/// <summary>
/// 表示可跨投影重建恢复的时间轴选择状态。
/// </summary>
internal abstract class SelectionState : IEquatable<SelectionState>
{
    public static readonly SelectionState None = new NoneSelection();
    /// <summary>
    /// 当前选择所属 Track 的稳定 GUID，用于投影重建及 Undo/Redo 后重新定位轨道；分组或空选择返回空字符串。
    /// </summary>
    public virtual string TrackId => string.Empty;
    /// <summary>
    /// 当前选择所属 TrackItem 的稳定 GUID，用于投影重建及 Undo/Redo 后恢复具体内容；非 Item 选择返回空字符串。
    /// </summary>
    public virtual string ItemId => string.Empty;

    /// <summary>
    /// 使用具体选择类型和稳定 GUID 判断是否指向同一个对象。
    /// </summary>
    /// <param name="other">待比较的时间轴选择。</param>
    /// <returns>具体类型、TrackId 与 ItemId 均一致时返回 true。</returns>
    public bool Equals(SelectionState other) =>
        other != null && GetType() == other.GetType() && TrackId == other.TrackId && ItemId == other.ItemId;

    /// <summary>
    /// 把 object 相等比较转交给强类型选择实现。
    /// </summary>
    /// <param name="obj">待比较的对象。</param>
    /// <returns>对象表示同一个稳定选择时返回 true。</returns>
    public override bool Equals(object obj) => obj is SelectionState other && Equals(other);

    /// <summary>
    /// 将具体类型与稳定 GUID 一起纳入哈希值。
    /// </summary>
    /// <returns>可与选择相等语义配套使用的哈希值。</returns>
    public override int GetHashCode() => HashCode.Combine(GetType(), TrackId, ItemId);
}

/// <summary>
/// 表示时间轴当前没有选择对象。
/// </summary>
internal sealed class NoneSelection : SelectionState
{
}

/// <summary>
/// 表示动画轨道分组选择。
/// </summary>
internal sealed class AnimationGroupSelection : SelectionState
{
}

/// <summary>
/// 表示特效轨道分组选择。
/// </summary>
internal sealed class VfxGroupSelection : SelectionState
{
}

/// <summary>
/// 表示音频轨道分组选择。
/// </summary>
internal sealed class AudioGroupSelection : SelectionState
{
}

/// <summary>
/// 表示事件轨道分组选择。
/// </summary>
internal sealed class EventGroupSelection : SelectionState
{
}

/// <summary>
/// 表示通过轨道 GUID 恢复的轨道选择基类。
/// </summary>
internal abstract class TrackSelection : SelectionState
{
    private readonly string trackId;
    /// <summary>
    /// 被选择轨道的稳定 GUID，不使用可随重排变化的列表索引。
    /// </summary>
    public override string TrackId => trackId;

    // 使用稳定轨道 GUID 创建选择，禁止保存可随重排变化的列表索引。
    protected TrackSelection(string trackId) => this.trackId = trackId ?? string.Empty;
}

/// <summary>
/// 表示动画轨道选择。
/// </summary>
internal sealed class AnimationTrackSelection : TrackSelection
{
    /// <summary>
    /// 使用动画轨道 GUID 创建选择。
    /// </summary>
    public AnimationTrackSelection(string trackId) : base(trackId)
    {
    }
}

/// <summary>
/// 表示特效轨道选择。
/// </summary>
internal sealed class VfxTrackSelection : TrackSelection
{
    /// <summary>
    /// 使用特效轨道 GUID 创建选择。
    /// </summary>
    public VfxTrackSelection(string trackId) : base(trackId)
    {
    }
}

/// <summary>
/// 表示音频轨道选择。
/// </summary>
internal sealed class AudioTrackSelection : TrackSelection
{
    /// <summary>
    /// 使用音频轨道 GUID 创建选择。
    /// </summary>
    public AudioTrackSelection(string trackId) : base(trackId)
    {
    }
}

/// <summary>
/// 表示事件轨道选择。
/// </summary>
internal sealed class EventTrackSelection : TrackSelection
{
    /// <summary>
    /// 使用事件轨道 GUID 创建选择。
    /// </summary>
    public EventTrackSelection(string trackId) : base(trackId)
    {
    }
}
#endregion

#region 单个 TrackItem 选项
/// <summary>
/// 表示通过轨道与内容 GUID 恢复的内容选择基类。
/// </summary>
internal abstract class ItemSelection : SelectionState
{
    private readonly string trackId;
    private readonly string itemId;
    /// <summary>
    /// TrackItem 所属轨道的稳定 GUID，用于先定位强类型轨道集合。
    /// </summary>
    public override string TrackId => trackId;
    /// <summary>
    /// 被选择 TrackItem 自身的稳定 GUID，用于在轨道内恢复 Clip 或 Marker。
    /// </summary>
    public override string ItemId => itemId;

    // 使用稳定轨道与内容 GUID 创建选择，保证投影重建后仍可恢复。
    protected ItemSelection(string trackId, string itemId)
    {
        this.trackId = trackId ?? string.Empty;
        this.itemId = itemId ?? string.Empty;
    }
}

/// <summary>
/// 表示动画片段选择。
/// </summary>
internal sealed class AnimationClipSelection : ItemSelection
{
    /// <summary>
    /// 使用动画轨道与片段 GUID 创建选择。
    /// </summary>
    public AnimationClipSelection(string trackId, string itemId) : base(trackId, itemId)
    {
    }
}

/// <summary>
/// 表示特效片段选择。
/// </summary>
internal sealed class VfxClipSelection : ItemSelection
{
    /// <summary>
    /// 使用特效轨道与片段 GUID 创建选择。
    /// </summary>
    public VfxClipSelection(string trackId, string itemId) : base(trackId, itemId)
    {
    }
}

/// <summary>
/// 表示音频片段选择。
/// </summary>
internal sealed class AudioClipSelection : ItemSelection
{
    /// <summary>
    /// 使用音频轨道与片段 GUID 创建选择。
    /// </summary>
    public AudioClipSelection(string trackId, string itemId) : base(trackId, itemId)
    {
    }
}

/// <summary>
/// 表示事件标记选择。
/// </summary>
internal sealed class EventMarkerSelection : ItemSelection
{
    /// <summary>
    /// 使用事件轨道与标记 GUID 创建选择。
    /// </summary>
    public EventMarkerSelection(string trackId, string itemId) : base(trackId, itemId)
    {
    }
}
#endregion

}
