#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理共享预览角色并按注册顺序调度全部轨道预览处理器。
    /// </summary>
    internal sealed class CompositePreview : IPreview
    {
        #region 依赖与状态
        // 依赖
        private readonly PreviewSceneService sceneService;
        private readonly PreviewActorFactory actorFactory;
        private readonly IReadOnlyList<ITrackPreviewHandler> handlers;
        private readonly IPreviewActorPoseProvider actorPoseProvider;
        private SkillConfig config;

        // 状态
        private GameObject actorSource;
        private PreviewActorInstance actorInstance;
        private bool applyRootMotion;
        private string lastStatus = string.Empty;
        private bool disposed;

        public event Action<string> StatusChanged;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建共享角色上下文和按模块顺序执行的轨道预览组合器。
        /// </summary>
        public CompositePreview(PreviewSceneService sceneService, PreviewActorFactory actorFactory,
            IReadOnlyList<ITrackPreviewHandler> handlers)
        {
            this.sceneService = sceneService ?? throw new ArgumentNullException(nameof(sceneService));
            this.actorFactory = actorFactory ?? throw new ArgumentNullException(nameof(actorFactory));
            this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            actorPoseProvider = ResolveActorPoseProvider(handlers);
        }

        /// <summary>
        /// 逆序释放轨道处理器、角色副本和状态事件。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
            for (int index = handlers.Count - 1; index >= 0; index--)
                handlers[index]?.Dispose();
            StatusChanged = null;
            config = null;
            actorSource = null;
        }

        #endregion

        #region 配置与采样

        /// <summary>
        /// 切换技能配置并清除所有依赖旧内容的轨道缓存。
        /// </summary>
        public void SetSkillConfig(SkillConfig value)
        {
            if (ReferenceEquals(config, value))
            {
                InvalidateContent();
                return;
            }

            config = value;
            InvalidateContent();
            if (config == null) Clear();
        }

        /// <summary>
        /// 切换演示角色并释放旧角色副本；源场景对象始终保持不被直接采样。
        /// </summary>
        public void SetPreviewActor(GameObject actor)
        {
            if (actorSource == actor) return;
            actorSource = actor;
            ReleaseActor();
            InvalidateContent();
        }

        /// <summary>
        /// 切换 Root Motion 后使绝对帧缓存失效，角色副本可继续复用。
        /// </summary>
        public void SetApplyRootMotion(bool value)
        {
            if (applyRootMotion == value) return;
            applyRootMotion = value;
            InvalidateContent();
        }

        /// <summary>
        /// 使所有轨道的派生缓存失效，不销毁当前角色副本。
        /// </summary>
        public void InvalidateContent()
        {
            foreach (ITrackPreviewHandler handler in handlers)
                handler?.Invalidate();
        }

        /// <summary>
        /// 在固定预览场景中准备隔离角色，并按采样原因把整数帧广播给全部已注册轨道处理器。
        /// </summary>
        public void SampleFrame(int frame, PreviewSampleReason reason)
        {
            if (disposed || config == null) return;
            if (!sceneService.IsPreviewSceneLoaded)
            {
                ReportStatus("请先使用工具栏的“加载场景”打开固定预览场景。");
                return;
            }

            if (!EnsureActor()) return;
            int clampedFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, config.DurationFrames - 1));
            PreviewFrameContext context = new(
                config, actorInstance, clampedFrame, applyRootMotion, reason, actorPoseProvider);
            try
            {
                foreach (ITrackPreviewHandler handler in handlers)
                    handler?.SampleFrame(context);
                ReportStatus("预览已就绪。");
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                ReportStatus($"预览第 {clampedFrame} 帧失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 停止各轨道动态资源并暂停共享 AnimancerGraph，保留当前显示姿势。
        /// </summary>
        public void Stop()
        {
            foreach (ITrackPreviewHandler handler in handlers)
                handler?.Stop();
            actorInstance?.StopGraph();
        }

        /// <summary>
        /// 清理轨道预览资源和隔离角色，但保留当前 Config 与编辑器设置引用以便重建。
        /// </summary>
        public void Clear()
        {
            foreach (ITrackPreviewHandler handler in handlers)
                handler?.Clear();
            ReleaseActor();
        }

        #endregion

        #region 角色与状态辅助

        // 从模块 Handler 中解析唯一角色姿态提供器，防止多个动画来源导致 VFX 绑定结果不确定。
        private static IPreviewActorPoseProvider ResolveActorPoseProvider(
            IReadOnlyList<ITrackPreviewHandler> previewHandlers)
        {
            IPreviewActorPoseProvider result = null;
            foreach (ITrackPreviewHandler handler in previewHandlers)
            {
                if (handler is not IPreviewActorPoseProvider candidate) continue;
                if (result != null)
                    throw new InvalidOperationException("预览中注册了多个角色根姿态提供器。");
                result = candidate;
            }

            return result;
        }

        // 延迟创建预览角色，并保证创建失败不会阻止播放头继续工作。
        private bool EnsureActor()
        {
            if (actorInstance != null && actorInstance.IsValid && actorInstance.Source == actorSource) return true;
            ReleaseActor();
            if (!actorFactory.TryCreate(actorSource, out actorInstance, out string error))
            {
                ReportStatus(error);
                return false;
            }

            InvalidateContent();
            return true;
        }

        // 释放角色前先清理各轨道对旧角色生成的缓存或临时资源。
        private void ReleaseActor()
        {
            foreach (ITrackPreviewHandler handler in handlers)
                handler?.Clear();
            actorInstance?.Dispose();
            actorInstance = null;
        }

        // 仅在消息变化时通知 ViewModel，避免 EditorApplication.update 每帧重复刷新状态栏。
        private void ReportStatus(string message)
        {
            message ??= string.Empty;
            if (lastStatus == message) return;
            lastStatus = message;
            StatusChanged?.Invoke(message);
        }

        #endregion
    }
}
#endif