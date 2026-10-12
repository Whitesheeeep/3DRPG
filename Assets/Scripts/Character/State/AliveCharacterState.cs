using RPG.Character;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.State
{
    /// <summary>管理存活角色的战斗阶段，并持有其 Locomotion 子状态机。</summary>
    internal sealed class AliveCharacterState : CustomState<E_CharacterLifeStateId, CharacterLifeStateMachine>
    {
        #region 依赖字段

        private readonly CharacterLifeStateMachine lifeStateMachine;
        private readonly CharacterLocomotionStateMachine locomotion = new();

        #endregion

        #region 查询属性

        /// <summary>获取 Alive 状态持有的 Locomotion 子状态机。</summary>
        internal CharacterLocomotionStateMachine Locomotion => locomotion;

        #endregion

        #region 生命周期

        /// <summary>创建 Alive 状态及其 Locomotion 子状态机，并绑定各阶段转发。</summary>
        /// <param name="owner">拥有该状态的生命状态机。</param>
        internal AliveCharacterState(CharacterLifeStateMachine owner)
            : base(E_CharacterLifeStateId.Alive)
        {
            lifeStateMachine = owner;
            CharacterActor actor = owner.Actor;
            locomotion.Configure(actor.Config.Gravity, actor.Config.LocomotionTransition);
            locomotion.Initialize(actor, actor.MotionDriver);

            OnEnter(_ => HandleStateEntered());
            OnExit(_ => HandleStateExited());
            OnUpdate(_ => lifeStateMachine.AdvanceAliveUpdate());
            OnFixedUpdate(_ => lifeStateMachine.AdvanceAliveFixed());
            OnLateUpdate(_ => lifeStateMachine.AdvanceAliveLate());
        }

        #endregion

        #region Player 控制生命周期

        /// <summary>Active Actor 进入操控后按 Player 门禁启动或保持 Locomotion。</summary>
        internal void OnActorEntered() => ApplyControlPermission();

        /// <summary>Active Actor 退出操控时释放 Locomotion 的状态和重力请求。</summary>
        internal void OnActorExited() => locomotion.Deactivate();

        /// <summary>Player 控制门禁改变时同步 Alive 子状态的激活状态。</summary>
        internal void OnControlPermissionChanged() => ApplyControlPermission();

        /// <summary>将 Animator 求值阶段按 ASC 后 Locomotion 的顺序提交。</summary>
        /// <param name="deltaPosition">Animator 根位移。</param>
        /// <param name="deltaRotation">Animator 根旋转。</param>
        /// <param name="evaluationDeltaTime">Animator 求值时间。</param>
        /// <returns>该阶段由活动 Locomotion 成功处理时返回 true。</returns>
        internal bool UpdateAnimationMove(Vector3 deltaPosition, Quaternion deltaRotation, float evaluationDeltaTime)
        {
            if (!lifeStateMachine.IsActorControlEntered || !lifeStateMachine.IsControlAllowed ||
                lifeStateMachine.Actor.IsActionPaused)
                return false;

            lifeStateMachine.Actor.UpdateAnimationMoveAbility(deltaPosition, deltaRotation);
            locomotion.UpdateAnimationMove(deltaPosition, deltaRotation, evaluationDeltaTime);
            return true;
        }

        #endregion

        #region 状态进入与退出

        /// <summary>状态进入后根据上场状态启动 Locomotion；初始化时后台角色仍保持静止。</summary>
        private void HandleStateEntered()
        {
            ApplyControlPermission();
            if (lifeStateMachine.Actor.IsActiveCharacter && lifeStateMachine.IsControlAllowed)
                Debug.Log($"[AliveCharacterState] 存活状态进入并启用 Locomotion，character={lifeStateMachine.Actor.CharacterId}。",
                    lifeStateMachine.Actor);
        }

        /// <summary>死亡转场前保存垂直速度，再退出 Locomotion 并释放重力请求。</summary>
        private void HandleStateExited()
        {
            lifeStateMachine.RecordVerticalSpeedBeforeDeath(locomotion.VerticalSpeed);
            locomotion.Deactivate();
            Debug.Log($"[AliveCharacterState] 已退出 Alive 状态并释放 Locomotion，character={lifeStateMachine.Actor.CharacterId}。",
                lifeStateMachine.Actor);
        }

        /// <summary>把生命 FSM 的 Player 控制门禁映射为 Locomotion Activate/Deactivate。</summary>
        private void ApplyControlPermission()
        {
            if (!lifeStateMachine.IsActorControlEntered || !lifeStateMachine.Actor.IsActiveCharacter ||
                !lifeStateMachine.IsControlAllowed)
            {
                locomotion.Deactivate();
                return;
            }

            locomotion.Activate();
        }

        #endregion
    }
}
