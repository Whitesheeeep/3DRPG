#if UNITY_EDITOR
using WS_Modules.Utilities.Editor;

namespace RPG.TaskSystemNS.Editor
{
    /// <summary>为任务接取条件 SerializeReference 列表提供派生条件类型选择。</summary>
    [UnityEditor.CustomPropertyDrawer(typeof(TaskConditionDefinition), true)]
    internal sealed class TaskConditionDefinitionPropertyDrawer : ManagedReferenceDropdownPropertyDrawer<TaskConditionDefinition>
    {
        /// <summary>设置条件类型菜单使用的 Undo 操作名称。</summary>
        protected override string UndoActionName => "更改任务条件类型";
    }
}
#endif
