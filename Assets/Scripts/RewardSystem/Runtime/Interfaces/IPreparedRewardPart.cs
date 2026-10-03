namespace RPG.RewardSystemNS
{
    /// <summary>表示一个已经校验、等待统一提交的领域奖励批次。最后通过 PreparedRewardGrant 进行总的提交。</summary>
    public interface IPreparedRewardPart
    {
        /// <summary>判断准备时依赖的领域状态是否仍然有效。</summary>
        bool CanCommit { get; }

        /// <summary>只写入状态，不调用事件订阅者。</summary>
        void CommitState();

        /// <summary>在所有奖励和调用方状态都已提交后发布变化通知。</summary>
        void PublishNotifications();
    }
}
