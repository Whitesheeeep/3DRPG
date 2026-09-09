namespace WS_Modules.FSM
{
    /// <summary>FSM/HFSM 共用状态接口。</summary>
    public interface IState<TStateId, TOwner>
    {
        TStateId StateId { get; }
        IStateMachine<TStateId, TOwner> Machine { get; }
        TOwner Owner { get; }

        bool CanEnter();
        void Init(TOwner owner, IStateMachine<TStateId, TOwner> machine);
        /// <summary>进入状态；状态机节点可选择跳过默认子状态。</summary>
        /// <param name="suppressDefaultState">是否只激活状态机节点而不进入默认子状态。</param>
        void OnEnter(bool suppressDefaultState = false);
        void OnUpdate();
        void OnFixedUpdate();
        void OnLateUpdate();
        void OnAnimationMove();
        void OnExit();
    }
}
