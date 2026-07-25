#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 作为 SkillConfig 编辑资产的唯一写入入口，统一负责校验、SerializedObject、Undo 与变更通知。
    /// </summary>
    internal sealed class Document : IDisposable
    {
        #region Serialized state and events

        private readonly IReadOnlyList<ITrackDocumentHandler> handlers;
        private SerializedObject serializedObject;

        public event Action ContentChanged;
        public event Action ConfigChanged;

        public SkillConfig CurrentConfig { get; private set; }
        public SerializedObject SerializedObject => serializedObject;

        #endregion

        #region Lifecycle and configuration

        /// <summary>
        /// 创建并初始化 Document。
        /// </summary>
        public Document(IReadOnlyList<ITrackDocumentHandler> handlers)
        {
            this.handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
            if (handlers.Count == 0) throw new ArgumentException("至少需要一个轨道数据处理器。", nameof(handlers));
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        /// <summary>
        /// 释放事件订阅和该对象持有的编辑器资源。
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
        /// 打开指定技能配置并重建序列化编辑上下文。
        /// </summary>
        public void Open(SkillConfig config)
        {
            if (CurrentConfig == config)
            {
                return;
            }

            CurrentConfig = config;
            serializedObject = config != null ? new SerializedObject(config) : null;
            if (config != null)
            {
                EnsureStableIds();
            }

            ConfigChanged?.Invoke();
            ContentChanged?.Invoke();
        }

        /// <summary>
        /// 创建 SkillConfig 资产并将其设为当前编辑文档。
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
        /// 修改配置帧率，并在保持实际时间的前提下重采样全部内容。
        /// </summary>
        public EditResult ChangeFrameRate(int frameRate)
        {
            if (!HasConfig)
            {
                return EditResult.Failure("请先选择 SkillConfig。");
            }

            frameRate = Mathf.Clamp(frameRate, 1, 240);
            int oldFrameRate = CurrentConfig.FrameRate;
            if (oldFrameRate == frameRate)
            {
                return EditResult.Success();
            }

            serializedObject.Update();
            List<FrameTransform> transforms = CollectFrameRateTransforms(oldFrameRate, frameRate);
            if (!ValidateTransformedIntervals(transforms, out string error))
            {
                return EditResult.Failure(error);
            }

            Mutate("修改技能时间轴 FPS", () =>
            {
                serializedObject.FindProperty(DocumentFieldNames.FrameRate).intValue = frameRate;
                foreach (FrameTransform transform in transforms)
                {
                    transform.Apply();
                }

                int duration = Mathf.Max(1, Mathf.RoundToInt(CurrentConfig.DurationFrames * (float)frameRate / oldFrameRate));
                serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue = duration;
            });
            return EditResult.Success();
        }

        /// <summary>
        /// 修改时间轴总帧数，并拒绝截断现有内容。
        /// </summary>
        public EditResult SetDurationFrames(int durationFrames)
        {
            if (!HasConfig)
            {
                return EditResult.Failure("请先选择 SkillConfig。");
            }

            durationFrames = Mathf.Max(1, durationFrames);
            int required = GetContentEndFrame();
            if (durationFrames < required)
            {
                return EditResult.Failure($"时间轴至少需要 {required} 帧才能容纳现有内容。");
            }

            Mutate("修改技能时间轴长度", () => serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue = durationFrames);
            return EditResult.Success();
        }

        /// <summary>
        /// 把时间轴总帧数裁剪到容纳现有内容所需的长度。
        /// </summary>
        public void TrimToContent()
        {
            if (!HasConfig) return;
            int duration = Mathf.Max(1, GetContentEndFrame());
            Mutate("裁剪技能时间轴", () => serializedObject.FindProperty(DocumentFieldNames.DurationFrames).intValue = duration);
        }

        #endregion

        #region Track editing

        /// <summary>
        /// 创建指定类型的轨道并返回其稳定标识符。
        /// </summary>
        public string AddTrack(ITrackDocumentHandler handler)
        {
            if (!HasConfig) return string.Empty;
            string id = NewId();
            Mutate("添加技能轨道", () =>
            {
                SerializedProperty tracks = GetTracksProperty(handler);
                int index = tracks.arraySize;
                tracks.arraySize++;
                SerializedProperty track = tracks.GetArrayElementAtIndex(index);
                SerializedProperty header = track.FindPropertyRelative(DocumentFieldNames.Header);
                header.FindPropertyRelative(DocumentFieldNames.Id).stringValue = id;
                header.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = GetDefaultTrackName(handler, index + 1);
                header.FindPropertyRelative(DocumentFieldNames.Muted).boolValue = false;
                header.FindPropertyRelative(DocumentFieldNames.EditorLocked).boolValue = false;
                header.FindPropertyRelative(DocumentFieldNames.EditorColor).colorValue = Color.white;
                GetItemsProperty(handler, track).ClearArray();
            });
            return id;
        }

        /// <summary>
        /// 删除指定类型和标识符的轨道。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        public EditResult RemoveTrack(ITrackDocumentHandler handler, string trackId)
        {
            if (!TryFindTrack(handler, trackId, out SerializedProperty tracks, out _, out int index))
                return EditResult.Failure("轨道不存在。");
            Mutate("删除技能轨道", () => tracks.DeleteArrayElementAtIndex(index));
            return EditResult.Success();
        }

        /// <summary>
        /// 在同类型轨道列表中重排指定轨道。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        public EditResult MoveTrack(ITrackDocumentHandler handler, string trackId, int offset)
        {
            if (!TryFindTrack(handler, trackId, out SerializedProperty tracks, out _, out int index))
                return EditResult.Failure("轨道不存在。");
            int target = Mathf.Clamp(index + offset, 0, tracks.arraySize - 1);
            if (target == index) return EditResult.Success();
            Mutate("重排技能轨道", () => tracks.MoveArrayElement(index, target));
            return EditResult.Success();
        }

        /// <summary>
        /// 修改指定轨道的名称、静音和编辑锁定状态。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        public EditResult EditTrack(ITrackDocumentHandler handler, string trackId, string displayName, bool muted, bool locked)
        {
            if (!TryFindTrack(handler, trackId, out _, out SerializedProperty track, out _))
                return EditResult.Failure("轨道不存在。");
            Mutate("修改技能轨道", () =>
            {
                SerializedProperty header = track.FindPropertyRelative(DocumentFieldNames.Header);
                header.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = string.IsNullOrWhiteSpace(displayName) ? "未命名轨道" : displayName.Trim();
                header.FindPropertyRelative(DocumentFieldNames.Muted).boolValue = muted;
                header.FindPropertyRelative(DocumentFieldNames.EditorLocked).boolValue = locked;
            }, trackId, false);
            return EditResult.Success();
        }

        #endregion

        #region Timeline content editing

        /// <summary>
        /// 在指定轨道末尾的可用帧创建默认内容项。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        public string AddItem(ITrackDocumentHandler handler, string trackId)
        {
            if (!TryFindTrack(handler, trackId, out _, out SerializedProperty track, out _)) return string.Empty;
            string id = NewId();
            Mutate("添加技能时间轴内容", () =>
            {
                SerializedProperty items = GetItemsProperty(handler, track);
                int index = items.arraySize;
                items.arraySize++;
                SerializedProperty item = items.GetArrayElementAtIndex(index);
                ResetItem(handler, item, id, FindAvailableStartFrame(handler, track));
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            });
            return id;
        }

        /// <summary>
        /// 把类型化创建请求交给对应轨道处理器，并保持 Document 为唯一资产事务入口。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        public ItemsCreateResult CreateItems(ITrackDocumentHandler handler, string trackId,
            IItemCreateRequest request)
        {
            if (handler == null) return ItemsCreateResult.Failure("轨道数据处理器不存在。");
            if (request == null) return ItemsCreateResult.Failure("内容创建请求不存在。");
            return handler.CreateItems(this, trackId, request);
        }
        /// <summary>
        /// 从指定轨道删除片段或事件标记。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        public EditResult RemoveItem(ITrackDocumentHandler handler, string trackId, string itemId)
        {
            if (!TryFindItem(handler, trackId, itemId, out _, out SerializedProperty items, out _, out int index))
                return EditResult.Failure("时间轴内容不存在。");
            Mutate("删除技能时间轴内容", () => items.DeleteArrayElementAtIndex(index));
            return EditResult.Success();
        }

        /// <summary>
        /// 复制指定内容项，生成新标识符并放置到可用帧。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        public string DuplicateItem(ITrackDocumentHandler handler, string trackId, string itemId)
        {
            if (!TryFindItem(handler, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out int index)) return string.Empty;

            string newId = NewId();
            int start = GetItemStart(handler, item);
            int duration = GetItemDuration(handler, item);
            int proposed = handler.SupportsResize ? start + duration : start + 1;
            if (!CanPlaceInterval(handler, track, itemId, proposed, duration))
                proposed = FindAvailableStartFrame(handler, track);

            Mutate("复制技能时间轴内容", () =>
            {
                items.InsertArrayElementAtIndex(index);
                SerializedProperty source = items.GetArrayElementAtIndex(index);
                SerializedProperty copy = items.GetArrayElementAtIndex(index + 1);
                handler.CopySpecificFields(source, copy);
                copy.FindPropertyRelative(DocumentFieldNames.Id).stringValue = newId;
                SetItemFrame(handler, copy, proposed, duration);
                ExpandDurationForItem(handler, copy);
                SortItems(handler, items);
            });
            return newId;
        }

        /// <summary>
        /// 校验并移动指定片段或事件标记到目标帧。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        public EditResult MoveItem(ITrackDocumentHandler handler, string trackId, string itemId, int startFrame)
        {
            if (!TryFindItem(handler, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("时间轴内容不存在。");
            startFrame = Mathf.Max(0, startFrame);
            int duration = GetItemDuration(handler, item);
            if (!CanPlaceInterval(handler, track, itemId, startFrame, duration))
                return EditResult.Failure("目标位置与同轨内容重叠。");

            Mutate("移动技能时间轴内容", () =>
            {
                SetItemFrame(handler, item, startFrame, duration);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            }, itemId, false);
            return EditResult.Success();
        }

        /// <summary>
        /// 只读检查一个 Item 能否保持原帧区间移动到同模块的另一条轨道。
        /// </summary>
        /// <param name="handler">源轨道与目标轨道共同使用的类型化数据处理器。</param>
        /// <param name="sourceTrackId">源轨道头中的稳定 GUID。</param>
        /// <param name="targetTrackId">目标轨道头中的稳定 GUID。</param>
        /// <param name="itemId">需要跨轨道移动的稳定 Item GUID。</param>
        /// <returns>可以移动时返回成功，否则携带锁定、缺失或区间冲突原因。</returns>
        public EditResult CanMoveItemToTrack(ITrackDocumentHandler handler, string sourceTrackId,
            string targetTrackId, string itemId) =>
            ResolveItemTrackMove(handler, sourceTrackId, targetTrackId, itemId,
                out _, out _, out _, out _, out _, out _);

        /// <summary>
        /// 在同一模块的两条轨道之间移动 Item，并保持 GUID、帧区间及类型专用数据不变。
        /// </summary>
        /// <param name="handler">源轨道与目标轨道共同使用的类型化数据处理器。</param>
        /// <param name="sourceTrackId">源轨道头中的稳定 GUID。</param>
        /// <param name="targetTrackId">目标轨道头中的稳定 GUID。</param>
        /// <param name="itemId">需要跨轨道移动的稳定 Item GUID。</param>
        /// <returns>事务提交成功或未修改资产的失败原因。</returns>
        public EditResult MoveItemToTrack(ITrackDocumentHandler handler, string sourceTrackId,
            string targetTrackId, string itemId)
        {
            EditResult validation = ResolveItemTrackMove(handler, sourceTrackId, targetTrackId, itemId,
                out SerializedProperty sourceItems, out SerializedProperty sourceItem, out int sourceIndex,
                out SerializedProperty targetItems, out int startFrame, out int durationFrames);
            if (!validation.Succeeded) return validation;

            Mutate("跨轨道移动技能时间轴内容", () =>
            {
                SerializedProperty destination = AppendItem(
                    handler, targetItems, itemId, startFrame, durationFrames);
                handler.CopySpecificFields(sourceItem, destination);
                sourceItems.DeleteArrayElementAtIndex(sourceIndex);
                SortItems(handler, targetItems);
            }, itemId, false);
            return EditResult.Success();
        }

        /// <summary>
        /// 校验并提交片段的新半开帧区间。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        public EditResult ResizeItem(ITrackDocumentHandler handler, string trackId, string itemId, int startFrame, int durationFrames)
        {
            if (!handler.SupportsResize) return EditResult.Failure("当前内容不支持裁剪。");
            if (!TryFindItem(handler, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("时间轴内容不存在。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(handler, track, itemId, startFrame, durationFrames))
                return EditResult.Failure("裁剪结果与同轨内容重叠。");

            Mutate("裁剪技能时间轴内容", () =>
            {
                SetItemFrame(handler, item, startFrame, durationFrames);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            }, itemId, false);
            return EditResult.Success();
        }

        /// <summary>
        /// 把类型化字段编辑请求交给对应轨道处理器，并保持 Undo 与通知由 Document 统一发送。
        /// </summary>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        public EditResult EditItem(ITrackDocumentHandler handler, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (handler == null) return EditResult.Failure("轨道数据处理器不存在。");
            if (request == null) return EditResult.Failure("内容编辑请求不存在。");
            return handler.EditItem(this, trackId, itemId, request);
        }
        /// <summary>
        /// 执行动画和特效片段共享的区间校验与资产修改流程。
        /// </summary>
        /// <param name="editSpecific">用于编辑特定字段的回调函数。</param>
        // 对可持续内容执行公共半开区间校验，并在同一事务中写入类型专用字段。
        // trackId 是轨道头稳定 GUID，itemId 是该轨道内 Clip 的稳定 GUID，均不使用数组索引。
        internal EditResult EditClip(ITrackDocumentHandler handler, string trackId, string itemId, int startFrame,
            int durationFrames, string undoName, Action<SerializedProperty> editSpecific)
        {
            if (!TryFindItem(handler, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return EditResult.Failure("Clip 不存在。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(handler, track, itemId, startFrame, durationFrames))
                return EditResult.Failure("修改结果与同轨内容重叠。");
            Mutate(undoName, () =>
            {
                SetItemFrame(handler, item, startFrame, durationFrames);
                editSpecific(item);
                ExpandDurationForItem(handler, item);
                SortItems(handler, items);
            }, itemId, false);
            return EditResult.Success();
        }

        #endregion

        #region Serialization and validation helpers

        private bool HasConfig => CurrentConfig != null && serializedObject != null;

        // 在一次 SerializedObject.Update 后解析跨轨道移动所需属性，确保只读查询与写入使用同一套锁定和半开区间规则。
        private EditResult ResolveItemTrackMove(ITrackDocumentHandler handler, string sourceTrackId,
            string targetTrackId, string itemId, out SerializedProperty sourceItems,
            out SerializedProperty sourceItem, out int sourceIndex, out SerializedProperty targetItems,
            out int startFrame, out int durationFrames)
        {
            sourceItems = null;
            sourceItem = null;
            sourceIndex = -1;
            targetItems = null;
            startFrame = 0;
            durationFrames = 1;

            if (!HasConfig) return EditResult.Failure("请先选择 SkillConfig。");
            if (handler == null) return EditResult.Failure("轨道数据处理器不存在。");
            if (string.IsNullOrEmpty(sourceTrackId) || string.IsNullOrEmpty(targetTrackId) ||
                string.IsNullOrEmpty(itemId))
                return EditResult.Failure("跨轨道移动缺少稳定 GUID。");
            if (sourceTrackId == targetTrackId)
                return EditResult.Failure("源轨道与目标轨道相同。");

            serializedObject.Update();
            SerializedProperty tracks = GetTracksProperty(handler);
            SerializedProperty sourceTrack = null;
            SerializedProperty targetTrack = null;
            for (int trackIndex = 0; trackIndex < tracks.arraySize; trackIndex++)
            {
                SerializedProperty candidate = tracks.GetArrayElementAtIndex(trackIndex);
                string candidateId = candidate.FindPropertyRelative(DocumentFieldNames.Header)
                    .FindPropertyRelative(DocumentFieldNames.Id).stringValue;
                if (candidateId == sourceTrackId) sourceTrack = candidate;
                if (candidateId == targetTrackId) targetTrack = candidate;
            }

            if (sourceTrack == null) return EditResult.Failure("源轨道不存在。");
            if (targetTrack == null) return EditResult.Failure("目标轨道不存在。");
            if (IsTrackLocked(sourceTrack)) return EditResult.Failure("源轨道已锁定。");
            if (IsTrackLocked(targetTrack)) return EditResult.Failure("目标轨道已锁定。");

            sourceItems = GetItemsProperty(handler, sourceTrack);
            for (int itemIndex = 0; itemIndex < sourceItems.arraySize; itemIndex++)
            {
                SerializedProperty candidate = sourceItems.GetArrayElementAtIndex(itemIndex);
                if (candidate.FindPropertyRelative(DocumentFieldNames.Id).stringValue != itemId) continue;
                sourceItem = candidate;
                sourceIndex = itemIndex;
                break;
            }

            if (sourceItem == null) return EditResult.Failure("时间轴内容不存在。");
            startFrame = GetItemStart(handler, sourceItem);
            durationFrames = GetItemDuration(handler, sourceItem);
            targetItems = GetItemsProperty(handler, targetTrack);
            for (int itemIndex = 0; itemIndex < targetItems.arraySize; itemIndex++)
            {
                SerializedProperty candidate = targetItems.GetArrayElementAtIndex(itemIndex);
                if (candidate.FindPropertyRelative(DocumentFieldNames.Id).stringValue == itemId)
                    return EditResult.Failure("目标轨道已存在相同 GUID 的内容。");
            }

            return CanPlaceInterval(handler, targetTrack, string.Empty, startFrame, durationFrames)
                ? EditResult.Success()
                : EditResult.Failure("目标轨道的对应帧区间已有内容。");
        }

        /// <summary>
        /// 执行一次带 Undo、Dirty 标记和内容变更通知的资产修改。
        /// </summary>
        // 统一记录 Undo、应用 SerializedObject、标记 Dirty 并发送内容变化通知。
        internal void Mutate(string undoName, Action action, string itemId = "", bool broad = true)
        {
            if (!HasConfig) return;
            Undo.RecordObject(CurrentConfig, undoName);
            serializedObject.Update();
            action();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
            ContentChanged?.Invoke();
        }

        /// <summary>
        /// 在 Undo 或 Redo 后重建序列化上下文。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (!HasConfig) return;
            serializedObject = new SerializedObject(CurrentConfig);
            ContentChanged?.Invoke();
        }

        /// <summary>
        /// 为配置、轨道和内容补齐全局唯一的稳定标识符。
        /// </summary>
        private void EnsureStableIds()
        {
            serializedObject.Update();
            bool changed = false;
            HashSet<string> used = new();
            SerializedProperty configId = serializedObject.FindProperty(DocumentFieldNames.Id);
            if (string.IsNullOrWhiteSpace(configId.stringValue) || !used.Add(configId.stringValue))
            {
                configId.stringValue = NewUniqueId(used);
                changed = true;
            }

            foreach (ITrackDocumentHandler handler in handlers)
            {
                SerializedProperty tracks = GetTracksProperty(handler);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty track = tracks.GetArrayElementAtIndex(i);
                    SerializedProperty trackId = track.FindPropertyRelative(DocumentFieldNames.Header).FindPropertyRelative(DocumentFieldNames.Id);
                    if (string.IsNullOrWhiteSpace(trackId.stringValue) || !used.Add(trackId.stringValue))
                    {
                        trackId.stringValue = NewUniqueId(used);
                        changed = true;
                    }

                    SerializedProperty items = GetItemsProperty(handler, track);
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty itemId = items.GetArrayElementAtIndex(j).FindPropertyRelative(DocumentFieldNames.Id);
                        if (string.IsNullOrWhiteSpace(itemId.stringValue) || !used.Add(itemId.stringValue))
                        {
                            itemId.stringValue = NewUniqueId(used);
                            changed = true;
                        }
                    }
                }
            }

            if (!changed) return;
            Undo.RecordObject(CurrentConfig, "初始化技能时间轴 ID");
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
        }

        // 通过 Document 内部 Schema 获取指定轨道类型对应的 Config 序列化列表。
        private SerializedProperty GetTracksProperty(ITrackDocumentHandler handler)
        {
            return serializedObject.FindProperty(handler.TracksPropertyName);
        }

        // 通过 Document 内部 Schema 获取具体轨道中的片段或标记数组。
        // 通过 Handler 声明的属性名获取轨道内容数组，不缓存 SerializedProperty。
        internal static SerializedProperty GetItemsProperty(ITrackDocumentHandler handler, SerializedProperty track)
        {
            return track.FindPropertyRelative(handler.ItemsPropertyName);
        }

        /// <summary>
        /// 按类型和标识符查找轨道序列化属性。
        /// </summary>
        // trackId 是轨道头内保存的稳定 GUID；每次重新查找，确保 Undo 和 Apply 后引用仍然有效。
        internal bool TryFindTrack(ITrackDocumentHandler handler, string trackId, out SerializedProperty tracks,
            out SerializedProperty track, out int index)
        {
            track = null;
            index = -1;
            tracks = null;
            if (!HasConfig) return false;
            serializedObject.Update();
            tracks = GetTracksProperty(handler);
            for (int i = 0; i < tracks.arraySize; i++)
            {
                SerializedProperty candidate = tracks.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative(DocumentFieldNames.Header).FindPropertyRelative(DocumentFieldNames.Id).stringValue != trackId) continue;
                track = candidate;
                index = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按轨道和内容标识符查找对应序列化属性。
        /// </summary>
        // trackId 定位轨道头 GUID，itemId 定位该轨道内的 Clip 或 Marker GUID，二者都不是数组索引。
        internal bool TryFindItem(ITrackDocumentHandler handler, string trackId, string itemId, out SerializedProperty track,
            out SerializedProperty items, out SerializedProperty item, out int index)
        {
            items = null;
            item = null;
            index = -1;
            if (!TryFindTrack(handler, trackId, out _, out track, out _)) return false;
            items = GetItemsProperty(handler, track);
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty candidate = items.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative(DocumentFieldNames.Id).stringValue != itemId) continue;
                item = candidate;
                index = i;
                return true;
            }
            return false;
        }

        // 委托轨道处理器初始化公共帧字段与具体内容字段。
        private static void ResetItem(ITrackDocumentHandler handler, SerializedProperty item,
            string id, int startFrame) => handler.InitializeItem(item, id, startFrame);

        // 向强类型轨道数组追加完整初始化的 Clip，帧区间始终使用 [StartFrame, EndFrame)。
        // 追加并完整初始化内容项；itemId 是新内容的稳定 GUID，持续帧使用半开区间长度。
        internal SerializedProperty AppendItem(ITrackDocumentHandler handler, SerializedProperty items,
            string itemId, int startFrame, int durationFrames)
        {
            int index = items.arraySize;
            items.arraySize++;
            SerializedProperty item = items.GetArrayElementAtIndex(index);
            handler.InitializeItem(item, itemId, startFrame);
            if (handler.SupportsResize)
                item.FindPropertyRelative(handler.DurationPropertyName).intValue = Mathf.Max(1, durationFrames);
            return item;
        }

        // 批量生成稳定 GUID，确保失败校验完成前不会触碰序列化资产。
        // 在修改资产前批量生成稳定 GUID，失败校验不会留下部分数据。
        internal static string[] CreateItemIds(int count)
        {
            string[] ids = new string[count];
            for (int index = 0; index < count; index++) ids[index] = NewId();
            return ids;
        }

        // 读取纯编辑器锁定字段，锁定轨道禁止通过拖拽创建内容。
        // 读取纯编辑器锁定字段，锁定轨道禁止创建内容。
        internal static bool IsTrackLocked(SerializedProperty track)
        {
            SerializedProperty header = track?.FindPropertyRelative(DocumentFieldNames.Header);
            SerializedProperty locked = header?.FindPropertyRelative(DocumentFieldNames.EditorLocked);
            return locked != null && locked.boolValue;
        }

        // 读取 Clip 起始帧或事件 Marker 所在帧。
        private static int GetItemStart(ITrackDocumentHandler handler, SerializedProperty item) =>
            item.FindPropertyRelative(handler.StartFramePropertyName).intValue;

        // 读取持续帧；没有持续字段的事件 Marker 固定占用一帧。
        private static int GetItemDuration(ITrackDocumentHandler handler, SerializedProperty item) =>
            handler.SupportsResize
                ? Mathf.Max(1, item.FindPropertyRelative(handler.DurationPropertyName).intValue)
                : 1;

        // 写入起始帧和可选持续帧，事件 Marker 只写入自身帧位置。
        internal static void SetItemFrame(ITrackDocumentHandler handler, SerializedProperty item,
            int startFrame, int duration)
        {
            item.FindPropertyRelative(handler.StartFramePropertyName).intValue = startFrame;
            if (handler.SupportsResize)
                item.FindPropertyRelative(handler.DurationPropertyName).intValue = duration;
        }

        /// <summary>
        /// 判断目标半开区间是否会与同轨其他片段重叠。
        /// </summary>
        // 使用 [StartFrame, EndFrame) 规则校验同轨排他区间；ignoreItemId 是需排除的稳定 Item GUID。
        internal bool CanPlaceInterval(ITrackDocumentHandler handler, SerializedProperty track, string ignoreItemId,
            int startFrame, int durationFrames)
        {
            int endFrame = startFrame + durationFrames;
            SerializedProperty items = GetItemsProperty(handler, track);
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty other = items.GetArrayElementAtIndex(i);
                if (other.FindPropertyRelative(DocumentFieldNames.Id).stringValue == ignoreItemId) continue;
                int otherStart = GetItemStart(handler, other);
                int otherEnd = otherStart + GetItemDuration(handler, other);
                if (startFrame < otherEnd && endFrame > otherStart) return false;
            }
            return true;
        }

        /// <summary>
        /// 按起始帧稳定排序轨道内容。
        /// </summary>
        // 按起始帧稳定排序；即时区间校验已经保证同轨内容不会落在相同帧区间。
        internal static void SortItems(ITrackDocumentHandler handler, SerializedProperty items)
        {
            for (int i = 1; i < items.arraySize; i++)
            {
                int j = i;
                while (j > 0 && GetItemStart(handler, items.GetArrayElementAtIndex(j - 1)) >
                       GetItemStart(handler, items.GetArrayElementAtIndex(j)))
                {
                    items.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }

        /// <summary>
        /// 查找指定轨道末尾第一个可放置内容的帧。
        /// </summary>
        private int FindAvailableStartFrame(ITrackDocumentHandler handler, SerializedProperty track)
        {
            SerializedProperty items = GetItemsProperty(handler, track);
            int start = 0;
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(i);
                start = Mathf.Max(start, GetItemStart(handler, item) + GetItemDuration(handler, item));
            }
            return start;
        }

        /// <summary>
        /// 当内容超出时间轴时自动扩展配置总帧数。
        /// </summary>
        // 内容越过总长度时只向后扩展技能持续帧，不截断其他内容。
        internal void ExpandDurationForItem(ITrackDocumentHandler handler, SerializedProperty item)
        {
            int required = GetItemStart(handler, item) + GetItemDuration(handler, item);
            SerializedProperty duration = serializedObject.FindProperty(DocumentFieldNames.DurationFrames);
            if (required > duration.intValue) duration.intValue = required;
        }

        /// <summary>
        /// 计算所有轨道内容占用到的最末帧边界。
        /// </summary>
        private int GetContentEndFrame()
        {
            if (!HasConfig) return 1;
            serializedObject.Update();
            int end = 1;
            foreach (ITrackDocumentHandler handler in handlers)
            {
                SerializedProperty tracks = GetTracksProperty(handler);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty items = GetItemsProperty(handler, tracks.GetArrayElementAtIndex(i));
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty item = items.GetArrayElementAtIndex(j);
                        end = Mathf.Max(end, GetItemStart(handler, item) + GetItemDuration(handler, item));
                    }
                }
            }
            return end;
        }

        /// <summary>
        /// 计算修改帧率后每个片段和标记的新帧数据。
        /// </summary>
        private List<FrameTransform> CollectFrameRateTransforms(int oldRate, int newRate)
        {
            List<FrameTransform> result = new();
            foreach (ITrackDocumentHandler handler in handlers)
            {
                SerializedProperty tracks = GetTracksProperty(handler);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty items = GetItemsProperty(handler, tracks.GetArrayElementAtIndex(i));
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty item = items.GetArrayElementAtIndex(j);
                        int oldStart = GetItemStart(handler, item);
                        int oldDuration = GetItemDuration(handler, item);
                        int newStart = Mathf.Max(0, Mathf.RoundToInt(oldStart * (float)newRate / oldRate));
                        int newEnd = Mathf.Max(newStart + 1, Mathf.RoundToInt((oldStart + oldDuration) * (float)newRate / oldRate));
                        result.Add(new FrameTransform(handler, i, item, newStart,
                            newEnd - newStart, oldRate, newRate));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 校验帧率重采样后的同轨区间是否仍不重叠。
        /// </summary>
        private static bool ValidateTransformedIntervals(List<FrameTransform> transforms, out string error)
        {
            foreach (IGrouping<(ITrackDocumentHandler Handler, int TrackIndex), FrameTransform> group in
                     transforms.GroupBy(x => (x.Handler, x.TrackIndex)))
            {
                FrameTransform[] ordered = group.OrderBy(x => x.StartFrame).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                {
                    if (ordered[i].StartFrame < ordered[i - 1].StartFrame + ordered[i - 1].DurationFrames)
                    {
                        error = "修改 FPS 会导致同轨内容重叠，操作已取消。";
                        return false;
                    }
                }
            }
            error = string.Empty;
            return true;
        }

        /// <summary>
        /// 生成指定类型轨道的默认显示名称。
        /// </summary>
        private static string GetDefaultTrackName(ITrackDocumentHandler handler, int number) =>
            $"{handler.DefaultTrackNamePrefix} {number}";
        /// <summary>
        /// 生成新的无分隔符 GUID。
        /// </summary>
        private static string NewId() => Guid.NewGuid().ToString("N");
        /// <summary>
        /// 生成一个不在给定集合中的稳定标识符。
        /// </summary>
        private static string NewUniqueId(ISet<string> used)
        {
            string id;
            do id = NewId(); while (!used.Add(id));
            return id;
        }

        #endregion

        #region Frame-rate transform value

        /// <summary>
        /// 保存一次 FPS 重采样后的目标帧区间及其轨道处理器。
        /// </summary>
        private sealed class FrameTransform
        {
            public ITrackDocumentHandler Handler { get; }
            public int TrackIndex { get; }
            public int StartFrame { get; }
            public int DurationFrames { get; }
            private readonly SerializedProperty item;
            private readonly int oldFrameRate;
            private readonly int newFrameRate;

            /// <summary>
            /// 创建并初始化 FrameTransform。
            /// </summary>
            public FrameTransform(ITrackDocumentHandler handler, int trackIndex, SerializedProperty item,
                int startFrame, int durationFrames, int oldFrameRate, int newFrameRate)
            {
                Handler = handler;
                TrackIndex = trackIndex;
                this.item = item.Copy();
                StartFrame = startFrame;
                DurationFrames = durationFrames;
                this.oldFrameRate = oldFrameRate;
                this.newFrameRate = newFrameRate;
            }

            /// <summary>
            /// 将重采样后的帧区间写入对应序列化属性。
            /// </summary>
            public void Apply()
            {
                SetItemFrame(Handler, item, StartFrame, DurationFrames);
                Handler.ResampleSpecificFrameFields(item, oldFrameRate, newFrameRate);
            }
        }

        #endregion

    }
}
#endif
