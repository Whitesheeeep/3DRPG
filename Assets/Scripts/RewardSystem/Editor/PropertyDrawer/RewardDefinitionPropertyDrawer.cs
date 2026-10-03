#if UNITY_EDITOR
using RPG.RewardSystemNS;
using WS_Modules.Utilities.Editor;

namespace RPG.RewardSystemNS.Editor
{
    /// <summary>为通用奖励 SerializeReference 列表提供派生奖励类型选择。</summary>
    [UnityEditor.CustomPropertyDrawer(typeof(RewardDefinition), true)]
    internal sealed class RewardDefinitionPropertyDrawer : ManagedReferenceDropdownPropertyDrawer<RewardDefinition>
    {
        /// <summary>设置奖励类型菜单使用的 Undo 操作名称。</summary>
        protected override string UndoActionName => "更改任务奖励类型";
    }
}
#endif
