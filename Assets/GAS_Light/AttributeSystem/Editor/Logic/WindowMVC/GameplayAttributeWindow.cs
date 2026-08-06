#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;
using WS_Modules.GAS.AttributeSystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 GAS 主窗口内容区域中组合 Attribute Editor View 与 Controller。</summary>
    public sealed class GameplayAttributeWindow : IGameplayAttributeWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath =
            "Assets/GAS_Light/AttributeSystem/Editor/Style/GameplayAttributeEditor.uxml";

        private readonly VisualElement pageRoot;
        private IGameplayAttributeEditorView view;
        private GameplayAttributeEditorController controller;
        private bool disposed;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameplayAttributeRegistry CurrentRegistry => GameplayAttributeEditorSession.GetRegistry();

        /// <inheritdoc />
        public GameplayAttributeSet CurrentSet => GameplayAttributeEditorSession.GetAttributeSet();

        #endregion

        #region 生命周期

        /// <summary>在指定宿主中创建 Attribute 模块并恢复目标子页面与资产。</summary>
        /// <param name="contentHost">GAS 主窗口唯一内容容器。</param>
        /// <param name="registry">目标 Registry；null 时恢复 Session。</param>
        /// <param name="set">目标 AttributeSet；null 时恢复 Session。</param>
        /// <param name="page">初始子页面。</param>
        /// <exception cref="ArgumentNullException">contentHost 为 null。</exception>
        public GameplayAttributeWindow(
            VisualElement contentHost,
            GameplayAttributeRegistry registry,
            GameplayAttributeSet set,
            GameplayAttributeEditorPage page)
        {
            if (contentHost == null) throw new ArgumentNullException(nameof(contentHost));
            pageRoot = new VisualElement { name = "GameplayAttributePage" };
            pageRoot.AddToClassList("gas-setting-module-page");
            contentHost.Add(pageRoot);
            CreateEditor(registry, set, page);
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
        public void SelectPage(GameplayAttributeEditorPage page)
        {
            if (disposed) return;
            controller?.SelectPage(page);
        }

        /// <inheritdoc />
        public void SetRegistry(GameplayAttributeRegistry registry, bool restoreSelection)
        {
            if (disposed) return;
            controller?.SetRegistry(registry, restoreSelection);
        }

        /// <inheritdoc />
        public void SetAttributeSet(GameplayAttributeSet set, bool restoreSelection)
        {
            if (disposed) return;
            controller?.SetAttributeSet(set, restoreSelection);
        }

        #endregion

        #region 内部辅助

        // 加载 UXML 并创建既有 MVC；缺少资源时在当前页面内显示明确错误。
        private void CreateEditor(
            GameplayAttributeRegistry registry,
            GameplayAttributeSet set,
            GameplayAttributeEditorPage page)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (asset == null)
            {
                pageRoot.Add(new HelpBox(
                    "Gameplay Attribute Editor UXML asset is missing.",
                    HelpBoxMessageType.Error));
                return;
            }

            asset.CloneTree(pageRoot);
            view = new GameplayAttributeEditorView(pageRoot);
            controller = new GameplayAttributeEditorController(view);
            if (registry != null) controller.SetRegistry(registry, true);
            if (set != null) controller.SetAttributeSet(set, true);
            controller.SelectPage(page);
        }

        #endregion
    }
}
#endif
