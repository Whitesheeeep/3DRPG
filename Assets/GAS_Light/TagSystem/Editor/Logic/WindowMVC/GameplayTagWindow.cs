#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 GAS 主窗口内容区域中组合 Gameplay Tag View 与 Controller。</summary>
    public sealed class GameplayTagWindow : IGameplayTagWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath =
            "Assets/GAS_Light/TagSystem/Editor/Style/GameplayTagEditor.uxml";
        private const string RowUxmlPath =
            "Assets/GAS_Light/TagSystem/Editor/Style/GameplayTagTreeRow.uxml";

        private readonly VisualElement pageRoot;
        private IGameplayTagEditorView view;
        private GameplayTagEditorController controller;
        private bool disposed;

        #endregion

        #region 属性

        /// <summary>获取当前通过 SessionState 选中的数据库。</summary>
        public GameplayTagDatabase CurrentDatabase => GameplayTagEditorSession.GetDatabase();

        #endregion

        #region 生命周期

        /// <summary>在指定内容宿主中创建 Tag 页面并初始化现有 MVC。</summary>
        /// <param name="contentHost">GAS 主窗口唯一的页面内容容器。</param>
        /// <param name="database">需要编辑的数据库；null 表示未选择数据库。</param>
        /// <param name="restoreSelection">是否从 SessionState 恢复节点选择。</param>
        /// <exception cref="ArgumentNullException">内容宿主为 null。</exception>
        public GameplayTagWindow(VisualElement contentHost, GameplayTagDatabase database, bool restoreSelection)
        {
            if (contentHost == null) throw new ArgumentNullException(nameof(contentHost));

            pageRoot = new VisualElement { name = "GameplayTagPage" };
            pageRoot.AddToClassList("gas-setting-module-page");
            contentHost.Add(pageRoot);
            CreateEditor(database, restoreSelection);
        }

        /// <summary>释放 Controller、View 和页面根节点；重复调用不会产生副作用。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            controller?.Dispose();
            controller = null;
            view?.Dispose();
            view = null;
            pageRoot.RemoveFromHierarchy();
            pageRoot.Clear();
        }

        #endregion

        #region 公开操作

        /// <summary>切换 Tag 数据库，并指定是否恢复该数据库的节点选择状态。</summary>
        /// <param name="database">需要编辑的数据库；null 表示清空选择。</param>
        /// <param name="restoreSelection">是否从 SessionState 恢复节点选择。</param>
        public void SetDatabase(GameplayTagDatabase database, bool restoreSelection)
        {
            if (disposed) return;
            GameplayTagEditorSession.SetDatabase(database);
            controller?.SetDatabase(database, restoreSelection);
        }

        #endregion

        #region 内部辅助

        // 加载 Tag UXML 并创建原有 View/Controller；资源缺失时在当前页面内显示错误。
        private void CreateEditor(GameplayTagDatabase database, bool restoreSelection)
        {
            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            VisualTreeAsset rowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RowUxmlPath);
            if (windowAsset == null || rowAsset == null)
            {
                pageRoot.Add(new HelpBox(
                    "Gameplay Tag Editor UXML assets are missing.", HelpBoxMessageType.Error));
                GameplayTagEditorSession.SetDatabase(database);
                return;
            }

            windowAsset.CloneTree(pageRoot);
            view = new GameplayTagEditorView(pageRoot, rowAsset);
            controller = new GameplayTagEditorController(view);
            controller.SetDatabase(database, restoreSelection);
        }

        #endregion
    }
}
#endif
