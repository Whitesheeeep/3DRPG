using System.Collections.Generic;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{

#region 一个轨道分组数据
/// <summary>
/// 提供轨道分组标题和子轨道列表的公共显示数据。
/// </summary>
internal abstract class GroupViewData : IViewData
{
    public string DisplayName { get; }
    public IReadOnlyList<TrackViewData> Tracks { get; }

    /// <summary>
    /// 创建轨道分组的公共显示投影。
    /// </summary>
    protected GroupViewData(string displayName, IReadOnlyList<TrackViewData> tracks)
    {
        DisplayName = displayName;
        Tracks = tracks;
    }
}

/// <summary>
/// 提供动画轨道分组显示数据。
/// </summary>
internal sealed class AnimationGroupViewData : GroupViewData
{
    /// <summary>
    /// 创建动画分组显示投影。
    /// </summary>
    public AnimationGroupViewData(IReadOnlyList<TrackViewData> tracks)
        : base("动画配置", tracks)
    {
    }
}

/// <summary>
/// 提供特效轨道分组显示数据。
/// </summary>
internal sealed class VfxGroupViewData : GroupViewData
{
    /// <summary>
    /// 创建特效分组显示投影。
    /// </summary>
    public VfxGroupViewData(IReadOnlyList<TrackViewData> tracks)
        : base("特效配置", tracks)
    {
    }
}

/// <summary>
/// 提供音频轨道分组显示数据。
/// </summary>
internal sealed class AudioGroupViewData : GroupViewData
{
    /// <summary>
    /// 创建音频分组显示投影。
    /// </summary>
    public AudioGroupViewData(IReadOnlyList<TrackViewData> tracks)
        : base("音频配置", tracks)
    {
    }
}

/// <summary>
/// 提供事件轨道分组显示数据。
/// </summary>
internal sealed class EventGroupViewData : GroupViewData
{
    /// <summary>
    /// 创建事件分组显示投影。
    /// </summary>
    public EventGroupViewData(IReadOnlyList<TrackViewData> tracks)
        : base("事件配置", tracks)
    {
    }
}
#endregion
}
