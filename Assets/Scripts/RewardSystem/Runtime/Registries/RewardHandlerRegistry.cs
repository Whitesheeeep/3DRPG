using System;
using System.Collections.Generic;

namespace RPG.RewardSystemNS
{
    /// <summary>按奖励定义精确 CLR 类型解析奖励 Handler。</summary>
    public sealed class RewardHandlerRegistry
    {
        #region 状态字段

        // key：奖励定义 CLR 类型；value：唯一负责解释该配置类型的 Handler。
        private readonly Dictionary<Type, IRewardHandler> handlerByDefinitionTypeMap =
            new Dictionary<Type, IRewardHandler>();
        private bool defaultsRegistered;

        #endregion

        #region 注册与查询

        /// <summary>显式登记通用奖励 Handler；同一类型重复登记会报错。</summary>
        /// <typeparam name="TDefinition">精确奖励定义类型。</typeparam>
        /// <param name="handler">处理该类型的 Handler。</param>
        /// <exception cref="ArgumentNullException">Handler 为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">类型不匹配或已登记时抛出。</exception>
        public void Register<TDefinition>(IRewardHandler handler) where TDefinition : RewardDefinition
        {
            if (handler == null)
            {
                UnityEngine.Debug.LogError($"[RewardHandlerRegistry] 注册奖励 Handler 失败，definitionType={typeof(TDefinition).FullName}, handler=null。");
                throw new ArgumentNullException(nameof(handler));
            }
            Type definitionType = typeof(TDefinition);
            Type handlerDefinitionType = handler.DefinitionType;
            if (handlerDefinitionType != definitionType)
            {
                UnityEngine.Debug.LogError(
                    $"[RewardHandlerRegistry] Handler 声明类型与注册类型不一致，registeredType={definitionType.FullName}, handlerType={handlerDefinitionType?.FullName ?? "<null>"}。");
                throw new InvalidOperationException(
                    $"[RewardHandlerRegistry] Handler 类型 {handlerDefinitionType?.FullName ?? "<null>"} 与登记类型 {definitionType.FullName} 不一致。");
            }
            RegisterInternal(handler);
        }

        /// <summary>登记本实例的默认 Handler；重复调用不会重复登记。</summary>
        /// <param name="handlers">该业务架构使用的默认 Handler。</param>
        /// <exception cref="ArgumentNullException">Handler 数组或其中一项为空时抛出。</exception>
        /// <exception cref="InvalidOperationException">默认 Handler 与已有登记冲突时抛出。</exception>
        public void RegisterDefault(params IRewardHandler[] handlers)
        {
            if (handlers == null) throw new ArgumentNullException(nameof(handlers));
            if (defaultsRegistered) return;
            for (int index = 0; index < handlers.Length; index++)
            {
                if (handlers[index] == null)
                {
                    UnityEngine.Debug.LogError($"[RewardHandlerRegistry] 默认 Handler 注册失败，index={index} 的配置为空。");
                    throw new ArgumentNullException(nameof(handlers));
                }
                RegisterInternal(handlers[index]);
            }

            defaultsRegistered = true;
            UnityEngine.Debug.Log($"[RewardHandlerRegistry] 已登记默认奖励 Handler，count={handlers.Length}。");
        }

        /// <summary>按定义精确类型解析 Handler。</summary>
        /// <param name="definitionType">奖励定义 CLR 类型。</param>
        /// <returns>已登记 Handler。</returns>
        /// <exception cref="InvalidOperationException">没有该类型的 Handler 时抛出。</exception>
        public IRewardHandler Resolve(Type definitionType)
        {
            if (definitionType == null) throw new ArgumentNullException(nameof(definitionType));
            if (!handlerByDefinitionTypeMap.TryGetValue(definitionType, out IRewardHandler handler))
            {
                UnityEngine.Debug.LogError(
                    $"[RewardHandlerRegistry] 奖励定义缺少 Handler 注册，definitionType={definitionType.FullName}。");
                throw new InvalidOperationException(
                    $"[RewardHandlerRegistry] 未登记奖励定义 Handler：{definitionType.FullName}。");
            }
            return handler;
        }

        /// <summary>获取当前已登记 Handler 数量。</summary>
        public int Count => handlerByDefinitionTypeMap.Count;

        #endregion

        #region 内部注册

        /// <summary>执行唯一类型登记并记录注册成功。</summary>
        /// <param name="handler">待登记 Handler。</param>
        /// <exception cref="InvalidOperationException">类型已经登记时抛出。</exception>
        private void RegisterInternal(IRewardHandler handler)
        {
            Type definitionType = handler.DefinitionType;
            if (definitionType == null || !typeof(RewardDefinition).IsAssignableFrom(definitionType))
            {
                UnityEngine.Debug.LogError(
                    $"[RewardHandlerRegistry] Handler 声明了无效奖励定义类型，handlerType={handler.GetType().FullName}, definitionType={definitionType?.FullName ?? "<null>"}。");
                throw new InvalidOperationException("[RewardHandlerRegistry] Handler 必须声明具体 RewardDefinition 类型。");
            }
            if (handlerByDefinitionTypeMap.ContainsKey(definitionType))
            {
                UnityEngine.Debug.LogError(
                    $"[RewardHandlerRegistry] 奖励定义类型重复登记，definitionType={definitionType.FullName}。");
                throw new InvalidOperationException(
                    $"[RewardHandlerRegistry] 奖励定义类型重复登记：{definitionType.FullName}。");
            }
            handlerByDefinitionTypeMap.Add(definitionType, handler);
            UnityEngine.Debug.Log($"[RewardHandlerRegistry] 已登记奖励 Handler，definitionType={definitionType.FullName}。");
        }

        #endregion
    }
}
