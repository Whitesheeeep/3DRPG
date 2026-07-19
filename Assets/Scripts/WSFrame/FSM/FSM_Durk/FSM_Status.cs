using System;
using System.Collections.Generic;

/// <summary>
/// 表示有限状态机（FSM）中的单个状态。
/// 负责保存状态标识（name）、可选的子状态机引用（subMachine）、激活标记（activated）和共享数据黑板（dataBase）；
/// 管理从该状态发出的过渡集合（transitions）并通过回调处理进入/退出/每帧逻辑（m_onEnter/m_onExit/m_onAction）。
/// 提供生命周期方法：OnInit（初始化）、OnEnter（进入时）、OnAction（每帧更新）和OnExit（退出时），以及 AddTransition 用于注册过渡。
/// 此类为状态机的基本构建块，允许通过组合状态和过渡来实现行为切换与复合状态机。
/// </summary>
public class FSM_Status<TStateId>
{
    /// <summary>
    /// 状态名称
    /// </summary>
    public TStateId name;
    /// <summary>
    /// 子状态机
    /// </summary>
    public IFSM_Machine<TStateId> subMachine;
    /// <summary>
    /// 该状态是否激活
    /// </summary>
    public bool activated;
    /// <summary>
    /// 数据黑板
    /// </summary>
    public FSM_DataBase dataBase;
    /// <summary>
    /// 过渡线
    /// </summary>
    public List<FSM_Transition<TStateId>> transitions;
    /// <summary>
    /// 每帧刷新的事件
    /// </summary>
    private Action<FSM_Status<TStateId>> m_onAction;
    /// <summary>
    /// 进入该状态的事件
    /// </summary>
    private Action<FSM_Status<TStateId>> m_onEnter;
    /// <summary>
    /// 退出该状态的事件
    /// </summary>
    private Action<FSM_Status<TStateId>> m_onExit;

    public FSM_Status(Action<FSM_Status<TStateId>> onEnter = null, Action<FSM_Status<TStateId>> onExit = null, Action<FSM_Status<TStateId>> onAction = null)
    {
        this.m_onEnter = onEnter;
        this.m_onExit = onExit;
        this.m_onAction = onAction;
    }
    /// <summary>
    /// 添加过渡
    /// </summary>
    public virtual void AddTransition(FSM_Transition<TStateId> transition)
    {
        transitions = transitions ?? new List<FSM_Transition<TStateId>>();
        transitions.Add(transition);
    }
    /// <summary>
    /// 初始化时
    /// </summary>
    public virtual void OnInit()
    { 
        
    }
    /// <summary>
    /// 该状态的行为
    /// </summary>
    public virtual void OnAction() 
    {
        m_onAction?.Invoke(this);
    }
    /// <summary>
    /// 进入该状态
    /// </summary>
    public virtual void OnEnter() 
    {
        m_onEnter?.Invoke(this);
    }
    /// <summary>
    /// 退出该状态
    /// </summary>
    public virtual void OnExit() 
    {
        m_onExit?.Invoke(this);
    }
}

public class FSM_Status : FSM_Status<string>
{
    public FSM_Status(Action<FSM_Status<string>> onEnter = null, Action<FSM_Status<string>> onExit = null, Action<FSM_Status<string>> onAction = null) : base(onEnter, onExit, onAction)
    {
    }
}