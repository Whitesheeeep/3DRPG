#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 区分手动定位、播放起点和播放时钟推进，使音频等动态预览可以选择正确的重建策略。
    /// </summary>
    internal enum PreviewSampleReason
    {
        Scrub,
        PlaybackStart,
        PlaybackAdvance
    }

    /// <summary>
    /// 定义时间轴播放控制器与场景预览实现之间的生命周期和逐帧采样边界。
    /// </summary>
    internal interface IPreview : IDisposable
    {
        event Action<string> StatusChanged;

        /// <summary>
        /// 切换预览使用的技能配置，并使依赖旧内容的轨道缓存失效。
        /// </summary>
        void SetSkillConfig(SkillConfig config);
        /// <summary>
        /// 切换预览使用的演示角色；具体实现不得直接修改源对象。
        /// </summary>
        void SetPreviewActor(GameObject actor);
        /// <summary>
        /// 设置预览是否应用动画根位移。
        /// </summary>
        void SetApplyRootMotion(bool value);
        /// <summary>
        /// 通知预览当前配置内容已经改变，但配置引用本身未切换。
        /// </summary>
        void InvalidateContent();
        /// <summary>
        /// 采样指定整数帧的预览状态，并声明本次定位来自手动操作还是播放时钟。
        /// </summary>
        void SampleFrame(int frame, PreviewSampleReason reason);
        /// <summary>
        /// 停止轨道持有的动态预览资源；播放头由 PlaybackController 管理。
        /// </summary>
        void Stop();
        /// <summary>
        /// 清理角色副本、轨道缓存和预览实现持有的全部状态。
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 保存一次预览采样所需的技能、角色、帧、Marker 绑定及任意帧姿态查询能力。
    /// </summary>
    internal readonly struct PreviewFrameContext
    {
        public SkillConfig Config { get; }
        public PreviewActorInstance Actor { get; }
        public int Frame { get; }
        public float TimeSeconds { get; }
        public bool ApplyRootMotion { get; }
        public PreviewSampleReason Reason { get; }
        public IPreviewActorPoseProvider ActorPoseProvider { get; }
        public IPreviewActorBindingPoseProvider BindingPoseProvider { get; }

        /// <summary>
        /// 创建统一的逐帧预览上下文。
        /// </summary>
        public PreviewFrameContext(SkillConfig config, PreviewActorInstance actor,
            int frame, bool applyRootMotion, PreviewSampleReason reason,
            IPreviewActorPoseProvider actorPoseProvider,
            IPreviewActorBindingPoseProvider bindingPoseProvider)
        {
            Config = config;
            Actor = actor;
            Frame = frame;
            TimeSeconds = config != null && config.FrameRate > 0
                ? frame / (float)config.FrameRate
                : 0f;
            ApplyRootMotion = applyRootMotion;
            Reason = reason;
            ActorPoseProvider = actorPoseProvider;
            BindingPoseProvider = bindingPoseProvider;
        }

        // 仅在启用 Root Motion 且动画模块提供姿态查询时读取历史根姿态，否则返回角色初始姿态。
        internal RootPose ResolveRootPose(int frame) =>
            ApplyRootMotion && ActorPoseProvider != null
                ? ActorPoseProvider.GetRootPose(Config, Actor, frame)
                : RootPose.Identity;

        // 从当前预览角色解析指定语义挂点；空 Key 明确返回角色根节点。
        internal bool TryGetBindingTransform(MarkerKey markerKey, out Transform transform,
            out string error) => Actor.TryGetMarker(markerKey, out transform, out error);

        // 通过动画模块临时采样目标帧的完整角色姿势与 Root Motion，并在查询后恢复当前帧。
        internal bool TryResolveBindingWorldMatrix(MarkerKey markerKey, int frame,
            out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return BindingPoseProvider.TryGetBindingWorldMatrix(
                Config, Actor, markerKey, frame, Frame, ApplyRootMotion, out matrix);
        }

        // 批量读取预览副本节点在任意帧的世界矩阵，并由动画模块恢复当前帧以避免扰动后续 Handler。
        internal bool TryResolveWorldMatrices(IReadOnlyList<Transform> transforms, int frame,
            out Matrix4x4[] matrices)
        {
            matrices = Array.Empty<Matrix4x4>();
            return BindingPoseProvider != null && BindingPoseProvider.TryGetWorldMatrices(
                Config, Actor, transforms, frame, Frame, ApplyRootMotion, out matrices);
        }
    }
}
#endif
