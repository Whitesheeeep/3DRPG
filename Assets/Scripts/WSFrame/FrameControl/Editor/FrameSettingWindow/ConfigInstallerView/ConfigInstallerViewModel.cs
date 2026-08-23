using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.ConfigInstaller;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    /// <summary>
    /// 管理 ConfigInstaller 面板的配置树、节点类型发现和批量应用状态。
    /// </summary>
    internal sealed class ConfigInstallerViewModel
    {
        #region 常量与字段

        private const string ConfigAssetFolder = "Assets/Scripts/WSFrame/ConfigInstaller/Assets";
        private const string ApplySelectionUndoName = "Add Config Installer Node Selection";

        // 面板所绑定的 FrameSetting 资产，所有编辑最终都写回该资产引用的配置树。
        private readonly WSFrameSetting frameSetting;

        // 配置树使用稳定的 TreeView 数据源，避免 UI 虚拟化期间丢失父子关系。
        private readonly List<TreeViewItemData<ConfigTreeNodeViewData>> rootItems = new();
        private readonly Dictionary<int, ConfigTreeNodeViewData> nodeMap = new();

        // 节点类型列表按类型聚合，同一类型只允许通过面板加入一个实例。
        private readonly List<ConfigRegisterNodeOptionViewData> availableNodeOptions = new();

        private int nextId;
        private bool availableNodeDiscoveryQueued;
        private bool availableNodeDiscoveryInProgress;
        private string availableNodeActionMessage;
        private bool disposed;

        #endregion

        #region 事件与属性

        /// <summary>
        /// 配置根节点引用发生变化时触发。
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// 配置树结构发生变化时触发。
        /// </summary>
        public event Action TreeChanged;

        /// <summary>
        /// 当前树节点选择发生变化时触发。
        /// </summary>
        public event Action SelectionChanged;

        /// <summary>
        /// 可发现节点列表或其持久化包含状态发生变化时触发。
        /// </summary>
        public event Action AvailableNodesChanged;

        /// <summary>
        /// 节点类型异步扫描状态发生变化时触发。
        /// </summary>
        public event Action AvailableNodeScanStateChanged;

        /// <summary>
        /// 创建面板 ViewModel。
        /// </summary>
        /// <param name="frameSetting">面板编辑的 FrameSetting 资产。</param>
        public ConfigInstallerViewModel(WSFrameSetting frameSetting)
        {
            this.frameSetting = frameSetting;
        }

        /// <summary>
        /// 当前配置树根节点。
        /// </summary>
        public ConfigRegisterNodeBase RootNode { get; private set; }

        /// <summary>
        /// 当前选中的配置树节点。
        /// </summary>
        public ConfigTreeNodeViewData SelectedNode { get; private set; }

        /// <summary>
        /// TreeView 使用的根节点数据。
        /// </summary>
        public IList<TreeViewItemData<ConfigTreeNodeViewData>> RootItems => rootItems;

        /// <summary>
        /// 面板显示的可发现注册节点类型。
        /// </summary>
        public List<ConfigRegisterNodeOptionViewData> AvailableNodeOptions => availableNodeOptions;

        /// <summary>
        /// 当前是否仍在等待编辑器延迟扫描。
        /// </summary>
        public bool IsDiscoveringAvailableNodes => availableNodeDiscoveryInProgress;

        /// <summary>
        /// 当前是否具备将节点选择加入所选组合节点的条件。
        /// </summary>
        public bool CanApplyAvailableNodes =>
            !IsDiscoveringAvailableNodes &&
            SelectedNode?.Node is CompositeConfigRegisterNode;

        /// <summary>
        /// 当前是否可以删除选中的树节点引用。
        /// </summary>
        public bool CanRemoveSelectedNode =>
            SelectedNode?.Node != null &&
            SelectedNode.Parent?.Node is CompositeConfigRegisterNode;

        /// <summary>
        /// 最近一次节点批量操作的结果提示。
        /// </summary>
        public string AvailableNodeActionMessage => availableNodeActionMessage;

        #endregion

        #region 生命周期与刷新

        /// <summary>
        /// 从 FrameSetting 读取根节点并立即构建已有配置树；节点类型扫描由调用方另行延迟执行。
        /// </summary>
        public void Refresh()
        {
            RootNode = frameSetting?.configRegisterSetting?.rootNode;
            availableNodeActionMessage = null;
            RebuildTree();
            UpdateAvailableNodeStates(true);
            StateChanged?.Invoke();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// 将节点类型扫描排入下一次编辑器更新，避免阻塞面板首帧布局。
        /// </summary>
        public void BeginAvailableNodeDiscovery()
        {
            if (disposed || availableNodeDiscoveryQueued || availableNodeDiscoveryInProgress)
            {
                return;
            }

            availableNodeDiscoveryQueued = true;
            availableNodeDiscoveryInProgress = true;
            AvailableNodeScanStateChanged?.Invoke();
            EditorApplication.delayCall += DiscoverAvailableNodesDelayed;
        }

        /// <summary>
        /// 取消尚未执行的延迟扫描并释放 ViewModel 的编辑器回调生命周期。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            EditorApplication.delayCall -= DiscoverAvailableNodesDelayed;
            availableNodeDiscoveryQueued = false;
            availableNodeDiscoveryInProgress = false;
        }

        /// <summary>
        /// 写回根节点后重新构建配置树和节点包含状态。
        /// </summary>
        public void RefreshTreeFromModel()
        {
            availableNodeActionMessage = null;
            RebuildTree();
            UpdateAvailableNodeStates(true);
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// 设置 FrameSetting 引用的 ConfigInstaller 根节点。
        /// </summary>
        /// <param name="rootNode">新的配置注册根节点。</param>
        public void SetRootNode(ConfigRegisterNodeBase rootNode)
        {
            if (frameSetting == null)
            {
                return;
            }

            EnsureConfigRegisterSetting();
            SerializedObject serializedFrameSetting = new SerializedObject(frameSetting);
            SerializedProperty settingProperty = serializedFrameSetting.FindProperty("configRegisterSetting");
            SerializedProperty rootProperty = settingProperty.FindPropertyRelative("rootNode");
            rootProperty.objectReferenceValue = rootNode;
            serializedFrameSetting.ApplyModifiedProperties();
            EditorUtility.SetDirty(frameSetting);
            SaveAssets();

            RootNode = rootNode;
            availableNodeActionMessage = null;
            RebuildTree();
            UpdateAvailableNodeStates(true);
            StateChanged?.Invoke();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        #endregion

        #region 配置树操作

        /// <summary>
        /// 设置当前树节点选择。
        /// </summary>
        /// <param name="node">待选中的树节点。</param>
        public void Select(ConfigTreeNodeViewData node)
        {
            SelectedNode = node;
            availableNodeActionMessage = null;
            UpdateAvailableNodeStates(true);
            SelectionChanged?.Invoke();
        }

        /// <summary>
        /// 创建或查找默认 FrameworkConfigRootNode，并将其设为当前根节点。
        /// </summary>
        /// <returns>当前使用的框架配置根节点。</returns>
        public FrameworkConfigRootNode CreateOrFindRootNode()
        {
            FrameworkConfigRootNode root = FindFirstAsset<FrameworkConfigRootNode>();
            if (root == null)
            {
                root = CreateNodeAsset<FrameworkConfigRootNode>("FrameworkConfigRootNode");
            }

            SetRootNode(root);
            return root;
        }

        /// <summary>
        /// 将待添加节点加入当前选中的组合节点。
        /// </summary>
        /// <param name="child">待添加的节点资产。</param>
        public void AddChildToSelectedComposite(ConfigRegisterNodeBase child)
        {
            if (SelectedNode?.Node is not CompositeConfigRegisterNode composite || child == null)
            {
                return;
            }

            AddChild(composite, child);
        }

        /// <summary>
        /// 将待添加节点加入根组合节点。
        /// </summary>
        /// <param name="child">待添加的节点资产。</param>
        public void AddChildToRoot(ConfigRegisterNodeBase child)
        {
            if (RootNode is CompositeConfigRegisterNode composite && child != null)
            {
                AddChild(composite, child);
            }
        }

        /// <summary>
        /// 移除当前选中的树节点引用，但保留节点资产本身。
        /// </summary>
        public void RemoveSelectedNode()
        {
            if (SelectedNode?.Parent?.Node is not CompositeConfigRegisterNode parent || SelectedNode.Node == null)
            {
                return;
            }

            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            int index = FindChildIndex(children, SelectedNode.Node);
            if (index < 0)
            {
                return;
            }

            Undo.RecordObject(parent, "Remove Config Register Node");
            children.DeleteArrayElementAtIndex(index);
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            SelectedNode = SelectedNode.Parent;
            RefreshTreeFromModel();
        }

        /// <summary>
        /// 调整当前选中节点在父组合节点中的顺序。
        /// </summary>
        /// <param name="offset">相对当前位置的移动步数。</param>
        public void MoveSelectedNode(int offset)
        {
            if (SelectedNode?.Parent?.Node is not CompositeConfigRegisterNode parent || SelectedNode.Node == null)
            {
                return;
            }

            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            int oldIndex = FindChildIndex(children, SelectedNode.Node);
            int newIndex = oldIndex + offset;
            if (oldIndex < 0 || newIndex < 0 || newIndex >= children.arraySize)
            {
                return;
            }

            Undo.RecordObject(parent, "Move Config Register Node");
            children.MoveArrayElement(oldIndex, newIndex);
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            RefreshTreeFromModel();
        }

        /// <summary>
        /// 手动执行当前 FrameSetting 的全部配置注册节点。
        /// </summary>
        public void RegisterAll()
        {
            if (frameSetting?.configRegisterSetting == null)
            {
                Debug.LogWarning("[ConfigRegister] FrameSetting or ConfigRegisterSetting is missing.");
                return;
            }

            ConfigRegisterSystem.Instance.Register(frameSetting.configRegisterSetting);
        }

        /// <summary>
        /// 在 Project 窗口中定位指定配置资产。
        /// </summary>
        /// <param name="target">待定位的资产。</param>
        public void Ping(Object target)
        {
            if (target == null)
            {
                return;
            }

            EditorGUIUtility.PingObject(target);
            Selection.activeObject = target;
        }

        #endregion

        #region 节点发现与批量应用

        /// <summary>
        /// 更新单个节点类型选项的待应用状态。
        /// </summary>
        /// <param name="option">待修改的节点类型选项。</param>
        /// <param name="isSelected">新的待应用状态。</param>
        public void SetAvailableNodeSelection(ConfigRegisterNodeOptionViewData option, bool isSelected)
        {
            option.IsSelected = isSelected;
        }

        /// <summary>
        /// 将勾选的节点类型加入当前选中的组合节点；已有引用和节点资产不会被删除。
        /// </summary>
        public void ApplySelectedNodes()
        {
            if (!CanApplyAvailableNodes)
            {
                availableNodeActionMessage = "Select a Composite node before applying selections.";
                AvailableNodeScanStateChanged?.Invoke();
                Debug.LogWarning("[ConfigRegister] A CompositeConfigRegisterNode must be selected before applying node selections.");
                return;
            }

            CompositeConfigRegisterNode target = (CompositeConfigRegisterNode)SelectedNode.Node;
            List<ConfigRegisterNodeBase> nodesToAdd = new List<ConfigRegisterNodeBase>();
            List<string> skippedTypes = new List<string>();
            foreach (ConfigRegisterNodeOptionViewData option in availableNodeOptions)
            {
                if (!option.IsSelected)
                {
                    continue;
                }

                ConfigRegisterNodeBase existingNode = FindNodeOfType(
                    RootNode,
                    option.NodeType,
                    new HashSet<ConfigRegisterNodeBase>());
                if (existingNode != null)
                {
                    skippedTypes.Add($"{option.TypeDisplayName} (already exists in the tree)");
                    continue;
                }

                if (option.NodeAsset != null && WouldCreateCycle(target, option.NodeAsset))
                {
                    skippedTypes.Add($"{option.TypeDisplayName} (would create a cycle)");
                    continue;
                }

                // 只有用户明确应用选择时才创建缺失资产，扫描阶段不会产生项目文件。
                option.NodeAsset ??= CreateNodeAsset(option.NodeType);
                nodesToAdd.Add(option.NodeAsset);
            }

            int addedCount = AddChildReferences(target, nodesToAdd);
            if (addedCount > 0)
            {
                // 批量写入完成后统一保存和刷新，避免每个节点单独触发树重建。
                SaveAssets();
                RefreshTreeFromModel();
            }

            availableNodeActionMessage = BuildApplyResultMessage(addedCount, skippedTypes);
            AvailableNodeScanStateChanged?.Invoke();
        }

        /// <summary>
        /// 在编辑器延迟回调中扫描注册节点类型和现有资产。
        /// </summary>
        private void DiscoverAvailableNodesDelayed()
        {
            availableNodeDiscoveryQueued = false;
            if (disposed)
            {
                return;
            }

            // TypeCache 和 AssetDatabase 都要求在 Unity 编辑器主线程访问，因此使用延迟回调而不是后台线程。
            List<Type> nodeTypes = new List<Type>(TypeCache.GetTypesDerivedFrom<ConfigRegisterNodeBase>());
            nodeTypes.RemoveAll(IsUnsupportedNodeType);
            nodeTypes.Sort(CompareTypes);

            HashSet<ConfigRegisterNodeBase> includedAssets = CollectNodeAssets(RootNode);
            availableNodeOptions.Clear();
            foreach (Type nodeType in nodeTypes)
            {
                ConfigRegisterNodeBase nodeAsset = FindPreferredNodeAsset(nodeType, includedAssets);
                bool isIncluded = IsNodeIncludedInSelectedComposite(nodeType);
                availableNodeOptions.Add(new ConfigRegisterNodeOptionViewData(nodeType, nodeAsset, isIncluded));
            }

            availableNodeDiscoveryInProgress = false;
            AvailableNodesChanged?.Invoke();
            AvailableNodeScanStateChanged?.Invoke();
        }

        /// <summary>
        /// 判断类型是否不应作为面板可选节点显示。
        /// </summary>
        /// <param name="nodeType">待判断的注册节点类型。</param>
        /// <returns>类型不可作为面板选项时返回 true。</returns>
        private static bool IsUnsupportedNodeType(Type nodeType)
        {
            return !nodeType.IsClass ||
                   nodeType.IsAbstract ||
                   nodeType.IsGenericType ||
                   (!nodeType.IsPublic && !nodeType.IsNestedPublic) ||
                   !typeof(ScriptableObject).IsAssignableFrom(nodeType) ||
                   nodeType == typeof(FrameworkConfigRootNode);
        }

        /// <summary>
        /// 按完整类型名建立稳定的发现顺序。
        /// </summary>
        /// <param name="left">左侧类型。</param>
        /// <param name="right">右侧类型。</param>
        /// <returns>类型名比较结果。</returns>
        private static int CompareTypes(Type left, Type right)
        {
            return string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 读取当前配置树中优先复用的指定类型资产。
        /// </summary>
        /// <param name="nodeType">待查询的节点类型。</param>
        /// <param name="includedAssets">当前配置树已引用的资产集合。</param>
        /// <returns>当前树中同类型资产、项目中首个同类型资产，或 null。</returns>
        private static ConfigRegisterNodeBase FindPreferredNodeAsset(
            Type nodeType,
            HashSet<ConfigRegisterNodeBase> includedAssets)
        {
            string[] guids = AssetDatabase.FindAssets($"t:{nodeType.Name}");
            Array.Sort(guids, CompareAssetGuids);

            ConfigRegisterNodeBase fallback = null;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ConfigRegisterNodeBase nodeAsset = AssetDatabase.LoadAssetAtPath<ConfigRegisterNodeBase>(path);
                if (nodeAsset == null || nodeAsset.GetType() != nodeType)
                {
                    continue;
                }

                if (includedAssets.Contains(nodeAsset))
                {
                    return nodeAsset;
                }

                fallback ??= nodeAsset;
            }

            return fallback;
        }

        /// <summary>
        /// 按资产路径比较 GUID，确保同类型资产复用结果稳定。
        /// </summary>
        /// <param name="leftGuid">左侧资产 GUID。</param>
        /// <param name="rightGuid">右侧资产 GUID。</param>
        /// <returns>资产路径比较结果。</returns>
        private static int CompareAssetGuids(string leftGuid, string rightGuid)
        {
            string leftPath = AssetDatabase.GUIDToAssetPath(leftGuid);
            string rightPath = AssetDatabase.GUIDToAssetPath(rightGuid);
            return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 根据当前选中的组合节点刷新节点选项状态。
        /// </summary>
        /// <param name="resetSelection">是否将待应用选择重置为当前配置树状态。</param>
        private void UpdateAvailableNodeStates(bool resetSelection)
        {
            if (availableNodeOptions.Count == 0)
            {
                return;
            }

            foreach (ConfigRegisterNodeOptionViewData option in availableNodeOptions)
            {
                option.IsIncluded = IsNodeIncludedInSelectedComposite(option.NodeType);
                if (resetSelection)
                {
                    option.IsSelected = option.IsIncluded;
                }
            }

            AvailableNodesChanged?.Invoke();
        }

        /// <summary>
        /// 判断节点类型是否已存在于当前选中组合节点的子节点树中。
        /// </summary>
        /// <param name="nodeType">待检查的精确节点类型。</param>
        /// <returns>当前目标的子节点树已包含该类型时返回 true。</returns>
        private bool IsNodeIncludedInSelectedComposite(Type nodeType)
        {
            if (SelectedNode?.Node is not CompositeConfigRegisterNode composite)
            {
                return false;
            }

            SerializedObject serializedComposite = new SerializedObject(composite);
            SerializedProperty children = serializedComposite.FindProperty("children");
            HashSet<ConfigRegisterNodeBase> visited = new HashSet<ConfigRegisterNodeBase>();
            for (int index = 0; index < children.arraySize; index++)
            {
                ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(index).objectReferenceValue as ConfigRegisterNodeBase;
                // 从 children 开始遍历，避免把 SelectedNode 自身误判为已注册子节点。
                if (FindNodeOfType(child, nodeType, visited) != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 生成批量加入结果文本，保留重复类型和循环引用的处理原因。
        /// </summary>
        /// <param name="addedCount">实际加入的节点数量。</param>
        /// <param name="skippedTypes">被跳过的类型和原因。</param>
        /// <returns>供面板状态栏显示的结果文本。</returns>
        private static string BuildApplyResultMessage(int addedCount, List<string> skippedTypes)
        {
            List<string> messages = new List<string>();
            if (addedCount > 0)
            {
                messages.Add($"Added {addedCount} node(s) to the selected Composite.");
            }

            if (skippedTypes.Count > 0)
            {
                messages.Add($"Skipped {skippedTypes.Count}: {string.Join(", ", skippedTypes)}.");
            }

            return messages.Count == 0
                ? "No new node types were selected."
                : string.Join(" ", messages);
        }

        /// <summary>
        /// 判断把候选节点挂到目标组合节点下是否会形成循环引用。
        /// </summary>
        /// <param name="target">接收候选节点的组合节点。</param>
        /// <param name="candidate">待加入的候选节点。</param>
        /// <returns>候选节点已包含目标节点或与目标相同时返回 true。</returns>
        private static bool WouldCreateCycle(
            CompositeConfigRegisterNode target,
            ConfigRegisterNodeBase candidate)
        {
            return target == null || candidate == null || ContainsNode(candidate, target, new HashSet<ConfigRegisterNodeBase>());
        }

        /// <summary>
        /// 递归检查一个节点子树中是否包含目标节点。
        /// </summary>
        /// <param name="node">当前遍历节点。</param>
        /// <param name="target">待查找的目标节点。</param>
        /// <param name="visited">已遍历节点集合，防止异常数据循环。</param>
        /// <returns>子树包含目标节点时返回 true。</returns>
        private static bool ContainsNode(
            ConfigRegisterNodeBase node,
            ConfigRegisterNodeBase target,
            HashSet<ConfigRegisterNodeBase> visited)
        {
            if (node == null || target == null || !visited.Add(node))
            {
                return false;
            }

            if (node == target)
            {
                return true;
            }

            if (node is not CompositeConfigRegisterNode composite)
            {
                return false;
            }

            SerializedObject serializedComposite = new SerializedObject(composite);
            SerializedProperty children = serializedComposite.FindProperty("children");
            for (int index = 0; index < children.arraySize; index++)
            {
                ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(index).objectReferenceValue as ConfigRegisterNodeBase;
                if (ContainsNode(child, target, visited))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 递归查找配置树中的指定精确类型节点。
        /// </summary>
        /// <param name="node">当前递归节点。</param>
        /// <param name="nodeType">待查找的精确类型。</param>
        /// <param name="visited">防止异常循环引用导致递归重复处理。</param>
        /// <returns>找到的节点资产；找不到时返回 null。</returns>
        private static ConfigRegisterNodeBase FindNodeOfType(
            ConfigRegisterNodeBase node,
            Type nodeType,
            HashSet<ConfigRegisterNodeBase> visited)
        {
            if (node == null || !visited.Add(node))
            {
                return null;
            }

            if (node.GetType() == nodeType)
            {
                return node;
            }

            if (node is not CompositeConfigRegisterNode composite)
            {
                return null;
            }

            SerializedObject serializedComposite = new SerializedObject(composite);
            SerializedProperty children = serializedComposite.FindProperty("children");
            for (int index = 0; index < children.arraySize; index++)
            {
                ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(index).objectReferenceValue as ConfigRegisterNodeBase;
                ConfigRegisterNodeBase found = FindNodeOfType(child, nodeType, visited);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 递归收集配置树中的节点资产，用于优先复用当前树资产。
        /// </summary>
        /// <param name="root">待遍历的配置树根节点。</param>
        /// <returns>配置树引用的非空节点资产集合。</returns>
        private static HashSet<ConfigRegisterNodeBase> CollectNodeAssets(ConfigRegisterNodeBase root)
        {
            HashSet<ConfigRegisterNodeBase> assets = new HashSet<ConfigRegisterNodeBase>();
            CollectNodeAssets(root, assets, new HashSet<ConfigRegisterNodeBase>());
            return assets;
        }

        /// <summary>
        /// 递归收集配置树节点资产的内部实现。
        /// </summary>
        /// <param name="node">当前递归节点。</param>
        /// <param name="assets">接收节点资产的集合。</param>
        /// <param name="visited">防止异常循环引用导致递归重复处理。</param>
        private static void CollectNodeAssets(
            ConfigRegisterNodeBase node,
            HashSet<ConfigRegisterNodeBase> assets,
            HashSet<ConfigRegisterNodeBase> visited)
        {
            if (node == null || !visited.Add(node))
            {
                return;
            }

            assets.Add(node);
            if (node is not CompositeConfigRegisterNode composite)
            {
                return;
            }

            SerializedObject serializedComposite = new SerializedObject(composite);
            SerializedProperty children = serializedComposite.FindProperty("children");
            for (int index = 0; index < children.arraySize; index++)
            {
                ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(index).objectReferenceValue as ConfigRegisterNodeBase;
                CollectNodeAssets(child, assets, visited);
            }
        }

        /// <summary>
        /// 将一批节点引用加入组合节点，并统一写入 Undo 和资产脏状态。
        /// </summary>
        /// <param name="parent">接收引用的组合节点。</param>
        /// <param name="childrenToAdd">待加入的子节点集合。</param>
        /// <returns>实际加入的节点数量。</returns>
        private static int AddChildReferences(
            CompositeConfigRegisterNode parent,
            IList<ConfigRegisterNodeBase> childrenToAdd)
        {
            if (parent == null || childrenToAdd == null || childrenToAdd.Count == 0)
            {
                return 0;
            }

            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            List<ConfigRegisterNodeBase> uniqueChildren = new List<ConfigRegisterNodeBase>();
            foreach (ConfigRegisterNodeBase child in childrenToAdd)
            {
                if (child != null && FindChildIndex(children, child) < 0 && !uniqueChildren.Contains(child))
                {
                    uniqueChildren.Add(child);
                }
            }

            if (uniqueChildren.Count == 0)
            {
                return 0;
            }

            Undo.RecordObject(parent, ApplySelectionUndoName);
            foreach (ConfigRegisterNodeBase child in uniqueChildren)
            {
                int index = children.arraySize;
                children.InsertArrayElementAtIndex(index);
                children.GetArrayElementAtIndex(index).objectReferenceValue = child;
            }

            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            return uniqueChildren.Count;
        }

        #endregion

        #region 树构建与资产辅助

        /// <summary>
        /// 将指定根节点转换为 TreeView 数据。
        /// </summary>
        private void RebuildTree()
        {
            ConfigRegisterNodeBase previousSelection = SelectedNode?.Node;
            rootItems.Clear();
            nodeMap.Clear();
            nextId = 1;

            if (RootNode == null)
            {
                SelectedNode = null;
                return;
            }

            TreeViewItemData<ConfigTreeNodeViewData> rootItem = BuildTreeItem(RootNode, null, 0);
            rootItems.Add(rootItem);
            SelectedNode = FindViewData(previousSelection) ?? rootItem.data;
        }

        /// <summary>
        /// 递归构建单个配置树节点及其子节点数据。
        /// </summary>
        /// <param name="node">当前节点。</param>
        /// <param name="parent">当前节点的父 ViewData。</param>
        /// <param name="depth">当前树深度。</param>
        /// <returns>TreeView 使用的节点数据。</returns>
        private TreeViewItemData<ConfigTreeNodeViewData> BuildTreeItem(
            ConfigRegisterNodeBase node,
            ConfigTreeNodeViewData parent,
            int depth)
        {
            int id = nextId++;
            ConfigTreeNodeViewData viewData = new ConfigTreeNodeViewData(id, depth, node, parent);
            nodeMap[id] = viewData;

            List<TreeViewItemData<ConfigTreeNodeViewData>> childrenItems = new List<TreeViewItemData<ConfigTreeNodeViewData>>();
            if (node is CompositeConfigRegisterNode composite)
            {
                SerializedObject serializedNode = new SerializedObject(composite);
                SerializedProperty children = serializedNode.FindProperty("children");
                for (int index = 0; index < children.arraySize; index++)
                {
                    ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(index).objectReferenceValue as ConfigRegisterNodeBase;
                    childrenItems.Add(BuildTreeItem(child, viewData, depth + 1));
                }
            }

            return new TreeViewItemData<ConfigTreeNodeViewData>(id, viewData, childrenItems);
        }

        /// <summary>
        /// 确保 FrameSetting 存在 ConfigRegisterSetting 序列化对象。
        /// </summary>
        private void EnsureConfigRegisterSetting()
        {
            if (frameSetting.configRegisterSetting != null)
            {
                return;
            }

            frameSetting.configRegisterSetting = new ConfigRegisterSetting();
            EditorUtility.SetDirty(frameSetting);
        }

        /// <summary>
        /// 查找父组合节点中指定子资产的数组索引。
        /// </summary>
        /// <param name="children">组合节点 children 序列化属性。</param>
        /// <param name="child">待查找的子资产。</param>
        /// <returns>找到的索引；找不到时返回 -1。</returns>
        private static int FindChildIndex(SerializedProperty children, ConfigRegisterNodeBase child)
        {
            for (int index = 0; index < children.arraySize; index++)
            {
                if (children.GetArrayElementAtIndex(index).objectReferenceValue == child)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 根据节点资产查找对应的 TreeView 数据。
        /// </summary>
        /// <param name="node">待查找的节点资产。</param>
        /// <returns>对应的 ViewData；找不到时返回 null。</returns>
        private ConfigTreeNodeViewData FindViewData(ConfigRegisterNodeBase node)
        {
            if (node == null)
            {
                return null;
            }

            foreach (ConfigTreeNodeViewData viewData in nodeMap.Values)
            {
                if (viewData.Node == node)
                {
                    return viewData;
                }
            }

            return null;
        }

        /// <summary>
        /// 查找项目中的首个指定类型资产。
        /// </summary>
        /// <typeparam name="T">待查找的 Unity 资产类型。</typeparam>
        /// <returns>首个找到的资产；找不到时返回 null。</returns>
        private static T FindFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0)
            {
                return null;
            }

            Array.Sort(guids, CompareAssetGuids);
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>
        /// 按静态类型创建并保存一个新的 ConfigRegisterNodeBase 资产。
        /// </summary>
        /// <typeparam name="T">静态确定的节点资产类型。</typeparam>
        /// <param name="fileName">默认文件名。</param>
        /// <returns>新创建的节点资产。</returns>
        private static T CreateNodeAsset<T>(string fileName) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ConfigAssetFolder}/{fileName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        /// <summary>
        /// 按运行时 Type 创建并保存一个新的注册节点资产。
        /// </summary>
        /// <param name="nodeType">具体注册节点类型。</param>
        /// <returns>新创建的注册节点资产。</returns>
        private static ConfigRegisterNodeBase CreateNodeAsset(Type nodeType)
        {
            ScriptableObject asset = ScriptableObject.CreateInstance(nodeType);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ConfigAssetFolder}/{nodeType.Name}.asset");
            AssetDatabase.CreateAsset(asset, path);
            return (ConfigRegisterNodeBase)asset;
        }

        /// <summary>
        /// 保存配置树和新创建节点资产，并刷新 AssetDatabase 对引用的解析。
        /// </summary>
        private static void SaveAssets()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 将已有节点加入指定组合节点，并保持已有的面板刷新行为。
        /// </summary>
        /// <param name="parent">接收子节点的组合节点。</param>
        /// <param name="child">待加入的子节点。</param>
        private void AddChild(CompositeConfigRegisterNode parent, ConfigRegisterNodeBase child)
        {
            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            if (FindChildIndex(children, child) >= 0)
            {
                Debug.LogWarning($"[ConfigRegister] Child already exists: {child.name}");
                return;
            }

            Undo.RecordObject(parent, "Add Config Register Node");
            int index = children.arraySize;
            children.InsertArrayElementAtIndex(index);
            children.GetArrayElementAtIndex(index).objectReferenceValue = child;
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            RefreshTreeFromModel();
        }

        #endregion
    }
}
