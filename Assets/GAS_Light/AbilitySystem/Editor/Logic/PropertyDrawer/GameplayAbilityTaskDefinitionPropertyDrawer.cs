#if UNITY_EDITOR
using UnityEditor;
using WS_Modules.GAS.GameplayAbilitySystem;
using WS_Modules.Utilities.Editor;

namespace WS_Modules.GAS.Editor
{
    /// <summary>
    /// 为 SerializeReference Task Definition 提供统一的类型选择、清空、Undo 与字段绘制。
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayAbilityTaskConfig), true)]
    public sealed class GameplayAbilityTaskDefinitionPropertyDrawer
        : ManagedReferenceDropdownPropertyDrawer<GameplayAbilityTaskConfig>
    {
        #region Task 专属配置

        /// <summary>
        /// 获取 Ability Task 类型切换使用的 Undo 操作名称。
        /// </summary>
        protected override string UndoActionName => "Change Gameplay Ability Task";

        #endregion
    }
}
#endif
