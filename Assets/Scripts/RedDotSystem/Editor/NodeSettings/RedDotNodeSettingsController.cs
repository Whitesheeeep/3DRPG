#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>
    /// 协调 RedDotConfig 节点设置页的 Asset 编辑、树校验、Undo 和删除流程。
    /// </summary>
    internal sealed class RedDotNodeSettingsController : IDisposable
    {
        #region 依赖字段

        private readonly RedDotNodeSettingsView view;
        private readonly RedDotEditorSettings editorSettings;

        #endregion

        #region 状态字段

        private RedDotConfig currentConfig;
        private string lastValidNodeFolder;
        private bool disposed;
        private readonly HashSet<RedDotKey> assetNameManagedKeySet = new HashSet<RedDotKey>();

        #endregion

        #region 生命周期

        /// <summary>创建节点设置 Controller，绑定 View 并读取默认 Config。</summary>
        /// <param name="view">节点设置 View。</param>
        public RedDotNodeSettingsController(RedDotNodeSettingsView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            editorSettings = RedDotEditorSettings.instance;
            EnsureNodeFolderSetting();
            BindViewEvents();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            EditorApplication.projectChanged += OnProjectChanged;

            currentConfig = FindDefaultConfig();
            lastValidNodeFolder = editorSettings.NewNodeAssetFolder;
            view.SetEditorState(currentConfig, editorSettings.NewNodeAssetFolder);
            Debug.Log(
                $"[RedDotNodeSettings] 已创建，config={(currentConfig == null ? "None" : currentConfig.name)}。");
        }

        /// <summary>解除 View、Undo 和项目变化回调。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.projectChanged -= OnProjectChanged;
            UnbindViewEvents();
        }

        #endregion

        #region Config 与目录

        /// <summary>切换当前编辑 Config 并刷新节点树。</summary>
        /// <param name="config">新的编辑 Config。</param>
        private void OnConfigChanged(RedDotConfig config)
        {
            currentConfig = config;
            view.Render(currentConfig);
            view.ShowStatus(
                currentConfig == null
                    ? "请选择一个 RedDotConfig。"
                    : $"已切换到 Config：{currentConfig.name}",
                false);
        }

        /// <summary>验证并保存新建节点目录。</summary>
        /// <param name="folderPath">用户输入的目录路径。</param>
        private void OnNodeFolderChanged(string folderPath)
        {
            try
            {
                editorSettings.SetNewNodeAssetFolder(folderPath);
                editorSettings.SaveSettings();
                lastValidNodeFolder = editorSettings.NewNodeAssetFolder;
                view.SetNodeFolder(editorSettings.NewNodeAssetFolder);
                view.ShowStatus($"新建节点目录：{editorSettings.NewNodeAssetFolder}", false);
            }
            catch (ArgumentException exception)
            {
                view.SetNodeFolder(lastValidNodeFolder);
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning(exception.Message);
            }
        }

        /// <summary>校正历史目录值，确保属性绑定开始时已有合法 Assets 路径。</summary>
        private void EnsureNodeFolderSetting()
        {
            string currentPath = editorSettings.NewNodeAssetFolder;
            try
            {
                editorSettings.SetNewNodeAssetFolder(currentPath);
            }
            catch (ArgumentException)
            {
                editorSettings.SetNewNodeAssetFolder(RedDotEditorSettings.DefaultNodeAssetFolder);
                Debug.LogWarning(
                    $"[RedDotNodeSettings] 已将无效节点 Asset 目录恢复为默认值：{RedDotEditorSettings.DefaultNodeAssetFolder}。");
            }
        }

        /// <summary>在当前输入路径下创建缺失的 Unity 文件夹。</summary>
        private void OnCreateFolderRequested()
        {
            string folderPath = RedDotEditorSettings.NormalizeAssetFolder(
                editorSettings.NewNodeAssetFolder);
            if (!RedDotEditorSettings.IsValidAssetFolder(folderPath))
            {
                view.ShowStatus("当前节点 Asset 目录格式无效。", true);
                return;
            }

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                view.ShowStatus($"目录已经存在：{folderPath}", false);
                return;
            }

            CreateUnityFolderRecursive(folderPath);
            AssetDatabase.SaveAssets();
            view.ShowStatus($"已创建目录：{folderPath}", false);
            Debug.Log($"[RedDotNodeSettings] 创建节点 Asset 目录，path={folderPath}。");
        }

        /// <summary>递归创建 Assets 下的 Unity 文件夹。</summary>
        /// <param name="folderPath">项目相对目录。</param>
        private static void CreateUnityFolderRecursive(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            int separatorIndex = folderPath.LastIndexOf('/');
            string parentPath = separatorIndex < 0
                ? string.Empty
                : folderPath.Substring(0, separatorIndex);
            string folderName = separatorIndex < 0
                ? folderPath
                : folderPath.Substring(separatorIndex + 1);

            if (!string.IsNullOrEmpty(parentPath))
            {
                CreateUnityFolderRecursive(parentPath);
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(parentPath, folderName);
            }
        }

        /// <summary>重新读取当前 Config，响应外部 Asset 修改。</summary>
        private void OnRefreshConfigRequested()
        {
            view.Render(currentConfig);
            view.ShowStatus("已刷新 RedDotConfig 节点树。", false);
        }

        /// <summary>创建一个新的空 RedDotConfig Asset。</summary>
        private void OnCreateConfigRequested()
        {
            string defaultFolder = "Assets/Scripts/RedDotSystem/Runtime/Config/Assets";
            string path = EditorUtility.SaveFilePanelInProject(
                "新建 RedDotConfig",
                "RedDotConfig",
                "asset",
                "选择 RedDotConfig 保存位置",
                defaultFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var config = ScriptableObject.CreateInstance<RedDotConfig>();
            AssetDatabase.CreateAsset(config, path);
            Undo.RegisterCreatedObjectUndo(config, "创建 RedDotConfig");
            AssetDatabase.SaveAssets();
            currentConfig = config;
            view.SetEditorState(currentConfig, editorSettings.NewNodeAssetFolder);
            view.ShowStatus($"已创建 Config：{config.name}", false);
            Debug.Log($"[RedDotNodeSettings] 创建 RedDotConfig，path={path}。");
        }

        /// <summary>查找项目中的默认 Config；多个时选择排序后的第一个。</summary>
        /// <returns>可用 Config；不存在时返回 null。</returns>
        private static RedDotConfig FindDefaultConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:RedDotConfig");
            Array.Sort(guids, StringComparer.Ordinal);
            for (int index = 0; index < guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                RedDotConfig config = AssetDatabase.LoadAssetAtPath<RedDotConfig>(path);
                if (config != null)
                {
                    return config;
                }
            }

            return null;
        }

        #endregion

        #region 节点创建与编辑

        /// <summary>创建选中节点的子节点或一个新的根节点。</summary>
        /// <param name="segmentName">新节点名称。</param>
        /// <param name="parent">选中父节点；为空时创建根节点。</param>
        private void OnCreateNodeRequested(string segmentName, RedDotKey parent)
        {
            if (currentConfig == null)
            {
                view.ShowStatus("请先选择 RedDotConfig。", true);
                return;
            }

            try
            {
                ValidateSegmentName(segmentName);
                if (parent != null && !currentConfig.Contains(parent))
                {
                    throw new InvalidOperationException("新节点的 Parent 不属于当前 RedDotConfig。");
                }

                string folderPath = RedDotEditorSettings.NormalizeAssetFolder(
                    editorSettings.NewNodeAssetFolder);
                if (!RedDotEditorSettings.IsValidAssetFolder(folderPath) ||
                    !AssetDatabase.IsValidFolder(folderPath))
                {
                    throw new InvalidOperationException(
                        $"新建节点目录不存在：{folderPath}。请先选择或创建目录。");
                }

                ValidateSiblingName(segmentName, parent, null);
                int siblingOrder = GetSiblingKeys(parent).Count;
                // 文件名体现节点当前路径；后续父级变化会沿相同规则同步整个子树。
                string derivedPath = BuildDerivedPath(parent, segmentName);
                string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folderPath}/{GetAssetNameStem(derivedPath)}.asset");

                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("创建红点节点");
                var key = ScriptableObject.CreateInstance<RedDotKey>();
                AssetDatabase.CreateAsset(key, assetPath);
                Undo.RegisterCreatedObjectUndo(key, "创建 RedDotKey");
                SetKeySerializedValues(key, segmentName, parent, siblingOrder, "创建 RedDotKey");
                AddKeyToCurrentConfig(key);
                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssets();
                view.SetNewNodeName("New");
                view.Render(currentConfig);
                view.SelectNode(key);
                view.FocusSegmentNameField();
                view.ShowStatus($"已创建节点：{key.DerivedPath}", false);
                Debug.Log($"[RedDotNodeSettings] 创建 RedDotKey，path={assetPath}，parent={(parent == null ? "<root>" : parent.name)}。");
            }
            catch (ArgumentException exception)
            {
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning(exception.Message);
            }
        }

        /// <summary>提交节点 SegmentName 的修改。</summary>
        /// <param name="key">待改名节点。</param>
        /// <param name="segmentName">新的层级名称。</param>
        private void OnSegmentNameSubmitted(RedDotKey key, string segmentName)
        {
            if (key == null || currentConfig == null || !currentConfig.Contains(key))
            {
                return;
            }

            try
            {
                ValidateSegmentName(segmentName);
                if (string.Equals(key.SegmentName, segmentName, StringComparison.Ordinal))
                {
                    return;
                }

                ValidateSiblingName(segmentName, key.Parent, key);
                string nextDerivedPath = BuildDerivedPath(key.Parent, segmentName);
                List<AssetRenameOperation> renameOperations = BuildSubtreeAssetRenameOperations(
                    key,
                    nextDerivedPath);
                // 先完成文件名事务；AssetDatabase 报错时，节点序列化字段仍保持原值。
                ExecuteAssetRenames(renameOperations);

                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("修改红点节点名称");
                try
                {
                    SetKeySerializedValues(key, segmentName, key.Parent, key.SiblingOrder, "修改红点节点名称");
                    Undo.CollapseUndoOperations(undoGroup);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception exception)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    RollbackAssetRenames(renameOperations, renameOperations.Count);
                    throw new InvalidOperationException(
                        $"节点名称写入失败，已恢复 Asset 文件名。reason={exception.Message}",
                        exception);
                }

                assetNameManagedKeySet.UnionWith(CollectSubtree(key));
                view.Render(currentConfig);
                view.SelectNode(key);
                view.ShowStatus($"已改名：{key.DerivedPath}", false);
                Debug.Log(
                    $"[RedDotNodeSettings] 修改节点名称并同步子树 Asset 文件名，key={key.name}，segmentName={segmentName}，renamedCount={renameOperations.Count}。");
            }
            catch (ArgumentException exception)
            {
                view.Render(currentConfig);
                view.SelectNode(key);
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                view.Render(currentConfig);
                view.SelectNode(key);
                view.ShowStatus(exception.Message, true);
                Debug.LogWarning(exception.Message);
            }
        }

        /// <summary>通过 Parent ObjectField 迁移节点到新父级。</summary>
        /// <param name="key">待迁移节点。</param>
        /// <param name="newParent">新的父节点；为空时迁移到根。</param>
        private void OnParentChangedRequested(RedDotKey key, RedDotKey newParent)
        {
            MoveNode(key, newParent, NodeDropPlacement.AppendAsChild, null);
        }

        /// <summary>解析并提交详情页派生路径输入，将目标路径节点作为新的 Parent。</summary>
        /// <param name="key">当前选中的节点 Asset。</param>
        /// <param name="rawPath">用户输入的目标父节点派生路径。</param>
        private void OnDerivedParentPathSubmitted(RedDotKey key, string rawPath)
        {
            if (key == null || currentConfig == null || !currentConfig.Contains(key))
            {
                return;
            }

            try
            {
                RedDotKey targetParent = ResolveTargetParentPath(rawPath);
                MoveNode(
                    key,
                    targetParent,
                    NodeDropPlacement.AppendAsChild,
                    null,
                    true);
            }
            catch (ArgumentException exception)
            {
                ShowDerivedPathFailure(key, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                ShowDerivedPathFailure(key, exception.Message);
            }
        }

        /// <summary>展示派生路径迁移失败原因，并恢复当前节点的真实路径。</summary>
        /// <param name="key">发生错误的节点 Asset。</param>
        /// <param name="message">校验失败原因。</param>
        private void ShowDerivedPathFailure(RedDotKey key, string message)
        {
            view.Render(currentConfig);
            view.SelectNode(key);
            view.ShowDerivedPathError(message);
            view.ShowStatus(message, true);
            Debug.LogWarning($"[RedDotNodeSettings] 派生路径迁移失败，key={key.name}，reason={message}");
        }

        /// <summary>按当前 Config 的完整派生路径精确解析目标父节点。</summary>
        /// <param name="rawPath">用户输入的路径。</param>
        /// <returns>路径对应的目标父节点 Asset。</returns>
        private RedDotKey ResolveTargetParentPath(string rawPath)
        {
            string path = rawPath?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException(
                    "目标父节点路径不能为空；迁移到根节点请清空 Parent 或拖到 TreeView 根区域。",
                    nameof(rawPath));
            }

            if (path.Contains("\\", StringComparison.Ordinal) ||
                path.StartsWith("/", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.Contains("//", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "目标父节点路径必须使用非空的 / 分段，不能包含反斜杠、首尾 / 或连续 //。",
                    nameof(rawPath));
            }

            string[] segments = path.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                ValidateSegmentName(segments[index]);
            }

            RedDotKey resolvedKey = null;
            for (int index = 0; index < currentConfig.NodeKeys.Count; index++)
            {
                RedDotKey candidate = currentConfig.NodeKeys[index];
                if (candidate == null ||
                    !string.Equals(candidate.DerivedPath, path, StringComparison.Ordinal))
                {
                    continue;
                }

                if (resolvedKey != null)
                {
                    throw new InvalidOperationException(
                        $"当前 Config 中存在重复目标路径：{path}，请先修复节点配置。");
                }

                resolvedKey = candidate;
            }

            if (resolvedKey == null)
            {
                throw new InvalidOperationException(
                    $"当前 Config 中不存在目标父节点路径：{path}。不会自动创建缺失节点。");
            }

            return resolvedKey;
        }

        /// <summary>处理 TreeView 拖拽产生的父级和顺序迁移。</summary>
        /// <param name="key">被拖节点。</param>
        /// <param name="target">落点节点；根区域时为空。</param>
        /// <param name="placement">落点语义。</param>
        private void OnNodeDropRequested(
            RedDotKey key,
            RedDotKey target,
            NodeDropPlacement placement)
        {
            MoveNode(key, target, placement, target);
        }

        /// <summary>处理工具栏或右键菜单发出的同级上移、下移请求。</summary>
        /// <param name="key">待移动节点。</param>
        /// <param name="delta">同级位置变化量，-1 表示上移，1 表示下移。</param>
        private void OnSiblingMoveRequested(RedDotKey key, int delta)
        {
            if (key == null || currentConfig == null || !currentConfig.Contains(key))
            {
                return;
            }

            if (delta != -1 && delta != 1)
            {
                view.ShowStatus("节点同级移动方向无效。", true);
                Debug.LogWarning("[RedDotNodeSettings] 收到无效的同级移动方向。");
                return;
            }

            List<RedDotKey> siblings = GetSiblingKeys(key.Parent);
            int currentIndex = siblings.IndexOf(key);
            int targetIndex = currentIndex + delta;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= siblings.Count)
            {
                view.ShowStatus(
                    delta < 0 ? "当前节点已经是同级第一个节点。" : "当前节点已经是同级最后一个节点。",
                    true);
                return;
            }

            RedDotKey target = siblings[targetIndex];
            MoveNode(
                key,
                target,
                delta < 0 ? NodeDropPlacement.Before : NodeDropPlacement.After,
                target);
        }

        /// <summary>
        /// 执行一次包含 Parent 和同级顺序更新的节点迁移事务。
        /// </summary>
        /// <param name="key">被迁移节点。</param>
        /// <param name="targetParentOrNode">根据 placement 解释的目标节点。</param>
        /// <param name="placement">迁移落点语义。</param>
        /// <param name="targetNode">Before/After 时的相邻节点。</param>
        private void MoveNode(
            RedDotKey key,
            RedDotKey targetParentOrNode,
            NodeDropPlacement placement,
            RedDotKey targetNode,
            bool showDerivedPathError = false)
        {
            if (key == null || currentConfig == null || !currentConfig.Contains(key))
            {
                return;
            }

            try
            {
                RedDotKey newParent = ResolveNewParent(targetParentOrNode, placement);
                if (newParent != null && !currentConfig.Contains(newParent))
                {
                    throw new InvalidOperationException("目标 Parent 不属于当前 RedDotConfig。");
                }

                if (newParent == key || IsDescendant(newParent, key))
                {
                    throw new InvalidOperationException("不能把节点移动到自身或自己的后代下。");
                }

                ValidateSiblingName(key.SegmentName, newParent, key);
                List<RedDotKey> oldSiblings = GetSiblingKeys(key.Parent);
                List<RedDotKey> newSiblings = GetSiblingKeys(newParent);
                oldSiblings.Remove(key);
                newSiblings.Remove(key);

                int insertionIndex = ResolveInsertionIndex(
                    newSiblings,
                    targetNode,
                    placement);
                if (placement == NodeDropPlacement.AppendAsChild ||
                    placement == NodeDropPlacement.AppendToRoot)
                {
                    insertionIndex = newSiblings.Count;
                }

                newSiblings.Insert(insertionIndex, key);
                var affectedKeys = new HashSet<RedDotKey>(oldSiblings);
                affectedKeys.UnionWith(newSiblings);
                affectedKeys.Add(key);
                string previousDerivedPath = key.DerivedPath;
                string nextDerivedPath = BuildDerivedPath(newParent, key.SegmentName);
                List<RedDotKey> movedSubtreeKeys = CollectSubtree(key);
                List<AssetRenameOperation> renameOperations = string.Equals(
                    previousDerivedPath,
                    nextDerivedPath,
                    StringComparison.Ordinal)
                    ? new List<AssetRenameOperation>()
                    : BuildSubtreeAssetRenameOperations(key, nextDerivedPath);
                // 同级排序不会改变派生路径，因此只在路径变化时改名子树 Asset。
                ExecuteAssetRenames(renameOperations);

                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("迁移红点节点");
                try
                {
                    Undo.RecordObjects(affectedKeys.Cast<UnityEngine.Object>().ToArray(), "迁移红点节点");
                    SetParentSerializedValue(key, newParent);
                    ApplySiblingOrders(oldSiblings);
                    ApplySiblingOrders(newSiblings);
                    Undo.CollapseUndoOperations(undoGroup);
                    AssetDatabase.SaveAssets();
                }
                catch (Exception exception)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    RollbackAssetRenames(renameOperations, renameOperations.Count);
                    throw new InvalidOperationException(
                        $"节点迁移写入失败，已恢复 Asset 文件名。reason={exception.Message}",
                        exception);
                }

                if (renameOperations.Count > 0)
                {
                    assetNameManagedKeySet.UnionWith(movedSubtreeKeys);
                }

                view.Render(currentConfig);
                view.SelectNode(key);
                view.ShowStatus($"已迁移节点：{key.DerivedPath}", false);
                Debug.Log(
                    $"[RedDotNodeSettings] 迁移节点并同步子树 Asset 文件名，key={key.name}，parent={(newParent == null ? "<root>" : newParent.name)}，renamedCount={renameOperations.Count}。");
            }
            catch (InvalidOperationException exception)
            {
                // Parent ObjectField 可能已经先更新了视觉值；失败时重新读取 Asset，避免字段与树结构不一致。
                view.Render(currentConfig);
                view.SelectNode(key);
                view.ShowStatus(exception.Message, true);
                if (showDerivedPathError)
                {
                    view.ShowDerivedPathError(exception.Message);
                }
                Debug.LogWarning(exception.Message);
            }
        }

        /// <summary>根据拖拽语义解析新的 Parent。</summary>
        /// <param name="target">落点节点。</param>
        /// <param name="placement">落点语义。</param>
        /// <returns>新的 Parent；根区域时为空。</returns>
        private static RedDotKey ResolveNewParent(
            RedDotKey target,
            NodeDropPlacement placement)
        {
            return placement == NodeDropPlacement.AppendAsChild
                ? target
                : placement == NodeDropPlacement.AppendToRoot
                    ? null
                    : target?.Parent;
        }

        /// <summary>取得同级列表中 Before/After 的插入位置。</summary>
        /// <param name="siblings">已移除源节点的同级列表。</param>
        /// <param name="target">相邻目标节点。</param>
        /// <param name="placement">Before 或 After 语义。</param>
        /// <returns>插入索引。</returns>
        private static int ResolveInsertionIndex(
            IReadOnlyList<RedDotKey> siblings,
            RedDotKey target,
            NodeDropPlacement placement)
        {
            if (target == null ||
                (placement != NodeDropPlacement.Before && placement != NodeDropPlacement.After))
            {
                return siblings.Count;
            }

            int targetIndex = -1;
            for (int index = 0; index < siblings.Count; index++)
            {
                if (ReferenceEquals(siblings[index], target))
                {
                    targetIndex = index;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                throw new InvalidOperationException("拖拽目标已经不在当前 Config 中，请刷新节点树。");
            }

            return placement == NodeDropPlacement.Before
                ? targetIndex
                : targetIndex + 1;
        }

        /// <summary>判断 candidate 是否位于 ancestor 的后代链中。</summary>
        /// <param name="candidate">待检查节点。</param>
        /// <param name="ancestor">祖先节点。</param>
        /// <returns>candidate 是 ancestor 后代时返回 true。</returns>
        private static bool IsDescendant(RedDotKey candidate, RedDotKey ancestor)
        {
            RedDotKey current = candidate;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        /// <summary>获取指定父级下按 SiblingOrder 排序的节点列表。</summary>
        /// <param name="parent">父节点；为空时获取根节点。</param>
        /// <returns>同级节点列表。</returns>
        private List<RedDotKey> GetSiblingKeys(RedDotKey parent)
        {
            return currentConfig.NodeKeys
                .Where(key => key != null && ReferenceEquals(key.Parent, parent))
                .OrderBy(key => key.SiblingOrder)
                .ThenBy(GetConfigOrder)
                .ToList();
        }

        /// <summary>获取节点在当前 Config 清单中的稳定索引。</summary>
        /// <param name="key">待查询节点。</param>
        /// <returns>清单索引；找不到时返回最大整数。</returns>
        private int GetConfigOrder(RedDotKey key)
        {
            for (int index = 0; index < currentConfig.NodeKeys.Count; index++)
            {
                if (ReferenceEquals(currentConfig.NodeKeys[index], key))
                {
                    return index;
                }
            }

            return int.MaxValue;
        }

        /// <summary>获取节点的全部直接和间接后代。</summary>
        /// <param name="root">子树根节点。</param>
        /// <returns>包含 root 的深度优先节点列表。</returns>
        private List<RedDotKey> CollectSubtree(RedDotKey root)
        {
            var result = new List<RedDotKey>();
            var pending = new Stack<RedDotKey>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                RedDotKey current = pending.Pop();
                result.Add(current);
                List<RedDotKey> children = GetSiblingKeys(current);
                for (int index = children.Count - 1; index >= 0; index--)
                {
                    pending.Push(children[index]);
                }
            }

            return result;
        }

        /// <summary>校验节点名称为单一合法层级分段。</summary>
        /// <param name="segmentName">待校验名称。</param>
        private static void ValidateSegmentName(string segmentName)
        {
            if (string.IsNullOrWhiteSpace(segmentName) ||
                !string.Equals(segmentName, segmentName.Trim(), StringComparison.Ordinal) ||
                segmentName.Contains("/", StringComparison.Ordinal) ||
                segmentName.Contains("\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "红点节点名称不能为空、不能包含首尾空白或路径分隔符。",
                    nameof(segmentName));
            }
        }

        /// <summary>校验同一父级下不存在重复 SegmentName。</summary>
        /// <param name="segmentName">待校验名称。</param>
        /// <param name="parent">目标父节点。</param>
        /// <param name="ignoreKey">改名或移动时忽略的原节点。</param>
        private void ValidateSiblingName(
            string segmentName,
            RedDotKey parent,
            RedDotKey ignoreKey)
        {
            ValidateSegmentName(segmentName);
            List<RedDotKey> siblings = GetSiblingKeys(parent);
            for (int index = 0; index < siblings.Count; index++)
            {
                RedDotKey sibling = siblings[index];
                if (!ReferenceEquals(sibling, ignoreKey) &&
                    string.Equals(sibling.SegmentName, segmentName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"同一父节点下已经存在名称：{segmentName}。");
                }
            }
        }

        /// <summary>更新一个节点的序列化字段并记录 Undo。</summary>
        /// <param name="key">待更新节点。</param>
        /// <param name="segmentName">名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="siblingOrder">同级顺序。</param>
        /// <param name="undoName">Undo 操作名称。</param>
        private static void SetKeySerializedValues(
            RedDotKey key,
            string segmentName,
            RedDotKey parent,
            int siblingOrder,
            string undoName)
        {
            Undo.RecordObject(key, undoName);
            SerializedObject serializedKey = new SerializedObject(key);
            serializedKey.Update();
            serializedKey.FindProperty("segmentName").stringValue = segmentName;
            serializedKey.FindProperty("parent").objectReferenceValue = parent;
            serializedKey.FindProperty("siblingOrder").intValue = siblingOrder;
            serializedKey.ApplyModifiedProperties();
            EditorUtility.SetDirty(key);
        }

        /// <summary>仅更新节点的 Parent 字段。</summary>
        /// <param name="key">待更新节点。</param>
        /// <param name="parent">新的父节点。</param>
        private static void SetParentSerializedValue(RedDotKey key, RedDotKey parent)
        {
            SerializedObject serializedKey = new SerializedObject(key);
            serializedKey.Update();
            serializedKey.FindProperty("parent").objectReferenceValue = parent;
            serializedKey.ApplyModifiedProperties();
            EditorUtility.SetDirty(key);
        }

        /// <summary>将同级节点顺序重写为连续整数。</summary>
        /// <param name="siblings">已按目标顺序排列的同级节点。</param>
        private static void ApplySiblingOrders(IReadOnlyList<RedDotKey> siblings)
        {
            for (int index = 0; index < siblings.Count; index++)
            {
                RedDotKey key = siblings[index];
                SerializedObject serializedKey = new SerializedObject(key);
                serializedKey.Update();
                serializedKey.FindProperty("siblingOrder").intValue = index;
                serializedKey.ApplyModifiedProperties();
                EditorUtility.SetDirty(key);
            }
        }

        #endregion

        #region 删除

        /// <summary>删除没有子节点的当前节点 Asset。</summary>
        /// <param name="key">待删除节点。</param>
        private void OnDeleteNodeRequested(RedDotKey key)
        {
            if (key == null || currentConfig == null)
            {
                return;
            }

            if (GetDirectChildren(key).Count > 0)
            {
                view.ShowStatus("当前节点仍有子节点，请先迁移子节点，或使用“删除整个子树”。", true);
                return;
            }

            DeleteNodes(new List<RedDotKey> { key });
        }

        /// <summary>删除当前节点及其全部后代。</summary>
        /// <param name="key">子树根节点。</param>
        private void OnDeleteSubtreeRequested(RedDotKey key)
        {
            if (key == null || currentConfig == null)
            {
                return;
            }

            DeleteNodes(CollectSubtree(key));
        }

        /// <summary>读取当前节点的直接子节点。</summary>
        /// <param name="parent">父节点。</param>
        /// <returns>直接子节点列表。</returns>
        private List<RedDotKey> GetDirectChildren(RedDotKey parent)
        {
            return currentConfig.NodeKeys
                .Where(key => key != null && ReferenceEquals(key.Parent, parent))
                .ToList();
        }

        /// <summary>立即移除节点、更新 Config 顺序并将 Asset 放入系统回收站。</summary>
        /// <param name="keys">待删除节点集合。</param>
        private void DeleteNodes(IReadOnlyList<RedDotKey> keys)
        {
            var affectedParentKeys = new HashSet<RedDotKey>();
            for (int index = 0; index < keys.Count; index++)
            {
                RedDotKey parent = keys[index].Parent;
                if (parent != null && !keys.Contains(parent))
                {
                    affectedParentKeys.Add(parent);
                }
            }

            RemoveKeysFromConfig(keys);
            ApplySiblingOrders(GetSiblingKeys(null));
            foreach (RedDotKey parent in affectedParentKeys)
            {
                ApplySiblingOrders(GetSiblingKeys(parent));
            }
            AssetDatabase.SaveAssets();

            int movedCount = 0;
            for (int index = keys.Count - 1; index >= 0; index--)
            {
                string assetPath = AssetDatabase.GetAssetPath(keys[index]);
                if (!string.IsNullOrEmpty(assetPath) && AssetDatabase.MoveAssetToTrash(assetPath))
                {
                    movedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            view.Render(currentConfig);
            bool allAssetsMoved = movedCount == keys.Count;
            view.ShowStatus(
                allAssetsMoved
                    ? $"已删除 {keys.Count} 个节点，并移入系统回收站。"
                    : $"已从 Config 删除 {keys.Count} 个节点，成功移入回收站 {movedCount} 个。",
                !allAssetsMoved);
            if (allAssetsMoved)
            {
                Debug.Log(
                    $"[RedDotNodeSettings] 删除节点完成，requestedCount={keys.Count}，trashedCount={movedCount}。");
            }
            else
            {
                Debug.LogWarning(
                    $"[RedDotNodeSettings] 节点已从 Config 移除，但部分 Asset 未能移入回收站，requestedCount={keys.Count}，trashedCount={movedCount}。");
            }
        }

        /// <summary>从当前 Config 节点列表中移除指定节点。</summary>
        /// <param name="keys">待移除节点。</param>
        private void RemoveKeysFromConfig(IReadOnlyCollection<RedDotKey> keys)
        {
            SerializedObject serializedConfig = new SerializedObject(currentConfig);
            serializedConfig.Update();
            SerializedProperty nodesProperty = serializedConfig.FindProperty("nodeKeys");
            for (int index = nodesProperty.arraySize - 1; index >= 0; index--)
            {
                RedDotKey key = nodesProperty.GetArrayElementAtIndex(index).objectReferenceValue as RedDotKey;
                if (keys.Contains(key))
                {
                    nodesProperty.DeleteArrayElementAtIndex(index);
                }
            }

            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(currentConfig);
        }

        /// <summary>在 Project 窗口中定位当前节点 Asset。</summary>
        /// <param name="key">待定位节点。</param>
        private static void OnPingAssetRequested(RedDotKey key)
        {
            if (key == null)
            {
                return;
            }

            EditorGUIUtility.PingObject(key);
            Selection.activeObject = key;
        }

        #endregion

        #region 事件绑定与辅助

        /// <summary>绑定节点设置 View 的全部用户意图。</summary>
        private void BindViewEvents()
        {
            view.ConfigChangedRequested += OnConfigChanged;
            view.CreateConfigRequested += OnCreateConfigRequested;
            view.NodeFolderChangedRequested += OnNodeFolderChanged;
            view.CreateFolderRequested += OnCreateFolderRequested;
            view.RefreshConfigRequested += OnRefreshConfigRequested;
            view.CreateNodeRequested += OnCreateNodeRequested;
            view.SegmentNameSubmitted += OnSegmentNameSubmitted;
            view.NodeDropRequested += OnNodeDropRequested;
            view.ParentChangedRequested += OnParentChangedRequested;
            view.DerivedParentPathSubmitted += OnDerivedParentPathSubmitted;
            view.SiblingMoveRequested += OnSiblingMoveRequested;
            view.PingAssetRequested += OnPingAssetRequested;
            view.DeleteNodeRequested += OnDeleteNodeRequested;
            view.DeleteSubtreeRequested += OnDeleteSubtreeRequested;
        }

        /// <summary>解除节点设置 View 的全部用户意图。</summary>
        private void UnbindViewEvents()
        {
            view.ConfigChangedRequested -= OnConfigChanged;
            view.CreateConfigRequested -= OnCreateConfigRequested;
            view.NodeFolderChangedRequested -= OnNodeFolderChanged;
            view.CreateFolderRequested -= OnCreateFolderRequested;
            view.RefreshConfigRequested -= OnRefreshConfigRequested;
            view.CreateNodeRequested -= OnCreateNodeRequested;
            view.SegmentNameSubmitted -= OnSegmentNameSubmitted;
            view.NodeDropRequested -= OnNodeDropRequested;
            view.ParentChangedRequested -= OnParentChangedRequested;
            view.DerivedParentPathSubmitted -= OnDerivedParentPathSubmitted;
            view.SiblingMoveRequested -= OnSiblingMoveRequested;
            view.PingAssetRequested -= OnPingAssetRequested;
            view.DeleteNodeRequested -= OnDeleteNodeRequested;
            view.DeleteSubtreeRequested -= OnDeleteSubtreeRequested;
        }

        /// <summary>响应 Undo/Redo 后重新读取当前 Config。</summary>
        private void OnUndoRedoPerformed()
        {
            // Undo/Redo 只重读序列化树；这里不调用 AssetDatabase.RenameAsset，避免回调再写入资源操作。
            view.Render(currentConfig);
            RedDotNodeSettingsViewData selectedNode = view.GetSelectedNode();
            if (selectedNode != null &&
                assetNameManagedKeySet.Contains(selectedNode.Key) &&
                !IsAssetNameForDerivedPath(
                    Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(selectedNode.Key)),
                    selectedNode.Key.DerivedPath))
            {
                view.ShowStatus(
                    "Undo/Redo 已恢复节点树；Asset 文件名保留最近一次节点操作结果，可能与当前派生路径暂时不一致。",
                    true);
            }
        }

        /// <summary>响应项目 Asset 变化并刷新当前树。</summary>
        private void OnProjectChanged()
        {
            view.Render(currentConfig);
        }

        /// <summary>将节点加入当前 Config 的序列化列表。</summary>
        /// <param name="key">待加入节点。</param>
        private void AddKeyToCurrentConfig(RedDotKey key)
        {
            if (currentConfig.Contains(key))
            {
                throw new InvalidOperationException(
                    $"节点 {key.name} 已经存在于当前 RedDotConfig：{currentConfig.name}。");
            }

            RedDotConfig owningConfig = RedDotConfigOwnershipFinder.FindOwningConfig(
                key,
                currentConfig);
            if (owningConfig != null)
            {
                throw new InvalidOperationException(
                    $"节点 {key.name} 已归属于其他 RedDotConfig：{owningConfig.name}。");
            }

            Undo.RecordObject(currentConfig, "加入 RedDotConfig");
            SerializedObject serializedConfig = new SerializedObject(currentConfig);
            serializedConfig.Update();
            SerializedProperty nodesProperty = serializedConfig.FindProperty("nodeKeys");
            nodesProperty.InsertArrayElementAtIndex(nodesProperty.arraySize);
            nodesProperty.GetArrayElementAtIndex(nodesProperty.arraySize - 1).objectReferenceValue = key;
            serializedConfig.ApplyModifiedProperties();
            EditorUtility.SetDirty(currentConfig);
        }

        /// <summary>生成不含文件系统非法字符的 Asset 文件名。</summary>
        /// <param name="segmentName">节点名称。</param>
        /// <returns>可用于 AssetDatabase 的文件名。</returns>
        private static string SanitizeFileName(string segmentName)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(segmentName.Length);
            for (int index = 0; index < segmentName.Length; index++)
            {
                builder.Append(invalidCharacters.Contains(segmentName[index]) ? '_' : segmentName[index]);
            }

            return builder.ToString();
        }

        #region Asset 路径同步

        /// <summary>由父节点路径和当前分段名称生成节点派生路径。</summary>
        /// <param name="parent">目标父节点；为空时表示根节点。</param>
        /// <param name="segmentName">当前节点分段名称。</param>
        /// <returns>目标节点的完整派生路径。</returns>
        private static string BuildDerivedPath(RedDotKey parent, string segmentName)
        {
            return parent == null
                ? segmentName
                : $"{parent.DerivedPath}/{segmentName}";
        }

        /// <summary>将完整派生路径转换为 Asset 文件名主干。</summary>
        /// <param name="derivedPath">节点完整派生路径。</param>
        /// <returns>以下划线分隔层级且已处理非法字符的文件名主干。</returns>
        private static string GetAssetNameStem(string derivedPath)
        {
            return SanitizeFileName(derivedPath.Replace('/', '_'));
        }

        /// <summary>为节点子树计算文件名更新计划，但不执行资源修改。</summary>
        /// <param name="subtreeRoot">派生路径即将变化的子树根节点。</param>
        /// <param name="nextRootDerivedPath">修改后的子树根派生路径。</param>
        /// <returns>按稳定子树顺序排列的文件名更新操作。</returns>
        private List<AssetRenameOperation> BuildSubtreeAssetRenameOperations(
            RedDotKey subtreeRoot,
            string nextRootDerivedPath)
        {
            string previousRootDerivedPath = subtreeRoot.DerivedPath;
            List<RedDotKey> subtreeKeys = CollectSubtree(subtreeRoot);
            var reservedTargetPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var operations = new List<AssetRenameOperation>(subtreeKeys.Count);

            // 先保留已符合路径规则的节点文件名，避免后续节点占用其已有唯一名称。
            for (int index = 0; index < subtreeKeys.Count; index++)
            {
                RedDotKey key = subtreeKeys[index];
                string currentAssetPath = AssetDatabase.GetAssetPath(key);
                if (string.IsNullOrEmpty(currentAssetPath))
                {
                    throw new InvalidOperationException(
                        $"RedDotKey 没有可重命名的 Asset 路径：{key.name}。");
                }

                string suffix = ReferenceEquals(key, subtreeRoot)
                    ? string.Empty
                    : key.DerivedPath.Substring(previousRootDerivedPath.Length);
                string nextDerivedPath = $"{nextRootDerivedPath}{suffix}";
                string currentAssetName = Path.GetFileNameWithoutExtension(currentAssetPath);
                if (IsAssetNameForDerivedPath(currentAssetName, nextDerivedPath))
                {
                    reservedTargetPathSet.Add(currentAssetPath.Replace('\\', '/'));
                    continue;
                }

                string targetAssetPath = BuildUniqueTargetAssetPath(
                    currentAssetPath,
                    nextDerivedPath,
                    reservedTargetPathSet);
                if (!string.Equals(
                        currentAssetPath.Replace('\\', '/'),
                        targetAssetPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    operations.Add(new AssetRenameOperation(key, currentAssetPath, targetAssetPath));
                }
            }

            return operations;
        }

        /// <summary>在节点 Asset 原目录内生成不冲突的完整路径文件名。</summary>
        /// <param name="currentAssetPath">当前 Asset 项目相对路径。</param>
        /// <param name="derivedPath">新派生路径。</param>
        /// <param name="reservedTargetPathSet">本批次已经预留的目标路径集合。</param>
        /// <returns>经过 Unity 唯一路径规则处理的目标 Asset 路径。</returns>
        private static string BuildUniqueTargetAssetPath(
            string currentAssetPath,
            string derivedPath,
            HashSet<string> reservedTargetPathSet)
        {
            string assetFolderPath = Path.GetDirectoryName(currentAssetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(assetFolderPath))
            {
                throw new InvalidOperationException(
                    $"无法确定 RedDotKey 的 Asset 目录：{currentAssetPath}。");
            }

            string extension = Path.GetExtension(currentAssetPath);
            string assetNameStem = GetAssetNameStem(derivedPath);
            string targetAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{assetFolderPath}/{assetNameStem}{extension}").Replace('\\', '/');
            int suffixIndex = 1;
            while (!reservedTargetPathSet.Add(targetAssetPath))
            {
                targetAssetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{assetFolderPath}/{assetNameStem} {suffixIndex++}{extension}").Replace('\\', '/');
            }

            return targetAssetPath;
        }

        /// <summary>执行一批 Asset 改名；任一操作失败时按逆序恢复已完成的改名。</summary>
        /// <param name="operations">预先计算且已避开当前占用路径的改名操作。</param>
        private static void ExecuteAssetRenames(IReadOnlyList<AssetRenameOperation> operations)
        {
            int completedCount = 0;
            try
            {
                for (int index = 0; index < operations.Count; index++)
                {
                    AssetRenameOperation operation = operations[index];
                    string error = AssetDatabase.RenameAsset(
                        operation.OriginalAssetPath,
                        Path.GetFileNameWithoutExtension(operation.TargetAssetPath));
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new InvalidOperationException(
                            $"无法重命名 RedDotKey Asset：{operation.OriginalAssetPath} -> {operation.TargetAssetPath}；{error}");
                    }

                    completedCount++;
                    string renamedAssetPath = AssetDatabase.GetAssetPath(operation.Key).Replace('\\', '/');
                    if (!string.Equals(
                            renamedAssetPath,
                            operation.TargetAssetPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"RedDotKey Asset 改名结果与预期不符：expected={operation.TargetAssetPath}，actual={renamedAssetPath}。");
                    }
                }
            }
            catch (Exception exception)
            {
                string rollbackFailure = RollbackAssetRenames(operations, completedCount);
                string rollbackSummary = string.IsNullOrEmpty(rollbackFailure)
                    ? "已恢复本批次已完成的 Asset 改名。"
                    : $"Asset 改名恢复不完整：{rollbackFailure}";
                Debug.LogWarning(
                    $"[RedDotNodeSettings] 子树 Asset 改名失败，renamedCount={completedCount}，{rollbackSummary}");
                throw new InvalidOperationException(
                    $"同步 RedDotKey Asset 文件名失败。{rollbackSummary} reason={exception.Message}",
                    exception);
            }
        }

        /// <summary>按原始路径逆序恢复一批 Asset 改名并汇总无法恢复的项目。</summary>
        /// <param name="operations">原始改名计划。</param>
        /// <param name="completedCount">已成功完成的操作数量。</param>
        /// <returns>回滚错误摘要；全部恢复成功时为空字符串。</returns>
        private static string RollbackAssetRenames(
            IReadOnlyList<AssetRenameOperation> operations,
            int completedCount)
        {
            var failureMessages = new List<string>();
            for (int index = completedCount - 1; index >= 0; index--)
            {
                AssetRenameOperation operation = operations[index];
                string currentAssetPath = AssetDatabase.GetAssetPath(operation.Key).Replace('\\', '/');
                string originalAssetPath = operation.OriginalAssetPath.Replace('\\', '/');
                if (string.Equals(currentAssetPath, originalAssetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    string error = AssetDatabase.RenameAsset(
                        currentAssetPath,
                        Path.GetFileNameWithoutExtension(originalAssetPath));
                    string restoredAssetPath = AssetDatabase.GetAssetPath(operation.Key).Replace('\\', '/');
                    if (!string.IsNullOrEmpty(error) ||
                        !string.Equals(restoredAssetPath, originalAssetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        failureMessages.Add(
                            $"{originalAssetPath} ({(string.IsNullOrEmpty(error) ? restoredAssetPath : error)})");
                    }
                }
                catch (Exception exception)
                {
                    failureMessages.Add($"{originalAssetPath} ({exception.Message})");
                }
            }

            if (failureMessages.Count == 0)
            {
                return string.Empty;
            }

            string failureSummary = string.Join("；", failureMessages);
            Debug.LogError($"[RedDotNodeSettings] 部分 RedDotKey Asset 文件名无法回滚：{failureSummary}");
            return failureSummary;
        }

        /// <summary>判断 Asset 文件名是否为派生路径主干或 Unity 冲突序号形式。</summary>
        /// <param name="assetName">不含扩展名的 Asset 文件名。</param>
        /// <param name="derivedPath">节点派生路径。</param>
        /// <returns>文件名符合路径规则时返回 true。</returns>
        private static bool IsAssetNameForDerivedPath(string assetName, string derivedPath)
        {
            string expectedAssetName = GetAssetNameStem(derivedPath);
            if (string.Equals(assetName, expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string numberedPrefix = $"{expectedAssetName} ";
            if (string.IsNullOrEmpty(assetName) || !assetName.StartsWith(numberedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string suffix = assetName.Substring(numberedPrefix.Length);
            return suffix.Length > 0 && suffix.All(char.IsDigit);
        }

        #endregion Asset 路径同步

        #endregion

        #region 嵌套类型

        /// <summary>保存单个 RedDotKey Asset 改名前后的路径，用于失败回滚。</summary>
        private sealed class AssetRenameOperation
        {
            /// <summary>初始化 Asset 改名操作数据。</summary>
            /// <param name="key">发生改名的节点 Asset。</param>
            /// <param name="originalAssetPath">改名前的项目相对路径。</param>
            /// <param name="targetAssetPath">改名后的目标项目相对路径。</param>
            public AssetRenameOperation(RedDotKey key, string originalAssetPath, string targetAssetPath)
            {
                Key = key;
                OriginalAssetPath = originalAssetPath;
                TargetAssetPath = targetAssetPath;
            }

            /// <summary>获取被改名的节点 Asset。</summary>
            public RedDotKey Key { get; }

            /// <summary>获取改名前的项目相对路径。</summary>
            public string OriginalAssetPath { get; }

            /// <summary>获取目标项目相对路径。</summary>
            public string TargetAssetPath { get; }
        }

        #endregion
    }
}
#endif
