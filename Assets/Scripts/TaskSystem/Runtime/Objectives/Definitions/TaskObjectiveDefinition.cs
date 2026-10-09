using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 描述任务阶段中的静态目标配置，并负责创建对应的独立运行时。
    /// </summary>
    [Serializable]
    public abstract class TaskObjectiveDefinition
    {
        #region 配置字段

        [SerializeField] private string objectiveId = string.Empty;
        [SerializeField, LabelText("目标说明"), TextArea(1, 3)] private string displayDescription = string.Empty;
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
            : this(objectiveId, required, string.Empty)
        {
        }

        /// <summary>创建带稳定 ID、正数需求和玩家说明的目标。</summary>
        /// <param name="objectiveId">所属阶段内唯一的目标标识。</param>
        /// <param name="required">目标完成所需数量。</param>
        /// <param name="displayDescription">显示给玩家的目标说明。</param>
        protected TaskObjectiveDefinition(string objectiveId, int required, string displayDescription)
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
            this.displayDescription = displayDescription ?? string.Empty;
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

        /// <summary>获取显示给玩家的目标说明。</summary>
        public string DisplayDescription => displayDescription ?? string.Empty;

        /// <summary>根据静态配置和所属任务上下文创建新的目标运行时实例。</summary>
        /// <param name="context">由 TaskRuntime 限定到当前任务与阶段的运行时上下文。</param>
        /// <returns>本任务实例专属的目标 Runtime。</returns>
        /// <exception cref="ArgumentNullException">实现需要的配置或上下文为空时抛出。</exception>
        public abstract ITaskObjectiveRuntime CreateRuntime(ITaskObjectiveRuntimeContext context);

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
