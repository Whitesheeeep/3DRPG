using System.Collections.Generic;

public interface ITransition<TStateId>
{
    public TStateId fromStatusID { get; }
    public TStateId toStatusID { get;  }
    public int weightOrder { get;  }
    public List<ICondition<TStateId>> conditions { get; }
    public bool Tick(IFSMOwn dataBase);
}

public class Transition<TStateId> : ITransition<TStateId>
{
    public TStateId fromStatusID { get; }
    public TStateId toStatusID { get; }
    public int weightOrder { get; }
    public List<ICondition<TStateId>> conditions { get; }
    
    public Transition(TStateId fromStatusID, TStateId toStatusID, int weightOrder = 0)
    {
        this.fromStatusID = fromStatusID;
        this.toStatusID = toStatusID;
        this.weightOrder = weightOrder;
    }

    public bool Tick(IFSMOwn dataBase) => conditions.TrueForAll(condition => condition.Tick(dataBase));

    public void AddCondition(ICondition<TStateId> condition)
    {
        if (!conditions.Contains(condition))
        {
            conditions.Add(condition);
        }
    }

    public bool IsSame(TStateId fromStatus, TStateId toStatus)
    {
        return EqualityComparer<TStateId>.Default.Equals(fromStatusID, fromStatus) &&
               EqualityComparer<TStateId>.Default.Equals(toStatusID, toStatus);
    }
}

public interface ICondition<TStateId>
{
    TStateId dataName { get; set; }
    bool Tick(IFSMOwn dataBase);
}

public class Condition<TStateId> : ICondition<TStateId>
{
    public TStateId dataName { get; set; }
    private readonly System.Func<IFSMOwn, bool> conditionFunc;

    public Condition(TStateId dataName, System.Func<IFSMOwn, bool> conditionFunc)
    {
        this.dataName = dataName;
        this.conditionFunc = conditionFunc;
    }

    public bool Tick(IFSMOwn dataBase) => conditionFunc(dataBase);
}
