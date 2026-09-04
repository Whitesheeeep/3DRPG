#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.UIModule.Editor;

namespace WS_Modules.Baking.Editor
{
    /// <summary>显示任意烘焙结果数据源最终表格的独立 EditorWindow。</summary>
    public sealed class BakedResultViewerWindow : EditorWindow
    {
        #region 字段

        [SerializeField] private UnityEngine.Object serializedSource;
        private IBakedResultDataSource source;
        private BakedResultViewerController controller;

        #endregion

        #region 公开入口

        /// <summary>打开或切换到指定烘焙结果数据源。</summary>
        /// <param name="dataSource">实现烘焙结果接口的数据源。</param>
        /// <exception cref="ArgumentNullException">数据源为空时抛出。</exception>
        public static void Open(IBakedResultDataSource dataSource)
        {
            if (dataSource == null) throw new ArgumentNullException(nameof(dataSource));
            var window = GetWindow<BakedResultViewerWindow>();
            window.source = dataSource;
            window.serializedSource = dataSource as UnityEngine.Object;
            window.titleContent = new GUIContent("烘焙结果");
            window.minSize = new Vector2(720f, 360f);
            window.Show();
            window.controller?.Bind(dataSource);
        }

        /// <summary>刷新当前正在显示指定数据源的窗口。</summary>
        /// <param name="dataSource">可能被重新烘焙的数据源。</param>
        public static void RefreshIfDisplaying(IBakedResultDataSource dataSource)
        {
            if (dataSource == null) return;
            BakedResultViewerWindow[] windows = Resources.FindObjectsOfTypeAll<BakedResultViewerWindow>();
            for (int index = 0; index < windows.Length; index++)
                if (ReferenceEquals(windows[index].source, dataSource))
                    windows[index].controller?.Refresh();
        }

        #endregion

        #region Unity 生命周期

        /// <summary>创建窗口 UI Toolkit 视觉树和 MVC 连接。</summary>
        private void CreateGUI()
        {
            // UI Toolkit 重建视觉树时先释放旧 Controller，避免同一窗口重复订阅按钮事件。
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            string windowUxmlPath = UxmlUssPathConstants.Uxml.AssetsScriptsWSFrameUtilitiesEditorBakingStyleBakedResultViewerWindow;
            VisualTreeAsset template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(windowUxmlPath);
            if (template == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到烘焙结果窗口 UXML：{windowUxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            template.CloneTree(rootVisualElement);
            var view = new BakedResultViewerView(rootVisualElement);
            controller = new BakedResultViewerController(view, new BakedResultEditorService());
            IBakedResultDataSource restoredSource = source ?? serializedSource as IBakedResultDataSource;
            if (restoredSource != null) controller.Bind(restoredSource);
            else view.ShowUnavailableSource();
        }

        /// <summary>窗口禁用时释放 View 和 Controller。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }

        #endregion
    }
}
#endif
