using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

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
        /// <summary>获取当前状态树最深处的活动叶状态。</summary>
        public IState<TStateId, TOwner> CurrentLeafState
        {
            get
            {
                if (CurrentState is StateMachine<TStateId, TOwner> childMachine)
                    return childMachine.CurrentLeafState;
                return CurrentState;
            }
        }
        /// <summary>获取从当前状态机节点到活动叶状态的路径快照。</summary>
        public IReadOnlyList<IState<TStateId, TOwner>> CurrentStatePath
        {
            get
            {
                var path = new List<IState<TStateId, TOwner>>();
                AppendCurrentStatePath(path);
                return path;
            }
        }
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

            if (ReferenceEquals(state, this))
                throw new InvalidOperationException("状态机不能把自身注册为子状态。");
            if (state.Machine != null && !ReferenceEquals(state.Machine, this))
                throw new InvalidOperationException("同一个状态实例不能注册到多个父状态机。");

            IStateMachine<TStateId, TOwner> ancestor = this;
            while (ancestor != null)
            {
                if (ReferenceEquals(ancestor, state))
                    throw new InvalidOperationException("状态机不能注册自身或祖先作为子状态。");
                ancestor = ancestor.Machine;
            }

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

            CommitStateChange(nextState, false);
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
        /// <example>
        ///     当前状态机为 A->B->C, A 中还有 A -> D -> E
        ///     需要从  C -> E 则调用 A.ChangeStatePath(D, E) 就会进行切换，先退出 C、B，然后进入 D、E。
        /// </example>
        /// <returns>路径完整有效并完成切换时返回 true。</returns>
        public bool ChangeStatePath(params TStateId[] statePath)
        {
            if (!TryCollectStatePath(statePath, out List<StateMachine<TStateId, TOwner>> machines,
                    out List<IState<TStateId, TOwner>> targets))
                return false;

            // 计算目标路径与当前活动路径的最长公共前缀长度，复用公共前缀节点。
            int commonLength = FindCommonPathLength(machines, targets);
            // 相同说明目标路径与当前活动路径完全一致，无需切换。
            if (commonLength == statePath.Length)
                return false;

            // 先完整预检，任何目标拒绝都不会退出当前活动路径。
            for (int index = commonLength; index < targets.Count; index++)
                if (!targets[index].CanEnter())
                    return false;

            // 从叶节点向公共前缀退出，再逐级提交目标路径。
            for (int index = statePath.Length - 1; index >= commonLength; index--)
                machines[index].ExitCurrentState();

            for (int index = commonLength; index < targets.Count; index++)
            {
                StateMachine<TStateId, TOwner> machine = machines[index];
                machine.CommitStateChange(targets[index], index < targets.Count - 1);
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
        public override void OnEnter(bool suppressDefaultState = false)
        {
            base.OnEnter();
            if (!suppressDefaultState && mHasDefaultState)
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

        /// <summary>按当前层优先级检查 AnyTransition 和当前状态的普通 Transition。</summary>
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

        /// <summary>收集目标路径并验证每一段都是当前节点的直接子状态。</summary>
        /// <param name="statePath">从当前状态机开始的直接子状态路径。</param>
        /// <param name="machines">输出每个路径节点所属的状态机。</param>
        /// <param name="targets">输出路径上解析到的目标状态。</param>
        /// <returns>路径完整且所有中间节点都是状态机时返回 true。</returns>
        private bool TryCollectStatePath(
            IReadOnlyList<TStateId> statePath,
            out List<StateMachine<TStateId, TOwner>> machines,
            out List<IState<TStateId, TOwner>> targets)
        {
            machines = new List<StateMachine<TStateId, TOwner>>();
            targets = new List<IState<TStateId, TOwner>>();
            if (statePath == null || statePath.Count == 0)
                return false;

            StateMachine<TStateId, TOwner> currentMachine = this;
            for (int i = 0; i < statePath.Count; i++)
            {
                if (!currentMachine.mStates.TryGetValue(statePath[i], out var nextState))
                    return false;

                machines.Add(currentMachine);
                targets.Add(nextState);

                // i < statePath.Count - 1 表示不是路径的最后一个节点，则必须是状态机才能继续向下解析。
                if (i < statePath.Count - 1)
                {
                    // 如果下一个状态不是状态机，则无法继续向下解析。
                    if (!(nextState is StateMachine<TStateId, TOwner> childMachine))
                        return false;

                    currentMachine = childMachine;
                }
            }

            return true;
        }

        /// <summary>计算目标路径与当前活动路径的最长公共前缀长度。</summary>
        /// <param name="machines">目标路径上各节点所属的状态机。</param>
        /// <param name="targets">解析后的目标状态。</param>
        /// <returns>可以复用的路径节点数量。</returns>
        /// <example>比如：如果目标路径是 [A, B, C]，当前路径是 [A, B, D]，则最长公共前缀长度为 2。</example>
        private int FindCommonPathLength(
            IReadOnlyList<StateMachine<TStateId, TOwner>> machines,
            IReadOnlyList<IState<TStateId, TOwner>> targets)
        {
            int commonLength = 0;
            for (; commonLength < targets.Count; commonLength++)
            {
                if (!IsCurrentState(machines[commonLength], targets[commonLength].StateId))
                    break;
            }
            return commonLength;
        }

        /// <summary>在路径预检通过后提交一个状态节点，不重复执行 CanEnter。</summary>
        /// <param name="nextState">要进入的直接子状态。</param>
        /// <param name="suppressDefaultState">状态机节点是否只激活自身而跳过默认子状态。</param>
        private void CommitStateChange(IState<TStateId, TOwner> nextState, bool suppressDefaultState)
        {
            ExitCurrentState();
            CurrentState = nextState;
            CurrentState.OnEnter(suppressDefaultState);
        }

        /// <summary>退出当前直接子状态；父状态机节点本身保持活动。</summary>
        private void ExitCurrentState()
        {
            if (CurrentState == null)
                return;
            CurrentState.OnExit();
            PreviousState = CurrentState;
            CurrentState = null;
        }

        /// <summary>判断指定状态是否已经是状态机当前子状态，以便复用路径前缀。</summary>
        /// <param name="stateMachine">需要检查的状态机。</param>
        /// <param name="stateId">目标状态标识。</param>
        /// <returns>当前子状态标识相同时返回 true。</returns>
        private static bool IsCurrentState(
            StateMachine<TStateId, TOwner> stateMachine,
            TStateId stateId)
        {
            return stateMachine.CurrentState != null &&
                   EqualityComparer<TStateId>.Default.Equals(
                       stateMachine.CurrentState.StateId,
                       stateId);
        }

        /// <summary>把当前节点和递归子状态追加到路径快照。</summary>
        /// <param name="path">接收路径节点的列表。</param>
        private void AppendCurrentStatePath(ICollection<IState<TStateId, TOwner>> path)
        {
            path.Add(this);
            if (CurrentState == null)
                return;

            path.Add(CurrentState);
            if (CurrentState is StateMachine<TStateId, TOwner> childMachine)
                childMachine.AppendCurrentStatePathWithoutSelf(path);
        }

        /// <summary>追加嵌套状态机的当前子状态而不重复添加状态机节点。</summary>
        /// <param name="path">接收路径节点的列表。</param>
        private void AppendCurrentStatePathWithoutSelf(ICollection<IState<TStateId, TOwner>> path)
        {
            if (CurrentState == null)
                return;

            path.Add(CurrentState);
            if (CurrentState is StateMachine<TStateId, TOwner> childMachine)
                childMachine.AppendCurrentStatePathWithoutSelf(path);
        }
        /// <summary>按优先级依次测试自动过渡，并在首个成功过渡后停止。</summary>
        /// <param name="transitions">当前层待检查的过渡集合。</param>
        /// <returns>本次检查完成状态切换时返回 true。</returns>
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

        /// <summary>按权重从高到低排序，保证同帧过渡优先级稳定。</summary>
        /// <param name="transitions">待排序的过渡列表。</param>
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

        /// <summary>递归追加直接子状态及嵌套状态机的调试信息。</summary>
        /// <param name="builder">接收调试文本的构建器。</param>
        /// <param name="childIndent">子节点缩进文本。</param>
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

        /// <summary>统一处理 StateBase 子类和直接实现 IState 的调试文本格式。</summary>
        /// <param name="state">需要输出的子状态。</param>
        /// <param name="indent">当前节点缩进。</param>
        /// <param name="isLast">是否为同级最后一个节点。</param>
        /// <param name="isCurrent">是否为当前活动节点。</param>
        /// <param name="isDefault">是否为默认节点。</param>
        /// <returns>格式化后的单节点调试文本。</returns>
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
