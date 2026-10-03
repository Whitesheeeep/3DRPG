using System;
using System.Collections.Generic;

namespace RPG.RewardSystemNS
{
    /// <summary>把一种精确奖励定义类型转换为可提交的领域操作。</summary>
    public interface IRewardHandler
    {
        /// <summary>获取此 Handler 精确处理的奖励定义类型。</summary>
        Type DefinitionType { get; }

        /// <summary>准备同类奖励；不得修改玩家状态或发布事件。</summary>
        /// <param name="definitions">同一精确类型的奖励定义。</param>
        /// <param name="preparedParts">成功时返回待提交领域批次。</param>
        /// <param name="result">结果或业务拒绝原因。</param>
        /// <returns>准备成功时返回 true。</returns>
        bool TryPrepare(
            IReadOnlyList<RewardDefinition> definitions,
            out IReadOnlyList<IPreparedRewardPart> preparedParts,
            out RewardGrantResult result);
    }
}
