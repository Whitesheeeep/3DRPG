#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 处理 Project 素材拖入具体轨道 Lane 的本地交互，并把吸附后的创建意图交给 ViewModel。
    /// </summary>
    internal sealed class TrackDragController : IDisposable
    {
        #region 常量与依赖

        private const string ValidDropClass = "is-asset-drop-valid";
        private const string InvalidDropClass = "is-asset-drop-invalid";

        private readonly Dictionary<VisualElement, TrackViewData> dragDataMap = new();
        private readonly CoordinateMapper mapper;
        private readonly TrackModuleRegistry modules;
        private readonly EditorViewModel viewModel;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建轨道素材拖拽控制器，后续拖拽坐标统一使用同一个时间轴映射器。
        /// </summary>
        public TrackDragController(CoordinateMapper mapper, TrackModuleRegistry modules,
            EditorViewModel viewModel)
        {
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        /// <summary>
        /// 注销全部动态 Lane 的拖拽回调并释放控制器。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Reset();
        }

        /// <summary>
        /// 注销当前重建周期内的所有动态 Lane 回调和拖拽状态样式。
        /// </summary>
        public void Reset()
        {
            foreach (VisualElement lane in dragDataMap.Keys)
            {
                lane.UnregisterCallback<DragUpdatedEvent>(OnDragUpdated);
                lane.UnregisterCallback<DragPerformEvent>(OnDragPerform);
                lane.UnregisterCallback<DragLeaveEvent>(OnDragLeave);
                lane.UnregisterCallback<DragExitedEvent>(OnDragExited);
                ClearDropStyle(lane);
            }
            dragDataMap.Clear();
        }

        #endregion

        #region 动态轨道注册

        /// <summary>
        /// 将具体轨道投影与其 Item Lane 绑定，供 Project 拖拽事件恢复语义目标。
        /// </summary>
        public void RegisterTrackEvent(TrackViewData track, VisualElement lane)
        {
            if (disposed) throw new ObjectDisposedException(nameof(TrackDragController));
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (lane == null) throw new ArgumentNullException(nameof(lane));

            dragDataMap[lane] = track;
            lane.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            lane.RegisterCallback<DragPerformEvent>(OnDragPerform);
            lane.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            lane.RegisterCallback<DragExitedEvent>(OnDragExited);
        }

        #endregion

        #region 拖拽事件处理

        // 拖动阶段只校验资源类型与更新 USS 状态，不创建 Item 或修改 SkillConfig。
        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (!TryGetLaneAndTrack(evt.currentTarget, out VisualElement lane, out TrackViewData track)) return;
            bool accepted = TryGetDropHandler(track, DragAndDrop.objectReferences, out _);
            DragAndDrop.visualMode = accepted ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            SetDropStyle(lane, accepted);
            evt.StopPropagation();
        }

        // 松手时把 Lane 内容坐标吸附到整数帧，并只提交一次批量创建语义操作。
        private void OnDragPerform(DragPerformEvent evt)
        {
            if (!TryGetLaneAndTrack(evt.currentTarget, out VisualElement lane, out TrackViewData track)) return;
            if (!TryGetDropHandler(track, DragAndDrop.objectReferences,
                    out ITrackDropHandler handler))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                ClearDropStyle(lane);
                evt.StopPropagation();
                return;
            }

            int startFrame = mapper.ContentXToFrame(evt.localMousePosition.x);
            IItemCreateRequest request = handler.CreateRequest(DragAndDrop.objectReferences, startFrame);
            EditResult result = viewModel.CreateItems(track, request);
            if (result.Succeeded) DragAndDrop.AcceptDrag();
            else DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            ClearDropStyle(lane);
            evt.StopPropagation();
        }

        // 指针离开当前 Lane 时清理局部拖拽反馈，避免重建前残留高亮。
        private void OnDragLeave(DragLeaveEvent evt)
        {
            if (evt.currentTarget is VisualElement lane) ClearDropStyle(lane);
        }

        // Unity 结束整次拖拽会发送 DragExited，同样清除最后一个目标 Lane 的状态。
        private void OnDragExited(DragExitedEvent evt)
        {
            if (evt.currentTarget is VisualElement lane) ClearDropStyle(lane);
        }

        #endregion

        #region 资源校验与语义提交

        // 通过事件 currentTarget 恢复注册时的具体 Lane 与 TrackViewData。
        private bool TryGetLaneAndTrack(IEventHandler target, out VisualElement lane, out TrackViewData track)
        {
            lane = target as VisualElement;
            track = null;
            return lane != null && dragDataMap.TryGetValue(lane, out track);
        }

        // 锁定轨道、空批次、未注册轨道和处理器校验失败都会整批拒绝，避免产生部分导入结果。
        private bool TryGetDropHandler(TrackViewData track, IReadOnlyList<UnityEngine.Object> assets,
            out ITrackDropHandler handler)
        {
            handler = null;
            if (track == null || track.Locked || assets == null || assets.Count == 0) return false;
            return modules.TryGetDrop(track, out handler) && handler.CanAccept(assets);
        }

        // 切换合法或非法拖入状态，尺寸与颜色全部由 USS 控制。
        private static void SetDropStyle(VisualElement lane, bool accepted)
        {
            lane.EnableInClassList(ValidDropClass, accepted);
            lane.EnableInClassList(InvalidDropClass, !accepted);
        }

        // 同时移除两种拖入状态，确保动态行复用或释放时没有视觉残留。
        private static void ClearDropStyle(VisualElement lane)
        {
            lane.RemoveFromClassList(ValidDropClass);
            lane.RemoveFromClassList(InvalidDropClass);
        }

        #endregion
    }
}
#endif
