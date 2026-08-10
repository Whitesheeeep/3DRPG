#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using WS_Modules.GAS.AttributeSystem;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.GAS.GameplayCue;
using WS_Modules.GAS.TAG;

namespace WS_Modules.GAS.Editor
{
    /// <summary>使用单一 EditorWindow 选项卡承载 Tag、Attribute、Effect 与 Ability 编辑页面。</summary>
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
        [SerializeField] private GameplayEffectData requestedGameplayEffect;
        [SerializeField] private GameplayAbilityData requestedGameplayAbility;
        [SerializeField] private GameplayCueDatabase requestedGameplayCueDatabase;
        [SerializeField] private GameplayAttributeEditorPage requestedAttributePage =
            GameplayAttributeEditorPage.Specs;

        private Button gameplayTagsTab;
        private Button gameplayAttributesTab;
        private Button gameplayEffectsTab;
        private Button gameplayAbilitiesTab;
        private Button gameplayCuesTab;
        private VisualElement contentHost;
        private IGameplayTagWindow gameplayTagWindow;
        private IGameplayAttributeWindow gameplayAttributeWindow;
        private IGameplayEffectWindow gameplayEffectWindow;
        private IGameplayAbilityWindow gameplayAbilityWindow;
        private IGameplayCueWindow gameplayCueWindow;
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

        /// <summary>打开或聚焦 GAS 设置窗口，并显示指定模块。</summary>
        /// <param name="module">需要显示的模块。</param>
        public static void ShowWindow(GASEditorModule module)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            window.SelectModule(module);
            window.Show();
        }

        /// <summary>打开 Tag 页并选择指定数据库。</summary>
        /// <param name="database">目标数据库；null 时恢复 SessionState 数据库。</param>
        public static void ShowGameplayTags(GameplayTagDatabase database)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayTagDatabase target = database != null
                ? database
                : GameplayTagEditorSession.GetDatabase();
            window.requestedTagDatabase = target;
            window.SelectModule(GASEditorModule.GameplayTags);
            if (window.gameplayTagWindow != null)
            {
                window.gameplayTagWindow.SetDatabase(target, true);
                window.requestedTagDatabase = null;
            }

            window.Show();
        }

        /// <summary>打开 Attribute 页并定位作者资源与子页面。</summary>
        /// <param name="registry">目标 Registry；null 时恢复 SessionState。</param>
        /// <param name="set">目标 AttributeSet；null 时恢复 SessionState。</param>
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

        /// <summary>打开 GE 页并选择指定 GameplayEffectData。</summary>
        /// <param name="effect">目标 GE；null 时恢复 SessionState 资产。</param>
        public static void ShowGameplayEffect(GameplayEffectData effect)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayEffectData target = effect != null
                ? effect
                : GameplayEffectEditorSession.GetEffect();
            window.requestedGameplayEffect = target;
            window.SelectModule(GASEditorModule.GameplayEffects);
            if (window.gameplayEffectWindow != null)
            {
                window.gameplayEffectWindow.SetEffect(target, true);
                window.requestedGameplayEffect = null;
            }

            window.Show();
        }

        /// <summary>打开 GA 页并选择指定 GameplayAbilityData。</summary>
        /// <param name="ability">目标 GA；null 时恢复 SessionState 资产。</param>
        public static void ShowGameplayAbility(GameplayAbilityData ability)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayAbilityData target = ability != null
                ? ability
                : GameplayAbilityEditorSession.GetAbility();
            window.requestedGameplayAbility = target;
            window.SelectModule(GASEditorModule.GameplayAbilities);
            if (window.gameplayAbilityWindow != null)
            {
                window.gameplayAbilityWindow.SetAbility(target, true);
                window.requestedGameplayAbility = null;
            }

            window.Show();
        }

        /// <summary>打开 Cue 页面并选择指定的 Cue Database。</summary>
        /// <param name="database">要编辑的 Cue Database；为空时恢复上次数据库。</param>
        public static void ShowGameplayCues(GameplayCueDatabase database)
        {
            GAS_SettingWindow window = GetConfiguredWindow();
            GameplayCueDatabase target = database != null
                ? database
                : GameplayCueEditorSession.GetDatabase();
            window.requestedGameplayCueDatabase = target;
            window.SelectModule(GASEditorModule.GameplayCues);
            if (window.gameplayCueWindow != null)
            {
                window.gameplayCueWindow.SetDatabase(target, true);
                window.requestedGameplayCueDatabase = null;
            }

            window.Show();
        }

        // 菜单入口使用 SessionState 恢复上次选中的 Cue Database。
        [MenuItem("WSFrame/GAS/Gameplay Cues")]
        private static void ShowGameplayCuesMenu() => ShowGameplayCues(null);

        /// <summary>打开 Cue 页面并定位已注册的 CueData。</summary>
        /// <param name="cue">要定位的 CueData。</param>
        public static void ShowGameplayCue(GameplayCueData cue)
        {
            GameplayCueDatabase database = GameplayCueEditorSession.GetDatabase();
            if (cue != null)
            {
                GameplayCueEditorService service = new GameplayCueEditorService();
                System.Collections.Generic.List<GameplayCueDatabase> matches =
                    service.FindDatabasesContaining(cue);
                if (database == null || !matches.Contains(database))
                    database = matches.Count == 1 ? matches[0] : matches.Count > 0 ? matches[0] : null;
                if (matches.Count > 1)
                    Debug.LogWarning($"CueData '{cue.name}' 注册在多个 GameplayCueDatabase 中，已选择 '{database?.name}'。", cue);
            }

            GAS_SettingWindow window = GetConfiguredWindow();
            window.requestedGameplayCueDatabase = database;
            window.SelectModule(GASEditorModule.GameplayCues);
            if (window.gameplayCueWindow != null)
            {
                window.gameplayCueWindow.SetDatabase(database, true);
                window.gameplayCueWindow.SetCue(cue, false);
                window.requestedGameplayCueDatabase = null;
            }

            window.Show();
        }

        /// <summary>释放当前页面并在同一内容宿主中创建目标模块页面。</summary>
        /// <param name="module">需要显示的模块。</param>
        /// <exception cref="ArgumentOutOfRangeException">模块值未定义。</exception>
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
                    GameplayEffectData effect = requestedGameplayEffect != null
                        ? requestedGameplayEffect
                        : GameplayEffectEditorSession.GetEffect();
                    gameplayEffectWindow = new GameplayEffectWindow(contentHost, effect);
                    requestedGameplayEffect = null;
                    break;
                case GASEditorModule.GameplayAbilities:
                    GameplayAbilityData ability = requestedGameplayAbility != null
                        ? requestedGameplayAbility
                        : GameplayAbilityEditorSession.GetAbility();
                    gameplayAbilityWindow = new GameplayAbilityWindow(contentHost, ability);
                    requestedGameplayAbility = null;
                    break;
                case GASEditorModule.GameplayCues:
                    GameplayCueDatabase cueDatabase = requestedGameplayCueDatabase != null
                        ? requestedGameplayCueDatabase
                        : GameplayCueEditorSession.GetDatabase();
                    gameplayCueWindow = new GameplayCueWindow(contentHost, cueDatabase);
                    requestedGameplayCueDatabase = null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(module), module, "未知的 GAS 编辑模块。");
            }

            RefreshTabState();
        }

        #endregion

        #region 生命周期

        // 域重载后恢复上次模块，页面由 CreateGUI 统一重建。
        private void OnEnable() => requestedModule = RestoreActiveModule();

        // 加载宿主布局、订阅选项卡，并创建当前模块页面。
        private void CreateGUI()
        {
            ReleaseWindowContent();
            rootVisualElement.Clear();

            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "GAS Setting Window UXML asset is missing.",
                    HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            gameplayTagsTab = RequireElement<Button>("GameplayTagsTab");
            gameplayAttributesTab = RequireElement<Button>("GameplayAttributesTab");
            gameplayEffectsTab = RequireElement<Button>("GameplayEffectsTab");
            gameplayAbilitiesTab = RequireElement<Button>("GameplayAbilitiesTab");
            gameplayCuesTab = RequireElement<Button>("GameplayCuesTab");
            contentHost = RequireElement<VisualElement>("ContentHost");

            gameplayTagsTab.clicked += OnGameplayTagsClicked;
            gameplayAttributesTab.clicked += OnGameplayAttributesClicked;
            gameplayEffectsTab.clicked += OnGameplayEffectsClicked;
            gameplayAbilitiesTab.clicked += OnGameplayAbilitiesClicked;
            gameplayCuesTab.clicked += OnGameplayCuesClicked;

            hasActiveModule = false;
            SelectModule(requestedModule);
        }

        // 窗口关闭或域重载时释放子 MVC 和宿主事件。
        private void OnDisable() => ReleaseWindowContent();

        #endregion

        #region 事件处理

        // 各选项卡只请求宿主切换，不感知子页面内部 UI。
        private void OnGameplayTagsClicked() => SelectModule(GASEditorModule.GameplayTags);
        // Attribute 选项卡请求切换到 Attribute 子 MVC。
        private void OnGameplayAttributesClicked() =>
            SelectModule(GASEditorModule.GameplayAttributes);
        // Effect 选项卡请求切换到 GE 子 MVC。
        private void OnGameplayEffectsClicked() => SelectModule(GASEditorModule.GameplayEffects);
        // Ability 选项卡请求切换到 GA 子 MVC。
        private void OnGameplayAbilitiesClicked() => SelectModule(GASEditorModule.GameplayAbilities);
        // Cue 选项卡只请求宿主切换页面，不参与 Cue 详情编辑。
        private void OnGameplayCuesClicked() => SelectModule(GASEditorModule.GameplayCues);

        #endregion

        #region 页面与状态刷新

        // 释放当前子 MVC；Controller 会先解除 Undo、项目和 View 事件。
        private void ReleaseActivePage()
        {
            gameplayTagWindow?.Dispose();
            gameplayTagWindow = null;
            gameplayAttributeWindow?.Dispose();
            gameplayAttributeWindow = null;
            gameplayEffectWindow?.Dispose();
            gameplayEffectWindow = null;
            gameplayAbilityWindow?.Dispose();
            gameplayAbilityWindow = null;
            gameplayCueWindow?.Dispose();
            gameplayCueWindow = null;
            contentHost?.Clear();
            hasActiveModule = false;
        }

        // 仅通过 USS 状态类表达当前选项卡。
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
            gameplayCuesTab?.EnableInClassList(
                ActiveTabClass, activeModule == GASEditorModule.GameplayCues);
        }

        #endregion

        #region 内部辅助

        // 创建并配置唯一 GAS EditorWindow 实例。
        private static GAS_SettingWindow GetConfiguredWindow()
        {
            GAS_SettingWindow window = GetWindow<GAS_SettingWindow>();
            window.titleContent = new GUIContent("GAS Settings");
            window.minSize = new Vector2(730f, 500f);
            return window;
        }

        // 恢复有效 Session 模块，未知值回退到 Tag。
        private GASEditorModule RestoreActiveModule()
        {
            var module = (GASEditorModule)SessionState.GetInt(
                ActiveModuleSessionKey,
                (int)GASEditorModule.GameplayTags);
            return Enum.IsDefined(typeof(GASEditorModule), module)
                ? module
                : GASEditorModule.GameplayTags;
        }

        // 在改变页面前验证枚举值，避免产生无内容的激活选项卡。
        private static void ValidateModule(GASEditorModule module)
        {
            if (!Enum.IsDefined(typeof(GASEditorModule), module))
                throw new ArgumentOutOfRangeException(nameof(module), module, "未知的 GAS 编辑模块。");
        }

        // 查询必需 UXML 元素，缺失时立即暴露布局与代码契约不一致。
        private T RequireElement<T>(string name) where T : VisualElement
        {
            T element = rootVisualElement.Q<T>(name);
            if (element == null)
                throw new InvalidOperationException(
                    $"GAS Setting Window UXML is missing required element '{name}'.");
            return element;
        }

        // 清除一次性 Attribute 导航参数，后续切页使用 SessionState。
        private void ClearRequestedAttributeAssets()
        {
            requestedAttributeRegistry = null;
            requestedAttributeSet = null;
            requestedAttributePage = GameplayAttributeEditorSession.Page;
        }

        // 按页面、回调和控件引用顺序释放窗口资源。
        private void ReleaseWindowContent()
        {
            ReleaseActivePage();
            if (gameplayTagsTab != null) gameplayTagsTab.clicked -= OnGameplayTagsClicked;
            if (gameplayAttributesTab != null)
                gameplayAttributesTab.clicked -= OnGameplayAttributesClicked;
            if (gameplayEffectsTab != null) gameplayEffectsTab.clicked -= OnGameplayEffectsClicked;
            if (gameplayAbilitiesTab != null)
                gameplayAbilitiesTab.clicked -= OnGameplayAbilitiesClicked;
            if (gameplayCuesTab != null)
                gameplayCuesTab.clicked -= OnGameplayCuesClicked;

            gameplayTagsTab = null;
            gameplayAttributesTab = null;
            gameplayEffectsTab = null;
            gameplayAbilitiesTab = null;
            gameplayCuesTab = null;
            contentHost = null;
        }

        #endregion
    }
}
#endif
