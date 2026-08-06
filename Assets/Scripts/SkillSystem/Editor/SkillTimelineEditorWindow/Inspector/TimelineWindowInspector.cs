#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;

namespace RPG.SkillSystem.Editor
{
    /// <summary>
    /// 在 Unity 原生 Inspector 中绘制 TimelineWindow 当前选择的实际 Track 或 Item Config。
    /// </summary>
    [CustomEditor(typeof(TimelineWindow))]
    internal sealed class TimelineWindowInspector : UnityEditor.Editor
    {
        private const string StylePath =
            "Assets/Scripts/SkillSystem/Editor/SkillTimelineEditorWindow/EditorWindowStyle/SkillTimelineEditorWindow.uss";
        private TimelineWindow window;
        private VisualElement root;

        /// <summary>
        /// 创建原生 Inspector UI，并订阅窗口稳定刷新事件。
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            window = (TimelineWindow)target;
            root = new VisualElement();
            StyleSheet style = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (style != null) root.styleSheets.Add(style);
            window.NativeInspectorChanged += Refresh;
            root.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            Refresh();
            return root;
        }

        // 根据窗口当前直接 Config 引用选择模块 Drawer。
        private void Refresh()
        {
            if (root == null || window == null) return;
            root.Clear();
            object data = window.SelectedData;
            if (data == null)
            {
                root.Add(new HelpBox("点击时间轴中的 Track 或 Item 后在这里编辑。",
                    HelpBoxMessageType.Info));
                return;
            }
            IInspectorDrawer drawer = window.Modules?.GetInspector(data);
            if (drawer == null)
            {
                root.Add(new HelpBox("当前选择没有可用 Inspector。",
                    HelpBoxMessageType.Info));
                return;
            }
            drawer.Draw(root, data, window.ViewModel);
        }

        // Inspector 被销毁或切换目标时注销窗口事件，避免重复刷新。
        private void OnDetached(DetachFromPanelEvent _)
        {
            if (window != null) window.NativeInspectorChanged -= Refresh;
            window = null;
            root = null;
        }
    }
}
#endif
