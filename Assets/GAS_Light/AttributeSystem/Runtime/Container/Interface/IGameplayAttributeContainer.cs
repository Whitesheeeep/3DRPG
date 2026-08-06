using System.Collections.Generic;

namespace WS_Modules.GAS.AttributeSystem
{
    /// <summary>在只读查询基础上提供受控 Attribute 修改操作。</summary>
    public interface IGameplayAttributeContainer : IReadOnlyGameplayAttributeContainer
    {
        /// <summary>执行一次不绑定 Container Owner 的即时数值结算。</summary>
        /// <param name="modifier">已经计算完成的单项 Modifier；Priority 被忽略。</param>
        void ApplyInstantModifier(AttributeModifier modifier);

        /// <summary>按列表顺序原子结算一组 Instant Modifier。</summary>
        /// <param name="modifiers">允许为空的已计算 Modifier 列表。</param>
        /// <returns>全部 Modifier、Pre 与数值计算成功并整体提交时返回 true。</returns>
        bool TryApplyInstantModifiers(IReadOnlyList<AttributeModifier> modifiers);

        /// <summary>应用一个已经计算完成的持续 Modifier。</summary>
        /// <param name="modifier">未归属其他 Container 的候选 Modifier。</param>
        /// <returns>Modifier 合法、目标为 Stat 且聚合结果已原子提交时返回 true。</returns>
        bool TryAddModifier(AttributeModifier modifier);

        /// <summary>按对象引用移除一个运行时 Modifier。</summary>
        /// <param name="modifier">已绑定当前 Container 的运行时 Modifier。</param>
        /// <returns>Modifier 已移除且新 CurrentValue 已原子提交时返回 true。</returns>
        bool TryRemoveModifier(AttributeModifier modifier);

        /// <summary>移除指定 Source 在当前 Container 中产生的全部 Modifier。</summary>
        /// <param name="source">用于引用相等匹配的运行时 Source。</param>
        /// <param name="removedCount">成功时返回移除数量。</param>
        /// <returns>至少找到一个 Modifier 且全部受影响 CurrentValue 已原子提交时返回 true。</returns>
        bool TryRemoveModifiers(IModifierSource source, out int removedCount);

        /// <summary>原子替换指定 Source 在当前 Container 中持有的全部持续 Modifier。</summary>
        /// <param name="source">Active GE 等运行时 Modifier Source。</param>
        /// <param name="modifiers">替换后的完整候选 Modifier；空列表表示只移除旧值。</param>
        /// <returns>全部旧值移除、新值创建和受影响 CurrentValue 提交成功时返回 true。</returns>
        bool TryReplaceModifiers(
            IModifierSource source,
            IReadOnlyList<AttributeModifier> modifiers);

        /// <summary>通过受控修改流程恢复全部默认值。</summary>
        void ResetToDefaultValues();

        /// <summary>清空全部运行时 Attribute。</summary>
        void Clear();
    }
}
