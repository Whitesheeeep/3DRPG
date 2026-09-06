#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIToolkitExtensions.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>
    /// Item 配置窗口的组合根门面。
    /// 根 View 只负责组装子 View、转发事件和转发 Controller 指令，不拥有具体控件状态。
    /// </summary>
    internal sealed class ItemConfigEditorView : IDisposable
    {
        #region 子 View 与布局

        // 子 View 各自拥有一块 UI 和其生命周期；根节点只负责跨面板组合。
        private readonly ItemConfigToolbarView toolbarView;
        private readonly ItemDefinitionListView definitionListView;
        private readonly ItemDefinitionDetailsView definitionDetailsView;
        private readonly ItemConfigStatusView statusView;
        private bool disposed;

        #endregion

        #region 事件转发

        /// <summary>转发数据库选择事件。</summary>
        internal event Action<ItemDatabase> DatabaseChanged
        {
            add => toolbarView.DatabaseChanged += value;
            remove => toolbarView.DatabaseChanged -= value;
        }

        /// <summary>转发搜索变化事件。</summary>
        internal event Action<string> SearchChanged
        {
            add => definitionListView.SearchChanged += value;
            remove => definitionListView.SearchChanged -= value;
        }

        /// <summary>转发分类筛选变化事件。</summary>
        internal event Action<string> CategoryChanged
        {
            add => definitionListView.CategoryChanged += value;
            remove => definitionListView.CategoryChanged -= value;
        }

        /// <summary>转发定义类型筛选变化事件。</summary>
        internal event Action<string> KindChanged
        {
            add => definitionListView.KindChanged += value;
            remove => definitionListView.KindChanged -= value;
        }

        /// <summary>转发排序字段变化事件。</summary>
        internal event Action<string> SortFieldChanged
        {
            add => definitionListView.SortFieldChanged += value;
            remove => definitionListView.SortFieldChanged -= value;
        }

        /// <summary>转发排序方向变化事件。</summary>
        internal event Action<string> SortDirectionChanged
        {
            add => definitionListView.SortDirectionChanged += value;
            remove => definitionListView.SortDirectionChanged -= value;
        }

        /// <summary>转发列表选择事件。</summary>
        internal event Action<ItemDefinition> DefinitionSelected
        {
            add => definitionListView.DefinitionSelected += value;
            remove => definitionListView.DefinitionSelected -= value;
        }

        /// <summary>转发新建可堆叠物品请求。</summary>
        internal event Action<ItemCategory> NewStackableRequested
        {
            add
            {
                toolbarView.NewStackableRequested += value;
                definitionListView.NewStackableRequested += value;
            }
            remove
            {
                toolbarView.NewStackableRequested -= value;
                definitionListView.NewStackableRequested -= value;
            }
        }

        /// <summary>转发新建武器请求。</summary>
        internal event Action NewWeaponRequested
        {
            add
            {
                toolbarView.NewWeaponRequested += value;
                definitionListView.NewWeaponRequested += value;
            }
            remove
            {
                toolbarView.NewWeaponRequested -= value;
                definitionListView.NewWeaponRequested -= value;
            }
        }

        /// <summary>转发新建养成道具请求。</summary>
        internal event Action NewDevelopmentItemRequested
        {
            add
            {
                toolbarView.NewDevelopmentItemRequested += value;
                definitionListView.NewDevelopmentItemRequested += value;
            }
            remove
            {
                toolbarView.NewDevelopmentItemRequested -= value;
                definitionListView.NewDevelopmentItemRequested -= value;
            }
        }

        /// <summary>转发新建圣遗物请求。</summary>
        internal event Action NewArtifactRequested
        {
            add
            {
                toolbarView.NewArtifactRequested += value;
                definitionListView.NewArtifactRequested += value;
            }
            remove
            {
                toolbarView.NewArtifactRequested -= value;
                definitionListView.NewArtifactRequested -= value;
            }
        }

        /// <summary>转发工具栏复制请求。</summary>
        internal event Action DuplicateRequested
        {
            add => toolbarView.DuplicateRequested += value;
            remove => toolbarView.DuplicateRequested -= value;
        }

        /// <summary>转发工具栏移出数据库请求。</summary>
        internal event Action RemoveRequested
        {
            add => toolbarView.RemoveRequested += value;
            remove => toolbarView.RemoveRequested -= value;
        }

        /// <summary>转发工具栏删除请求。</summary>
        internal event Action DeleteRequested
        {
            add => toolbarView.DeleteRequested += value;
            remove => toolbarView.DeleteRequested -= value;
        }

        /// <summary>转发列表右键命令。</summary>
        internal event Action<ItemDefinition, ItemDefinitionCommand> DefinitionCommandRequested
        {
            add => definitionListView.DefinitionCommandRequested += value;
            remove => definitionListView.DefinitionCommandRequested -= value;
        }

        /// <summary>转发应用类型默认值请求。</summary>
        internal event Action ApplyDefaultsRequested
        {
            add => toolbarView.ApplyDefaultsRequested += value;
            remove => toolbarView.ApplyDefaultsRequested -= value;
        }

        /// <summary>转发验证请求。</summary>
        internal event Action ValidateRequested
        {
            add => toolbarView.ValidateRequested += value;
            remove => toolbarView.ValidateRequested -= value;
        }

        /// <summary>转发定位资产请求。</summary>
        internal event Action PingRequested
        {
            add => toolbarView.PingRequested += value;
            remove => toolbarView.PingRequested -= value;
        }

        /// <summary>转发武器成长烘焙请求。</summary>
        internal event Action BakeGrowthRequested
        {
            add => definitionDetailsView.BakeGrowthRequested += value;
            remove => definitionDetailsView.BakeGrowthRequested -= value;
        }

        /// <summary>转发圣遗物成长烘焙请求。</summary>
        internal event Action BakeArtifactGrowthRequested
        {
            add => definitionDetailsView.BakeArtifactGrowthRequested += value;
            remove => definitionDetailsView.BakeArtifactGrowthRequested -= value;
        }

        /// <summary>转发武器通用烘焙结果查看请求。</summary>
        internal event Action ViewBakedResultRequested
        {
            add => definitionDetailsView.ViewBakedResultRequested += value;
            remove => definitionDetailsView.ViewBakedResultRequested -= value;
        }

        /// <summary>转发圣遗物通用烘焙结果查看请求。</summary>
        internal event Action ViewArtifactBakedResultRequested
        {
            add => definitionDetailsView.ViewArtifactBakedResultRequested += value;
            remove => definitionDetailsView.ViewArtifactBakedResultRequested -= value;
        }

        /// <summary>合并列表和右侧详情的统一重命名事件。</summary>
        internal event Action<ItemDefinition, string> RenameSubmitted
        {
            add
            {
                definitionListView.RenameSubmitted += value;
                definitionDetailsView.RenameSubmitted += value;
            }
            remove
            {
                definitionListView.RenameSubmitted -= value;
                definitionDetailsView.RenameSubmitted -= value;
            }
        }

        /// <summary>转发详情字段变化事件。</summary>
        internal event Action<ItemDefinition> PropertiesChanged
        {
            add => definitionDetailsView.PropertiesChanged += value;
            remove => definitionDetailsView.PropertiesChanged -= value;
        }

        /// <summary>转发编辑器预览 Sprite 变化事件。</summary>
        internal event Action<ItemDefinition, Sprite> PreviewIconChanged
        {
            add => definitionDetailsView.PreviewIconChanged += value;
            remove => definitionDetailsView.PreviewIconChanged -= value;
        }

        #endregion

        #region 生命周期

        /// <summary>组装窗口各个子 View，并配置固定左侧面板。</summary>
        /// <param name="root">窗口根节点。</param>
        internal ItemConfigEditorView(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            CustomTwoPanelSplitView splitView = Require<CustomTwoPanelSplitView>(root, "ItemEditorSplitView");
            splitView.ConfigureFixedPane(320f, 360f, 360f, "RPG.ItemConfigEditor.SidebarWidth");

            Button toolbarAnchor = Require<Button>(root, "NewStackableButton");
            toolbarView = new ItemConfigToolbarView(toolbarAnchor.parent);

            ListView list = Require<ListView>(root, "DefinitionList");
            definitionListView = new ItemDefinitionListView(list.parent, list);

            ScrollView details = Require<ScrollView>(root, "DetailsContainer");
            definitionDetailsView = new ItemDefinitionDetailsView(details.parent, details);

            statusView = new ItemConfigStatusView(Require<Label>(root, "StatusLabel"));
        }

        /// <summary>按子 View 所有权顺序释放事件、绑定和虚拟化资源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            definitionDetailsView.Dispose();
            definitionListView.Dispose();
            toolbarView.Dispose();
            statusView.Dispose();
        }

        #endregion

        #region Controller 转发

        /// <summary>设置当前数据库字段显示值。</summary>
        /// <param name="database">数据库。</param>
        internal void SetDatabase(ItemDatabase database) => toolbarView.SetDatabase(database);

        /// <summary>设置搜索文本。</summary>
        /// <param name="value">搜索文本。</param>
        internal void SetSearch(string value) => definitionListView.SetSearch(value);

        /// <summary>设置筛选控件显示值。</summary>
        /// <param name="category">分类筛选。</param>
        /// <param name="kind">定义类型筛选。</param>
        internal void SetFilters(string category, string kind) => definitionListView.SetFilters(category, kind);

        /// <summary>设置排序控件显示值。</summary>
        /// <param name="field">排序字段。</param>
        /// <param name="direction">排序方向。</param>
        internal void SetSorting(string field, string direction) => definitionListView.SetSorting(field, direction);

        /// <summary>渲染筛选后的定义集合并恢复当前选择。</summary>
        /// <param name="definitions">筛选后的定义。</param>
        /// <param name="selected">当前选中定义。</param>
        /// <param name="locateSelection">是否将选中项滚动到可视区域。</param>
        internal void RenderDefinitions(IReadOnlyList<ItemDefinition> definitions, ItemDefinition selected, bool locateSelection = true) =>
            definitionListView.RenderDefinitions(definitions, selected, locateSelection);

        /// <summary>绑定右侧当前定义详情。</summary>
        /// <param name="definition">当前定义；为空时显示空状态。</param>
        internal void BindDefinition(ItemDefinition definition) => definitionDetailsView.BindDefinition(definition);

        /// <summary>刷新列表行和右侧摘要等轻量展示。</summary>
        /// <param name="definition">发生变化的定义。</param>
        /// <param name="refreshList">是否刷新列表行。</param>
        internal void RefreshDefinitionPresentation(ItemDefinition definition, bool refreshList = true)
        {
            definitionListView.RefreshDefinition(definition, refreshList);
            definitionDetailsView.RefreshPresentation(definition);
        }

        /// <summary>刷新 Undo/Redo 后的序列化状态，并丢弃尚未提交的临时名称输入。</summary>
        internal void PrepareForUndoRedoRefresh() => definitionDetailsView.PrepareForUndoRedoRefresh();

        /// <summary>更新底部普通状态。</summary>
        /// <param name="message">状态文本。</param>
        internal void RefreshStatus(string message) => statusView.ShowMessage(message);

        /// <summary>更新底部错误状态。</summary>
        /// <param name="message">错误文本。</param>
        internal void ShowError(string message) => statusView.ShowError(message);

        #endregion

        #region 内部查询

        /// <summary>在限定根节点内查询必需的 UXML 控件。</summary>
        /// <typeparam name="TElement">控件类型。</typeparam>
        /// <param name="root">查询根节点。</param>
        /// <param name="name">UXML 名称。</param>
        /// <returns>找到的控件。</returns>
        private static TElement Require<TElement>(VisualElement root, string name) where TElement : VisualElement
        {
            TElement element = root.Q<TElement>(name);
            if (element == null) throw new InvalidOperationException($"Item 配置窗口缺少 UXML 控件：{name}。");
            return element;
        }

        #endregion
    }
}
#endif
