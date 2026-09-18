#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace RPG.RedDotSystemNS.Editor
{
    /// <summary>承载红点节点设置页和 Play Mode Runtime Debugger 的 UI Toolkit EditorWindow。</summary>
    public sealed class RedDotEditorWindow : EditorWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath = UxmlUssPathConstants.Uxml.AssetsScriptsRedDotSystemEditorRedDotEditorWindow;
        #endregion

        #region 依赖字段

        // 运行时调试页 MVC 依赖。
        private RedDotDebuggerView view;
        private RedDotDebuggerController controller;

        // 静态节点设置页 MVC 依赖。
        private RedDotNodeSettingsView nodeSettingsView;
        private RedDotNodeSettingsController nodeSettingsController;

        // 页签按钮和内容容器依赖。
        private Button settingsTabButton;
        private Button debuggerTabButton;
        private VisualElement settingsTabContent;
        private VisualElement debuggerTabContent;

        #endregion

        #region 公开入口

        /// <summary>打开红点节点设置和运行时调试窗口。</summary>
        [MenuItem("Tools/RPG/Red Dot Editor")]
        public static void Open()
        {
            RedDotEditorWindow window = GetWindow<RedDotEditorWindow>();
            window.titleContent = new GUIContent("Red Dot Editor");
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        #endregion

        #region Unity 生命周期

        /// <summary>加载 UXML/USS 并建立窗口 MVC 连接。</summary>
        private void CreateGUI()
        {
            ReleaseMvc();
            rootVisualElement.Clear();

            VisualTreeAsset template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (template == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到红点调试窗口 UXML：{WindowUxmlPath}", HelpBoxMessageType.Error));
                Debug.LogError($"[RedDotDebugger] 窗口创建失败，UXML 不存在：{WindowUxmlPath}");
                return;
            }

            template.CloneTree(rootVisualElement);

            BindTabControls();
            view = new RedDotDebuggerView(rootVisualElement);
            controller = new RedDotDebuggerController(view);
            nodeSettingsView = new RedDotNodeSettingsView(rootVisualElement);
            nodeSettingsController = new RedDotNodeSettingsController(nodeSettingsView);
            ShowTab(true);
            Debug.Log("[RedDotEditor] 节点设置与 Runtime Debugger UI 已创建。");
        }

        /// <summary>窗口禁用时释放 Play Mode、Framework 和 View 订阅。</summary>
        private void OnDisable()
        {
            ReleaseMvc();
            Debug.Log("[RedDotEditor] EditorWindow 已关闭并释放连接。");
        }

        #endregion

        #region 内部辅助

        /// <summary>按 Controller 后 View 的顺序释放 MVC 对象。</summary>
        private void ReleaseMvc()
        {
            controller?.Dispose();
            controller = null;
            view?.Dispose();
            view = null;
            nodeSettingsController?.Dispose();
            nodeSettingsController = null;
            nodeSettingsView?.Dispose();
            nodeSettingsView = null;
            if (settingsTabButton != null)
            {
                settingsTabButton.clicked -= ShowSettingsTab;
            }

            if (debuggerTabButton != null)
            {
                debuggerTabButton.clicked -= ShowDebuggerTab;
            }

            settingsTabButton = null;
            debuggerTabButton = null;
            settingsTabContent = null;
            debuggerTabContent = null;
        }

        #endregion

        #region 页签管理

        /// <summary>查询页签按钮和内容区域，并注册切换事件。</summary>
        private void BindTabControls()
        {
            settingsTabButton = rootVisualElement.Q<Button>("SettingsTabButton");
            debuggerTabButton = rootVisualElement.Q<Button>("DebuggerTabButton");
            settingsTabContent = rootVisualElement.Q<VisualElement>("SettingsTabContent");
            debuggerTabContent = rootVisualElement.Q<VisualElement>("DebuggerTabContent");
            if (settingsTabButton == null || debuggerTabButton == null ||
                settingsTabContent == null || debuggerTabContent == null)
            {
                throw new InvalidOperationException("[RedDotEditor] UXML 缺少页签控件。");
            }

            settingsTabButton.clicked += ShowSettingsTab;
            debuggerTabButton.clicked += ShowDebuggerTab;
        }

        /// <summary>显示节点设置页签。</summary>
        private void ShowSettingsTab() => ShowTab(true);

        /// <summary>显示 Runtime Debugger 页签。</summary>
        private void ShowDebuggerTab() => ShowTab(false);

        /// <summary>切换页签显示状态并更新按钮样式。</summary>
        /// <param name="showSettings">是否显示节点设置页。</param>
        private void ShowTab(bool showSettings)
        {
            if (settingsTabContent == null || debuggerTabContent == null)
            {
                return;
            }

            settingsTabContent.style.display = showSettings
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            debuggerTabContent.style.display = showSettings
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            settingsTabButton?.EnableInClassList("tab-button-active", showSettings);
            debuggerTabButton?.EnableInClassList("tab-button-active", !showSettings);
        }

        #endregion
    }
}
#endif
