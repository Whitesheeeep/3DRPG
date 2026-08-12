using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.SkillSystem
{
    /// <summary>
    /// 保存一条摄像机修饰轨道及其 FOV、抖动区间。
    /// </summary>
    [TimelineTrack("摄像机修饰轨道", 50)]
    public sealed class CameraModifierTrackConfig : TrackConfigBase
    {
        [SerializeField] private List<CameraModifierSkillClipConfig> clips = new();

        public IReadOnlyList<CameraModifierSkillClipConfig> Clips => clips;
        public override IReadOnlyList<TimelineItemConfigBase> Items => clips;
    }

    /// <summary>
    /// 标识摄像机修饰数据的具体类别。
    /// </summary>
    public enum CameraModifierType
    {
        Fov,
        Shake
    }

    /// <summary>
    /// 指定修饰与较早请求叠加，或阻断较早请求的同通道贡献。
    /// </summary>
    public enum CameraModifierBlendMode
    {
        Additive,
        Exclusive
    }

    /// <summary>
    /// 提供摄像机修饰配置的集中创建与深复制入口。
    /// </summary>
    [Serializable]
    public abstract class CameraModifierDataBase
    {
        [SerializeField] private CameraModifierBlendMode blendMode;

        public CameraModifierBlendMode BlendMode => blendMode;
        public abstract CameraModifierType Type { get; }

        /// <summary>创建指定叠加模式的配置基类。</summary>
        protected CameraModifierDataBase(CameraModifierBlendMode blendMode = CameraModifierBlendMode.Additive) =>
            this.blendMode = blendMode;

        /// <summary>
        /// 根据类型创建拥有稳定默认值的修饰配置。
        /// </summary>
        public static CameraModifierDataBase Create(CameraModifierType type) => type switch
        {
            CameraModifierType.Fov => new FovCameraModifierData(),
            CameraModifierType.Shake => new ShakeCameraModifierData(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知摄像机修饰类型。")
        };

        /// <summary>
        /// 深复制修饰配置，避免 Inspector 草稿与资产共享可变曲线。
        /// </summary>
        public static CameraModifierDataBase Copy(CameraModifierDataBase source)
        {
            if (source == null) return null;
            return JsonUtility.FromJson(JsonUtility.ToJson(source), source.GetType()) as CameraModifierDataBase;
        }
    }

    /// <summary>
    /// 保存相对于当前运行时基础镜头的目标 FOV 倍率与区间权重曲线。
    /// </summary>
    [Serializable]
    public sealed class FovCameraModifierData : CameraModifierDataBase
    {
        [SerializeField, Min(0.01f)] private float targetScale = 1f;
        [SerializeField] private AnimationCurve weightCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public override CameraModifierType Type => CameraModifierType.Fov;
        public float TargetScale => targetScale;
        public AnimationCurve WeightCurve => weightCurve;

        /// <summary>创建默认 FOV 修饰。</summary>
        public FovCameraModifierData()
        {
        }

        /// <summary>创建 Inspector 提交使用的完整 FOV 快照。</summary>
        public FovCameraModifierData(float targetScale, AnimationCurve weightCurve,
            CameraModifierBlendMode blendMode) : base(blendMode)
        {
            this.targetScale = Mathf.Max(0.01f, targetScale);
            this.weightCurve = weightCurve != null ? new AnimationCurve(weightCurve.keys) :
                AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
    }

    /// <summary>
    /// 保存确定性局部位置与旋转抖动参数。
    /// </summary>
    [Serializable]
    public sealed class ShakeCameraModifierData : CameraModifierDataBase
    {
        [SerializeField] private Vector3 localPositionAmplitude;
        [SerializeField] private Vector3 localRotationAmplitude;
        [SerializeField, Min(0f)] private float frequency = 10f;
        [SerializeField] private int seed;
        [SerializeField] private AnimationCurve intensityCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);

        public override CameraModifierType Type => CameraModifierType.Shake;
        public Vector3 LocalPositionAmplitude => localPositionAmplitude;
        public Vector3 LocalRotationAmplitude => localRotationAmplitude;
        public float Frequency => frequency;
        public int Seed => seed;
        public AnimationCurve IntensityCurve => intensityCurve;

        /// <summary>创建默认 Shake 修饰。</summary>
        public ShakeCameraModifierData()
        {
        }

        /// <summary>创建 Inspector 提交使用的完整 Shake 快照。</summary>
        public ShakeCameraModifierData(Vector3 positionAmplitude, Vector3 rotationAmplitude,
            float frequency, int seed, AnimationCurve intensityCurve,
            CameraModifierBlendMode blendMode) : base(blendMode)
        {
            localPositionAmplitude = positionAmplitude;
            localRotationAmplitude = rotationAmplitude;
            this.frequency = Mathf.Max(0f, frequency);
            this.seed = seed;
            this.intensityCurve = intensityCurve != null ? new AnimationCurve(intensityCurve.keys) :
                AnimationCurve.Linear(0f, 1f, 1f, 1f);
        }
    }

    /// <summary>
    /// 保存一个摄像机修饰的稳定标识、半开帧区间和局部多态参数。
    /// </summary>
    [Serializable]
    public sealed class CameraModifierSkillClipConfig : TimelineItemConfigBase
    {
        [SerializeField, ReadOnly, LabelText("内容 ID")] private string id = string.Empty;
        [SerializeField, Min(0)] private int startFrame;
        [SerializeField, Min(1)] private int durationFrames = 1;
        [SerializeReference] private CameraModifierDataBase modifierData = new FovCameraModifierData();

        public override string Id => id;
        public override int StartFrame => startFrame;
        public override int DurationFrames => durationFrames;
        public CameraModifierDataBase ModifierData => modifierData;
        public CameraModifierType ModifierType => modifierData?.Type ?? CameraModifierType.Fov;
    }
}
