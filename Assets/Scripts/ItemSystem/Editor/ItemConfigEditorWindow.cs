#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace RPG.ItemSystem.Editor
{
    /// <summary>ItemDatabase 的 UI Toolkit 配置窗口组合根。</summary>
    public sealed class ItemConfigEditorWindow : EditorWindow
    {
        private ItemDatabase pendingDatabase;
        private ItemDefinition pendingDefinition;
        private ItemConfigEditorController controller;

        /// <summary>打开 Item 配置窗口。</summary>
        [MenuItem("RPG/ItemSystem/物品配置编辑器", priority = 100)]
        private static void ShowWindow()
        {
            ItemConfigEditorWindow window = GetWindow<ItemConfigEditorWindow>();
            window.ConfigureWindow();
            window.Show();
        }

        /// <summary>双击 ItemDatabase 或 ItemDefinition 资产时打开对应配置编辑器。</summary>
        /// <param name="instanceId">资产实例 ID。</param>
        /// <param name="line">Unity 传入的代码行号。</param>
        /// <returns>对象为 ItemDatabase 或 ItemDefinition 时返回 true。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            ItemDatabase database = EditorUtility.InstanceIDToObject(instanceId) as ItemDatabase;
            if (database != null)
            {
                Open(database);
                return true;
            }

            ItemDefinition definition = EditorUtility.InstanceIDToObject(instanceId) as ItemDefinition;
            if (definition == null) return false;
            Open(definition);
            return true;
        }

        /// <summary>打开窗口并选中指定物品数据库。</summary>
        /// <param name="database">待选择的数据库。</param>
        internal static void Open(ItemDatabase database)
        {
            if (database == null) return;
            ItemConfigEditorWindow window = GetWindow<ItemConfigEditorWindow>();
            window.ConfigureWindow();
            window.pendingDatabase = database;
            window.pendingDefinition = null;
            window.Show();
            window.controller?.OpenDatabase(database);
        }

        /// <summary>打开窗口并选中指定物品。</summary>
        /// <param name="definition">待选中的物品定义。</param>
        internal static void Open(ItemDefinition definition)
        {
            ItemConfigEditorWindow window = GetWindow<ItemConfigEditorWindow>();
            window.ConfigureWindow();
            window.pendingDatabase = null;
            window.pendingDefinition = definition;
            window.Show();
            window.controller?.OpenDefinition(definition);
        }

        /// <summary>创建窗口视图和 MVC Controller。</summary>
        private void CreateGUI()
        {
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            string windowUxmlPath = UxmlUssPathConstants.Uxml.AssetsScriptsItemSystemEditorStyleItemConfigEditorWindow;
            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(windowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到 Item 配置窗口 UXML：{windowUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            ItemConfigEditorView view = new ItemConfigEditorView(rootVisualElement);
            controller = new ItemConfigEditorController(view);
            if (pendingDatabase != null)
            {
                controller.OpenDatabase(pendingDatabase);
            }
            else if (pendingDefinition != null)
            {
                controller.OpenDefinition(pendingDefinition);
            }

            // 请求只用于本次视觉树初始化；清除它避免窗口后续重建时覆盖用户当前选择。
            pendingDatabase = null;
            pendingDefinition = null;
        }

        /// <summary>窗口禁用时释放 Controller 和绑定。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }

        /// <summary>设置窗口标题和最小尺寸。</summary>
        private void ConfigureWindow()
        {
            titleContent = new GUIContent("物品配置编辑器");
            minSize = new Vector2(1080f, 680f);
        }
    }
}
#endif
