#if UNITY_EDITOR
using System;
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
    /// 保存一次预览采样需要的只读技能、角色、帧、采样原因和 Root Motion 查询能力。
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

        /// <summary>
        /// 创建统一的逐帧预览上下文。
        /// </summary>
        public PreviewFrameContext(SkillConfig config, PreviewActorInstance actor,
            int frame, bool applyRootMotion, PreviewSampleReason reason,
            IPreviewActorPoseProvider actorPoseProvider)
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
        }

        // 仅在启用 Root Motion 且动画模块提供姿态查询时读取历史根姿态，否则返回角色初始姿态。
        internal RootPose ResolveRootPose(int frame) =>
            ApplyRootMotion && ActorPoseProvider != null
                ? ActorPoseProvider.GetRootPose(Config, Actor, frame)
                : RootPose.Identity;
    }
}
#endif
