#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.ConditionSystemNS.Editor
{
    /// <summary>组装独立条件图窗口；运行时配置不引用任何 Editor 类型。</summary>
    public sealed class ConditionGraphEditorWindow : EditorWindow
    {
        #region 依赖字段
        private const string UxmlPath = "Assets/Scripts/ConditionSystem/Editor/Style/ConditionGraphEditorWindow.uxml";
        private ConditionGraphEditorController controller;
        #endregion

        #region 窗口状态
        private ConditionGraph_SO pendingAsset;
        #endregion

        #region 入口
        /// <summary>从菜单打开窗口，恢复最后编辑资产。</summary>
        [MenuItem("RPG/Condition/Condition Graph Editor", priority = 110)]
        public static void ShowWindow() => Open(null);

        /// <summary>双击条件图时使用专用窗口打开。</summary>
        /// <param name="instanceId">资产实例 ID。</param>
        /// <param name="line">Unity 打开请求行号。</param>
        /// <returns>当前资产是否由本窗口处理。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(instanceId) is ConditionGraph_SO asset)) return false;
            Open(asset);
            return true;
        }

        /// <summary>打开指定资产，等待 CreateGUI 完成后绑定。</summary>
        /// <param name="asset">显式指定的资产，菜单调用时为空。</param>
        public static void Open(ConditionGraph_SO asset)
        {
            var window = GetWindow<ConditionGraphEditorWindow>();
            window.titleContent = new GUIContent("Condition Graph");
            window.minSize = new Vector2(1000, 650);
            window.pendingAsset = asset;
            window.Show();
            if (window.controller != null && asset != null)
            {
                window.controller.OpenAsset(asset);
                window.pendingAsset = null;
            }
        }
        #endregion

        #region 生命周期
        /// <summary>从 UXML 创建界面并组装 MVC，脚本重载后恢复当前编辑目标。</summary>
        private void CreateGUI()
        {
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError("[ConditionGraphEditor] 无法加载窗口 UXML。");
                rootVisualElement.Add(new HelpBox("条件编辑器 UXML 缺失", HelpBoxMessageType.Error));
                return;
            }
            uxml.CloneTree(rootVisualElement);
            var view = new ConditionGraphEditorView(rootVisualElement);
            var graphView = new ConditionGraphView();
            view.GraphContainer.Add(graphView);
            controller = new ConditionGraphEditorController(view, graphView, ConditionGraphEditorState.instance);
            controller.OpenAsset(pendingAsset != null ? pendingAsset : ConditionGraphEditorState.instance.ResolveAsset());
            pendingAsset = null;
        }

        /// <summary>关闭或域重载时释放回调与序列化绑定。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }
        #endregion
    }
}
#endif
