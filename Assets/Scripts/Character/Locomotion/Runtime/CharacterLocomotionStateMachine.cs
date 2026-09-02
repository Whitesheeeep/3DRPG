using System;
using Animancer;
using RPG.Character.Animation;
using UnityEngine;
using WS_Modules.FSM;
using WS_Modules.GAS.Generated;

namespace RPG.Character
{
    /// <summary>角色 Locomotion UnifiedFSM 的稳定状态标识。</summary>
    public enum CharacterLocomotionStateId
    {
        /// <summary>未启用状态。</summary>
        Disable,
        /// <summary>无水平位移的待机状态。</summary>
        Idle,
        /// <summary>按 GAS Speed 进行代码移动。</summary>
        CodeLocomotion,
        /// <summary>使用起步根运动进入普通移动。</summary>
        RootMotionStart,
        /// <summary>使用停止根运动结束移动。</summary>
        RootMotionStop,
        /// <summary>使用急转向根运动修正大角度方向。</summary>
        RootMotionSharpTurn
    }

    /// <summary>封装一个 CharacterActor 的 UnifiedFSM，并把业务运动提交到共享 MotionDriver。</summary>
    [Serializable]
    public sealed class CharacterLocomotionStateMachine
    {
        #region 配置与运行时状态
        [SerializeField] private PlayerFSMTransition transition;
        [SerializeField, Min(0f)] private float gravity = 9.81f;
        // PlayerController 注入的角色与统一运动请求出口。
        private StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine> stateMachine;
        private CharacterActor owner;
        private IMotionDriver driver;
        private Vector3 movementInput;
        private float verticalSpeed;
        private float fixedDeltaTime;
        private float rootSampleDistance;
        private float rootSampleTime;
        private float moveReferenceSpeed;
        private float moveParameterXValue;
        private float moveParameterRotationValue;
        private float sharpTurnCooldownRemaining;
        private bool active;
        private MotionControlHandle gravityHandle;
        #endregion

        #region 查询
        /// <summary>获取状态机所属角色。</summary>
        internal CharacterActor Owner => owner;
        /// <summary>获取统一运动请求接口。</summary>
        internal IMotionDriver Driver => driver;
        /// <summary>获取当前配置。</summary>
        internal PlayerFSMTransition Transition => transition;
        /// <summary>获取当前 FSM 状态。</summary>
        public CharacterLocomotionStateId CurrentState => stateMachine?.CurrentState?.StateId ?? CharacterLocomotionStateId.Disable;
        /// <summary>获取本帧是否存在世界空间移动输入。</summary>
        internal bool HasMovementInput => movementInput.sqrMagnitude > 0.0001f;
        /// <summary>获取本物理步时长。</summary>
        internal float FixedDeltaTime => fixedDeltaTime;
        /// <summary>获取当前角色 GAS Speed。</summary>
        internal float TargetSpeed
        {
            get
            {
                if (!owner.AbilitySystemComponent.TryGetCurrentValue(GameplayAttributes.Attribute_Speed, out float value))
                    throw new InvalidOperationException($"角色 '{owner.name}' 未初始化 GameplayAttributes.Speed。");
                return Mathf.Max(0f, value);
            }
        }
        /// <summary>获取进入 Move 时沿用的速度。</summary>
        internal float EntrySpeed { get; private set; }
        /// <summary>获取当前 Move Loop 所使用的根运动参考速度。</summary>
        internal float MoveReferenceSpeed => moveReferenceSpeed;
        /// <summary>获取起步方向修正是否到达配置窗口。</summary>
        internal bool ShouldCorrectStartRotation
        {
            // 起步从第一帧就使用独立的快速修正速度，避免角色先沿旧朝向走一段再转向。
            get => HasMovementInput;
        }
        /// <summary>获取急转向方向修正是否到达配置窗口。</summary>
        internal bool ShouldCorrectSharpTurnRotation => ShouldCorrectStartRotation;
        /// <summary>获取是否配置起步根运动。</summary>
        internal bool HasStartAnimation => transition?.ForwardStart != null;
        /// <summary>获取是否配置停止根运动；未配置时松开输入直接回到 Idle。</summary>
        internal bool HasStopAnimation => transition?.StopLeft != null || transition?.StopRight != null;
        /// <summary>获取是否配置急转向根运动；未配置时继续使用普通平滑转向。</summary>
        internal bool HasSharpTurnAnimation => transition?.SharpTurnLeft != null || transition?.SharpTurnRight != null;

        /// <summary>清零一次起步根运动速度采样窗口。</summary>
        internal void BeginRootSpeedSample()
        {
            rootSampleDistance = 0f;
            rootSampleTime = 0f;
        }
        /// <summary>获取当前输入是否达到急转向角度。</summary>
        internal bool ShouldEnterSharpTurn
        {
            get
            {
                if (!HasMovementInput || transition == null) return false;
                return sharpTurnCooldownRemaining <= 0f &&
                    Mathf.Abs(Vector3.SignedAngle(owner.RootTransform.forward, movementInput, Vector3.up)) >=
                    transition.SharpTurnAngle;
            }
        }
        #endregion

        #region 生命周期与阶段驱动
        /// <summary>绑定角色、MotionDriver 和正式 UnifiedFSM 状态树。</summary>
        /// <param name="sourceOwner">角色包装对象。</param>
        /// <param name="sourceDriver">PlayerController 持有的运动接口。</param>
        internal void Initialize(CharacterActor sourceOwner, IMotionDriver sourceDriver)
        {
            owner = sourceOwner ?? throw new ArgumentNullException(nameof(sourceOwner));
            driver = sourceDriver ?? throw new ArgumentNullException(nameof(sourceDriver));
            if (transition == null)
                throw new InvalidOperationException($"角色 '{owner.name}' 未配置 PlayerFSMTransition。 ");
            stateMachine = new StateMachine<CharacterLocomotionStateId, CharacterLocomotionStateMachine>(
                CharacterLocomotionStateId.Disable);
            stateMachine.AddState(new IdleLocomotionState());
            stateMachine.AddState(new RootMotionStartState());
            stateMachine.AddState(new CodeLocomotionState());
            stateMachine.AddState(new RootMotionStopState());
            stateMachine.AddState(new RootMotionSharpTurnState());
            stateMachine.SetDefaultState(CharacterLocomotionStateId.Idle);
            stateMachine.Init(this, null);
        }

        /// <summary>启用 FSM 并进入 Idle。</summary>
        internal void Activate()
        {
            if (active) return;
            active = true;
            rootSampleDistance = 0f;
            rootSampleTime = 0f;
            moveReferenceSpeed = 0f;
            moveParameterXValue = 0f;
            moveParameterRotationValue = 0f;
            sharpTurnCooldownRemaining = 0f;
            gravityHandle = driver.RequestControl(new MotionControlRequest(owner, MotionPriority.Gravity,
                MotionChannels.Vertical, false));
            stateMachine.OnEnter();
        }

        /// <summary>停用 FSM 并释放当前状态请求。</summary>
        internal void Deactivate()
        {
            if (!active) return;
            active = false;
            stateMachine.OnExit();
            gravityHandle?.Dispose();
            gravityHandle = null;
            movementInput = Vector3.zero;
            verticalSpeed = 0f;
            EntrySpeed = 0f;
            rootSampleDistance = 0f;
            rootSampleTime = 0f;
            moveReferenceSpeed = 0f;
            moveParameterXValue = 0f;
            moveParameterRotationValue = 0f;
            sharpTurnCooldownRemaining = 0f;
        }

        /// <summary>主动切换正式 UnifiedFSM 状态。</summary>
        /// <param name="stateId">目标状态标识。</param>
        public bool ChangeState(CharacterLocomotionStateId stateId)
        {
            if (!active && stateId != CharacterLocomotionStateId.Disable) return false;
            if (stateId == CharacterLocomotionStateId.Disable)
            {
                Deactivate();
                return true;
            }
            if (stateId == CharacterLocomotionStateId.CodeLocomotion)
            {
                float measured = rootSampleTime > 0f ? rootSampleDistance / rootSampleTime : 0f;
                moveReferenceSpeed = transition != null && transition.UseMeasuredRootReferenceSpeed && measured > 0f
                    ? measured : Mathf.Max(0.01f, transition?.RootReferenceSpeed ?? TargetSpeed);
                EntrySpeed = transition != null && transition.UseMeasuredRootReferenceSpeed && measured > 0f
                    ? measured : Mathf.Min(TargetSpeed, moveReferenceSpeed);
            }
            if (stateId == CharacterLocomotionStateId.RootMotionSharpTurn)
                sharpTurnCooldownRemaining = transition?.SharpTurnCooldown ?? 0f;
            return stateMachine.ChangeState(stateId);
        }

        /// <summary>保存输入仲裁后的世界水平移动方向。</summary>
        /// <param name="input">世界 X/Z 平面方向与强度。</param>
        internal void SetMovementInput(Vector3 input) => movementInput = Vector3.ClampMagnitude(
            Vector3.ProjectOnPlane(input, Vector3.up), 1f);

        /// <summary>推进普通帧，状态内部决定动画和状态切换。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void Tick(float deltaTime)
        {
            if (!active) return;
            sharpTurnCooldownRemaining = Mathf.Max(0f, sharpTurnCooldownRemaining - Mathf.Max(0f, deltaTime));
            stateMachine.OnUpdate();
        }

        /// <summary>推进物理阶段并提交重力及当前状态运动。</summary>
        /// <param name="deltaTime">本物理步时长。</param>
        internal void FixedTick(float deltaTime)
        {
            if (!active) return;
            fixedDeltaTime = deltaTime;
            verticalSpeed = driver.IsGrounded && verticalSpeed < 0f ? -2f : verticalSpeed - gravity * deltaTime;
            stateMachine.OnFixedUpdate();
            // 重力始终是世界 Y，不参与水平输入和角色朝向转换。
            driver.SubmitFixed(gravityHandle, FixedMotionRequest.TranslationOnly(Vector3.up * (verticalSpeed * deltaTime)));
        }

        /// <summary>推进当前 Locomotion 的延迟阶段，不在该阶段产生 CharacterController 移动。</summary>
        /// <param name="deltaTime">本帧缩放时间。</param>
        internal void LateTick(float deltaTime)
        {
            if (!active) return;
            stateMachine.OnLateUpdate();
        }

        /// <summary>转发当前 Animator 求值阶段。</summary>
        internal void UpdateAnimationMove(Vector3 deltaPosition, float evaluationDeltaTime)
        {
            if (!active) return;
            if (CurrentState == CharacterLocomotionStateId.RootMotionStart && evaluationDeltaTime > 0f)
            {
                rootSampleDistance += Vector3.ProjectOnPlane(deltaPosition, Vector3.up).magnitude;
                rootSampleTime += evaluationDeltaTime;
            }
            stateMachine.OnAnimationMove();
        }
        #endregion

        #region 运动与动画协作
        /// <summary>根据镜头转换后的世界方向计算平滑转向和世界位移。</summary>
        /// <param name="speed">本物理步使用的水平速度。</param>
        /// <param name="turnFactor">普通移动转向系数。</param>
        /// <param name="displacement">输出世界空间位移。</param>
        /// <param name="rotation">输出 CharacterRoot 附加旋转。</param>
        internal void CalculateCodeMotion(float speed, float turnFactor, out Vector3 displacement, out Quaternion rotation)
        {
            Vector3 target = movementInput.normalized;
            float angle = Vector3.SignedAngle(owner.RootTransform.forward, target, Vector3.up);
            float step = Mathf.Clamp(angle, -turnFactor * 180f * fixedDeltaTime, turnFactor * 180f * fixedDeltaTime);
            Quaternion nextRotation = Quaternion.AngleAxis(step, Vector3.up) * owner.RootTransform.rotation;
            rotation = Quaternion.Inverse(owner.RootTransform.rotation) * nextRotation;
            displacement = nextRotation * Vector3.forward * (speed * movementInput.magnitude * fixedDeltaTime);
        }

        /// <summary>提交状态计算的根运动方向修正，不直接修改 CharacterRoot。</summary>
        /// <param name="handle">当前根运动状态的获胜候选句柄。</param>
        internal void SubmitRootRotationCorrection(MotionControlHandle handle)
        {
            Vector3 target = movementInput.sqrMagnitude > 0.0001f ? movementInput.normalized : owner.RootTransform.forward;
            float angle = Vector3.SignedAngle(owner.RootTransform.forward, target, Vector3.up);
            float step = Mathf.Clamp(angle, -Transition.CorrectionSpeed * 180f * fixedDeltaTime,
                Transition.CorrectionSpeed * 180f * fixedDeltaTime);
            Quaternion next = Quaternion.AngleAxis(step, Vector3.up) * owner.RootTransform.rotation;
            driver.SubmitFixed(handle, new FixedMotionRequest(Vector3.zero,
                Quaternion.Inverse(owner.RootTransform.rotation) * next));
        }

        /// <summary>播放 Move Mixer 并保持每个角色独立的运行时状态。</summary>
        internal AnimancerState PlayMoveLoop() => transition?.MoveMixerTransition == null
            ? null : owner.AnimationPlayer.Play(AnimationLayerType.Base, transition.MoveMixerTransition);

        /// <summary>播放待机 Transition。</summary>
        internal void PlayIdleAnimation()
        {
            if (transition?.IdleTransition != null)
                owner.AnimationPlayer.Play(AnimationLayerType.Base, transition.IdleTransition);
        }

        /// <summary>播放起步根运动；缺少配置时进入普通 Move。</summary>
        internal AnimancerState PlayStartAnimation() => transition?.ForwardStart == null
            ? null : owner.AnimationPlayer.Play(AnimationLayerType.Base, transition.ForwardStart);

        /// <summary>播放根据脚步选出的停止根运动。</summary>
        internal AnimancerState PlayStopAnimation()
        {
            ITransition stop = transition?.StopRight ?? transition?.StopLeft;
            return stop == null ? null : owner.AnimationPlayer.Play(AnimationLayerType.Base, stop);
        }

        /// <summary>播放急转向根运动。</summary>
        internal AnimancerState PlaySharpTurnAnimation()
        {
            ITransition turn = transition?.SharpTurnRight ?? transition?.SharpTurnLeft;
            return turn == null ? null : owner.AnimationPlayer.Play(AnimationLayerType.Base, turn);
        }

        /// <summary>按当前实际速度调整 Move Loop 播放速度。</summary>
        /// <param name="state">当前 Move Mixer 状态。</param>
        /// <param name="speed">当前代码移动速度。</param>
        internal void UpdateMoveAnimationSpeed(AnimancerState state, float speed)
        {
            if (state == null || transition == null || MoveReferenceSpeed <= 0f) return;
            state.Speed = speed / MoveReferenceSpeed;
            float parameterSmoothing = transition.MoveParameterSmoothing;
            if (transition.MoveParameterX != null)
            {
                float target = movementInput.magnitude;
                float value = SmoothParameter(target, parameterSmoothing, Time.deltaTime,
                    ref moveParameterXValue);
                owner.AnimationPlayer.SetFloatParameter(transition.MoveParameterX, value);
            }
            if (transition.MoveParameterRotation != null)
            {
                float target = Vector3.SignedAngle(owner.RootTransform.forward,
                    movementInput.sqrMagnitude > 0.0001f ? movementInput.normalized : owner.RootTransform.forward,
                    Vector3.up) * Mathf.Deg2Rad;
                float value = SmoothParameter(target, parameterSmoothing, Time.deltaTime,
                    ref moveParameterRotationValue);
                owner.AnimationPlayer.SetFloatParameter(transition.MoveParameterRotation, value);
            }
        }

        /// <summary>按配置的秒数平滑一个 Move Mixer 参数，避免方向或幅度突变。</summary>
        /// <param name="target">目标参数值。</param>
        /// <param name="smoothing">达到目标所使用的近似秒数；零表示立即跟随。</param>
        /// <param name="deltaTime">本帧缩放时间。</param>
        /// <param name="current">当前缓存值。</param>
        /// <returns>平滑后的参数值。</returns>
        private static float SmoothParameter(float target, float smoothing, float deltaTime, ref float current)
        {
            if (smoothing <= 0f)
            {
                current = target;
                return current;
            }

            float factor = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothing);
            current = Mathf.Lerp(current, target, factor);
            return current;
        }
        #endregion
    }
}
