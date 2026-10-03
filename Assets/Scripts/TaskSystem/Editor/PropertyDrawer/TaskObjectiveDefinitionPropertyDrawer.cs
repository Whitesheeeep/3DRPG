#if UNITY_EDITOR
using WS_Modules.Utilities.Editor;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>为任务目标 SerializeReference 列表提供派生目标类型选择。</summary>
    [UnityEditor.CustomPropertyDrawer(typeof(TaskObjectiveDefinition), true)]
    internal sealed class TaskObjectiveDefinitionPropertyDrawer : ManagedReferenceDropdownPropertyDrawer<TaskObjectiveDefinition>
    {
        /// <summary>设置目标类型菜单使用的 Undo 操作名称。</summary>
        protected override string UndoActionName => "更改任务目标类型";
    }
}
#endif
