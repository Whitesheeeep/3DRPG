#if UNITY_EDITOR
using System;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 定义一种轨道的窗口私有预览处理器，由 CompositePreview 在每次采样时统一调度。
    /// </summary>
    internal interface ITrackPreviewHandler : IDisposable
    {
        /// <summary>
        /// 使依赖 SkillConfig 内容的派生缓存失效，但保留可复用的编辑器资源。
        /// </summary>
        void Invalidate();

        /// <summary>
        /// 使用统一只读上下文采样当前帧；采样原因决定动态资源是重建、推进还是保持静音。
        /// </summary>
        void SampleFrame(in PreviewFrameContext context);

        /// <summary>
        /// 停止音频、粒子等动态状态；静态姿势是否保留由具体轨道定义。
        /// </summary>
        void Stop();

        /// <summary>
        /// 清理该轨道持有的实例、缓存和临时编辑器资源。
        /// </summary>
        void Clear();
    }

    /// <summary>
    /// 作为 TrackModule 的无状态可选能力，为每个窗口创建独立轨道预览处理器。
    /// </summary>
    internal interface ITrackPreviewFactory
    {
        /// <summary>
        /// 创建不得被其他窗口共享的轨道预览处理器。
        /// </summary>
        ITrackPreviewHandler Create();
    }

    /// <summary>
    /// 为其他预览模块提供任意帧的角色根姿态，避免它们依赖动画 Handler 或 Animancer 实现。
    /// </summary>
    internal interface IPreviewActorPoseProvider
    {
        /// <summary>
        /// 查询指定整数帧相对角色初始变换的累计 Root Motion 姿态。
        /// </summary>
        RootPose GetRootPose(SkillConfig config, PreviewActorInstance actor, int frame);
    }
}
#endif
