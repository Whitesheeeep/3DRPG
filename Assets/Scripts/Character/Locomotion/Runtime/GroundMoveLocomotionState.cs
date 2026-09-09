using Animancer;
using RPG.Character.Animation;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>
    /// Walk 与 Run 之间共享的连续移动动画和速度运行时。
    /// 两个 HFSM 状态仍各自拥有生命周期与 Tag，但切档时保留同一动画相位。
    /// </summary>
    internal sealed class GroundMoveRuntime
    {
        /// <summary>当前播放的 Move Mixer 状态。</summary>
        internal AnimancerState AnimationState;
        /// <summary>当前实际代码移动速度。</summary>
        internal float CurrentSpeed;
        /// <summary>当前 Mixer Y 方向参数。</summary>
        internal float MoveParameterRotationValue;
        /// <summary>清理一次角色重新激活周期的共享移动数据。</summary>
        internal void ResetForActivation()
        {
            AnimationState = null;
            CurrentSpeed = 0f;
            MoveParameterRotationValue = 0f;
        }
    }

    /// <summary>Walk/Run 共用的代码移动行为；具体状态只提供目标速度倍率。</summary>
    internal abstract class GroundMoveLocomotionState : CharacterLocomotionState
    {
        #region 依赖与运行时状态

        private readonly GroundMoveRuntime runtime;
        private AnimancerState animationState;

        #endregion

        #region 构造与入口

        /// <summary>创建接地代码移动状态。</summary>
        /// <param name="stateId">状态标识。</param>
        /// <param name="sharedRuntime">Walk 与 Run 共享的连续移动运行时。</param>
        protected GroundMoveLocomotionState(CharacterLocomotionStateId stateId, GroundMoveRuntime sharedRuntime)
            : base(stateId)
        {
            runtime = sharedRuntime ?? throw new System.ArgumentNullException(nameof(sharedRuntime));
        }

        /// <summary>获取该状态相对 GAS Speed 的倍率。</summary>
        protected abstract float SpeedMultiplier { get; }

        /// <summary>计算当前状态的目标速度。</summary>
        protected float TargetMoveSpeed => TargetSpeed * Mathf.Max(0f, SpeedMultiplier);

        /// <summary>获取当前代码移动速度，供移动诊断和测试读取。</summary>
        internal float CurrentSpeed => runtime.CurrentSpeed;
        /// <summary>获取 Walk/Run 共享运行时。</summary>
        protected GroundMoveRuntime Runtime => runtime;
        /// <summary>获取本帧包含输入幅度的有效水平移动速度。</summary>
        internal float EffectiveMoveSpeed =>
            runtime.CurrentSpeed * Mathf.Clamp01(MovementInput.magnitude);

        /// <inheritdoc />
        public override void OnEnter(bool suppressDefaultState = false)
        {
            bool continueMove = Machine.PreviousState is GroundMoveLocomotionState &&
                runtime.AnimationState != null;
            bool fromRootMotionStart = Machine.PreviousState is RootMotionStartState;

            base.OnEnter(suppressDefaultState);
            AcquireControl(MotionChannels.Horizontal | MotionChannels.Rotation);

            if (!continueMove)
            {
                bool startToMove = fromRootMotionStart;
                runtime.CurrentSpeed = Owner.IsActivationEntry
                    ? TargetMoveSpeed
                    : startToMove
                        ? GetStartReferenceSpeed()
                        : Mathf.Min(TargetMoveSpeed, Transition.RunReferenceSpeed);
                // 起步/落地后的第一帧保持正向 Mixer 姿态；后续 Update 再平滑追踪新的相对方向。
                runtime.MoveParameterRotationValue = 0f;

                if (Transition.MoveMixerTransition != null)
                {
                    // 起步完成后要求零融合直接续接；其他入口继续使用 Transition Library 的来源规则。
                    animationState = startToMove
                        ? Character.AnimationPlayer.Play(
                            AnimationLayerType.Base,
                            Transition.MoveMixerTransition,
                            0f)
                        : Character.AnimationPlayer.Play(
                            AnimationLayerType.Base,
                            Transition.MoveMixerTransition);
                    animationState.NormalizedTime = 0f;
                    runtime.AnimationState = animationState;
                }
            }
            else
            {
                animationState = runtime.AnimationState;
            }

            ApplyMoveParameters();
            if (animationState != null)
                animationState.Speed = CalculateAnimationSpeed();
                // animationState.Speed = 1;
        }

        /// <inheritdoc />
        public override void OnUpdate()
        {
            if (!HasMovement)
            {
                Owner.ChangeState(CharacterLocomotionStateId.RootMotionStop);
                return;
            }

            runtime.CurrentSpeed = Mathf.MoveTowards(
                runtime.CurrentSpeed,
                TargetMoveSpeed,
                Transition.MovementAcceleration * DeltaTime);

            Vector3 targetDirection = MovementInput.normalized;
            float angle = Vector3.SignedAngle(Character.RootTransform.forward, targetDirection, Vector3.up);
            float maxAngle = Transition.TurnSpeed * 180f * DeltaTime;
            float step = Mathf.Clamp(angle, -maxAngle, maxAngle);
            Quaternion nextRotation = Quaternion.AngleAxis(step, Vector3.up) * Character.RootTransform.rotation;
            Quaternion relativeRotation = Quaternion.Inverse(Character.RootTransform.rotation) * nextRotation;
            Vector3 displacement = nextRotation * Vector3.forward *
                (runtime.CurrentSpeed * MovementInput.magnitude * DeltaTime);

            Driver.SubmitUpdate(ControlHandle, new UpdateMotionSubmission(displacement, relativeRotation));
            UpdateMoveAnimation(nextRotation);
        }

        /// <inheritdoc />
        public override void OnExit()
        {
            ReleaseControl();
            animationState = null;
            base.OnExit();
        }

        /// <inheritdoc />
        internal override void ResetForActivation()
        {
            animationState = null;
            runtime.ResetForActivation();
            base.ResetForActivation();
        }

        #endregion

        #region 动画表现

        /// <summary>按有效移动速度和相对方向更新 Move Mixer。</summary>
        private void UpdateMoveAnimation(Quaternion predictedRotation)
        {
            if (animationState != null)
                animationState.Speed = CalculateAnimationSpeed();
                // animationState.Speed = 1;

            if (Transition.MoveParameterX != null)
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterX,
                    CalculateMixerSpeedParameter());

            if (Transition.MoveParameterRotation == null)
                return;

            float target = CalculateMoveRotationParameter(predictedRotation);
            runtime.MoveParameterRotationValue = SmoothScalarParameter(
                target,
                Transition.MoveParameterSmoothing,
                DeltaTime,
                runtime.MoveParameterRotationValue);
            Character.AnimationPlayer.SetFloatParameter(
                Transition.MoveParameterRotation,
                Mathf.Clamp(runtime.MoveParameterRotationValue, -2f, 2f));
        }

        /// <summary>根据有效速度计算 Move Mixer X；0、1、2 分别对应 Idle、Walk 参考点和 Run 参考点。</summary>
        private float CalculateMixerSpeedParameter()
        {
            float walkReferenceSpeed = Mathf.Max(0.01f, Transition.WalkReferenceSpeed);
            float runReferenceSpeed = Mathf.Max(0.01f, Transition.RunReferenceSpeed);
            if (EffectiveMoveSpeed <= walkReferenceSpeed)
                return Mathf.Clamp01(EffectiveMoveSpeed / walkReferenceSpeed);

            return Mathf.Clamp(
                1f + (EffectiveMoveSpeed - walkReferenceSpeed) /
                Mathf.Max(0.01f, runReferenceSpeed - walkReferenceSpeed),
                1f,
                2f);
        }

        /// <summary>按起步状态类型返回与起步动画匹配的 Move 入口速度。</summary>
        /// <returns>WalkStart 使用 Walk 参考速度，RunStart 使用 Run 参考速度。</returns>
        private float GetStartReferenceSpeed()
        {
            return Machine.PreviousState is RootMotionRunStartState
                ? Transition.RunReferenceSpeed
                : Transition.WalkReferenceSpeed;
        }

        /// <summary>计算 Move Mixer 的整体播放倍率；仅超过 Run 参考速度后加速。</summary>
        private float CalculateAnimationSpeed()
        {
            float runReferenceSpeed = Mathf.Max(0.01f, Transition.RunReferenceSpeed);
            return Mathf.Max(1f, EffectiveMoveSpeed / runReferenceSpeed);
        }

        /// <summary>计算角色前向与世界 Move 的有符号弧度角。</summary>
        private float CalculateMoveRotationParameter(Quaternion predictedRotation)
        {
            Vector3 predictedForward = Vector3.ProjectOnPlane(
                predictedRotation * Vector3.forward,
                Vector3.up);
            Vector3 targetDirection = Vector3.ProjectOnPlane(
                HasMovement ? MovementInput : predictedForward,
                Vector3.up);
            if (predictedForward.sqrMagnitude <= 0.0001f || targetDirection.sqrMagnitude <= 0.0001f)
                return 0f;

            float angleParameter = Mathf.Clamp(
                Vector3.SignedAngle(
                    predictedForward.normalized,
                    targetDirection.normalized,
                    Vector3.up) * Mathf.Deg2Rad,
                -2f,
                2f);
            float runReferenceSpeed = Mathf.Max(0.01f, Transition.RunReferenceSpeed);
            float speedWeight = Mathf.Clamp01(EffectiveMoveSpeed / runReferenceSpeed);
            return Mathf.Clamp(angleParameter * speedWeight, -2f, 2f);
        }

        /// <summary>初始化当前状态的速度映射和方向参数。</summary>
        private void ApplyMoveParameters()
        {
            if (Transition.MoveParameterX != null)
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterX,
                    CalculateMixerSpeedParameter());
            if (Transition.MoveParameterRotation != null)
                Character.AnimationPlayer.SetFloatParameter(
                    Transition.MoveParameterRotation,
                    runtime.MoveParameterRotationValue);
        }

        /// <summary>以最短角度差平滑 Move Mixer 的 Y 参数。</summary>
        private static float SmoothScalarParameter(float target, float smoothing, float deltaTime, float current)
        {
            if (smoothing <= 0f)
                return Mathf.Clamp(target, -2f, 2f);
            float factor = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / smoothing);
            return Mathf.Clamp(
                Mathf.Lerp(current, target, factor),
                -2f,
                2f);
        }

        #endregion
    }
}
