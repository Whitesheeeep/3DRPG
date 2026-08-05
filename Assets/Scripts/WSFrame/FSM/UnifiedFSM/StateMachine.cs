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

        /// <summary>
        /// 创建未绑定 Owner 的状态机，适合先构建状态树再通过 Init 绑定宿主。
        /// </summary>
        public StateMachine(TStateId stateId) : base(stateId) { }

        /// <summary>
        /// 创建并绑定 Owner 的状态机。
        /// </summary>
        public StateMachine(TStateId stateId, TOwner owner) : base(stateId)
        {
            Init(owner, null);
        }

        /// <summary>
        /// 绑定 Owner 和父状态机，并递归刷新已注册子状态的上下文。
        /// </summary>
        public override void Init(TOwner owner, IStateMachine<TStateId, TOwner> machine)
        {
            base.Init(owner, machine);
            foreach (var state in mStates.Values)
                state.Init(owner, this);
        }

        /// <summary>
        /// 添加一个直接子状态，并立即绑定当前 Owner 和状态机。
        /// </summary>
        public void AddState(IState<TStateId, TOwner> state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            mStates.Add(state.StateId, state);
            state.Init(Owner, this);

            if (!mHasDefaultState)
                SetDefaultState(state.StateId);
        }

        /// <summary>
        /// 获取或创建指定 ID 的链式自定义子状态。
        /// </summary>
        public CustomState<TStateId, TOwner> State(TStateId stateId)
        {
            if (mStates.TryGetValue(stateId, out var state))
                return state as CustomState<TStateId, TOwner>;

            var customState = new CustomState<TStateId, TOwner>(stateId);
            AddState(customState);
            return customState;
        }

        /// <summary>
        /// 设置状态机进入时自动激活的直接子状态。
        /// </summary>
        public void SetDefaultState(TStateId stateId)
        {
            if (!mStates.ContainsKey(stateId))
                throw new ArgumentException("Default state must be added before it can be selected.", nameof(stateId));

            mDefaultStateId = stateId;
            mHasDefaultState = true;
        }

        /// <summary>
        /// 在当前状态机的直接子状态中执行切换。
        /// </summary>
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

        /// <summary>
        /// 请求切换状态；当前状态机没有目标时，将请求转交给父状态机。
        /// </summary>
        /// <param name="stateId">要请求进入的状态 ID。</param>
        /// <returns>请求成功进入目标状态时返回 true。</returns>
        public bool RequestStateChange(TStateId stateId)
        {
            if (mStates.ContainsKey(stateId))
                return ChangeState(stateId);

            return Machine != null && Machine.RequestStateChange(stateId);
        }

        /// <summary>
        /// 按直接子状态 ID 路径执行层级切换，路径无效时保持现状。
        /// </summary>
        /// <param name="statePath">从当前状态机开始、依次指向嵌套子状态的直接子状态 ID。</param>
        /// <returns>路径完整有效并完成切换时返回 true。</returns>
        public bool ChangeStatePath(params TStateId[] statePath)
        {
            if (!TryValidateStatePath(statePath))
                return false;

            StateMachine<TStateId, TOwner> currentMachine = this;
            for (int i = 0; i < statePath.Length; i++)
            {
                if (!IsCurrentState(currentMachine, statePath[i]) &&
                    !currentMachine.ChangeState(statePath[i]))
                {
                    return false;
                }

                if (i < statePath.Length - 1)
                {
                    currentMachine = (StateMachine<TStateId, TOwner>)
                        currentMachine.mStates[statePath[i]];
                }
            }

            return true;
        }
        /// <summary>
        /// 添加从指定源状态出发的自动过渡。
        /// </summary>
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

        /// <summary>
        /// 添加当前状态机范围内的任意状态自动过渡。
        /// </summary>
        public void AddAnyTransition(Transition<TStateId, TOwner> transition)
        {
            if (transition == null)
                throw new ArgumentNullException(nameof(transition));

            mAnyTransitions.Add(transition);
            SortTransitions(mAnyTransitions);
        }

        /// <summary>
        /// 激活状态机并进入其默认直接子状态。
        /// </summary>
        public override void OnEnter()
        {
            base.OnEnter();
            if (mHasDefaultState)
                ChangeState(mDefaultStateId);
        }

        /// <summary>
        /// 先处理当前层自动过渡，再向当前激活子状态传递更新。
        /// </summary>
        public override void OnUpdate()
        {
            base.OnUpdate();
            if (!TryAutoTransition())
                CurrentState?.OnUpdate();
        }

        /// <summary>
        /// 向当前激活子状态传递固定帧更新。
        /// </summary>
        public override void OnFixedUpdate()
        {
            base.OnFixedUpdate();
            CurrentState?.OnFixedUpdate();
        }

        /// <summary>
        /// 向当前激活子状态传递延迟帧更新。
        /// </summary>
        public override void OnLateUpdate()
        {
            base.OnLateUpdate();
            CurrentState?.OnLateUpdate();
        }

        /// <summary>
        /// 向当前激活子状态传递动画位移回调。
        /// </summary>
        public override void OnAnimationMove()
        {
            base.OnAnimationMove();
            CurrentState?.OnAnimationMove();
        }

        /// <summary>
        /// 退出当前子状态，并递归结束嵌套状态机的活动状态。
        /// </summary>
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

        // 按当前层优先级检查 AnyTransition 和当前状态的普通 Transition。
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

        // 先验证完整路径和进入条件，再执行任何状态切换，避免无效路径留下半完成层级。
        private bool TryValidateStatePath(IReadOnlyList<TStateId> statePath)
        {
            if (statePath == null || statePath.Count == 0)
                return false;

            StateMachine<TStateId, TOwner> currentMachine = this;
            for (int i = 0; i < statePath.Count; i++)
            {
                if (!currentMachine.mStates.TryGetValue(statePath[i], out var nextState))
                    return false;

                bool isCurrentState = IsCurrentState(currentMachine, statePath[i]);
                if (!isCurrentState && !nextState.CanEnter())
                    return false;

                if (i < statePath.Count - 1)
                {
                    if (!(nextState is StateMachine<TStateId, TOwner> childMachine))
                        return false;

                    currentMachine = childMachine;
                }
            }

            return true;
        }

        // 判断目标是否已经是指定状态机的当前子状态；当前节点可作为路径前缀复用。
        private static bool IsCurrentState(
            StateMachine<TStateId, TOwner> stateMachine,
            TStateId stateId)
        {
            return stateMachine.CurrentState != null &&
                   EqualityComparer<TStateId>.Default.Equals(
                       stateMachine.CurrentState.StateId,
                       stateId);
        }
        // 依次测试过渡条件，并在首个成功过渡后停止本帧检查。
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

        // 按权重从高到低排序，保证同帧过渡优先级稳定。
        private static void SortTransitions(List<Transition<TStateId, TOwner>> transitions)
        {
            transitions.Sort((left, right) => right.WeightOrder.CompareTo(left.WeightOrder));
        }

        /// <summary>
        /// 返回当前状态树的调试文本。
        /// </summary>
        public override string ToString() => ToDebugString();

        /// <summary>
        /// 输出包含当前状态、默认状态和嵌套状态机的调试树。
        /// </summary>
        public string ToDebugString()
        {
            var builder = new StringBuilder();
            builder.Append(StateId);
            builder.Append(BuildDebugTags(false, false, true));
            AppendChildrenDebugString(builder, string.Empty);
            return builder.ToString();
        }

        /// <summary>
        /// 输出当前状态机节点及其子节点的带缩进调试文本。
        /// </summary>
        public override string ToDebugString(string indent, bool isLast, bool isCurrent, bool isDefault)
        {
            var builder = new StringBuilder();
            builder.Append(FormatDebugLine(indent, isLast, StateId, BuildDebugTags(isCurrent, isDefault, true)));
            AppendChildrenDebugString(builder, indent + (isLast ? "   " : "│  "));
            return builder.ToString();
        }

        // 递归追加直接子状态及嵌套状态机的调试信息。
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

        // 统一处理 StateBase 子类和直接实现 IState 的调试文本格式。
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