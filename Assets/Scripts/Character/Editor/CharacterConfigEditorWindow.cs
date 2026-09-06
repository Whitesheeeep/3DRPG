#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace RPG.Character.Editor
{
    /// <summary>角色配置数据库与单个 CharacterConfig 的 UI Toolkit 编辑窗口。</summary>
    public sealed class CharacterConfigEditorWindow : EditorWindow
    {
        #region 依赖字段

        // Controller 持有窗口级事件编排和 SerializedObject 刷新职责。
        private CharacterConfigEditorController controller;

        #endregion

        #region 窗口状态

        private CharacterDatabase pendingDatabase;
        private CharacterConfig pendingConfig;

        #endregion

        /// <summary>打开角色配置编辑器。</summary>
        /// <param name="database">可选初始数据库。</param>
        /// <param name="config">可选初始角色配置；提供时按配置归属解析数据库。</param>
        public static void Open(CharacterDatabase database = null, CharacterConfig config = null)
        {
            var window = GetWindow<CharacterConfigEditorWindow>();
            window.ConfigureWindow();
            // 菜单入口没有显式对象时优先恢复唯一数据库；打开具体配置时交给 Service 按归属解析，避免选错数据库。
            window.pendingDatabase = database ?? (config == null ? CharacterConfigEditorSession.ResolveDatabase() : null);
            window.pendingConfig = config;
            window.Show();
            window.Focus();
            if (window.controller != null)
            {
                window.controller.Open(database, config);
                window.pendingDatabase = null;
                window.pendingConfig = null;
            }
        }

        /// <summary>提供菜单入口。</summary>
        [MenuItem("RPG/Character/角色配置编辑器")]
        private static void OpenMenu() => Open();

        /// <summary>为双击的角色数据库或配置提供 Project 入口。</summary>
        /// <param name="instanceID">Unity 对象 InstanceID。</param>
        /// <param name="line">源码行号。</param>
        /// <returns>已处理对象时返回 true。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceID, int line)
        {
            Object target = EditorUtility.InstanceIDToObject(instanceID);
            if (target is CharacterDatabase database)
            {
                Open(database);
                return true;
            }
            if (target is CharacterConfig config)
            {
                Open(null, config);
                return true;
            }
            return false;
        }

        /// <summary>创建并挂载 UI Toolkit 视图。</summary>
        private void CreateGUI()
        {
            // UI Toolkit 重新构建时先解除旧 Controller，避免同一窗口重复订阅 Undo 和按钮事件。
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            string windowUxmlPath = UxmlUssPathConstants.Uxml.AssetsScriptsCharacterEditorStyleCharacterConfigEditorWindow;
            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(windowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到角色配置窗口 UXML：{windowUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            string windowUssPath = UxmlUssPathConstants.Uss.AssetsScriptsCharacterEditorStyleCharacterConfigEditorWindow;
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(windowUssPath);
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);
            var view = new CharacterConfigEditorView(rootVisualElement);
            controller = new CharacterConfigEditorController();
            controller.Bind(view, pendingDatabase, pendingConfig);
            pendingDatabase = null;
            pendingConfig = null;
        }

        /// <summary>释放 Controller 和 SerializedObject 绑定。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }

        /// <summary>设置窗口标题和最小尺寸，保证分栏区域拥有可用布局空间。</summary>
        private void ConfigureWindow()
        {
            titleContent = new GUIContent("角色配置编辑器");
            minSize = new Vector2(1080f, 680f);
        }
    }
}
#endif
