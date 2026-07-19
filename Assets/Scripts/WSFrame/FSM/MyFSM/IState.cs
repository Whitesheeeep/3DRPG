public interface IState<TStateId>
{
    public IFSMOwn Owner { get; }
    public IFSM<TStateId> FSM { get; }
    public TStateId StateId { get; }
    public void Init(IFSMOwn owner, IFSM<TStateId> fsm);
    public void OnEnter();
    public void OnUpdate();
    public void OnExit();
    
    
}