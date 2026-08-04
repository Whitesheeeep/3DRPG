#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 管理共享预览角色、轨道采样，以及 VFX 与攻击检测的场景编辑服务。
    /// </summary>
    internal sealed class CompositePreview : IPreview, IVfxSceneEditService, IAttackDetectionSceneEditService
    {
        #region 依赖与状态
        // 依赖
        private readonly PreviewSceneService sceneService;
        private readonly PreviewActorFactory actorFactory;
        private readonly IReadOnlyList<ITrackPreviewHandler> handlers;
        private readonly IPreviewActorPoseProvider actorPoseProvider;
        private readonly IPreviewActorBindingPoseProvider bindingPoseProvider;
        private readonly IVfxSceneEditService vfxSceneEditService;
        private readonly IAttackDetectionSceneEditService attackDetectionSceneEditService;
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
            bindingPoseProvider = ResolveBindingPoseProvider(handlers);
            vfxSceneEditService = ResolveVfxSceneEditService(handlers);
            attackDetectionSceneEditService = ResolveAttackDetectionSceneEditService(handlers);
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
                config, actorInstance, clampedFrame, applyRootMotion, reason, actorPoseProvider,
                bindingPoseProvider);
            try
            {
                foreach (ITrackPreviewHandler handler in handlers)
                    handler?.SampleFrame(context);
                string handlerStatus = ResolveHandlerStatus();
                ReportStatus(string.IsNullOrEmpty(handlerStatus) ? "预览已就绪。" : handlerStatus);
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

        #region VFX 场景编辑

        /// <summary>
        /// 判断指定 VFX Clip 是否正在通过当前窗口的独立代理编辑。
        /// </summary>
        public bool IsEditing(string clipId) => vfxSceneEditService.IsEditing(clipId);

        /// <summary>
        /// 将 VFX Clip 的场景编辑请求转交给唯一 VFX Preview Handler。
        /// </summary>
        public EditResult BeginEdit(VfxSkillClipConfig clip) => vfxSceneEditService.BeginEdit(clip);

        /// <summary>
        /// 将指定 Clip 的重新选择请求转交给唯一 VFX Preview Handler。
        /// </summary>
        public EditResult SelectProxy(string clipId) => vfxSceneEditService.SelectProxy(clipId);

        /// <summary>
        /// 从独立编辑代理读取相对于冻结挂点矩阵的局部 Transform 快照。
        /// </summary>
        public EditResult Capture(string clipId, out VfxTransformSnapshot snapshot) =>
            vfxSceneEditService.Capture(clipId, out snapshot);

        /// <summary>
        /// 销毁当前窗口尚未提交的 VFX 场景编辑代理。
        /// </summary>
        public void CancelEdit() => vfxSceneEditService.CancelEdit();

        #endregion

        #region 攻击检测场景编辑

        /// <summary>
        /// 转发攻击检测 Scene Handle 完成后的独立数据快照。
        /// </summary>
        public event Action<AttackDetectionSceneEditCommit> EditCommitted
        {
            add => attackDetectionSceneEditService.EditCommitted += value;
            remove => attackDetectionSceneEditService.EditCommitted -= value;
        }

        /// <summary>
        /// 将当前攻击检测选择同步给唯一 AttackDetection Preview Handler。
        /// </summary>
        public void SetSelectedClip(string clipId) =>
            attackDetectionSceneEditService.SetSelectedClip(clipId);

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

        // 从模块 Handler 中解析唯一的完整挂点姿态提供器，确保 VFX 起始帧冻结结果确定。
        private static IPreviewActorBindingPoseProvider ResolveBindingPoseProvider(
            IReadOnlyList<ITrackPreviewHandler> previewHandlers)
        {
            IPreviewActorBindingPoseProvider result = null;
            foreach (ITrackPreviewHandler handler in previewHandlers)
            {
                if (handler is not IPreviewActorBindingPoseProvider candidate) continue;
                if (result != null)
                    throw new InvalidOperationException("预览中注册了多个角色挂点姿态提供器。");
                result = candidate;
            }

            return result;
        }

        // 从模块 Handler 中解析唯一 VFX 场景编辑能力，避免 ViewModel 依赖具体 Handler 类型。
        private static IVfxSceneEditService ResolveVfxSceneEditService(
            IReadOnlyList<ITrackPreviewHandler> previewHandlers)
        {
            IVfxSceneEditService result = null;
            foreach (ITrackPreviewHandler handler in previewHandlers)
            {
                if (handler is not IVfxSceneEditService candidate) continue;
                if (result != null)
                    throw new InvalidOperationException("预览中注册了多个 VFX 场景编辑服务。");
                result = candidate;
            }

            return result ?? throw new InvalidOperationException("VFX 模块没有注册场景编辑服务。");
        }

        // 从模块 Handler 中解析唯一攻击检测场景编辑能力，避免 ViewModel 依赖具体 Handler 类型。
        private static IAttackDetectionSceneEditService ResolveAttackDetectionSceneEditService(
            IReadOnlyList<ITrackPreviewHandler> previewHandlers)
        {
            IAttackDetectionSceneEditService result = null;
            foreach (ITrackPreviewHandler handler in previewHandlers)
            {
                if (handler is not IAttackDetectionSceneEditService candidate) continue;
                if (result != null)
                    throw new InvalidOperationException("预览中注册了多个攻击检测场景编辑服务。");
                result = candidate;
            }

            return result ?? throw new InvalidOperationException("攻击检测模块没有注册场景编辑服务。");
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

        // 按模块顺序读取首个局部预览错误，使单个轨道 Clip 失败不会阻断其他 Handler。
        private string ResolveHandlerStatus()
        {
            foreach (ITrackPreviewHandler handler in handlers)
            {
                if (handler is ITrackPreviewStatusProvider provider &&
                    !string.IsNullOrEmpty(provider.StatusMessage))
                    return provider.StatusMessage;
            }

            return string.Empty;
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