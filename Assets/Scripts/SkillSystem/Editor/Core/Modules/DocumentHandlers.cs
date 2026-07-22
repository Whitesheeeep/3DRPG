#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.SkillSystem;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 提供轨道序列化结构与公共 Item 初始化流程，具体字段和业务编辑由派生 Handler 完成。
    /// </summary>
    internal abstract class TrackDocumentHandler : ITrackDocumentHandler
    {
        public string TracksPropertyName { get; }
        public string ItemsPropertyName { get; }
        public string StartFramePropertyName { get; }
        public string DurationPropertyName { get; }
        public string DefaultTrackNamePrefix { get; }
        public bool SupportsResize => !string.IsNullOrEmpty(DurationPropertyName);
        public bool RequiresExclusiveIntervals { get; }

        // 保存不可变序列化结构，不缓存会在 Undo 或 Apply 后失效的 SerializedProperty。
        protected TrackDocumentHandler(string tracksPropertyName, string itemsPropertyName,
            string startFramePropertyName, string durationPropertyName,
            string defaultTrackNamePrefix, bool requiresExclusiveIntervals)
        {
            TracksPropertyName = tracksPropertyName;
            ItemsPropertyName = itemsPropertyName;
            StartFramePropertyName = startFramePropertyName;
            DurationPropertyName = durationPropertyName ?? string.Empty;
            DefaultTrackNamePrefix = defaultTrackNamePrefix;
            RequiresExclusiveIntervals = requiresExclusiveIntervals;
        }

        /// <summary>
        /// 初始化内容公共 GUID、起始帧和可选持续帧，再初始化具体轨道字段。
        /// </summary>
        /// <param name="item">新建内容对应的 SerializedProperty。</param>
        /// <param name="id">分配给新 Clip 或 Marker 的稳定 Item GUID。</param>
        /// <param name="startFrame">新内容所在的非负整数帧。</param>
        public void InitializeItem(SerializedProperty item, string id, int startFrame)
        {
            item.FindPropertyRelative(DocumentFieldNames.Id).stringValue = id;
            item.FindPropertyRelative(StartFramePropertyName).intValue = Mathf.Max(0, startFrame);
            if (SupportsResize)
                item.FindPropertyRelative(DurationPropertyName).intValue = 1;
            InitializeSpecificFields(item);
        }

        /// <summary>
        /// 根据具体创建请求批量创建内容；实现必须通过 Document 事务提交。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        public abstract ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request);

        /// <summary>
        /// 根据具体编辑请求修改内容；实现必须通过 Document 事务提交。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        public abstract EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request);

        // 初始化某种 Item 独有字段，调用时公共帧字段已经有效。
        protected abstract void InitializeSpecificFields(SerializedProperty item);
    }

    /// <summary>
    /// 定义动画轨道序列化结构，并处理 AnimationClip 的创建与字段编辑。
    /// </summary>
    internal sealed class AnimationDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建动画轨道数据处理器。
        /// </summary>
        public AnimationDocumentHandler()
            : base(DocumentFieldNames.AnimationTracks, DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "动画轨道", true)
        {
        }

        /// <summary>
        /// 校验动画创建请求，并在一次 Undo 中连续创建全部 Clip。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        public override ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request)
        {
            if (request is not AnimationCreateRequest animation)
                return ItemsCreateResult.Failure("动画轨道收到不匹配的创建请求。");
            if (!document.TryFindTrack(this, trackId, out _, out SerializedProperty track, out _))
                return ItemsCreateResult.Failure("动画轨道不存在。");
            if (Document.IsTrackLocked(track)) return ItemsCreateResult.Failure("目标轨道已锁定。");
            if (animation.Clips == null || animation.Clips.Count == 0)
                return ItemsCreateResult.Failure("没有可创建的 AnimationClip。");

            int[] durations = new int[animation.Clips.Count];
            long total = 0;
            for (int index = 0; index < animation.Clips.Count; index++)
            {
                AnimationClip clip = animation.Clips[index];
                if (clip == null || !EditorUtility.IsPersistent(clip))
                    return ItemsCreateResult.Failure("动画轨道只接受 Project 中的 AnimationClip。");
                durations[index] = Mathf.Max(1,
                    Mathf.CeilToInt(clip.length * document.CurrentConfig.FrameRate));
                total += durations[index];
            }

            int startFrame = Mathf.Max(0, animation.StartFrame);
            if (total > int.MaxValue - (long)startFrame)
                return ItemsCreateResult.Failure("拖入动画的总持续帧超出范围。");
            if (!document.CanPlaceInterval(this, track, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨动画片段重叠。");

            string[] itemIds = Document.CreateItemIds(animation.Clips.Count);
            document.Mutate("拖入动画素材", () =>
            {
                SerializedProperty items = Document.GetItemsProperty(this, track);
                int nextFrame = startFrame;
                for (int index = 0; index < animation.Clips.Count; index++)
                {
                    SerializedProperty item = document.AppendItem(this, items,
                        itemIds[index], nextFrame, durations[index]);
                    item.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue = animation.Clips[index];
                    document.ExpandDurationForItem(this, item);
                    nextFrame += durations[index];
                }
                Document.SortItems(this, items);
            });
            return ItemsCreateResult.Success(itemIds);
        }

        /// <summary>
        /// 校验动画编辑请求并提交区间、素材、源偏移和播放速度。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not AnimationEditRequest animation)
                return EditResult.Failure("动画轨道收到不匹配的编辑请求。");
            return document.EditClip(this, trackId, itemId, animation.StartFrame,
                animation.DurationFrames, "修改动画 Clip", item =>
                {
                    item.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue = animation.AnimationClip;
                    item.FindPropertyRelative(DocumentFieldNames.SourceStartFrame).intValue = Mathf.Max(0, animation.SourceStartFrame);
                    item.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue = Mathf.Max(0.01f, animation.PlaybackSpeed);
                });
        }

        // 初始化动画 Clip 的素材、源偏移和播放速度默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.SourceStartFrame).intValue = 0;
            item.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue = 1f;
        }
    }

    /// <summary>
    /// 定义特效轨道序列化结构，并处理 Prefab Clip 的创建与字段编辑。
    /// </summary>
    internal sealed class VfxDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建特效轨道数据处理器。
        /// </summary>
        public VfxDocumentHandler()
            : base(DocumentFieldNames.VfxTracks, DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "特效轨道", true)
        {
        }

        /// <summary>
        /// 校验特效创建请求，并在一次 Undo 中连续创建全部 Clip。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        public override ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request)
        {
            if (request is not VfxCreateRequest vfx)
                return ItemsCreateResult.Failure("特效轨道收到不匹配的创建请求。");
            if (!document.TryFindTrack(this, trackId, out _, out SerializedProperty track, out _))
                return ItemsCreateResult.Failure("特效轨道不存在。");
            if (Document.IsTrackLocked(track)) return ItemsCreateResult.Failure("目标轨道已锁定。");
            if (vfx.Prefabs == null || vfx.Prefabs.Count == 0)
                return ItemsCreateResult.Failure("没有可创建的特效 Prefab。");

            for (int index = 0; index < vfx.Prefabs.Count; index++)
            {
                GameObject prefab = vfx.Prefabs[index];
                if (prefab == null || !EditorUtility.IsPersistent(prefab) ||
                    !PrefabUtility.IsPartOfPrefabAsset(prefab))
                    return ItemsCreateResult.Failure("特效轨道只接受 Project 中的 Prefab。");
            }

            int startFrame = Mathf.Max(0, vfx.StartFrame);
            int durationFrames = Mathf.Max(1, vfx.DurationFrames);
            long total = (long)durationFrames * vfx.Prefabs.Count;
            if (total > int.MaxValue - (long)startFrame)
                return ItemsCreateResult.Failure("拖入特效的总持续帧超出范围。");
            if (!document.CanPlaceInterval(this, track, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨特效片段重叠。");

            string[] itemIds = Document.CreateItemIds(vfx.Prefabs.Count);
            document.Mutate("拖入特效素材", () =>
            {
                SerializedProperty items = Document.GetItemsProperty(this, track);
                int nextFrame = startFrame;
                for (int index = 0; index < vfx.Prefabs.Count; index++)
                {
                    SerializedProperty item = document.AppendItem(this, items,
                        itemIds[index], nextFrame, durationFrames);
                    item.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue = vfx.Prefabs[index];
                    document.ExpandDurationForItem(this, item);
                    nextFrame += durationFrames;
                }
                Document.SortItems(this, items);
            });
            return ItemsCreateResult.Success(itemIds);
        }

        /// <summary>
        /// 校验特效编辑请求并提交区间、Prefab、绑定和局部变换字段。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not VfxEditRequest vfx)
                return EditResult.Failure("特效轨道收到不匹配的编辑请求。");
            return document.EditClip(this, trackId, itemId, vfx.StartFrame,
                vfx.DurationFrames, "修改特效 Clip", item =>
                {
                    item.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue = vfx.Prefab;
                    item.FindPropertyRelative(DocumentFieldNames.BindingPath).stringValue = vfx.BindingPath ?? string.Empty;
                    item.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value = vfx.LocalPosition;
                    item.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value = vfx.LocalEulerAngles;
                    item.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value = vfx.LocalScale;
                    item.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex = (int)vfx.FollowMode;
                    item.FindPropertyRelative(DocumentFieldNames.StopMode).enumValueIndex = (int)vfx.StopMode;
                });
        }

        // 初始化特效 Clip 的 Prefab、绑定、局部变换和结束策略默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.BindingPath).stringValue = string.Empty;
            item.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value = Vector3.zero;
            item.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value = Vector3.zero;
            item.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value = Vector3.one;
            item.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex = 0;
            item.FindPropertyRelative(DocumentFieldNames.StopMode).enumValueIndex = 0;
        }
    }

    /// <summary>
    /// 定义音频轨道序列化结构，并处理 AudioClip 的批量创建与字段编辑。
    /// </summary>
    internal sealed class AudioDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建音频轨道数据处理器。
        /// </summary>
        public AudioDocumentHandler()
            : base(DocumentFieldNames.AudioTracks, DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "音频轨道", true)
        {
        }

        /// <summary>
        /// 校验音频创建请求，并按素材实际时长在一次 Undo 中连续创建全部 Clip。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        public override ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request)
        {
            if (request is not AudioCreateRequest audio)
                return ItemsCreateResult.Failure("音频轨道收到不匹配的创建请求。");
            if (!document.TryFindTrack(this, trackId, out _, out SerializedProperty track, out _))
                return ItemsCreateResult.Failure("音频轨道不存在。");
            if (Document.IsTrackLocked(track)) return ItemsCreateResult.Failure("目标轨道已锁定。");
            if (audio.AudioClips == null || audio.AudioClips.Count == 0)
                return ItemsCreateResult.Failure("没有可创建的 AudioClip。");
            if (float.IsNaN(audio.Pitch) || float.IsInfinity(audio.Pitch) || audio.Pitch < 0.01f)
                return ItemsCreateResult.Failure("音频 Pitch 必须大于或等于 0.01。");
            if (float.IsNaN(audio.Volume) || float.IsInfinity(audio.Volume) ||
                audio.Volume < 0f || audio.Volume > 1f)
                return ItemsCreateResult.Failure("音频音量必须位于 0 到 1 之间。");

            int frameRate = document.CurrentConfig.FrameRate;
            int[] durations = new int[audio.AudioClips.Count];
            long total = 0;
            for (int index = 0; index < audio.AudioClips.Count; index++)
            {
                AudioClip clip = audio.AudioClips[index];
                if (clip == null || !EditorUtility.IsPersistent(clip))
                    return ItemsCreateResult.Failure("音频轨道只接受 Project 中的 AudioClip。");
                double rawDuration = clip.length * frameRate / audio.Pitch;
                if (double.IsNaN(rawDuration) || double.IsInfinity(rawDuration) || rawDuration > int.MaxValue)
                    return ItemsCreateResult.Failure("音频素材的持续帧超出有效范围。");
                durations[index] = Math.Max(1, (int)Math.Ceiling(rawDuration));
                total += durations[index];
            }

            int startFrame = Mathf.Max(0, audio.StartFrame);
            if (total > int.MaxValue - (long)startFrame)
                return ItemsCreateResult.Failure("拖入音频的总持续帧超出范围。");
            if (!document.CanPlaceInterval(this, track, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨音频片段重叠。");

            string[] itemIds = Document.CreateItemIds(audio.AudioClips.Count);
            document.Mutate("拖入音频素材", () =>
            {
                SerializedProperty items = Document.GetItemsProperty(this, track);
                int nextFrame = startFrame;
                for (int index = 0; index < audio.AudioClips.Count; index++)
                {
                    SerializedProperty item = document.AppendItem(this, items,
                        itemIds[index], nextFrame, durations[index]);
                    item.FindPropertyRelative(DocumentFieldNames.AudioClip).objectReferenceValue = audio.AudioClips[index];
                    item.FindPropertyRelative(DocumentFieldNames.Volume).floatValue = audio.Volume;
                    item.FindPropertyRelative(DocumentFieldNames.Pitch).floatValue = audio.Pitch;
                    document.ExpandDurationForItem(this, item);
                    nextFrame += durations[index];
                }
                Document.SortItems(this, items);
            });
            return ItemsCreateResult.Success(itemIds);
        }

        /// <summary>
        /// 校验并提交音频素材、半开帧区间、音量和 Pitch；Pitch 不自动改写持续帧。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not AudioEditRequest audio)
                return EditResult.Failure("音频轨道收到不匹配的编辑请求。");
            if (float.IsNaN(audio.Pitch) || float.IsInfinity(audio.Pitch) || audio.Pitch < 0.01f)
                return EditResult.Failure("音频 Pitch 必须大于或等于 0.01。");
            if (float.IsNaN(audio.Volume) || float.IsInfinity(audio.Volume) ||
                audio.Volume < 0f || audio.Volume > 1f)
                return EditResult.Failure("音频音量必须位于 0 到 1 之间。");
            if (audio.AudioClip != null && !EditorUtility.IsPersistent(audio.AudioClip))
                return EditResult.Failure("音频轨道只接受 Project 中的 AudioClip。");

            return document.EditClip(this, trackId, itemId, audio.StartFrame,
                audio.DurationFrames, "修改音频 Clip", item =>
                {
                    item.FindPropertyRelative(DocumentFieldNames.AudioClip).objectReferenceValue = audio.AudioClip;
                    item.FindPropertyRelative(DocumentFieldNames.Volume).floatValue = audio.Volume;
                    item.FindPropertyRelative(DocumentFieldNames.Pitch).floatValue = audio.Pitch;
                });
        }

        // 初始化 Audio Clip 的素材、音量和 Pitch 默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.AudioClip).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.Volume).floatValue = 1f;
            item.FindPropertyRelative(DocumentFieldNames.Pitch).floatValue = 1f;
        }
    }
    /// <summary>
    /// 定义事件轨道序列化结构，并处理单帧 Marker 的字段编辑。
    /// </summary>
    internal sealed class EventDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建事件轨道数据处理器。
        /// </summary>
        public EventDocumentHandler()
            : base(DocumentFieldNames.EventTracks, DocumentFieldNames.Markers, DocumentFieldNames.Frame,
                string.Empty, "事件轨道", false)
        {
        }

        /// <summary>
        /// Event 轨道当前不支持从 Project 素材批量创建内容。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="request">与当前 Handler 匹配的类型化创建请求。</param>
        public override ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request) =>
            ItemsCreateResult.Failure("事件轨道不支持 Project 素材拖入。");

        /// <summary>
        /// 修改事件 Marker 的帧、类型名、显示名和参数文本。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID，不是轨道数组索引或显示名称。</param>
        /// <param name="itemId">目标 Clip 或 Marker 自身的稳定 GUID，不是内容数组索引。</param>
        /// <param name="request">与当前 Handler 匹配的类型化编辑请求。</param>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not EventEditRequest marker)
                return EditResult.Failure("事件轨道收到不匹配的编辑请求。");
            if (!document.TryFindItem(this, trackId, itemId, out _, out SerializedProperty items,
                    out SerializedProperty item, out _))
                return EditResult.Failure("事件 Marker 不存在。");
            document.Mutate("修改事件 Marker", () =>
            {
                Document.SetItemFrame(this, item, Mathf.Max(0, marker.Frame), 1);
                item.FindPropertyRelative(DocumentFieldNames.EventTypeName).stringValue = marker.EventTypeName ?? string.Empty;
                item.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = string.IsNullOrWhiteSpace(marker.DisplayName)
                    ? "事件"
                    : marker.DisplayName.Trim();
                item.FindPropertyRelative(DocumentFieldNames.ParameterText).stringValue = marker.ParameterText ?? string.Empty;
                document.ExpandDurationForItem(this, item);
                Document.SortItems(this, items);
            }, itemId, false);
            return EditResult.Success();
        }

        // 初始化事件 Marker 的类型名、显示名和参数文本默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.EventTypeName).stringValue = string.Empty;
            item.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = "事件";
            item.FindPropertyRelative(DocumentFieldNames.ParameterText).stringValue = string.Empty;
        }
    }
}
#endif