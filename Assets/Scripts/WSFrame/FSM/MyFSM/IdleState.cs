using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class IdleState: IState<string> 
{
    public IFSMOwn Owner { get; private set; }
    public IFSM<string> FSM { get; private set; }
    public string StateId => "Idle";
    
    public IdleState(IFSMOwn owner, IFSM<string> fsm)
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
        Debug.Log("进入 Idle");
    }

    public void OnUpdate()
    {
        Debug.Log("正在 Idle");
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Debug.Log("Space key pressed - triggering FSM event");
            FSM.ChangeState("Run");
        }
    }

    public void OnExit()
    {
        Debug.Log("退出 Idle");
    }
}