using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.SkillSystem
{
    #region Skill asset

    /// <summary>
    /// 保存技能时间轴的运行时帧参数和各类强类型轨道数据。
    /// </summary>
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "RPG/Skill/Skill Config")]
    public sealed class SkillConfig : ScriptableObject
    {
        [SerializeField, ReadOnly, LabelText("技能 ID")] private string id = string.Empty;
        [SerializeField, Min(1), LabelText("FPS")] private int frameRate = 30;
        [SerializeField, Min(1), LabelText("总帧")] private int durationFrames = 1;
        [SerializeField, LabelText("动画配置")] private List<AnimationTrackConfig> animationTracks = new();
        [SerializeField, LabelText("攻击检测")] private List<AttackDetectionTrackConfig> attackDetectionTracks = new();
        [SerializeField, LabelText("特效配置")] private List<VfxTrackConfig> vfxTracks = new();
        [SerializeField, LabelText("音频配置")] private List<AudioTrackConfig> audioTracks = new();
        [SerializeField, LabelText("事件配置")] private List<EventTrackConfig> eventTracks = new();

        public string Id => id;
        public int FrameRate => frameRate;
        public int DurationFrames => durationFrames;
        public IReadOnlyList<AnimationTrackConfig> AnimationTracks => animationTracks;
        public IReadOnlyList<AttackDetectionTrackConfig> AttackDetectionTracks => attackDetectionTracks;
        public IReadOnlyList<VfxTrackConfig> VfxTracks => vfxTracks;
        public IReadOnlyList<AudioTrackConfig> AudioTracks => audioTracks;
        public IReadOnlyList<EventTrackConfig> EventTracks => eventTracks;
    }

    #endregion

    #region Track configurations

    /// <summary>
    /// 保存轨道的公共头部信息，包括唯一 ID、显示名称和静音状态。
    /// </summary>
    [Serializable]
    public sealed class SkillTrackHeader
    {
        [SerializeField, ReadOnly, LabelText("轨道 ID")] private string id = string.Empty;
        [SerializeField, LabelText("静音")] private bool muted;

#if UNITY_EDITOR
        [SerializeField, LabelText("轨道名称")] private string displayName = "新轨道";
        [SerializeField, LabelText("锁定")] private bool editorLocked;
        [SerializeField, LabelText("编辑颜色")] private Color editorColor;
#endif

        public string Id => id;
        public bool Muted => muted;

#if UNITY_EDITOR
        public string DisplayName => displayName;
        public bool EditorLocked => editorLocked;
        public Color EditorColor => editorColor;
#endif
    }

    /// <summary>
    /// 保存一条动画轨道的公共轨道头和动画片段列表。
    /// </summary>
    [Serializable]
    public sealed class AnimationTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<AnimationSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<AnimationSkillClipConfig> Clips => clips;
    }

    /// <summary>
    /// 保存一条攻击检测轨道的公共轨道头和检测片段列表。
    /// </summary>
    [Serializable]
    public sealed class AttackDetectionTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<AttackDetectionSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<AttackDetectionSkillClipConfig> Clips => clips;
    }

    /// <summary>
    /// 保存一条特效轨道的公共轨道头和特效片段列表。
    /// </summary>
    [Serializable]
    public sealed class VfxTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<VfxSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<VfxSkillClipConfig> Clips => clips;
    }

    /// <summary>
    /// 保存一条音频轨道的公共轨道头和音频片段列表。
    /// </summary>
    [Serializable]
    public sealed class AudioTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<AudioSkillClipConfig> clips = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<AudioSkillClipConfig> Clips => clips;
    }

    /// <summary>
    /// 保存一条事件轨道的公共轨道头和单帧事件标记列表。
    /// </summary>
    [Serializable]
    public sealed class EventTrackConfig
    {
        [SerializeField] private SkillTrackHeader header = new();
        [SerializeField] private List<SkillEventMarkerConfig> markers = new();

        public SkillTrackHeader Header => header;
        public IReadOnlyList<SkillEventMarkerConfig> Markers => markers;
    }
    #endregion

    #region Timeline content

    /// <summary>
    /// 保存动画素材、半开帧区间、源偏移和播放速度。
    /// </summary>
    [Serializable]
    public sealed class AnimationSkillClipConfig
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField] private AnimationClip animationClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Min(0)] private int sourceStartFrame;
        [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

        public string Id => id;
        public AnimationClip AnimationClip => animationClip;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public int SourceStartFrame => sourceStartFrame;
        public float PlaybackSpeed => playbackSpeed;
    }

    /// <summary>
    /// 指定特效在生成后是否持续跟随运行时传入的基准对象。
    /// </summary>
    public enum VfxFollowMode
    {
        FollowBinding,
        KeepWorldPosition
    }

    /// <summary>
    /// 指定特效到达 Clip 结束边界时的生命周期策略。
    /// </summary>
    public enum VfxStopMode
    {
        ReturnToPoolAtEnd,
        StopEmissionAtEnd,
        KeepAlive
    }

    /// <summary>
    /// 保存特效 Prefab、半开帧区间、局部变换和生命周期策略。
    /// </summary>
    [Serializable]
    public sealed class VfxSkillClipConfig
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField] private GameObject prefab;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private Vector3 localScale = Vector3.one;
        [SerializeField] private VfxFollowMode followMode;
        [SerializeField] private VfxStopMode stopMode;

        public string Id => id;
        public GameObject Prefab => prefab;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;
        public Vector3 LocalScale => localScale;
        public VfxFollowMode FollowMode => followMode;
        public VfxStopMode StopMode => stopMode;
    }

    /// <summary>
    /// 标识攻击检测片段当前使用的具体检测数据类型。
    /// </summary>
    public enum AttackDetectionType
    {
        None = 0,
        Box = 1,
        Sphere = 2,
        Capsule = 3,
        Sector = 4,
        WeaponTrace = 5
    }

    /// <summary>
    /// 标识胶囊体的局部轴向。
    /// </summary>
    public enum CapsuleAxis
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    /// <summary>
    /// 提供攻击检测具体配置的多态序列化基类和集中工厂。
    /// </summary>
    [Serializable]
    public abstract class AttackDetectionDataBase
    {
        public abstract AttackDetectionType Type { get; }

        /// <summary>
        /// 创建指定检测类型的默认配置；新增类型时必须同步扩展此工厂。
        /// </summary>
        /// <param name="type">需要创建的攻击检测类型。</param>
        /// <returns>对应的独立配置实例；None 返回空引用。</returns>
        /// <exception cref="ArgumentOutOfRangeException">检测类型尚未注册到工厂。</exception>
        public static AttackDetectionDataBase Create(AttackDetectionType type)
        {
            return type switch
            {
                AttackDetectionType.None => null,
                AttackDetectionType.Box => new BoxAttackDetectionData(),
                AttackDetectionType.Sphere => new SphereAttackDetectionData(),
                AttackDetectionType.Capsule => new CapsuleAttackDetectionData(),
                AttackDetectionType.Sector => new SectorAttackDetectionData(),
                AttackDetectionType.WeaponTrace => new WeaponTraceAttackDetectionData(),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未注册的攻击检测配置类型。")
            };
        }

        /// <summary>
        /// 创建现有检测配置的独立副本，避免复制 Clip 后共享 managed reference。
        /// </summary>
        /// <param name="source">需要复制的检测配置。</param>
        /// <returns>字段值相同但引用独立的配置实例；空输入返回空引用。</returns>
        /// <exception cref="ArgumentOutOfRangeException">具体配置类型尚未注册到复制工厂。</exception>
        public static AttackDetectionDataBase Copy(AttackDetectionDataBase source)
        {
            return source switch
            {
                null => null,
                BoxAttackDetectionData value => new BoxAttackDetectionData(
                    value.LocalPosition, value.LocalEulerAngles, value.Size),
                SphereAttackDetectionData value => new SphereAttackDetectionData(
                    value.LocalPosition, value.Radius),
                CapsuleAttackDetectionData value => new CapsuleAttackDetectionData(
                    value.LocalPosition, value.LocalEulerAngles, value.Radius, value.Height, value.Axis),
                SectorAttackDetectionData value => new SectorAttackDetectionData(
                    value.LocalPosition, value.LocalEulerAngles, value.InnerRadius,
                    value.OuterRadius, value.Angle, value.Height),
                WeaponTraceAttackDetectionData value => new WeaponTraceAttackDetectionData(value.SamplePointCount),
                _ => throw new ArgumentOutOfRangeException(nameof(source), source.GetType(),
                    "未注册的攻击检测配置具体类型。")
            };
        }
    }

    /// <summary>
    /// 保存具有局部位置和局部旋转的攻击体积公共数据。
    /// </summary>
    [Serializable]
    public abstract class VolumeAttackDetectionDataBase : AttackDetectionDataBase
    {
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;

        public Vector3 LocalPosition => localPosition;
        public Vector3 LocalEulerAngles => localEulerAngles;

        // 初始化所有体积检测共享的局部空间数据。
        protected VolumeAttackDetectionDataBase(Vector3 localPosition, Vector3 localEulerAngles)
        {
            this.localPosition = localPosition;
            this.localEulerAngles = localEulerAngles;
        }
    }

    /// <summary>
    /// 保存立方体检测的局部变换和完整尺寸。
    /// </summary>
    [Serializable]
    public sealed class BoxAttackDetectionData : VolumeAttackDetectionDataBase
    {
        [SerializeField] private Vector3 size = Vector3.one;

        public override AttackDetectionType Type => AttackDetectionType.Box;
        public Vector3 Size => size;

        /// <summary>
        /// 创建使用默认局部变换和单位尺寸的立方体检测配置。
        /// </summary>
        public BoxAttackDetectionData() : base(Vector3.zero, Vector3.zero)
        {
        }

        /// <summary>
        /// 创建具有完整局部空间参数的立方体检测配置。
        /// </summary>
        /// <param name="localPosition">相对运行时基准的局部位置。</param>
        /// <param name="localEulerAngles">相对运行时基准的局部欧拉角。</param>
        /// <param name="size">立方体完整尺寸。</param>
        public BoxAttackDetectionData(Vector3 localPosition, Vector3 localEulerAngles, Vector3 size)
            : base(localPosition, localEulerAngles)
        {
            this.size = size;
        }
    }

    /// <summary>
    /// 保存球形检测的局部位置和半径。
    /// </summary>
    [Serializable]
    public sealed class SphereAttackDetectionData : VolumeAttackDetectionDataBase
    {
        [SerializeField, Min(0.001f)] private float radius = 0.5f;

        public override AttackDetectionType Type => AttackDetectionType.Sphere;
        public float Radius => radius;

        /// <summary>
        /// 创建位于局部原点、使用默认半径的球形检测配置。
        /// </summary>
        public SphereAttackDetectionData() : base(Vector3.zero, Vector3.zero)
        {
        }

        /// <summary>
        /// 创建具有指定局部位置和半径的球形检测配置。
        /// </summary>
        /// <param name="localPosition">相对运行时基准的局部位置。</param>
        /// <param name="radius">球形半径。</param>
        public SphereAttackDetectionData(Vector3 localPosition, float radius)
            : base(localPosition, Vector3.zero)
        {
            this.radius = radius;
        }
    }

    /// <summary>
    /// 保存胶囊体检测的局部变换、半径、高度和轴向。
    /// </summary>
    [Serializable]
    public sealed class CapsuleAttackDetectionData : VolumeAttackDetectionDataBase
    {
        [SerializeField, Min(0.001f)] private float radius = 0.5f;
        [SerializeField, Min(0.001f)] private float height = 2f;
        [SerializeField] private CapsuleAxis axis = CapsuleAxis.Y;

        public override AttackDetectionType Type => AttackDetectionType.Capsule;
        public float Radius => radius;
        public float Height => height;
        public CapsuleAxis Axis => axis;

        /// <summary>
        /// 创建使用默认局部变换和尺寸的胶囊体检测配置。
        /// </summary>
        public CapsuleAttackDetectionData() : base(Vector3.zero, Vector3.zero)
        {
        }

        /// <summary>
        /// 创建具有完整局部空间参数的胶囊体检测配置。
        /// </summary>
        /// <param name="localPosition">相对运行时基准的局部位置。</param>
        /// <param name="localEulerAngles">相对运行时基准的局部欧拉角。</param>
        /// <param name="radius">胶囊体半径。</param>
        /// <param name="height">胶囊体总高度。</param>
        /// <param name="axis">胶囊体在局部空间中的轴向。</param>
        public CapsuleAttackDetectionData(Vector3 localPosition, Vector3 localEulerAngles,
            float radius, float height, CapsuleAxis axis) : base(localPosition, localEulerAngles)
        {
            this.radius = radius;
            this.height = height;
            this.axis = axis;
        }
    }

    /// <summary>
    /// 保存水平扇形柱体检测的局部变换、半径、角度和高度。
    /// </summary>
    [Serializable]
    public sealed class SectorAttackDetectionData : VolumeAttackDetectionDataBase
    {
        [SerializeField, Min(0f)] private float innerRadius;
        [SerializeField, Min(0.001f)] private float outerRadius = 2f;
        [SerializeField, Range(0.01f, 360f)] private float angle = 90f;
        [SerializeField, Min(0.001f)] private float height = 1f;

        public override AttackDetectionType Type => AttackDetectionType.Sector;
        public float InnerRadius => innerRadius;
        public float OuterRadius => outerRadius;
        public float Angle => angle;
        public float Height => height;

        /// <summary>
        /// 创建使用默认局部变换和范围的扇形检测配置。
        /// </summary>
        public SectorAttackDetectionData() : base(Vector3.zero, Vector3.zero)
        {
        }

        /// <summary>
        /// 创建具有完整局部空间参数的扇形检测配置。
        /// </summary>
        /// <param name="localPosition">相对运行时基准的局部位置。</param>
        /// <param name="localEulerAngles">相对运行时基准的局部欧拉角。</param>
        /// <param name="innerRadius">扇形内半径。</param>
        /// <param name="outerRadius">扇形外半径。</param>
        /// <param name="angle">扇形角度，单位为度。</param>
        /// <param name="height">扇形柱体高度。</param>
        public SectorAttackDetectionData(Vector3 localPosition, Vector3 localEulerAngles,
            float innerRadius, float outerRadius, float angle, float height)
            : base(localPosition, localEulerAngles)
        {
            this.innerRadius = innerRadius;
            this.outerRadius = outerRadius;
            this.angle = angle;
            this.height = height;
        }
    }

    /// <summary>
    /// 保存武器轨迹检测沿刀刃插值的采样点数量。
    /// </summary>
    [Serializable]
    public sealed class WeaponTraceAttackDetectionData : AttackDetectionDataBase
    {
        [SerializeField, Range(2, 16)] private int samplePointCount = 4;

        public override AttackDetectionType Type => AttackDetectionType.WeaponTrace;
        public int SamplePointCount => samplePointCount;

        /// <summary>
        /// 创建使用默认采样点数量的武器轨迹检测配置。
        /// </summary>
        public WeaponTraceAttackDetectionData()
        {
        }

        /// <summary>
        /// 创建具有指定采样点数量的武器轨迹检测配置。
        /// </summary>
        /// <param name="samplePointCount">刀根到刀尖之间的插值采样点数量。</param>
        public WeaponTraceAttackDetectionData(int samplePointCount)
        {
            this.samplePointCount = samplePointCount;
        }
    }

    /// <summary>
    /// 保存攻击检测的半开帧区间、采样间隔和当前具体检测配置。
    /// </summary>
    [Serializable]
    public sealed class AttackDetectionSkillClipConfig
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Min(1)] private int sampleIntervalFrames = 1;
        [SerializeReference] private AttackDetectionDataBase detectionData =
            AttackDetectionDataBase.Create(AttackDetectionType.Box);

        public string Id => id;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public int SampleIntervalFrames => sampleIntervalFrames;
        public AttackDetectionDataBase DetectionData => detectionData;
        public AttackDetectionType DetectionType => detectionData?.Type ?? AttackDetectionType.None;
    }

    /// <summary>
    /// 保存音频素材、半开帧区间、音量和播放音调等运行时数据。
    /// </summary>
    [Serializable]
    public sealed class AudioSkillClipConfig
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [FormerlySerializedAs("clip"), SerializeField] private AudioClip audioClip;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [FormerlySerializedAs("playbackSpeed"), SerializeField, Min(0.01f)] private float pitch = 1f;

        public string Id => id;
        public AudioClip AudioClip => audioClip;
        public int StartFrame => startFrame;
        public int DurationFrames => durationFrames;
        public int EndFrame => startFrame + durationFrames;
        public float Volume => volume;
        public float Pitch => pitch;
    }

    /// <summary>
    /// 保存单帧事件的稳定标识、类型名、显示名称和参数文本。
    /// </summary>
    [Serializable]
    public sealed class SkillEventMarkerConfig
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int frame;
        [SerializeField] private string eventTypeName = string.Empty;
        [SerializeField] private string displayName = "事件";
        [SerializeField, TextArea] private string parameterText = string.Empty;

        public string Id => id;
        public int Frame => frame;
        public string EventTypeName => eventTypeName;
        public string DisplayName => displayName;
        public string ParameterText => parameterText;
    }

    #endregion
}
