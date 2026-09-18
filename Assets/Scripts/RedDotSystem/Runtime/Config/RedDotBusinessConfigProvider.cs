using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 通过 WSFrame ConfigInstaller 统一注册全部红点业务配置。
    /// 每个业务模块保留独立配置 Asset，Provider 只负责建立按具体类型查询的索引。
    /// </summary>
    [CreateAssetMenu(
        fileName = "RedDotBusinessConfigProvider",
        menuName = "RPG/Red Dot/Business Config Provider",
        order = 41)]
    public sealed class RedDotBusinessConfigProvider : ConfigRegisterNodeBase
    {
        #region 配置字段

        [SerializeField, Tooltip("全部红点业务配置 Asset；同一种具体配置类型只能出现一次。")]
        private List<RedDotBusinessConfig> configs = new List<RedDotBusinessConfig>();

        #endregion

        #region 静态注册状态

        // key：业务配置的具体 C# 类型；value：该类型唯一对应的配置 Asset。
        private static readonly Dictionary<Type, RedDotBusinessConfig> configByTypeMap =
            new Dictionary<Type, RedDotBusinessConfig>();

        #endregion

        #region 公开查询

        /// <summary>获取当前 Provider 序列化的业务配置数量。</summary>
        public int RegisteredConfigCount => configs == null ? 0 : configs.Count;

        /// <summary>
        /// 获取 ConfigInstaller 最近一次注册的指定业务配置。
        /// </summary>
        /// <typeparam name="TConfig">业务配置的具体类型。</typeparam>
        /// <returns>已注册的业务配置 Asset。</returns>
        /// <exception cref="InvalidOperationException">目标类型尚未注册时抛出。</exception>
        public static TConfig GetConfig<TConfig>()
            where TConfig : RedDotBusinessConfig
        {
            Type configType = typeof(TConfig);
            if (!configByTypeMap.TryGetValue(configType, out RedDotBusinessConfig config))
            {
                throw new InvalidOperationException(
                    $"[RedDotBusinessConfigProvider] 未注册业务配置类型 {configType.FullName}。" );
            }

            return (TConfig)config;
        }

        #endregion

        #region 注册生命周期

        /// <summary>
        /// 注册全部业务配置并建立按具体类型查询的静态索引。
        /// 节点层级与节点引用合法性由 RedDotSystem 和业务实际使用点负责处理。
        /// </summary>
        /// <exception cref="InvalidOperationException">配置列表未初始化、包含空引用或类型重复时抛出。</exception>
        public override void Register()
        {
            // 每次 ConfigInstaller 注册都从空索引开始，避免上一轮 Play Mode 的静态引用残留。
            configByTypeMap.Clear();
            if (configs == null)
            {
                throw new InvalidOperationException(
                    "[RedDotBusinessConfigProvider] 业务配置列表未初始化，无法注册红点业务配置。" );
            }

            for (int index = 0; index < configs.Count; index++)
            {
                RedDotBusinessConfig config = configs[index];
                if (config == null)
                {
                    throw new InvalidOperationException(
                        $"[RedDotBusinessConfigProvider] 配置列表包含空引用，index={index}。" );
                }

                Type configType = config.GetType();
                if (!configByTypeMap.TryAdd(configType, config))
                {
                    throw new InvalidOperationException(
                        $"[RedDotBusinessConfigProvider] 配置类型重复注册：{configType.FullName}。" );
                }
            }

            Debug.Log(
                $"[RedDotBusinessConfigProvider] 已注册红点业务配置，configCount={configByTypeMap.Count}。" );
        }

        #endregion
    }
}
