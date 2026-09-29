using System;

namespace RPG.TaskSystem
{
    #region Handler 接口

    /// <summary>
    /// 为一个具体目标定义创建任务实例运行时的 Handler 契约。
    /// </summary>
    public interface ITaskObjectiveHandler
    {
        /// <summary>
        /// 获取该 Handler 支持的定义 CLR 类型。
        /// </summary>
        Type DefinitionType { get; }

        /// <summary>
        /// 创建一个属于具体任务实例的目标运行时对象。
        /// </summary>
        /// <param name="definition">目标静态定义。</param>
        /// <param name="context">目标运行时进度上下文。</param>
        /// <returns>可启动和停止监听的目标运行时对象。</returns>
        ITaskObjectiveRuntime CreateRuntime(
            TaskObjectiveDefinition definition,
            ITaskObjectiveRuntimeContext context);
    }

    /// <summary>
    /// 为强类型目标定义提供类型安全创建入口的 Handler 契约。
    /// </summary>
    /// <typeparam name="TDefinition">Handler 支持的目标定义类型。</typeparam>
    public interface ITaskObjectiveHandler<in TDefinition> : ITaskObjectiveHandler
        where TDefinition : TaskObjectiveDefinition
    {
        /// <summary>
        /// 使用强类型目标定义创建运行时对象。
        /// </summary>
        /// <param name="definition">目标静态定义。</param>
        /// <param name="context">目标运行时进度上下文。</param>
        /// <returns>可启动和停止监听的目标运行时对象。</returns>
        ITaskObjectiveRuntime CreateRuntime(
            TDefinition definition,
            ITaskObjectiveRuntimeContext context);
    }

    #endregion
}
