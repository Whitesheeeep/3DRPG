using Animancer;
using UnityEngine;

namespace RPG.Character
{
    /// <summary>集中保存角色 Locomotion FSM 的动画过渡、速度基准与转向参数。</summary>
    [CreateAssetMenu(menuName = "RPG/Character/Player FSM Transition")]
    public sealed class PlayerFSMTransition : ScriptableObject
    {
        #region 动画配置
        [SerializeField] private TransitionAsset idleTransition;
        [SerializeField] private TransitionAsset moveMixerTransition;
        [SerializeField] private StringAsset moveParameterX;
        [SerializeField] private StringAsset moveParameterRotation;
        [SerializeField, Min(0f)] private float moveParameterSmoothing = 0.1f;
        [SerializeField] private ClipTransition forwardStart;
        [SerializeField] private ClipTransition stopLeft;
        [SerializeField] private ClipTransition stopRight;
        [SerializeField] private ClipTransition sharpTurnLeft;
        [SerializeField] private ClipTransition sharpTurnRight;
        #endregion

        #region 运动配置
        [SerializeField, Min(0f)] private float movementAcceleration = 12f;
        [SerializeField, Min(0f)] private float turnSpeed = 1.4f;
        [SerializeField, Min(0f)] private float correctionSpeed = 1.8f;
        // 保留起步/急转向动画的修正窗口配置；当前策略从动画首帧开始快速修正。
        [SerializeField, Range(0f, 1f)] private float correctionNormalizedTime = 0.4f;
        [SerializeField, Range(0f, 180f)] private float sharpTurnAngle = 120f;
        [SerializeField, Min(0f)] private float sharpTurnCooldown = 0.2f;
        [SerializeField, Min(0f)] private float rootReferenceSpeed = 2f;
        [SerializeField] private bool useMeasuredRootReferenceSpeed;
        #endregion

        /// <summary>获取待机 Transition。</summary>
        public ITransition IdleTransition => idleTransition;
        /// <summary>获取普通 Move Mixer Transition。</summary>
        public ITransition MoveMixerTransition => moveMixerTransition;
        /// <summary>获取 Move Mixer 横向参数键。</summary>
        public StringAsset MoveParameterX => moveParameterX;
        /// <summary>获取 Move Mixer 旋转参数键。</summary>
        public StringAsset MoveParameterRotation => moveParameterRotation;
        /// <summary>获取参数平滑时长。</summary>
        public float MoveParameterSmoothing => moveParameterSmoothing;
        /// <summary>获取前向起步 Transition。</summary>
        public ClipTransition ForwardStart => forwardStart;
        /// <summary>获取左脚停止 Transition。</summary>
        public ClipTransition StopLeft => stopLeft;
        /// <summary>获取右脚停止 Transition。</summary>
        public ClipTransition StopRight => stopRight;
        /// <summary>获取左转急转向 Transition。</summary>
        public ClipTransition SharpTurnLeft => sharpTurnLeft;
        /// <summary>获取右转急转向 Transition。</summary>
        public ClipTransition SharpTurnRight => sharpTurnRight;
        /// <summary>获取代码移动加速度。</summary>
        public float MovementAcceleration => movementAcceleration;
        /// <summary>获取普通移动转向速度。</summary>
        public float TurnSpeed => turnSpeed;
        /// <summary>获取根运动方向修正速度。</summary>
        public float CorrectionSpeed => correctionSpeed;
        /// <summary>获取根运动方向修正配置窗口；当前起步和急转向从动画首帧开始修正。</summary>
        public float CorrectionNormalizedTime => correctionNormalizedTime;
        /// <summary>获取急转向角度阈值。</summary>
        public float SharpTurnAngle => sharpTurnAngle;
        /// <summary>获取急转向再次触发间隔。</summary>
        public float SharpTurnCooldown => sharpTurnCooldown;
        /// <summary>获取动画一倍速对应的根运动参考速度。</summary>
        public float RootReferenceSpeed => rootReferenceSpeed;
        /// <summary>获取是否使用编辑器测得的根运动参考速度。</summary>
        public bool UseMeasuredRootReferenceSpeed => useMeasuredRootReferenceSpeed;
    }
}
