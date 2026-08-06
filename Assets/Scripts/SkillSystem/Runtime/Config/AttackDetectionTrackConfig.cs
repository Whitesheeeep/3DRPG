using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条攻击检测轨道的公共轨道数据和检测片段列表。
    /// </summary>
    [TimelineTrack("攻击检测轨道", 10)]
    public sealed class AttackDetectionTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<AttackDetectionSkillClipConfig> clips = new();

        public IReadOnlyList<AttackDetectionSkillClipConfig> Clips => clips;
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;
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
    public sealed class AttackDetectionSkillClipConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeField, Min(1)] private int sampleIntervalFrames = 1;
        [SerializeReference] private AttackDetectionDataBase detectionData =
            AttackDetectionDataBase.Create(AttackDetectionType.Box);

        public override string Id => id;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;
        public int SampleIntervalFrames => sampleIntervalFrames;
        public AttackDetectionDataBase DetectionData => detectionData;
        public AttackDetectionType DetectionType => detectionData?.Type ?? AttackDetectionType.None;
    }
}