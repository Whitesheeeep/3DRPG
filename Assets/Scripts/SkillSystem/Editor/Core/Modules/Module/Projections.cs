#if UNITY_EDITOR
using System;
using System.Linq;
using WS_Modules.MVVM;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 提供具体轨道投影共有的类型声明、选择创建和稳定 GUID 查找流程。
    /// </summary>
    internal abstract class TrackProjection<TGroup, TTrack, TItem, TGroupSelection, TTrackSelection, TItemSelection>
        : ITrackProjection
        where TGroup : GroupViewData
        where TTrack : TrackViewData
        where TItem : ItemViewData
        where TGroupSelection : SelectionState
        where TTrackSelection : TrackSelection
        where TItemSelection : ItemSelection
    {
        public Type GroupType => typeof(TGroup);
        public Type TrackType => typeof(TTrack);
        public Type ItemType => typeof(TItem);
        public Type GroupSelectionType => typeof(TGroupSelection);
        public Type TrackSelectionType => typeof(TTrackSelection);
        public Type ItemSelectionType => typeof(TItemSelection);

        /// <summary>
        /// 从当前技能配置创建一种具体轨道分组投影。
        /// </summary>
        public abstract GroupViewData CreateGroup(SkillConfig config);

        /// <summary>
        /// 创建该模块的分组选择。
        /// </summary>
        public abstract SelectionState CreateGroupSelection();

        /// <summary>
        /// 使用稳定轨道 GUID 创建该模块的轨道选择。
        /// </summary>
        public abstract SelectionState CreateTrackSelection(string trackId);

        /// <summary>
        /// 使用稳定轨道与内容 GUID 创建该模块的内容选择。
        /// </summary>
        public abstract SelectionState CreateItemSelection(string trackId, string itemId);

        /// <summary>
        /// 保持原内容选择类型，并替换为复制后生成的新内容 GUID。
        /// </summary>
        public SelectionState CloneItemSelection(SelectionState selection, string itemId)
        {
            if (selection?.GetType() != ItemSelectionType)
                throw new ArgumentException("选择不属于当前轨道模块。", nameof(selection));
            return CreateItemSelection(selection.TrackId, itemId);
        }

        /// <summary>
        /// 按具体选择类型和稳定 GUID 在一个分组投影中查找目标 ViewData。
        /// </summary>
        public IViewData FindSelection(GroupViewData group, SelectionState selection)
        {
            if (group?.GetType() != GroupType || selection == null) return null;
            if (selection.GetType() == GroupSelectionType) return group;

            TrackViewData track = group.Tracks.FirstOrDefault(candidate =>
                candidate.GetType() == TrackType && candidate.Id == selection.TrackId);
            if (selection.GetType() == TrackSelectionType) return track;
            if (track == null || selection.GetType() != ItemSelectionType) return null;
            return track.Items.FirstOrDefault(candidate =>
                candidate.GetType() == ItemType && candidate.Id == selection.ItemId);
        }
    }

    /// <summary>
    /// 把动画轨道及其 Clip 投影为动画编辑器显示类型。
    /// </summary>
    internal sealed class AnimationProjection :
        TrackProjection<AnimationGroupViewData, AnimationTrackViewData, AnimationClipViewData,
            AnimationGroupSelection, AnimationTrackSelection, AnimationClipSelection>
    {
        /// <summary>
        /// 创建动画分组及全部动画轨道投影；空 Config 返回空分组。
        /// </summary>
        public override GroupViewData CreateGroup(SkillConfig config)
        {
            TrackViewData[] tracks = config?.AnimationTracks.Select(track =>
            {
                ItemViewData[] items = track.Clips.Select(clip => (ItemViewData)new AnimationClipViewData(
                    clip, clip.AnimationClip != null ? clip.AnimationClip.name : "Animation Clip")).ToArray();
                return (TrackViewData)new AnimationTrackViewData(track, items);
            }).ToArray() ?? Array.Empty<TrackViewData>();
            return new AnimationGroupViewData(tracks);
        }

        /// <summary>
        /// 创建动画分组选择。
        /// </summary>
        public override SelectionState CreateGroupSelection() => new AnimationGroupSelection();

        /// <summary>
        /// 创建动画轨道选择。
        /// </summary>
        public override SelectionState CreateTrackSelection(string trackId) => new AnimationTrackSelection(trackId);

        /// <summary>
        /// 创建动画 Clip 选择。
        /// </summary>
        public override SelectionState CreateItemSelection(string trackId, string itemId) =>
            new AnimationClipSelection(trackId, itemId);
    }

    /// <summary>
    /// 把攻击检测轨道及其 Clip 投影为攻击检测编辑器显示类型。
    /// </summary>
    internal sealed class AttackDetectionProjection :
        TrackProjection<AttackDetectionGroupViewData, AttackDetectionTrackViewData, AttackDetectionClipViewData,
            AttackDetectionGroupSelection, AttackDetectionTrackSelection, AttackDetectionClipSelection>
    {
        /// <summary>
        /// 创建攻击检测分组及全部轨道投影；空 Config 返回空分组。
        /// </summary>
        public override GroupViewData CreateGroup(SkillConfig config)
        {
            TrackViewData[] tracks = config?.AttackDetectionTracks.Select(track =>
            {
                ItemViewData[] items = track.Clips.Select(clip =>
                    (ItemViewData)new AttackDetectionClipViewData(
                        clip, $"{clip.DetectionType} Detection")).ToArray();
                return (TrackViewData)new AttackDetectionTrackViewData(track, items);
            }).ToArray() ?? Array.Empty<TrackViewData>();
            return new AttackDetectionGroupViewData(tracks);
        }

        /// <summary>
        /// 创建攻击检测分组选择。
        /// </summary>
        public override SelectionState CreateGroupSelection() => new AttackDetectionGroupSelection();

        /// <summary>
        /// 创建攻击检测轨道选择。
        /// </summary>
        public override SelectionState CreateTrackSelection(string trackId) =>
            new AttackDetectionTrackSelection(trackId);

        /// <summary>
        /// 创建攻击检测 Clip 选择。
        /// </summary>
        public override SelectionState CreateItemSelection(string trackId, string itemId) =>
            new AttackDetectionClipSelection(trackId, itemId);
    }

    /// <summary>
    /// 把特效轨道及其 Clip 投影为特效编辑器显示类型。
    /// </summary>
    internal sealed class VfxProjection :
        TrackProjection<VfxGroupViewData, VfxTrackViewData, VfxClipViewData,
            VfxGroupSelection, VfxTrackSelection, VfxClipSelection>
    {
        /// <summary>
        /// 创建特效分组及全部特效轨道投影；空 Config 返回空分组。
        /// </summary>
        public override GroupViewData CreateGroup(SkillConfig config)
        {
            TrackViewData[] tracks = config?.VfxTracks.Select(track =>
            {
                ItemViewData[] items = track.Clips.Select(clip => (ItemViewData)new VfxClipViewData(
                    clip, clip.Prefab != null ? clip.Prefab.name : "VFX Clip")).ToArray();
                return (TrackViewData)new VfxTrackViewData(track, items);
            }).ToArray() ?? Array.Empty<TrackViewData>();
            return new VfxGroupViewData(tracks);
        }

        /// <summary>
        /// 创建特效分组选择。
        /// </summary>
        public override SelectionState CreateGroupSelection() => new VfxGroupSelection();

        /// <summary>
        /// 创建特效轨道选择。
        /// </summary>
        public override SelectionState CreateTrackSelection(string trackId) => new VfxTrackSelection(trackId);

        /// <summary>
        /// 创建特效 Clip 选择。
        /// </summary>
        public override SelectionState CreateItemSelection(string trackId, string itemId) =>
            new VfxClipSelection(trackId, itemId);
    }

    /// <summary>
    /// 把音频轨道及其 Clip 投影为音频编辑器显示类型。
    /// </summary>
    internal sealed class AudioProjection :
        TrackProjection<AudioGroupViewData, AudioTrackViewData, AudioClipViewData,
            AudioGroupSelection, AudioTrackSelection, AudioClipSelection>
    {
        /// <summary>
        /// 创建音频分组及全部音频轨道投影；空 Config 返回空分组。
        /// </summary>
        public override GroupViewData CreateGroup(SkillConfig config)
        {
            TrackViewData[] tracks = config?.AudioTracks.Select(track =>
            {
                ItemViewData[] items = track.Clips.Select(clip => (ItemViewData)new AudioClipViewData(
                    clip, clip.AudioClip != null ? clip.AudioClip.name : "Audio Clip")).ToArray();
                return (TrackViewData)new AudioTrackViewData(track, items);
            }).ToArray() ?? Array.Empty<TrackViewData>();
            return new AudioGroupViewData(tracks);
        }

        /// <summary>
        /// 创建音频分组选择。
        /// </summary>
        public override SelectionState CreateGroupSelection() => new AudioGroupSelection();

        /// <summary>
        /// 创建音频轨道选择。
        /// </summary>
        public override SelectionState CreateTrackSelection(string trackId) => new AudioTrackSelection(trackId);

        /// <summary>
        /// 创建音频 Clip 选择。
        /// </summary>
        public override SelectionState CreateItemSelection(string trackId, string itemId) =>
            new AudioClipSelection(trackId, itemId);
    }
    /// <summary>
    /// 把事件轨道及其 Marker 投影为事件编辑器显示类型。
    /// </summary>
    internal sealed class EventProjection :
        TrackProjection<EventGroupViewData, EventTrackViewData, EventMarkerViewData,
            EventGroupSelection, EventTrackSelection, EventMarkerSelection>
    {
        /// <summary>
        /// 创建事件分组及全部事件轨道投影；空 Config 返回空分组。
        /// </summary>
        public override GroupViewData CreateGroup(SkillConfig config)
        {
            TrackViewData[] tracks = config?.EventTracks.Select(track =>
            {
                ItemViewData[] items = track.Markers.Select(marker =>
                    (ItemViewData)new EventMarkerViewData(marker)).ToArray();
                return (TrackViewData)new EventTrackViewData(track, items);
            }).ToArray() ?? Array.Empty<TrackViewData>();
            return new EventGroupViewData(tracks);
        }

        /// <summary>
        /// 创建事件分组选择。
        /// </summary>
        public override SelectionState CreateGroupSelection() => new EventGroupSelection();

        /// <summary>
        /// 创建事件轨道选择。
        /// </summary>
        public override SelectionState CreateTrackSelection(string trackId) => new EventTrackSelection(trackId);

        /// <summary>
        /// 创建事件 Marker 选择。
        /// </summary>
        public override SelectionState CreateItemSelection(string trackId, string itemId) =>
            new EventMarkerSelection(trackId, itemId);
    }
}
#endif
