#region 单个数据
using RPG.SkillSystem;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{

/// <summary>
/// 提供时间轴上单个内容项的显示数据。
/// </summary>
internal abstract class ItemViewData : IViewData
{
    public string Id { get; }
    public string DisplayName { get; }
    public int StartFrame { get; }
    public int DurationFrames { get; }
    public bool IsResizable { get; }

    /// <summary>
    /// 创建内容项的公共显示投影。
    /// </summary>
    protected ItemViewData(string id, string displayName, int startFrame,
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
internal sealed class AnimationClipViewData : ItemViewData
{
    public AnimationSkillClipConfig Config { get; }

    /// <summary>
    /// 创建动画片段显示投影。
    /// </summary>
    public AnimationClipViewData(AnimationSkillClipConfig config, string displayName)
        : base(config.Id, displayName, config.StartFrame, config.DurationFrames, true) => Config = config;
}

/// <summary>
/// 提供特效片段在时间轴与 Inspector 中显示所需的数据。
/// </summary>
internal sealed class VfxClipViewData : ItemViewData
{
    public VfxSkillClipConfig Config { get; }

    /// <summary>
    /// 创建特效片段显示投影。
    /// </summary>
    public VfxClipViewData(VfxSkillClipConfig config, string displayName)
        : base(config.Id, displayName, config.StartFrame, config.DurationFrames, true) => Config = config;
}

/// <summary>
/// 提供音频片段在时间轴与 Inspector 中显示所需的数据。
/// </summary>
internal sealed class AudioClipViewData : ItemViewData
{
    public AudioSkillClipConfig Config { get; }

    /// <summary>
    /// 创建音频片段显示投影。
    /// </summary>
    public AudioClipViewData(AudioSkillClipConfig config, string displayName)
        : base(config.Id, displayName, config.StartFrame, config.DurationFrames, true) => Config = config;
}

/// <summary>
/// 提供事件标记在时间轴与 Inspector 中显示所需的数据。
/// </summary>
internal sealed class EventMarkerViewData : ItemViewData
{
    public SkillEventMarkerConfig Config { get; }

    /// <summary>
    /// 创建事件标记显示投影。
    /// </summary>
    public EventMarkerViewData(SkillEventMarkerConfig config)
        : base(config.Id, config.DisplayName, config.Frame, 1, false) => Config = config;
}
#endregion
}
