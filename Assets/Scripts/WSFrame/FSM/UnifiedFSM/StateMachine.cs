using System;
using System.Collections.Generic;
using System.Text;

namespace WS_Modules.FSM
{
    /// <summary>统一的 FSM/HFSM 状态机，状态机本身也可以作为子状态。</summary>
    public class StateMachine<TStateId, TOwner> : StateBase<TStateId, TOwner>, IStateMachine<TStateId, TOwner>
    {
        private readonly Dictionary<TStateId, IState<TStateId, TOwner>> mStates = new();
        private readonly Dictionary<TStateId, List<Transition<TStateId, TOwner>>> mTransitions = new();
        private readonly List<Transition<TStateId, TOwner>> mAnyTransitions = new();

        private bool mHasDefaultState;
        private TStateId mDefaultStateId;

        public IState<TStateId, TOwner> CurrentState { get; private set; }
        public IState<TStateId, TOwner> PreviousState { get; private set; }
        public IReadOnlyDictionary<TStateId, IState<TStateId, TOwner>> States => mStates;

        public StateMachine(TStateId stateId) : base(stateId) { }

        public StateMachine(TStateId stateId, TOwner owner) : base(stateId)
        {
            Init(owner, null);
        }

        public override void Init(TOwner owner, IStateMachine<TStateId, TOwner> machine)
        {
            base.Init(owner, machine);
            foreach (var state in mStates.Values)
                state.Init(owner, this);
        }

        public void AddState(IState<TStateId, TOwner> state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            mStates.Add(state.StateId, state);
            state.Init(Owner, this);

            if (!mHasDefaultState)
                SetDefaultState(state.StateId);
        }

        public CustomState<TStateId, TOwner> State(TStateId stateId)
        {
            if (mStates.TryGetValue(stateId, out var state))
                return state as CustomState<TStateId, TOwner>;

            var customState = new CustomState<TStateId, TOwner>(stateId);
            AddState(customState);
            return customState;
        }

        public void SetDefaultState(TStateId stateId)
        {
            if (!mStates.ContainsKey(stateId))
                throw new ArgumentException("Default state must be added before it can be selected.", nameof(stateId));

            mDefaultStateId = stateId;
            mHasDefaultState = true;
        }

        public bool ChangeState(TStateId stateId)
        {
            if (!mStates.TryGetValue(stateId, out var nextState))
                return false;

            if (CurrentState != null &&
                EqualityComparer<TStateId>.Default.Equals(CurrentState.StateId, stateId))
                return false;

            if (!nextState.CanEnter())
                return false;

            CurrentState?.OnExit();
            PreviousState = CurrentState;
            CurrentState = nextState;
            CurrentState.OnEnter();
            return true;
        }

        public void AddTransition(Transition<TStateId, TOwner> transition)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));

            if (!mTransitions.TryGetValue(transition.FromStateId, out var transitions))
            {
                transitions = new List<Transition<TStateId, TOwner>>();
                mTransitions.Add(transition.FromStateId, transitions);
            }

            transitions.Add(transition);
            SortTransitions(transitions);
        }

        public void AddAnyTransition(Transition<TStateId, TOwner> transition)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));

            mAnyTransitions.Add(transition);
            SortTransitions(mAnyTransitions);
        }

        public override void OnEnter()
        {
            base.OnEnter();
            if (mHasDefaultState)
                ChangeState(mDefaultStateId);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (!TryAutoTransition())
                CurrentState?.OnUpdate();
        }

        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            CurrentState?.OnFixedUpdate();
        }

        public override void OnLateUpdate()
        {
            base.OnLateUpdate();
            CurrentState?.OnLateUpdate();
        }

        public override void OnAnimationMove()
        {
            base.OnAnimationMove();
            CurrentState?.OnAnimationMove();
        }

        public override void OnExit()
        {
            if (CurrentState != null)
            {
                CurrentState.OnExit();
                PreviousState = CurrentState;
                CurrentState = null;
            }

            base.OnExit();
        }

        private bool TryAutoTransition()
        {
            if (TryTransitions(mAnyTransitions))
                return true;

            if (CurrentState == null)
                return false;

            if (!mTransitions.TryGetValue(CurrentState.StateId, out var transitions))
                return false;

            return TryTransitions(transitions);
        }

        private bool TryTransitions(List<Transition<TStateId, TOwner>> transitions)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                var transition = transitions[i];

                if (CurrentState != null &&
                    EqualityComparer<TStateId>.Default.Equals(CurrentState.StateId, transition.ToStateId))
                    continue;

                if (transition.Tick(Owner) && ChangeState(transition.ToStateId))
                    return true;
            }

            return false;
        }

        private static void SortTransitions(List<Transition<TStateId, TOwner>> transitions)
        {
            transitions.Sort((left, right) => right.WeightOrder.CompareTo(left.WeightOrder));
        }

        public override string ToString() => ToDebugString();

        public string ToDebugString()
        {
            var builder = new StringBuilder();
            builder.Append(StateId);
            builder.Append(BuildDebugTags(false, false, true));
            AppendChildrenDebugString(builder, string.Empty);
            return builder.ToString();
        }

        public override string ToDebugString(string indent, bool isLast, bool isCurrent, bool isDefault)
        {
            var builder = new StringBuilder();
            builder.Append(FormatDebugLine(indent, isLast, StateId, BuildDebugTags(isCurrent, isDefault, true)));
            AppendChildrenDebugString(builder, indent + (isLast ? "   " : "│  "));
            return builder.ToString();
        }

        private void AppendChildrenDebugString(StringBuilder builder, string childIndent)
        {
            if (mStates.Count == 0)
                return;

            int index = 0;
            foreach (var state in mStates.Values)
            {
                builder.AppendLine();

                bool isLast = index == mStates.Count - 1;
                bool isCurrent = CurrentState != null &&
                                 EqualityComparer<TStateId>.Default.Equals(CurrentState.StateId, state.StateId);
                bool isDefault = mHasDefaultState &&
                                 EqualityComparer<TStateId>.Default.Equals(mDefaultStateId, state.StateId);

                builder.Append(ToChildDebugString(state, childIndent, isLast, isCurrent, isDefault));
                index++;
            }
        }

        private string ToChildDebugString(
            IState<TStateId, TOwner> state,
            string indent,
            bool isLast,
            bool isCurrent,
            bool isDefault)
        {
            if (state is StateBase<TStateId, TOwner> stateBase)
                return stateBase.ToDebugString(indent, isLast, isCurrent, isDefault);

            return FormatDebugLine(indent, isLast, state.StateId, BuildDebugTags(isCurrent, isDefault, false));
        }
    }
}