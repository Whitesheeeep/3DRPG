using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.RewardSystemNS
{
    /// <summary>保存一个同步奖励请求的全部已准备领域批次。</summary>
    internal sealed class PreparedRewardGrant
    {
        #region 状态字段

        private readonly IReadOnlyList<IPreparedRewardPart> preparedParts;
        private bool committed;
        private bool notificationsPublished;

        #endregion

        #region 构造与状态查询

        /// <summary>创建本次请求的准备结果。</summary>
        /// <param name="parts">各领域已经校验的批次。</param>
        /// <exception cref="ArgumentNullException">批次列表为空时抛出。</exception>
        internal PreparedRewardGrant(IReadOnlyList<IPreparedRewardPart> parts)
        {
            preparedParts = parts ?? throw new ArgumentNullException(nameof(parts));
        }

        /// <summary>获取本批次包含的领域操作数量。</summary>
        internal int PartCount => preparedParts.Count;

        /// <summary>判断所有领域状态是否仍可提交。</summary>
        internal bool CanCommit
        {
            get
            {
                for (int index = 0; index < preparedParts.Count; index++)
                    if (!preparedParts[index].CanCommit) return false;
                return true;
            }
        }

        #endregion

        #region 提交与通知

        /// <summary>依次提交所有领域状态；调用期间不调用事件订阅者。</summary>
        /// <exception cref="InvalidOperationException">批次过期或重复提交时抛出。</exception>
        internal void CommitState()
        {
            if (committed) throw new InvalidOperationException("[RewardSystem] 奖励批次不能重复提交。");
            if (!CanCommit) throw new InvalidOperationException("[RewardSystem] 奖励批次依赖状态已变化，必须重新准备。");
            for (int index = 0; index < preparedParts.Count; index++) preparedParts[index].CommitState();
            committed = true;
        }

        /// <summary>发布已提交批次的变化通知；订阅者异常被记录且不回滚玩家状态。</summary>
        /// <exception cref="InvalidOperationException">状态尚未提交或通知已发布时抛出。</exception>
        internal void PublishNotifications()
        {
            if (!committed) throw new InvalidOperationException("[RewardSystem] 未提交的奖励批次不能发布通知。");
            if (notificationsPublished) throw new InvalidOperationException("[RewardSystem] 奖励批次通知不能重复发布。");
            notificationsPublished = true;
            for (int index = 0; index < preparedParts.Count; index++)
            {
                try
                {
                    preparedParts[index].PublishNotifications();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[RewardSystem] 奖励已提交，但领域通知发生异常，partIndex={index}。\n{exception}");
                }
            }
        }

        #endregion
    }

    /// <summary>把单个领域 Manager 的准备批次适配到奖励提交接口。一个通用的命令模式实现。除非有类需要直接操作底层状态，否则应使用此适配器。</summary>
    internal sealed class PreparedRewardGrantPart : IPreparedRewardPart
    {
        #region 依赖字段

        private readonly Func<bool> canCommit;
        private readonly Action commitState;
        private readonly Action publishNotifications;

        #endregion

        #region 构造与操作

        /// <summary>创建 Manager 准备批次适配器。</summary>
        /// <param name="canCommit">状态快照有效性查询。</param>
        /// <param name="commitState">纯状态提交操作。</param>
        /// <param name="publishNotifications">提交后的通知操作。</param>
        internal PreparedRewardGrantPart(Func<bool> canCommit, Action commitState, Action publishNotifications)
        {
            this.canCommit = canCommit ?? throw new ArgumentNullException(nameof(canCommit));
            this.commitState = commitState ?? throw new ArgumentNullException(nameof(commitState));
            this.publishNotifications = publishNotifications ?? throw new ArgumentNullException(nameof(publishNotifications));
        }

        /// <summary>判断 Manager 的准备状态是否仍有效。</summary>
        public bool CanCommit => canCommit();

        /// <summary>提交领域状态，不调用订阅者。</summary>
        public void CommitState() => commitState();

        /// <summary>发布领域变化通知。</summary>
        public void PublishNotifications() => publishNotifications();

        #endregion
    }
}
