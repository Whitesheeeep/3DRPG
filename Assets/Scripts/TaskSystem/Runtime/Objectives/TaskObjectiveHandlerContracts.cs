using System;
using System.Collections.Generic;

namespace RPG.TaskSystem
{
    #region Handler 契约

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

    /// <summary>
    /// 管理目标定义类型到 Handler 的显式注册表。
    /// </summary>
    public sealed class TaskObjectiveHandlerRegistry
    {
        private readonly Dictionary<Type, ITaskObjectiveHandler> handlers =
            new Dictionary<Type, ITaskObjectiveHandler>();

        /// <summary>
        /// 注册一个强类型目标 Handler。
        /// </summary>
        /// <typeparam name="TDefinition">Handler 支持的目标定义类型。</typeparam>
        /// <param name="handler">待注册 Handler。</param>
        /// <exception cref="ArgumentNullException">Handler 为空时抛出。</exception>
        /// <exception cref="ArgumentException">同一定义类型已经注册 Handler 时抛出。</exception>
        public void Register<TDefinition>(ITaskObjectiveHandler<TDefinition> handler)
            where TDefinition : TaskObjectiveDefinition
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type definitionType = typeof(TDefinition);
            if (handler.DefinitionType != definitionType)
            {
                throw new ArgumentException(
                    $"Handler 声明类型 {handler.DefinitionType} 与泛型定义类型 {definitionType} 不一致。",
                    nameof(handler));
            }

            if (handlers.ContainsKey(definitionType))
            {
                throw new ArgumentException($"目标定义类型已经注册 Handler：{definitionType.FullName}。", nameof(handler));
            }

            handlers.Add(definitionType, handler);
        }

        /// <summary>
        /// 解析目标定义对应的 Handler。
        /// </summary>
        /// <param name="definition">目标静态定义。</param>
        /// <returns>匹配的 Handler。</returns>
        /// <exception cref="ArgumentNullException">定义为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">没有匹配 Handler 时抛出。</exception>
        public ITaskObjectiveHandler Resolve(TaskObjectiveDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (!handlers.TryGetValue(definition.GetType(), out ITaskObjectiveHandler handler))
            {
                throw new InvalidOperationException(
                    $"没有注册目标定义类型的 Handler：{definition.GetType().FullName}。 ");
            }

            return handler;
        }
    }

    /// <summary>
    /// 表示具体任务目标运行时的订阅生命周期。
    /// </summary>
    public interface ITaskObjectiveRuntime
    {
        /// <summary>
        /// 启动该目标所需的类型事件监听；重复调用必须无副作用。
        /// </summary>
        void StartListening();

        /// <summary>
        /// 取消该目标所有事件监听；重复调用必须安全。
        /// </summary>
        void StopListening();
    }

    /// <summary>
    /// 为目标 Handler 提供受限的任务进度修改入口。
    /// </summary>
    public interface ITaskObjectiveRuntimeContext
    {
        /// <summary>
        /// 获取所属任务标识。
        /// </summary>
        TaskId TaskId { get; }

        /// <summary>
        /// 获取目标标识。
        /// </summary>
        ObjectiveId ObjectiveId { get; }

        /// <summary>
        /// 获取目标需求数量。
        /// </summary>
        int Required { get; }

        /// <summary>
        /// 获取当前进度。
        /// </summary>
        int Current { get; }

        /// <summary>
        /// 累加事件型目标进度。
        /// </summary>
        /// <param name="delta">非负增加量。</param>
        void AddProgress(int delta);

        /// <summary>
        /// 设置状态型目标当前值。
        /// </summary>
        /// <param name="value">新的非负当前值。</param>
        void SetProgress(int value);
    }

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

    #endregion
}
