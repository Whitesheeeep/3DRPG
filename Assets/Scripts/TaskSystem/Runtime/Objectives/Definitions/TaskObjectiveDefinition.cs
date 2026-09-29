using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 描述一个需要由目标 Handler 转换为事件监听的阶段目标。
    /// </summary>
    [Serializable]
    public abstract class TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField] private string objectiveId = string.Empty;
        [SerializeField, MinValue(1)] private int required = 1;

        #endregion

        #region 构造

        /// <summary>
        /// 创建供 Unity 序列化使用的空目标。
        /// </summary>
        protected TaskObjectiveDefinition()
        {
        }

        /// <summary>
        /// 创建带稳定 ID 和正数需求的目标。
        /// </summary>
        /// <param name="objectiveId">所属阶段内唯一的目标标识。</param>
        /// <param name="required">目标完成所需数量。</param>
        /// <exception cref="ArgumentException">目标标识非法时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">需求数量不是正数时抛出。</exception>
        protected TaskObjectiveDefinition(string objectiveId, int required)
        {
            if (!TaskIdentifierRules.IsValid(objectiveId))
            {
                throw new ArgumentException("目标 ID 无效。", nameof(objectiveId));
            }

            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required), "目标所需数量必须大于零。");
            }

            this.objectiveId = objectiveId;
            this.required = required;
        }

        #endregion

        #region 属性与校验

        /// <summary>
        /// 获取所属阶段内的稳定目标标识。
        /// </summary>
        public ObjectiveId ObjectiveId => new ObjectiveId(objectiveId);

        /// <summary>
        /// 获取目标需求数量。
        /// </summary>
        public int Required => required;

        /// <summary>
        /// 校验目标标识与数量。
        /// </summary>
        /// <exception cref="ArgumentException">目标标识非法时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">需求数量不是正数时抛出。</exception>
        public virtual void Validate()
        {
            if (!TaskIdentifierRules.IsValid(objectiveId))
            {
                throw new ArgumentException("目标 ID 无效。", nameof(objectiveId));
            }

            if (required <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(required), "目标所需数量必须大于零。");
            }
        }

        #endregion
    }
}
