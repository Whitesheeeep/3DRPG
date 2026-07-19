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

        /// <summary>
        /// 普通过渡的源状态。注册为 AnyTransition 时会忽略该值。
        /// </summary>
        public TStateId FromStateId { get; private set; }

        /// <summary>
        /// 条件满足后尝试进入的目标状态。
        /// </summary>
        public TStateId ToStateId { get; private set; }

        /// <summary>
        /// 过渡优先级，数值越大越先检测。
        /// </summary>
        public int WeightOrder { get; private set; }

        /// <summary>
        /// 该过渡需要同时满足的条件列表。
        /// </summary>
        public IReadOnlyList<ICondition<TOwner>> Conditions => mConditions;

        public Transition(TStateId fromStateId, TStateId toStateId, int weightOrder = 0)
        {
            FromStateId = fromStateId;
            ToStateId = toStateId;
            WeightOrder = weightOrder;
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
    }
}
