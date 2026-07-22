#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 同步构建固定标签、Lane 背景和 Item 三层行集合，并维护分组折叠与选中表现。
    /// </summary>
    internal sealed class RowCollectionView
    {
        #region 依赖、状态与事件

        private const string CollapseKeyPrefix = "RPG.SkillTimeline.GroupCollapsed.";
        private readonly VisualElement headerRows;
        private readonly VisualElement laneBackgroundRows;
        private readonly VisualElement laneItemRows;

        private readonly ElementFactory elementFactory;
        private readonly CoordinateMapper mapper;
        private readonly TrackModuleRegistry modules;
        private readonly ItemDragController dragController;
        private readonly TrackDragController trackDragController;

        // 自己的数据
        private readonly List<ItemView> itemViews = new();
        //
        private readonly List<RowSelectionBinding> rowSelections = new();
        // 折叠状态仅按分组类型保存，避免在不同技能间切换时丢失折叠意图。
        private readonly Dictionary<Type, bool> collapsedGroups = new();
        private EditorViewModel viewModel;
        private IReadOnlyList<GroupViewData> groups = Array.Empty<GroupViewData>();

        /// <summary>
        /// 动态行数量、顺序或折叠投影完成重建时触发，供 CanvasController 重新计算内容高度。
        /// </summary>
        public event Action RowsChanged;

        #endregion

        #region 生命周期与刷新
        /// <summary>
        /// 创建三层轨道行集合视图。
        /// </summary>
        public RowCollectionView(VisualElement headerRows, VisualElement laneBackgroundRows,
            VisualElement laneItemRows, ElementFactory elementFactory, CoordinateMapper mapper,
            TrackModuleRegistry modules, ItemDragController dragController,
            TrackDragController trackDragController)
        {
            this.headerRows = headerRows;
            this.laneBackgroundRows = laneBackgroundRows;
            this.laneItemRows = laneItemRows;
            this.elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.dragController = dragController;
            this.trackDragController = trackDragController;
        }

        /// <summary>
        /// 绑定 ViewModel，后续所有动态行操作只转发语义意图。
        /// </summary>
        public void Bind(EditorViewModel model) => viewModel = model;

        /// <summary>
        /// 根据当前具体 ViewData 投影重建标签、背景和 Item 三层结构。
        /// </summary>
        public void Rebuild(IReadOnlyList<GroupViewData> nextGroups)
        {
            groups = nextGroups ?? Array.Empty<GroupViewData>();
            dragController.Reset();
            trackDragController.Reset();
            itemViews.Clear();
            rowSelections.Clear();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();

            foreach (GroupViewData group in groups)
            {
                EnsureCollapseState(group);
                AddGroupRow(group);
                if (collapsedGroups[group.GetType()]) continue;
                for (int index = 0; index < group.Tracks.Count; index++)
                    AddTrackRow(group.Tracks[index], index, group.Tracks.Count);
            }
            RefreshSelection();
            RowsChanged?.Invoke();
        }

        /// <summary>
        /// 刷新所有 Clip 与 Marker 的权威帧位置，供缩放变化使用。
        /// </summary>
        public void RefreshItemGeometry()
        {
            foreach (ItemView itemView in itemViews)
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
            foreach (ItemView itemView in itemViews)
                itemView.SetSelected(viewModel.IsSelected(itemView.Track, itemView.Item));
        }

        /// <summary>
        /// 取消交互并清空全部动态行。
        /// </summary>
        public void Unbind()
        {
            dragController.Reset();
            trackDragController.Reset();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();
            itemViews.Clear();
            rowSelections.Clear();
            viewModel = null;
        }

        #endregion

        #region 行构建与交互

        // 为首次出现的具体分组类型恢复折叠 SessionState。
        private void EnsureCollapseState(GroupViewData group)
        {
            Type type = group.GetType();
            if (collapsedGroups.ContainsKey(type)) return;
            string key = CollapseKeyPrefix + group.GetType().FullName;
            collapsedGroups[type] = SessionState.GetBool(key, false);
        }

        // 同时创建分组的左侧标题、右侧背景和透明 Item 行。
        private void AddGroupRow(GroupViewData group)
        {
            VisualElement header = elementFactory.CreateGroupHeader();
            // bind
            Button foldout = header.Q<Button>("FoldoutButton");
            foldout.text = collapsedGroups[group.GetType()] ? "▶" : "▼";
            header.Q<Label>("NameLabel").text = group.DisplayName;
            header.Q<Button>("AddButton").clicked += () => viewModel.AddTrack(group);
            foldout.clicked += () => ToggleGroup(group);
            SelectionState selection = modules.Get(group).Projection.CreateGroupSelection();
            header.RegisterCallback<PointerDownEvent>(_ => viewModel.Select(selection));
            // 关联动态标题元素与选择状态，用于选择刷新。
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
        private void AddTrackRow(TrackViewData track, int index, int count)
        {
            // 标题头
            VisualElement header = elementFactory.CreateTrackHeader();
            // bind
            header.Q<Label>("NameLabel").text = track.DisplayName;
            header.Q<Button>("AddButton").clicked += () => viewModel.AddItem(track);
            Button moveUp = header.Q<Button>("MoveUpButton");
            Button moveDown = header.Q<Button>("MoveDownButton");
            moveUp.SetEnabled(index > 0);
            moveDown.SetEnabled(index < count - 1);
            moveUp.clicked += () => MoveTrack(track, -1);
            moveDown.clicked += () => MoveTrack(track, 1);
            header.Q<Button>("RemoveButton").clicked += () => RemoveTrack(track);
            SelectionState selection = modules.Get(track).Projection.CreateTrackSelection(track.Id);
            header.RegisterCallback<PointerDownEvent>(_ => viewModel.Select(selection));
            rowSelections.Add(new RowSelectionBinding(header, selection));
            headerRows.Add(header);

            // 背景
            VisualElement background = elementFactory.CreateLaneBackground();
            background.EnableInClassList("is-muted", track.Muted);
            background.EnableInClassList("is-locked", track.Locked);
            laneBackgroundRows.Add(background);

            // 具体 Item
            VisualElement itemRow = elementFactory.CreateLaneItemRow();
            itemRow.EnableInClassList("is-muted", track.Muted);
            itemRow.EnableInClassList("is-locked", track.Locked);
            trackDragController.RegisterTrackEvent(track, itemRow);
            // bind
            foreach (ItemViewData item in track.Items)
            {
                ItemView itemView = modules.CreateItemView(track, item, elementFactory, mapper);
                itemRow.Add(itemView.Element);
                itemViews.Add(itemView);
                dragController.Register(itemView);
            }
            laneItemRows.Add(itemRow);
        }

        // 先选择目标轨道，再把重排意图交给 ViewModel。
        private void MoveTrack(TrackViewData track, int offset)
        {
            viewModel.SelectTrack(track);
            viewModel.MoveSelectedTrack(offset);
        }

        // 先选择目标轨道，再把删除意图交给 ViewModel。
        private void RemoveTrack(TrackViewData track)
        {
            viewModel.SelectTrack(track);
            viewModel.RemoveSelectedTrack();
        }

        // 切换具体分组的本地折叠状态并重建三层行结构。
        private void ToggleGroup(GroupViewData group)
        {
            Type type = group.GetType();
            collapsedGroups[type] = !collapsedGroups[type];
            string key = CollapseKeyPrefix + group.GetType().FullName;
            SessionState.SetBool(key, collapsedGroups[type]);
            Rebuild(groups);
        }

        /// <summary>
        /// 关联动态标题元素及其对应的稳定具体 Selection。
        /// </summary>
        private sealed class RowSelectionBinding
        {
            public VisualElement Element { get; }
            public SelectionState Selection { get; }

            /// <summary>
            /// 创建行选择绑定。
            /// </summary>
            public RowSelectionBinding(VisualElement element, SelectionState selection)
            {
                Element = element;
                Selection = selection;
            }
        }

        #endregion
    }
}
#endif
