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
        void OnEnter();
        void OnUpdate();
        void OnFixedUpdate();
        void OnLateUpdate();
        void OnAnimationMove();
        void OnExit();
    }
}