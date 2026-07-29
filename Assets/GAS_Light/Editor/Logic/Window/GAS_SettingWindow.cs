#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用选项卡承载 Gameplay Tag、Attribute、Effect 与 Ability 编辑页面。</summary>
    public sealed class GAS_SettingWindow : EditorWindow, IGASSettingWindow
    {
        #region 常量与字段

        private const string WindowUxmlPath = "Assets/GAS_Light/Editor/Style/GASSettingWindow.uxml";
        private const string ActiveModuleSessionKey = "WSFrame.GAS.SettingWindow.ActiveModule";
        private const string ActiveTabClass = "gas-setting-tab--active";

        [SerializeField] private GASEditorModule requestedModule = GASEditorModule.GameplayTags;
        [SerializeField] private GameplayTagDatabase requestedTagDatabase;
        [SerializeField] private GameplayAttributeRegistry requestedAttributeRegistry;
        [SerializeField] private GameplayAttributeSet requestedAttributeSet;
        [SerializeField] private GameplayAttributeEditorPage requestedAttributePage =
            GameplayAttributeEditorPage.Specs;

        private Button gameplayTagsTab;
        private Button gameplayAttributesTab;
        private Button gameplayEffectsTab;
        private Button gameplayAbilitiesTab;
        private VisualElement contentHost;
        private IGameplayTagWindow gameplayTagWindow;
        private IGameplayAttributeWindow gameplayAttributeWindow;
        private GASEditorModule activeModule = GASEditorModule.GameplayTags;
        private bool hasActiveModule;

        #endregion

        #region 属性

        /// <summary>获取当前显示的 GAS 编辑模块。</summary>
        public GASEditorModule ActiveModule => activeModule;

        #endregion

        #region 公开入口

        /// <summary>打开或聚焦 GAS 设置窗口，并恢复上次选项卡。</summary>
        [MenuItem("WSFrame/GAS/Settings")]
        public static void ShowWindow()
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            window.SelectModule(window.RestoreActiveModule());
            window.Show();
        }

        // 保留原 Gameplay Tags 菜单，但统一路由到主窗口的 Tag 选项卡。
        /*[MenuItem("WSFrame/GAS/Gameplay Tags")]
        private static void ShowGameplayTagsMenu() =>
            ShowGameplayTags(GameplayTagEditorSession.GetDatabase());*/

        /// <summary>打开或聚焦 GAS 设置窗口，并显示指定模块。</summary>
        /// <param name="module">需要显示的模块。</param>
        public static void ShowWindow(GASEditorModule module)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            window.SelectModule(module);
            window.Show();
        }

        /// <summary>打开主窗口的 Tag 选项卡，并选择指定数据库。</summary>
        /// <param name="database">需要编辑的数据库；null 时恢复 SessionState 数据库。</param>
        public static void ShowGameplayTags(GameplayTagDatabase database)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayTagDatabase targetDatabase =
                database != null ? database : GameplayTagEditorSession.GetDatabase();
            window.requestedTagDatabase = targetDatabase;
            window.SelectModule(GASEditorModule.GameplayTags);
            if (window.gameplayTagWindow != null)
            {
                window.gameplayTagWindow.SetDatabase(targetDatabase, true);
                window.requestedTagDatabase = null;
            }
            window.Show();
        }

        /// <summary>打开主窗口的 Attribute 选项卡，并定位到指定作者资源和子页面。</summary>
        /// <param name="registry">需要编辑的 Attribute Registry；null 时恢复 SessionState 资源。</param>
        /// <param name="set">需要编辑的 Attribute Set；null 时恢复 SessionState 资源。</param>
        /// <param name="page">需要显示的 Attribute 子页面。</param>
        public static void ShowGameplayAttributes(
            GameplayAttributeRegistry registry,
            GameplayAttributeSet set,
            GameplayAttributeEditorPage page)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayAttributeRegistry targetRegistry = registry != null
                ? registry
                : GameplayAttributeEditorSession.GetRegistry();
            GameplayAttributeSet targetSet = set != null
                ? set
                : GameplayAttributeEditorSession.GetAttributeSet();
            window.requestedAttributeRegistry = targetRegistry;
            window.requestedAttributeSet = targetSet;
            window.requestedAttributePage = page;
            window.SelectModule(GASEditorModule.GameplayAttributes);
            if (window.gameplayAttributeWindow != null)
            {
                window.gameplayAttributeWindow.SetRegistry(targetRegistry, true);
                window.gameplayAttributeWindow.SetAttributeSet(targetSet, true);
                window.gameplayAttributeWindow.SelectPage(page);
                window.ClearRequestedAttributeAssets();
            }

            window.Show();
        }

        /// <summary>切换当前选项卡；切换前释放旧页面，随后在同一内容宿主中创建目标页面。</summary>
        /// <param name="module">需要显示的模块。</param>
        /// <exception cref="ArgumentOutOfRangeException">模块值未在当前版本中定义。</exception>
        public void SelectModule(GASEditorModule module)
        {
            ValidateModule(module);
            requestedModule = module;
            SessionState.SetInt(ActiveModuleSessionKey, (int)module);

            if (contentHost == null) return;
            if (hasActiveModule && activeModule == module)
            {
                RefreshTabState();
                return;
            }

            ReleaseActivePage();
            activeModule = module;
            hasActiveModule = true;

            switch (module)
            {
                case GASEditorModule.GameplayTags:
                    GameplayTagDatabase database = requestedTagDatabase != null
                        ? requestedTagDatabase
                        : GameplayTagEditorSession.GetDatabase();
                    gameplayTagWindow = new GameplayTagWindow(contentHost, database, true);
                    requestedTagDatabase = null;
                    break;
                case GASEditorModule.GameplayAttributes:
                    gameplayAttributeWindow = new GameplayAttributeWindow(
                        contentHost,
                        requestedAttributeRegistry,
                        requestedAttributeSet,
                        requestedAttributePage);
                    ClearRequestedAttributeAssets();
                    break;
                case GASEditorModule.GameplayEffects:
                    ShowPlaceholder("Gameplay Effects", "Gameplay Effects editor is not implemented yet.");
                    break;
                case GASEditorModule.GameplayAbilities:
                    ShowPlaceholder("Gameplay Abilities", "Gameplay Abilities editor is not implemented yet.");
                    break;
            }

            RefreshTabState();
        }

        #endregion

        #region 生命周期

        // 域重载后恢复上次有效选项卡；页面本身由 CreateGUI 重建。
        private void OnEnable() => requestedModule = RestoreActiveModule();

        // 加载宿主布局、订阅选项卡，并在唯一内容区域中创建当前模块页面。
        private void CreateGUI()
        {
            ReleaseWindowContent();
            rootVisualElement.Clear();

            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "GAS Setting Window UXML asset is missing.", HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            gameplayTagsTab = RequireElement<Button>("GameplayTagsTab");
            gameplayAttributesTab = RequireElement<Button>("GameplayAttributesTab");
            gameplayEffectsTab = RequireElement<Button>("GameplayEffectsTab");
            gameplayAbilitiesTab = RequireElement<Button>("GameplayAbilitiesTab");
            contentHost = RequireElement<VisualElement>("ContentHost");

            gameplayTagsTab.clicked += OnGameplayTagsClicked;
            gameplayAttributesTab.clicked += OnGameplayAttributesClicked;
            gameplayEffectsTab.clicked += OnGameplayEffectsClicked;
            gameplayAbilitiesTab.clicked += OnGameplayAbilitiesClicked;

            hasActiveModule = false;
            SelectModule(requestedModule);
        }

        // 域重载或窗口关闭时释放当前子 MVC，并注销宿主选项卡回调。
        private void OnDisable() => ReleaseWindowContent();

        #endregion

        #region 事件处理

        // Tag 选项卡仅请求宿主切换，不了解 Tag 页面内部结构。
        private void OnGameplayTagsClicked() => SelectModule(GASEditorModule.GameplayTags);

        // Attribute 选项卡承载 Spec 与 Set 两个子页面。
        private void OnGameplayAttributesClicked() =>
            SelectModule(GASEditorModule.GameplayAttributes);

        // GE 选项卡切换到同一内容宿主中的占位页面。
        private void OnGameplayEffectsClicked() => SelectModule(GASEditorModule.GameplayEffects);

        // GA 选项卡切换到同一内容宿主中的占位页面。
        private void OnGameplayAbilitiesClicked() => SelectModule(GASEditorModule.GameplayAbilities);

        #endregion

        #region 页面与状态刷新

        // 释放当前模块页面；Tag 页面会对称注销 Controller、View 和 Undo 回调。
        private void ReleaseActivePage()
        {
            gameplayTagWindow?.Dispose();
            gameplayTagWindow = null;
            gameplayAttributeWindow?.Dispose();
            gameplayAttributeWindow = null;
            contentHost?.Clear();
            hasActiveModule = false;
        }

        // 为尚未实现的模块创建同一内容区域内的说明页面。
        private void ShowPlaceholder(string title, string message)
        {
            var placeholder = new VisualElement();
            placeholder.AddToClassList("gas-setting-placeholder");
            var titleLabel = new Label(title);
            titleLabel.AddToClassList("gas-setting-placeholder-title");
            var messageLabel = new Label(message);
            messageLabel.AddToClassList("gas-setting-placeholder-message");
            placeholder.Add(titleLabel);
            placeholder.Add(messageLabel);
            contentHost.Add(placeholder);
        }

        // 只通过 USS 状态类表达当前选项卡，避免在 C# 中写固定视觉样式。
        private void RefreshTabState()
        {
            gameplayTagsTab?.EnableInClassList(
                ActiveTabClass, activeModule == GASEditorModule.GameplayTags);
            gameplayAttributesTab?.EnableInClassList(
                ActiveTabClass, activeModule == GASEditorModule.GameplayAttributes);
            gameplayEffectsTab?.EnableInClassList(
                ActiveTabClass, activeModule == GASEditorModule.GameplayEffects);
            gameplayAbilitiesTab?.EnableInClassList(
                ActiveTabClass, activeModule == GASEditorModule.GameplayAbilities);
        }

        #endregion

        #region 内部辅助

        // 创建并配置唯一的 GAS EditorWindow 实例。
        private static GAS_SettingWindow GetConfiguredWindow()
        {
            GAS_SettingWindow window = GetWindow<GAS_SettingWindow>();
            window.titleContent = new GUIContent("GAS Settings");
            window.minSize = new Vector2(730f, 500f);
            return window;
        }

        // 恢复 SessionState 中的有效模块值，未知值回退到 Tag。
        private GASEditorModule RestoreActiveModule()
        {
            var module = (GASEditorModule)SessionState.GetInt(
                ActiveModuleSessionKey, (int)GASEditorModule.GameplayTags);
            return Enum.IsDefined(typeof(GASEditorModule), module)
                ? module
                : GASEditorModule.GameplayTags;
        }

        // 在改变页面前验证模块值，防止无内容但选项卡状态已改变。
        private static void ValidateModule(GASEditorModule module)
        {
            if (!Enum.IsDefined(typeof(GASEditorModule), module))
                throw new ArgumentOutOfRangeException(nameof(module), module, "未知的 GAS 编辑模块。");
        }

        // 查询必需 UXML 元素；缺失时立即暴露资源与代码契约不一致。
        private T RequireElement<T>(string name) where T : VisualElement
        {
            T element = rootVisualElement.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"GAS Setting Window UXML is missing required element '{name}'.");
            return element;
        }

        // 清除一次性导航参数，后续切换页面时改由 Attribute SessionState 恢复。
        private void ClearRequestedAttributeAssets()
        {
            requestedAttributeRegistry = null;
            requestedAttributeSet = null;
            requestedAttributePage = GameplayAttributeEditorSession.Page;
        }

        // 按页面、回调、控件引用的顺序释放宿主窗口资源。
        private void ReleaseWindowContent()
        {
            ReleaseActivePage();
            if (gameplayTagsTab != null) gameplayTagsTab.clicked -= OnGameplayTagsClicked;
            if (gameplayAttributesTab != null)
                gameplayAttributesTab.clicked -= OnGameplayAttributesClicked;
            if (gameplayEffectsTab != null) gameplayEffectsTab.clicked -= OnGameplayEffectsClicked;
            if (gameplayAbilitiesTab != null) gameplayAbilitiesTab.clicked -= OnGameplayAbilitiesClicked;

            gameplayTagsTab = null;
            gameplayAttributesTab = null;
            gameplayEffectsTab = null;
            gameplayAbilitiesTab = null;
            contentHost = null;
        }

        #endregion
    }
}
#endif
