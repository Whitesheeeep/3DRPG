#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.Baking.Editor
{
    /// <summary>渲染扁平烘焙结果表并转发窗口操作的 UI Toolkit View。</summary>
    internal sealed class BakedResultViewerView : IDisposable
    {
        #region 字段

        private readonly Label titleLabel;
        private readonly Label sourceLabel;
        private readonly Label statusLabel;
        private readonly Button bakeButton;
        private readonly Button refreshButton;
        private readonly Button pingButton;
        private readonly MultiColumnListView table;
        private readonly List<BakedResultRowData> rows = new();
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>请求执行 Bake。</summary>
        internal event Action BakeRequested;
        /// <summary>请求重新读取结果。</summary>
        internal event Action RefreshRequested;
        /// <summary>请求定位 Unity 数据源。</summary>
        internal event Action PingRequested;

        #endregion

        #region 生命周期

        /// <summary>查询窗口控件并注册用户操作回调。</summary>
        /// <param name="root">已克隆窗口 UXML 的根节点。</param>
        public BakedResultViewerView(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            titleLabel = Require<Label>(root, "TitleLabel");
            sourceLabel = Require<Label>(root, "SourceLabel");
            statusLabel = Require<Label>(root, "StatusLabel");
            bakeButton = Require<Button>(root, "BakeButton");
            refreshButton = Require<Button>(root, "RefreshButton");
            pingButton = Require<Button>(root, "PingButton");
            table = Require<MultiColumnListView>(root, "ResultTable");
            bakeButton.clicked += OnBakeClicked;
            refreshButton.clicked += OnRefreshClicked;
            pingButton.clicked += OnPingClicked;
            table.selectionType = SelectionType.None;
            table.sortingEnabled = false;
        }

        /// <summary>释放按钮回调和动态表格数据。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bakeButton.clicked -= OnBakeClicked;
            refreshButton.clicked -= OnRefreshClicked;
            pingButton.clicked -= OnPingClicked;
            rows.Clear();
            table.itemsSource = null;
            table.columns.Clear();
        }

        #endregion

        #region 状态渲染

        /// <summary>显示当前数据源标题并启用可用操作。</summary>
        /// <param name="source">当前数据源。</param>
        internal void SetSource(IBakedResultDataSource source)
        {
            titleLabel.text = source.BakedResultTitle;
            sourceLabel.text = source is UnityEngine.Object target
                ? $"来源：{target.name}"
                : "来源：当前编辑器会话中的对象";
            bakeButton.SetEnabled(true);
            refreshButton.SetEnabled(true);
            pingButton.SetEnabled(source is UnityEngine.Object);
        }

        /// <summary>渲染一次最终结果表快照。</summary>
        /// <param name="data">结果表快照。</param>
        internal void Render(BakedResultTableData data)
        {
            titleLabel.text = data.Title;
            table.columns.Clear();
            rows.Clear();
            rows.AddRange(data.Rows);
            for (int index = 0; index < data.Headers.Count; index++)
            {
                int columnIndex = index;
                var column = new Column
                {
                    name = $"BakedResultColumn{index}",
                    title = data.Headers[index],
                    width = index == 0 ? 100f : 150f,
                    resizable = true,
                    stretchable = true
                };
                column.makeCell = () => new Label();
                column.bindCell = (element, rowIndex) =>
                {
                    if (element is not Label label || rowIndex < 0 || rowIndex >= rows.Count)
                    {
                        return;
                    }

                    label.text = rows[rowIndex].Cells[columnIndex];
                    label.tooltip = label.text;
                };
                table.columns.Add(column);
            }

            table.itemsSource = rows;
            table.style.display = data.Rows.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            statusLabel.text = data.Rows.Count == 0
                ? "尚未生成烘焙结果。"
                : $"已生成 {data.Rows.Count} 行、{data.Headers.Count} 列。";
            table.Rebuild();
        }

        /// <summary>显示数据源丢失状态。</summary>
        internal void ShowUnavailableSource()
        {
            titleLabel.text = "烘焙结果";
            sourceLabel.text = "来源对象已失效，请从资产或代码入口重新打开。";
            statusLabel.text = "无法恢复普通 C# 数据源。";
            bakeButton.SetEnabled(false);
            refreshButton.SetEnabled(false);
            pingButton.SetEnabled(false);
            table.style.display = DisplayStyle.None;
        }

        /// <summary>显示错误状态并隐藏旧表格。</summary>
        /// <param name="message">错误消息。</param>
        internal void ShowError(string message)
        {
            statusLabel.text = message ?? "烘焙结果读取失败。";
            table.style.display = DisplayStyle.None;
        }

        #endregion

        #region 事件处理

        /// <summary>转发 Bake 按钮操作。</summary>
        private void OnBakeClicked() => BakeRequested?.Invoke();

        /// <summary>转发刷新按钮操作。</summary>
        private void OnRefreshClicked() => RefreshRequested?.Invoke();

        /// <summary>转发定位按钮操作。</summary>
        private void OnPingClicked() => PingRequested?.Invoke();

        #endregion

        #region 内部辅助

        /// <summary>从根节点获取必需控件。</summary>
        /// <typeparam name="T">控件类型。</typeparam>
        /// <param name="root">查找根节点。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>找到的控件。</returns>
        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new InvalidOperationException($"烘焙结果窗口缺少控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
