namespace WS_Modules.FSM
{
    /// <summary>类状态基类，业务状态只需重写需要的生命周期。</summary>
    public abstract class StateBase<TStateId, TOwner> : IState<TStateId, TOwner>
    {
        public TStateId StateId { get; private set; }
        public IStateMachine<TStateId, TOwner> Machine { get; private set; }
        public TOwner Owner { get; private set; }

        protected StateBase(TStateId stateId)
        {
            StateId = stateId;
        }

        public virtual bool CanEnter() => true;

        public virtual void Init(TOwner owner, IStateMachine<TStateId, TOwner> machine)
        {
            Owner = owner;
            Machine = machine;
        }

        /// <summary>进入状态；普通叶状态不使用默认子状态参数。</summary>
        /// <param name="suppressDefaultState">是否跳过子状态机默认状态。</param>
        public virtual void OnEnter(bool suppressDefaultState = false) { }
        public virtual void OnUpdate() { }
        public virtual void OnFixedUpdate() { }
        public virtual void OnLateUpdate() { }
        public virtual void OnAnimationMove() { }
        public virtual void OnExit() { }

        public virtual string ToDebugString(string indent, bool isLast, bool isCurrent, bool isDefault)
        {
            return FormatDebugLine(indent, isLast, StateId, BuildDebugTags(isCurrent, isDefault, false));
        }

        protected static string FormatDebugLine(string indent, bool isLast, object stateId, string tags)
        {
            return indent + (isLast ? "└─ " : "├─ ") + stateId + tags;
        }

        protected static string BuildDebugTags(bool isCurrent, bool isDefault, bool isStateMachine)
        {
            string tags = string.Empty;
            if (isCurrent) tags += tags.Length == 0 ? "Current" : ", Current";
            if (isDefault) tags += tags.Length == 0 ? "Default" : ", Default";
            if (isStateMachine) tags += tags.Length == 0 ? "StateMachine" : ", StateMachine";
            return tags.Length == 0 ? string.Empty : " [" + tags + "]";
        }
    }
}
