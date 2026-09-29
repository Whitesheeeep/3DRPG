using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystem
{
    /// <summary>
    /// 管理目标定义类型到 Handler 的显式注册表。
    /// </summary>
    public sealed class TaskObjectiveHandlerRegistry
    {
        #region 状态

        // key：目标定义 CLR 类型；value：负责该目标类型运行时创建与监听的 Handler。
        private readonly Dictionary<Type, ITaskObjectiveHandler> handlerByDefinitionTypeMap =
            new Dictionary<Type, ITaskObjectiveHandler>();
        private bool defaultHandlersRegistered;

        #endregion

        #region 默认注册与显式注册

        /// <summary>
        /// 注册正式玩法的默认目标 Handler；当前尚无已接入的玩法目标适配器。
        /// </summary>
        public void RegisterDefault()
        {
            if (defaultHandlersRegistered)
            {
                return;
            }

            // 测试 Handler 由测试入口单独注入，避免把测试行为混入正式默认配置。
            defaultHandlersRegistered = true;
            Debug.Log("[TaskObjectiveHandlerRegistry] 默认目标 Handler 初始化完成，handlerCount=0；玩法目标适配器尚待接入。");
        }

        /// <summary>
        /// 注册一个强类型目标 Handler。
        /// </summary>
        /// <typeparam name="TDefinition">Handler 支持的目标定义类型。</typeparam>
        /// <param name="handler">待注册 Handler。</param>
        /// <exception cref="ArgumentNullException">Handler 为空时抛出。</exception>
        /// <exception cref="ArgumentException">Handler 类型不匹配或目标定义类型重复注册时抛出。</exception>
        public void Register<TDefinition>(ITaskObjectiveHandler<TDefinition> handler)
            where TDefinition : TaskObjectiveDefinition
        {
            if (handler == null)
            {
                const string message = "不能注册空的目标 Handler。";
                Debug.LogError($"[TaskObjectiveHandlerRegistry] {message}");
                throw new ArgumentNullException(nameof(handler), message);
            }

            Type definitionType = typeof(TDefinition);
            if (handler.DefinitionType != definitionType)
            {
                string message = $"Handler 声明类型 {handler.DefinitionType} 与泛型定义类型 {definitionType} 不一致。";
                Debug.LogError($"[TaskObjectiveHandlerRegistry] {message}");
                throw new ArgumentException(message, nameof(handler));
            }

            if (handlerByDefinitionTypeMap.ContainsKey(definitionType))
            {
                string message = $"目标定义类型已经注册 Handler：{definitionType.FullName}。";
                Debug.LogError($"[TaskObjectiveHandlerRegistry] {message}");
                throw new ArgumentException(message, nameof(handler));
            }

            // 注册键使用定义的精确 CLR 类型，与 Resolve 的匹配规则保持一致。
            handlerByDefinitionTypeMap.Add(definitionType, handler);
            Debug.Log($"[TaskObjectiveHandlerRegistry] 已注册目标 Handler，definitionType={definitionType.FullName}。");
        }

        #endregion

        #region 解析

        /// <summary>
        /// 按目标定义的精确 CLR 类型解析 Handler，不沿继承链回退。
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

            Type definitionType = definition.GetType();
            if (!handlerByDefinitionTypeMap.TryGetValue(definitionType, out ITaskObjectiveHandler handler))
            {
                string message = $"没有注册目标定义类型的 Handler：{definitionType.FullName}。";
                Debug.LogError($"[TaskObjectiveHandlerRegistry] {message}");
                throw new InvalidOperationException(message);
            }

            return handler;
        }

        #endregion
    }
}
