#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 组合技能时间轴的 Editor-only 配置、数据文档、播放时钟、ViewModel 与主视图。
    /// </summary>
    public sealed class SkillTimelineEditorWindow : EditorWindow
    {
        #region 资源路径与组合状态

        private const string UxmlPath = "Assets/Scripts/SkillSystem/Editor/SkillTimelineEditorWindow/SkillTimelineEditorWindow.uxml";
        private const string EditorConfigPath = "Assets/Scripts/SkillSystem/Editor/Config/SkillTimelineEditorConfig.asset";
        private SkillTimelineEditorConfig editorConfig;
        private SkillTimelineEditorDocument document;
        private SkillTimelinePreviewSceneService previewSceneService;
        private SkillTimelinePlaybackController playback;
        private SkillTimelineEditorViewModel viewModel;
        private SkillTimelineEditorView view;

        #endregion

        #region Window 生命周期

        // 打开或聚焦窗口；实际最小尺寸在 Editor Config 加载后应用。
        [MenuItem("Tools/RPG/Skill Timeline Editor")]
        private static void ShowWindow()
        {
            SkillTimelineEditorWindow window = GetWindow<SkillTimelineEditorWindow>();
            window.titleContent = new GUIContent("技能时间轴");
            window.Show();
        }

        // 加载纯编辑器配置与 UXML，再按固定顺序创建窗口组合对象。
        private void CreateGUI()
        {
            DisposeComposition();
            rootVisualElement.Clear();
            editorConfig = AssetDatabase.LoadAssetAtPath<SkillTimelineEditorConfig>(EditorConfigPath);
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
            SkillTimelineViewportController viewport = new(editorConfig);
            document = new SkillTimelineEditorDocument();
            previewSceneService = new SkillTimelinePreviewSceneService(SkillTimelineEditorSettings.instance);
            playback = new SkillTimelinePlaybackController();
            viewModel = new SkillTimelineEditorViewModel(document, playback, previewSceneService);
            view = new SkillTimelineEditorView(rootVisualElement, viewport, editorConfig);
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
            editorConfig = null;
        }

        #endregion
    }
}
#endif