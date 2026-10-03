using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.TaskSystemNS
{
    /// <summary>
    /// 管理接取条件类型到 Handler 的显式映射。
    /// </summary>
    public sealed class TaskConditionHandlerRegistry
    {
        #region 状态

        // key：条件定义 CLR 类型；value：负责读取任务事实并返回结构化资格原因的 Handler。
        private readonly Dictionary<Type, ITaskConditionHandler> handlerByDefinitionTypeMap =
            new Dictionary<Type, ITaskConditionHandler>();
        private bool defaultHandlersRegistered;

        #endregion

        #region 默认注册与显式注册

        /// <summary>
        /// 登记任务系统内建的接取条件 Handler；重复调用不会重复创建注册项。
        /// </summary>
        public void RegisterDefault()
        {
            if (defaultHandlersRegistered)
            {
                return;
            }

            // 默认条件集中在该入口登记，架构装配层无需依赖具体 Handler 类型。
            Register<TaskPrerequisiteCompletedConditionDefinition>(
                new TaskPrerequisiteCompletedConditionHandler());
            defaultHandlersRegistered = true;
        }

        /// <summary>
        /// 注册一个接取条件 Handler。
        /// </summary>
        /// <typeparam name="TDefinition">Handler 支持的条件定义类型。</typeparam>
        /// <param name="handler">待注册 Handler。</param>
        /// <exception cref="ArgumentNullException">Handler 为空时抛出。</exception>
        /// <exception cref="ArgumentException">Handler 类型不匹配或条件定义类型重复注册时抛出。</exception>
        public void Register<TDefinition>(ITaskConditionHandler handler)
            where TDefinition : TaskConditionDefinition
        {
            if (handler == null)
            {
                const string message = "不能注册空的接取条件 Handler。";
                Debug.LogError($"[TaskConditionHandlerRegistry] {message}");
                throw new ArgumentNullException(nameof(handler), message);
            }

            Type definitionType = typeof(TDefinition);
            if (handler.DefinitionType != definitionType)
            {
                string message = $"条件 Handler 声明类型 {handler.DefinitionType} 与注册类型 {definitionType} 不一致。";
                Debug.LogError($"[TaskConditionHandlerRegistry] {message}");
                throw new ArgumentException(message, nameof(handler));
            }

            if (handlerByDefinitionTypeMap.ContainsKey(definitionType))
            {
                string message = $"条件类型已经注册 Handler：{definitionType.FullName}。";
                Debug.LogError($"[TaskConditionHandlerRegistry] {message}");
                throw new ArgumentException(message, nameof(handler));
            }

            // 注册键使用定义的精确 CLR 类型，与 Resolve 的匹配规则保持一致。
            handlerByDefinitionTypeMap.Add(definitionType, handler);
            Debug.Log($"[TaskConditionHandlerRegistry] 已注册接取条件 Handler，definitionType={definitionType.FullName}。");
        }

        #endregion

        #region 解析

        /// <summary>
        /// 按条件定义的精确 CLR 类型解析 Handler，不沿继承链回退。
        /// </summary>
        /// <param name="definition">条件定义。</param>
        /// <returns>匹配的 Handler。</returns>
        /// <exception cref="ArgumentNullException">定义为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">条件类型没有 Handler 时抛出。</exception>
        public ITaskConditionHandler Resolve(TaskConditionDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Type definitionType = definition.GetType();
            if (!handlerByDefinitionTypeMap.TryGetValue(definitionType, out ITaskConditionHandler handler))
            {
                string message = $"没有注册接取条件类型的 Handler：{definitionType.FullName}。";
                Debug.LogError($"[TaskConditionHandlerRegistry] {message}");
                throw new InvalidOperationException(message);
            }

            return handler;
        }

        #endregion
    }
}
