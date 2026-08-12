#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 定义摄像机修饰轨道的区间、多态数据编辑和深复制规则。
    /// </summary>
    internal sealed class CameraModifierDocumentHandler : TrackDocumentHandler
    {
        /// <summary>创建摄像机修饰数据处理器。</summary>
        internal CameraModifierDocumentHandler() : base(typeof(CameraModifierTrackConfig),
            DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
            DocumentFieldNames.DurationFrames, "摄像机修饰轨道")
        {
        }

        /// <summary>该轨道不接受 Project 素材拖入。</summary>
        public override ItemsCreateResult CreateItems(Document document, string trackId,
            IItemCreateRequest request) => ItemsCreateResult.Failure("摄像机修饰轨道不支持 Project 素材拖入。");

        /// <summary>提交区间和独立 managed reference 快照。</summary>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not CameraModifierEditRequest modifier)
                return EditResult.Failure("摄像机修饰轨道收到不匹配的编辑请求。");
            return document.EditClip(this, trackId, itemId, modifier.StartFrame,
                modifier.DurationFrames, "修改摄像机修饰 Clip", item =>
                    item.FindPropertyRelative(DocumentFieldNames.ModifierData).managedReferenceValue =
                        CameraModifierDataBase.Copy(modifier.ModifierData));
        }

        /// <summary>深复制多态配置，避免复制后的 Clip 共享曲线对象。</summary>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination) =>
            destination.FindPropertyRelative(DocumentFieldNames.ModifierData).managedReferenceValue =
                CameraModifierDataBase.Copy(
                    source.FindPropertyRelative(DocumentFieldNames.ModifierData).managedReferenceValue
                        as CameraModifierDataBase);

        /// <summary>新建 Clip 默认使用 FOV 修饰。</summary>
        protected override void InitializeSpecificFields(SerializedProperty item) =>
            item.FindPropertyRelative(DocumentFieldNames.ModifierData).managedReferenceValue =
                CameraModifierDataBase.Create(CameraModifierType.Fov);
    }

    /// <summary>
    /// 提供轨道序列化结构与公共 Item 初始化流程，具体字段和业务编辑由派生 Handler 完成。
    /// </summary>
    internal abstract class TrackDocumentHandler : ITrackDocumentHandler
    {
        public Type TrackType { get; }
        public string ItemsPropertyName { get; }
        public string StartFramePropertyName { get; }
        public string DurationPropertyName { get; }
        public string DefaultTrackNamePrefix { get; }
        public bool SupportsResize => !string.IsNullOrEmpty(DurationPropertyName);

        // 保存不可变序列化结构，不缓存会在 Undo 或 Apply 后失效的 SerializedProperty。
        protected TrackDocumentHandler(Type trackType, string itemsPropertyName,
            string startFramePropertyName, string durationPropertyName,
            string defaultTrackNamePrefix)
        {
            TrackType = trackType;
            ItemsPropertyName = itemsPropertyName;
            StartFramePropertyName = startFramePropertyName;
            DurationPropertyName = durationPropertyName ?? string.Empty;
            DefaultTrackNamePrefix = defaultTrackNamePrefix;
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

        /// <summary>
        /// 复制一种 Item 的全部类型专用字段；公共 GUID 与帧区间由 Document 统一复制。
        /// </summary>
        /// <param name="source">保持不变的权威源 Item。</param>
        /// <param name="destination">已经初始化公共字段的目标 Item。</param>
        public abstract void CopySpecificFields(SerializedProperty source, SerializedProperty destination);

        /// <summary>
        /// 默认没有额外帧字段需要重采样；具体 Handler 可覆盖此钩子。
        /// </summary>
        public virtual void ResampleSpecificFrameFields(SerializedProperty item,
            int oldFrameRate, int newFrameRate)
        {
        }

        // 初始化某种 Item 独有字段，调用时公共帧字段已经有效。
        protected abstract void InitializeSpecificFields(SerializedProperty item);
    }

    /// <summary>
    /// 定义动作阶段轨道序列化结构，并处理阶段区间与打断设置。
    /// </summary>
    internal sealed class ActionPhaseDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建动作阶段轨道数据处理器。
        /// </summary>
        public ActionPhaseDocumentHandler()
            : base(typeof(ActionPhaseTrackConfig), DocumentFieldNames.Clips,
                DocumentFieldNames.StartFrame, DocumentFieldNames.DurationFrames, "动作阶段轨道")
        {
        }

        /// <summary>
        /// 动作阶段轨道不接受 Project 素材拖入。
        /// </summary>
        /// <param name="document">负责资产事务的 Document。</param>
        /// <param name="trackId">目标轨道稳定 GUID。</param>
        /// <param name="request">当前轨道不支持的素材创建请求。</param>
        /// <returns>始终返回不支持素材拖入的失败结果。</returns>
        public override ItemsCreateResult CreateItems(Document document, string trackId, IItemCreateRequest request) =>
            ItemsCreateResult.Failure("动作阶段轨道不支持从 Project 素材创建内容。");

        /// <summary>
        /// 校验动作阶段编辑请求并提交半开帧区间、阶段类型和打断设置。
        /// </summary>
        /// <param name="document">负责区间校验、Undo 和资产写入的 Document。</param>
        /// <param name="trackId">目标轨道稳定 GUID。</param>
        /// <param name="itemId">目标动作阶段 Item 稳定 GUID。</param>
        /// <param name="request">动作阶段完整编辑请求。</param>
        /// <returns>提交结果及可能的区间冲突原因。</returns>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not ActionPhaseEditRequest actionPhase)
                return EditResult.Failure("动作阶段轨道收到不匹配的编辑请求。");
            return document.EditClip(this, trackId, itemId, actionPhase.StartFrame,
                actionPhase.DurationFrames, "修改动作阶段", item =>
                {
                    item.FindPropertyRelative(DocumentFieldNames.ActionPhase).enumValueIndex =
                        (int)actionPhase.Phase;
                    item.FindPropertyRelative(DocumentFieldNames.CanBeInterrupted).boolValue =
                        actionPhase.CanBeInterrupted;
                });
        }

        /// <summary>
        /// 复制动作阶段类型与打断设置，供复制操作共用。
        /// </summary>
        /// <param name="source">保持不变的源 Item。</param>
        /// <param name="destination">接收类型专用字段的新 Item。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative(DocumentFieldNames.ActionPhase).enumValueIndex =
                source.FindPropertyRelative(DocumentFieldNames.ActionPhase).enumValueIndex;
            destination.FindPropertyRelative(DocumentFieldNames.CanBeInterrupted).boolValue =
                source.FindPropertyRelative(DocumentFieldNames.CanBeInterrupted).boolValue;
        }

        // 新建动作阶段默认为一帧前摇，并且不允许被外部逻辑打断。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.ActionPhase).enumValueIndex =
                (int)ActionPhaseType.Startup;
            item.FindPropertyRelative(DocumentFieldNames.CanBeInterrupted).boolValue = false;
        }
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
            : base(typeof(AnimationTrackConfig), DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "动画轨道")
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
            if (!document.TryFindTrack(this, trackId, out TrackConfigBase track, out SerializedObject trackObject, out SerializedProperty items))
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
            if (!document.CanPlaceInterval(this, items, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨动画片段重叠。");

            string[] itemIds = Document.CreateItemIds(animation.Clips.Count);
            document.MutateTrack("拖入动画素材", track, trackObject, () =>
            {
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
                    item.FindPropertyRelative(DocumentFieldNames.FadeDuration).floatValue = Mathf.Max(0f, animation.FadeDuration);
                });
        }

        /// <summary>
        /// 复制动画素材、源偏移和播放速度，供复制与跨轨道移动共用。
        /// </summary>
        /// <param name="source">保持不变的源动画 Clip。</param>
        /// <param name="destination">接收动画专用字段的目标 Clip。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue =
                source.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue;
            destination.FindPropertyRelative(DocumentFieldNames.SourceStartFrame).intValue =
                source.FindPropertyRelative(DocumentFieldNames.SourceStartFrame).intValue;
            destination.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue;
            destination.FindPropertyRelative(DocumentFieldNames.FadeDuration).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.FadeDuration).floatValue;
        }

        // 初始化动画 Clip 的素材、源偏移和播放速度默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.AnimationClip).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.SourceStartFrame).intValue = 0;
            item.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue = 1f;
            item.FindPropertyRelative(DocumentFieldNames.FadeDuration).floatValue = 0.1f;
        }
    }

    /// <summary>
    /// 定义攻击检测轨道序列化结构，并处理多态检测配置的编辑、复制与帧率重采样。
    /// </summary>
    internal sealed class AttackDetectionDocumentHandler : TrackDocumentHandler
    {
        /// <summary>
        /// 创建攻击检测轨道数据处理器。
        /// </summary>
        public AttackDetectionDocumentHandler()
            : base(typeof(AttackDetectionTrackConfig), DocumentFieldNames.Clips,
                DocumentFieldNames.StartFrame, DocumentFieldNames.DurationFrames, "攻击检测轨道")
        {
        }

        /// <summary>
        /// 攻击检测轨道不接收 Project 素材，内容由轨道“+”按钮创建。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID。</param>
        /// <param name="request">未受支持的素材创建请求。</param>
        public override ItemsCreateResult CreateItems(Document document, string trackId,
            IItemCreateRequest request) =>
            ItemsCreateResult.Failure("攻击检测轨道不支持 Project 素材拖入。");

        /// <summary>
        /// 提交半开帧区间、采样间隔及独立的多态检测参数。
        /// </summary>
        /// <param name="document">负责 Undo、校验和资产写入的文档。</param>
        /// <param name="trackId">目标轨道头中的稳定 GUID。</param>
        /// <param name="itemId">目标攻击检测 Clip 的稳定 GUID。</param>
        /// <param name="request">与当前 Handler 匹配的完整编辑请求。</param>
        public override EditResult EditItem(Document document, string trackId, string itemId,
            IItemEditRequest request)
        {
            if (request is not AttackDetectionEditRequest attack)
                return EditResult.Failure("攻击检测轨道收到不匹配的编辑请求。");

            return document.EditClip(this, trackId, itemId, attack.StartFrame,
                attack.DurationFrames, "修改攻击检测 Clip", item =>
                {
                    item.FindPropertyRelative(DocumentFieldNames.SampleIntervalFrames).intValue =
                        Mathf.Max(1, attack.SampleIntervalFrames);
                    item.FindPropertyRelative(DocumentFieldNames.DetectionData).managedReferenceValue =
                        AttackDetectionDataBase.Copy(attack.DetectionData);
                });
        }

        /// <summary>
        /// 复制 managed reference 为独立实例，避免副本与源 Clip 共享检测参数对象。
        /// </summary>
        /// <param name="source">复制后的权威源 Item。</param>
        /// <param name="destination">需要写入独立 managed reference 的新 Item。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative(DocumentFieldNames.SampleIntervalFrames).intValue =
                source.FindPropertyRelative(DocumentFieldNames.SampleIntervalFrames).intValue;
            AttackDetectionDataBase sourceData = source
                .FindPropertyRelative(DocumentFieldNames.DetectionData).managedReferenceValue
                as AttackDetectionDataBase;
            destination.FindPropertyRelative(DocumentFieldNames.DetectionData).managedReferenceValue =
                AttackDetectionDataBase.Copy(sourceData);
        }

        /// <summary>
        /// 按实际时间重采样检测间隔，并保证至少每帧采样一次。
        /// </summary>
        /// <param name="item">正在重采样的攻击检测 Clip。</param>
        /// <param name="oldFrameRate">修改前 FPS。</param>
        /// <param name="newFrameRate">修改后 FPS。</param>
        public override void ResampleSpecificFrameFields(SerializedProperty item,
            int oldFrameRate, int newFrameRate)
        {
            SerializedProperty interval = item.FindPropertyRelative(DocumentFieldNames.SampleIntervalFrames);
            interval.intValue = Mathf.Max(1,
                Mathf.RoundToInt(interval.intValue * (float)newFrameRate / oldFrameRate));
        }

        // 新建检测 Clip 默认使用一帧采样间隔和 Box 配置。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.SampleIntervalFrames).intValue = 1;
            item.FindPropertyRelative(DocumentFieldNames.DetectionData).managedReferenceValue =
                AttackDetectionDataBase.Create(AttackDetectionType.Box);
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
            : base(typeof(VfxTrackConfig), DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "特效轨道")
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
            if (!document.TryFindTrack(this, trackId, out TrackConfigBase track, out SerializedObject trackObject, out SerializedProperty items))
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
            if (!document.CanPlaceInterval(this, items, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨特效片段重叠。");

            string[] itemIds = Document.CreateItemIds(vfx.Prefabs.Count);
            document.MutateTrack("拖入特效素材", track, trackObject, () =>
            {
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
        /// 校验特效编辑请求并提交区间、Prefab、MarkerKey、局部变换和播放倍率。
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
                    item.FindPropertyRelative(DocumentFieldNames.MarkerKey).objectReferenceValue = vfx.MarkerKey;
                    item.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value = vfx.LocalPosition;
                    item.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value = vfx.LocalEulerAngles;
                    item.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value = vfx.LocalScale;
                    item.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue =
                        Mathf.Max(0.01f, vfx.PlaybackSpeed);
                    item.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex = (int)vfx.FollowMode;
                    item.FindPropertyRelative(DocumentFieldNames.StopMode).enumValueIndex = (int)vfx.StopMode;
                });
        }

        /// <summary>
        /// 复制特效 Prefab、MarkerKey、局部变换、播放倍率、跟随和结束策略，供复制与跨轨道移动共用。
        /// </summary>
        /// <param name="source">保持不变的源特效 Clip。</param>
        /// <param name="destination">接收特效专用字段的目标 Clip。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue =
                source.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue;
            destination.FindPropertyRelative(DocumentFieldNames.MarkerKey).objectReferenceValue =
                source.FindPropertyRelative(DocumentFieldNames.MarkerKey).objectReferenceValue;
            destination.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value =
                source.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value;
            destination.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value =
                source.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value;
            destination.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value =
                source.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value;
            destination.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue;
            destination.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex =
                source.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex;
            destination.FindPropertyRelative(DocumentFieldNames.StopMode).enumValueIndex =
                source.FindPropertyRelative(DocumentFieldNames.StopMode).enumValueIndex;
        }

        // 初始化特效 Clip 的 Prefab、空 MarkerKey、局部变换、原速播放和结束策略默认值。
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            item.FindPropertyRelative(DocumentFieldNames.Prefab).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.MarkerKey).objectReferenceValue = null;
            item.FindPropertyRelative(DocumentFieldNames.LocalPosition).vector3Value = Vector3.zero;
            item.FindPropertyRelative(DocumentFieldNames.LocalEulerAngles).vector3Value = Vector3.zero;
            item.FindPropertyRelative(DocumentFieldNames.LocalScale).vector3Value = Vector3.one;
            item.FindPropertyRelative(DocumentFieldNames.FollowMode).enumValueIndex = 0;
            item.FindPropertyRelative(DocumentFieldNames.PlaybackSpeed).floatValue = 1f;
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
            : base(typeof(AudioTrackConfig), DocumentFieldNames.Clips, DocumentFieldNames.StartFrame,
                DocumentFieldNames.DurationFrames, "音频轨道")
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
            if (!document.TryFindTrack(this, trackId, out TrackConfigBase track, out SerializedObject trackObject, out SerializedProperty items))
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
            if (!document.CanPlaceInterval(this, items, string.Empty, startFrame, (int)total))
                return ItemsCreateResult.Failure("拖入位置与同轨音频片段重叠。");

            string[] itemIds = Document.CreateItemIds(audio.AudioClips.Count);
            document.MutateTrack("拖入音频素材", track, trackObject, () =>
            {
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

        /// <summary>
        /// 复制音频素材、音量和 Pitch，供复制与跨轨道移动共用。
        /// </summary>
        /// <param name="source">保持不变的源音频 Clip。</param>
        /// <param name="destination">接收音频专用字段的目标 Clip。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            destination.FindPropertyRelative(DocumentFieldNames.AudioClip).objectReferenceValue =
                source.FindPropertyRelative(DocumentFieldNames.AudioClip).objectReferenceValue;
            destination.FindPropertyRelative(DocumentFieldNames.Volume).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.Volume).floatValue;
            destination.FindPropertyRelative(DocumentFieldNames.Pitch).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.Pitch).floatValue;
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
            : base(typeof(EventTrackConfig), DocumentFieldNames.Markers, DocumentFieldNames.Frame,
                string.Empty, "事件轨道")
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
        /// 修改事件 Marker 的帧、业务键、显示名和完整类型化参数联合体。
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
            if (marker.ObjectValue != null && !EditorUtility.IsPersistent(marker.ObjectValue))
                return EditResult.Failure("事件对象参数只允许引用 Project 资产。");
            if (!document.TryFindItem(this, trackId, itemId, out TrackConfigBase track,
                    out SerializedObject trackObject, out SerializedProperty items,
                    out SerializedProperty item, out _))
                return EditResult.Failure("事件 Marker 不存在。");
            if (track.EditorLocked) return EditResult.Failure("轨道已锁定。");
            int targetFrame = Mathf.Max(0, marker.Frame);
            if (!document.CanPlaceInterval(this, items, itemId, targetFrame, 1))
                return EditResult.Failure("目标帧已有其他 Event Marker。");
            document.MutateTrack("修改事件 Marker", track, trackObject, () =>
            {
                // 一次事务写入全部候选值，切换 ValueType 时不会清空非活动字段。
                Document.SetItemFrame(this, item, targetFrame, 1);
                item.FindPropertyRelative(DocumentFieldNames.EventKey).stringValue = marker.EventKey ?? string.Empty;
                item.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = string.IsNullOrWhiteSpace(marker.DisplayName)
                    ? "事件"
                    : marker.DisplayName.Trim();
                item.FindPropertyRelative(DocumentFieldNames.EventValueType).enumValueIndex = (int)marker.ValueType;
                item.FindPropertyRelative(DocumentFieldNames.IntValue).intValue = marker.IntValue;
                item.FindPropertyRelative(DocumentFieldNames.StringValue).stringValue = marker.StringValue ?? string.Empty;
                item.FindPropertyRelative(DocumentFieldNames.LongValue).longValue = marker.LongValue;
                item.FindPropertyRelative(DocumentFieldNames.BoolValue).boolValue = marker.BoolValue;
                item.FindPropertyRelative(DocumentFieldNames.DoubleValue).doubleValue = marker.DoubleValue;
                item.FindPropertyRelative(DocumentFieldNames.FloatValue).floatValue = marker.FloatValue;
                item.FindPropertyRelative(DocumentFieldNames.ObjectValue).objectReferenceValue = marker.ObjectValue;
                document.ExpandDurationForItem(this, item);
                Document.SortItems(this, items);
            });
            return EditResult.Success();
        }

        /// <summary>
        /// 复制事件业务信息和全部候选值，供复制与跨轨道移动共用。
        /// </summary>
        /// <param name="source">保持不变的源事件 Marker。</param>
        /// <param name="destination">接收事件专用字段的目标 Marker。</param>
        public override void CopySpecificFields(SerializedProperty source, SerializedProperty destination)
        {
            // 联合体的所有字段都必须复制，不能只复制当前 ValueType 对应的活动值。
            destination.FindPropertyRelative(DocumentFieldNames.EventKey).stringValue =
                source.FindPropertyRelative(DocumentFieldNames.EventKey).stringValue;
            destination.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue =
                source.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue;
            destination.FindPropertyRelative(DocumentFieldNames.EventValueType).enumValueIndex =
                source.FindPropertyRelative(DocumentFieldNames.EventValueType).enumValueIndex;
            destination.FindPropertyRelative(DocumentFieldNames.IntValue).intValue =
                source.FindPropertyRelative(DocumentFieldNames.IntValue).intValue;
            destination.FindPropertyRelative(DocumentFieldNames.StringValue).stringValue =
                source.FindPropertyRelative(DocumentFieldNames.StringValue).stringValue;
            destination.FindPropertyRelative(DocumentFieldNames.LongValue).longValue =
                source.FindPropertyRelative(DocumentFieldNames.LongValue).longValue;
            destination.FindPropertyRelative(DocumentFieldNames.BoolValue).boolValue =
                source.FindPropertyRelative(DocumentFieldNames.BoolValue).boolValue;
            destination.FindPropertyRelative(DocumentFieldNames.DoubleValue).doubleValue =
                source.FindPropertyRelative(DocumentFieldNames.DoubleValue).doubleValue;
            destination.FindPropertyRelative(DocumentFieldNames.FloatValue).floatValue =
                source.FindPropertyRelative(DocumentFieldNames.FloatValue).floatValue;
            destination.FindPropertyRelative(DocumentFieldNames.ObjectValue).objectReferenceValue =
                source.FindPropertyRelative(DocumentFieldNames.ObjectValue).objectReferenceValue;
        }

        /// <summary>
        /// 初始化新事件 Marker 的业务字段，并默认选择字符串参数。
        /// </summary>
        /// <param name="item">待初始化的新 Marker 序列化节点。</param>
        /// <summary>
        /// 初始化动画 Clip 的素材、源偏移、播放速度和淡入时长默认值。
        /// </summary>
        /// <param name="item">刚追加到轨道列表的动画 Clip SerializedProperty。</param>
        protected override void InitializeSpecificFields(SerializedProperty item)
        {
            // 初始化全部候选值，避免新建、复制和 Undo 路径之间出现未定义状态。
            item.FindPropertyRelative(DocumentFieldNames.EventKey).stringValue = string.Empty;
            item.FindPropertyRelative(DocumentFieldNames.DisplayName).stringValue = "事件";
            item.FindPropertyRelative(DocumentFieldNames.EventValueType).enumValueIndex =
                (int)SkillEventValueType.String;
            item.FindPropertyRelative(DocumentFieldNames.IntValue).intValue = 0;
            item.FindPropertyRelative(DocumentFieldNames.StringValue).stringValue = string.Empty;
            item.FindPropertyRelative(DocumentFieldNames.LongValue).longValue = 0L;
            item.FindPropertyRelative(DocumentFieldNames.BoolValue).boolValue = false;
            item.FindPropertyRelative(DocumentFieldNames.DoubleValue).doubleValue = 0d;
            item.FindPropertyRelative(DocumentFieldNames.FloatValue).floatValue = 0f;
            item.FindPropertyRelative(DocumentFieldNames.ObjectValue).objectReferenceValue = null;
        }
    }
}
#endif
