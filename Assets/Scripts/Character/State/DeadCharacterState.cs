using Animancer;
using RPG.Character;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.FSM;

namespace RPG.Character.State
{
    /// <summary>管理死亡 Runtime 清理、垂直重力、死亡动画和复活退出。</summary>
    internal sealed class DeadCharacterState : CustomState<E_CharacterLifeStateId, CharacterLifeStateMachine>
    {
        #region 运行时资源

        private float verticalSpeed;
        private MotionControlHandle gravityHandle;
        private AnimancerState deathAnimationState;
        private bool deathAnimationCompletionRaised;

        #endregion

        #region 依赖字段

        private readonly CharacterLifeStateMachine lifeStateMachine;

        #endregion

        #region 生命周期

        /// <summary>创建 Dead 状态并绑定死亡开始、退出和 Fixed 阶段回调。</summary>
        /// <param name="owner">拥有该状态的生命状态机。</param>
        internal DeadCharacterState(CharacterLifeStateMachine owner)
            : base(E_CharacterLifeStateId.Dead)
        {
            lifeStateMachine = owner;
            OnEnter(_ => HandleStateEntered());
            OnExit(_ => HandleStateExited());
            OnFixedUpdate(_ => lifeStateMachine.AdvanceDeadFixed());
        }

        #endregion

        #region Actor 控制生命周期

        /// <summary>Active 死亡角色重新进入画面时恢复死亡重力和动画表现。</summary>
        internal void OnActorEntered()
        {
            deathAnimationCompletionRaised = false;
            BeginActiveDeathPresentation();
            PlayDeathAnimation();
        }

        /// <summary>死亡角色退场时解除动画回调并释放其垂直运动句柄。</summary>
        internal void OnActorExited()
        {
            ClearDeathAnimationCallback();
            ReleaseGravityHandle();
        }

        /// <summary>从 Alive 状态取得死亡切换前的垂直速度，作为后续重力积分初值。</summary>
        /// <param name="sourceVerticalSpeed">Locomotion 最近一次结算的垂直速度。</param>
        internal void SetEntryVerticalSpeed(float sourceVerticalSpeed) => verticalSpeed = sourceVerticalSpeed;

        /// <summary>在活动死亡状态中仅提交垂直向的固定步重力位移。</summary>
        /// <param name="deltaTime">当前 Fixed 时间步长。</param>
        internal void AdvanceFixed(float deltaTime)
        {
            if (!lifeStateMachine.Actor.IsActiveCharacter || gravityHandle == null || !gravityHandle.IsValid)
                return;

            verticalSpeed = lifeStateMachine.PlayerController.IsCharacterGrounded
                ? 0f
                : Mathf.Max(-53f, verticalSpeed - lifeStateMachine.Actor.Config.Gravity * deltaTime);
            lifeStateMachine.Actor.MotionDriver.SubmitFixed(
                gravityHandle,
                FixedMotionRequest.TranslationOnly(Vector3.up * (verticalSpeed * deltaTime)));
        }

        #endregion

        #region 状态进入与退出

        /// <summary>清理全部 GAS Runtime、重建 State.Dead，并在活动角色上启动死亡表现。</summary>
        private void HandleStateEntered()
        {
            deathAnimationCompletionRaised = false;
            lifeStateMachine.Actor.NotifyLifeStateChanged(true);
            if (lifeStateMachine.Actor.IsActiveCharacter)
                BeginActiveDeathPresentation();

            // ClearRuntime 会清除所有 Runtime Tag，因此只在全部 GAS 控制器清理后重新写入 Dead 标记。
            lifeStateMachine.Actor.AbilitySystemComponent.ClearRuntime();
            if (!lifeStateMachine.Actor.AbilitySystemComponent.AddLooseGameplayTag(
                    WS_Modules.GAS.Generated.GameplayTags.Tag_State_Dead))
                throw new System.InvalidOperationException(
                    $"角色 {lifeStateMachine.Actor.CharacterId} 清理 Runtime 后无法写入 State.Dead。");

            if (lifeStateMachine.Actor.IsActiveCharacter)
                PlayDeathAnimation();
            Debug.Log(
                $"[DeadCharacterState] 角色进入死亡状态并清理 Runtime，character={lifeStateMachine.Actor.CharacterId}, active={lifeStateMachine.Actor.IsActiveCharacter}。",
                lifeStateMachine.Actor);
        }

        /// <summary>退出时释放死亡资源并移除 State.Dead，存活通知由 Alive 状态进入时发送。</summary>
        private void HandleStateExited()
        {
            ClearDeathAnimationCallback();
            ReleaseGravityHandle();

            if (lifeStateMachine.IsDisposing)
                return;

            if (!lifeStateMachine.Actor.AbilitySystemComponent.RemoveLooseGameplayTag(
                    WS_Modules.GAS.Generated.GameplayTags.Tag_State_Dead))
                throw new System.InvalidOperationException(
                    $"角色 {lifeStateMachine.Actor.CharacterId} 的 State.Dead Tag 未能对称移除。");

            // Action 层可能在 Actor 退到后台后仍保留死亡动画；复活时无论当前槽位都清除该层。
            lifeStateMachine.Actor.LifeAnimationController.StopLayer(AnimationLayerType.Action);
            Debug.Log(
                $"[DeadCharacterState] 已清理角色死亡状态表现，等待 Alive 状态发布存活通知，character={lifeStateMachine.Actor.CharacterId}, active={lifeStateMachine.Actor.IsActiveCharacter}。",
                lifeStateMachine.Actor);
        }

        /// <summary>建立只控制垂直通道的死亡重力句柄，不覆盖角色水平朝向或位移。</summary>
        private void BeginActiveDeathPresentation()
        {
            if (!lifeStateMachine.Actor.IsActiveCharacter)
                return;

            ReleaseGravityHandle();
            lifeStateMachine.PlayerController.ReleaseCharacterControl(lifeStateMachine.Actor);
            gravityHandle = lifeStateMachine.Actor.MotionDriver.RequestControl(new MotionControlRequest(
                lifeStateMachine.Actor, MotionPriority.Gravity, MotionChannels.Vertical));
        }

        /// <summary>播放配置的非循环死亡动画并注册自然结束通知。</summary>
        private void PlayDeathAnimation()
        {
            if (!lifeStateMachine.Actor.IsActiveCharacter || deathAnimationCompletionRaised || deathAnimationState != null)
                return;

            deathAnimationState = lifeStateMachine.Actor.LifeAnimationController.Play(
                AnimationLayerType.Action,
                lifeStateMachine.Actor.Config.LocomotionTransition.DeathTransition);
            deathAnimationState.Events(this).OnEnd += HandleDeathAnimationEnded;
            Debug.Log($"[DeadCharacterState] 开始播放死亡动画，character={lifeStateMachine.Actor.CharacterId}。",
                lifeStateMachine.Actor);
        }

        /// <summary>死亡动画自然结束后通知 CharacterManager 在安全阶段处理队伍切换。</summary>
        private void HandleDeathAnimationEnded()
        {
            ClearDeathAnimationCallback();
            if (!lifeStateMachine.IsDead || !lifeStateMachine.Actor.IsActiveCharacter || deathAnimationCompletionRaised)
                return;

            // 先移除已经播放完毕的 Action 状态，再通知 Manager 切人，避免结束帧残留死亡覆盖层。
            lifeStateMachine.Actor.LifeAnimationController.StopLayer(AnimationLayerType.Action);
            deathAnimationCompletionRaised = true;
            lifeStateMachine.Actor.NotifyDeathAnimationCompleted();
            Debug.Log($"[DeadCharacterState] 死亡动画播放完成并停止 Action 层，character={lifeStateMachine.Actor.CharacterId}。",
                lifeStateMachine.Actor);
        }

        /// <summary>解除 Animancer 状态对当前死亡状态实例的引用。</summary>
        private void ClearDeathAnimationCallback()
        {
            if (deathAnimationState == null)
                return;

            deathAnimationState.Events(this).OnEnd -= HandleDeathAnimationEnded;
            deathAnimationState = null;
        }

        /// <summary>释放死亡状态拥有的 MotionDriver 句柄，避免复活或退场后继续提交重力。</summary>
        private void ReleaseGravityHandle()
        {
            if (gravityHandle == null)
                return;

            gravityHandle.Dispose();
            gravityHandle = null;
        }

        #endregion
    }
}
