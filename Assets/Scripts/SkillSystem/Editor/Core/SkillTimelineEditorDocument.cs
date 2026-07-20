#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    internal sealed class SkillTimelineEditorDocument : IDisposable
    {
        #region Serialized state and events

        private const string AnimationTracks = "animationTracks";
        private const string VfxTracks = "vfxTracks";
        private const string EventTracks = "eventTracks";

        private SerializedObject serializedObject;

        public event Action<SkillTimelineContentChangedEventArgs> ContentChanged;
        public event Action ConfigChanged;

        public SkillConfig CurrentConfig { get; private set; }
        public SerializedObject SerializedObject => serializedObject;

        #endregion

        #region Lifecycle and configuration

        /// <summary>
        /// 创建并初始化 SkillTimelineEditorDocument。
        /// </summary>
        public SkillTimelineEditorDocument()
        {
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
            ContentChanged?.Invoke(new SkillTimelineContentChangedEventArgs());
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
        public TimelineEditResult ChangeFrameRate(int frameRate)
        {
            if (!HasConfig)
            {
                return TimelineEditResult.Failure("请先选择 SkillConfig。");
            }

            frameRate = Mathf.Clamp(frameRate, 1, 240);
            int oldFrameRate = CurrentConfig.FrameRate;
            if (oldFrameRate == frameRate)
            {
                return TimelineEditResult.Success();
            }

            serializedObject.Update();
            List<FrameTransform> transforms = CollectFrameRateTransforms(oldFrameRate, frameRate);
            if (!ValidateTransformedIntervals(transforms, out string error))
            {
                return TimelineEditResult.Failure(error);
            }

            Mutate("修改技能时间轴 FPS", () =>
            {
                serializedObject.FindProperty("frameRate").intValue = frameRate;
                foreach (FrameTransform transform in transforms)
                {
                    transform.Apply();
                }

                int duration = Mathf.Max(1, Mathf.RoundToInt(CurrentConfig.DurationFrames * (float)frameRate / oldFrameRate));
                serializedObject.FindProperty("durationFrames").intValue = duration;
            });
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 修改时间轴总帧数，并拒绝截断现有内容。
        /// </summary>
        public TimelineEditResult SetDurationFrames(int durationFrames)
        {
            if (!HasConfig)
            {
                return TimelineEditResult.Failure("请先选择 SkillConfig。");
            }

            durationFrames = Mathf.Max(1, durationFrames);
            int required = GetContentEndFrame();
            if (durationFrames < required)
            {
                return TimelineEditResult.Failure($"时间轴至少需要 {required} 帧才能容纳现有内容。");
            }

            Mutate("修改技能时间轴长度", () => serializedObject.FindProperty("durationFrames").intValue = durationFrames);
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 把时间轴总帧数裁剪到容纳现有内容所需的长度。
        /// </summary>
        public void TrimToContent()
        {
            if (!HasConfig) return;
            int duration = Mathf.Max(1, GetContentEndFrame());
            Mutate("裁剪技能时间轴", () => serializedObject.FindProperty("durationFrames").intValue = duration);
        }

        #endregion

        #region Track editing

        /// <summary>
        /// 创建指定类型的轨道并返回其稳定标识符。
        /// </summary>
        public string AddTrack(SkillTrackKind kind)
        {
            if (!HasConfig) return string.Empty;
            string id = NewId();
            Mutate("添加技能轨道", () =>
            {
                SerializedProperty tracks = GetTracksProperty(kind);
                int index = tracks.arraySize;
                tracks.arraySize++;
                SerializedProperty track = tracks.GetArrayElementAtIndex(index);
                SerializedProperty header = track.FindPropertyRelative("header");
                header.FindPropertyRelative("id").stringValue = id;
                header.FindPropertyRelative("displayName").stringValue = GetDefaultTrackName(kind, index + 1);
                header.FindPropertyRelative("muted").boolValue = false;
                header.FindPropertyRelative("editorLocked").boolValue = false;
                header.FindPropertyRelative("editorColor").colorValue = Color.white;
                GetItemsProperty(kind, track).ClearArray();
            });
            return id;
        }

        /// <summary>
        /// 删除指定类型和标识符的轨道。
        /// </summary>
        public TimelineEditResult RemoveTrack(SkillTrackKind kind, string trackId)
        {
            if (!TryFindTrack(kind, trackId, out SerializedProperty tracks, out _, out int index))
                return TimelineEditResult.Failure("轨道不存在。");
            Mutate("删除技能轨道", () => tracks.DeleteArrayElementAtIndex(index));
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 在同类型轨道列表中重排指定轨道。
        /// </summary>
        public TimelineEditResult MoveTrack(SkillTrackKind kind, string trackId, int offset)
        {
            if (!TryFindTrack(kind, trackId, out SerializedProperty tracks, out _, out int index))
                return TimelineEditResult.Failure("轨道不存在。");
            int target = Mathf.Clamp(index + offset, 0, tracks.arraySize - 1);
            if (target == index) return TimelineEditResult.Success();
            Mutate("重排技能轨道", () => tracks.MoveArrayElement(index, target));
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 修改指定轨道的名称、静音和编辑锁定状态。
        /// </summary>
        public TimelineEditResult EditTrack(SkillTrackKind kind, string trackId, string displayName, bool muted, bool locked)
        {
            if (!TryFindTrack(kind, trackId, out _, out SerializedProperty track, out _))
                return TimelineEditResult.Failure("轨道不存在。");
            Mutate("修改技能轨道", () =>
            {
                SerializedProperty header = track.FindPropertyRelative("header");
                header.FindPropertyRelative("displayName").stringValue = string.IsNullOrWhiteSpace(displayName) ? "未命名轨道" : displayName.Trim();
                header.FindPropertyRelative("muted").boolValue = muted;
                header.FindPropertyRelative("editorLocked").boolValue = locked;
            }, trackId, false);
            return TimelineEditResult.Success();
        }

        #endregion

        #region Timeline content editing

        /// <summary>
        /// 在指定轨道末尾的可用帧创建默认内容项。
        /// </summary>
        public string AddItem(SkillTrackKind kind, string trackId)
        {
            if (!TryFindTrack(kind, trackId, out _, out SerializedProperty track, out _)) return string.Empty;
            string id = NewId();
            Mutate("添加技能时间轴内容", () =>
            {
                SerializedProperty items = GetItemsProperty(kind, track);
                int index = items.arraySize;
                items.arraySize++;
                SerializedProperty item = items.GetArrayElementAtIndex(index);
                ResetItem(kind, item, id, FindAvailableStartFrame(kind, track));
                ExpandDurationForItem(kind, item);
                SortItems(kind, items);
            });
            return id;
        }

        /// <summary>
        /// 从指定轨道删除片段或事件标记。
        /// </summary>
        public TimelineEditResult RemoveItem(SkillTrackKind kind, string trackId, string itemId)
        {
            if (!TryFindItem(kind, trackId, itemId, out _, out SerializedProperty items, out _, out int index))
                return TimelineEditResult.Failure("时间轴内容不存在。");
            Mutate("删除技能时间轴内容", () => items.DeleteArrayElementAtIndex(index));
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 复制指定内容项，生成新标识符并放置到可用帧。
        /// </summary>
        public string DuplicateItem(SkillTrackKind kind, string trackId, string itemId)
        {
            if (!TryFindItem(kind, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out int index)) return string.Empty;

            string newId = NewId();
            int start = GetItemStart(kind, item);
            int duration = GetItemDuration(kind, item);
            int proposed = kind == SkillTrackKind.Event ? start + 1 : start + duration;
            if (kind != SkillTrackKind.Event && !CanPlaceInterval(kind, track, itemId, proposed, duration))
                proposed = FindAvailableStartFrame(kind, track);

            Mutate("复制技能时间轴内容", () =>
            {
                items.InsertArrayElementAtIndex(index);
                SerializedProperty copy = items.GetArrayElementAtIndex(index + 1);
                copy.FindPropertyRelative("id").stringValue = newId;
                SetItemFrame(kind, copy, proposed, duration);
                ExpandDurationForItem(kind, copy);
                SortItems(kind, items);
            });
            return newId;
        }

        /// <summary>
        /// 校验并移动指定片段或事件标记到目标帧。
        /// </summary>
        public TimelineEditResult MoveItem(SkillTrackKind kind, string trackId, string itemId, int startFrame)
        {
            if (!TryFindItem(kind, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return TimelineEditResult.Failure("时间轴内容不存在。");
            startFrame = Mathf.Max(0, startFrame);
            int duration = GetItemDuration(kind, item);
            if (kind != SkillTrackKind.Event && !CanPlaceInterval(kind, track, itemId, startFrame, duration))
                return TimelineEditResult.Failure("目标位置与同轨内容重叠。");

            Mutate("移动技能时间轴内容", () =>
            {
                SetItemFrame(kind, item, startFrame, duration);
                ExpandDurationForItem(kind, item);
                SortItems(kind, items);
            }, itemId, false);
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 校验并提交片段的新半开帧区间。
        /// </summary>
        public TimelineEditResult ResizeItem(SkillTrackKind kind, string trackId, string itemId, int startFrame, int durationFrames)
        {
            if (kind == SkillTrackKind.Event) return TimelineEditResult.Failure("事件 Marker 不支持裁剪。");
            if (!TryFindItem(kind, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return TimelineEditResult.Failure("时间轴内容不存在。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(kind, track, itemId, startFrame, durationFrames))
                return TimelineEditResult.Failure("裁剪结果与同轨内容重叠。");

            Mutate("裁剪技能时间轴内容", () =>
            {
                SetItemFrame(kind, item, startFrame, durationFrames);
                ExpandDurationForItem(kind, item);
                SortItems(kind, items);
            }, itemId, false);
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 校验并提交动画片段字段修改。
        /// </summary>
        public TimelineEditResult EditAnimationClip(string trackId, string itemId, AnimationClipEditRequest request) =>
            EditClipCommon(SkillTrackKind.Animation, trackId, itemId, request.StartFrame, request.DurationFrames,
                "修改动画 Clip", item =>
                {
                    item.FindPropertyRelative("animationClip").objectReferenceValue = request.AnimationClip;
                    item.FindPropertyRelative("sourceStartFrame").intValue = Mathf.Max(0, request.SourceStartFrame);
                    item.FindPropertyRelative("playbackSpeed").floatValue = Mathf.Max(0.01f, request.PlaybackSpeed);
                });

        /// <summary>
        /// 校验并提交特效片段字段修改。
        /// </summary>
        public TimelineEditResult EditVfxClip(string trackId, string itemId, VfxClipEditRequest request) =>
            EditClipCommon(SkillTrackKind.Vfx, trackId, itemId, request.StartFrame, request.DurationFrames,
                "修改特效 Clip", item =>
                {
                    item.FindPropertyRelative("prefab").objectReferenceValue = request.Prefab;
                    item.FindPropertyRelative("bindingPath").stringValue = request.BindingPath ?? string.Empty;
                    item.FindPropertyRelative("localPosition").vector3Value = request.LocalPosition;
                    item.FindPropertyRelative("localEulerAngles").vector3Value = request.LocalEulerAngles;
                    item.FindPropertyRelative("localScale").vector3Value = request.LocalScale;
                    item.FindPropertyRelative("followMode").enumValueIndex = (int)request.FollowMode;
                    item.FindPropertyRelative("stopMode").enumValueIndex = (int)request.StopMode;
                });

        /// <summary>
        /// 校验并提交事件标记字段修改。
        /// </summary>
        public TimelineEditResult EditEventMarker(string trackId, string itemId, EventMarkerEditRequest request)
        {
            if (!TryFindItem(SkillTrackKind.Event, trackId, itemId, out _, out SerializedProperty items,
                    out SerializedProperty item, out _)) return TimelineEditResult.Failure("事件 Marker 不存在。");
            Mutate("修改事件 Marker", () =>
            {
                item.FindPropertyRelative("frame").intValue = Mathf.Max(0, request.Frame);
                item.FindPropertyRelative("eventTypeName").stringValue = request.EventTypeName ?? string.Empty;
                item.FindPropertyRelative("displayName").stringValue = string.IsNullOrWhiteSpace(request.DisplayName) ? "事件" : request.DisplayName.Trim();
                item.FindPropertyRelative("parameterText").stringValue = request.ParameterText ?? string.Empty;
                ExpandDurationForItem(SkillTrackKind.Event, item);
                SortItems(SkillTrackKind.Event, items);
            }, itemId, false);
            return TimelineEditResult.Success();
        }

        /// <summary>
        /// 校验当前配置并返回全部数据问题。
        /// </summary>
        public IReadOnlyList<string> Validate() => SkillTimelineValidator.Validate(CurrentConfig);

        /// <summary>
        /// 执行动画和特效片段共享的区间校验与资产修改流程。
        /// </summary>
        private TimelineEditResult EditClipCommon(SkillTrackKind kind, string trackId, string itemId, int startFrame,
            int durationFrames, string undoName, Action<SerializedProperty> editSpecific)
        {
            if (!TryFindItem(kind, trackId, itemId, out SerializedProperty track, out SerializedProperty items,
                    out SerializedProperty item, out _)) return TimelineEditResult.Failure("Clip 不存在。");
            startFrame = Mathf.Max(0, startFrame);
            durationFrames = Mathf.Max(1, durationFrames);
            if (!CanPlaceInterval(kind, track, itemId, startFrame, durationFrames))
                return TimelineEditResult.Failure("修改结果与同轨内容重叠。");
            Mutate(undoName, () =>
            {
                SetItemFrame(kind, item, startFrame, durationFrames);
                editSpecific(item);
                ExpandDurationForItem(kind, item);
                SortItems(kind, items);
            }, itemId, false);
            return TimelineEditResult.Success();
        }

        #endregion

        #region Serialization and validation helpers

        private bool HasConfig => CurrentConfig != null && serializedObject != null;

        /// <summary>
        /// 执行一次带 Undo、Dirty 标记和内容变更通知的资产修改。
        /// </summary>
        private void Mutate(string undoName, Action action, string itemId = "", bool broad = true)
        {
            if (!HasConfig) return;
            Undo.RecordObject(CurrentConfig, undoName);
            serializedObject.Update();
            action();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(CurrentConfig);
            ContentChanged?.Invoke(new SkillTimelineContentChangedEventArgs(itemId, broad));
        }

        /// <summary>
        /// 在 Undo 或 Redo 后重建序列化上下文。
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if (!HasConfig) return;
            serializedObject = new SerializedObject(CurrentConfig);
            ContentChanged?.Invoke(new SkillTimelineContentChangedEventArgs());
        }

        /// <summary>
        /// 为配置、轨道和内容补齐全局唯一的稳定标识符。
        /// </summary>
        private void EnsureStableIds()
        {
            serializedObject.Update();
            bool changed = false;
            HashSet<string> used = new();
            SerializedProperty configId = serializedObject.FindProperty("id");
            if (string.IsNullOrWhiteSpace(configId.stringValue) || !used.Add(configId.stringValue))
            {
                configId.stringValue = NewUniqueId(used);
                changed = true;
            }

            foreach (SkillTrackKind kind in Enum.GetValues(typeof(SkillTrackKind)))
            {
                SerializedProperty tracks = GetTracksProperty(kind);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty track = tracks.GetArrayElementAtIndex(i);
                    SerializedProperty trackId = track.FindPropertyRelative("header").FindPropertyRelative("id");
                    if (string.IsNullOrWhiteSpace(trackId.stringValue) || !used.Add(trackId.stringValue))
                    {
                        trackId.stringValue = NewUniqueId(used);
                        changed = true;
                    }

                    SerializedProperty items = GetItemsProperty(kind, track);
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty itemId = items.GetArrayElementAtIndex(j).FindPropertyRelative("id");
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

        /// <summary>
        /// 获取指定轨道类型对应的序列化列表。
        /// </summary>
        private SerializedProperty GetTracksProperty(SkillTrackKind kind) => serializedObject.FindProperty(kind switch
        {
            SkillTrackKind.Animation => AnimationTracks,
            SkillTrackKind.Vfx => VfxTracks,
            _ => EventTracks
        });

        /// <summary>
        /// 获取具体轨道中的片段或标记数组属性。
        /// </summary>
        private static SerializedProperty GetItemsProperty(SkillTrackKind kind, SerializedProperty track) =>
            track.FindPropertyRelative(kind == SkillTrackKind.Event ? "markers" : "clips");

        /// <summary>
        /// 按类型和标识符查找轨道序列化属性。
        /// </summary>
        private bool TryFindTrack(SkillTrackKind kind, string trackId, out SerializedProperty tracks,
            out SerializedProperty track, out int index)
        {
            track = null;
            index = -1;
            tracks = null;
            if (!HasConfig) return false;
            serializedObject.Update();
            tracks = GetTracksProperty(kind);
            for (int i = 0; i < tracks.arraySize; i++)
            {
                SerializedProperty candidate = tracks.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("header").FindPropertyRelative("id").stringValue != trackId) continue;
                track = candidate;
                index = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 按轨道和内容标识符查找对应序列化属性。
        /// </summary>
        private bool TryFindItem(SkillTrackKind kind, string trackId, string itemId, out SerializedProperty track,
            out SerializedProperty items, out SerializedProperty item, out int index)
        {
            items = null;
            item = null;
            index = -1;
            if (!TryFindTrack(kind, trackId, out _, out track, out _)) return false;
            items = GetItemsProperty(kind, track);
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty candidate = items.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("id").stringValue != itemId) continue;
                item = candidate;
                index = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 初始化新增片段或事件标记的默认字段。
        /// </summary>
        private static void ResetItem(SkillTrackKind kind, SerializedProperty item, string id, int startFrame)
        {
            item.FindPropertyRelative("id").stringValue = id;
            if (kind == SkillTrackKind.Event)
            {
                item.FindPropertyRelative("frame").intValue = startFrame;
                item.FindPropertyRelative("eventTypeName").stringValue = string.Empty;
                item.FindPropertyRelative("displayName").stringValue = "事件";
                item.FindPropertyRelative("parameterText").stringValue = string.Empty;
                return;
            }

            item.FindPropertyRelative("startFrame").intValue = startFrame;
            item.FindPropertyRelative("durationFrames").intValue = 1;
            if (kind == SkillTrackKind.Animation)
            {
                item.FindPropertyRelative("animationClip").objectReferenceValue = null;
                item.FindPropertyRelative("sourceStartFrame").intValue = 0;
                item.FindPropertyRelative("playbackSpeed").floatValue = 1f;
            }
            else
            {
                item.FindPropertyRelative("prefab").objectReferenceValue = null;
                item.FindPropertyRelative("bindingPath").stringValue = string.Empty;
                item.FindPropertyRelative("localPosition").vector3Value = Vector3.zero;
                item.FindPropertyRelative("localEulerAngles").vector3Value = Vector3.zero;
                item.FindPropertyRelative("localScale").vector3Value = Vector3.one;
                item.FindPropertyRelative("followMode").enumValueIndex = 0;
                item.FindPropertyRelative("stopMode").enumValueIndex = 0;
            }
        }

        /// <summary>
        /// 读取内容项的起始帧或事件帧。
        /// </summary>
        private static int GetItemStart(SkillTrackKind kind, SerializedProperty item) =>
            item.FindPropertyRelative(kind == SkillTrackKind.Event ? "frame" : "startFrame").intValue;

        /// <summary>
        /// 读取内容项持续帧数；事件标记固定视为一帧。
        /// </summary>
        private static int GetItemDuration(SkillTrackKind kind, SerializedProperty item) =>
            kind == SkillTrackKind.Event ? 1 : Mathf.Max(1, item.FindPropertyRelative("durationFrames").intValue);

        /// <summary>
        /// 写入内容项的起始帧和持续帧数。
        /// </summary>
        private static void SetItemFrame(SkillTrackKind kind, SerializedProperty item, int startFrame, int duration)
        {
            item.FindPropertyRelative(kind == SkillTrackKind.Event ? "frame" : "startFrame").intValue = startFrame;
            if (kind != SkillTrackKind.Event) item.FindPropertyRelative("durationFrames").intValue = duration;
        }

        /// <summary>
        /// 判断目标半开区间是否会与同轨其他片段重叠。
        /// </summary>
        private static bool CanPlaceInterval(SkillTrackKind kind, SerializedProperty track, string ignoreItemId,
            int startFrame, int durationFrames)
        {
            int endFrame = startFrame + durationFrames;
            SerializedProperty items = GetItemsProperty(kind, track);
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty other = items.GetArrayElementAtIndex(i);
                if (other.FindPropertyRelative("id").stringValue == ignoreItemId) continue;
                int otherStart = GetItemStart(kind, other);
                int otherEnd = otherStart + GetItemDuration(kind, other);
                if (startFrame < otherEnd && endFrame > otherStart) return false;
            }
            return true;
        }

        /// <summary>
        /// 按起始帧稳定排序轨道内容。
        /// </summary>
        private static void SortItems(SkillTrackKind kind, SerializedProperty items)
        {
            for (int i = 1; i < items.arraySize; i++)
            {
                int j = i;
                while (j > 0 && GetItemStart(kind, items.GetArrayElementAtIndex(j - 1)) >
                       GetItemStart(kind, items.GetArrayElementAtIndex(j)))
                {
                    items.MoveArrayElement(j, j - 1);
                    j--;
                }
            }
        }

        /// <summary>
        /// 查找指定轨道末尾第一个可放置内容的帧。
        /// </summary>
        private int FindAvailableStartFrame(SkillTrackKind kind, SerializedProperty track)
        {
            SerializedProperty items = GetItemsProperty(kind, track);
            int start = 0;
            for (int i = 0; i < items.arraySize; i++)
            {
                SerializedProperty item = items.GetArrayElementAtIndex(i);
                start = Mathf.Max(start, GetItemStart(kind, item) + GetItemDuration(kind, item));
            }
            return start;
        }

        /// <summary>
        /// 当内容超出时间轴时自动扩展配置总帧数。
        /// </summary>
        private void ExpandDurationForItem(SkillTrackKind kind, SerializedProperty item)
        {
            int required = GetItemStart(kind, item) + GetItemDuration(kind, item);
            SerializedProperty duration = serializedObject.FindProperty("durationFrames");
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
            foreach (SkillTrackKind kind in Enum.GetValues(typeof(SkillTrackKind)))
            {
                SerializedProperty tracks = GetTracksProperty(kind);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty items = GetItemsProperty(kind, tracks.GetArrayElementAtIndex(i));
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty item = items.GetArrayElementAtIndex(j);
                        end = Mathf.Max(end, GetItemStart(kind, item) + GetItemDuration(kind, item));
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
            foreach (SkillTrackKind kind in Enum.GetValues(typeof(SkillTrackKind)))
            {
                SerializedProperty tracks = GetTracksProperty(kind);
                for (int i = 0; i < tracks.arraySize; i++)
                {
                    SerializedProperty items = GetItemsProperty(kind, tracks.GetArrayElementAtIndex(i));
                    for (int j = 0; j < items.arraySize; j++)
                    {
                        SerializedProperty item = items.GetArrayElementAtIndex(j);
                        int oldStart = GetItemStart(kind, item);
                        int oldDuration = GetItemDuration(kind, item);
                        int newStart = Mathf.Max(0, Mathf.RoundToInt(oldStart * (float)newRate / oldRate));
                        int newEnd = Mathf.Max(newStart + 1, Mathf.RoundToInt((oldStart + oldDuration) * (float)newRate / oldRate));
                        result.Add(new FrameTransform(kind, i, item, newStart, newEnd - newStart));
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
            foreach (IGrouping<(SkillTrackKind Kind, int TrackIndex), FrameTransform> group in
                     transforms.Where(x => x.Kind != SkillTrackKind.Event).GroupBy(x => (x.Kind, x.TrackIndex)))
            {
                FrameTransform[] ordered = group.OrderBy(x => x.StartFrame).ToArray();
                for (int i = 1; i < ordered.Length; i++)
                {
                    if (ordered[i].StartFrame < ordered[i - 1].StartFrame + ordered[i - 1].DurationFrames)
                    {
                        error = "修改 FPS 会导致同轨 Clip 重叠，操作已取消。";
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
        private static string GetDefaultTrackName(SkillTrackKind kind, int number) => kind switch
        {
            SkillTrackKind.Animation => $"动画轨道 {number}",
            SkillTrackKind.Vfx => $"特效轨道 {number}",
            _ => $"事件轨道 {number}"
        };

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

        private sealed class FrameTransform
        {
            public SkillTrackKind Kind { get; }
            public int TrackIndex { get; }
            public int StartFrame { get; }
            public int DurationFrames { get; }
            private readonly SerializedProperty item;

            /// <summary>
            /// 创建并初始化 FrameTransform。
            /// </summary>
            public FrameTransform(SkillTrackKind kind, int trackIndex, SerializedProperty item, int startFrame, int durationFrames)
            {
                Kind = kind;
                TrackIndex = trackIndex;
                this.item = item.Copy();
                StartFrame = startFrame;
                DurationFrames = durationFrames;
            }

            /// <summary>
            /// 将重采样后的帧区间写入对应序列化属性。
            /// </summary>
            public void Apply() => SetItemFrame(Kind, item, StartFrame, DurationFrames);
        }

        #endregion
    }
}
#endif
