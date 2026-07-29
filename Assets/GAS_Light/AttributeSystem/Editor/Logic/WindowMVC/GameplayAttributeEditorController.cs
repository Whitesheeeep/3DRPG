#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>协调 Attribute Editor View、Registry、Set、Undo、Bake 与 SessionState。</summary>
    public sealed class GameplayAttributeEditorController : IDisposable
    {
        #region 字段

        private readonly IGameplayAttributeEditorView view;
        private readonly GameplayAttributeEditorService service = new();
        private GameplayAttributeRegistry registry;
        private GameplayAttributeSet set;
        private GameplayAttributeEditorPage page;
        private string search = string.Empty;
        private string selectedSpecGuid = string.Empty;
        private int selectedDefinitionId = -1;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>注入 View、订阅用户意图并恢复 Session 状态。</summary>
        /// <param name="view">不暴露 UI Toolkit 类型的 Attribute View。</param>
        /// <exception cref="ArgumentNullException">view 为 null。</exception>
        public GameplayAttributeEditorController(IGameplayAttributeEditorView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            Subscribe();
            page = GameplayAttributeEditorSession.Page;
            search = GameplayAttributeEditorSession.Search;
            selectedSpecGuid = GameplayAttributeEditorSession.SelectedSpecGuid;
            selectedDefinitionId = GameplayAttributeEditorSession.SelectedDefinitionId;
            registry = GameplayAttributeEditorSession.ResolveSingleRegistry(out _);
            set = GameplayAttributeEditorSession.GetAttributeSet();
            RefreshAll();
        }

        /// <summary>注销全部 View 与 Undo 回调；重复调用安全。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Unsubscribe();
        }

        #endregion

        #region 公开状态操作

        /// <summary>切换 Registry 并恢复或清空 Spec 选择。</summary>
        /// <param name="value">新的 Registry。</param>
        /// <param name="restoreSelection">是否尝试恢复 Session 中的 Spec Guid。</param>
        public void SetRegistry(GameplayAttributeRegistry value, bool restoreSelection)
        {
            registry = value;
            GameplayAttributeEditorSession.SetRegistry(value);
            selectedSpecGuid = restoreSelection
                ? GameplayAttributeEditorSession.SelectedSpecGuid
                : string.Empty;
            RefreshAll();
        }

        /// <summary>切换 AttributeSet 并恢复或清空 Definition 选择。</summary>
        /// <param name="value">新的 AttributeSet。</param>
        /// <param name="restoreSelection">是否尝试恢复 Session 中的 AttributeId。</param>
        public void SetAttributeSet(GameplayAttributeSet value, bool restoreSelection)
        {
            set = value;
            GameplayAttributeEditorSession.SetAttributeSet(value);
            selectedDefinitionId = restoreSelection
                ? GameplayAttributeEditorSession.SelectedDefinitionId
                : -1;
            RefreshAll();
        }

        /// <summary>切换 Specs 或 Sets 子页面。</summary>
        /// <param name="value">目标子页面。</param>
        public void SelectPage(GameplayAttributeEditorPage value)
        {
            page = value;
            GameplayAttributeEditorSession.Page = value;
            RefreshAll();
        }

        #endregion

        #region 订阅

        // 对称订阅全部 View 意图和 Undo/Redo。
        private void Subscribe()
        {
            view.PageChanged += SelectPage;
            view.RegistryChanged += OnRegistryChanged;
            view.SetChanged += OnSetChanged;
            view.SearchChanged += OnSearchChanged;
            view.SpecSelectionChanged += OnSpecSelectionChanged;
            view.DefinitionSelectionChanged += OnDefinitionSelectionChanged;
            view.CreateSpecRequested += OnCreateSpecRequested;
            view.DeleteSpecRequested += OnDeleteSpecRequested;
            view.SpecNameSubmitted += OnSpecNameSubmitted;
            view.SpecDescriptionSubmitted += OnSpecDescriptionSubmitted;
            view.BakeRequested += OnBakeRequested;
            view.CreateSetRequested += OnCreateSetRequested;
            view.AddDefinitionRequested += OnAddDefinitionRequested;
            view.DeleteDefinitionRequested += OnDeleteDefinitionRequested;
            view.DefinitionSubmitted += OnDefinitionSubmitted;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        // 对称注销，防止主窗口选项卡切换后重复响应。
        private void Unsubscribe()
        {
            view.PageChanged -= SelectPage;
            view.RegistryChanged -= OnRegistryChanged;
            view.SetChanged -= OnSetChanged;
            view.SearchChanged -= OnSearchChanged;
            view.SpecSelectionChanged -= OnSpecSelectionChanged;
            view.DefinitionSelectionChanged -= OnDefinitionSelectionChanged;
            view.CreateSpecRequested -= OnCreateSpecRequested;
            view.DeleteSpecRequested -= OnDeleteSpecRequested;
            view.SpecNameSubmitted -= OnSpecNameSubmitted;
            view.SpecDescriptionSubmitted -= OnSpecDescriptionSubmitted;
            view.BakeRequested -= OnBakeRequested;
            view.CreateSetRequested -= OnCreateSetRequested;
            view.AddDefinitionRequested -= OnAddDefinitionRequested;
            view.DeleteDefinitionRequested -= OnDeleteDefinitionRequested;
            view.DefinitionSubmitted -= OnDefinitionSubmitted;
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        #endregion

        #region 用户意图处理

        // 用户 ObjectField 切换 Registry。
        private void OnRegistryChanged(GameplayAttributeRegistry value) => SetRegistry(value, false);

        // 用户 ObjectField 切换 Set。
        private void OnSetChanged(GameplayAttributeSet value) => SetAttributeSet(value, false);

        // 搜索仅改变 View 投影，不修改资产。
        private void OnSearchChanged(string value)
        {
            search = value ?? string.Empty;
            GameplayAttributeEditorSession.Search = search;
            RefreshAll();
        }

        // 保存并渲染当前 Spec 选择。
        private void OnSpecSelectionChanged(string guid)
        {
            selectedSpecGuid = guid ?? string.Empty;
            GameplayAttributeEditorSession.SelectedSpecGuid = selectedSpecGuid;
            RefreshSpecDetails();
        }

        // 保存并渲染当前 Definition 选择。
        private void OnDefinitionSelectionChanged(int attributeId)
        {
            selectedDefinitionId = attributeId;
            GameplayAttributeEditorSession.SelectedDefinitionId = attributeId;
            RefreshDefinitionDetails();
        }

        // 创建 Spec 后保持选中新节点。
        private void OnCreateSpecRequested()
        {
            GameplayAttributeEditorNode node = service.CreateSpec(registry);
            if (node == null)
            {
                view.ShowError("Create Attribute Spec", "请先选择 GameplayAttributeRegistry。");
                return;
            }

            selectedSpecGuid = node.Guid;
            GameplayAttributeEditorSession.SelectedSpecGuid = selectedSpecGuid;
            RefreshAll();
        }

        // 删除 Spec 前显示 Set 引用，并明确只删除全局身份。
        private void OnDeleteSpecRequested()
        {
            GameplayAttributeEditorNode node = FindSelectedSpec();
            if (node == null) return;
            registry.TryGetBakedAttribute(node.Guid, out GameplayAttribute attribute);
            List<string> references = service.FindSetReferences(attribute);
            string referenceText = references.Count == 0
                ? string.Empty
                : "\n\n以下 Set 将产生失效引用：\n" + string.Join("\n", references);
            if (!view.Confirm(
                    "Delete Attribute Spec",
                    $"删除全局 Attribute '{node.Name}'？下次 Bake 后 ID 永久废弃。{referenceText}"))
                return;

            service.DeleteSpec(registry, node);
            selectedSpecGuid = string.Empty;
            GameplayAttributeEditorSession.SelectedSpecGuid = string.Empty;
            RefreshAll();
        }

        // 校验并提交名称。
        private void OnSpecNameSubmitted(string name)
        {
            GameplayAttributeEditorNode node = FindSelectedSpec();
            if (!service.TryRenameSpec(registry, node, name, out string error))
                view.ShowError("Rename Attribute Spec", error);
            RefreshAll();
        }

        // 提交说明；说明不影响 Bake。
        private void OnSpecDescriptionSubmitted(string description)
        {
            service.SetSpecDescription(registry, FindSelectedSpec(), description);
            RefreshSpecDetails();
        }

        // Bake 只处理 Specs ID 与生成代码，不处理 Set Definition。
        private void OnBakeRequested()
        {
            bool success = GameplayAttributeBaker.TryBake(registry, out string message);
            view.ShowResult(success ? "Bake Complete" : "Bake Failed", message);
            RefreshAll();
        }

        // 创建基础 Set 资产并自动切换 Sets 页面。
        private void OnCreateSetRequested()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Gameplay Attribute Set",
                "GameplayAttributeSet",
                "asset",
                "选择 AttributeSet 资产保存位置。");
            if (string.IsNullOrEmpty(path)) return;
            GameplayAttributeSet created = service.CreateSetAsset(path);
            if (created == null)
            {
                view.ShowError("Create Attribute Set", "创建 AttributeSet 失败。");
                return;
            }

            set = created;
            GameplayAttributeEditorSession.SetAttributeSet(created);
            SelectPage(GameplayAttributeEditorPage.Sets);
        }

        // 使用第一个未被当前 Set 使用的已烘焙 Attribute 创建 Definition。
        private void OnAddDefinitionRequested(GameplayAttributeType type)
        {
            List<GameplayAttributeEditorNode> nodes = BuildBakedNodes();
            GameplayAttributeEditorNode selectedNode = nodes.Find(candidate =>
                registry.TryGetBakedAttribute(candidate.Guid, out GameplayAttribute candidateAttribute) &&
                GameplayAttributeEditorService.FindDefinitionIndex(
                    set,
                    candidateAttribute.Id) < 0);
            if (selectedNode == null ||
                !registry.TryGetBakedAttribute(selectedNode.Guid, out GameplayAttribute attribute))
            {
                view.ShowError("Add Attribute Definition", "没有可添加的已烘焙 Attribute。");
                return;
            }

            if (!service.AddDefinition(set, attribute, type, out string error))
            {
                view.ShowError("Add Attribute Definition", error);
                return;
            }

            selectedDefinitionId = attribute.Id;
            GameplayAttributeEditorSession.SelectedDefinitionId = selectedDefinitionId;
            RefreshAll();
        }

        // 删除只影响当前 Set Definition，不删除全局 Spec。
        private void OnDeleteDefinitionRequested()
        {
            if (set == null || selectedDefinitionId < 0) return;
            string name = ResolveAttributeName(selectedDefinitionId);
            if (!view.Confirm(
                    "Delete Attribute Definition",
                    $"从 Set '{set.name}' 移除 '{name}'？"))
                return;

            service.DeleteDefinition(set, selectedDefinitionId);
            selectedDefinitionId = -1;
            GameplayAttributeEditorSession.SelectedDefinitionId = -1;
            RefreshAll();
        }

        // 整体提交 Definition，AttributeId 变化后同步选择。
        private void OnDefinitionSubmitted(GameplayAttributeDefinitionEditRequest request)
        {
            if (!service.TryUpdateDefinition(set, request, out string error))
            {
                view.ShowError("Edit Attribute Definition", error);
                RefreshDefinitionDetails();
                return;
            }

            selectedDefinitionId = request.Attribute.Id;
            GameplayAttributeEditorSession.SelectedDefinitionId = selectedDefinitionId;
            RefreshAll();
        }

        // Undo/Redo 后按 Guid 与 AttributeId 重新定位，不保留失效对象引用。
        private void OnUndoRedo()
        {
            registry = GameplayAttributeEditorSession.GetRegistry();
            set = GameplayAttributeEditorSession.GetAttributeSet();
            RefreshAll();
        }

        #endregion

        #region 刷新

        // 同步全部控件状态并重新构建当前只读投影。
        private void RefreshAll()
        {
            if (disposed) return;
            view.SetPage(page);
            view.SetRegistry(registry);
            view.SetAttributeSet(set);
            view.SetSearch(search);
            RefreshSpecs();
            RefreshDefinitions();
            RefreshStatus();
        }

        // 构建按名称排序且受搜索过滤的 Spec Model 引用列表。
        private void RefreshSpecs()
        {
            var items = new List<GameplayAttributeEditorNode>();
            if (registry != null)
                for (int i = 0; i < registry.Nodes.Count; i++)
                {
                    GameplayAttributeEditorNode node = registry.Nodes[i];
                    if (node != null && MatchesSearch(node.Name)) items.Add(node);
                }

            items.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            if (!items.Any(item => item.Guid == selectedSpecGuid)) selectedSpecGuid = string.Empty;
            view.RenderSpecs(items, selectedSpecGuid);
            RefreshSpecDetails();
        }

        // 将当前 Spec Model 直接交给 View；null 表示没有选择。
        private void RefreshSpecDetails() => view.RenderSpecDetails(FindSelectedSpec());

        // 构建当前 Set 的 Definition Model 引用列表；搜索与排序仍由 Controller 协调。
        private void RefreshDefinitions()
        {
            var items = new List<GameplayAttributeDefinition>();
            if (set != null)
                for (int i = 0; i < set.Definitions.Count; i++)
                {
                    GameplayAttributeDefinition definition = set.Definitions[i];
                    if (definition == null) continue;
                    string name = ResolveAttributeName(definition.Attribute.Id);
                    if (MatchesSearch(name)) items.Add(definition);
                }

            items.Sort((left, right) => string.Compare(
                ResolveAttributeName(left.Attribute.Id),
                ResolveAttributeName(right.Attribute.Id),
                StringComparison.Ordinal));
            if (!items.Any(item => item.Attribute.Id == selectedDefinitionId))
                selectedDefinitionId = -1;
            view.RenderDefinitions(items, selectedDefinitionId);
            RefreshDefinitionDetails();
        }

        // 将 Definition Model 与已烘焙 Editor Node 直接交给 View。
        private void RefreshDefinitionDetails() =>
            view.RenderDefinitionDetails(FindSelectedDefinition(), BuildBakedNodes());

        // 汇总 Registry 和当前 Set 错误，以单一状态区域反馈。
        private void RefreshStatus()
        {
            var messages = new List<string>();
            bool error = false;
            if (registry == null)
            {
                messages.Add("请选择或创建 GameplayAttributeRegistry。");
                error = true;
            }
            else
            {
                if (registry.BakeDirty) messages.Add("Attribute Specs 已修改，需要 Bake。");
                List<string> registryErrors = GameplayAttributeBaker.ValidateRegistry(registry);
                List<string> setErrors = service.ValidateSet(set, registry);
                if (registryErrors.Count > 0 || setErrors.Count > 0) error = true;
                messages.AddRange(registryErrors);
                messages.AddRange(setErrors);
            }

            view.RenderStatus(string.Join("\n", messages), error);
        }

        #endregion

        #region 查询辅助

        // 按 Session Guid 重新查询当前 Spec。
        private GameplayAttributeEditorNode FindSelectedSpec()
        {
            if (registry == null || string.IsNullOrEmpty(selectedSpecGuid)) return null;
            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode node = registry.Nodes[i];
                if (node != null && node.Guid == selectedSpecGuid) return node;
            }

            return null;
        }

        // 按 AttributeId 线性查询当前 Set Definition。
        private GameplayAttributeDefinition FindSelectedDefinition()
        {
            int index = GameplayAttributeEditorService.FindDefinitionIndex(set, selectedDefinitionId);
            return index >= 0 ? set.Definitions[index] : null;
        }

        // 构建全部已烘焙 Spec 的 Model 引用列表，供添加操作和 View 下拉框复用。
        private List<GameplayAttributeEditorNode> BuildBakedNodes()
        {
            var result = new List<GameplayAttributeEditorNode>();
            if (registry == null) return result;
            for (int i = 0; i < registry.Nodes.Count; i++)
            {
                GameplayAttributeEditorNode node = registry.Nodes[i];
                if (node != null && registry.TryGetBakedAttribute(node.Guid, out _))
                    result.Add(node);
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
            return result;
        }

        // 使用 Registry 解析当前名称，失效 ID 保留明确诊断文本。
        private string ResolveAttributeName(int id) =>
            registry != null && registry.TryGetNodeById(id, out GameplayAttributeEditorNode node)
                ? node.Name
                : $"Invalid AttributeId ({id})";

        // 使用 OrdinalIgnoreCase 过滤平铺名称。
        private bool MatchesSearch(string value) =>
            string.IsNullOrEmpty(search) ||
            (!string.IsNullOrEmpty(value) &&
             value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        #endregion
    }
}
#endif
