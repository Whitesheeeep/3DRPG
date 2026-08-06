#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayEffect;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 GAS 主窗口内容区域中组合 Gameplay Effect Editor MVC。</summary>
    public sealed class GameplayEffectWindow : IGameplayEffectWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath =
            "Assets/GAS_Light/GameEffectSystem/Editor/Style/GameplayEffectEditor.uxml";

        private readonly VisualElement pageRoot;
        private IGameplayEffectEditorView view;
        private GameplayEffectEditorController controller;
        private bool disposed;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameplayEffectData CurrentEffect => GameplayEffectEditorSession.GetEffect();

        #endregion

        #region 生命周期

        /// <summary>在指定宿主中创建 GE 页面并恢复编辑状态。</summary>
        /// <param name="contentHost">GAS 主窗口的唯一内容容器。</param>
        /// <param name="effect">需要编辑的 GE；null 时恢复 Session 资产。</param>
        /// <exception cref="ArgumentNullException">contentHost 为 null。</exception>
        public GameplayEffectWindow(VisualElement contentHost, GameplayEffectData effect)
        {
            if (contentHost == null) throw new ArgumentNullException(nameof(contentHost));
            pageRoot = new VisualElement { name = "GameplayEffectPage" };
            pageRoot.AddToClassList("gas-setting-module-page");
            contentHost.Add(pageRoot);
            CreateEditor(effect);
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
        public void SetEffect(GameplayEffectData effect, bool restoreSelection)
        {
            if (disposed) return;
            controller?.SetEffect(effect, restoreSelection);
        }

        #endregion

        #region 内部辅助

        // 加载 UXML 并创建 MVC；资源缺失时在当前页面内显示错误。
        private void CreateEditor(GameplayEffectData effect)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (asset == null)
            {
                pageRoot.Add(new HelpBox(
                    "Gameplay Effect Editor UXML asset is missing.",
                    HelpBoxMessageType.Error));
                return;
            }

            asset.CloneTree(pageRoot);
            view = new GameplayEffectEditorView(pageRoot);
            controller = new GameplayEffectEditorController(view);
            if (effect != null) controller.SetEffect(effect, true);
        }

        #endregion
    }
}
#endif
