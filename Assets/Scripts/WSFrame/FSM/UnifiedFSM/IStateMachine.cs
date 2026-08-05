namespace WS_Modules.FSM
{
    /// <summary>
    /// 状态机接口。
    /// 状态机本身也继承 IState，因此可以作为父状态机里的一个子状态，用于实现 HFSM。
    /// </summary>
    public interface IStateMachine<TStateId, TOwner> : IState<TStateId, TOwner>
    {
        /// <summary>
        /// 当前激活的子状态。
        /// </summary>
        IState<TStateId, TOwner> CurrentState { get; }

        /// <summary>
        /// 上一个激活的子状态。
        /// </summary>
        IState<TStateId, TOwner> PreviousState { get; }

        /// <summary>
        /// 添加子状态。子状态可以是普通状态，也可以是另一个 StateMachine。
        /// </summary>
        void AddState(IState<TStateId, TOwner> state);

        /// <summary>
        /// 设置该状态机进入时自动进入的默认子状态。
        /// </summary>
        void SetDefaultState(TStateId stateId);

        /// <summary>
        /// 主动切换状态。目标不存在、重复切换或 CanEnter 不通过时返回 false。
        /// </summary>
        bool ChangeState(TStateId stateId);

        /// <summary>
        /// 请求切换状态；当前层找不到目标时，会继续向父状态机查找。
        /// </summary>
        /// <param name="stateId">要请求进入的状态 ID。</param>
        /// <returns>请求成功进入目标状态时返回 true。</returns>
        bool RequestStateChange(TStateId stateId);

        /// <summary>
        /// 按从当前状态机到目标子状态的路径执行切换。
        /// </summary>
        /// <param name="statePath">从当前状态机开始、依次指向嵌套子状态的直接子状态 ID。</param>
        /// <returns>路径完整有效并完成切换时返回 true。</returns>
        bool ChangeStatePath(params TStateId[] statePath);

        /// <summary>
        /// 添加从指定源状态出发的自动过渡。
        /// </summary>
        void AddTransition(Transition<TStateId, TOwner> transition);

        /// <summary>
        /// 添加任意状态自动过渡。AnyTransition 会先于当前状态自己的 Transition 检测。
        /// </summary>
        void AddAnyTransition(Transition<TStateId, TOwner> transition);
    }
}
