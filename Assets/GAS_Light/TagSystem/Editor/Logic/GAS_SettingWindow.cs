#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.TAG;

namespace WSFrame.GAS.Editor
{
    /// <summary>Gameplay Tag 数据库的 UI Toolkit 编辑窗口。</summary>
    public sealed class GAS_SettingWindow : EditorWindow
    {
        #region 常量与字段
        private const string WindowUxmlPath = "Assets/GAS_Light/TagSystem/Editor/Style/GameplayTagEditor.uxml";
        private const string RowUxmlPath = "Assets/GAS_Light/TagSystem/Editor/Style/GameplayTagTreeRow.uxml";
        [SerializeField] private GameplayTagDatabase initialDatabase;
        private IGameplayTagEditorView view;
        private GameplayTagEditorController controller;
        #endregion

        /// <summary>打开或聚焦 Gameplay Tag 编辑窗口，并恢复会话数据库。</summary>
        [MenuItem("WSFrame/GAS/Gameplay Tags")]
        public static void ShowWindow() => ShowWindow(GameplayTagEditorSession.GetDatabase());

        /// <summary>打开或聚焦窗口并选择指定数据库。</summary>
        /// <param name="database">需要编辑的数据库；为 null 时尝试恢复会话选择。</param>
        public static void ShowWindow(GameplayTagDatabase database)
        {
            GAS_SettingWindow window = GetWindow<GAS_SettingWindow>();
            window.titleContent = new GUIContent("Gameplay Tags");
            window.minSize = new Vector2(700f, 420f);
            window.initialDatabase = database != null ? database : GameplayTagEditorSession.GetDatabase();
            GameplayTagEditorSession.SetDatabase(window.initialDatabase);
            window.controller?.SetDatabase(window.initialDatabase, true);
            window.Show();
        }

        #region 生命周期
        // 域重载后优先通过 SessionState 的 Asset GUID 恢复数据库对象。
        private void OnEnable()
        {
            GameplayTagDatabase restored = GameplayTagEditorSession.GetDatabase();
            if (restored != null) initialDatabase = restored;
        }

        // 加载 UXML，创建具体 View，并通过接口注入 Controller。
        private void CreateGUI()
        {
            ReleaseEditor();
            rootVisualElement.Clear();
            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            VisualTreeAsset rowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowUxmlPath);
            if (windowAsset == null || rowAsset == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "Gameplay Tag Editor UXML assets are missing.", HelpBoxMessageType.Error));
                return;
            }
            windowAsset.CloneTree(rootVisualElement);
            view = new GameplayTagEditorView(rootVisualElement, rowAsset);
            controller = new GameplayTagEditorController(view);
            controller.SetDatabase(
                initialDatabase != null ? initialDatabase : GameplayTagEditorSession.GetDatabase(), true);
        }

        // 域重载或窗口关闭时按依赖顺序释放 Controller 与 View。
        private void OnDisable() => ReleaseEditor();

        // 先释放 Controller 意图订阅，再释放具体 View 的控件回调。
        private void ReleaseEditor()
        {
            controller?.Dispose();
            controller = null;
            view?.Dispose();
            view = null;
        }
        #endregion
    }
}
#endif
