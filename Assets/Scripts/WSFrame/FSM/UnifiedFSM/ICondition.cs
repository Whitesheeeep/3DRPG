namespace WS_Modules.FSM
{
    /// <summary>
    /// Transition 使用的切换条件。
    /// 同一条 Transition 上的所有条件都通过后，才会尝试切换。
    /// </summary>
    public interface ICondition<TOwner>
    {
        /// <summary>
        /// 使用 owner 作为业务上下文进行条件判断。
        /// </summary>
        bool Tick(TOwner owner);
    }
}
