using System;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 为泛型 Handler 转发强类型目标定义的显式适配基类。
    /// </summary>
    /// <typeparam name="TDefinition">目标定义类型。</typeparam>
    public abstract class TaskObjectiveHandler<TDefinition> : ITaskObjectiveHandler<TDefinition>
        where TDefinition : TaskObjectiveDefinition
    {
        /// <summary>
        /// 获取该 Handler 支持的目标定义类型。
        /// </summary>
        public Type DefinitionType => typeof(TDefinition);

        /// <summary>
        /// 使用强类型定义创建目标运行时对象。
        /// </summary>
        /// <param name="definition">目标静态定义。</param>
        /// <param name="context">目标运行时上下文。</param>
        /// <returns>目标运行时对象。</returns>
        public abstract ITaskObjectiveRuntime CreateRuntime(
            TDefinition definition,
            ITaskObjectiveRuntimeContext context);

        /// <summary>
        /// 将非泛型定义转发为强类型 Handler 调用。
        /// </summary>
        /// <param name="definition">目标静态定义。</param>
        /// <param name="context">目标运行时上下文。</param>
        /// <returns>目标运行时对象。</returns>
        /// <exception cref="ArgumentException">定义类型不匹配时抛出。</exception>
        ITaskObjectiveRuntime ITaskObjectiveHandler.CreateRuntime(
            TaskObjectiveDefinition definition,
            ITaskObjectiveRuntimeContext context)
        {
            if (!(definition is TDefinition typedDefinition))
            {
                throw new ArgumentException(
                    $"目标定义类型不匹配，需要 {typeof(TDefinition).FullName}。",
                    nameof(definition));
            }

            return CreateRuntime(typedDefinition, context);
        }
    }
}
