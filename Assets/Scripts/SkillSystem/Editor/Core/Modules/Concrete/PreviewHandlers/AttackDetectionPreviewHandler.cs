#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 为每个时间轴窗口创建独立的攻击检测 Scene 预览与 Handle 编辑处理器。
    /// </summary>
    internal sealed class AttackDetectionPreviewFactory : ITrackPreviewFactory
    {
        #region 依赖

        private readonly EditorConfig config;

        #endregion

        // 保存纯编辑器绘制配置；每次 Create 都返回独立 Handler。
        internal AttackDetectionPreviewFactory(EditorConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 创建窗口私有的攻击检测预览处理器。
        /// </summary>
        public ITrackPreviewHandler Create() => new AttackDetectionPreviewHandler(config);
    }

    /// <summary>
    /// 按当前帧收集有效攻击检测，在 Scene View 绘制形状并管理提交前的 Handle 草稿。
    /// </summary>
    internal sealed class AttackDetectionPreviewHandler : ITrackPreviewHandler,
        ITrackPreviewStatusProvider, IAttackDetectionSceneEditService
    {
        #region 依赖与状态

        private readonly EditorConfig config;
        private readonly Dictionary<Type, IAttackDetectionSceneDrawer> drawers = new();
        private readonly List<PreviewEntry> entries = new();
        private string selectedClipId = string.Empty;
        private string draftClipId = string.Empty;
        private AttackDetectionDataBase draftData;
        private int activeHandleControlId;
        private string statusMessage = string.Empty;
        private bool isPlaying;
        private bool disposed;

        #endregion

        #region 事件与属性

        public event Action<AttackDetectionSceneEditCommit> EditCommitted;

        public string StatusMessage => statusMessage;

        #endregion

        #region 生命周期

        // 注册具体 Drawer 与 SceneView 回调；重复类型会立即暴露配置错误。
        internal AttackDetectionPreviewHandler(EditorConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            Register(new BoxSceneDrawer());
            Register(new SphereSceneDrawer());
            Register(new CapsuleSceneDrawer());
            Register(new SectorSceneDrawer());
            Register(new WeaponTraceSceneDrawer());
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>
        /// 注销 Scene 回调并丢弃所有未提交草稿。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SceneView.duringSceneGui -= OnSceneGui;
            Clear();
            EditCommitted = null;
        }

        #endregion

        #region 预览操作

        /// <summary>
        /// 配置内容变化时清理显示快照和本地 Handle 草稿。
        /// </summary>
        public void Invalidate()
        {
            ClearEntriesAndDraft();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 收集当前半开区间内未静音 Clip，并为 WeaponTrace 构建前后采样帧刀刃快照。
        /// </summary>
        public void SampleFrame(in PreviewFrameContext context)
        {
            if (disposed) return;
            ClearEntriesAndDraft();
            isPlaying = context.Reason != PreviewSampleReason.Scrub;
            statusMessage = string.Empty;
            if (context.Config == null || context.Actor?.RootTransform == null) return;

            foreach (AttackDetectionTrackConfig track in context.Config.AttackDetectionTracks)
            {
                if (track.Header.Muted) continue;
                foreach (AttackDetectionSkillClipConfig clip in track.Clips)
                {
                    if (context.Frame < clip.StartFrame || context.Frame >= clip.EndFrame ||
                        clip.DetectionData == null)
                        continue;
                    if (!drawers.TryGetValue(clip.DetectionData.GetType(), out IAttackDetectionSceneDrawer drawer))
                        continue;

                    bool isSampleFrame = (context.Frame - clip.StartFrame) %
                                         Mathf.Max(1, clip.SampleIntervalFrames) == 0;
                    WeaponTraceSweepSegment? weaponSegment = drawer.RequiresWeaponTraceMarkers
                        ? BuildWeaponTrace(context, clip)
                        : null;
                    entries.Add(new PreviewEntry(clip, drawer, isSampleFrame,
                        CreateDrawContext(context.Actor.RootTransform,
                            config.AttackDetectionColor, weaponSegment)));
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 暂停后保留当前形状，并允许所选体积 Clip 显示 Handles。
        /// </summary>
        public void Stop()
        {
            isPlaying = false;
            CancelDraft();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 清理全部 Scene 显示状态和错误信息。
        /// </summary>
        public void Clear()
        {
            ClearEntriesAndDraft();
            statusMessage = string.Empty;
            isPlaying = false;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 设置唯一可编辑 Clip；选择变化会丢弃旧目标尚未提交的草稿。
        /// </summary>
        public void SetSelectedClip(string clipId)
        {
            clipId ??= string.Empty;
            if (selectedClipId == clipId) return;
            selectedClipId = clipId;
            CancelDraft();
            SceneView.RepaintAll();
        }

        #endregion

        #region Scene 绘制与编辑

        // 绘制全部当前有效形状，并只把 Unity 当前 W/E/R 工具对应的 Handle 交给所选体积 Clip。
        private void OnSceneGui(SceneView _)
        {
            if (disposed) return;
            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                CancelDraft();
                currentEvent.Use();
                SceneView.RepaintAll();
                return;
            }

            AttackDetectionHandleMode handleMode = ResolveHandleMode(Tools.current);
            bool shouldCommit = false;
            foreach (PreviewEntry entry in entries)
            {
                AttackDetectionDataBase displayData = entry.Clip.Id == draftClipId && draftData != null
                    ? draftData
                    : entry.Clip.DetectionData;
                Color color = entry.Clip.Id == selectedClipId
                    ? config.AttackDetectionSelectedColor
                    : config.AttackDetectionColor;
                if (!entry.IsSampleFrame) color.a *= config.AttackDetectionUnsampledAlpha;
                AttackDetectionSceneDrawContext drawContext = CreateDrawContext(
                    entry.Context.ActorRoot, color, entry.Context.WeaponSegment);
                entry.Drawer.Draw(drawContext, displayData);
                if (isPlaying || entry.Clip.Id != selectedClipId || !entry.Drawer.SupportsHandles ||
                    handleMode == AttackDetectionHandleMode.None)
                    continue;

                EditorGUI.BeginChangeCheck();
                AttackDetectionDataBase changed = entry.Drawer.DrawHandles(
                    drawContext, displayData, handleMode);
                if (EditorGUI.EndChangeCheck())
                {
                    draftClipId = entry.Clip.Id;
                    draftData = changed;
                    if (GUIUtility.hotControl != 0)
                        activeHandleControlId = GUIUtility.hotControl;
                    else
                        shouldCommit = true;
                }
            }

            // Handles 会消费 MouseUp；以 hotControl 释放作为拖拽完成的权威信号。
            if (draftData != null && activeHandleControlId != 0 && GUIUtility.hotControl == 0)
                shouldCommit = true;
            if (shouldCommit && draftData != null) CommitDraft();
        }

        // 根据线框透明度继续乘以表面透明度，确保非采样帧的两层表现同步弱化。
        private AttackDetectionSceneDrawContext CreateDrawContext(Transform actorRoot, Color color,
            WeaponTraceSweepSegment? weaponSegment)
        {
            Color fillColor = color;
            fillColor.a *= config.AttackDetectionFillAlpha;
            return new AttackDetectionSceneDrawContext(actorRoot, color, fillColor,
                config.AttackDetectionSurfaceSegments, weaponSegment);
        }

        // 将 Unity Scene 工具映射为单一攻击检测编辑类别；View、Rect 等工具只保留线框。
        private static AttackDetectionHandleMode ResolveHandleMode(Tool tool) => tool switch
        {
            Tool.Move => AttackDetectionHandleMode.Position,
            Tool.Rotate => AttackDetectionHandleMode.Rotation,
            Tool.Scale => AttackDetectionHandleMode.Shape,
            _ => AttackDetectionHandleMode.None
        };

        // 在 Scene 绘制循环结束后发送独立快照，避免同步刷新 entries 时修改正在遍历的集合。
        private void CommitDraft()
        {
            AttackDetectionSceneEditCommit commit = new(
                draftClipId, AttackDetectionDataBase.Copy(draftData));
            CancelDraft();
            EditCommitted?.Invoke(commit);
            SceneView.RepaintAll();
        }

        // 注册具体数据类型对应的 Drawer，避免 Handler 维护 AttackDetectionType 分支。
        private void Register(IAttackDetectionSceneDrawer drawer)
        {
            if (!drawers.TryAdd(drawer.DataType, drawer))
                throw new InvalidOperationException(
                    $"攻击检测 Scene Drawer 类型 {drawer.DataType.FullName} 重复注册。");
        }

        // 丢弃未完成 Handle 草稿；权威 Config 始终由 ViewModel/Document 修改。
        private void CancelDraft()
        {
            draftClipId = string.Empty;
            draftData = null;
            activeHandleControlId = 0;
        }

        // 清除逐帧快照和草稿，不改变当前选择。
        private void ClearEntriesAndDraft()
        {
            entries.Clear();
            CancelDraft();
        }

        #endregion

        #region WeaponTrace 快照

        // 按 EditorConfig 固定 Socket Key 解析唯一激活 Provider，并确定性采样上一采样帧。
        private WeaponTraceSweepSegment? BuildWeaponTrace(in PreviewFrameContext context,
            AttackDetectionSkillClipConfig clip)
        {
            if (!context.Actor.TryGetWeaponTraceMarkers(
                    config.PreviewWeaponTraceRootKey, config.PreviewWeaponTraceTipKey,
                    out Transform root, out Transform tip, out string markerError))
            {
                SetFirstStatus(markerError);
                return null;
            }

            Transform[] targets = { root, tip };
            Matrix4x4[] currentMatrices =
            {
                root.localToWorldMatrix,
                tip.localToWorldMatrix
            };
            int previousFrame = Mathf.Max(clip.StartFrame,
                context.Frame - Mathf.Max(1, clip.SampleIntervalFrames));
            if (!context.TryResolveWorldMatrices(targets, previousFrame,
                    out Matrix4x4[] previousMatrices))
            {
                SetFirstStatus("无法读取 WeaponTrace 上一采样帧的刀刃姿态。");
                return null;
            }

            return new WeaponTraceSweepSegment(
                previousMatrices[0].GetColumn(3),
                previousMatrices[1].GetColumn(3),
                currentMatrices[0].GetColumn(3),
                currentMatrices[1].GetColumn(3));
        }
        // 仅保留首个局部错误，避免多个无效 Clip 持续覆盖状态栏信息。
        private void SetFirstStatus(string message)
        {
            if (string.IsNullOrEmpty(statusMessage)) statusMessage = message;
        }

        #endregion

        #region 内部类型

        /// <summary>
        /// 保存一次 SampleFrame 后供 SceneView 重复重绘的只读 Clip 快照。
        /// </summary>
        private sealed class PreviewEntry
        {
            internal AttackDetectionSkillClipConfig Clip { get; }
            internal IAttackDetectionSceneDrawer Drawer { get; }
            internal bool IsSampleFrame { get; }
            internal AttackDetectionSceneDrawContext Context { get; }

            // 保存权威 Clip 引用、无状态 Drawer 和当前帧场景上下文。
            internal PreviewEntry(AttackDetectionSkillClipConfig clip,
                IAttackDetectionSceneDrawer drawer, bool isSampleFrame,
                AttackDetectionSceneDrawContext context)
            {
                Clip = clip;
                Drawer = drawer;
                IsSampleFrame = isSampleFrame;
                Context = context;
            }
        }

        #endregion
    }
}
#endif