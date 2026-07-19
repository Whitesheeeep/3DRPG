using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace WS_Modules
{
    public partial class FrameSettingWindow : EditorWindow
    {
        [MenuItem("Tools/WSFrame/Global Setting %#W")]
        private static void ShowWindow()
        {
            var window = GetWindow<FrameSettingWindow>();
            window.titleContent = new GUIContent("Frame Setting");
            window.minSize = new Vector2(560, 400);
            window.Show();
        }

        // 构筑普通 UI 界面，展示模块列表和对应设置项
        public VisualTreeAsset visualTreeAsset;

        // EventSystem 面板的单个事件项模板，请在 Inspector 中拖入 EventInfoItem.uxml。
        [SerializeField] private VisualTreeAsset eventInfoTemplate;

        // EventSystem 面板根模板，请在 Inspector 中拖入 EventSystemPanel.uxml；未拖入时会从项目中兜底加载。
        [SerializeField] private VisualTreeAsset eventSystemTemplate;

        // PoolSystem 面板模板，请在 Inspector 中拖入 PoolSystemView.uxml；未拖入时会从项目中兜底加载。
        [SerializeField] private VisualTreeAsset poolSystemTemplate;

        private ListView _listView;

        // 注册其他窗口
        private FrameModuleRegistry _moduleRegistry;
        // 查找根对象
        private readonly IFrameRootResolver _frameRootResolver = new SceneFrameRootResolver();
        // Odin 窗口绘制器
        private readonly IInspectorDrawer _inspectorDrawer = new OdinInspectorDrawer();

        // 需要与其他数据交互的窗口
        private readonly EventSystemView _eventSystemView = new EventSystemView();
        private readonly UISystemView _uiSystemView = new UISystemView();
        private PoolSystemView _poolSystemView;

        private void CreateGUI()
        {
            if (!TryLoadVisualTreeAsset()) return;

            // 1) 先把 UXML 克隆到窗口根节点
            visualTreeAsset.CloneTree(rootVisualElement);

            // 2) 初始化依赖视图：Pooling 面板依赖根对象查找器
            _poolSystemView ??= new PoolSystemView(GetFrameRoot);

            // 3) 准备模块注册表和模块数据
            _moduleRegistry = BuildDefaultModuleRegistry();
            var modules = _moduleRegistry.GetAll();

            // 4) 查找并配置 UI 容器
            _listView = rootVisualElement.Q<ListView>("ModuleList");
            var settingContainer = rootVisualElement.Q<VisualElement>("SettingContainer");
            if (_listView == null || settingContainer == null)
            {
                rootVisualElement.Add(new HelpBox(
                    "FrameSettingWindow UXML is missing required elements: ModuleList or SettingContainer.",
                    HelpBoxMessageType.Error));
                return;
            }

            ConfigureSettingContainer(settingContainer);

            // 5) 绑定模块列表和选择事件
            SetupModuleListView(modules, settingContainer);

            // 6) 默认选中第一个模块，保证窗口打开后就有内容
            SelectDefaultModule(modules);
        }

        // 如果没有手动赋值，就从项目里兜底加载主 UXML。
        private bool TryLoadVisualTreeAsset()
        {
            if (visualTreeAsset != null) return true;

            var guids = AssetDatabase.FindAssets("FrameSettingUxml t:VisualTreeAsset");
            if (guids.Length == 0) return false;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            return visualTreeAsset != null;
        }

        // 右侧设置区域需要占满剩余空间，并避免内容溢出到外部。
        private static void ConfigureSettingContainer(VisualElement settingContainer)
        {
            if (settingContainer == null) return;

            settingContainer.AddToClassList("frame-setting-container");
        }

        // 左侧模块列表：统一在这里绑定显示文本、选择事件和样式。
        private void SetupModuleListView(List<FrameModuleDescriptor> modules, VisualElement settingContainer)
        {
            if (_listView == null) return;

            _listView.fixedItemHeight = 40;
            _listView.makeItem = () =>
            {
                var label = new Label();
                label.AddToClassList("frame-module-list-item");
                return label;
            };
            _listView.bindItem = (element, i) =>
            {
                if (element is Label label)
                {
                    label.text = modules[i].DisplayName;
                }
            };
            _listView.itemsSource = modules;
            _listView.selectionType = SelectionType.Single;
            _listView.selectionChanged += objects => OnModuleSelectionChanged(objects, settingContainer);
        }

        // 切换模块时先清空右侧区域，再渲染当前模块的设置 UI。
        private void OnModuleSelectionChanged(IEnumerable<object> objects, VisualElement settingContainer)
        {
            settingContainer?.Clear();
            if (objects == null) return;

            var selectedModule = objects.Select(item => item as FrameModuleDescriptor).FirstOrDefault();

            if (selectedModule == null) return;

            var moduleContent = new VisualElement();
            moduleContent.AddToClassList("frame-module-content");
            settingContainer?.Add(moduleContent);

            var label = new Label($"Settings for {selectedModule.DisplayName}");
            label.AddToClassList("frame-module-settings-title");
            moduleContent.Add(label);

            var body = new VisualElement();
            body.AddToClassList("frame-module-settings-body");
            moduleContent.Add(body);

            DrawModuleSettings(selectedModule.Id, body);
        }

        // 打开窗口后默认选中第一项，避免右侧空白。
        private void SelectDefaultModule(List<FrameModuleDescriptor> modules)
        {
            if (_listView == null || modules == null || modules.Count == 0) return;
            _listView.selectedIndex = 0;
        }

        #region UI 入口与模块分发
        private void DrawModuleSettings(string moduleName, VisualElement container)
        {
            if (_moduleRegistry != null && _moduleRegistry.TryGet(moduleName, out var module) && module.Enabled)
            {
                module.DrawAction?.Invoke(container);
                return;
            }

            container.Add(new HelpBox($"Module '{moduleName}' not found in registry.", HelpBoxMessageType.Warning));
        }

        private WSFrameRoot GetFrameRoot(VisualElement container)
        {
            var frameRoot = _frameRootResolver.Resolve();

            if (frameRoot == null)
            {
                container.Add(new HelpBox(
                    "WSFrameRoot object not found in the scene. Please create a GameObject named 'WSFrameRoot' in the scene, or use the FrameRoot prefab provided in the FrameControl folder.",
                    HelpBoxMessageType.Error));
            }

            return frameRoot;
        }
        // 整个绘画逻辑结束

        private void DrawOdinProperty(object target, VisualElement container)
        {
            _inspectorDrawer.Draw(target, CreateScrollableModuleBody(container));
        }

        private void DrawOdinProperty(Object target, string propertyPath, VisualElement container)
        {
            _inspectorDrawer.DrawProperty(target, propertyPath, CreateScrollableModuleBody(container));
        }

        private void DrawOdinPropertyInline(Object target, string propertyPath, VisualElement container)
        {
            _inspectorDrawer.DrawProperty(target, propertyPath, container);
        }

        private void DrawUnityProperty(Object target, string propertyPath, VisualElement container)
        {
            _inspectorDrawer.DrawUnityProperty(target, propertyPath, CreateScrollableModuleBody(container));
        }

        private static ScrollView CreateScrollableModuleBody(VisualElement container)
        {
            var scrollView = new ScrollView();
            scrollView.AddToClassList("frame-module-settings-scroll");
            container.Add(scrollView);
            return scrollView;
        }

        private bool TryGetFrameSetting(VisualElement container, out WSFrameRoot wsFrameRoot)
        {
            wsFrameRoot = GetFrameRoot(container);
            if (wsFrameRoot == null) return false;

            if (wsFrameRoot.FrameSetting == null)
            {
                container.Add(new HelpBox("FrameSetting is missing in WSFrameRoot.", HelpBoxMessageType.Warning));
                return false;
            }

            return true;
        }

        #region 各个模块的 Draw
        private void DrawFrameRootSettings(VisualElement container)
        {
            var wsFrameRoot = GetFrameRoot(container);
            if (wsFrameRoot == null) return;

            // Draw FrameRoot specific properties (like Audio shortcuts in the root)
            // But maybe we just want to draw the whole FrameRoot component Inspector
            DrawOdinProperty(wsFrameRoot, container);
        }

        private void DrawLogSettings(VisualElement container)
        {
            if (!TryGetFrameSetting(container, out var wsFrameRoot)) return;

            DrawOdinProperty(wsFrameRoot.FrameSetting, "logSetting", container);
        }

        private void DrawAudioSettings(VisualElement container)
        {
            if (!TryGetFrameSetting(container, out var wsFrameRoot)) return;

            // Also draw global volume controls from FrameRoot?
            var globalAudioTitle = new Label("Global Audio Controls (from FrameRoot):");
            globalAudioTitle.AddToClassList("frame-setting-section-title");
            container.Add(globalAudioTitle);
            // This is tricky because these are properties on FrameRoot, not a separate object.
            // We can use an IMGUI container to draw specific properties using Odin or standard API

            // container.Add(audioControls);

            var audioSystemTitle = new Label("Audio System Settings:");
            audioSystemTitle.AddToClassList("frame-setting-section-title-spaced");
            container.Add(audioSystemTitle);
            DrawOdinProperty(wsFrameRoot.FrameSetting, "audioSystemSetting", container);
        }

        private void DrawResSystemSettings(VisualElement container)
        {
            if (!TryGetFrameSetting(container, out var wsFrameRoot)) return;

            // Draw ResLoadType enum
            DrawOdinProperty(wsFrameRoot.FrameSetting, "resLoadType", container);
        }

        private void DrawSceneSystemSettings(VisualElement container)
        {
            container.Add(new Label("Scene System Settings (Not Implemented)"));
        }

        private void DrawUISystemSettings(VisualElement container)
        {
            if (!TryGetFrameSetting(container, out var wsFrameRoot)) return;

            _uiSystemView.Draw(container, wsFrameRoot.FrameSetting);
        }

        private void DrawEventSystemSettings(VisualElement container)
        {
            _eventSystemView.Draw(container, eventInfoTemplate, eventSystemTemplate);
        }

        private void DrawPoolingSettings(VisualElement container)
        {
            if (TryGetFrameSetting(container, out var wsFrameRoot))
            {
                var poolingSettingSection = new VisualElement();
                poolingSettingSection.AddToClassList("frame-module-inline-section");
                var poolingSettingTitle = new Label("Pooling Settings");
                poolingSettingTitle.AddToClassList("frame-setting-section-title");
                poolingSettingSection.Add(poolingSettingTitle);
                container.Add(poolingSettingSection);

                DrawOdinPropertyInline(wsFrameRoot.FrameSetting, "poolingSetting", poolingSettingSection);
            }

            _poolSystemView ??= new PoolSystemView(GetFrameRoot);
            _poolSystemView.Draw(container, poolSystemTemplate);
        }

        #endregion
        #endregion

    }
}
