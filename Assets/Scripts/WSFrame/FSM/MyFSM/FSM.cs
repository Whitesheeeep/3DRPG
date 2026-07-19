using System;
using System.Collections.Generic;
using WS_Modules.MonoSystem;

public interface IFSMOwn
{
}

public interface IFSM<TStateId>
{
    IState<TStateId> CurrentState { get; }
    IState<TStateId> LastState { get; }
    Dictionary<TStateId, IState<TStateId>> AllStates { get; }
    IFSMOwn Owner { get; }
    void Initialize(IState<TStateId> startState);
    void Update();
    void ChangeState(TStateId newState);
    bool HasState(TStateId state);
    void AddState(IState<TStateId> state);
}

public class FSM<TStateId> : IFSM<TStateId>
{
    public IState<TStateId> CurrentState { get; private set; }
    public IState<TStateId> LastState { get; private set; }
    
    // All states in the FSM
    public Dictionary<TStateId, IState<TStateId>> AllStates { get; private set; } = new();
    
    public IFSMOwn Owner { get; private set; }
    
    public FSM(IFSMOwn owner)
    {
        Owner = owner;
    }
    
    public void Initialize(IState<TStateId> startState)
    {
        CurrentState = startState;
        ChangeState(startState.StateId);
    }
    
    public void Update()
    {
        CurrentState.OnUpdate();
    }
    
    public void ChangeState(TStateId newState)
    {
        if (!HasState(newState))
        {
            throw new Exception("State not found in FSM");
        }
        if (CurrentState is not null)
        {
            CurrentState.OnExit();
            PublicMono.Instance.UnRegisterUpdate(CurrentState.OnUpdate);
        }

        LastState = CurrentState;
        CurrentState = AllStates[newState];
        
        if (CurrentState is null)
            return;
        CurrentState.OnEnter();
        PublicMono.Instance.RegisterUpdate(CurrentState.OnUpdate);
    }
    
    public bool HasState(TStateId state) => AllStates.ContainsKey(state);

    public void AddState(IState<TStateId> state)
    {
        AllStates.Add(state.StateId, state);
    }
}