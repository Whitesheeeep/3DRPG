#if UNITY_EDITOR
using System;
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
        private InspectorFieldCommitController fieldCommitController;

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
            fieldCommitController?.Dispose();
            fieldCommitController = null;
            root.Clear();
            object data = window.SelectedData;
            if (data == null)
            {
                root.Add(new HelpBox("点击时间轴中的 Track 或 Item 后在这里编辑。",
                    HelpBoxMessageType.Info));
                return;
            }
            IInspectorDrawer drawer = window.Modules?.GetInspectorDrawer(data);
            if (drawer == null)
            {
                root.Add(new HelpBox("当前选择没有可用 Inspector。",
                    HelpBoxMessageType.Info));
                return;
            }
            TimelineWindow capturedWindow = window;
            EditorViewModel capturedViewModel = capturedWindow.ViewModel;
            Action cancelDraft = data switch
            {
                AttackDetectionSkillClipConfig attack =>
                    () => ClearAttackDetectionDraft(capturedWindow, capturedViewModel, attack),
                CameraModifierSkillClipConfig modifier =>
                    () => ClearCameraModifierDraft(capturedWindow, capturedViewModel, modifier),
                _ => null
            };
            fieldCommitController = new InspectorFieldCommitController(cancelDraft);
            drawer.Draw(root, data, window.ViewModel, fieldCommitController);
        }

        // Inspector 被销毁或切换目标时注销窗口事件，避免重复刷新。
        private void OnDetached(DetachFromPanelEvent _)
        {
            if (window != null) window.NativeInspectorChanged -= Refresh;
            fieldCommitController?.Dispose();
            fieldCommitController = null;
            window = null;
            root = null;
        }

        /// <summary>
        /// 在捕获窗口仍持有同一 ViewModel 时清除攻击检测 Inspector 草稿。
        /// </summary>
        /// <param name="capturedWindow">创建本轮 Inspector 时捕获的窗口实例。</param>
        /// <param name="capturedViewModel">创建草稿回调时捕获的稳定 ViewModel。</param>
        /// <param name="clip">需要清除预览草稿的攻击检测片段。</param>
        private static void ClearAttackDetectionDraft(TimelineWindow capturedWindow,
            EditorViewModel capturedViewModel, AttackDetectionSkillClipConfig clip)
        {
            // 迟到的 Detach 可能发生在窗口组合释放之后，此时 Preview 已由窗口生命周期统一清理。
            if (capturedWindow == null || capturedWindow.ViewModel != capturedViewModel) return;
            capturedViewModel.ClearAttackDetectionInspectorDraft(clip);
        }

        /// <summary>
        /// 在捕获窗口仍持有同一 ViewModel 时清除镜头修饰 Inspector 草稿。
        /// </summary>
        /// <param name="capturedWindow">创建本轮 Inspector 时捕获的窗口实例。</param>
        /// <param name="capturedViewModel">创建草稿回调时捕获的稳定 ViewModel。</param>
        /// <param name="clip">需要清除预览草稿的镜头修饰片段。</param>
        private static void ClearCameraModifierDraft(TimelineWindow capturedWindow,
            EditorViewModel capturedViewModel, CameraModifierSkillClipConfig clip)
        {
            // 只在当前组合仍然有效时访问 ViewModel，避免 Inspector Detach 晚于窗口释放。
            if (capturedWindow == null || capturedWindow.ViewModel != capturedViewModel) return;
            capturedViewModel.ClearCameraModifierDraft(clip);
        }
    }
}
#endif
