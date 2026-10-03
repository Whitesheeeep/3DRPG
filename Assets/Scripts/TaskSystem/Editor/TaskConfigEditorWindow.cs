#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>TaskDatabase 与 TaskDefinition 的 UI Toolkit 编辑器窗口。</summary>
    public sealed class TaskConfigEditorWindow : EditorWindow
    {
        #region 依赖字段

        private TaskConfigEditorController controller;

        #endregion

        #region 窗口状态

        private TaskDatabase pendingDatabase;
        private TaskDefinition pendingDefinition;

        #endregion

        #region 菜单与打开入口

        /// <summary>从 Unity RPG 菜单打开任务配置编辑器。</summary>
        [MenuItem("RPG/TaskSystem/任务配置编辑器", priority = 110)]
        private static void ShowWindow()
        {
            TaskConfigEditorWindow window = GetWindow<TaskConfigEditorWindow>();
            window.ConfigureWindow();
            window.Show();
        }

        /// <summary>双击 TaskDatabase 或 TaskDefinition 资产时打开任务编辑器。</summary>
        /// <param name="instanceId">资产实例 ID。</param>
        /// <param name="line">Unity 提供的行号。</param>
        /// <returns>资产由此编辑器处理时返回 true。</returns>
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            UnityEngine.Object asset = EditorUtility.InstanceIDToObject(instanceId);
            if (asset is TaskDatabase database)
            {
                OpenDatabase(database);
                return true;
            }

            if (asset is TaskDefinition definition)
            {
                OpenDefinition(definition);
                return true;
            }

            return false;
        }

        /// <summary>打开窗口并选中指定 TaskDatabase。</summary>
        /// <param name="database">任务数据库。</param>
        public static void OpenDatabase(TaskDatabase database)
        {
            if (database == null) return;
            TaskConfigEditorWindow window = GetWindow<TaskConfigEditorWindow>();
            window.ConfigureWindow();
            window.pendingDatabase = database;
            window.pendingDefinition = null;
            window.Show();
            window.controller?.OpenDatabase(database);
        }

        /// <summary>打开窗口并选中指定 TaskDefinition。</summary>
        /// <param name="definition">任务定义资产。</param>
        public static void OpenDefinition(TaskDefinition definition)
        {
            if (definition == null) return;
            TaskConfigEditorWindow window = GetWindow<TaskConfigEditorWindow>();
            window.ConfigureWindow();
            window.pendingDefinition = definition;
            window.pendingDatabase = null;
            window.Show();
            window.controller?.OpenDefinition(definition);
        }

        #endregion

        #region 生命周期

        /// <summary>载入窗口布局资源并创建 View 与 Controller。</summary>
        private void CreateGUI()
        {
            controller?.Dispose();
            controller = null;
            rootVisualElement.Clear();
            const string windowPath = "Assets/Scripts/TaskSystem/Editor/Style/TaskConfigEditorWindow.uxml";
            const string rowPath = "Assets/Scripts/TaskSystem/Editor/Style/TaskConfigEditorRow.uxml";
            const string stagePath = "Assets/Scripts/TaskSystem/Editor/Style/TaskStageEditorCard.uxml";
            VisualTreeAsset windowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(windowPath);
            VisualTreeAsset rowTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(rowPath);
            VisualTreeAsset stageTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(stagePath);
            if (windowTemplate == null || rowTemplate == null || stageTemplate == null)
            {
                rootVisualElement.Add(new HelpBox(
                    $"任务配置编辑器 UXML 缺失：{windowPath}、{rowPath} 或 {stagePath}。",
                    HelpBoxMessageType.Error));
                Debug.LogError("[TaskConfigEditor] 创建窗口失败，找不到必需的 UXML 模板。");
                return;
            }

            windowTemplate.CloneTree(rootVisualElement);
            var view = new TaskConfigEditorView(rootVisualElement, TaskConfigEditorSettings.instance, rowTemplate, stageTemplate);
            controller = new TaskConfigEditorController(view, new TaskConfigEditorService(TaskConfigEditorSettings.instance), TaskConfigEditorSettings.instance);
            if (pendingDatabase != null) controller.OpenDatabase(pendingDatabase);
            else if (pendingDefinition != null) controller.OpenDefinition(pendingDefinition);
            pendingDatabase = null;
            pendingDefinition = null;
            Debug.Log("[TaskConfigEditor] 任务配置窗口已创建并完成首次绑定。");
        }

        /// <summary>关闭窗口时释放 Controller 的事件和序列化绑定。</summary>
        private void OnDisable()
        {
            controller?.Dispose();
            controller = null;
        }

        /// <summary>设置窗口标题和最小可用尺寸。</summary>
        private void ConfigureWindow()
        {
            titleContent = new GUIContent("任务配置编辑器");
            minSize = new Vector2(1050f, 650f);
        }

        #endregion
    }
}
#endif
