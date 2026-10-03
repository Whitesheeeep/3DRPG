using System;

namespace RPG.RewardSystemNS
{
    /// <summary>描述可由通用奖励系统处理的静态奖励配置。</summary>
    [Serializable]
    public abstract class RewardDefinition
    {
        /// <summary>校验奖励配置；具体奖励定义负责检查自己的字段。</summary>
        /// <exception cref="ArgumentException">奖励字段非法时抛出。</exception>
        public abstract void Validate();
    }
}
