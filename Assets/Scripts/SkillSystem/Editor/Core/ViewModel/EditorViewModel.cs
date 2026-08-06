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
    /// 保存窗口选择与播放状态，并把直接 Config 引用上的语义意图交给 Document。
    /// </summary>
    internal sealed class EditorViewModel : IViewModel
    {
        #region 字段
        private readonly Document document;
        private readonly PlaybackController playback;
        private readonly PreviewSceneService previewSceneService;
        private readonly TrackModuleRegistry modules;
        private readonly IVfxSceneEditService vfxSceneEditService;
        private readonly IAttackDetectionSceneEditService attackDetectionSceneEditService;
        private SelectionState selection = SelectionState.None;
        private bool disposed;
        #endregion

        #region 事件
        public event Action TimelineChanged;
        public event Action SelectionChanged;
        public event Action SelectionActivated;
        public event Action PlayheadChanged;
        public event Action PlaybackChanged;
        public event Action InspectorChanged;
        public event Action SettingsChanged;
        public event Action StatusChanged;
        #endregion

        #region 属性
        public IReadOnlyList<TrackConfigBase> Tracks =>
            document.CurrentConfig?.Tracks ?? Array.Empty<TrackConfigBase>();
        public SelectionState Selection => selection;
        public SkillConfig CurrentConfig => document.CurrentConfig;
        public int CurrentFrame => playback.CurrentFrame;
        public bool IsPlaying => playback.IsPlaying;
        public bool IsLooping => playback.IsLooping;
        public SceneAsset PreviewScene => previewSceneService.PreviewScene;
        public GameObject PreviewActor => previewSceneService.PreviewActor;
        public bool PreviewApplyRootMotion => previewSceneService.ApplyRootMotion;
        public string StatusMessage { get; private set; } = "请选择或新建 SkillConfig。";
        public TrackConfigBase SelectedTrack => ResolveSelectedTrack();
        public TimelineItemConfigBase SelectedItem => ResolveSelectedItem();
        public object SelectedData => (object)SelectedItem ?? SelectedTrack;
        public TrackModuleRegistry Modules => modules;
        #endregion

        #region 生命周期
        /// <summary>
        /// 创建窗口私有 ViewModel，并订阅文档、播放和 Preview 事件。
        /// </summary>
        public EditorViewModel(Document document, PlaybackController playback,
            PreviewSceneService previewSceneService, TrackModuleRegistry modules,
            IVfxSceneEditService vfxSceneEditService,
            IAttackDetectionSceneEditService attackDetectionSceneEditService)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
            this.playback = playback ?? throw new ArgumentNullException(nameof(playback));
            this.previewSceneService = previewSceneService ?? throw new ArgumentNullException(nameof(previewSceneService));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.vfxSceneEditService = vfxSceneEditService ?? throw new ArgumentNullException(nameof(vfxSceneEditService));
            this.attackDetectionSceneEditService = attackDetectionSceneEditService ??
                                                   throw new ArgumentNullException(nameof(attackDetectionSceneEditService));
            attackDetectionSceneEditService.EditCommitted += OnAttackDetectionSceneEditCommitted;
            document.ContentChanged += OnDocumentContentChanged;
            document.ConfigChanged += OnConfigChanged;
            playback.FrameChanged += OnFrameChanged;
            playback.PlaybackChanged += OnPlaybackChanged;
            playback.PreviewStatusChanged += OnPreviewStatusChanged;
            previewSceneService.SettingsChanged += OnSettingsChanged;
            playback.SetPreviewActor(previewSceneService.PreviewActor);
            playback.SetApplyRootMotion(previewSceneService.ApplyRootMotion);
        }

        /// <summary>
        /// 释放全部外部事件订阅。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            attackDetectionSceneEditService.EditCommitted -= OnAttackDetectionSceneEditCommitted;
            document.ContentChanged -= OnDocumentContentChanged;
            document.ConfigChanged -= OnConfigChanged;
            playback.FrameChanged -= OnFrameChanged;
            playback.PlaybackChanged -= OnPlaybackChanged;
            playback.PreviewStatusChanged -= OnPreviewStatusChanged;
            previewSceneService.SettingsChanged -= OnSettingsChanged;
            TimelineChanged = null;
            SelectionChanged = null;
            SelectionActivated = null;
            PlayheadChanged = null;
            PlaybackChanged = null;
            InspectorChanged = null;
            SettingsChanged = null;
            StatusChanged = null;
        }
        #endregion

        #region 模型同步
        // Config 切换时清空稳定选择并重置播放上下文。
        private void OnConfigChanged()
        {
            selection = SelectionState.None;
            SynchronizeAttackDetectionSelection();
            playback.SetSkillConfig(document.CurrentConfig);
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
            PlayheadChanged?.Invoke();
        }

        // 内容变化后按 GUID 恢复直接引用选择并刷新 Preview。
        private void OnDocumentContentChanged()
        {
            RestoreSelection();
            SynchronizeAttackDetectionSelection();
            playback.InvalidatePreviewContent();
            playback.ClampToDuration();
            TimelineChanged?.Invoke();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
        }

        // 转发权威播放头变化。
        private void OnFrameChanged(int _) => PlayheadChanged?.Invoke();

        // 转发播放状态变化。
        private void OnPlaybackChanged() => PlaybackChanged?.Invoke();

        // EditorSettings 变化后重建 Preview 上下文。
        private void OnSettingsChanged()
        {
            playback.ClearPreview();
            playback.SetPreviewActor(previewSceneService.PreviewActor);
            playback.SetApplyRootMotion(previewSceneService.ApplyRootMotion);
            playback.RefreshPreview();
            SettingsChanged?.Invoke();
        }

        // 将 Preview 状态写入窗口状态栏。
        private void OnPreviewStatusChanged(string message) => SetStatus(message);

        // 将 Scene Handle 完成后的攻击检测快照提交为一条 Item 编辑事务。
        private void OnAttackDetectionSceneEditCommitted(AttackDetectionSceneEditCommit commit)
        {
            if (SelectedItem is not AttackDetectionSkillClipConfig clip || clip.Id != commit.ClipId) return;
            EditItem(SelectedTrack, clip, new AttackDetectionEditRequest(
                clip.StartFrame, clip.DurationFrames, clip.SampleIntervalFrames, commit.DetectionData));
        }

        // Scene Handle 只接收当前攻击检测 Item 的稳定 GUID。
        private void SynchronizeAttackDetectionSelection() =>
            attackDetectionSceneEditService.SetSelectedClip(
                SelectedItem is AttackDetectionSkillClipConfig clip ? clip.Id : string.Empty);

        // Undo 跨轨移动时先按 Item GUID 在统一列表中恢复实际所属 Track；数据已删除才清空选择。
        private void RestoreSelection()
        {
            if (selection is NoneSelection) return;
            TrackConfigBase track = ResolveSelectedTrack();
            if (selection is not ItemSelection)
            {
                if (track == null) selection = SelectionState.None;
                return;
            }
            if (track != null && track.Items.Any(item => item.Id == selection.ItemId)) return;
            TrackConfigBase actualTrack = Tracks.FirstOrDefault(candidate => candidate != null &&
                candidate.Items.Any(item => item.Id == selection.ItemId));
            selection = actualTrack != null
                ? new ItemSelection(actualTrack.Id, selection.ItemId)
                : SelectionState.None;
        }

        // 按统一列表查找选择轨道。
        private TrackConfigBase ResolveSelectedTrack() =>
            Tracks.FirstOrDefault(track => track != null && track.Id == selection.TrackId);

        // 在已选轨道实际 Item 列表中查找选择内容。
        private TimelineItemConfigBase ResolveSelectedItem() =>
            ResolveSelectedTrack()?.Items.FirstOrDefault(item => item.Id == selection.ItemId);
        #endregion

        #region 配置与选择
        /// <summary>
        /// 打开指定 SkillConfig。
        /// </summary>
        public void OpenConfig(SkillConfig config) => document.Open(config);

        /// <summary>
        /// 创建并打开 SkillConfig。
        /// </summary>
        public SkillConfig CreateConfig(string path)
        {
            SkillConfig config = document.CreateConfig(path);
            SetStatus("已创建 SkillConfig。");
            return config;
        }

        /// <summary>
        /// 设置稳定选择并同步 Scene Handle。
        /// </summary>
        public void Select(SelectionState next)
        {
            next ??= SelectionState.None;
            if (selection.Equals(next))
            {
                SelectionActivated?.Invoke();
                return;
            }
            selection = next;
            SelectionActivated?.Invoke();
            SynchronizeAttackDetectionSelection();
            SelectionChanged?.Invoke();
            InspectorChanged?.Invoke();
        }

        /// <summary>
        /// 选择实际 Track 子资产。
        /// </summary>
        public void SelectTrack(TrackConfigBase track) =>
            Select(track == null ? SelectionState.None : new TrackSelection(track.Id));

        /// <summary>
        /// 选择 Track 内的实际 Item Config。
        /// </summary>
        public void SelectItem(TrackConfigBase track, TimelineItemConfigBase item) =>
            Select(track == null || item == null
                ? SelectionState.None : new ItemSelection(track.Id, item.Id));

        /// <summary>
        /// 判断实际 Track 是否被选中。
        /// </summary>
        public bool IsSelected(TrackConfigBase track) =>
            track != null && selection is TrackSelection && selection.TrackId == track.Id;

        /// <summary>
        /// 判断实际 Item 是否被选中。
        /// </summary>
        public bool IsSelected(TrackConfigBase track, TimelineItemConfigBase item) =>
            track != null && item != null && selection is ItemSelection &&
            selection.TrackId == track.Id && selection.ItemId == item.Id;
        #endregion

        #region 播放与预览
        /// <summary>跳转到整数帧。</summary>
        public void SetCurrentFrame(int frame) => playback.Seek(frame);
        /// <summary>开始播放。</summary>
        public void Play() => playback.Play();
        /// <summary>暂停播放。</summary>
        public void Pause() => playback.Pause();
        /// <summary>停止并回到第零帧。</summary>
        public void Stop() => playback.Stop();
        /// <summary>设置全技能循环播放。</summary>
        public void SetLooping(bool value) => playback.SetLooping(value);
        /// <summary>后退一帧。</summary>
        public void StepPreviousFrame() => playback.StepPreviousFrame();
        /// <summary>前进一帧。</summary>
        public void StepNextFrame() => playback.StepNextFrame();
        /// <summary>设置预览场景。</summary>
        public void SetPreviewScene(SceneAsset scene) => previewSceneService.SetPreviewScene(scene);
        /// <summary>设置预览角色。</summary>
        public void SetPreviewActor(GameObject actor) => previewSceneService.SetPreviewActor(actor);
        /// <summary>设置预览 Root Motion。</summary>
        public void SetPreviewApplyRootMotion(bool value) => previewSceneService.SetApplyRootMotion(value);
        /// <summary>打开预览场景。</summary>
        public void OpenPreviewScene() => previewSceneService.OpenPreviewScene();
        #endregion

        #region 时间轴与 Track 操作
        /// <summary>修改 FPS。</summary>
        public void ChangeFrameRate(int value) => Report(document.ChangeFrameRate(value));
        /// <summary>修改总帧。</summary>
        public void SetDurationFrames(int value) => Report(document.SetDurationFrames(value));
        /// <summary>裁剪到内容。</summary>
        public void TrimToContent() => document.TrimToContent();

        /// <summary>
        /// 追加一个模块声明的 Track 子资产。
        /// </summary>
        public void AddTrack(TrackModule module)
        {
            string id = document.AddTrack(module);
            if (!string.IsNullOrEmpty(id)) Select(new TrackSelection(id));
        }

        /// <summary>
        /// 按类型声明顺序稳定重排全部轨道。
        /// </summary>
        public void SortTracksByType() => Report(document.SortTracksByType(modules));

        /// <summary>删除所选 Track。</summary>
        public void RemoveSelectedTrack()
        {
            TrackConfigBase track = SelectedTrack;
            if (track == null) return;
            EditResult result = document.RemoveTrack(track.Id);
            if (result.Succeeded) Select(SelectionState.None);
            Report(result);
        }

        /// <summary>将所选 Track 移动一个物理行。</summary>
        public void MoveSelectedTrack(int offset)
        {
            if (SelectedTrack != null) Report(document.MoveTrack(SelectedTrack.Id, offset));
        }

        /// <summary>将指定 Track 移动到统一列表插入边界。</summary>
        public EditResult MoveTrack(TrackConfigBase track, int insertionIndex)
        {
            EditResult result = track != null
                ? document.MoveTrackToIndex(track.Id, insertionIndex)
                : EditResult.Failure("轨道不存在。");
            Report(result);
            return result;
        }

        /// <summary>修改所选 Track 公共字段。</summary>
        public void EditSelectedTrack(string displayName, bool muted, bool locked)
        {
            if (SelectedTrack != null)
                Report(document.EditTrack(SelectedTrack.Id, displayName, muted, locked));
        }

        /// <summary>提交 Track 内联重命名。</summary>
        public void RenameTrack(TrackConfigBase track, string displayName)
        {
            if (track != null) Report(document.EditTrack(track.Id, displayName, track.Muted, track.EditorLocked));
        }

        /// <summary>切换 Track 静音。</summary>
        public void SetTrackMuted(TrackConfigBase track, bool muted)
        {
            if (track != null) Report(document.EditTrack(track.Id, track.DisplayName, muted, track.EditorLocked));
        }

        /// <summary>切换 Track 锁定。</summary>
        public void SetTrackLocked(TrackConfigBase track, bool locked)
        {
            if (track != null) Report(document.EditTrack(track.Id, track.DisplayName, track.Muted, locked));
        }
        #endregion

        #region Item 操作
        /// <summary>在 Track 中添加默认 Item。</summary>
        public void AddItem(TrackConfigBase track)
        {
            if (track == null) return;
            string id = document.AddItem(modules.Get(track).Document, track.Id);
            if (!string.IsNullOrEmpty(id)) Select(new ItemSelection(track.Id, id));
        }

        /// <summary>通过类型化请求批量创建 Item。</summary>
        public ItemsCreateResult CreateItems(TrackConfigBase track, IItemCreateRequest request)
        {
            ItemsCreateResult result = document.CreateItems(modules.Get(track).Document, track.Id, request);
            if (result.Succeeded && result.ItemIds.Count > 0)
                Select(new ItemSelection(track.Id, result.ItemIds[result.ItemIds.Count - 1]));
            Report(result.EditResult);
            return result;
        }

        /// <summary>删除所选 Item。</summary>
        public void RemoveSelectedItem()
        {
            if (SelectedTrack == null || SelectedItem == null) return;
            TrackConfigBase track = SelectedTrack;
            TimelineItemConfigBase item = SelectedItem;
            EditResult result = document.RemoveItem(modules.Get(track).Document, track.Id, item.Id);
            if (result.Succeeded) Select(new TrackSelection(track.Id));
            Report(result);
        }

        /// <summary>复制所选 Item。</summary>
        public void DuplicateSelectedItem()
        {
            if (SelectedTrack == null || SelectedItem == null) return;
            string id = document.DuplicateItem(modules.Get(SelectedTrack).Document,
                SelectedTrack.Id, SelectedItem.Id);
            if (!string.IsNullOrEmpty(id)) Select(new ItemSelection(SelectedTrack.Id, id));
        }

        /// <summary>只读校验 Item 在当前 Track 的目标帧。</summary>
        public EditResult CanMoveItem(TrackConfigBase track, TimelineItemConfigBase item, int startFrame) =>
            document.CanMoveItem(modules.Get(track).Document, track.Id, item.Id, startFrame);

        /// <summary>移动 Item 起始帧。</summary>
        public EditResult MoveItem(TrackConfigBase track, TimelineItemConfigBase item, int startFrame)
        {
            EditResult result = document.MoveItem(modules.Get(track).Document, track.Id, item.Id, startFrame);
            Report(result);
            return result;
        }

        /// <summary>校验 Item 的同类型跨轨移动。</summary>
        public EditResult CanMoveItemToTrack(TrackConfigBase sourceTrack,
            TimelineItemConfigBase item, TrackConfigBase targetTrack, int startFrame) =>
            document.CanMoveItemToTrack(modules.Get(sourceTrack).Document,
                sourceTrack.Id, targetTrack.Id, item.Id, startFrame);

        /// <summary>提交 Item 的同类型跨轨移动。</summary>
        public EditResult MoveItemToTrack(TrackConfigBase sourceTrack,
            TimelineItemConfigBase item, TrackConfigBase targetTrack, int startFrame)
        {
            EditResult result = document.MoveItemToTrack(modules.Get(sourceTrack).Document,
                sourceTrack.Id, targetTrack.Id, item.Id, startFrame);
            if (result.Succeeded) Select(new ItemSelection(targetTrack.Id, item.Id));
            Report(result);
            return result;
        }

        /// <summary>裁剪 Item 区间。</summary>
        public EditResult ResizeItem(TrackConfigBase track, TimelineItemConfigBase item,
            int startFrame, int durationFrames)
        {
            EditResult result = document.ResizeItem(modules.Get(track).Document,
                track.Id, item.Id, startFrame, durationFrames);
            Report(result);
            return result;
        }

        /// <summary>提交类型化 Item 编辑请求。</summary>
        public EditResult EditItem(TrackConfigBase track, TimelineItemConfigBase item,
            IItemEditRequest request)
        {
            EditResult result = document.EditItem(modules.Get(track).Document, track.Id, item.Id, request);
            Report(result);
            if (!result.Succeeded) InspectorChanged?.Invoke();
            return result;
        }

        /// <summary>判断动画持续帧是否可匹配素材原始长度。</summary>
        public bool CanMatchAnimationDuration(AnimationSkillClipConfig item)
        {
            if (item?.AnimationClip == null || CurrentConfig == null) return false;
            return item.DurationFrames != Mathf.Max(1,
                Mathf.CeilToInt(item.AnimationClip.length * CurrentConfig.FrameRate));
        }

        /// <summary>将动画持续帧匹配到素材原始长度。</summary>
        public EditResult MatchAnimationDuration(AnimationSkillClipConfig item)
        {
            if (item?.AnimationClip == null || SelectedTrack == null)
                return EditResult.Failure("动画素材为空。");
            int duration = Mathf.Max(1,
                Mathf.CeilToInt(item.AnimationClip.length * CurrentConfig.FrameRate));
            return EditItem(SelectedTrack, item, new AnimationEditRequest(
                item.AnimationClip, item.StartFrame, duration, item.SourceStartFrame, item.PlaybackSpeed));
        }
        #endregion

        #region VFX 场景代理
        /// <summary>开始 VFX 场景代理编辑。</summary>
        public void BeginVfxSceneEdit(VfxSkillClipConfig item)
        {
            if (item != null) Report(vfxSceneEditService.BeginEdit(item));
        }

        /// <summary>判断 VFX Item 是否正在编辑代理。</summary>
        public bool IsVfxSceneEditing(VfxSkillClipConfig item) =>
            item != null && vfxSceneEditService.IsEditing(item.Id);

        /// <summary>重新选择 VFX 编辑代理。</summary>
        public void SelectVfxSceneEditProxy(VfxSkillClipConfig item)
        {
            if (item != null) Report(vfxSceneEditService.SelectProxy(item.Id));
        }

        /// <summary>捕获 VFX 代理 Transform 并提交 Item 编辑。</summary>
        public void ApplyVfxSceneEdit(VfxSkillClipConfig item)
        {
            if (item == null || SelectedTrack == null) return;
            EditResult capture = vfxSceneEditService.Capture(item.Id, out VfxTransformSnapshot snapshot);
            if (!capture.Succeeded)
            {
                Report(capture);
                return;
            }
            EditResult result = EditItem(SelectedTrack, item, new VfxEditRequest(
                item.Prefab, item.MarkerKey, item.StartFrame, item.DurationFrames,
                snapshot.LocalPosition, snapshot.LocalEulerAngles, snapshot.LocalScale,
                item.PlaybackSpeed, item.FollowMode, item.StopMode));
            if (result.Succeeded) vfxSceneEditService.CancelEdit();
        }

        /// <summary>取消 VFX 场景代理编辑。</summary>
        public void CancelVfxSceneEdit() => vfxSceneEditService.CancelEdit();
        #endregion

        #region 状态
        // 把语义操作结果写入状态栏。
        private void Report(EditResult result) =>
            SetStatus(result.Succeeded ? "操作完成。" : result.Message);

        // 更新状态文本并通知 View。
        private void SetStatus(string message)
        {
            StatusMessage = message ?? string.Empty;
            StatusChanged?.Invoke();
        }
        #endregion
    }
}
#endif
