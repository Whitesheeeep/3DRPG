#if UNITY_EDITOR
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
        // 对数据层的通信
        private EditorViewModel viewModel;

        // 对 UI 层的通信
        private EditorView view;
        #endregion

        #region Window 生命周期
        // 打开或聚焦窗口；实际最小尺寸在 Editor Config 加载后应用。
        [MenuItem("Tools/RPG/Skill Timeline Editor")]
        private static void ShowWindow()
        {
            TimelineWindow window = GetWindow<TimelineWindow>();
            window.titleContent = new GUIContent("技能时间轴");
            window.Show();
            window.minSize = new Vector2(800, 600);
        }

        // 加载纯编辑器配置与 UXML，再按固定顺序创建窗口组合对象。
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
            playback = new PlaybackController();
            viewModel = new EditorViewModel(document, playback, previewSceneService, modules);
            view = new EditorView(rootVisualElement, editorConfig, modules);
            view.Bind(viewModel);
        }

        // 窗口禁用时释放全部 Editor 事件和序列化文档引用。
        private void OnDisable() => DisposeComposition();

        // 按 View、ViewModel、控制器和 Document 的逆序释放组合对象。
        private void DisposeComposition()
        {
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
        #endregion
    }
}
#endif