#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 统一修改 SkillConfig 根资产及 Track 子资产，并负责 Undo、区间约束和变更通知。
    /// </summary>
    internal sealed class Document : IDisposable
    {
        #region 字段、事件与属性
        private readonly IReadOnlyList<ITrackDocumentHandler> handlers;
        private SerializedObject serializedObject;
        public event Action ContentChanged;
        public event Action ConfigChanged;
        public SkillConfig CurrentConfig { get; private set; }
        public SerializedObject SerializedObject => serializedObject;
        private bool HasConfig => CurrentConfig != null && serializedObject != null;
        #endregion

        #region 生命周期与根配置
        /// <summary>
        /// 创建 Document 并订阅 Undo/Redo。
        /// </summary>
        public Document(IReadOnlyList<ITrackDocumentHandler> handlers)
        {
            this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        /// <summary>
        /// 释放事件和序列化上下文。
        /// </summary>
        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            CurrentConfig = null;
            serializedObject = null;
            ContentChanged = null;
            ConfigChanged = null;
        }

        /// <summary>
        /// 打开技能配置并修复根、Track 与 Item 的稳定 GUID。
        /// </summary>
        public void Open(SkillConfig config)
        {
            if (CurrentConfig == config) return;
            CurrentConfig = config;
            serializedObject = config != null ? new SerializedObject(config) : null;
            if (config != null) EnsureStableIds();
            ConfigChanged?.Invoke();
            ContentChanged?.Invoke();
        }

        /// <summary>
        /// 创建空 SkillConfig 根资产。
        /// </summary>
        public SkillConfig CreateConfig(string assetPath)
        {
            SkillConfig config = ScriptableObject.CreateInstance<SkillConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();
            Open(config);
            return config;
        }

        /// <summary>
        /// 修改 FPS，并保持全部帧字段的实际时间。
        /// </summary>
        public EditResult ChangeFrameRate(int frameRate)
        {
            if (!HasConfig) return EditResult.Failure("请先选择 SkillConfig。");
            frameRate = Mathf.Clamp(frameRate, 1, 240);
            int oldRate = CurrentConfig.FrameRate;
            if (oldRate == frameRate) return EditResult.Success();
            List<FrameTransform> transforms = CollectFrameRateTransforms(oldRate, frameRate);
            if (!ValidateTransformedIntervals(transforms, out string error)) return EditResult.Failure(error);

            UnityEngine.Object[] targets = CurrentConfig.Tracks.Where(value => value != null)
                .Cast<UnityEngine.Object>().Prepend(CurrentConfig).ToArray();
            int group = BeginUndo("修改技能时间轴 FPS", targets);
            serializedObject.Update();
            serializedObject.FindProperty(DocumentFieldNames.FrameRate).intValue = frameRate;
            serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue =
                Mathf.Max(1, Mathf.RoundToInt(CurrentConfig.DurationFrames * (float)frameRate / oldRate));
            foreach (IGrouping<TrackConfigBase, FrameTransform> values in transforms.GroupBy(value => value.Track))
            {
                SerializedObject trackObject = new(values.Key);
                trackObject.Update();
                foreach (FrameTransform value in values)
                {
                    SerializedProperty item = FindItemProperty(value.Handler, trackObject, value.ItemId);
                    SetItemFrame(value.Handler, item, value.StartFrame, value.DurationFrames);
                    value.Handler.ResampleSpecificFrameFields(item, oldRate, frameRate);
                }
                SortItems(values.First().Handler, trackObject.FindProperty(values.First().Handler.ItemsPropertyName));
                trackObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(values.Key);
            }
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
            CompleteUndo(group);
            ContentChanged?.Invoke();
            return EditResult.Success();
        }

        /// <summary>
        /// 修改总帧数，且不允许截断现有内容。
        /// </summary>
        public EditResult SetDurationFrames(int durationFrames)
        {
            if (!HasConfig) return EditResult.Failure("请先选择 SkillConfig。");
            durationFrames = Mathf.Max(1, durationFrames);
            int required = GetContentEndFrame();
            if (durationFrames < required) return EditResult.Failure($"时间轴至少需要 {required} 帧。");
            MutateRoot("修改技能时间轴长度",
                () => serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue = durationFrames);
            return EditResult.Success();
        }

        /// <summary>
        /// 将总帧裁剪到最后内容的排他结束帧。
        /// </summary>
        public void TrimToContent()
        {
            if (!HasConfig) return;
            int duration = Mathf.Max(1, GetContentEndFrame());
            MutateRoot("裁剪技能时间轴",
                () => serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue = duration);
        }
        #endregion

        #region Track 子资产编辑
        /// <summary>
        /// 检查当前技能是否允许新增指定模块的轨道，单例轨道按具体 TrackConfig 类型判重。
        /// </summary>
        /// <param name="module">待创建轨道的完整模块定义。</param>
        /// <returns>允许创建时返回成功，否则包含拒绝原因。</returns>
        public EditResult CanAddTrack(TrackModule module)
        {
            if (!HasConfig) return EditResult.Failure("请先选择 SkillConfig。");
            if (module == null) return EditResult.Failure("轨道模块不存在。");
            if (!module.Metadata.AllowMultiple && CurrentConfig.Tracks.Any(track =>
                    track != null && track.GetType() == module.TrackType))
                return EditResult.Failure($"每个技能只能创建一条{module.Metadata.MenuPath}。");
            return EditResult.Success();
        }

        /// <summary>
        /// 创建指定模块的 Track 子资产并追加到统一列表。
        /// </summary>
        public string AddTrack(TrackModule module)
        {
            if (!CanAddTrack(module).Succeeded) return string.Empty;
            TrackConfigBase track = (TrackConfigBase)ScriptableObject.CreateInstance(module.TrackType);
            string id = NewGUID();
            track.name = module.Metadata.MenuPath;
            track.hideFlags = HideFlags.HideInHierarchy;
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("添加技能轨道");
            Undo.RecordObject(CurrentConfig, "添加技能轨道");
            AssetDatabase.AddObjectToAsset(track, CurrentConfig);
            Undo.RegisterCreatedObjectUndo(track, "添加技能轨道");

            SerializedObject trackObject = new(track);
            trackObject.Update();
            trackObject.FindProperty(DocumentFieldNames.Id).stringValue = id;
            trackObject.FindProperty(DocumentFieldNames.DisplayName).stringValue = module.Metadata.MenuPath;
            trackObject.FindProperty(DocumentFieldNames.Muted).boolValue = false;
            trackObject.FindProperty(DocumentFieldNames.EditorLocked).boolValue = false;
            trackObject.FindProperty(module.Document.ItemsPropertyName).ClearArray();
            trackObject.ApplyModifiedProperties();

            serializedObject.Update();
            SerializedProperty tracks = serializedObject.FindProperty(DocumentFieldNames.Tracks);
            int index = tracks.arraySize++;
            tracks.GetArrayElementAtIndex(index).objectReferenceValue = track;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
            EditorUtility.SetDirty(track);
            Undo.CollapseUndoOperations(group);
            ContentChanged?.Invoke();
            return id;
        }

        /// <summary>
        /// 移除列表引用并销毁 Track 子资产。
        /// </summary>
        public EditResult RemoveTrack(string trackId)
        {
            if (!TryFindTrack(trackId, out TrackConfigBase track, out int index))
                return EditResult.Failure("轨道不存在。");
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("删除技能轨道");
            Undo.RecordObject(CurrentConfig, "删除技能轨道");
            serializedObject.Update();
            SerializedProperty tracks = serializedObject.FindProperty(DocumentFieldNames.Tracks);
            tracks.GetArrayElementAtIndex(index).objectReferenceValue = null;
            tracks.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();
            Undo.DestroyObjectImmediate(track);
            EditorUtility.SetDirty(CurrentConfig);
            Undo.CollapseUndoOperations(group);
            ContentChanged?.Invoke();
            return EditResult.Success();
        }

        /// <summary>
        /// 在统一列表内移动一个物理行。
        /// </summary>
        public EditResult MoveTrack(string trackId, int offset)
        {
            if (!TryFindTrack(trackId, out _, out int index)) return EditResult.Failure("轨道不存在。");
            int target = Mathf.Clamp(index + offset, 0, CurrentConfig.Tracks.Count - 1);
            int insertionIndex = target > index ? target + 1 : target;
            return MoveTrackToIndex(trackId, insertionIndex);
        }

        /// <summary>
        /// 将轨道移动到统一列表的插入边界；移除源元素后会修正目标索引。
        /// </summary>
        public EditResult MoveTrackToIndex(string trackId, int insertionIndex)
        {
            if (!TryFindTrack(trackId, out _, out int sourceIndex))
                return EditResult.Failure("轨道不存在。");

            int trackCount = CurrentConfig.Tracks.Count;
            insertionIndex = Mathf.Clamp(insertionIndex, 0, trackCount);
            int targetIndex = insertionIndex > sourceIndex ? insertionIndex - 1 : insertionIndex;
            targetIndex = Mathf.Clamp(targetIndex, 0, trackCount - 1);
            if (targetIndex == sourceIndex) return EditResult.Success();

            MutateRoot("拖拽重排技能轨道",
                () => serializedObject.FindProperty(DocumentFieldNames.Tracks)
                    .MoveArrayElement(sourceIndex, targetIndex));
            return EditResult.Success();
        }

        /// <summary>
        /// 按 TimelineTrack.Order 稳定重排，不改变同类型轨道相对顺序。
        /// </summary>
        public EditResult SortTracksByType(TrackModuleRegistry modules)
        {
            if (!HasConfig) return EditResult.Failure("请先选择 SkillConfig。");
            TrackConfigBase[] sorted = CurrentConfig.Tracks.Select((track, index) =>
                    new { track, index, order = track == null ? int.MaxValue : modules.Get(track).Metadata.Order })
                .OrderBy(value => value.order).ThenBy(value => value.index)
                .Select(value => value.track).ToArray();
            if (sorted.SequenceEqual(CurrentConfig.Tracks)) return EditResult.Success();
            MutateRoot("按轨道类型重排", () =>
            {
                SerializedProperty tracks = serializedObject.FindProperty(DocumentFieldNames.Tracks);
                for (int index = 0; index < sorted.Length; index++)
                    tracks.GetArrayElementAtIndex(index).objectReferenceValue = sorted[index];
            });
            return EditResult.Success();
        }

        /// <summary>
        /// 修改 Track 公共字段。
        /// </summary>
        public EditResult EditTrack(string trackId, string displayName, bool muted, bool locked)
        {
            if (!TryFindTrack(trackId, out TrackConfigBase track, out _))
                return EditResult.Failure("轨道不存在。");
            MutateTrack("修改技能轨道", track, trackObject =>
            {
                trackObject.FindProperty(DocumentFieldNames.DisplayName).stringValue =
                    string.IsNullOrWhiteSpace(displayName) ? "未命名轨道" : displayName.Trim();
                trackObject.FindProperty(DocumentFieldNames.Muted).boolValue = muted;
                trackObject.FindProperty(DocumentFieldNames.EditorLocked).boolValue = locked;
            });
            return EditResult.Success();
        }
        #endregion

        #region Item 编辑
        /// <summary>
        /// 在轨道末尾创建默认 Item。
        /// </summary>
        public string AddItem(ITrackDocumentHandler handler, string trackId)
        {
            if (!TryFindTrack(handler, trackId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items)) return string.Empty;
            if (track.EditorLocked) return string.Empty;
            string id = NewGUID();
            MutateTrack("添加技能时间轴内容", track, trackObject, () =>
            {
                SerializedProperty item = AppendItem(handler, items, id, FindAvailableStartFrame(handler, items), 1);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            });
            return id;
        }

        /// <summary>
        /// 把类型化创建请求交给 Handler。
        /// </summary>
        public ItemsCreateResult CreateItems(ITrackDocumentHandler handler, string trackId,
            IItemCreateRequest request) => handler.CreateItems(this, trackId, request);

        /// <summary>
        /// 删除指定 Item。
        /// </summary>
        public EditResult RemoveItem(ITrackDocumentHandler handler, string trackId, string itemId)
        {
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items, out _, out int index))
                return EditResult.Failure("内容不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");
            MutateTrack("删除技能时间轴内容", track, trackObject,
                () => items.DeleteArrayElementAtIndex(index));
            return EditResult.Success();
        }

        /// <summary>
        /// 复制 Item，并为副本分配新 GUID。
        /// </summary>
        public string DuplicateItem(ITrackDocumentHandler handler, string trackId, string itemId)
        {
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items,
                    out SerializedProperty source, out _)) return string.Empty;
            if (track.EditorLocked) return string.Empty;
            string id = NewGUID();
            int start = FindAvailableStartFrame(handler, items);
            int duration = GetItemDuration(handler, source);
            MutateTrack("复制技能时间轴内容", track, trackObject, () =>
            {
                SerializedProperty destination = AppendItem(handler, items, id, start, duration);
                handler.CopySpecificFields(source, destination);
                ExpandDurationForItem(handler, destination);
                SortItems(handler, items);
            });
            return id;
        }

        /// <summary>
        /// 只读校验 Item 是否能在当前轨道移动到目标帧，不产生 Undo 或资产修改。
        /// </summary>
        public EditResult CanMoveItem(ITrackDocumentHandler handler, string trackId, string itemId,
            int startFrame)
        {
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out _, out SerializedProperty items, out SerializedProperty item, out _))
                return EditResult.Failure("内容不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");

            startFrame = Mathf.Max(0, startFrame);
            int duration = GetItemDuration(handler, item);
            return CanPlaceInterval(handler, items, itemId, startFrame, duration)
                ? EditResult.Success()
                : EditResult.Failure("目标区间与同轨内容重叠。");
        }

        /// <summary>
        /// 移动 Item 起始帧。
        /// </summary>
        public EditResult MoveItem(ITrackDocumentHandler handler, string trackId, string itemId, int startFrame)
        {
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("内容不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");
            startFrame = Mathf.Max(0, startFrame);
            int duration = GetItemDuration(handler, item);
            if (GetItemStart(handler, item) == startFrame) return EditResult.Success();
            if (!CanPlaceInterval(handler, items, itemId, startFrame, duration))
                return EditResult.Failure("目标区间与同轨内容重叠。");
            MutateTrack("移动技能时间轴内容", track, trackObject, () =>
            {
                SetItemFrame(handler, item, startFrame, duration);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            });
            return EditResult.Success();
        }

        /// <summary>
        /// 校验 Item 是否可移动到同类型目标轨道。
        /// </summary>
        public EditResult CanMoveItemToTrack(ITrackDocumentHandler handler, string sourceTrackId,
            string targetTrackId, string itemId, int startFrame) =>
            ResolveItemTrackMove(handler, sourceTrackId, targetTrackId, itemId, startFrame,
                out _, out _, out _, out _, out _, out _);

        /// <summary>
        /// 在两个同类型 Track 子资产间移动 Item。
        /// </summary>
        public EditResult MoveItemToTrack(ITrackDocumentHandler handler, string sourceTrackId,
            string targetTrackId, string itemId, int startFrame)
        {
            EditResult validation = ResolveItemTrackMove(handler, sourceTrackId, targetTrackId, itemId,
                startFrame, out TrackConfigBase sourceTrack, out SerializedObject sourceObject,
                out SerializedProperty sourceItems, out int sourceIndex,
                out TrackConfigBase targetTrack, out SerializedObject targetObject);
            if (!validation.Succeeded) return validation;
            SerializedProperty sourceItem = sourceItems.GetArrayElementAtIndex(sourceIndex);
            int duration = GetItemDuration(handler, sourceItem);
            int group = BeginUndo("跨轨道移动技能内容",
                new UnityEngine.Object[] { CurrentConfig, sourceTrack, targetTrack });
            serializedObject.Update();
            sourceObject.Update();
            targetObject.Update();
            sourceItems = sourceObject.FindProperty(handler.ItemsPropertyName);
            sourceItem = FindItemProperty(handler, sourceObject, itemId);
            SerializedProperty targetItems = targetObject.FindProperty(handler.ItemsPropertyName);
            SerializedProperty destination = AppendItem(handler, targetItems, itemId,
                Mathf.Max(0, startFrame), duration);
            handler.CopySpecificFields(sourceItem, destination);
            ExpandDurationForItem(handler, destination);
            sourceItems.DeleteArrayElementAtIndex(FindItemIndex(sourceItems, itemId));
            SortItems(handler, targetItems);
            sourceObject.ApplyModifiedProperties();
            targetObject.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(sourceTrack);
            EditorUtility.SetDirty(targetTrack);
            CompleteUndo(group);
            ContentChanged?.Invoke();
            return EditResult.Success();
        }

        /// <summary>
        /// 调整可 Resize Item 的半开帧区间。
        /// </summary>
        public EditResult ResizeItem(ITrackDocumentHandler handler, string trackId, string itemId,
            int startFrame, int durationFrames)
        {
            if (!handler.SupportsResize) return EditResult.Failure("该内容不支持裁剪。");
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("内容不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(handler, items, itemId, startFrame, durationFrames))
                return EditResult.Failure("目标区间与同轨内容重叠。");
            MutateTrack("裁剪技能时间轴内容", track, trackObject, () =>
            {
                SetItemFrame(handler, item, startFrame, durationFrames);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            });
            return EditResult.Success();
        }

        /// <summary>
        /// 将类型化 Item 请求交给 Handler。
        /// </summary>
        public EditResult EditItem(ITrackDocumentHandler handler, string trackId, string itemId,
            IItemEditRequest request) => handler.EditItem(this, trackId, itemId, request);
        #endregion

        #region Handler 事务 API
        /// <summary>
        /// 校验并提交 Clip 区间及专用字段。
        /// </summary>
        internal EditResult EditClip(ITrackDocumentHandler handler, string trackId, string itemId,
            int startFrame, int durationFrames, string undoName, Action<SerializedProperty> editSpecific)
        {
            if (!TryFindItem(handler, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("内容不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(handler, items, itemId, startFrame, durationFrames))
                return EditResult.Failure("目标区间与同轨内容重叠。");
            MutateTrack(undoName, track, trackObject, () =>
            {
                SetItemFrame(handler, item, startFrame, durationFrames);
                editSpecific(item);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            });
            return EditResult.Success();
        }

        // 使用 Handler 已取得的 Track SerializedObject 提交一条 Undo。
        internal void MutateTrack(string undoName, TrackConfigBase track,
            SerializedObject trackObject, Action action)
        {
            int group = BeginUndo(undoName, new UnityEngine.Object[] { CurrentConfig, track });
            serializedObject.Update();
            trackObject.Update();
            action();
            trackObject.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(CurrentConfig);
            CompleteUndo(group);
            ContentChanged?.Invoke();
        }

        // 为公共轨道字段编辑创建 Track SerializedObject 并提交一条 Undo。
        internal void MutateTrack(string undoName, TrackConfigBase track,
            Action<SerializedObject> action)
        {
            SerializedObject trackObject = new(track);
            int group = BeginUndo(undoName, new UnityEngine.Object[] { CurrentConfig, track });
            serializedObject.Update();
            trackObject.Update();
            action(trackObject);
            trackObject.ApplyModifiedProperties();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(track);
            EditorUtility.SetDirty(CurrentConfig);
            CompleteUndo(group);
            ContentChanged?.Invoke();
        }
        #endregion

        #region 查询与区间
        // 按稳定 GUID 查找 Track 子资产及其统一列表索引。
        internal bool TryFindTrack(string trackId, out TrackConfigBase track, out int index)
        {
            track = null;
            index = -1;
            if (!HasConfig) return false;
            for (int i = 0; i < CurrentConfig.Tracks.Count; i++)
            {
                TrackConfigBase candidate = CurrentConfig.Tracks[i];
                if (candidate == null || candidate.Id != trackId) continue;
                track = candidate;
                index = i;
                return true;
            }
            return false;
        }

        // 为指定 Track 创建临时 SerializedObject 并取得 Item 列表。
        internal bool TryFindTrack(ITrackDocumentHandler handler, string trackId,
            out TrackConfigBase track, out SerializedObject trackObject, out SerializedProperty items)
        {
            trackObject = null;
            items = null;
            if (!TryFindTrack(trackId, out track, out _) || track.GetType() != handler.TrackType) return false;
            trackObject = new SerializedObject(track);
            trackObject.Update();
            items = trackObject.FindProperty(handler.ItemsPropertyName);
            return items != null;
        }

        // 在 Track 子资产中按稳定 GUID 查找 Item。
        internal bool TryFindItem(ITrackDocumentHandler handler, string trackId, string itemId,
            out TrackConfigBase track, out SerializedObject trackObject, out SerializedProperty items,
            out SerializedProperty item, out int index)
        {
            item = null;
            index = -1;
            if (!TryFindTrack(handler, trackId, out track, out trackObject, out items)) return false;
            index = FindItemIndex(items, itemId);
            if (index < 0) return false;
            item = items.GetArrayElementAtIndex(index);
            return true;
        }

        // 返回 Track 的 Editor 锁定状态。
        internal static bool IsTrackLocked(TrackConfigBase track) => track.EditorLocked;

        // 追加一个 Item 并初始化公共字段。
        internal SerializedProperty AppendItem(ITrackDocumentHandler handler, SerializedProperty items,
            string id, int startFrame, int durationFrames)
        {
            int index = items.arraySize++;
            SerializedProperty item = items.GetArrayElementAtIndex(index);
            handler.InitializeItem(item, id, startFrame);
            SetItemFrame(handler, item, startFrame, durationFrames);
            return item;
        }

        // 为批量创建预生成稳定 GUID。
        internal static string[] CreateItemIds(int count)
        {
            string[] ids = new string[count];
            for (int index = 0; index < count; index++) ids[index] = NewGUID();
            return ids;
        }

        // 检查同轨半开区间互斥。
        internal bool CanPlaceInterval(ITrackDocumentHandler handler, SerializedProperty items,
            string ignoreItemId, int startFrame, int durationFrames)
        {
            int end = startFrame + Mathf.Max(1, durationFrames);
            for (int index = 0; index < items.arraySize; index++)
            {
                SerializedProperty other = items.GetArrayElementAtIndex(index);
                if (other.FindPropertyRelative(DocumentFieldNames.Id).stringValue == ignoreItemId) continue;
                int otherStart = GetItemStart(handler, other);
                if (startFrame < otherStart + GetItemDuration(handler, other) && otherStart < end) return false;
            }
            return true;
        }

        // 按起始帧稳定排序 Item。
        internal static void SortItems(ITrackDocumentHandler handler, SerializedProperty items)
        {
            for (int source = 1; source < items.arraySize; source++)
            {
                int target = source;
                int start = GetItemStart(handler, items.GetArrayElementAtIndex(source));
                while (target > 0 &&
                       GetItemStart(handler, items.GetArrayElementAtIndex(target - 1)) > start) target--;
                if (target != source) items.MoveArrayElement(source, target);
            }
        }

        // 写入起始帧和可选持续帧。
        internal static void SetItemFrame(ITrackDocumentHandler handler, SerializedProperty item,
            int startFrame, int durationFrames)
        {
            item.FindPropertyRelative(handler.StartFramePropertyName).intValue = Mathf.Max(0, startFrame);
            if (handler.SupportsResize)
                item.FindPropertyRelative(handler.DurationPropertyName).intValue = Mathf.Max(1, durationFrames);
        }

        // 扩展 SkillConfig 总帧以包含当前 Item。
        internal void ExpandDurationForItem(ITrackDocumentHandler handler, SerializedProperty item)
        {
            int end = GetItemStart(handler, item) + GetItemDuration(handler, item);
            SerializedProperty duration = serializedObject.FindProperty(DocumentFieldNames.DurationFrames);
            duration.intValue = Mathf.Max(duration.intValue, end);
        }
        #endregion

        #region 内部事务与辅助
        // 提交仅修改 SkillConfig 根资产的 Undo。
        private void MutateRoot(string undoName, Action action)
        {
            int group = BeginUndo(undoName, new UnityEngine.Object[] { CurrentConfig });
            serializedObject.Update();
            action();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
            CompleteUndo(group);
            ContentChanged?.Invoke();
        }

        // 开启语义 Undo 组并记录全部相关对象。
        private static int BeginUndo(string undoName, UnityEngine.Object[] targets)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RecordObjects(targets, undoName);
            return group;
        }

        // 合并语义事务，确保一次操作只产生一条 Undo。
        private static void CompleteUndo(int group) => Undo.CollapseUndoOperations(group);

        // Undo/Redo 后只重建根 SerializedObject；Track 上下文始终按需创建。
        private void OnUndoRedoPerformed()
        {
            if (CurrentConfig == null) return;
            serializedObject = new SerializedObject(CurrentConfig);
            ContentChanged?.Invoke();
        }

        // 修复根、Track、Item GUID，不执行修改后的全量内容校验。
        private void EnsureStableIds()
        {
            HashSet<string> used = new();
            serializedObject.Update();
            SerializedProperty configId = serializedObject.FindProperty(DocumentFieldNames.Id);
            if (string.IsNullOrEmpty(configId.stringValue) || !used.Add(configId.stringValue))
                configId.stringValue = NewUniqueId(used);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            foreach (TrackConfigBase track in CurrentConfig.Tracks.Where(value => value != null))
            {
                SerializedObject trackObject = new(track);
                trackObject.Update();
                SerializedProperty trackId = trackObject.FindProperty(DocumentFieldNames.Id);
                if (string.IsNullOrEmpty(trackId.stringValue) || !used.Add(trackId.stringValue))
                    trackId.stringValue = NewUniqueId(used);
                ITrackDocumentHandler handler = GetHandler(track);
                SerializedProperty items = trackObject.FindProperty(handler.ItemsPropertyName);
                for (int index = 0; index < items.arraySize; index++)
                {
                    SerializedProperty itemId = items.GetArrayElementAtIndex(index)
                        .FindPropertyRelative(DocumentFieldNames.Id);
                    if (string.IsNullOrEmpty(itemId.stringValue) || !used.Add(itemId.stringValue))
                        itemId.stringValue = NewUniqueId(used);
                }
                trackObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(track);
            }
            EditorUtility.SetDirty(CurrentConfig);
        }

        // 校验跨轨移动并返回两套 Track 序列化上下文。
        private EditResult ResolveItemTrackMove(ITrackDocumentHandler handler,
            string sourceTrackId, string targetTrackId, string itemId, int startFrame,
            out TrackConfigBase sourceTrack, out SerializedObject sourceObject,
            out SerializedProperty sourceItems, out int sourceIndex,
            out TrackConfigBase targetTrack, out SerializedObject targetObject)
        {
            sourceTrack = null;
            sourceObject = null;
            sourceItems = null;
            sourceIndex = -1;
            targetTrack = null;
            targetObject = null;
            if (sourceTrackId == targetTrackId) return EditResult.Failure("源轨道与目标轨道相同。");
            if (!TryFindItem(handler, sourceTrackId, itemId, out sourceTrack, out sourceObject,
                    out sourceItems, out SerializedProperty sourceItem, out sourceIndex))
                return EditResult.Failure("源内容不存在。");
            if (!TryFindTrack(handler, targetTrackId, out targetTrack, out targetObject,
                    out SerializedProperty targetItems))
                return EditResult.Failure("目标轨道不存在。");
            if (sourceTrack.GetType() != targetTrack.GetType())
                return EditResult.Failure("只能移动到相同类型轨道。");
            if (sourceTrack.EditorLocked || targetTrack.EditorLocked)
                return EditResult.Failure("源轨道或目标轨道已锁定。");
            if (!CanPlaceInterval(handler, targetItems, string.Empty, Mathf.Max(0, startFrame),
                    GetItemDuration(handler, sourceItem)))
                return EditResult.Failure("目标轨道对应区间已有内容。");
            return EditResult.Success();
        }

        // 返回稳定 Item GUID 对应的数组索引。
        private static int FindItemIndex(SerializedProperty items, string itemId)
        {
            for (int index = 0; index < items.arraySize; index++)
                if (items.GetArrayElementAtIndex(index).FindPropertyRelative(DocumentFieldNames.Id)
                    .stringValue == itemId) return index;
            return -1;
        }

        // 在 Track SerializedObject 中按 GUID 取得 Item。
        private static SerializedProperty FindItemProperty(ITrackDocumentHandler handler,
            SerializedObject trackObject, string itemId)
        {
            SerializedProperty items = trackObject.FindProperty(handler.ItemsPropertyName);
            int index = FindItemIndex(items, itemId);
            if (index < 0) throw new InvalidOperationException($"Item {itemId} 在事务期间丢失。");
            return items.GetArrayElementAtIndex(index);
        }

        // 返回 Item 起始帧。
        private static int GetItemStart(ITrackDocumentHandler handler, SerializedProperty item) =>
            item.FindPropertyRelative(handler.StartFramePropertyName).intValue;

        // 返回 Item 持续帧；Marker 固定为一帧。
        private static int GetItemDuration(ITrackDocumentHandler handler, SerializedProperty item) =>
            handler.SupportsResize ? Mathf.Max(1,
                item.FindPropertyRelative(handler.DurationPropertyName).intValue) : 1;

        // 返回轨道末尾首个可用帧。
        private static int FindAvailableStartFrame(ITrackDocumentHandler handler, SerializedProperty items)
        {
            int frame = 0;
            for (int index = 0; index < items.arraySize; index++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(index);
                frame = Mathf.Max(frame, GetItemStart(handler, item) + GetItemDuration(handler, item));
            }
            return frame;
        }

        // 返回全部内容的最大排他结束帧。
        private int GetContentEndFrame() => CurrentConfig.Tracks.Where(value => value != null)
            .SelectMany(track => track.Items).Select(item => item.EndFrame).DefaultIfEmpty(0).Max();

        // 构建尚未写入资产的 FPS 重采样结果。
        private List<FrameTransform> CollectFrameRateTransforms(int oldRate, int newRate)
        {
            List<FrameTransform> result = new();
            foreach (TrackConfigBase track in CurrentConfig.Tracks.Where(value => value != null))
            {
                ITrackDocumentHandler handler = GetHandler(track);
                foreach (TimelineItemConfigBase item in track.Items)
                    result.Add(new FrameTransform(track, handler, item.Id,
                        Mathf.Max(0, Mathf.RoundToInt(item.StartFrame * (float)newRate / oldRate)),
                        handler.SupportsResize
                            ? Mathf.Max(1, Mathf.RoundToInt(item.DurationFrames * (float)newRate / oldRate)) : 1));
            }
            return result;
        }

        // 具体 Track 类型是 Handler 路由的唯一依据，不能使用 clips 等重复字段名猜测类型。
        private ITrackDocumentHandler GetHandler(TrackConfigBase track) =>
            handlers.Single(value => value.TrackType == track.GetType());

        // 写入前按 Track 校验重采样后的半开区间。
        private static bool ValidateTransformedIntervals(List<FrameTransform> transforms, out string error)
        {
            foreach (IGrouping<TrackConfigBase, FrameTransform> group in transforms.GroupBy(value => value.Track))
            {
                FrameTransform[] ordered = group.OrderBy(value => value.StartFrame).ToArray();
                for (int index = 1; index < ordered.Length; index++)
                    if (ordered[index].StartFrame <
                        ordered[index - 1].StartFrame + ordered[index - 1].DurationFrames)
                    {
                        error = $"修改 FPS 后轨道“{group.Key.DisplayName}”会产生内容重叠。";
                        return false;
                    }
            }
            error = string.Empty;
            return true;
        }

        // 创建唯一 GUID。
        private static string NewUniqueId(ISet<string> used)
        {
            string id;
            do id = NewGUID(); while (!used.Add(id));
            return id;
        }

        // 创建稳定 GUID。
        private static string NewGUID() => Guid.NewGuid().ToString("N");
        #endregion

        /// <summary>
        /// 保存一项尚未写入资产的 FPS 帧变换。
        /// </summary>
        private sealed class FrameTransform
        {
            public TrackConfigBase Track { get; }
            public ITrackDocumentHandler Handler { get; }
            public string ItemId { get; }
            public int StartFrame { get; }
            public int DurationFrames { get; }

            /// <summary>
            /// 保存不可变重采样结果，等待整轨校验通过后写入。
            /// </summary>
            public FrameTransform(TrackConfigBase track, ITrackDocumentHandler handler,
                string itemId, int startFrame, int durationFrames)
            {
                Track = track;
                Handler = handler;
                ItemId = itemId;
                StartFrame = startFrame;
                DurationFrames = durationFrames;
            }
        }
    }
}
#endif
