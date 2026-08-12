#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.SkillSystem;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 使用实际 Track 子资产和 Item Config 同步构建标题、Lane 背景与 Item 三层行。
    /// </summary>
    internal sealed class RowCollectionView
    {
        #region 依赖与状态
        private readonly VisualElement headerRows;
        private readonly VisualElement laneBackgroundRows;
        private readonly VisualElement laneItemRows;
        private readonly ElementFactory elementFactory;
        private readonly CoordinateMapper mapper;
        private readonly TrackModuleRegistry modules;
        private readonly ItemDragController dragController;
        private readonly ItemContextMenuController contextMenuController;
        private readonly TrackContextMenuController trackContextMenuController;
        private readonly TrackDragController trackDragController;
        private readonly TrackReorderDragController trackReorderDragController;
        private readonly List<ItemView> itemViews = new();
        private readonly List<RowSelectionBinding> rowSelections = new();
        private EditorViewModel viewModel;
        public event Action RowsChanged;
        #endregion

        #region 生命周期
        /// <summary>
        /// 创建无 Group 的三层轨道行集合。
        /// </summary>
        public RowCollectionView(VisualElement headerRows, VisualElement laneBackgroundRows,
            VisualElement laneItemRows, ElementFactory elementFactory, CoordinateMapper mapper,
            TrackModuleRegistry modules, ItemDragController dragController,
            ItemContextMenuController contextMenuController,
            TrackContextMenuController trackContextMenuController,
            TrackDragController trackDragController,
            TrackReorderDragController trackReorderDragController)
        {
            this.headerRows = headerRows ?? throw new ArgumentNullException(nameof(headerRows));
            this.laneBackgroundRows = laneBackgroundRows ?? throw new ArgumentNullException(nameof(laneBackgroundRows));
            this.laneItemRows = laneItemRows ?? throw new ArgumentNullException(nameof(laneItemRows));
            this.elementFactory = elementFactory ?? throw new ArgumentNullException(nameof(elementFactory));
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.dragController = dragController ?? throw new ArgumentNullException(nameof(dragController));
            this.contextMenuController = contextMenuController ?? throw new ArgumentNullException(nameof(contextMenuController));
            this.trackContextMenuController = trackContextMenuController ?? throw new ArgumentNullException(nameof(trackContextMenuController));
            this.trackDragController = trackDragController ?? throw new ArgumentNullException(nameof(trackDragController));
            this.trackReorderDragController = trackReorderDragController ?? throw new ArgumentNullException(nameof(trackReorderDragController));
        }

        /// <summary>
        /// 绑定外层 ViewModel。
        /// </summary>
        public void Bind(EditorViewModel model) => viewModel = model;

        /// <summary>
        /// 按 SkillConfig.Tracks 的物理顺序重建全部直接配置行。
        /// </summary>
        public void Rebuild(IReadOnlyList<TrackConfigBase> tracks)
        {
            dragController.Reset();
            contextMenuController.Reset();
            trackDragController.Reset();
            trackContextMenuController.Reset();
            trackReorderDragController.Reset();
            itemViews.Clear();
            rowSelections.Clear();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();
            IReadOnlyList<TrackConfigBase> source = tracks ?? Array.Empty<TrackConfigBase>();
            for (int index = 0; index < source.Count; index++)
                if (source[index] != null) AddTrackRow(source[index], index, source.Count);
            RefreshSelection();
            RowsChanged?.Invoke();
        }

        /// <summary>
        /// 缩放变化后按实际 Config 帧区间刷新 Item 几何。
        /// </summary>
        public void RefreshItemGeometry()
        {
            foreach (ItemView itemView in itemViews)
                itemView.RefreshGeometry(itemView.Item.StartFrame, itemView.Item.DurationFrames);
        }

        /// <summary>
        /// 根据通用 GUID Selection 刷新标题与 Item 选中样式。
        /// </summary>
        public void RefreshSelection()
        {
            if (viewModel == null) return;
            // Track Header 选中样式仅在当前 Selection 是 Track 时生效，Item 选中样式由 ViewModel.IsSelected 决定。
            foreach (RowSelectionBinding binding in rowSelections)
                binding.Element.EnableInClassList("is-selected",
                    binding.Track.Id == viewModel.Selection.TrackId &&
                    viewModel.Selection is TrackSelection);
            // Item 选中样式由 ViewModel.IsSelected 决定。
            foreach (ItemView itemView in itemViews)
                itemView.SetSelected(viewModel.IsSelected(itemView.Track, itemView.Item));
        }

        /// <summary>
        /// 注销交互并清空动态行。
        /// </summary>
        public void Unbind()
        {
            dragController.Reset();
            contextMenuController.Reset();
            trackDragController.Reset();
            trackContextMenuController.Reset();
            trackReorderDragController.Reset();
            headerRows.Clear();
            laneBackgroundRows.Clear();
            laneItemRows.Clear();
            itemViews.Clear();
            rowSelections.Clear();
            viewModel = null;
        }
        #endregion

        #region 行构建
        // 同时创建 Track 标题、背景、Lane 和全部实际 Item View。
        private void AddTrackRow(TrackConfigBase track, int index, int count)
        {
            VisualElement header = elementFactory.CreateTrackHeader();
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
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) viewModel.SelectTrack(track);
            });
            ObjectField trackAssetField = header.Q<ObjectField>("TrackAssetField");
            trackAssetField.objectType = track.GetType();
            trackAssetField.allowSceneObjects = false;
            trackAssetField.SetValueWithoutNotify(track);
            trackAssetField.SetEnabled(false);
            trackAssetField.tooltip = "避免重复引用，文件只允许只读\n" + AssetDatabase.GetAssetPath(track);
            rowSelections.Add(new RowSelectionBinding(header, track));
            trackContextMenuController.Register(track, header);
            headerRows.Add(header);

            VisualElement background = elementFactory.CreateLaneBackground();
            background.EnableInClassList("is-muted", track.Muted);
            background.EnableInClassList("is-locked", track.EditorLocked);
            laneBackgroundRows.Add(background);

            VisualElement itemRow = elementFactory.CreateLaneItemRow();
            itemRow.userData = track;
            itemRow.EnableInClassList("is-muted", track.Muted);
            itemRow.EnableInClassList("is-locked", track.EditorLocked);
            trackDragController.RegisterTrackEvent(track, itemRow);
            dragController.RegisterLane(track, itemRow);
            foreach (TimelineItemConfigBase item in track.Items)
            {
                ItemView itemView = modules.CreateItemView(track, item, elementFactory, mapper);
                itemRow.Add(itemView.Element);
                itemViews.Add(itemView);
                dragController.Register(itemView);
                contextMenuController.Register(itemView);
            }
            laneItemRows.Add(itemRow);
            VisualElement reorderHandle = header.Q<VisualElement>("TrackDragHandle");
            trackReorderDragController.Register(track, header, background, itemRow, reorderHandle);
        }

        // 选择轨道后提交全局单行移动。
        private void MoveTrack(TrackConfigBase track, int offset)
        {
            viewModel.SelectTrack(track);
            viewModel.MoveSelectedTrack(offset);
        }

        // 选择轨道后删除列表引用与子资产。
        private void RemoveTrack(TrackConfigBase track)
        {
            viewModel.SelectTrack(track);
            viewModel.RemoveSelectedTrack();
        }
        #endregion

        /// <summary>
        /// 关联标题元素和实际 Track 子资产。
        /// </summary>
        private sealed class RowSelectionBinding
        {
            public VisualElement Element { get; }
            public TrackConfigBase Track { get; }

            /// <summary>
            /// 创建标题选择绑定。
            /// </summary>
            public RowSelectionBinding(VisualElement element, TrackConfigBase track)
            {
                Element = element;
                Track = track;
            }
        }
    }

    /// <summary>
    /// 管理单条 Track Header 的本地改名草稿，并在回车或失焦时提交一次。
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
