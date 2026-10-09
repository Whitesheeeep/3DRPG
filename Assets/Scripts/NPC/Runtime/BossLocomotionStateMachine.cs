using Animancer;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.NPC
{
    /// <summary>标识 NPC 分层状态机的根状态与 Alive 子状态。</summary>
    public enum E_NPCStateId
    {
        /// <summary>尚未初始化的状态机。</summary>
        Disabled = -1,
        /// <summary>站立待机。</summary>
        Idle = 0,
        /// <summary>移动表现状态；首版只播放动画，不提交位移。</summary>
        Move = 1,
        /// <summary>NPC 可行动的根状态。</summary>
        Alive = 2,
        /// <summary>NPC 不可逆的死亡终止状态。</summary>
        Dead = 3
    }

    /// <summary>为 NPC 组装 Alive/Dead 根状态与 Idle/Move 子状态，并连接 Base 动画层。</summary>
    public sealed class BossLocomotionStateMachine
    {
        #region 运行时状态

        private StateMachine<E_NPCStateId, NPCController> rootStateMachine;

        #endregion

        #region 状态查询与初始化

        // 状态查询
        /// <summary>获取当前 NPC 状态机叶节点。</summary>
        public E_NPCStateId CurrentState =>
            rootStateMachine?.CurrentLeafState?.StateId ?? E_NPCStateId.Disabled;

        // 状态图初始化与主动切换
        /// <summary>绑定 NPC Owner 并创建默认进入 Alive/Idle 的分层状态图。</summary>
        /// <param name="owner">驱动 ASC 与动画的 NPCController。</param>
        /// <param name="idleTransition">Idle 状态动画。</param>
        /// <param name="moveTransition">Move 状态动画。</param>
        /// <param name="deadState">负责死亡动画及动画结束清理的终止状态。</param>
        internal void Initialize(
            NPCController owner,
            TransitionAsset idleTransition,
            TransitionAsset moveTransition,
            NPCDeadState deadState)
        {
            var aliveStateMachine = new PauseAwareAliveStateMachine();
            aliveStateMachine.State(E_NPCStateId.Idle)
                .OnEnter(_ => owner.AnimationPlayer.Play(AnimationLayerType.Base, idleTransition));
            aliveStateMachine.State(E_NPCStateId.Move)
                .OnEnter(_ => owner.AnimationPlayer.Play(AnimationLayerType.Base, moveTransition));
            aliveStateMachine.SetDefaultState(E_NPCStateId.Idle);

            rootStateMachine = new StateMachine<E_NPCStateId, NPCController>(E_NPCStateId.Disabled);
            rootStateMachine.AddState(aliveStateMachine);
            rootStateMachine.AddState(deadState);
            rootStateMachine.SetDefaultState(E_NPCStateId.Alive);
            rootStateMachine.AddTransition(
                new Transition<E_NPCStateId, NPCController>(E_NPCStateId.Alive, E_NPCStateId.Dead)
                    .AddCondition(stateOwner => stateOwner.IsDead));
            rootStateMachine.Init(owner, null);
            rootStateMachine.OnEnter();
            Debug.Log($"[BossLocomotionStateMachine] NPC '{owner.name}' 根状态机进入 Alive。");
        }

        /// <summary>尝试在 Alive 子状态机中切换 Idle 或 Move。</summary>
        /// <param name="stateId">目标状态。</param>
        /// <returns>目标状态存在且切换成功时返回 true。</returns>
        internal bool TryChangeState(E_NPCStateId stateId) =>
            rootStateMachine != null &&
            !rootStateMachine.Owner.IsDead &&
            rootStateMachine.ChangeStatePath(E_NPCStateId.Alive, stateId);

        #endregion

        #region Unity 阶段转发

        /// <summary>推进状态机的普通帧阶段。</summary>
        internal void Tick() => rootStateMachine?.OnUpdate();

        /// <summary>推进状态机的物理阶段。</summary>
        internal void FixedTick() => rootStateMachine?.OnFixedUpdate();

        /// <summary>推进状态机的延迟帧阶段。</summary>
        internal void LateTick() => rootStateMachine?.OnLateUpdate();

        /// <summary>推进 Animator 求值后的状态机阶段。</summary>
        internal void AnimatorMove() => rootStateMachine?.OnAnimationMove();

        /// <summary>退出当前状态并清空状态机实例。</summary>
        internal void Deactivate()
        {
            if (rootStateMachine == null)
                return;
            rootStateMachine.OnExit();
            rootStateMachine = null;
        }

        #endregion

        #region Alive 子状态暂停策略

        /// <summary>HitStop 期间暂停 Alive 内部状态更新，同时让父状态机继续检查死亡 Transition。</summary>
        private sealed class PauseAwareAliveStateMachine : StateMachine<E_NPCStateId, NPCController>
        {
            /// <summary>创建身份为 Alive 的 NPC 子状态机。</summary>
            public PauseAwareAliveStateMachine() : base(E_NPCStateId.Alive)
            {
            }

            /// <summary>Owner HitStop 时不推进 Idle/Move 子状态；根状态机的死亡检查不受影响。</summary>
            public override void OnUpdate()
            {
                if (Owner.Actor.IsActionPaused)
                    return;

                base.OnUpdate();
            }
        }

        #endregion
    }
}
