using System;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 描述一个需要由资格 Handler 评估的任务接取条件。
    /// </summary>
    [Serializable]
    public abstract class TaskConditionDefinition
    {
        /// <summary>
        /// 校验条件配置；具体条件可补充业务约束。
        /// </summary>
        public virtual void Validate()
        {
            // 基类没有通用字段，具体条件在自己的定义类型中校验。
        }
    }
}
