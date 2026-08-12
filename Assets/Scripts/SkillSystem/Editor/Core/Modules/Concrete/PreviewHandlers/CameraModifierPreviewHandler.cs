#if UNITY_EDITOR
using RPG.CameraSystem;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace RPG.SkillSystem.Editor
{
    /// <summary>为每个窗口创建独立 Scene View Camera Modifier 预览处理器。</summary>
    internal sealed class CameraModifierPreviewFactory : ITrackPreviewFactory
    {
        /// <summary>创建窗口私有处理器。</summary>
        public ITrackPreviewHandler Create() => new CameraModifierPreviewHandler();
    }

    /// <summary>
    /// 从初始 Scene View 姿态确定性显示当前帧的 FOV 与 Shake，不创建隐藏 Brain。
    /// </summary>
    internal sealed class CameraModifierPreviewHandler : IActorIndependentPreviewHandler,
        ICameraModifierPreviewService
    {
        private readonly Dictionary<string, CameraModifierDataBase> drafts = new();
        private SceneView sceneView;
        private Vector3 basePivot;
        private Quaternion baseRotation;
        private float baseSize;
        private float baseFov;
        private SkillConfig lastConfig;
        private int lastFrame;
        private bool captured;

        /// <summary>配置变化不需要重建资源；下一帧直接重新求值。</summary>
        public void Invalidate()
        {
        }

        /// <summary>兼容组合预览的统一上下文入口。</summary>
        public void SampleFrame(in PreviewFrameContext context) => SampleFrame(context.Config, context.Frame);

        /// <summary>使用 Scene View 基准状态应用当前帧修饰。</summary>
        public void SampleFrame(SkillConfig config, int frame)
        {
            lastConfig = config;
            lastFrame = frame;
            if (!EditorSettings.instance.PreviewCameraModifier)
            {
                Restore();
                return;
            }
            SceneView current = SceneView.lastActiveSceneView;
            if (current == null || config == null) return;
            if (!captured || sceneView != current) Capture(current);
            CameraModifierState state = EvaluateWithDrafts(config, frame);

            // 每次回到启用预览时的基准姿态，避免连续 Scrub 累积 Shake。
            sceneView.pivot = basePivot + baseRotation * state.LocalPositionOffset;
            sceneView.rotation = baseRotation * Quaternion.Euler(state.LocalRotationOffset);
            // 正交视图使用 size 表示镜头范围；透视视图只修改 FOV，避免同时改变观察距离。
            sceneView.size = sceneView.orthographic ? baseSize * state.FovScale : baseSize;
            if (!sceneView.orthographic && sceneView.camera != null)
            {
                sceneView.camera.fieldOfView = baseFov * state.FovScale;
            }
            sceneView.Repaint();
        }

        /// <summary>停止播放保留当前定帧画面。</summary>
        public void Stop()
        {
        }

        /// <summary>恢复启用预览前的 Scene View 状态。</summary>
        public void Clear()
        {
            drafts.Clear();
            lastConfig = null;
            Restore();
        }

        /// <summary>释放并恢复 Scene View。</summary>
        public void Dispose() => Restore();

        /// <summary>保存独立深复制草稿并立即请求 Scene View 重绘。</summary>
        public void SetDraft(string clipId, CameraModifierDataBase data)
        {
            drafts[clipId] = CameraModifierDataBase.Copy(data);
            if (lastConfig != null) SampleFrame(lastConfig, lastFrame);
        }

        /// <summary>清除单个 Clip 草稿。</summary>
        public void ClearDraft(string clipId)
        {
            if (!string.IsNullOrEmpty(clipId)) drafts.Remove(clipId);
            if (lastConfig != null) SampleFrame(lastConfig, lastFrame);
        }

        /// <summary>优先使用 Inspector 草稿求值，未编辑 Clip 继续读取权威 Config。</summary>
        private CameraModifierState EvaluateWithDrafts(SkillConfig config, int frame)
        {
            if (drafts.Count == 0) return CameraModifierEvaluator.Evaluate(config, frame);
            return CameraModifierEvaluator.Evaluate(config, frame, (clipId, data) =>
                drafts.TryGetValue(clipId, out CameraModifierDataBase draft) ? draft : data);
        }

        /// <summary>记录当前 Scene View 作为不变的预览基准。</summary>
        private void Capture(SceneView value)
        {
            Restore();
            sceneView = value;
            basePivot = value.pivot;
            baseRotation = value.rotation;
            baseSize = value.size;
            baseFov = value.camera != null ? value.camera.fieldOfView : 60f;
            captured = true;
        }

        /// <summary>恢复基准并清除窗口私有引用。</summary>
        private void Restore()
        {
            if (!captured || sceneView == null) return;
            sceneView.pivot = basePivot;
            sceneView.rotation = baseRotation;
            sceneView.size = baseSize;
            if (sceneView.camera != null) sceneView.camera.fieldOfView = baseFov;
            sceneView.Repaint();
            sceneView = null;
            captured = false;
        }
    }
}
#endif
