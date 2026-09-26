using Animancer;
using RPG.Character.Animation;
using WS_Modules.FSM;

namespace RPG.NPC
{
    /// <summary>标识首版 Boss 自己维护的 Idle 与 Move 状态。</summary>
    public enum BossLocomotionStateId
    {
        /// <summary>尚未初始化的状态机。</summary>
        Disabled = -1,
        /// <summary>站立待机。</summary>
        Idle = 0,
        /// <summary>移动表现状态；首版只播放动画，不提交位移。</summary>
        Move = 1
    }

    /// <summary>为 Boss 组装独立的轻量状态机，并将状态入口连接到 Base 动画层。</summary>
    public sealed class BossLocomotionStateMachine
    {
        #region 运行时状态

        private StateMachine<BossLocomotionStateId, NPCController> stateMachine;

        #endregion

        #region 状态查询与初始化

        // 状态查询
        /// <summary>获取当前 Boss 移动状态。</summary>
        public BossLocomotionStateId CurrentState =>
            stateMachine?.CurrentState?.StateId ?? BossLocomotionStateId.Disabled;

        // 状态图初始化与主动切换
        /// <summary>绑定 Boss Owner 并创建默认进入 Idle 的状态图。</summary>
        /// <param name="owner">驱动 ASC 与动画的 NPCController。</param>
        /// <param name="idleTransition">Idle 状态动画。</param>
        /// <param name="moveTransition">Move 状态动画。</param>
        internal void Initialize(NPCController owner, TransitionAsset idleTransition, TransitionAsset moveTransition)
        {
            stateMachine = new StateMachine<BossLocomotionStateId, NPCController>(BossLocomotionStateId.Disabled);
            stateMachine.State(BossLocomotionStateId.Idle)
                .OnEnter(_ => owner.AnimationPlayer.Play(AnimationLayerType.Base, idleTransition));
            stateMachine.State(BossLocomotionStateId.Move)
                .OnEnter(_ => owner.AnimationPlayer.Play(AnimationLayerType.Base, moveTransition));
            stateMachine.SetDefaultState(BossLocomotionStateId.Idle);
            stateMachine.Init(owner, null);
            stateMachine.OnEnter();
        }

        /// <summary>尝试切换到指定 Boss 移动状态。</summary>
        /// <param name="stateId">目标状态。</param>
        /// <returns>目标状态存在且切换成功时返回 true。</returns>
        internal bool TryChangeState(BossLocomotionStateId stateId) =>
            stateMachine != null && stateMachine.ChangeState(stateId);

        #endregion

        #region Unity 阶段转发

        /// <summary>推进状态机的普通帧阶段。</summary>
        internal void Tick() => stateMachine?.OnUpdate();

        /// <summary>推进状态机的物理阶段。</summary>
        internal void FixedTick() => stateMachine?.OnFixedUpdate();

        /// <summary>推进状态机的延迟帧阶段。</summary>
        internal void LateTick() => stateMachine?.OnLateUpdate();

        /// <summary>推进 Animator 求值后的状态机阶段。</summary>
        internal void AnimatorMove() => stateMachine?.OnAnimationMove();

        /// <summary>退出当前状态并清空状态机实例。</summary>
        internal void Deactivate()
        {
            if (stateMachine == null)
                return;
            stateMachine.OnExit();
            stateMachine = null;
        }

        #endregion
    }
}
