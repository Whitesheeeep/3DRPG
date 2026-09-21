#if UNITY_EDITOR
using System;
using UnityEditor;
using WS_Modules.GAS.GameplayEffect;
using WS_Modules.Utilities.Editor;

namespace WS_Modules.GAS.Editor
{
    /// <summary>
    /// 为 GameplayEffectExecution 的 SerializeReference 数组元素提供 GE 专属类型名称和状态提示。
    /// </summary>
    [CustomPropertyDrawer(typeof(GameplayEffectExecution), true)]
    public sealed class GameplayEffectExecutionPropertyDrawer
        : ManagedReferenceDropdownPropertyDrawer<GameplayEffectExecution>
    {
        #region GE 专属配置

        /// <summary>
        /// 获取 GE Execution 类型切换使用的 Undo 操作名称。
        /// </summary>
        protected override string UndoActionName => "Change Gameplay Effect Execution";

        /// <summary>
        /// 将具体 Execution 类型转换为去掉基类后缀的可读名称。
        /// </summary>
        /// <param name="type">Execution 具体类型。</param>
        /// <returns>例如 BasicDamageGameplayEffectExecution 对应 Basic Damage。</returns>
        protected override string GetTypeDisplayName(Type type)
        {
            const string suffix = "GameplayEffectExecution";
            string typeName = type.Name;
            if (typeName.EndsWith(suffix, StringComparison.Ordinal))
            {
                typeName = typeName.Substring(0, typeName.Length - suffix.Length);
            }

            return UnityEditor.ObjectNames.NicifyVariableName(typeName);
        }

        /// <summary>
        /// 限制 GE Execution 菜单只显示 Unity 可序列化的公开具体类型。
        /// </summary>
        /// <param name="type">待筛选的 Execution 类型。</param>
        /// <returns>类型声明了 SerializableAttribute 时返回 true。</returns>
        protected override bool IsSelectableType(Type type)
        {
            return type.IsDefined(typeof(SerializableAttribute), false);
        }

        /// <summary>
        /// 获取 managed reference 类型丢失时的明确标题。
        /// </summary>
        protected override string MissingTypeDisplayName => "Missing Type";

        /// <summary>
        /// 获取 managed reference 类型丢失时的修复提示。
        /// </summary>
        protected override string MissingTypeMessage =>
            "该 Execution 的 managed reference 类型丢失，请重新选择一个有效类型。";

        /// <summary>
        /// 获取没有作者序列化字段的 Execution 展示提示。
        /// </summary>
        protected override string EmptyContentMessage =>
            "该 Execution 没有可配置的作者参数，运行时从 GameplayEffectCalculationContext 读取数据。";

        #endregion
    }
}
#endif
