using System;

namespace WS_Modules.FSM
{
    /// <summary>
    /// 基于委托的轻量条件。
    /// 适合条件数据已经存在于 owner 对象上的场景。
    /// </summary>
    public class FuncCondition<TOwner> : ICondition<TOwner>
    {
        private readonly Func<TOwner, bool> mConditionFunc;

        public FuncCondition(Func<TOwner, bool> conditionFunc)
        {
            mConditionFunc = conditionFunc ?? throw new ArgumentNullException(nameof(conditionFunc));
        }

        public bool Tick(TOwner owner)
        {
            return mConditionFunc(owner);
        }
    }
}
