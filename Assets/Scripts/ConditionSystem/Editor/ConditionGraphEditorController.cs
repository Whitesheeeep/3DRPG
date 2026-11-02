#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WS_Modules.UIToolkitExtensions.Editor.GraphView;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>协调图编辑、资源所有权和 Undo；界面与领域数据之间只保留对象引用。</summary>
    internal sealed class ConditionGraphEditorController : IDisposable
    {
        #region 依赖字段
        private readonly ConditionGraphEditorView view;
        private readonly ConditionGraphView graphView;
        private readonly ConditionGraphEditorState state;
        #endregion

        #region 当前编辑状态
        private ConditionGraph_SO graph;
        private ConditionNode selected;
        private bool disposed;
        private bool refreshScheduled;
        #endregion

        #region 生命周期
        /// <summary>建立窗口与画布意图连接，并订阅 Undo/Redo。</summary>
        /// <param name="view">窗口视图。</param>
        /// <param name="graphView">条件画布。</param>
        /// <param name="state">编辑器持久状态。</param>
        internal ConditionGraphEditorController(ConditionGraphEditorView view, ConditionGraphView graphView,
            ConditionGraphEditorState state)
        {
            this.view = view;
            this.graphView = graphView;
            this.state = state;
            view.AssetRequested += OpenAsset;
            view.NewRequested += CreateAsset;
            view.SaveRequested += SaveAsset;
            view.ValidateRequested += ValidateAsset;
            view.FieldsChanged += OnFieldsChanged;
            view.MoveChildRequested += MoveChild;
            view.DisconnectRequested += Disconnect;
            view.LocateRequested += graphView.SelectNode;
            graphView.Changed += OnGraphChanged;
            graphView.Selected += SelectNode;
            graphView.CreateRequested += CreateNode;
            graphView.RootRequested += SetRoot;
            graphView.LayoutChanged += OnLayoutChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
            Debug.Log("[ConditionGraphEditor] 绑定窗口与画布事件。");
        }

        /// <summary>保存画布状态并对称释放所有事件和序列化绑定。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StoreState();
            view.AssetRequested -= OpenAsset;
            view.NewRequested -= CreateAsset;
            view.SaveRequested -= SaveAsset;
            view.ValidateRequested -= ValidateAsset;
            view.FieldsChanged -= OnFieldsChanged;
            view.MoveChildRequested -= MoveChild;
            view.DisconnectRequested -= Disconnect;
            view.LocateRequested -= graphView.SelectNode;
            graphView.Changed -= OnGraphChanged;
            graphView.Selected -= SelectNode;
            graphView.CreateRequested -= CreateNode;
            graphView.RootRequested -= SetRoot;
            graphView.LayoutChanged -= OnLayoutChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            view.Dispose();
            Debug.Log("[ConditionGraphEditor] 解绑窗口与画布事件。");
        }
        #endregion

        #region 资产操作
        /// <summary>切换前释放旧绑定，并从新资产直接还原节点和连线。</summary>
        /// <param name="asset">新资产，允许清空。</param>
        internal void OpenAsset(ConditionGraph_SO asset)
        {
            StoreState();
            view.ClearDetails();
            graph = asset;
            selected = null;
            view.SetAsset(asset);
            graphView.Rebuild(asset);
            bool restore = asset != null && state.ResolveAsset() == asset;
            graphView.UpdateViewTransform(restore ? state.Position : Vector3.zero, restore ? state.Scale : Vector3.one);
            RefreshPanels();
            view.SetStatus(asset == null ? "未打开条件图" : $"当前条件图：{asset.name}");
            Debug.Log($"[ConditionGraphEditor] 打开条件图，asset={(asset == null ? "<empty>" : asset.name)}");
        }

        /// <summary>通过保存面板创建主资产；取消时保持当前编辑目标。</summary>
        private void CreateAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject("创建条件图", "ConditionGraph", "asset", "选择条件图保存位置");
            if (string.IsNullOrEmpty(path)) return;
            var asset = ScriptableObject.CreateInstance<ConditionGraph_SO>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            OpenAsset(asset);
            Debug.Log($"[ConditionGraphEditor] 创建条件图，asset={asset.name}");
        }

        /// <summary>允许保存未完成草稿，同时显示配置问题；不对结构作自动修复。</summary>
        private void SaveAsset()
        {
            if (graph == null) { view.SetStatus("请先打开条件图"); return; }
            // 原生绑定先提交参数，再保存主资产和子资产，避免保存旧字段快照。
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            StoreState();
            RefreshPanels();
            view.SetStatus($"已保存：{graph.name}（校验问题见右侧）");
            Debug.Log($"[ConditionGraphEditor] 保存完成，asset={graph.name}，nodeCount={graph.Nodes.Count}");
        }

        /// <summary>显式运行公共校验并输出一次结果摘要。</summary>
        private void ValidateAsset()
        {
            if (graph == null) { view.SetStatus("请先打开条件图"); return; }
            var issues = ConditionGraphValidator.Validate(graph);
            view.ShowIssues(issues);
            int errors = issues.Count(issue => issue.Severity == E_ConditionIssueSeverity.Error);
            view.SetStatus($"校验完成：错误 {errors}，警告 {issues.Count - errors}");
            if (errors > 0) Debug.LogWarning($"[ConditionGraphEditor] 校验未通过，asset={graph.name}，errors={errors}");
            else Debug.Log($"[ConditionGraphEditor] 校验完成，asset={graph.name}，warnings={issues.Count}");
        }

        /// <summary>保存窗口变换，主资产移动后仍通过 GUID 恢复。</summary>
        private void StoreState()
        {
            if (graph != null) state.Store(graph, graphView.viewTransform.position, graphView.viewTransform.scale);
        }
        #endregion

        #region 领域编辑
        /// <summary>创建具体节点子资产；首个节点自动作为入口，后续由右键显式设置。</summary>
        /// <param name="type">TypeCache 发现的具体节点类型。</param>
        /// <param name="position">GraphView 内容坐标。</param>
        private void CreateNode(Type type, Vector2 position)
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var node = (ConditionNode)ScriptableObject.CreateInstance(type);
            // 先记录对象创建，再记录配置前状态；创建注册会结束其他 RecordObject 的延迟记录。
            Undo.RegisterCreatedObjectUndo(node, "创建条件节点");
            RecordGraph("创建条件节点");
            Undo.RegisterCompleteObjectUndo(node, "配置条件节点");
            node.name = ObjectNames.NicifyVariableName(type.Name);
            node.EditorPosition = position;
            AssetDatabase.AddObjectToAsset(node, graph);
            graph.AddNode(node);
            if (graph.Root == null) graph.SetRoot(node);
            Undo.CollapseUndoOperations(undoGroup);
            selected = node;
            MarkGraphDirty();
            Rebuild();
        }

        /// <summary>将无父条件设为根节点，并更新全部根标记。</summary>
        /// <param name="node">目标根节点。</param>
        private void SetRoot(ConditionNode node)
        {
            RecordGraph("设置根条件");
            graph.SetRoot(node);
            MarkGraphDirty();
            graphView.RefreshSummaries();
            RefreshPanels();
        }

        /// <summary>通过结构命令调整求值顺序，不用原生集合编辑绕过图规则。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="index">原顺序。</param>
        /// <param name="targetIndex">目标顺序。</param>
        private void MoveChild(ConditionNode parent, int index, int targetIndex)
        {
            Undo.RecordObject(parent, "调整条件顺序");
            parent.MoveChild(index, targetIndex);
            EditorUtility.SetDirty(parent);
            graphView.RefreshSummaries();
            RefreshPanels();
        }

        /// <summary>处理属性面板的断开操作，保留子资产作为草稿。</summary>
        /// <param name="parent">父节点。</param>
        /// <param name="child">子节点。</param>
        private void Disconnect(ConditionNode parent, ConditionNode child)
        {
            RecordGraph("断开条件连接");
            graph.Disconnect(parent, child);
            MarkGraphDirty();
            Rebuild();
        }

        /// <summary>按已生效的图事件写回模型；结构重建延迟到整批通知结束之后。</summary>
        /// <param name="change">框架实际变化。</param>
        private void OnGraphChanged(GraphChangeEvent change)
        {
            if (graph == null) return;
            RecordGraph("编辑条件图");
            foreach (GraphConnectionContext connection in change.Connections)
            {
                var parent = ((ConditionGraphNodeView)connection.OutputNode).Model;
                var child = ((ConditionGraphNodeView)connection.InputNode).Model;
                if (change.Type == GraphChangeType.ConnectionsCreated) graph.Connect(parent, child);
                else if (change.Type == GraphChangeType.ConnectionsRemoved) graph.Disconnect(parent, child);
            }
            foreach (ConditionGraphNodeView nodeView in change.Nodes.OfType<ConditionGraphNodeView>())
            {
                ConditionNode model = nodeView.Model;
                // 延迟图事件可能与同一批销毁操作交错，已释放的资源不再重复销毁。
                if (model == null || !graph.Nodes.Contains(model)) continue;
                if (change.Type == GraphChangeType.NodesMoved)
                    model.EditorPosition = GetContentPosition(nodeView);
                else if (change.Type == GraphChangeType.NodesRemoved)
                {
                    // 先解绑即将销毁的序列化目标，再清除领域引用；子节点不会随父节点销毁。
                    view.ClearDetails();
                    if (selected == model) selected = null;
                    graph.RemoveNode(model);
                    Undo.DestroyObjectImmediate(model);
                }
            }
            MarkGraphDirty();
            if (change.Type == GraphChangeType.NodesMoved) return;
            ScheduleRebuild();
        }

        /// <summary>将框架布局的最终内容坐标写回，并将一次布局作为可撤销操作。</summary>
        /// <param name="change">布局结果。</param>
        private void OnLayoutChanged(GraphLayoutChange change)
        {
            if (graph == null) return;
            RecordGraph("条件节点布局");
            foreach (ConditionGraphNodeView nodeView in change.Nodes.OfType<ConditionGraphNodeView>())
                nodeView.Model.EditorPosition = GetContentPosition(nodeView);
            MarkGraphDirty();
        }

        /// <summary>读取 SetPosition 已写入的内容坐标，避免同一帧 GetPosition 仍返回上一次布局结果。</summary>
        /// <param name="nodeView">已由框架 SetPosition 设置绝对像素位置的节点。</param>
        /// <returns>GraphView 内容空间中的左上角像素坐标。</returns>
        private static Vector2 GetContentPosition(ConditionGraphNodeView nodeView) =>
            new Vector2(nodeView.style.left.value.value, nodeView.style.top.value.value);

        /// <summary>在修改前快照图与节点，包含连线和删除造成的入向引用变化。</summary>
        /// <param name="operation">Undo 操作名称。</param>
        private void RecordGraph(string operation)
        {
            // 完整快照立即记录旧引用，避免 RegisterCreatedObjectUndo 提前刷新而遗漏图集合变化。
            Undo.RegisterCompleteObjectUndo(graph.Nodes.Where(node => node != null).Cast<UnityEngine.Object>()
                .Append(graph).ToArray(), operation);
        }

        /// <summary>标记主资产和当前节点，保存时统一提交磁盘。</summary>
        private void MarkGraphDirty()
        {
            EditorUtility.SetDirty(graph);
            foreach (ConditionNode node in graph.Nodes) if (node != null) EditorUtility.SetDirty(node);
        }
        #endregion

        #region 视图同步
        /// <summary>切换属性目标时，View 负责解除旧对象绑定。</summary>
        /// <param name="node">当前选择。</param>
        private void SelectNode(ConditionNode node) { selected = node; view.ShowNode(node); }

        /// <summary>普通参数已经写回，只更新摘要和校验，避免重绑正在输入的字段。</summary>
        private void OnFieldsChanged()
        {
            graphView.RefreshSummaries();
            if (graph != null) view.ShowIssues(ConditionGraphValidator.Validate(graph));
        }

        /// <summary>刷新属性面板和可定位校验列表。</summary>
        private void RefreshPanels()
        {
            view.ShowNode(selected);
            if (graph != null) view.ShowIssues(ConditionGraphValidator.Validate(graph));
            else view.ShowIssues(null);
        }

        /// <summary>等待框架整批断线和删除通知结束后再重建视觉元素。</summary>
        private void ScheduleRebuild()
        {
            if (refreshScheduled) return;
            refreshScheduled = true;
            graphView.schedule.Execute(() =>
            {
                refreshScheduled = false;
                if (!disposed) Rebuild();
            });
        }

        /// <summary>从领域对象重建视图，恢复仍然有效的选择且不触发领域删除。</summary>
        private void Rebuild()
        {
            ConditionNode node = selected;
            graphView.Rebuild(graph);
            selected = node != null && graph != null && graph.Nodes.Contains(node) ? node : null;
            if (selected != null) graphView.SelectGraphNode(
                graphView.nodes.OfType<ConditionGraphNodeView>().First(item => item.Model == selected));
            RefreshPanels();
        }

        /// <summary>Undo/Redo 后从实际资产恢复视图，销毁目标不保留旧绑定。</summary>
        private void OnUndoRedo()
        {
            if (disposed) return;
            view.ClearDetails();
            if (graph != null)
            {
                // Unity 重做创建/撤销删除时可能只恢复内存对象；恢复子资产归属，不改变领域引用或参数。
                int restoredCount = 0;
                foreach (ConditionNode node in graph.Nodes)
                {
                    if (node == null || EditorUtility.IsPersistent(node)) continue;
                    AssetDatabase.AddObjectToAsset(node, graph);
                    EditorUtility.SetDirty(node);
                    restoredCount++;
                }
                if (restoredCount > 0)
                {
                    EditorUtility.SetDirty(graph);
                    Debug.Log($"[ConditionGraphEditor] Undo/Redo 恢复节点子资产，asset={graph.name}，count={restoredCount}");
                }
            }
            Rebuild();
        }
        #endregion
    }
}
#endif
