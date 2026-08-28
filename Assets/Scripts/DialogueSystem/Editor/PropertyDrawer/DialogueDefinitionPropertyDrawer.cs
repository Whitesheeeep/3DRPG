#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using RPG.DialogueSystemModule;
using WS_Modules.Utilities.Editor;

namespace RPG.DialogueSystemModule.Editor
{
    /// <summary>绘制 Dialogue Condition 派生命令。</summary>
    [CustomPropertyDrawer(typeof(DialogueCondition), true)]
    internal sealed class DialogueConditionDefinitionPropertyDrawer
        : ManagedReferenceDropdownPropertyDrawer<DialogueCondition>
    {
        /// <summary>获取 Dialogue 命令专用的 Undo 操作名称。</summary>
        protected override string UndoActionName => "Change Dialogue Command";
    }

    /// <summary>绘制 Dialogue Action 派生命令。</summary>
    [CustomPropertyDrawer(typeof(DialogueAction), true)]
    internal sealed class DialogueActionDefinitionPropertyDrawer
        : ManagedReferenceDropdownPropertyDrawer<DialogueAction>
    {
        /// <summary>获取 Dialogue 命令专用的 Undo 操作名称。</summary>
        protected override string UndoActionName => "Change Dialogue Command";
    }
}
#endif
