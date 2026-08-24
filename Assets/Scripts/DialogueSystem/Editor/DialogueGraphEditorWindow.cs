#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>
    /// Dialogue Graph Editor 的组合根，只负责创建 UI、GraphView 和 Controller。
    /// </summary>
    public sealed class DialogueGraphEditorWindow : EditorWindow
    {
        #region 常量与字段

        private const string WindowTitle = "Dialogue Graph Editor";
        private const string WindowUxmlPath = "Assets/Scripts/DialogueSystem/Editor/Style/DialogueGraphEditorWindow.uxml";
        private DialogueAsset pendingAsset;
        private DialogueGraphEditorController controller;

        #endregion

        #region 窗口入口

        /// <summary>打开或聚焦对话 GraphView 编辑器。</summary>
        [MenuItem("RPG/Dialogue/Dialogue Graph Editor", priority = 100)]
        private static void ShowWindow()
        {
            DialogueGraphEditorWindow window = GetWindow<DialogueGraphEditorWindow>();
            window.ConfigureWindow();
            window.Show();
        }

        /// <summary>双击 DialogueAsset 时打开对应编辑器窗口。</summary>
        /// <param name="instanceId">Unity 对象实例 ID。</param>
        /// <param name="line">资产打开请求的行号。</param>
        /// <returns>对象是 DialogueAsset 时返回 true。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            DialogueAsset asset = EditorUtility.InstanceIDToObject(instanceId) as DialogueAsset;
            if (asset == null) return false;
            Open(asset);
            return true;
        }

        /// <summary>打开窗口并载入指定对话资产。</summary>
        /// <param name="asset">待载入资产。</param>
        internal static void Open(DialogueAsset asset)
        {
            DialogueGraphEditorWindow window = GetWindow<DialogueGraphEditorWindow>();
            window.ConfigureWindow();
            window.pendingAsset = asset;
            window.Show();
            window.controller?.OpenAsset(asset);
        }

        #endregion

        #region 生命周期与组合

        /// <summary>加载窗口资源并组装 View、GraphView 和 Controller。</summary>
        private void CreateGUI()
        {
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            VisualTreeAsset windowAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WindowUxmlPath);
            if (windowAsset == null)
            {
                rootVisualElement.Add(new HelpBox($"找不到 Dialogue Graph Editor UXML：{WindowUxmlPath}",
                    HelpBoxMessageType.Error));
                return;
            }

            windowAsset.CloneTree(rootVisualElement);
            DialogueGraphEditorView editorView = new DialogueGraphEditorView(rootVisualElement);
            DialogueGraphView graphView = new DialogueGraphView();
            editorView.GraphContainer.Add(graphView);
            controller = new DialogueGraphEditorController(editorView, graphView);
            controller.Bind();
            if (pendingAsset != null) controller.OpenAsset(pendingAsset);
        }

        /// <summary>释放 Controller，避免事件跨窗口生命周期残留。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }

        /// <summary>设置窗口标题和最小尺寸。</summary>
        private void ConfigureWindow()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(980f, 640f);
        }

        #endregion
    }
}
#endif
