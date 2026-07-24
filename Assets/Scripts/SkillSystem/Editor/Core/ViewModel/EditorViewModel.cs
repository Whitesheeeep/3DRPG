#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using RPG.SkillSystem;
using UnityEditor;
using UnityEngine;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 将技能配置投影为只读时间轴状态，并把 View 的语义意图交给 Document。
    /// </summary>
    internal sealed class EditorViewModel : IViewModel
    {
        #region 字段

        private readonly Document document;
        private readonly PlaybackController playback;
        private readonly PreviewSceneService previewSceneService;
        private readonly TrackModuleRegistry modules;
        private readonly List<GroupViewData> groups = new();
        private SelectionState selection = SelectionState.None;
        private bool disposed;

        #endregion

        #region 事件

        public event Action TimelineChanged;
        public event Action SelectionChanged;
        public event Action PlayheadChanged;
        public event Action PlaybackChanged;
        public event Action InspectorChanged;
        public event Action SettingsChanged;
        public event Action StatusChanged;

        #endregion

        #region 属性

        public IReadOnlyList<GroupViewData> Groups => groups;
        public SelectionState Selection => selection;
        public SkillConfig CurrentConfig => document.CurrentConfig;
        public int CurrentFrame => playback.CurrentFrame;
        public bool IsPlaying => playback.IsPlaying;
        public SceneAsset PreviewScene => previewSceneService.PreviewScene;
        public GameObject PreviewActor => previewSceneService.PreviewActor;
        public string StatusMessage { get; private set; } = "请选择或新建 SkillConfig。";
        public IViewData SelectedViewData => FindSelectedViewData();

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建窗口私有 ViewModel，并订阅 Document、播放控制器和编辑器设置事件。
        /// </summary>
        public EditorViewModel(Document document, PlaybackController playback,
            PreviewSceneService previewSceneService, TrackModuleRegistry modules)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.previewSceneService = previewSceneService ?? throw new ArgumentNullException(nameof(previewSceneService));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            document.ContentChanged += OnDocumentContentChanged;
            document.ConfigChanged += OnConfigChanged;
            playback.FrameChanged += OnFrameChanged;
            playback.PlaybackChanged += OnPlaybackChanged;
            previewSceneService.SettingsChanged += OnSettingsChanged;
            RebuildTimelineFromModel();
        }

        /// <summary>
        /// 释放 ViewModel 持有的全部事件订阅。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            document.ContentChanged -= OnDocumentContentChanged;
            document.ConfigChanged -= OnConfigChanged;
            playback.FrameChanged -= OnFrameChanged;
            playback.PlaybackChanged -= OnPlaybackChanged;
            previewSceneService.SettingsChanged -= OnSettingsChanged;
            TimelineChanged = null;
            SelectionChanged = null;
            PlayheadChanged = null;
            PlaybackChanged = null;
            InspectorChanged = null;
            SettingsChanged = null;
            StatusChanged = null;
        }

        #endregion

        #region Model 同步

        // 配置切换后重置选择和播放头，并按模块注册顺序重建全部投影。
        private void OnConfigChanged()
        {
            selection = SelectionState.None;
            playback.SetSkillConfig(document.CurrentConfig);
            RebuildTimelineFromModel();
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
            PlayheadChanged?.Invoke();
        }

        // Document 内容变化后重建投影，并通过具体模块恢复稳定 GUID 选择。
        private void OnDocumentContentChanged()
        {
            RebuildTimelineFromModel();
            RestoreSelectionAfterTimelineRebuild();
            playback.ClampToDuration();
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
        }

        // 将播放头变化转换为 ViewModel 的细粒度通知。
        private void OnFrameChanged(int _) => PlayheadChanged?.Invoke();

        // 将播放状态变化转换为 ViewModel 的细粒度通知。
        private void OnPlaybackChanged() => PlaybackChanged?.Invoke();

        // 将固定编辑器设置变化转换为 ViewModel 通知。
        private void OnSettingsChanged() => SettingsChanged?.Invoke();

        // 每个模块只投影自身显式运行时列表，ViewModel 不判断具体轨道类型。
        private void RebuildTimelineFromModel()
        {
            groups.Clear();
            foreach (TrackModule module in modules.Modules)
                groups.Add(module.Projection.CreateGroup(document.CurrentConfig));
        }

        // 通过选择具体类型定位模块，再在对应分组中按稳定 GUID 查找显示对象。
        private IViewData FindSelectedViewData()
        {
            if (selection is NoneSelection) return null;
            TrackModule module = modules.Get(selection);
            GroupViewData group = groups.FirstOrDefault(candidate =>
                candidate.GetType() == module.Projection.GroupType);
            return module.Projection.FindSelection(group, selection);
        }

        // Undo、重排或投影重建后，仅当模块仍能找到目标时保留选择。
        private bool SelectionStillExists() =>
            selection is NoneSelection || FindSelectedViewData() != null;

        // 跨轨道移动或其 Undo/Redo 会改变 TrackId；ItemId 全局唯一时可在同一模块内恢复实际归属轨道。
        private void RestoreSelectionAfterTimelineRebuild()
        {
            if (SelectionStillExists()) return;
            if (selection is not ItemSelection itemSelection)
            {
                selection = SelectionState.None;
                return;
            }

            TrackModule module = modules.Get(selection);
            GroupViewData group = groups.FirstOrDefault(candidate =>
                candidate.GetType() == module.Projection.GroupType);
            TrackViewData actualTrack = group?.Tracks.FirstOrDefault(track =>
                track.Items.Any(item => item.Id == itemSelection.ItemId));
            selection = actualTrack == null
                ? SelectionState.None
                : module.Projection.CreateItemSelection(actualTrack.Id, itemSelection.ItemId);
        }

        #endregion

        #region View 意图

        /// <summary>
        /// 停止当前播放并切换到指定技能配置。
        /// </summary>
        public void OpenConfig(SkillConfig config) => document.Open(config);

        /// <summary>
        /// 创建 SkillConfig 资产并将其设为当前编辑文档。
        /// </summary>
        public SkillConfig CreateConfig(string path)
        {
            SkillConfig config = document.CreateConfig(path);
            SetStatus($"已创建 {config.name}。");
            return config;
        }

        /// <summary>
        /// 更新当前选择，并通知时间轴与 Inspector 刷新。
        /// </summary>
        public void Select(SelectionState next)
        {
            next ??= SelectionState.None;
            if (selection.Equals(next)) return;
            selection = next;
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
        }

        /// <summary>
        /// 选择具体轨道分组。
        /// </summary>
        public void SelectGroup(GroupViewData group) => Select(modules.Get(group).Projection.CreateGroupSelection());

        /// <summary>
        /// 选择具体轨道。
        /// </summary>
        public void SelectTrack(TrackViewData track) =>
            Select(modules.Get(track).Projection.CreateTrackSelection(track.Id));

        /// <summary>
        /// 选择具体轨道中的内容项。
        /// </summary>
        public void SelectItem(TrackViewData track, ItemViewData item) =>
            Select(modules.Get(item).Projection.CreateItemSelection(track.Id, item.Id));

        /// <summary>
        /// 判断当前选择是否指向指定分组。
        /// </summary>
        public bool IsSelected(GroupViewData group) =>
            modules.Get(group).Projection.CreateGroupSelection().Equals(selection);

        /// <summary>
        /// 判断当前选择是否指向指定轨道。
        /// </summary>
        public bool IsSelected(TrackViewData track) =>
            modules.Get(track).Projection.CreateTrackSelection(track.Id).Equals(selection);

        /// <summary>
        /// 判断当前选择是否指向指定内容项。
        /// </summary>
        public bool IsSelected(TrackViewData track, ItemViewData item) =>
            modules.Get(item).Projection.CreateItemSelection(track.Id, item.Id).Equals(selection);

        /// <summary>
        /// 将用户指定帧交给播放控制器定位。
        /// </summary>
        public void SetCurrentFrame(int frame) => playback.Seek(frame);

        /// <summary>
        /// 从当前帧开始播放时间轴。
        /// </summary>
        public void Play() => playback.Play();

        /// <summary>
        /// 暂停时间轴并保留当前帧。
        /// </summary>
        public void Pause() => playback.Pause();

        /// <summary>
        /// 停止播放并将播放头复位到第 0 帧。
        /// </summary>
        public void Stop() => playback.Stop();

        /// <summary>
        /// 将播放头移动到上一帧。
        /// </summary>
        public void StepPreviousFrame() => playback.StepPreviousFrame();

        /// <summary>
        /// 将播放头移动到下一帧。
        /// </summary>
        public void StepNextFrame() => playback.StepNextFrame();

        /// <summary>
        /// 保存固定预览场景的资产 GUID。
        /// </summary>
        public void SetPreviewScene(SceneAsset scene) => previewSceneService.SetPreviewScene(scene);

        /// <summary>
        /// 保存固定演示角色的 GlobalObjectId。
        /// </summary>
        public void SetPreviewActor(GameObject actor) => previewSceneService.SetPreviewActor(actor);

        /// <summary>
        /// 询问保存当前场景后打开固定预览场景。
        /// </summary>
        public void OpenPreviewScene() => previewSceneService.OpenPreviewScene();

        /// <summary>
        /// 修改配置帧率，并在保持实际时间的前提下重采样全部内容。
        /// </summary>
        public void ChangeFrameRate(int value) => Report(document.ChangeFrameRate(value));

        /// <summary>
        /// 修改时间轴总帧数，并拒绝截断现有内容。
        /// </summary>
        public void SetDurationFrames(int value) => Report(document.SetDurationFrames(value));

        /// <summary>
        /// 把时间轴总帧数裁剪到容纳现有内容所需的长度。
        /// </summary>
        public void TrimToContent() => document.TrimToContent();

        /// <summary>
        /// 在指定分组中创建轨道并选中新轨道。
        /// </summary>
        public void AddTrack(GroupViewData group)
        {
            TrackModule module = modules.Get(group);
            string id = document.AddTrack(module.Document);
            if (!string.IsNullOrEmpty(id)) Select(module.Projection.CreateTrackSelection(id));
        }

        /// <summary>
        /// 删除当前选择的轨道。
        /// </summary>
        public void RemoveSelectedTrack()
        {
            if (selection is not TrackSelection) return;
            TrackModule module = modules.Get(selection);
            Report(document.RemoveTrack(module.Document, selection.TrackId));
        }

        /// <summary>
        /// 将当前所选轨道向前或向后重排。
        /// </summary>
        public void MoveSelectedTrack(int offset)
        {
            if (selection is not TrackSelection) return;
            TrackModule module = modules.Get(selection);
            Report(document.MoveTrack(module.Document, selection.TrackId, offset));
        }

        /// <summary>
        /// 提交当前所选轨道的名称、静音和锁定设置。
        /// </summary>
        public void EditSelectedTrack(string displayName, bool muted, bool locked)
        {
            if (selection is not TrackSelection) return;
            TrackModule module = modules.Get(selection);
            Report(document.EditTrack(module.Document, selection.TrackId, displayName, muted, locked));
        }

        /// <summary>
        /// 提交指定轨道的最终显示名称，供标题栏内联编辑在选择变化时仍按稳定 GUID 定位目标。
        /// </summary>
        /// <param name="track">包含稳定轨道 GUID 和当前公共状态的只读投影。</param>
        /// <param name="displayName">回车或失焦后提交的最终名称。</param>
        public void RenameTrack(TrackViewData track, string displayName)
        {
            if (track == null) return;
            TrackModule module = modules.Get(track);
            Report(document.EditTrack(module.Document, track.Id,
                displayName, track.Muted, track.Locked));
        }

        /// <summary>
        /// 在指定轨道末尾的可用帧创建默认内容项。
        /// </summary>
        public void AddItem(TrackViewData track)
        {
            TrackModule module = modules.Get(track);
            string id = document.AddItem(module.Document, track.Id);
            if (!string.IsNullOrEmpty(id)) Select(module.Projection.CreateItemSelection(track.Id, id));
        }

        /// <summary>
        /// 按轨道模块处理类型化批量创建请求，并在成功后选择第一个新内容项。
        /// </summary>
        public EditResult CreateItems(TrackViewData track, IItemCreateRequest request)
        {
            if (track == null) return EditResult.Failure("目标轨道不存在。");
            TrackModule module = modules.Get(track);
            ItemsCreateResult result = document.CreateItems(module.Document, track.Id, request);
            Report(result.EditResult);
            if (result.Succeeded && result.ItemIds.Count > 0)
                Select(module.Projection.CreateItemSelection(track.Id, result.ItemIds[0]));
            return result.EditResult;
        }

        /// <summary>
        /// 删除当前选择的片段或事件标记。
        /// </summary>
        public void RemoveSelectedItem()
        {
            if (selection is not ItemSelection) return;
            TrackModule module = modules.Get(selection);
            Report(document.RemoveItem(module.Document, selection.TrackId, selection.ItemId));
        }

        /// <summary>
        /// 复制当前选择的片段或事件标记并选择副本。
        /// </summary>
        public void DuplicateSelectedItem()
        {
            if (selection is not ItemSelection) return;
            TrackModule module = modules.Get(selection);
            string id = document.DuplicateItem(module.Document, selection.TrackId, selection.ItemId);
            if (!string.IsNullOrEmpty(id)) Select(module.Projection.CloneItemSelection(selection, id));
        }

        /// <summary>
        /// 校验并移动指定内容项到目标帧。
        /// </summary>
        public EditResult MoveItem(TrackViewData track, ItemViewData item, int startFrame)
        {
            TrackModule module = modules.Get(track);
            EditResult result = document.MoveItem(module.Document, track.Id, item.Id, startFrame);
            Report(result);
            return result;
        }

        /// <summary>
        /// 获取当前分组中紧邻指定轨道的上一条或下一条同类轨道。
        /// </summary>
        /// <param name="sourceTrack">作为相邻查询起点的轨道投影。</param>
        /// <param name="offset">仅接受 -1 表示上方，或 1 表示下方。</param>
        /// <returns>存在相邻轨道时返回其投影，否则返回空。</returns>
        public TrackViewData GetAdjacentTrack(TrackViewData sourceTrack, int offset)
        {
            if (sourceTrack == null || (offset != -1 && offset != 1)) return null;
            TrackModule module = modules.Get(sourceTrack);
            GroupViewData group = groups.FirstOrDefault(candidate =>
                candidate.GetType() == module.Projection.GroupType);
            if (group == null) return null;
            int sourceIndex = -1;
            for (int index = 0; index < group.Tracks.Count; index++)
            {
                TrackViewData candidate = group.Tracks[index];
                if (candidate.GetType() != sourceTrack.GetType() || candidate.Id != sourceTrack.Id) continue;
                sourceIndex = index;
                break;
            }

            int targetIndex = sourceIndex + offset;
            return sourceIndex >= 0 && targetIndex >= 0 && targetIndex < group.Tracks.Count
                ? group.Tracks[targetIndex]
                : null;
        }

        /// <summary>
        /// 只读检查 Item 能否保持原帧区间移动到指定同模块轨道。
        /// </summary>
        /// <param name="sourceTrack">当前持有 Item 的源轨道。</param>
        /// <param name="item">需要移动的 Item。</param>
        /// <param name="targetTrack">相邻的目标轨道。</param>
        /// <returns>可移动时返回成功，否则携带具体禁用原因。</returns>
        public EditResult CanMoveItemToTrack(TrackViewData sourceTrack, ItemViewData item,
            TrackViewData targetTrack)
        {
            if (sourceTrack == null || item == null || targetTrack == null)
                return EditResult.Failure("不存在可用的相邻轨道。");
            TrackModule sourceModule = modules.Get(sourceTrack);
            if (!ReferenceEquals(sourceModule, modules.Get(item)) ||
                !ReferenceEquals(sourceModule, modules.Get(targetTrack)))
                return EditResult.Failure("只能在相同类型的轨道之间移动内容。");
            return document.CanMoveItemToTrack(
                sourceModule.Document, sourceTrack.Id, targetTrack.Id, item.Id);
        }

        /// <summary>
        /// 把 Item 保持原帧区间移动到指定同模块轨道，并报告事务结果。
        /// </summary>
        /// <param name="sourceTrack">当前持有 Item 的源轨道。</param>
        /// <param name="item">需要移动的 Item。</param>
        /// <param name="targetTrack">相邻的目标轨道。</param>
        /// <returns>跨轨道事务的成功状态或失败原因。</returns>
        public EditResult MoveItemToTrack(TrackViewData sourceTrack, ItemViewData item,
            TrackViewData targetTrack)
        {
            EditResult availability = CanMoveItemToTrack(sourceTrack, item, targetTrack);
            if (!availability.Succeeded)
            {
                Report(availability);
                return availability;
            }

            TrackModule module = modules.Get(sourceTrack);
            EditResult result = document.MoveItemToTrack(
                module.Document, sourceTrack.Id, targetTrack.Id, item.Id);
            Report(result);
            return result;
        }

        /// <summary>
        /// 校验并提交指定片段的新半开帧区间。
        /// </summary>
        public EditResult ResizeItem(TrackViewData track, ItemViewData item,
            int startFrame, int duration)
        {
            TrackModule module = modules.Get(track);
            EditResult result = document.ResizeItem(
                module.Document, track.Id, item.Id, startFrame, duration);
            Report(result);
            return result;
        }

        /// <summary>
        /// 把类型化字段请求交给当前内容所属模块处理。
        /// </summary>
        public void EditItem(ItemViewData item, IItemEditRequest request)
        {
            if (item == null || selection is not ItemSelection || selection.ItemId != item.Id) return;
            TrackModule module = modules.Get(item);
            EditResult result = document.EditItem(module.Document, selection.TrackId, item.Id, request);
            Report(result);
            if (!result.Succeeded) InspectorChanged?.Invoke();
        }

        #endregion

        #region 状态提示

        // 把编辑结果转换为窗口底部状态提示。
        private void Report(EditResult result) =>
            SetStatus(result.Succeeded ? "操作完成。" : result.Message);

        // 更新状态提示并发送细粒度状态通知。
        private void SetStatus(string message)
        {
            StatusMessage = message ?? string.Empty;
            StatusChanged?.Invoke();
        }

        #endregion
    }
}
#endif
