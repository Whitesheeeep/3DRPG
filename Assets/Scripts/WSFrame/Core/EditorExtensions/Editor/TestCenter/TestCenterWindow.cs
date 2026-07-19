using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace WS_Modules.EditorExtensions
{
    public sealed class TestCenterWindow : EditorWindow
    {
        private const string WindowTitle = "总测试窗口";
        private const string UxmlPath = "Assets/Scripts/WSFrame/Core/EditorExtensions/Editor/TestCenter/TestCenterWindow.uxml";

        private TestCenterViewModel viewModel;
        private TestCenterView view;

        [MenuItem("Tools/WSFrame/总测试窗口 %#T")]
        private static void ShowWindow()
        {
            TestCenterWindow window = GetWindow<TestCenterWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(860f, 520f);
            window.Show();
        }

        private void CreateGUI()
        {
            rootVisualElement.Clear();

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                rootVisualElement.Add(new HelpBox($"缺少窗口 UXML: {UxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            viewModel = new TestCenterViewModel();
            view = new TestCenterView(rootVisualElement, viewModel);
            view.Bind();
        }

        private void OnDisable()
        {
            view?.Dispose();
            view = null;
        }
    }
}
