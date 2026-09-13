using System;
using System.Collections.Generic;

namespace WS_Modules.FSM
{
    /// <summary>
    /// 自动状态切换线。
    /// Transition 只描述来源、目标、优先级和条件；真正的切换由 StateMachine 执行。
    /// </summary>
    public class Transition<TStateId, TOwner>
    {
        private readonly List<ICondition<TOwner>> mConditions = new();
        private readonly List<Action<TOwner>> mCommittedCallbacks = new();
        private readonly IReadOnlyList<TStateId> mTargetStatePath;

        /// <summary>
        /// 普通过渡的源状态。注册为 AnyTransition 时会忽略该值。
        /// </summary>
        public TStateId FromStateId { get; private set; }

        /// <summary>
        /// 条件满足后尝试进入的目标状态。
        /// </summary>
        public TStateId ToStateId { get; private set; }

        /// <summary>获取从当前注册状态机开始解析的完整目标路径。</summary>
        public IReadOnlyList<TStateId> TargetStatePath => mTargetStatePath;

        /// <summary>获取该 Transition 是否使用完整目标路径。</summary>
        public bool HasTargetStatePath => mTargetStatePath != null;

        /// <summary>
        /// 过渡优先级，数值越大越先检测。
        /// </summary>
        public int WeightOrder { get; private set; }

        /// <summary>
        /// 该过渡需要同时满足的条件列表。
        /// </summary>
        public IReadOnlyList<ICondition<TOwner>> Conditions => mConditions;

        /// <summary>创建当前状态机内的直接子状态 Transition。</summary>
        /// <param name="fromStateId">Transition 的来源直接子状态。</param>
        /// <param name="toStateId">Transition 的目标直接子状态。</param>
        /// <param name="weightOrder">Transition 优先级，数值越大越先执行。</param>
        public Transition(TStateId fromStateId, TStateId toStateId, int weightOrder = 0)
        {
            FromStateId = fromStateId;
            ToStateId = toStateId;
            WeightOrder = weightOrder;
            mTargetStatePath = null;
        }

        /// <summary>创建从当前状态机开始解析的完整路径 Transition。</summary>
        /// <param name="fromStateId">Transition 的来源直接子状态。</param>
        /// <param name="targetStatePath">从当前状态机开始的目标路径。</param>
        /// <param name="weightOrder">Transition 优先级，数值越大越先执行。</param>
        public Transition(
            TStateId fromStateId,
            IReadOnlyList<TStateId> targetStatePath,
            int weightOrder = 0)
        {
            if (targetStatePath == null || targetStatePath.Count == 0)
                throw new ArgumentException("目标路径不能为空。", nameof(targetStatePath));

            FromStateId = fromStateId;
            ToStateId = targetStatePath[targetStatePath.Count - 1];
            WeightOrder = weightOrder;
            mTargetStatePath = new List<TStateId>(targetStatePath).AsReadOnly();
        }

        public Transition<TStateId, TOwner> AddCondition(ICondition<TOwner> condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            if (!mConditions.Contains(condition))
            {
                mConditions.Add(condition);
            }

            return this;
        }

        public Transition<TStateId, TOwner> AddCondition(Func<TOwner, bool> conditionFunc)
        {
            return AddCondition(new FuncCondition<TOwner>(conditionFunc));
        }

        /// <summary>登记完整状态路径成功提交后的副作用回调。</summary>
        /// <param name="callback">状态路径成功进入后执行的回调。</param>
        /// <returns>当前 Transition。</returns>
        public Transition<TStateId, TOwner> OnCommitted(Action<TOwner> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            if (!mCommittedCallbacks.Contains(callback))
                mCommittedCallbacks.Add(callback);
            return this;
        }

        /// <summary>
        /// 所有条件都通过时返回 true。没有条件的过渡默认通过。
        /// </summary>
        public bool Tick(TOwner owner)
        {
            for (int i = 0; i < mConditions.Count; i++)
            {
                if (!mConditions[i].Tick(owner))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>通知所有者该 Transition 已完成状态路径提交。</summary>
        /// <param name="owner">当前状态机 Owner。</param>
        internal void NotifyCommitted(TOwner owner)
        {
            for (int index = 0; index < mCommittedCallbacks.Count; index++)
                mCommittedCallbacks[index](owner);
        }
    }
}
