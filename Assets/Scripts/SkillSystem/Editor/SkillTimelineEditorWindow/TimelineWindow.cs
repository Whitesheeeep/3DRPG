#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 作为窗口组合根创建轨道模块、Document、播放时钟、ViewModel 与主视图，并按逆序释放。
    /// </summary>
    public sealed class TimelineWindow : EditorWindow
    {
        #region 资源路径与组合状态
        private const string UxmlPath =
            "Assets/Scripts/SkillSystem/Editor/SkillTimelineEditorWindow/EditorWindowStyle/SkillTimelineEditorWindow.uxml";
        private const string EditorConfigPath =
            "Assets/Scripts/SkillSystem/Editor/EditorConfig/SkillTimelineEditorConfig.asset";
        private EditorConfig editorConfig;
        private TrackModuleRegistry modules;
        private Document document;
        private PreviewSceneService previewSceneService;
        private PlaybackController playback;
        private SkillConfig pendingConfig;
        // 对数据层的通信
        private EditorViewModel viewModel;

        // 对 UI 层的通信
        private EditorView view;
        #endregion

        #region 原生 Inspector 桥接
        internal EditorViewModel ViewModel => viewModel;
        internal TrackModuleRegistry Modules => modules;
        internal object SelectedData => viewModel?.SelectedData;
        internal event Action NativeInspectorChanged;
        #endregion

        #region Window 生命周期
        // 打开或聚焦窗口；实际最小尺寸在 Editor Config 加载后应用。
        [MenuItem("WSFrame/Skill Timeline Editor", priority = 100)]
        private static void ShowWindow()
        {
            Open(null);
        }

        /// <summary>
        /// 打开或聚焦唯一的技能时间轴窗口，并在窗口组合完成后载入指定配置。
        /// </summary>
        /// <param name="config">需要载入的 SkillConfig；为空时仅打开窗口。</param>
        internal static void Open(SkillConfig config)
        {
            TimelineWindow window = GetWindow<TimelineWindow>();
            window.titleContent = new GUIContent("技能时间轴");
            window.minSize = new Vector2(800, 600);
            window.Show();
            window.Focus();
            window.OpenConfig(config);
        }

        /// <summary>
        /// 加载纯编辑器配置与 UXML，再按固定顺序创建窗口组合对象并刷新原生 Inspector。
        /// </summary>
        private void CreateGUI()
        {
            DisposeComposition();
            rootVisualElement.Clear();
            editorConfig = AssetDatabase.LoadAssetAtPath<EditorConfig>(EditorConfigPath);
            if (editorConfig == null)
            {
                rootVisualElement.Add(new HelpBox($"缺少 Editor 配置：{EditorConfigPath}", HelpBoxMessageType.Error));
                return;
            }

            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                rootVisualElement.Add(new HelpBox($"缺少 UXML：{UxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            minSize = editorConfig.MinimumWindowSize;
            tree.CloneTree(rootVisualElement);
            modules = TrackModuleRegistry.CreateDefault(editorConfig);
            document = new Document(modules.DocumentHandlers);
            previewSceneService = new PreviewSceneService(EditorSettings.instance);
            CompositePreview preview = new(previewSceneService, new PreviewActorFactory(),
                modules.CreatePreviewHandlers());
            playback = new PlaybackController(preview);
            viewModel = new EditorViewModel(
                document, playback, previewSceneService, modules, preview, preview, preview);
            view = new EditorView(rootVisualElement, editorConfig, modules);
            view.Bind(viewModel);
            viewModel.SelectionActivated += OnTimelineSelectionActivated;
            viewModel.InspectorChanged += OnInspectorChanged;

            // 首次双击资产时 CreateGUI 可能晚于打开入口，因此在组合完成后再消费待打开配置。
            if (pendingConfig != null)
            {
                SkillConfig config = pendingConfig;
                pendingConfig = null;
                viewModel.OpenConfig(config);
            }

            // Inspector Editor 可能跨本次内部组合重建继续存活，因此完成绑定后必须主动读取新组合。
            NativeInspectorChanged?.Invoke();
        }

        // 窗口禁用时释放全部 Editor 事件和序列化文档引用。
        private void OnDisable() => DisposeComposition();

        /// <summary>
        /// 窗口真正销毁时移除活动选择，并结束原生 Inspector 桥接事件的生命周期。
        /// </summary>
        private void OnDestroy()
        {
            if (Selection.activeObject == this) Selection.activeObject = null;
            NativeInspectorChanged = null;
        }

        /// <summary>
        /// 按 View、ViewModel、控制器和 Document 的逆序释放可重建组合对象。
        /// </summary>
        private void DisposeComposition()
        {
            if (viewModel != null)
            {
                viewModel.SelectionActivated -= OnTimelineSelectionActivated;
                viewModel.InspectorChanged -= OnInspectorChanged;
            }
            view?.Unbind();
            view = null;
            viewModel?.Dispose();
            viewModel = null;
            playback?.Dispose();
            playback = null;
            previewSceneService?.Dispose();
            previewSceneService = null;
            document?.Dispose();
            document = null;
            modules = null;
            editorConfig = null;
        }

        // 用户点击时间轴选择时才激活窗口 Inspector，普通刷新不会抢占 Project 或 Scene 选择。
        private void OnTimelineSelectionActivated()
        {
            if (SelectedData != null) Selection.activeObject = this;
        }

        // 将 ViewModel 的稳定 Inspector 刷新事件转发给 Unity 原生 Inspector。
        private void OnInspectorChanged() => NativeInspectorChanged?.Invoke();

        /// <summary>
        /// 立即载入配置；窗口尚未完成 UI 组合时暂存到 CreateGUI 阶段处理。
        /// </summary>
        /// <param name="config">需要显示的 SkillConfig；为空时保持当前文档不变。</param>
        private void OpenConfig(SkillConfig config)
        {
            if (config == null) return;
            if (viewModel == null)
            {
                pendingConfig = config;
                return;
            }

            pendingConfig = null;
            viewModel.OpenConfig(config);
        }
        #endregion
    }
}
#endif
