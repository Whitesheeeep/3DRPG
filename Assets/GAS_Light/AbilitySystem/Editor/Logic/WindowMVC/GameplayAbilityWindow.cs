#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;
using WS_Modules.GAS.GameplayAbilitySystem;

namespace WS_Modules.GAS.Editor
{
    /// <summary>在 GAS 主窗口中组合并管理 Gameplay Ability Editor 的 MVC 生命周期。</summary>
    public sealed class GameplayAbilityWindow : IGameplayAbilityWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath =
            "Assets/GAS_Light/AbilitySystem/Editor/Style/GameplayAbilityEditor.uxml";

        private readonly VisualElement pageRoot;
        private IGameplayAbilityEditorView view;
        private GameplayAbilityEditorController controller;
        private bool disposed;

        #endregion

        #region 属性

        /// <inheritdoc />
        public GameplayAbilityData CurrentAbility => GameplayAbilityEditorSession.GetAbility();

        #endregion

        #region 生命周期

        /// <summary>在指定宿主中创建 GA 页面，并优先定位传入的 Ability。</summary>
        /// <param name="contentHost">GAS 主窗口的内容宿主。</param>
        /// <param name="ability">需要编辑的 Ability；为 null 时恢复 Session 选择。</param>
        /// <exception cref="ArgumentNullException">宿主为 null。</exception>
        public GameplayAbilityWindow(VisualElement contentHost, GameplayAbilityData ability)
        {
            if (contentHost == null) throw new ArgumentNullException(nameof(contentHost));
            pageRoot = new VisualElement { name = "GameplayAbilityPage" };
            pageRoot.AddToClassList("gas-setting-module-page");
            contentHost.Add(pageRoot);
            CreateEditor(ability);
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
        public void SetAbility(GameplayAbilityData ability, bool restoreSelection)
        {
            if (disposed) return;
            controller?.SetAbility(ability, restoreSelection);
        }

        #endregion

        #region 内部辅助

        // 加载页面资源并建立 MVC；资源缺失时在当前宿主内给出明确错误。
        private void CreateEditor(GameplayAbilityData ability)
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (asset == null)
            {
                pageRoot.Add(new HelpBox(
                    "Gameplay Ability Editor UXML asset is missing.",
                    HelpBoxMessageType.Error));
                return;
            }

            asset.CloneTree(pageRoot);
            view = new GameplayAbilityEditorView(pageRoot);
            controller = new GameplayAbilityEditorController(view);
            if (ability != null) controller.SetAbility(ability, true);
        }

        #endregion
    }
}
#endif
