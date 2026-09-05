#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WS_Modules.Baking;
using WS_Modules.Baking.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>Item 配置窗口 Controller，负责筛选状态、选中状态和编辑命令编排。</summary>
    internal sealed class ItemConfigEditorController : IDisposable
    {
        #region 字段

        private readonly ItemConfigEditorView view;
        private readonly ItemConfigEditorService service;
        // 通用烘焙事务服务统一负责 Undo、Dirty 和保存，Controller 只编排当前选中数据源。
        private readonly BakedResultEditorService bakedResultService;
        private readonly List<ItemDefinition> allDefinitions = new();
        // 只记录当前窗口会话中实际执行过重命名的 Definition，Undo Group 通过 GUID 与资产路径解耦。
        private readonly Dictionary<int, string> renameUndoTargets = new();
        private ItemDatabase database;
        private ItemDefinition selectedDefinition;
        private string search = string.Empty;
        private string category = "全部类型";
        private string kind = "全部定义";
        private string sortField = "默认排序优先级";
        private string sortDirection = "降序";
        private bool disposed;
        private bool refreshScheduled;
        private bool growthProfileSyncScheduled;
        private bool undoRedoRefreshScheduled;
        private bool undoRedoWriteSuppressed;
        private readonly HashSet<int> pendingRenameUndoGroups = new();

        #endregion

        #region 生命周期

        /// <summary>创建 Controller 并恢复窗口会话状态。</summary>
        /// <param name="view">窗口视图。</param>
        internal ItemConfigEditorController(ItemConfigEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            service = new ItemConfigEditorService();
            bakedResultService = new BakedResultEditorService();
            database = service.ResolveDatabase();
            search = ItemConfigEditorSession.Search;
            category = ItemConfigEditorSession.Category;
            sortField = ItemConfigEditorSession.SortField;
            sortDirection = ItemConfigEditorSession.SortDirection;
            view.DatabaseChanged += OnDatabaseChanged;
            view.SearchChanged += OnSearchChanged;
            view.CategoryChanged += OnCategoryChanged;
            view.KindChanged += OnKindChanged;
            view.SortFieldChanged += OnSortFieldChanged;
            view.SortDirectionChanged += OnSortDirectionChanged;
            view.DefinitionSelected += OnDefinitionSelected;
            view.NewStackableRequested += OnNewStackableRequested;
            view.NewWeaponRequested += OnNewWeaponRequested;
            view.NewDevelopmentItemRequested += OnNewDevelopmentItemRequested;
            view.NewArtifactRequested += OnNewArtifactRequested;
            view.DuplicateRequested += OnDuplicateRequested;
            view.RemoveRequested += OnRemoveRequested;
            view.DeleteRequested += OnDeleteRequested;
            view.DefinitionCommandRequested += OnDefinitionCommandRequested;
            view.ApplyDefaultsRequested += OnApplyDefaultsRequested;
            view.ValidateRequested += OnValidateRequested;
            view.PingRequested += OnPingRequested;
            view.BakeGrowthRequested += OnBakeGrowthRequested;
            view.BakeArtifactGrowthRequested += OnBakeArtifactGrowthRequested;
            view.ViewBakedResultRequested += OnViewBakedResultRequested;
            view.ViewArtifactBakedResultRequested += OnViewArtifactBakedResultRequested;
            view.RenameSubmitted += OnRenameSubmitted;
            view.PropertiesChanged += OnPropertiesChanged;
            view.PreviewIconChanged += OnPreviewIconChanged;
            Undo.undoRedoEvent += OnUndoRedoEvent;
            EditorApplication.projectChanged += OnProjectChanged;
            view.SetDatabase(database);
            view.SetSearch(search);
            view.SetFilters(category, kind);
            view.SetSorting(sortField, sortDirection);
            RefreshDefinitions();
        }

        /// <summary>选中指定定义并在需要时切换到其数据库。</summary>
        /// <param name="definition">待选定义。</param>
        internal void OpenDefinition(ItemDefinition definition)
        {
            if (definition == null) return;
            ItemDatabase owner = service.FindDatabase(definition);
            if (owner != null && owner != database)
            {
                database = owner;
                view.SetDatabase(database);
                ItemConfigEditorSession.SetDatabasePath(AssetDatabase.GetAssetPath(database));
            }
            selectedDefinition = definition;
            ItemConfigEditorSession.SetDefinitionPath(AssetDatabase.GetAssetPath(definition));
            RefreshDefinitions();
        }

        /// <summary>打开指定数据库并清除上一次的物品选择。</summary>
        /// <param name="targetDatabase">要在窗口中显示的数据库。</param>
        internal void OpenDatabase(ItemDatabase targetDatabase)
        {
            if (targetDatabase == null) return;
            database = targetDatabase;
            selectedDefinition = null;
            ItemConfigEditorSession.SetDatabasePath(AssetDatabase.GetAssetPath(targetDatabase));
            ItemConfigEditorSession.SetDefinitionPath(string.Empty);
            view.SetDatabase(targetDatabase);
            RefreshDefinitions();
        }

        /// <summary>解除所有事件订阅。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.DatabaseChanged -= OnDatabaseChanged;
            view.SearchChanged -= OnSearchChanged;
            view.CategoryChanged -= OnCategoryChanged;
            view.KindChanged -= OnKindChanged;
            view.SortFieldChanged -= OnSortFieldChanged;
            view.SortDirectionChanged -= OnSortDirectionChanged;
            view.DefinitionSelected -= OnDefinitionSelected;
            view.NewStackableRequested -= OnNewStackableRequested;
            view.NewWeaponRequested -= OnNewWeaponRequested;
            view.NewDevelopmentItemRequested -= OnNewDevelopmentItemRequested;
            view.NewArtifactRequested -= OnNewArtifactRequested;
            view.DuplicateRequested -= OnDuplicateRequested;
            view.RemoveRequested -= OnRemoveRequested;
            view.DeleteRequested -= OnDeleteRequested;
            view.DefinitionCommandRequested -= OnDefinitionCommandRequested;
            view.ApplyDefaultsRequested -= OnApplyDefaultsRequested;
            view.ValidateRequested -= OnValidateRequested;
            view.PingRequested -= OnPingRequested;
            view.BakeGrowthRequested -= OnBakeGrowthRequested;
            view.BakeArtifactGrowthRequested -= OnBakeArtifactGrowthRequested;
            view.ViewBakedResultRequested -= OnViewBakedResultRequested;
            view.ViewArtifactBakedResultRequested -= OnViewArtifactBakedResultRequested;
            view.RenameSubmitted -= OnRenameSubmitted;
            view.PropertiesChanged -= OnPropertiesChanged;
            view.PreviewIconChanged -= OnPreviewIconChanged;
            Undo.undoRedoEvent -= OnUndoRedoEvent;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.delayCall -= ExecuteScheduledRefresh;
            EditorApplication.delayCall -= ExecuteGrowthProfileSynchronization;
            EditorApplication.delayCall -= ExecuteUndoRedoRefresh;
            EditorApplication.delayCall -= FinishUndoRedoRefresh;
            view.Dispose();
        }

        #endregion

        #region 筛选与呈现

        /// <summary>重新读取数据库定义并按当前筛选条件渲染。</summary>
        /// <param name="rebindDefinition">是否重新绑定右侧详情。</param>
        /// <param name="locateSelection">是否滚动到当前选中项。</param>
        private void RefreshDefinitions(bool rebindDefinition = true, bool locateSelection = true)
        {
            allDefinitions.Clear();
            if (database != null)
            {
                for (int index = 0; index < database.Definitions.Count; index++)
                    if (database.Definitions[index] != null) allDefinitions.Add(database.Definitions[index]);
            }

            List<ItemDefinition> filtered = SortDefinitions(allDefinitions.Where(MatchesFilter)).ToList();
            if (selectedDefinition != null && !filtered.Contains(selectedDefinition)) selectedDefinition = filtered.FirstOrDefault();
            if (selectedDefinition == null) selectedDefinition = filtered.FirstOrDefault();
            view.RenderDefinitions(filtered, selectedDefinition, locateSelection);
            if (rebindDefinition) view.BindDefinition(selectedDefinition);
            view.RefreshStatus(database == null ? "请选择或创建 ItemDatabase。" : $"共 {allDefinitions.Count} 个定义，当前显示 {filtered.Count} 个。");
        }

        /// <summary>判断定义是否满足搜索和下拉筛选。</summary>
        /// <param name="definition">待判断定义。</param>
        /// <returns>匹配时返回 true。</returns>
        private bool MatchesFilter(ItemDefinition definition)
        {
            string displayName = definition.DisplayName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(search) && displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && definition.ItemId.ToString().IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (category != "全部类型" && ItemConfigEditorPresentation.GetCategoryText(definition.Category) != category) return false;
            if (kind == "可堆叠物品" && definition is not StackableItemDefinition) return false;
            if (kind == "养成道具定义" && definition is not DevelopmentItemDefinition) return false;
            if (kind == "武器定义" && definition is not WeaponDefinition) return false;
            if (kind == "圣遗物定义" && definition is not ArtifactDefinition) return false;
            return true;
        }

        /// <summary>对已筛选定义执行稳定的多字段排序。</summary>
        /// <param name="definitions">已完成搜索和分类筛选的定义。</param>
        /// <returns>排序后的定义序列。</returns>
        private IEnumerable<ItemDefinition> SortDefinitions(IEnumerable<ItemDefinition> definitions)
        {
            bool descending = sortDirection == "降序";
            switch (sortField)
            {
                case "显示名称":
                    return AddStableNameTie(SortText(definitions, definition => definition.DisplayName, descending));
                case "稀有度":
                    return AddStableNameTie(SortNumber(definitions, definition => (int)definition.Rarity, descending));
                case "物品类型":
                    return AddStableNameTie(SortText(definitions, definition => ItemConfigEditorPresentation.GetCategoryText(definition.Category), descending));
                case "定义类型":
                    return AddStableNameTie(SortText(definitions, ItemConfigEditorPresentation.GetDefinitionKindText, descending));
                case "养成用途":
                    return AddStableNameTie(SortText(definitions,
                        definition => definition is DevelopmentItemDefinition development
                            ? ItemConfigEditorPresentation.GetDevelopmentTypeText(development.DevelopmentType)
                            : string.Empty,
                        descending));
                case "稳定物品标识":
                    return SortText(definitions, definition => definition.ItemId.ToString(), descending);
                case "最大堆叠数量":
                    return AddStableNameTie(SortNullableNumber(definitions, definition => definition is StackableItemDefinition item ? item.MaxQuantity : (int?)null, descending));
                case "最大等级":
                    return AddStableNameTie(SortNullableNumber(definitions, definition => definition switch
                    {
                        WeaponDefinition weapon => weapon.MaxLevel,
                        ArtifactDefinition artifact => artifact.MaxLevel,
                        _ => (int?)null
                    }, descending));
                default:
                    // 默认优先级的次级顺序固定为稀有度、名称和稳定标识，确保刷新后顺序不跳动。
                    IOrderedEnumerable<ItemDefinition> priority = SortNumber(definitions, definition => definition.SortPriority, descending);
                    return priority.ThenByDescending(definition => (int)definition.Rarity)
                        .ThenBy(definition => definition.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(definition => definition.ItemId.ToString(), StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>按文本字段排序。</summary>
        /// <param name="definitions">待排序定义。</param>
        /// <param name="selector">文本读取器。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>排序结果。</returns>
        private static IOrderedEnumerable<ItemDefinition> SortText(IEnumerable<ItemDefinition> definitions, Func<ItemDefinition, string> selector, bool descending)
        {
            return descending
                ? definitions.OrderByDescending(selector, StringComparer.OrdinalIgnoreCase)
                : definitions.OrderBy(selector, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>按必有数值字段排序。</summary>
        /// <param name="definitions">待排序定义。</param>
        /// <param name="selector">数值读取器。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>排序结果。</returns>
        private static IOrderedEnumerable<ItemDefinition> SortNumber(IEnumerable<ItemDefinition> definitions, Func<ItemDefinition, int> selector, bool descending)
        {
            return descending ? definitions.OrderByDescending(selector) : definitions.OrderBy(selector);
        }

        /// <summary>按可空数值排序，并始终将缺失值放在末尾。</summary>
        /// <param name="definitions">待排序定义。</param>
        /// <param name="selector">可空数值读取器。</param>
        /// <param name="descending">是否降序。</param>
        /// <returns>排序结果。</returns>
        private static IOrderedEnumerable<ItemDefinition> SortNullableNumber(IEnumerable<ItemDefinition> definitions, Func<ItemDefinition, int?> selector, bool descending)
        {
            IOrderedEnumerable<ItemDefinition> ordered = definitions.OrderBy(definition => selector(definition).HasValue ? 0 : 1);
            return descending ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector);
        }

        /// <summary>为非稳定字段排序添加名称和物品标识的确定性次序。</summary>
        /// <param name="ordered">已有主排序结果。</param>
        /// <returns>追加稳定次级排序后的结果。</returns>
        private static IOrderedEnumerable<ItemDefinition> AddStableNameTie(IOrderedEnumerable<ItemDefinition> ordered)
        {
            return ordered.ThenBy(definition => definition.DisplayName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(definition => definition.ItemId.ToString(), StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region 命令处理

        /// <summary>处理列表右键菜单指定目标的定义命令。</summary>
        /// <param name="definition">右键操作目标。</param>
        /// <param name="command">操作类型。</param>
        private void OnDefinitionCommandRequested(ItemDefinition definition, ItemDefinitionCommand command)
        {
            if (definition == null || database == null) return;
            selectedDefinition = definition;
            ItemConfigEditorSession.SetDefinitionPath(AssetDatabase.GetAssetPath(definition));
            try
            {
                switch (command)
                {
                    case ItemDefinitionCommand.Duplicate:
                        selectedDefinition = service.DuplicateDefinition(definition, database);
                        RefreshDefinitions();
                        break;
                    case ItemDefinitionCommand.ApplyTypeDefaults:
                        service.ApplyCategoryDefaults(database, definition);
                        RefreshDefinitions(false, false);
                        view.RefreshDefinitionPresentation(definition);
                        view.RefreshStatus("已将当前物品类型默认值应用到选中物品。");
                        break;
                    case ItemDefinitionCommand.PingAsset:
                        EditorGUIUtility.PingObject(definition);
                        Selection.activeObject = definition;
                        break;
                    case ItemDefinitionCommand.RemoveFromDatabase:
                        service.RemoveDefinition(database, definition);
                        selectedDefinition = null;
                        RefreshDefinitions();
                        break;
                    case ItemDefinitionCommand.DeleteAsset:
                        if (!EditorUtility.DisplayDialog("删除物品资产", $"确定将“{definition.DisplayName}”移入 Unity 回收站吗？", "删除", "取消")) return;
                        service.DeleteDefinition(database, definition);
                        selectedDefinition = null;
                        RefreshDefinitions();
                        break;
                }
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>设置当前定义并保存会话路径。</summary>
        /// <param name="definition">新定义。</param>
        private void OnDefinitionSelected(ItemDefinition definition)
        {
            selectedDefinition = definition;
            ItemConfigEditorSession.SetDefinitionPath(definition == null ? string.Empty : AssetDatabase.GetAssetPath(definition));
            view.BindDefinition(definition);
        }

        /// <summary>切换数据库并清理旧的选中项。</summary>
        /// <param name="value">新数据库。</param>
        private void OnDatabaseChanged(ItemDatabase value)
        {
            database = value;
            selectedDefinition = null;
            ItemConfigEditorSession.SetDatabasePath(database == null ? string.Empty : AssetDatabase.GetAssetPath(database));
            RefreshDefinitions();
        }

        /// <summary>更新搜索会话并刷新列表。</summary>
        /// <param name="value">搜索文本。</param>
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            ItemConfigEditorSession.SetSearch(search);
            RefreshDefinitions();
        }

        /// <summary>更新分类筛选。</summary>
        /// <param name="value">分类文本。</param>
        private void OnCategoryChanged(string value)
        {
            category = value ?? "全部类型";
            ItemConfigEditorSession.SetCategory(category);
            RefreshDefinitions();
        }

        /// <summary>更新定义类型筛选。</summary>
        /// <param name="value">定义类型文本。</param>
        private void OnKindChanged(string value)
        {
            kind = value ?? "全部定义";
            RefreshDefinitions();
        }

        /// <summary>更新列表排序字段并刷新当前筛选结果。</summary>
        /// <param name="value">排序字段中文名称。</param>
        private void OnSortFieldChanged(string value)
        {
            sortField = value ?? "默认排序优先级";
            ItemConfigEditorSession.SetSortField(sortField);
            RefreshDefinitions();
        }

        /// <summary>更新列表排序方向并刷新当前筛选结果。</summary>
        /// <param name="value">排序方向中文名称。</param>
        private void OnSortDirectionChanged(string value)
        {
            sortDirection = value ?? "降序";
            ItemConfigEditorSession.SetSortDirection(sortDirection);
            RefreshDefinitions();
        }

        /// <summary>创建普通可堆叠物品。</summary>
        private void OnNewStackableRequested() => CreateDefinition(typeof(StackableItemDefinition));

        /// <summary>创建武器定义。</summary>
        private void OnNewWeaponRequested() => CreateDefinition(typeof(WeaponDefinition));

        /// <summary>创建养成道具定义。</summary>
        private void OnNewDevelopmentItemRequested() => CreateDefinition(typeof(DevelopmentItemDefinition));

        /// <summary>创建圣遗物定义。</summary>
        private void OnNewArtifactRequested() => CreateDefinition(typeof(ArtifactDefinition));

        /// <summary>创建定义并选中结果。</summary>
        /// <param name="type">定义类型。</param>
        private void CreateDefinition(Type type)
        {
            try
            {
                selectedDefinition = service.CreateDefinition(type, database);
                RefreshDefinitions();
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>复制当前定义。</summary>
        private void OnDuplicateRequested()
        {
            try
            {
                selectedDefinition = service.DuplicateDefinition(selectedDefinition, database);
                RefreshDefinitions();
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>只从数据库移除当前定义。</summary>
        private void OnRemoveRequested()
        {
            if (selectedDefinition == null || database == null) return;
            service.RemoveDefinition(database, selectedDefinition);
            selectedDefinition = null;
            RefreshDefinitions();
        }

        /// <summary>确认后删除当前定义资产。</summary>
        private void OnDeleteRequested()
        {
            if (selectedDefinition == null || database == null) return;
            if (!EditorUtility.DisplayDialog("删除物品资产", $"确定将“{selectedDefinition.DisplayName}”移入 Unity 回收站吗？", "删除", "取消")) return;
            service.DeleteDefinition(database, selectedDefinition);
            selectedDefinition = null;
            RefreshDefinitions();
        }

        /// <summary>将当前分类默认值应用到单个定义。</summary>
        private void OnApplyDefaultsRequested()
        {
            try
            {
                service.ApplyCategoryDefaults(database, selectedDefinition);
                RefreshDefinitions();
                view.RefreshStatus("已将当前物品类型默认值应用到选中物品。");
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>执行数据库验证。</summary>
        private void OnValidateRequested()
        {
            try { view.RefreshStatus(service.ValidateDatabase(database)); }
            catch (Exception exception) { view.ShowError(exception.Message); }
        }

        /// <summary>在 Project 窗口定位当前资产。</summary>
        private void OnPingRequested()
        {
            if (selectedDefinition == null) return;
            EditorGUIUtility.PingObject(selectedDefinition);
            Selection.activeObject = selectedDefinition;
        }

        /// <summary>烘焙当前武器成长曲线并保存最终结果快照。</summary>
        private void OnBakeGrowthRequested()
        {
            if (selectedDefinition is not WeaponDefinition weapon) return;
            try
            {
                bakedResultService.Bake(weapon);
                view.RefreshDefinitionPresentation(weapon);
                view.RefreshStatus($"已烘焙 {weapon.GrowthProfile?.BakedProgressions.Count ?? 0} 个等级条目。");
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>烘焙当前圣遗物成长曲线并保存最终结果快照。</summary>
        private void OnBakeArtifactGrowthRequested()
        {
            if (selectedDefinition is not ArtifactDefinition artifact) return;
            try
            {
                bakedResultService.Bake(artifact);
                view.RefreshDefinitionPresentation(artifact);
                view.RefreshStatus($"已烘焙 {artifact.GrowthProfile?.BakedProgressions.Count ?? 0} 个圣遗物等级条目。");
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        /// <summary>在通用结果窗口打开当前武器成长数据源。</summary>
        private void OnViewBakedResultRequested()
        {
            if (selectedDefinition is IBakedResultDataSource source)
                BakedResultViewerWindow.Open(source);
        }

        /// <summary>在通用结果窗口打开当前圣遗物成长数据源。</summary>
        private void OnViewArtifactBakedResultRequested()
        {
            if (selectedDefinition is IBakedResultDataSource source)
                BakedResultViewerWindow.Open(source);
        }

        /// <summary>提交双击名称后的显示名修改。</summary>
        /// <param name="definition">物品定义。</param>
        /// <param name="displayName">新显示名。</param>
        private void OnRenameSubmitted(ItemDefinition definition, string displayName)
        {
            if (definition == null) return;
            if (undoRedoWriteSuppressed)
            {
                // Undo/Redo 完成前 TextField 可能补发旧的 delayed ChangeEvent；忽略它，等待统一刷新重置输入框。
                return;
            }
            try
            {
                // 在路径变化前读取稳定 GUID，避免 RenameAsset 后的短暂刷新窗口导致 Undo 目标映射丢失。
                string definitionAssetPath = AssetDatabase.GetAssetPath(definition);
                string definitionGuid = string.IsNullOrEmpty(definitionAssetPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(definitionAssetPath);
                int undoGroup = service.RenameDefinition(definition, displayName);
                if (!string.IsNullOrEmpty(definitionGuid)) renameUndoTargets[undoGroup] = definitionGuid;
                // 名称编辑已经先恢复了行结构；仍匹配筛选时只刷新展示，避免详情绑定和输入焦点被重建。
                if (MatchesFilter(definition))
                {
                    view.RefreshDefinitionPresentation(definition, false);
                    RefreshDefinitions(false, false);
                    view.RefreshStatus($"已将物品重命名为“{definition.DisplayName}”。");
                }
                else
                {
                    ScheduleRefreshDefinitions();
                }
            }
            catch (Exception exception)
            {
                // Service 已在失败时尽力回滚资产路径；这里重新读取当前对象，恢复右侧字段显示。
                view.RefreshDefinitionPresentation(definition, true);
                view.ShowError(exception.Message);
            }
        }

        #endregion

        #region 事件处理

        /// <summary>字段修改后刷新列表行和标题。</summary>
        /// <param name="definition">发生变化的定义。</param>
        private void OnPropertiesChanged(ItemDefinition definition)
        {
            if (definition != selectedDefinition) return;
            if (undoRedoWriteSuppressed)
            {
                // Undo/Redo 已经恢复数据，变化 Tracker 只能等待统一刷新，不能把恢复结果再次写回。
                ScheduleRefreshDefinitions();
                return;
            }
            ScheduleGrowthProfileSynchronization(definition);
            // 绑定事件只更新行、标题和烘焙表；不得在 SerializedPropertyChangeEvent 派发期间重新 Bind。
            if (MatchesFilter(definition))
            {
                view.RefreshDefinitionPresentation(definition, false);
                RefreshDefinitions(false, false);
                return;
            }

            ScheduleRefreshDefinitions();
        }

        /// <summary>响应预览 Sprite 变化并自动同步图集 Address 与 Sprite 名称。</summary>
        /// <param name="definition">发生变化的物品定义。</param>
        /// <param name="previewIcon">用户刚选择的预览 Sprite。</param>
        private void OnPreviewIconChanged(ItemDefinition definition, Sprite previewIcon)
        {
            if (definition == null || definition != selectedDefinition || disposed) return;
            if (undoRedoWriteSuppressed) return;
            try
            {
                string message = service.SynchronizeIconReference(definition, previewIcon);
                view.RefreshDefinitionPresentation(definition, false);
                view.RefreshStatus(message);
            }
            catch (Exception exception)
            {
                // 失败时 Service 已清空运行时图标引用；保留用户选择的预览图并刷新字段显示。
                view.RefreshDefinitionPresentation(definition, false);
                view.ShowError(exception.Message);
            }
        }

        /// <summary>把会影响筛选结果的刷新延迟到当前 UI Toolkit 事件完成后。</summary>
        private void ScheduleRefreshDefinitions()
        {
            if (refreshScheduled || disposed) return;
            refreshScheduled = true;
            EditorApplication.delayCall += ExecuteScheduledRefresh;
        }

        /// <summary>执行延迟筛选刷新并清理调度标记。</summary>
        private void ExecuteScheduledRefresh()
        {
            refreshScheduled = false;
            if (!disposed) RefreshDefinitions();
        }

        /// <summary>响应 Undo/Redo，并延迟到 Unity 完成对象恢复后处理当前目标。</summary>
        /// <param name="undoRedoInfo">Undo/Redo 操作信息。</param>
        private void OnUndoRedoEvent(in UndoRedoInfo undoRedoInfo)
        {
            if (disposed) return;
            undoRedoWriteSuppressed = true;
            bool isKnownRenameGroup = renameUndoTargets.ContainsKey(undoRedoInfo.undoGroup);
            bool isRenameEvent = string.Equals(undoRedoInfo.undoName, ItemConfigEditorService.RenameDefinitionUndoName, StringComparison.Ordinal);
            // 当前会话以 GUID 映射为权威；只有映射因窗口重建而完全丢失时，才允许按名称回退到当前选中对象。
            if (isKnownRenameGroup || (isRenameEvent && renameUndoTargets.Count == 0))
                pendingRenameUndoGroups.Add(undoRedoInfo.undoGroup);

            // 取消 Undo 之前排队的派生写回；这些写回属于被撤销操作，不能在撤销完成后再次创建 Undo。
            if (growthProfileSyncScheduled)
            {
                growthProfileSyncScheduled = false;
                EditorApplication.delayCall -= ExecuteGrowthProfileSynchronization;
            }
            EditorApplication.delayCall -= ExecuteScheduledRefresh;
            refreshScheduled = false;
            ScheduleUndoRedoRefresh();
        }

        /// <summary>响应资产变化并保持列表最新。</summary>
        private void OnProjectChanged()
        {
            // AssetDatabase.RenameAsset 会触发 projectChanged；这里只合并界面刷新，禁止递归发起名称同步。
            ScheduleRefreshDefinitions();
        }

        /// <summary>安排一次 Undo/Redo 完成后的单对象路径恢复和界面刷新。</summary>
        private void ScheduleUndoRedoRefresh()
        {
            if (undoRedoRefreshScheduled || disposed) return;
            undoRedoRefreshScheduled = true;
            EditorApplication.delayCall += ExecuteUndoRedoRefresh;
        }

        /// <summary>在 Undo/Redo 处理完成后恢复目标资产路径并刷新当前界面。</summary>
        private void ExecuteUndoRedoRefresh()
        {
            undoRedoRefreshScheduled = false;
            if (disposed) return;

            try
            {
                foreach (int undoGroup in pendingRenameUndoGroups)
                {
                    ItemDefinition target = ResolveRenameUndoTarget(undoGroup);
                    if (target != null) service.SynchronizeDefinitionAssetPathAfterUndo(target);
                }

                view.PrepareForUndoRedoRefresh();
                RefreshDefinitions();
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
                RefreshDefinitions();
            }
            finally
            {
                pendingRenameUndoGroups.Clear();
                EditorApplication.delayCall += FinishUndoRedoRefresh;
            }
        }

        /// <summary>在 Undo/Redo 刷新完成后的下一次编辑器回调中解除响应式写入抑制。</summary>
        private void FinishUndoRedoRefresh()
        {
            EditorApplication.delayCall -= FinishUndoRedoRefresh;
            if (!disposed) undoRedoWriteSuppressed = false;
        }

        /// <summary>按 Undo Group 解析当前会话中记录的单个重命名目标。</summary>
        /// <param name="undoGroup">Undo/Redo 事件的组号。</param>
        /// <returns>对应 Definition；映射不存在时返回当前选中 Definition。</returns>
        private ItemDefinition ResolveRenameUndoTarget(int undoGroup)
        {
            if (renameUndoTargets.TryGetValue(undoGroup, out string definitionGuid) && !string.IsNullOrEmpty(definitionGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(definitionGuid);
                ItemDefinition definition = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<ItemDefinition>(assetPath);
                if (definition != null) return definition;
                view.ShowError($"无法定位 Undo Group {undoGroup} 对应的物品定义，未执行其他资产修复。");
                return null;
            }

            // 窗口重建会丢失会话映射；按约定只尝试当前选中对象，不扫描其他 Definition。
            return selectedDefinition;
        }

        /// <summary>将武器最大等级变化延迟到当前 UI 事件结束后同步。</summary>
        /// <param name="definition">发生字段变化的定义。</param>
        private void ScheduleGrowthProfileSynchronization(ItemDefinition definition)
        {
            if (definition is not WeaponDefinition || growthProfileSyncScheduled || disposed) return;
            growthProfileSyncScheduled = true;
            EditorApplication.delayCall += ExecuteGrowthProfileSynchronization;
        }

        /// <summary>执行武器最大等级与成长 Profile 的同步。</summary>
        private void ExecuteGrowthProfileSynchronization()
        {
            growthProfileSyncScheduled = false;
            if (disposed || selectedDefinition is not WeaponDefinition weapon || weapon.GrowthProfile == null) return;
            try
            {
                if (service.SynchronizeGrowthProfileMaxLevel(weapon))
                {
                    view.RefreshDefinitionPresentation(weapon);
                    view.RefreshStatus("已将成长配置最大等级同步为武器最大等级；如曲线已变化，请重新烘焙。");
                }
            }
            catch (Exception exception)
            {
                view.ShowError(exception.Message);
            }
        }

        #endregion

        #region 内部辅助

        #endregion
    }
}
#endif
