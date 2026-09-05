using Animancer;
using RPG.Character.Animation;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>使用 GAS Speed 驱动普通 Move Loop、转向和世界空间代码位移。</summary>
    public sealed class CodeLocomotionState : CharacterLocomotionState
    {
        #region 运行时状态

        private AnimancerState animationState;
        private float currentSpeed;
        private float moveReferenceSpeed;
        private float moveParameterXValue;
        private float moveParameterRotationValue;

        #endregion

        #region 生命周期

        /// <summary>创建普通代码移动状态。</summary>
        public CodeLocomotionState() : base(CharacterLocomotionStateId.CodeLocomotion)
        {
        }

        /// <inheritdoc />
        public override void OnEnter()
        {
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation);
            moveReferenceSpeed = Mathf.Max(0.01f, Transition.RootReferenceSpeed);
            animationState = null;

            bool enteredFromStart =
                !Owner.IsActivationEntry &&
                Machine.PreviousState is RootMotionStartState;

            // 首次启用或持续移动切人没有前置 Locomotion 状态，直接使用新角色自身 GAS Speed。
            currentSpeed = Owner.IsActivationEntry
                ? TargetSpeed
                : Mathf.Min(TargetSpeed, moveReferenceSpeed);

            if (enteredFromStart)
            {
                // Start 末段已经与 Move Loop 对齐；直接以当前输入初始化参数，避免第一帧从默认值追赶。
                moveParameterXValue = MovementInput.magnitude;
                moveParameterRotationValue = CalculateMoveRotationParameter();
                ApplyMoveParameters();
            }

            if (Transition.MoveMixerTransition != null)
            {
                animationState = Character.AnimationPlayer.Play(
                    AnimationLayerType.Base,
                    Transition.MoveMixerTransition,
                    enteredFromStart ? 0f : Transition.MoveMixerTransition.FadeDuration);
                animationState.Speed = 1;

                if (enteredFromStart)
                {
                    // 直接衔接使用素材约定的 Move 起点，不复用上一次循环状态的播放进度。
                    animationState.NormalizedTime = 0f;
                }
            }
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement)
            {
                Owner.ChangeState(HasStopAnimation
                    ? CharacterLocomotionStateId.RootMotionStop
                    : CharacterLocomotionStateId.Idle);
                return;
            }

            UpdateMoveAnimation();
        }

        /// <inheritdoc />
        public override void OnFixedUpdate()
        {
            // 速度加速和本物理步位移必须使用同一次 FixedTick 传入的步长，支持手动步进测试。
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                TargetSpeed,
                Transition.MovementAcceleration * FixedDeltaTime);

            Vector3 target = MovementInput.normalized;
            float angle = Vector3.SignedAngle(Character.RootTransform.forward, target, Vector3.up);
            float step = Mathf.Clamp(
                angle,
                -Transition.TurnSpeed * 180f * FixedDeltaTime,
                Transition.TurnSpeed * 180f * FixedDeltaTime);
            Quaternion nextRotation = Quaternion.AngleAxis(
                step,
                Vector3.up) * Character.RootTransform.rotation;
            Quaternion rotation = Quaternion.Inverse(Character.RootTransform.rotation) * nextRotation;
            Vector3 displacement = nextRotation * Vector3.forward *
                                   (currentSpeed * MovementInput.magnitude * FixedDeltaTime);

            Driver.SubmitFixed(ControlHandle, new FixedMotionRequest(displacement, rotation));
        }

        /// <inheritdoc />
        public override void OnExit() => ReleaseControl();

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            animationState = null;
            currentSpeed = 0f;
            moveReferenceSpeed = 0f;
            moveParameterXValue = 0f;
            moveParameterRotationValue = 0f;
        }

        #endregion

        #region 急转协作

        /// <summary>获取当前是否配置了停止根运动。</summary>
        private bool HasStopAnimation =>
            Transition.StopLeft != null && Transition.StopLeft.IsValid ||
            Transition.StopRight != null && Transition.StopRight.IsValid;

        #endregion

        #region 移动表现

        /// <summary>按当前实际速度和输入更新 Move Mixer 及其参数。</summary>
        private void UpdateMoveAnimation()
        {
            if (animationState != null && moveReferenceSpeed > 0f)
                animationState.Speed = 1;

            float parameterSmoothing = Transition.MoveParameterSmoothing;
            if (Transition.MoveParameterX != null)
            {
                moveParameterXValue = 1;
                /*moveParameterXValue = SmoothParameter(
                    MovementInput.magnitude,
                    parameterSmoothing,
                    DeltaTime,
                    ref moveParameterXValue);
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterX,
                    moveParameterXValue);*/
            }

            if (Transition.MoveParameterRotation != null)
            {
                float target = CalculateMoveRotationParameter();
                moveParameterRotationValue = SmoothParameter(
                    target,
                    parameterSmoothing,
                    DeltaTime,
                    ref moveParameterRotationValue);
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterRotation,
                    moveParameterRotationValue);
            }
        }

        /// <summary>计算 Move Mixer 使用的角色相对移动方向参数，结果单位为弧度。</summary>
        /// <returns>角色水平前向与当前世界移动方向的有符号夹角。</returns>
        private float CalculateMoveRotationParameter()
        {
            return Vector3.SignedAngle(
                Character.RootTransform.forward,
                HasMovement ? MovementInput.normalized : Character.RootTransform.forward,
                Vector3.up) * Mathf.Deg2Rad;
        }

        /// <summary>将当前缓存的 Move Mixer 参数立即写入 Animancer。</summary>
        /// <remarks>用于 Start 直接衔接时的首次求值，后续帧仍由指数平滑更新。</remarks>
        private void ApplyMoveParameters()
        {
            if (Transition.MoveParameterX != null)
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterX,
                    1);

            if (Transition.MoveParameterRotation != null)
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterRotation,
                    moveParameterRotationValue);
        }

        /// <summary>按配置的秒数平滑一个 Move Mixer 参数，避免方向或幅度突变。</summary>
        /// <param name="target">目标参数值。</param>
        /// <param name="smoothing">指数平滑时间常数；零表示立即跟随，并非完全到达目标的固定耗时。</param>
        /// <param name="deltaTime">当前普通 Tick 的帧时长。</param>
        /// <param name="current">当前缓存值。</param>
        /// <returns>平滑后的参数值。</returns>
        private static float SmoothParameter(
            float target,
            float smoothing,
            float deltaTime,
            ref float current)
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
