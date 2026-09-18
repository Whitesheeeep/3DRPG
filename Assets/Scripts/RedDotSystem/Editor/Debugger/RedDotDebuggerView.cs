#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>渲染红点调试窗口并把用户意图转发给 Controller。</summary>
    internal sealed class RedDotDebuggerView : IDisposable
    {
        #region 依赖字段

        private readonly Label modeLabel;
        private readonly Label connectionLabel;
        private readonly Label runtimeConfigLabel;
        private readonly Label countLabel;
        private readonly Label detailLabel;
        private readonly Label statusLabel;
        private readonly IntegerField overrideValueField;
        private readonly TreeView treeView;
        private readonly VisualElement runtimeControls;
        private readonly VisualElement selectedNodeControls;
        private readonly Button flushButton;
        private readonly Button refreshButton;
        private readonly Button clearAllOverridesButton;
        private readonly Button incrementButton;
        private readonly Button decrementButton;
        private readonly Button setZeroButton;
        private readonly Button setValueButton;
        private readonly Button clearOverrideButton;
        private readonly Button markDirtyButton;
        private readonly Button copyPathButton;
        private readonly Button pingKeyButton;

        #endregion

        #region 状态字段

        // key：RedDotKey Asset；value：当前运行时快照对应的 TreeView 行数据。
        private IReadOnlyDictionary<RedDotKey, RedDotDebuggerNodeViewData> nodeByKeyMap =
            new Dictionary<RedDotKey, RedDotDebuggerNodeViewData>();
        private RedDotDebuggerNodeViewData selectedNode;
        private bool refreshingTree;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求立即刷新脏节点。</summary>
        internal event Action FlushRequested;

        /// <summary>请求重新读取当前快照。</summary>
        internal event Action RefreshRequested;

        /// <summary>请求清除全部临时覆盖。</summary>
        internal event Action ClearAllOverridesRequested;

        /// <summary>请求在当前选中自身值上增加指定差值。</summary>
        internal event Action<int> DeltaRequested;

        /// <summary>请求把选中节点自身覆盖值设为零。</summary>
        internal event Action SetZeroRequested;

        /// <summary>请求把选中节点自身覆盖值设为输入值。</summary>
        internal event Action<int> SetValueRequested;

        /// <summary>请求清除选中节点的临时覆盖。</summary>
        internal event Action ClearOverrideRequested;

        /// <summary>请求标记选中节点。</summary>
        internal event Action MarkDirtyRequested;

        /// <summary>请求复制选中节点派生路径。</summary>
        internal event Action CopyPathRequested;

        /// <summary>请求定位选中节点的 RedDotKey Asset。</summary>
        internal event Action PingKeyRequested;

        #endregion

        #region 生命周期

        /// <summary>查询 UXML 控件并注册界面事件。</summary>
        /// <param name="root">已克隆窗口 UXML 的根节点。</param>
        public RedDotDebuggerView(VisualElement root)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            modeLabel = Require<Label>(root, "ModeLabel");
            connectionLabel = Require<Label>(root, "ConnectionLabel");
            runtimeConfigLabel = Require<Label>(root, "RuntimeConfigLabel");
            countLabel = Require<Label>(root, "CountLabel");
            detailLabel = Require<Label>(root, "DetailLabel");
            statusLabel = Require<Label>(root, "StatusLabel");
            overrideValueField = Require<IntegerField>(root, "OverrideValueField");
            treeView = Require<TreeView>(root, "NodeTreeView");
            runtimeControls = Require<VisualElement>(root, "RuntimeControls");
            selectedNodeControls = Require<VisualElement>(root, "SelectedNodeControls");

            flushButton = Require<Button>(root, "FlushButton");
            refreshButton = Require<Button>(root, "RefreshButton");
            clearAllOverridesButton = Require<Button>(root, "ClearAllOverridesButton");
            incrementButton = Require<Button>(root, "IncrementButton");
            decrementButton = Require<Button>(root, "DecrementButton");
            setZeroButton = Require<Button>(root, "SetZeroButton");
            setValueButton = Require<Button>(root, "SetValueButton");
            clearOverrideButton = Require<Button>(root, "ClearOverrideButton");
            markDirtyButton = Require<Button>(root, "MarkDirtyButton");
            copyPathButton = Require<Button>(root, "CopyKeyButton");
            pingKeyButton = Require<Button>(root, "PingKeyButton");

            treeView.fixedItemHeight = 24f;
            treeView.selectionType = SelectionType.Single;
            treeView.autoExpand = true;
            treeView.makeItem = MakeTreeRow;
            treeView.bindItem = BindTreeRow;
            treeView.selectionChanged += OnTreeSelectionChanged;

            flushButton.clicked += OnFlushClicked;
            refreshButton.clicked += OnRefreshClicked;
            clearAllOverridesButton.clicked += OnClearAllOverridesClicked;
            incrementButton.clicked += OnIncrementClicked;
            decrementButton.clicked += OnDecrementClicked;
            setZeroButton.clicked += OnSetZeroClicked;
            setValueButton.clicked += OnSetValueClicked;
            clearOverrideButton.clicked += OnClearOverrideClicked;
            markDirtyButton.clicked += OnMarkDirtyClicked;
            copyPathButton.clicked += OnCopyPathClicked;
            pingKeyButton.clicked += OnPingKeyClicked;
        }

        /// <summary>释放 TreeView 回调和当前快照引用。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            treeView.selectionChanged -= OnTreeSelectionChanged;
            flushButton.clicked -= OnFlushClicked;
            refreshButton.clicked -= OnRefreshClicked;
            clearAllOverridesButton.clicked -= OnClearAllOverridesClicked;
            incrementButton.clicked -= OnIncrementClicked;
            decrementButton.clicked -= OnDecrementClicked;
            setZeroButton.clicked -= OnSetZeroClicked;
            setValueButton.clicked -= OnSetValueClicked;
            clearOverrideButton.clicked -= OnClearOverrideClicked;
            markDirtyButton.clicked -= OnMarkDirtyClicked;
            copyPathButton.clicked -= OnCopyPathClicked;
            pingKeyButton.clicked -= OnPingKeyClicked;
            refreshingTree = true;
            treeView.SetRootItems(
                new List<TreeViewItemData<RedDotDebuggerNodeViewData>>());
            treeView.Rebuild();
            refreshingTree = false;
            nodeByKeyMap = new Dictionary<RedDotKey, RedDotDebuggerNodeViewData>();
            selectedNode = null;
        }

        #endregion

        #region 状态渲染

        /// <summary>显示当前 Editor 模式和运行时连接状态。</summary>
        /// <param name="isPlaying">当前是否处于 Play Mode。</param>
        /// <param name="isConnected">是否已取得 RedDotSystem。</param>
        internal void SetConnectionState(bool isPlaying, bool isConnected)
        {
            modeLabel.text = isPlaying ? "模式：Play Mode" : "模式：Edit Mode";
            connectionLabel.text = isConnected
                ? "连接：RedDotSystem 已连接"
                : isPlaying
                    ? "连接：等待 GameArchitecture"
                    : "连接：仅 Play Mode 可用";
            runtimeControls.SetEnabled(isConnected);
            UpdateSelectedNodeControls();
        }

        /// <summary>显示当前运行时 RedDotSystem 实际使用的 Config 名称。</summary>
        /// <param name="configName">Config 名称；未连接时传空。</param>
        internal void SetRuntimeConfigName(string configName)
        {
            runtimeConfigLabel.text = string.IsNullOrEmpty(configName)
                ? "运行时 Config：未连接"
                : $"运行时 Config：{configName}";
        }

        /// <summary>渲染一份完整节点快照，并尽量恢复之前选择。</summary>
        /// <param name="snapshots">当前红点树快照。</param>
        internal void Render(IReadOnlyList<DebugRedDotNodeSnapshot> snapshots)
        {
            RedDotKey selectedKey = selectedNode?.Snapshot.Key;
            IList<TreeViewItemData<RedDotDebuggerNodeViewData>> roots =
                RedDotDebuggerNodeViewData.BuildTreeItems(snapshots, out nodeByKeyMap);
            // TreeView 重建期间会触发中间选择回调；先屏蔽回调，再按 RedDotKey Asset 恢复选择。
            refreshingTree = true;
            treeView.SetRootItems(roots);
            treeView.Rebuild();
            treeView.ExpandAll();
            refreshingTree = false;

            int dirtyCount = snapshots.Count(snapshot => snapshot.IsDirty);
            countLabel.text = $"节点：{snapshots.Count}    Dirty：{dirtyCount}";
            if (selectedKey != null &&
                nodeByKeyMap.TryGetValue(selectedKey, out RedDotDebuggerNodeViewData restored))
            {
                treeView.SetSelectionByIdWithoutNotify(new[] { restored.Id });
                SetSelectedNode(restored);
            }
            else
            {
                treeView.ClearSelection();
                SetSelectedNode(null);
            }
        }

        /// <summary>清空运行时树数据和选择详情。</summary>
        internal void ClearRuntimeData()
        {
            refreshingTree = true;
            treeView.SetRootItems(
                new List<TreeViewItemData<RedDotDebuggerNodeViewData>>());
            treeView.Rebuild();
            refreshingTree = false;
            nodeByKeyMap = new Dictionary<RedDotKey, RedDotDebuggerNodeViewData>();
            countLabel.text = "节点：0    Dirty：0";
            SetSelectedNode(null);
        }

        /// <summary>显示最近操作或变化摘要。</summary>
        /// <param name="message">待显示消息。</param>
        /// <param name="isError">是否以错误样式显示。</param>
        internal void ShowStatus(string message, bool isError = false)
        {
            statusLabel.text = message;
            statusLabel.EnableInClassList("status-error", isError);
        }

        /// <summary>获取当前选中节点。</summary>
        /// <returns>当前选中行；没有选择时为空。</returns>
        internal RedDotDebuggerNodeViewData GetSelectedNode() => selectedNode;

        #endregion

        #region TreeView

        /// <summary>创建可复用的红点树行。</summary>
        /// <returns>未绑定数据的行元素。</returns>
        private static VisualElement MakeTreeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("red-dot-tree-row");
            row.Add(new Label { name = "KeyLabel" });
            row.Add(new Label { name = "SelfLabel" });
            row.Add(new Label { name = "OverrideLabel" });
            row.Add(new Label { name = "TotalLabel" });
            row.Add(new Label { name = "DirtyLabel" });
            return row;
        }

        /// <summary>把当前 TreeView 数据绑定到虚拟化行。</summary>
        /// <param name="element">待绑定行。</param>
        /// <param name="index">当前可见数据索引。</param>
        private void BindTreeRow(VisualElement element, int index)
        {
            RedDotDebuggerNodeViewData data =
                treeView.GetItemDataForIndex<RedDotDebuggerNodeViewData>(index);
            DebugRedDotNodeSnapshot snapshot = data.Snapshot;
            element.Q<Label>("KeyLabel").text = snapshot.Key.SegmentName;
            element.Q<Label>("KeyLabel").tooltip = snapshot.DerivedPath;
            element.Q<Label>("SelfLabel").text = $"Self {snapshot.SelfValue}";
            element.Q<Label>("OverrideLabel").text = snapshot.HasDebugOverride
                ? $"Override {snapshot.DebugOverrideValue}"
                : string.Empty;
            element.Q<Label>("TotalLabel").text = $"Total {snapshot.TotalValue}";
            element.Q<Label>("DirtyLabel").text = snapshot.IsDirty ? "Dirty" : string.Empty;
        }

        /// <summary>同步 TreeView 选择和右侧详情。</summary>
        /// <param name="selection">当前选择对象集合。</param>
        private void OnTreeSelectionChanged(IEnumerable<object> selection)
        {
            if (!refreshingTree)
            {
                SetSelectedNode(selection?.OfType<RedDotDebuggerNodeViewData>().FirstOrDefault());
            }
        }

        /// <summary>设置当前选择并刷新节点详情和按钮权限。</summary>
        /// <param name="node">新的选中节点。</param>
        private void SetSelectedNode(RedDotDebuggerNodeViewData node)
        {
            selectedNode = node;
            if (node == null)
            {
                detailLabel.text = "请选择一个节点。";
                overrideValueField.SetValueWithoutNotify(0);
            }
            else
            {
                DebugRedDotNodeSnapshot snapshot = node.Snapshot;
                detailLabel.text =
                    $"路径：{snapshot.DerivedPath}\n" +
                    $"SelfValue：{snapshot.SelfValue}\n" +
                    $"EffectiveSelfValue：{snapshot.EffectiveSelfValue}\n" +
                    $"TotalValue：{snapshot.TotalValue}\n" +
                    $"Override：{(snapshot.HasDebugOverride ? snapshot.DebugOverrideValue.ToString() : "None")}\n" +
                    $"Dirty：{snapshot.IsDirty}    Children：{snapshot.ChildCount}";
                overrideValueField.SetValueWithoutNotify(snapshot.EffectiveSelfValue);
            }

            UpdateSelectedNodeControls();
        }

        /// <summary>根据连接状态和是否选中节点启用调试操作。</summary>
        private void UpdateSelectedNodeControls()
        {
            bool canEditNode = runtimeControls.enabledSelf && selectedNode != null;
            selectedNodeControls.SetEnabled(canEditNode);
            copyPathButton.SetEnabled(runtimeControls.enabledSelf && selectedNode != null);
            pingKeyButton.SetEnabled(runtimeControls.enabledSelf && selectedNode != null);
        }

        #endregion

        #region 按钮事件

        /// <summary>转发立即刷新请求。</summary>
        private void OnFlushClicked() => FlushRequested?.Invoke();

        /// <summary>转发快照刷新请求。</summary>
        private void OnRefreshClicked() => RefreshRequested?.Invoke();

        /// <summary>转发清除全部覆盖请求。</summary>
        private void OnClearAllOverridesClicked() => ClearAllOverridesRequested?.Invoke();

        /// <summary>转发增加一请求。</summary>
        private void OnIncrementClicked() => DeltaRequested?.Invoke(1);

        /// <summary>转发减少一请求。</summary>
        private void OnDecrementClicked() => DeltaRequested?.Invoke(-1);

        /// <summary>转发设为零请求。</summary>
        private void OnSetZeroClicked() => SetZeroRequested?.Invoke();

        /// <summary>转发设置输入值请求。</summary>
        private void OnSetValueClicked() => SetValueRequested?.Invoke(overrideValueField.value);

        /// <summary>转发清除当前覆盖请求。</summary>
        private void OnClearOverrideClicked() => ClearOverrideRequested?.Invoke();

        /// <summary>转发标脏请求。</summary>
        private void OnMarkDirtyClicked() => MarkDirtyRequested?.Invoke();

        /// <summary>转发复制路径请求。</summary>
        private void OnCopyPathClicked() => CopyPathRequested?.Invoke();

        /// <summary>转发定位节点 Asset 请求。</summary>
        private void OnPingKeyClicked() => PingKeyRequested?.Invoke();

        #endregion

        #region 内部辅助

        /// <summary>取得窗口必需控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">查找根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null)
            {
                throw new InvalidOperationException($"[RedDotDebugger] UXML 缺少控件：{name}。");
            }

            return element;
        }

        #endregion
    }
}
#endif
