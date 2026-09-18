using System;
using UnityEngine;
using WS_Modules.ConfigInstaller;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 通过 WSFrame ConfigInstaller 将正式红点配置写入 RedDotSystem 的静态配置入口。
    /// </summary>
    [CreateAssetMenu(
        fileName = "RedDotConfigProvider",
        menuName = "RPG/Red Dot/Config Provider",
        order = 2)]
    public sealed class RedDotConfigProvider : ConfigRegisterNodeBase
    {
        #region 配置字段

        [SerializeField]
        private RedDotConfig config;

        #endregion

        #region 公开属性

        /// <summary>获取待注入的正式红点配置。</summary>
        public RedDotConfig Config => config;

        #endregion

        #region 注册生命周期

        /// <summary>
        /// 校验并将配置 Asset 写入 RedDotSystem 的静态字段。
        /// </summary>
        /// <exception cref="InvalidOperationException">Config 未配置时抛出。</exception>
        public override void Register()
        {
            if (config == null)
            {
                throw new InvalidOperationException(
                    "[RedDotConfigProvider] 未配置 RedDotConfig，无法注入 RedDotSystem。");
            }

            RedDotSystem.Config = config;
            Debug.Log(
                $"[RedDotConfigProvider] 已注入 RedDotConfig，config={config.name}，nodeCount={config.NodeKeys.Count}。");
        }

        #endregion
    }
}
