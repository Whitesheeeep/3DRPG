#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 GAS 主窗口中承载 Gameplay Cue 编辑 MVC 的嵌入页面。</summary>
    public sealed class GameplayCueWindow : IGameplayCueWindow
    {
        #region 字段与属性
        private const string WindowUxmlPath =
            "Assets/GAS_Light/GameplayCueSystem/Editor/Style/GameplayCueEditor.uxml";

        private readonly VisualElement pageRoot;
        private GameplayCueEditorController controller;
        private IGameplayCueEditorView view;
        private bool disposed;

        /// <summary>创建 Cue 页面并加载指定数据库。</summary>
        /// <param name="contentHost">宿主窗口内容容器。</param>
        /// <param name="database">初始数据库，可以为空。</param>
        public GameplayCueWindow(VisualElement contentHost, GameplayCueDatabase database)
        {
            if (contentHost == null) throw new ArgumentNullException(nameof(contentHost));

            pageRoot = new VisualElement { name = "GameplayCuePage" };
            pageRoot.AddToClassList("gas-setting-module-page");
            contentHost.Add(pageRoot);
            GameplayTagEditorSession.DatabaseChanged += OnTagDatabaseChanged;
            CreateEditor(database);
        }

        /// <summary>获取当前数据库。</summary>
        public GameplayCueDatabase CurrentDatabase => controller?.CurrentDatabase;

        /// <summary>获取当前 CueData。</summary>
        public GameplayCueData CurrentCue => controller?.CurrentCue;

        #endregion

        #region 生命周期与公开操作

        /// <summary>切换数据库。</summary>
        /// <param name="database">目标数据库。</param>
        /// <param name="restoreSelection">是否恢复 SessionState 选择。</param>
        public void SetDatabase(GameplayCueDatabase database, bool restoreSelection)
        {
            if (!disposed) controller?.SetDatabase(database, restoreSelection);
        }

        /// <summary>选中 CueData。</summary>
        /// <param name="cue">目标 CueData。</param>
        /// <param name="restoreSelection">是否允许恢复数据库选择。</param>
        public void SetCue(GameplayCueData cue, bool restoreSelection)
        {
            if (!disposed) controller?.SetCue(cue, restoreSelection);
        }

        /// <summary>按 Controller、View 和页面根节点顺序释放资源。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            GameplayTagEditorSession.DatabaseChanged -= OnTagDatabaseChanged;
            controller?.Dispose();
            controller = null;
            view?.Dispose();
            view = null;
            pageRoot.RemoveFromHierarchy();
            pageRoot.Clear();
        }

        #endregion

        #region 内部辅助

        // 加载 UXML 并创建 Cue View 与 Controller，错误直接显示在当前页面。
        private void CreateEditor(GameplayCueDatabase database)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (asset == null)
            {
                pageRoot.Add(new HelpBox("Gameplay Cue Editor UXML asset is missing.", HelpBoxMessageType.Error));
                return;
            }

            asset.CloneTree(pageRoot);
            view = new GameplayCueEditorView(pageRoot);
            view.SetTagDatabase(GameplayTagEditorSession.GetDatabase());
            controller = new GameplayCueEditorController(view);
            controller.SetDatabase(database, true);
        }

        // Tag 编辑器切换数据库后刷新 Cue 列表中的显示名称，不修改 CueData 序列化内容。
        private void OnTagDatabaseChanged(GameplayTagDatabase database) => view?.SetTagDatabase(database);

        #endregion
    }
}
#endif
