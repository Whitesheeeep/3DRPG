#if UNITY_EDITOR
using System;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 创建窗口私有的动画轨道预览处理器。
    /// </summary>
    internal sealed class AnimationPreviewFactory : ITrackPreviewFactory
    {
        /// <summary>
        /// 每次调用都创建独立处理器，避免多个 EditorWindow 共享缓存和角色状态。
        /// </summary>
        public ITrackPreviewHandler Create() => new AnimationPreviewHandler();
    }

    /// <summary>
    /// 保存当前帧选中的动画配置、素材以及已换算的源动画时间。
    /// </summary>
    internal readonly struct AnimationSample
    {
        public AnimationSkillClipConfig ClipConfig { get; }
        public AnimationClip Clip { get; }
        public float RawSourceTime { get; }
        public float SampleTime { get; }
        public bool IsLooping { get; }

        // 创建一次确定的动画绝对时间采样描述。
        internal AnimationSample(AnimationSkillClipConfig clipConfig, AnimationClip clip,
            float rawSourceTime, float sampleTime, bool isLooping)
        {
            ClipConfig = clipConfig;
            Clip = clip;
            RawSourceTime = rawSourceTime;
            SampleTime = sampleTime;
            IsLooping = isLooping;
        }
    }

    /// <summary>
    /// 按轨道顺序选择当前帧最上方的未静音动画，并统一换算源动画时间。
    /// </summary>
    internal sealed class AnimationClipSelector
    {
        // 查找当前帧的权威动画；空素材不会阻止继续查找更下方轨道。
        internal bool TrySelect(SkillConfig config, int frame, out AnimationSample sample)
        {
            sample = default;
            if (config == null) return false;
            foreach (AnimationTrackConfig track in config.AnimationTracks)
            {
                if (track?.Header == null || track.Header.Muted) continue;
                foreach (AnimationSkillClipConfig clipConfig in track.Clips)
                {
                    if (clipConfig == null || frame < clipConfig.StartFrame || frame >= clipConfig.EndFrame)
                        continue;
                    AnimationClip clip = clipConfig.AnimationClip;
                    if (clip == null) continue;
                    sample = CreateSample(config, clipConfig, clip, frame);
                    return true;
                }
            }

            return false;
        }

        // 使用技能 FPS 计算经过时间，源偏移则使用 AnimationClip 自身采样率。
        private static AnimationSample CreateSample(SkillConfig config,
            AnimationSkillClipConfig clipConfig, AnimationClip clip, int frame)
        {
            float sourceFrameRate = clip.frameRate > Mathf.Epsilon ? clip.frameRate : config.FrameRate;
            float sourceStartTime = clipConfig.SourceStartFrame / Mathf.Max(Mathf.Epsilon, sourceFrameRate);
            float elapsedTime = (frame - clipConfig.StartFrame) / (float)Mathf.Max(1, config.FrameRate);
            float rawTime = sourceStartTime + elapsedTime * Mathf.Max(0.01f, clipConfig.PlaybackSpeed);
            bool looping = clip.isLooping && clip.length > Mathf.Epsilon;
            float sampleTime = looping
                ? Mathf.Repeat(rawTime, clip.length)
                : Mathf.Clamp(rawTime, 0f, Mathf.Max(0f, clip.length));
            return new AnimationSample(clipConfig, clip, rawTime, sampleTime, looping);
        }
    }

    /// <summary>
    /// 缓存技能每一帧相对角色初始位置的绝对 Root Motion，保证跳帧与顺序播放一致。
    /// </summary>
    internal sealed class RootMotionCache
    {
        #region 缓存状态

        private readonly AnimationClipSelector selector;
        private RootPose[] framePoses;
        private SkillConfig cachedConfig;
        private PreviewActorInstance cachedActor;
        private bool valid;

        #endregion

        #region 生命周期与查询

        // 使用与姿势预览相同的动画选择器创建根运动缓存。
        internal RootMotionCache(AnimationClipSelector selector)
        {
            this.selector = selector ?? throw new ArgumentNullException(nameof(selector));
        }

        // 标记所有绝对帧结果失效，下次查询时从第 0 帧重新构建。
        internal void Invalidate()
        {
            valid = false;
            framePoses = null;
            cachedConfig = null;
            cachedActor = null;
        }

        // 返回指定帧的绝对根姿态，配置或角色变化时自动重建缓存。
        internal RootPose GetPose(SkillConfig config, PreviewActorInstance actor, int frame)
        {
            if (!valid || !ReferenceEquals(cachedConfig, config) || !ReferenceEquals(cachedActor, actor))
                Build(config, actor);
            if (framePoses == null || framePoses.Length == 0) return RootPose.Identity;
            return framePoses[Mathf.Clamp(frame, 0, framePoses.Length - 1)];
        }

        #endregion

        #region 缓存构建

        // 从第 0 帧顺序累计有效动画的根曲线，Clip 切换时以新 Clip 首帧作为段落基准。
        private void Build(SkillConfig config, PreviewActorInstance actor)
        {
            cachedConfig = config;
            cachedActor = actor;
            int frameCount = Mathf.Max(1, config?.DurationFrames ?? 1);
            framePoses = new RootPose[frameCount];
            RootPose accumulated = RootPose.Identity;
            AnimationSample previous = default;
            bool hasPrevious = false;

            for (int frame = 0; frame < frameCount; frame++)
            {
                if (!selector.TrySelect(config, frame, out AnimationSample current))
                {
                    hasPrevious = false;
                    framePoses[frame] = accumulated;
                    continue;
                }

                bool sameClip = hasPrevious && IsSameClip(previous, current);
                if (sameClip && current.Clip.hasRootCurves)
                {
                    RootDelta delta = CalculateDelta(actor, current.Clip,
                        previous.RawSourceTime, current.RawSourceTime, current.IsLooping);
                    accumulated = accumulated.Apply(delta);
                }

                framePoses[frame] = accumulated;
                previous = current;
                hasPrevious = true;
            }

            valid = true;
        }

        // 稳定 GUID 优先标识同一个时间轴 Clip，空 GUID 时退回对象引用比较。
        private static bool IsSameClip(AnimationSample left, AnimationSample right)
        {
            string leftId = left.ClipConfig?.Id;
            string rightId = right.ClipConfig?.Id;
            return !string.IsNullOrEmpty(leftId) && !string.IsNullOrEmpty(rightId)
                ? leftId == rightId
                : ReferenceEquals(left.ClipConfig, right.ClipConfig);
        }

        // 计算两个未折叠源时间之间的根运动，并正确组合跨越的完整循环。
        private static RootDelta CalculateDelta(PreviewActorInstance actor, AnimationClip clip,
            float fromRawTime, float toRawTime, bool looping)
        {
            float length = clip.length;
            if (!looping || length <= Mathf.Epsilon)
            {
                float from = Mathf.Clamp(fromRawTime, 0f, Mathf.Max(0f, length));
                float to = Mathf.Clamp(toRawTime, 0f, Mathf.Max(0f, length));
                return SampleDelta(actor, clip, from, to);
            }

            int fromLoop = Mathf.FloorToInt(fromRawTime / length);
            int toLoop = Mathf.FloorToInt(toRawTime / length);
            float fromTime = Mathf.Repeat(fromRawTime, length);
            float toTime = Mathf.Repeat(toRawTime, length);
            if (fromLoop == toLoop) return SampleDelta(actor, clip, fromTime, toTime);

            RootDelta result = SampleDelta(actor, clip, fromTime, length);
            RootDelta fullLoop = SampleDelta(actor, clip, 0f, length);
            for (int loop = fromLoop + 1; loop < toLoop; loop++)
                result = result.Then(fullLoop);
            return result.Then(SampleDelta(actor, clip, 0f, toTime));
        }

        // 把动画根曲线的两个绝对姿态转换为可连续组合的局部增量。
        private static RootDelta SampleDelta(PreviewActorInstance actor, AnimationClip clip,
            float fromTime, float toTime)
        {
            RootPose from = actor.SampleRootCurve(clip, fromTime);
            RootPose to = actor.SampleRootCurve(clip, toTime);
            return new RootDelta(
                to.Position - from.Position,
                Quaternion.Inverse(from.Rotation) * to.Rotation);
        }

        #endregion
    }

    /// <summary>
    /// 使用 Animancer 采样动画与 Root Motion，并为表现轨道提供任意帧 Marker 世界姿态。
    /// </summary>
    internal sealed class AnimationPreviewHandler : ITrackPreviewHandler, IPreviewActorPoseProvider,
        IPreviewActorBindingPoseProvider
    {
        #region 依赖与状态

        private readonly AnimationClipSelector selector = new();
        private readonly RootMotionCache rootMotion;
        private bool disposed;

        #endregion

        #region 生命周期

        // 创建动画选择器和绝对帧 Root Motion 缓存。
        internal AnimationPreviewHandler()
        {
            rootMotion = new RootMotionCache(selector);
        }

        /// <summary>
        /// 清理动画预览缓存；角色和 AnimancerGraph 由 PreviewActorInstance 统一释放。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
        }

        #endregion

        #region 预览操作

        /// <summary>
        /// 配置内容变化后使绝对帧 Root Motion 缓存失效。
        /// </summary>
        public void Invalidate() => rootMotion.Invalidate();

        /// <summary>
        /// 选择最上方有效动画，先采样姿势，再应用从第 0 帧累计出的绝对根姿态。
        /// </summary>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed || context.Actor == null || context.Config == null) return;
            SampleActorAtFrame(context.Config, context.Actor, context.Frame, context.ApplyRootMotion);
        }

        /// <summary>
        /// 复用动画绝对帧缓存，为 VFX 等模块返回指定帧的角色根姿态。
        /// </summary>
        public RootPose GetRootPose(SkillConfig config, PreviewActorInstance actor, int frame) =>
            disposed || config == null || actor == null
                ? RootPose.Identity
                : rootMotion.GetPose(config, actor, frame);

        /// <summary>
        /// 临时采样目标帧的完整动画姿势并读取 Marker 世界矩阵，随后恢复播放头当前帧。
        /// </summary>
        public bool TryGetBindingWorldMatrix(SkillConfig config, PreviewActorInstance actor,
            MarkerKey markerKey, int frame, int restoreFrame, bool applyRootMotion,
            out Matrix4x4 matrix)
        {
            matrix = default;
            if (disposed || config == null || actor == null) return false;

            bool found;
            try
            {
                SampleActorAtFrame(config, actor, frame, applyRootMotion);
                found = actor.TryGetMarker(markerKey, out Transform marker, out _);
                if (found) matrix = marker.localToWorldMatrix;
            }
            finally
            {
                SampleActorAtFrame(config, actor, restoreFrame, applyRootMotion);
            }

            return found;
        }

        // 使用与正常预览完全一致的动画选择和 Root Motion 规则定位任意整数帧。
        private void SampleActorAtFrame(SkillConfig config, PreviewActorInstance actor,
            int frame, bool applyRootMotion)
        {
            RootPose rootPose = applyRootMotion
                ? rootMotion.GetPose(config, actor, frame)
                : RootPose.Identity;
            if (selector.TrySelect(config, frame, out AnimationSample sample))
                actor.SamplePose(sample);
            else
                actor.RestoreBindPose();
            actor.ApplyAbsoluteRootPose(rootPose);
        }

        /// <summary>
        /// 动画使用绝对帧采样，停止时保留当前显示姿势。
        /// </summary>
        public void Stop()
        {
        }

        /// <summary>
        /// 清理只依赖配置内容的缓存，不负责销毁共享角色副本。
        /// </summary>
        public void Clear() => rootMotion.Invalidate();

        #endregion
    }
}
#endif