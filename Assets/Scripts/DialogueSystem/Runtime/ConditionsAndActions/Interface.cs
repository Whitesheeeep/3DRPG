using System;

namespace RPG.DialogueSystemModule
{
    #region Condition 与 Action 定义

    /// <summary>
    /// 表示一个可由 PropertyDrawer 选择具体类型的 Choice 条件配置。
    /// </summary>
    [Serializable]
    public abstract class DialogueCondition
    {
        public abstract bool IsMet();

        /// <summary>
        /// 校验当前 Condition 的具体字段；派生定义可覆盖此方法报告配置错误。
        /// </summary>
        /// <exception cref="ArgumentException">字段配置不满足定义契约时抛出。</exception>
        public virtual void Validate()
        {
        }
    }

    /// <summary>
    /// 表示一个可由 PropertyDrawer 选择具体类型的 Choice 动作配置。
    /// </summary>
    [Serializable]
    public abstract class DialogueAction
    {
        /// <summary>
        /// 校验当前 Action 的具体字段；派生定义可覆盖此方法报告配置错误。
        /// </summary>
        /// <exception cref="ArgumentException">字段配置不满足定义契约时抛出。</exception>
        public virtual void Validate()
        {
        }
    }

    #endregion
}
