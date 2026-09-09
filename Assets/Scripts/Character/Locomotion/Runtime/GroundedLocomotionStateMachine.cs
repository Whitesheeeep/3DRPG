using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character
{
    /// <summary>组合 Grounded 下的 Idle、Walk、Run 以及起停叶状态。</summary>
    public sealed class GroundedLocomotionStateMachine : StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>
    {
        private readonly GroundMoveRuntime groundMoveRuntime = new();
        /// <summary>创建 Grounded 子状态机并注册全部直接子状态。</summary>
        public GroundedLocomotionStateMachine() : base(CharacterLocomotionStateId.Grounded)
        {
            AddState(new IdleLocomotionState());
            AddState(new RootMotionWalkStartState());
            AddState(new RootMotionRunStartState());
            AddState(new WalkLocomotionState(groundMoveRuntime));
            AddState(new RunLocomotionState(groundMoveRuntime));
            AddState(new RootMotionStopState());
            SetDefaultState(CharacterLocomotionStateId.Idle);

            // Grounded 内部的稳定条件使用 Transition 组合；动画完成、起步中断等一次性流程仍由叶状态自己决定。
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Idle,
                    CharacterLocomotionStateId.RootMotionRunStart,
                    200)
                .AddCondition(owner => owner.Blackboard.HasMovement && owner.Blackboard.IsSprintHeld));
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Idle,
                    CharacterLocomotionStateId.RootMotionWalkStart,
                    100)
                .AddCondition(owner => owner.Blackboard.HasMovement && !owner.Blackboard.IsSprintHeld));
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Walk,
                    CharacterLocomotionStateId.Run,
                    100)
                .AddCondition(owner => owner.Blackboard.IsSprintHeld && owner.Blackboard.HasMovement));
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Walk,
                    CharacterLocomotionStateId.RootMotionStop,
                    300)
                .AddCondition(owner => !owner.Blackboard.HasMovement));
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Run,
                    CharacterLocomotionStateId.RootMotionStop,
                    300)
                .AddCondition(owner => !owner.Blackboard.HasMovement));
            AddTransition(new Transition<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                    CharacterLocomotionStateId.Run,
                    CharacterLocomotionStateId.Walk,
                    200)
                .AddCondition(owner => !owner.Blackboard.IsSprintHeld && owner.Blackboard.HasMovement));
        }

        /// <summary>
        /// 离开 Grounded 分支时清理 Walk/Run 共用的连续移动运行时，避免落地或重新激活时误判为档位续接。
        /// </summary>
        public override void OnExit()
        {
            groundMoveRuntime.ResetForActivation();
            base.OnExit();
        }
    }
}
