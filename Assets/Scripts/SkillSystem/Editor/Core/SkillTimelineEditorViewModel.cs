#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 将技能配置投影为时间轴只读数据，并接收 View 提交的语义编辑意图。
    /// </summary>
    internal sealed class SkillTimelineEditorViewModel : IViewModel
    {
        #region Fields

        private readonly SkillTimelineEditorDocument document;
        private readonly SkillTimelinePlaybackController playback;
        private readonly SkillTimelinePreviewSceneService previewSceneService;
        private readonly List<SkillTimelineGroupViewData> groups = new();
        private SkillTimelineSelection selection = SkillTimelineSelection.None;
        private bool disposed;

        #endregion

        #region Events

        public event Action TimelineChanged;
        public event Action SelectionChanged;
        public event Action PlayheadChanged;
        public event Action PlaybackChanged;
        public event Action InspectorChanged;
        public event Action SettingsChanged;
        public event Action StatusChanged;

        #endregion

        #region Properties

        public IReadOnlyList<SkillTimelineGroupViewData> Groups => groups;
        public SkillTimelineSelection Selection => selection;
        public SkillConfig CurrentConfig => document.CurrentConfig;
        public int CurrentFrame => playback.CurrentFrame;
        public bool IsPlaying => playback.IsPlaying;
        public SceneAsset PreviewScene => previewSceneService.PreviewScene;
        public GameObject PreviewActor => previewSceneService.PreviewActor;
        public string StatusMessage { get; private set; } = "请选择或新建 SkillConfig。";
        public IViewData SelectedViewData => FindSelectedViewData();

        #endregion

        #region Lifecycle

        /// <summary>
        /// 创建 ViewModel 并订阅 Document、Playback 和编辑器设置事件。
        /// </summary>
        public SkillTimelineEditorViewModel(SkillTimelineEditorDocument document,
            SkillTimelinePlaybackController playback, SkillTimelinePreviewSceneService previewSceneService)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.previewSceneService = previewSceneService ?? throw new ArgumentNullException(nameof(previewSceneService));
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

        #region Model projection

        // 配置切换后重置选择、播放头并重建全部显示投影。
        private void OnConfigChanged()
        {
            selection = SkillTimelineSelection.None;
            playback.SetSkillConfig(document.CurrentConfig);
            RebuildTimelineFromModel();
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
            PlayheadChanged?.Invoke();
        }

        // Document 内容变化后重建投影，并按稳定 GUID 校验当前选择。
        private void OnDocumentContentChanged(SkillTimelineContentChangedEventArgs eventArgs)
        {
            RebuildTimelineFromModel();
            if (!SelectionStillExists()) selection = SkillTimelineSelection.None;
            playback.ClampToDuration();
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
            SetStatus(document.Validate().FirstOrDefault() ?? "配置有效。");
        }

        // 将播放头变化转换为 ViewModel 的细粒度通知。
        private void OnFrameChanged(int _) => PlayheadChanged?.Invoke();

        // 将播放状态变化转换为 ViewModel 的细粒度通知。
        private void OnPlaybackChanged() => PlaybackChanged?.Invoke();

        // 将固定编辑器设置变化转换为 ViewModel 通知。
        private void OnSettingsChanged() => SettingsChanged?.Invoke();

        // 从当前 SkillConfig 的三套显式列表重建具体 Group、Track 和 Item ViewData。
        private void RebuildTimelineFromModel()
        {
            groups.Clear();
            SkillConfig config = document.CurrentConfig;
            if (config == null)
            {
                groups.Add(new AnimationTimelineGroupViewData(Array.Empty<SkillTimelineTrackViewData>()));
                groups.Add(new VfxTimelineGroupViewData(Array.Empty<SkillTimelineTrackViewData>()));
                groups.Add(new EventTimelineGroupViewData(Array.Empty<SkillTimelineTrackViewData>()));
                return;
            }

            groups.Add(new AnimationTimelineGroupViewData(
                config.AnimationTracks.Select(CreateAnimationTrackViewData).Cast<SkillTimelineTrackViewData>().ToArray()));
            groups.Add(new VfxTimelineGroupViewData(
                config.VfxTracks.Select(CreateVfxTrackViewData).Cast<SkillTimelineTrackViewData>().ToArray()));
            groups.Add(new EventTimelineGroupViewData(
                config.EventTracks.Select(CreateEventTrackViewData).Cast<SkillTimelineTrackViewData>().ToArray()));
        }

        // 把动画轨道及其 Clip 投影为具体只读类型。
        private static AnimationTimelineTrackViewData CreateAnimationTrackViewData(AnimationTrackConfig track)
        {
            SkillTimelineItemViewData[] items = track.Clips.Select(clip =>
                (SkillTimelineItemViewData)new AnimationClipTimelineItemViewData(clip,
                    clip.AnimationClip != null ? clip.AnimationClip.name : "Animation Clip")).ToArray();
            return new AnimationTimelineTrackViewData(track, items);
        }

        // 把特效轨道及其 Clip 投影为具体只读类型。
        private static VfxTimelineTrackViewData CreateVfxTrackViewData(VfxTrackConfig track)
        {
            SkillTimelineItemViewData[] items = track.Clips.Select(clip =>
                (SkillTimelineItemViewData)new VfxClipTimelineItemViewData(clip,
                    clip.Prefab != null ? clip.Prefab.name : "VFX Clip")).ToArray();
            return new VfxTimelineTrackViewData(track, items);
        }

        // 把事件轨道及其 Marker 投影为具体只读类型。
        private static EventTimelineTrackViewData CreateEventTrackViewData(EventTrackConfig track)
        {
            SkillTimelineItemViewData[] items = track.Markers.Select(marker =>
                (SkillTimelineItemViewData)new EventMarkerTimelineItemViewData(marker)).ToArray();
            return new EventTimelineTrackViewData(track, items);
        }

        // 检查选择的具体类型和稳定 GUID 是否仍能匹配当前投影。
        private bool SelectionStillExists()
        {
            if (selection is NoneSkillTimelineSelection) return true;
            if (selection is AnimationGroupTimelineSelection) return groups.OfType<AnimationTimelineGroupViewData>().Any();
            if (selection is VfxGroupTimelineSelection) return groups.OfType<VfxTimelineGroupViewData>().Any();
            if (selection is EventGroupTimelineSelection) return groups.OfType<EventTimelineGroupViewData>().Any();

            SkillTimelineTrackViewData track = FindTrack(selection.TrackId);
            if (track == null || !SelectionMatchesTrack(selection, track)) return false;
            if (selection is TrackTimelineSelection) return true;
            SkillTimelineItemViewData item = track.Items.FirstOrDefault(x => x.Id == selection.ItemId);
            return item != null && SelectionMatchesItem(selection, item);
        }

        // 根据具体选择类型查找 Inspector 应展示的 ViewData。
        private IViewData FindSelectedViewData()
        {
            if (selection is AnimationGroupTimelineSelection)
                return groups.OfType<AnimationTimelineGroupViewData>().FirstOrDefault();
            if (selection is VfxGroupTimelineSelection)
                return groups.OfType<VfxTimelineGroupViewData>().FirstOrDefault();
            if (selection is EventGroupTimelineSelection)
                return groups.OfType<EventTimelineGroupViewData>().FirstOrDefault();

            SkillTimelineTrackViewData track = FindTrack(selection.TrackId);
            if (selection is TrackTimelineSelection) return track;
            return track?.Items.FirstOrDefault(x => x.Id == selection.ItemId);
        }

        // 在全部分组中按稳定 GUID 查找轨道投影。
        private SkillTimelineTrackViewData FindTrack(string trackId) =>
            groups.SelectMany(x => x.Tracks).FirstOrDefault(x => x.Id == trackId);

        // 防止 GUID 偶然相同的情况下把选择恢复到错误轨道类型。
        private static bool SelectionMatchesTrack(SkillTimelineSelection current, SkillTimelineTrackViewData track) =>
            current switch
            {
                AnimationTrackTimelineSelection or AnimationClipTimelineSelection => track is AnimationTimelineTrackViewData,
                VfxTrackTimelineSelection or VfxClipTimelineSelection => track is VfxTimelineTrackViewData,
                EventTrackTimelineSelection or EventMarkerTimelineSelection => track is EventTimelineTrackViewData,
                _ => false
            };

        // 防止 GUID 偶然相同的情况下把选择恢复到错误内容类型。
        private static bool SelectionMatchesItem(SkillTimelineSelection current, SkillTimelineItemViewData item) =>
            current switch
            {
                AnimationClipTimelineSelection => item is AnimationClipTimelineItemViewData,
                VfxClipTimelineSelection => item is VfxClipTimelineItemViewData,
                EventMarkerTimelineSelection => item is EventMarkerTimelineItemViewData,
                _ => false
            };

        #endregion

        #region View intents

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
        /// 更新当前选择并通知时间轴和 Inspector 刷新。
        /// </summary>
        public void Select(SkillTimelineSelection next)
        {
            next ??= SkillTimelineSelection.None;
            if (selection.Equals(next)) return;
            selection = next;
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
        }

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
        /// 在指定具体分组中创建轨道并选中新轨道。
        /// </summary>
        public void AddTrack(SkillTimelineGroupViewData group)
        {
            SkillTrackKind kind = ResolveDocumentKind(group);
            string id = document.AddTrack(kind);
            if (!string.IsNullOrEmpty(id)) Select(CreateTrackSelection(kind, id));
        }

        /// <summary>
        /// 删除当前选择的轨道。
        /// </summary>
        public void RemoveSelectedTrack()
        {
            if (selection is not TrackTimelineSelection) return;
            Report(document.RemoveTrack(ResolveDocumentKind(selection), selection.TrackId));
        }

        /// <summary>
        /// 将当前所选轨道向前或向后重排。
        /// </summary>
        public void MoveSelectedTrack(int offset)
        {
            if (selection is not TrackTimelineSelection) return;
            Report(document.MoveTrack(ResolveDocumentKind(selection), selection.TrackId, offset));
        }

        /// <summary>
        /// 提交当前所选轨道的名称、静音和锁定设置。
        /// </summary>
        public void EditSelectedTrack(string displayName, bool muted, bool locked)
        {
            if (selection is not TrackTimelineSelection) return;
            Report(document.EditTrack(ResolveDocumentKind(selection), selection.TrackId, displayName, muted, locked));
        }

        /// <summary>
        /// 在指定具体轨道末尾的可用帧创建默认内容项。
        /// </summary>
        public void AddItem(SkillTimelineTrackViewData track)
        {
            SkillTrackKind kind = ResolveDocumentKind(track);
            string id = document.AddItem(kind, track.Id);
            if (!string.IsNullOrEmpty(id)) Select(CreateItemSelection(track, id));
        }

        /// <summary>
        /// 删除当前选择的片段或事件标记。
        /// </summary>
        public void RemoveSelectedItem()
        {
            if (selection is not ItemTimelineSelection) return;
            Report(document.RemoveItem(ResolveDocumentKind(selection), selection.TrackId, selection.ItemId));
        }

        /// <summary>
        /// 复制当前选择的片段或事件标记并选择副本。
        /// </summary>
        public void DuplicateSelectedItem()
        {
            if (selection is not ItemTimelineSelection) return;
            string id = document.DuplicateItem(ResolveDocumentKind(selection), selection.TrackId, selection.ItemId);
            if (!string.IsNullOrEmpty(id)) Select(CloneItemSelection(selection, id));
        }

        /// <summary>
        /// 校验并移动指定内容项到目标帧。
        /// </summary>
        public TimelineEditResult MoveItem(SkillTimelineTrackViewData track,
            SkillTimelineItemViewData item, int startFrame)
        {
            TimelineEditResult result = document.MoveItem(ResolveDocumentKind(track), track.Id, item.Id, startFrame);
            Report(result);
            return result;
        }

        /// <summary>
        /// 校验并提交指定片段的新半开帧区间。
        /// </summary>
        public TimelineEditResult ResizeItem(SkillTimelineTrackViewData track,
            SkillTimelineItemViewData item, int startFrame, int duration)
        {
            TimelineEditResult result = document.ResizeItem(
                ResolveDocumentKind(track), track.Id, item.Id, startFrame, duration);
            Report(result);
            return result;
        }

        /// <summary>
        /// 提交当前所选动画片段的类型化编辑请求。
        /// </summary>
        public void EditSelectedAnimationClip(AnimationClipEditRequest request) =>
            Report(document.EditAnimationClip(selection.TrackId, selection.ItemId, request));

        /// <summary>
        /// 提交当前所选特效片段的类型化编辑请求。
        /// </summary>
        public void EditSelectedVfxClip(VfxClipEditRequest request) =>
            Report(document.EditVfxClip(selection.TrackId, selection.ItemId, request));

        /// <summary>
        /// 提交当前所选事件标记的类型化编辑请求。
        /// </summary>
        public void EditSelectedEventMarker(EventMarkerEditRequest request) =>
            Report(document.EditEventMarker(selection.TrackId, selection.ItemId, request));

        // 把编辑结果转换为窗口底部状态提示。
        private void Report(TimelineEditResult result) =>
            SetStatus(result.Succeeded ? "操作完成。" : result.Message);

        // 更新状态提示并发送细粒度状态通知。
        private void SetStatus(string message)
        {
            StatusMessage = message ?? string.Empty;
            StatusChanged?.Invoke();
        }

        #endregion

        #region Document routing

        // 将具体 Group ViewData 映射到 Document 的显式列表类型。
        private static SkillTrackKind ResolveDocumentKind(SkillTimelineGroupViewData group) => group switch
        {
            AnimationTimelineGroupViewData => SkillTrackKind.Animation,
            VfxTimelineGroupViewData => SkillTrackKind.Vfx,
            EventTimelineGroupViewData => SkillTrackKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(group))
        };

        // 将具体 Track ViewData 映射到 Document 的显式列表类型。
        private static SkillTrackKind ResolveDocumentKind(SkillTimelineTrackViewData track) => track switch
        {
            AnimationTimelineTrackViewData => SkillTrackKind.Animation,
            VfxTimelineTrackViewData => SkillTrackKind.Vfx,
            EventTimelineTrackViewData => SkillTrackKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        };

        // 将具体 Selection 映射到 Document 的显式列表类型。
        private static SkillTrackKind ResolveDocumentKind(SkillTimelineSelection current) => current switch
        {
            AnimationTrackTimelineSelection or AnimationClipTimelineSelection => SkillTrackKind.Animation,
            VfxTrackTimelineSelection or VfxClipTimelineSelection => SkillTrackKind.Vfx,
            EventTrackTimelineSelection or EventMarkerTimelineSelection => SkillTrackKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(current))
        };

        // 根据 Document 返回的轨道类型创建对应具体选择。
        private static SkillTimelineSelection CreateTrackSelection(SkillTrackKind kind, string trackId) => kind switch
        {
            SkillTrackKind.Animation => new AnimationTrackTimelineSelection(trackId),
            SkillTrackKind.Vfx => new VfxTrackTimelineSelection(trackId),
            SkillTrackKind.Event => new EventTrackTimelineSelection(trackId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        // 根据具体轨道创建对应内容选择。
        private static SkillTimelineSelection CreateItemSelection(SkillTimelineTrackViewData track, string itemId) => track switch
        {
            AnimationTimelineTrackViewData => new AnimationClipTimelineSelection(track.Id, itemId),
            VfxTimelineTrackViewData => new VfxClipTimelineSelection(track.Id, itemId),
            EventTimelineTrackViewData => new EventMarkerTimelineSelection(track.Id, itemId),
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        };

        // 复制内容后保留原选择的具体类型并替换 Item GUID。
        private static SkillTimelineSelection CloneItemSelection(SkillTimelineSelection current, string itemId) => current switch
        {
            AnimationClipTimelineSelection => new AnimationClipTimelineSelection(current.TrackId, itemId),
            VfxClipTimelineSelection => new VfxClipTimelineSelection(current.TrackId, itemId),
            EventMarkerTimelineSelection => new EventMarkerTimelineSelection(current.TrackId, itemId),
            _ => throw new ArgumentOutOfRangeException(nameof(current))
        };

        #endregion
    }
}
#endif