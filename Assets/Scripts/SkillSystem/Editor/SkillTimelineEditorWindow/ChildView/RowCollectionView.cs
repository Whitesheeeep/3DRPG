#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
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
            Label name = header.Q<Label>("NameLabel");
            name.text = group.DisplayName;
            name.tooltip = group.DisplayName;
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
            // 标题名称编辑只保留本地草稿，最终文本才作为语义命令提交。
            _ = new TrackHeaderNameView(header, headerRows, track.DisplayName,
                () => viewModel?.SelectTrack(track),
                displayName => viewModel?.RenameTrack(track, displayName));
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

    /// <summary>
    /// 管理单个轨道标题名称的显示态、内联编辑草稿和最终提交，不直接修改技能资产。
    /// </summary>
    internal sealed class TrackHeaderNameView
    {
        #region 常量与字段

        private const string EditingClassName = "is-renaming";

        private readonly VisualElement root;
        private readonly VisualElement scheduleHost;
        private readonly Label nameLabel;
        private readonly TextField nameEditor;
        private readonly Action beginEdit;
        private readonly Action<string> commit;
        private string draftName;
        private bool isEditing;
        private bool isCompleting;
        private bool isDetached;

        #endregion

        #region 生命周期

        // 绑定当前动态行中的名称控件；元素销毁后回调会随 VisualElement 子树一起释放。
        internal TrackHeaderNameView(VisualElement root, VisualElement scheduleHost, string displayName,
            Action beginEdit, Action<string> commit)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.scheduleHost = scheduleHost ?? throw new ArgumentNullException(nameof(scheduleHost));
            this.beginEdit = beginEdit ?? throw new ArgumentNullException(nameof(beginEdit));
            this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
            nameLabel = root.Q<Label>("NameLabel") ??
                        throw new InvalidOperationException("轨道标题模板缺少 NameLabel。");
            nameEditor = root.Q<TextField>("NameEditor") ??
                         throw new InvalidOperationException("轨道标题模板缺少 NameEditor。");
            draftName = displayName ?? string.Empty;
            nameLabel.text = draftName;
            nameLabel.tooltip = nameLabel.text;
            nameEditor.tooltip = nameLabel.text;
            nameLabel.RegisterCallback<PointerDownEvent>(OnNamePointerDown);
            nameEditor.RegisterValueChangedCallback(OnEditorValueChanged);
            nameEditor.RegisterCallback<KeyDownEvent>(OnEditorKeyDown);
            nameEditor.RegisterCallback<FocusOutEvent>(OnEditorFocusOut);
            root.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region 输入处理

        // 仅响应鼠标左键双击；第一次点击仍由标题行负责选中轨道。
        private void OnNamePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2 || isEditing) return;
            beginEdit();
            isDetached = false;
            isEditing = true;
            isCompleting = false;
            root.AddToClassList(EditingClassName);
            draftName = nameLabel.text;
            nameEditor.SetValueWithoutNotify(draftName);
            nameEditor.tooltip = nameLabel.text;
            nameEditor.Focus();
            nameEditor.schedule.Execute(nameEditor.SelectAll);
            evt.StopImmediatePropagation();
        }

        // 输入过程中只同步当前行的本地草稿，不向 ViewModel 发送修改命令。
        private void OnEditorValueChanged(ChangeEvent<string> evt)
        {
            if (isEditing && !isCompleting) draftName = evt.newValue ?? string.Empty;
        }

        // Enter 提交最终草稿，Escape 恢复权威显示值且不创建 Undo。
        private void OnEditorKeyDown(KeyDownEvent evt)
        {
            if (!isEditing) return;
            if (evt.keyCode == KeyCode.Escape)
            {
                CompleteEditing(false, false);
                evt.PreventDefault();
                evt.StopImmediatePropagation();
                return;
            }

            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            CompleteEditing(true, false);
            evt.PreventDefault();
            evt.StopImmediatePropagation();
        }

        // 鼠标转移焦点时提交一次；Enter 导致的后续失焦会被状态保护过滤。
        private void OnEditorFocusOut(FocusOutEvent _)
        {
            if (isEditing && !isCompleting) CompleteEditing(true, true);
        }

        // 行被 Timeline 重建或窗口关闭移除时，取消本地草稿及尚未执行的失焦提交。
        private void OnDetachFromPanel(DetachFromPanelEvent _)
        {
            isDetached = true;
            isEditing = false;
            isCompleting = true;
        }

        #endregion

        #region 编辑完成

        // 统一完成编辑；提交前先退出本地状态，避免同步 Timeline 重建触发重复 FocusOut。
        private void CompleteEditing(bool shouldCommit, bool deferCommit)
        {
            if (!isEditing || isCompleting) return;
            isCompleting = true;
            isEditing = false;
            root.RemoveFromClassList(EditingClassName);
            if (!shouldCommit)
            {
                draftName = nameLabel.text;
                nameEditor.SetValueWithoutNotify(draftName);
                nameEditor.Blur();
                isCompleting = false;
                return;
            }

            string finalName = draftName;
            if (!deferCommit)
            {
                commit(finalName);
                return;
            }

            // 失焦提交延迟到当前 Pointer 事件结束；期间若行被移除则按外部刷新取消草稿。
            scheduleHost.schedule.Execute(() =>
            {
                if (isDetached || root.panel == null) return;
                commit(finalName);
            });
        }

        #endregion
    }
}
#endif
