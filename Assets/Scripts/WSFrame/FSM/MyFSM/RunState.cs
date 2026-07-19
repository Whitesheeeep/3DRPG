using UnityEngine;

public class RunState : IState<string>
{
    public IFSMOwn Owner { get; private set; }
    public IFSM<string> FSM { get; private set; }
    public string StateId => "Run";
    
    public RunState(IFSMOwn owner, IFSM<string> fsm)
    {
        Init(owner, fsm);
    }
    
    public void Init(IFSMOwn owner, IFSM<string> fsm)
    {
        Owner = owner;
        FSM = fsm;
    }

    public void OnEnter()
    {
        Debug.Log("进入 Run");
    }

    public void OnUpdate()
    {
        Debug.Log("正在 Run");
    }

    public void OnExit()
    {
        Debug.Log("退出 Run");
    }
}
