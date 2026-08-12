#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Markers;
using UnityEngine;

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
    /// 标记无需预览场景和角色即可采样的轨道预览处理器。
    /// </summary>
    internal interface IActorIndependentPreviewHandler : ITrackPreviewHandler
    {
        /// <summary>使用技能配置和整数帧执行角色无关采样。</summary>
        void SampleFrame(SkillConfig config, int frame);
    }

    /// <summary>
    /// 允许轨道预览处理器向组合预览报告不阻断其他模块的局部状态。
    /// </summary>
    internal interface ITrackPreviewStatusProvider
    {
        /// <summary>
        /// 获取最近一次采样产生的局部状态；空字符串表示该 Handler 正常。
        /// </summary>
        string StatusMessage { get; }
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

    /// <summary>
    /// 为表现轨道确定性查询角色任意帧的完整挂点世界矩阵，并负责恢复查询前的当前帧姿势。
    /// </summary>
    internal interface IPreviewActorBindingPoseProvider
    {
        /// <summary>
        /// 临时采样目标帧后读取挂点世界矩阵，并在返回前恢复指定当前帧。
        /// </summary>
        /// <param name="config">当前技能配置。</param>
        /// <param name="actor">窗口私有预览角色。</param>
        /// <param name="markerKey">目标挂点；空值表示角色根节点。</param>
        /// <param name="frame">需要读取绑定姿态的目标帧。</param>
        /// <param name="restoreFrame">查询结束后必须恢复的当前播放头帧。</param>
        /// <param name="applyRootMotion">是否把动画累计 Root Motion 应用到世界矩阵。</param>
        /// <param name="matrix">成功时返回挂点的世界 TRS 矩阵。</param>
        /// <returns>角色中存在目标挂点且矩阵已读取时返回 true。</returns>
        bool TryGetBindingWorldMatrix(SkillConfig config, PreviewActorInstance actor,
            MarkerKey markerKey, int frame, int restoreFrame, bool applyRootMotion,
            out Matrix4x4 matrix);

        /// <summary>
        /// 临时采样目标帧并批量读取预览副本中指定 Transform 的世界矩阵，随后恢复播放头当前帧。
        /// </summary>
        /// <param name="config">当前技能配置。</param>
        /// <param name="actor">窗口私有预览角色。</param>
        /// <param name="transforms">属于该预览角色副本的目标节点集合。</param>
        /// <param name="frame">需要读取姿态的目标帧。</param>
        /// <param name="restoreFrame">查询结束后恢复的播放头帧。</param>
        /// <param name="applyRootMotion">是否应用动画累计 Root Motion。</param>
        /// <param name="matrices">成功时按输入顺序返回世界矩阵。</param>
        /// <returns>全部目标节点有效并完成读取时返回 true。</returns>
        bool TryGetWorldMatrices(SkillConfig config, PreviewActorInstance actor,
            IReadOnlyList<Transform> transforms, int frame, int restoreFrame,
            bool applyRootMotion, out Matrix4x4[] matrices);
    }
}
#endif
