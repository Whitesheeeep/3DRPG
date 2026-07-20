#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 集中创建具体 Group、Track 和 Item 对应的稳定 Selection 类型。
    /// </summary>
    internal static class SkillTimelinePresentationTypeUtility
    {
        /// <summary>
        /// 根据具体 Group ViewData 创建分组选择。
        /// </summary>
        public static SkillTimelineSelection CreateGroupSelection(SkillTimelineGroupViewData group) => group switch
        {
            AnimationTimelineGroupViewData => new AnimationGroupTimelineSelection(),
            VfxTimelineGroupViewData => new VfxGroupTimelineSelection(),
            EventTimelineGroupViewData => new EventGroupTimelineSelection(),
            _ => throw new ArgumentOutOfRangeException(nameof(group))
        };

        /// <summary>
        /// 根据具体 Track ViewData 创建轨道选择。
        /// </summary>
        public static SkillTimelineSelection CreateTrackSelection(SkillTimelineTrackViewData track) => track switch
        {
            AnimationTimelineTrackViewData => new AnimationTrackTimelineSelection(track.Id),
            VfxTimelineTrackViewData => new VfxTrackTimelineSelection(track.Id),
            EventTimelineTrackViewData => new EventTrackTimelineSelection(track.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(track))
        };

        /// <summary>
        /// 根据具体 Item ViewData 创建内容选择。
        /// </summary>
        public static SkillTimelineSelection CreateItemSelection(
            SkillTimelineTrackViewData track, SkillTimelineItemViewData item) => item switch
        {
            AnimationClipTimelineItemViewData => new AnimationClipTimelineSelection(track.Id, item.Id),
            VfxClipTimelineItemViewData => new VfxClipTimelineSelection(track.Id, item.Id),
            EventMarkerTimelineItemViewData => new EventMarkerTimelineSelection(track.Id, item.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(item))
        };

        /// <summary>
        /// 返回具体分组类型对应的 SessionState 折叠键后缀。
        /// </summary>
        public static string GetGroupStateKey(SkillTimelineGroupViewData group) => group.GetType().Name;
    }

    /// <summary>
    /// 同步构建固定标签、Lane 背景和 Item 三层行集合，并维护分组折叠与选中表现。
    /// </summary>
    internal sealed class SkillTimelineRowCollectionView
    {
        private const string CollapseKeyPrefix = "RPG.SkillTimeline.GroupCollapsed.";
        private readonly VisualElement headerRows;
        private readonly VisualElement laneBackgroundRows;
        private readonly VisualElement laneItemRows;
        private readonly SkillTimelineElementFactory elementFactory;
        private readonly SkillTimelineItemDragController dragController;
        private readonly List<SkillTimelineItemView> itemViews = new();
        private readonly List<RowSelectionBinding> rowSelections = new();
        private readonly Dictionary<Type, bool> collapsedGroups = new();
        private SkillTimelineEditorViewModel viewModel;
        private IReadOnlyList<SkillTimelineGroupViewData> groups = Array.Empty<SkillTimelineGroupViewData>();

        /// <summary>
        /// 创建三层轨道行集合视图。
        /// </summary>
        public SkillTimelineRowCollectionView(VisualElement headerRows, VisualElement laneBackgroundRows,
            VisualElement laneItemRows, SkillTimelineElementFactory elementFactory,
            SkillTimelineItemDragController dragController)
        {
            this.headerRows = headerRows;
            this.laneBackgroundRows = laneBackgroundRows;
            this.laneItemRows = laneItemRows;
            this.elementFactory = elementFactory;
            this.dragController = dragController;
        }

        /// <summary>
        /// 绑定 ViewModel，后续所有动态行操作只转发语义意图。
        /// </summary>
        public void Bind(SkillTimelineEditorViewModel model) => viewModel = model;

        /// <summary>
        /// 根据当前具体 ViewData 投影重建标签、背景和 Item 三层结构。
        /// </summary>
        public void Rebuild(IReadOnlyList<SkillTimelineGroupViewData> nextGroups)
        {
            groups = nextGroups ?? Array.Empty<SkillTimelineGroupViewData>();
            dragController.Reset();
            itemViews.Clear();
            rowSelections.Clear();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();

            foreach (SkillTimelineGroupViewData group in groups)
            {
                EnsureCollapseState(group);
                AddGroupRow(group);
                if (collapsedGroups[group.GetType()]) continue;
                for (int index = 0; index < group.Tracks.Count; index++)
                    AddTrackRow(group.Tracks[index], index, group.Tracks.Count);
            }
            RefreshSelection();
        }

        /// <summary>
        /// 刷新所有 Clip 与 Marker 的权威帧位置，供缩放变化使用。
        /// </summary>
        public void RefreshItemGeometry()
        {
            foreach (SkillTimelineItemView itemView in itemViews)
                itemView.RefreshGeometry(itemView.Item.StartFrame, itemView.Item.DurationFrames);
        }

        /// <summary>
        /// 根据 ViewModel 当前具体 Selection 切换行和内容元素的选中 class。
        /// </summary>
        public void RefreshSelection()
        {
            if (viewModel == null) return;
            foreach (RowSelectionBinding binding in rowSelections)
                binding.Element.EnableInClassList("is-selected", binding.Selection.Equals(viewModel.Selection));
            foreach (SkillTimelineItemView itemView in itemViews)
                itemView.SetSelected(SkillTimelinePresentationTypeUtility
                    .CreateItemSelection(itemView.Track, itemView.Item).Equals(viewModel.Selection));
        }

        /// <summary>
        /// 取消交互并清空全部动态行。
        /// </summary>
        public void Unbind()
        {
            dragController.Reset();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();
            itemViews.Clear();
            rowSelections.Clear();
            viewModel = null;
        }

        // 为首次出现的具体分组类型恢复折叠 SessionState。
        private void EnsureCollapseState(SkillTimelineGroupViewData group)
        {
            Type type = group.GetType();
            if (collapsedGroups.ContainsKey(type)) return;
            string key = CollapseKeyPrefix + SkillTimelinePresentationTypeUtility.GetGroupStateKey(group);
            collapsedGroups[type] = SessionState.GetBool(key, false);
        }

        // 同时创建分组的左侧标题、右侧背景和透明 Item 行。
        private void AddGroupRow(SkillTimelineGroupViewData group)
        {
            VisualElement header = elementFactory.CreateGroupHeader();
            Button foldout = header.Q<Button>("FoldoutButton");
            foldout.text = collapsedGroups[group.GetType()] ? "▶" : "▼";
            header.Q<Label>("NameLabel").text = group.DisplayName;
            header.Q<Button>("AddButton").clicked += () => viewModel.AddTrack(group);
            foldout.clicked += () => ToggleGroup(group);
            SkillTimelineSelection selection = SkillTimelinePresentationTypeUtility.CreateGroupSelection(group);
            header.RegisterCallback<PointerDownEvent>(_ => viewModel.Select(selection));
            rowSelections.Add(new RowSelectionBinding(header, selection));
            headerRows.Add(header);

            VisualElement background = elementFactory.CreateLaneBackground();
            background.AddToClassList("timeline-group-row");
            laneBackgroundRows.Add(background);
            VisualElement itemRow = elementFactory.CreateLaneItemRow();
            itemRow.AddToClassList("timeline-group-row");
            laneItemRows.Add(itemRow);
        }

        // 同时创建轨道标题、背景、透明 Item 行以及其中的具体内容视图。
        private void AddTrackRow(SkillTimelineTrackViewData track, int index, int count)
        {
            VisualElement header = elementFactory.CreateTrackHeader();
            header.Q<Label>("NameLabel").text = track.DisplayName;
            header.Q<Button>("AddButton").clicked += () => viewModel.AddItem(track);
            Button moveUp = header.Q<Button>("MoveUpButton");
            Button moveDown = header.Q<Button>("MoveDownButton");
            moveUp.SetEnabled(index > 0);
            moveDown.SetEnabled(index < count - 1);
            moveUp.clicked += () => MoveTrack(track, -1);
            moveDown.clicked += () => MoveTrack(track, 1);
            header.Q<Button>("RemoveButton").clicked += () => RemoveTrack(track);
            SkillTimelineSelection selection = SkillTimelinePresentationTypeUtility.CreateTrackSelection(track);
            header.RegisterCallback<PointerDownEvent>(_ => viewModel.Select(selection));
            rowSelections.Add(new RowSelectionBinding(header, selection));
            headerRows.Add(header);

            VisualElement background = elementFactory.CreateLaneBackground();
            background.EnableInClassList("is-muted", track.Muted);
            background.EnableInClassList("is-locked", track.Locked);
            laneBackgroundRows.Add(background);

            VisualElement itemRow = elementFactory.CreateLaneItemRow();
            itemRow.EnableInClassList("is-muted", track.Muted);
            foreach (SkillTimelineItemViewData item in track.Items)
            {
                SkillTimelineItemView itemView = elementFactory.CreateItemView(track, item);
                itemRow.Add(itemView.Element);
                itemViews.Add(itemView);
                dragController.Register(itemView);
            }
            laneItemRows.Add(itemRow);
        }

        // 先选择目标轨道，再把重排意图交给 ViewModel。
        private void MoveTrack(SkillTimelineTrackViewData track, int offset)
        {
            viewModel.Select(SkillTimelinePresentationTypeUtility.CreateTrackSelection(track));
            viewModel.MoveSelectedTrack(offset);
        }

        // 先选择目标轨道，再把删除意图交给 ViewModel。
        private void RemoveTrack(SkillTimelineTrackViewData track)
        {
            viewModel.Select(SkillTimelinePresentationTypeUtility.CreateTrackSelection(track));
            viewModel.RemoveSelectedTrack();
        }

        // 切换具体分组的本地折叠状态并重建三层行结构。
        private void ToggleGroup(SkillTimelineGroupViewData group)
        {
            Type type = group.GetType();
            collapsedGroups[type] = !collapsedGroups[type];
            string key = CollapseKeyPrefix + SkillTimelinePresentationTypeUtility.GetGroupStateKey(group);
            SessionState.SetBool(key, collapsedGroups[type]);
            Rebuild(groups);
        }

        /// <summary>
        /// 关联动态标题元素及其对应的稳定具体 Selection。
        /// </summary>
        private sealed class RowSelectionBinding
        {
            public VisualElement Element { get; }
            public SkillTimelineSelection Selection { get; }

            /// <summary>
            /// 创建行选择绑定。
            /// </summary>
            public RowSelectionBinding(VisualElement element, SkillTimelineSelection selection)
            {
                Element = element;
                Selection = selection;
            }
        }
    }
}
#endif