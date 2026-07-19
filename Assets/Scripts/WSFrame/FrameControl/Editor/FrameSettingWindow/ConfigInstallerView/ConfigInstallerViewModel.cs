using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.ConfigInstaller;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    internal sealed class ConfigInstallerViewModel
    {
        private const string ConfigAssetFolder = "Assets/Scripts/WSFrame/ConfigInstaller/Assets";

        private readonly WSFrameSetting frameSetting;
        private readonly List<TreeViewItemData<ConfigTreeNodeViewData>> rootItems = new();
        private readonly Dictionary<int, ConfigTreeNodeViewData> nodeMap = new();
        private int nextId;

        public ConfigInstallerViewModel(WSFrameSetting frameSetting)
        {
            this.frameSetting = frameSetting;
        }

        public event Action StateChanged;
        public event Action TreeChanged;
        public event Action SelectionChanged;

        public ConfigRegisterNodeBase RootNode { get; private set; }
        public ConfigTreeNodeViewData SelectedNode { get; private set; }
        public IList<TreeViewItemData<ConfigTreeNodeViewData>> RootItems => rootItems;

        public void Refresh()
        {
            RootNode = frameSetting?.configRegisterSetting?.rootNode;
            RebuildTree();
            StateChanged?.Invoke();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

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
            RebuildTree();
            StateChanged?.Invoke();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void Select(ConfigTreeNodeViewData node)
        {
            SelectedNode = node;
            SelectionChanged?.Invoke();
        }

        public void RefreshTreeFromModel()
        {
            RebuildTree();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

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

        public void AddChildToSelectedComposite(ConfigRegisterNodeBase child)
        {
            if (SelectedNode?.Node is not CompositeConfigRegisterNode composite || child == null)
            {
                return;
            }

            AddChild(composite, child);
        }

        public void AddChildToRoot(ConfigRegisterNodeBase child)
        {
            if (RootNode is CompositeConfigRegisterNode composite && child != null)
            {
                AddChild(composite, child);
            }
        }

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

            children.DeleteArrayElementAtIndex(index);
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            SelectedNode = SelectedNode.Parent;
            RebuildTree();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

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

            children.MoveArrayElement(oldIndex, newIndex);
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            RebuildTree();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

        public void RegisterAll()
        {
            if (frameSetting?.configRegisterSetting == null)
            {
                Debug.LogWarning("[ConfigRegister] FrameSetting or ConfigRegisterSetting is missing.");
                return;
            }

            ConfigRegisterSystem.Instance.Register(frameSetting.configRegisterSetting);
        }

        public void Ping(Object target)
        {
            if (target == null)
            {
                return;
            }

            EditorGUIUtility.PingObject(target);
            Selection.activeObject = target;
        }

        private void AddChild(CompositeConfigRegisterNode parent, ConfigRegisterNodeBase child)
        {
            SerializedObject serializedParent = new SerializedObject(parent);
            SerializedProperty children = serializedParent.FindProperty("children");
            if (FindChildIndex(children, child) >= 0)
            {
                Debug.LogWarning($"[ConfigRegister] Child already exists: {child.name}");
                return;
            }

            int index = children.arraySize;
            children.InsertArrayElementAtIndex(index);
            children.GetArrayElementAtIndex(index).objectReferenceValue = child;
            serializedParent.ApplyModifiedProperties();
            EditorUtility.SetDirty(parent);
            SaveAssets();
            RebuildTree();
            TreeChanged?.Invoke();
            SelectionChanged?.Invoke();
        }

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
                for (int i = 0; i < children.arraySize; i++)
                {
                    ConfigRegisterNodeBase child = children.GetArrayElementAtIndex(i).objectReferenceValue as ConfigRegisterNodeBase;
                    childrenItems.Add(BuildTreeItem(child, viewData, depth + 1));
                }
            }

            return new TreeViewItemData<ConfigTreeNodeViewData>(id, viewData, childrenItems);
        }

        private void EnsureConfigRegisterSetting()
        {
            if (frameSetting.configRegisterSetting != null)
            {
                return;
            }

            frameSetting.configRegisterSetting = new ConfigRegisterSetting();
            EditorUtility.SetDirty(frameSetting);
        }

        private static int FindChildIndex(SerializedProperty children, ConfigRegisterNodeBase child)
        {
            for (int i = 0; i < children.arraySize; i++)
            {
                if (children.GetArrayElementAtIndex(i).objectReferenceValue == child)
                {
                    return i;
                }
            }

            return -1;
        }

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

        private static T FindFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
            if (guids.Length == 0)
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static T CreateNodeAsset<T>(string fileName) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            string path = AssetDatabase.GenerateUniqueAssetPath($"{ConfigAssetFolder}/{fileName}.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return asset;
        }

        private static void SaveAssets()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
