using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace RPG.Character
{
    /// <summary>集中保存角色 Locomotion FSM 的动画过渡、速度基准与转向参数。</summary>
    [CreateAssetMenu(menuName = "RPG/Character/Player FSM Transition")]
    public sealed class PlayerFSMTransition : ScriptableObject
    {
        #region 动画配置
        // 基础循环动画：角色在待机和普通移动状态下持续使用。
        [BoxGroup("基础循环动画"), SerializeField, LabelText("待机动画")]
        private TransitionAsset idleTransition;
        [BoxGroup("基础循环动画"), SerializeField, LabelText("移动混合动画")]
        private TransitionAsset moveMixerTransition;
        [BoxGroup("基础循环动画"), SerializeField, LabelText("移动档位参数"),
         Tooltip("Move Mixer 的 X 参数。当前 CodeLocomotion 固定写入 Walk 档位 1；未来启用明确的 Run 状态后可使用档位 2，不表示输入幅度。")]
        private StringAsset moveParameterX;
        [BoxGroup("基础循环动画"), SerializeField, LabelText("移动旋转参数")]
        private StringAsset moveParameterRotation;
        [BoxGroup("基础循环动画"), SerializeField, MinValue(0f), LabelText("移动转向参数平滑时间（秒）"),
         Tooltip("只平滑写入 Move Mixer 的 Y 转向参数。目标值来自角色前向与世界 Move 方向的夹角；不平滑输入本身、实际 CharacterController 位移、GAS Speed、代码转向速度或 X 移动档位。数值越大响应越慢，0 表示立即跟随；使用指数平滑，经过该时间常数约完成当前差值的 63%。")]
        private float moveParameterSmoothing = 0.1f;

        // 起步根运动：按角色朝向和世界空间 Move 输入的夹角选择一次。
        [InfoBox("角色从 Idle 开始移动时，根据当前朝向与世界移动输入的夹角选择对应起步动画。未配置的方向会直接进入代码移动，不会回退到前向动画；持续移动中切换角色不会播放起步动画。", InfoMessageType.Info)]
        [FoldoutGroup("起步根运动", Expanded = true), SerializeField, LabelText("左转 180°")]
        private ClipTransition left180Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("左转 135°")]
        private ClipTransition left135Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("左转 90°")]
        private ClipTransition left90Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("左转 45°")]
        private ClipTransition left45Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("前向 0°")]
        private ClipTransition forwardStart;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("右转 45°")]
        private ClipTransition right45Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("右转 90°")]
        private ClipTransition right90Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("右转 135°")]
        private ClipTransition right135Start;
        [FoldoutGroup("起步根运动"), SerializeField, LabelText("右转 180°")]
        private ClipTransition right180Start;

        [InfoBox("未配置停止动画时，松开 Move 会直接回到待机。", InfoMessageType.Info)]
        [BoxGroup("停止根运动"), SerializeField, LabelText("左脚停止动画")]
        private ClipTransition stopLeft;
        [BoxGroup("停止根运动"), SerializeField, LabelText("右脚停止动画")]
        private ClipTransition stopRight;

        [InfoBox("急转动画完成后才开始计算再次触发延迟；未配置急转动画时继续使用普通平滑转向。", InfoMessageType.Info)]
        [BoxGroup("急转根运动"), SerializeField, LabelText("左急转动画")]
        private ClipTransition sharpTurnLeft;
        [BoxGroup("急转根运动"), SerializeField, LabelText("右急转动画")]
        private ClipTransition sharpTurnRight;
        #endregion

        #region 运动配置
        // 代码移动参数：只影响 CodeLocomotion 的 Update 位移与转向。
        [BoxGroup("代码移动"), SerializeField, MinValue(0f), LabelText("移动加速度（米/秒²）")]
        private float movementAcceleration = 12f;
        [BoxGroup("代码移动"), SerializeField, MinValue(0f), LabelText("常态转向速度")]
        private float turnSpeed = 1.4f;

        // 根运动方向修正：起步按配置的归一化时间分段开始，使用 Slerp 响应系数跟随目标方向。
        [BoxGroup("根运动方向修正"), SerializeField, MinValue(0f), LabelText("根运动方向修正速度"),
         Tooltip("起步根运动朝向的插值响应系数，单位为秒⁻¹（1/s），不是度/秒。每次动画求值使用 Clamp01(响应系数 × 本次求值时长) 作为 Slerp 比例，从应用动画根旋转后的朝向靠近目标方向。数值越大修正越快，0 表示不进行代码方向修正但保留动画自身根旋转。前向起步立即修正，其他方向达到起步方向修正开始时间后修正。")]
        private float correctionSpeed = 1.8f;
        [BoxGroup("根运动方向修正"), SerializeField, Range(0f, 1f), LabelText("起步方向修正开始时间")]
        private float startDirectionCorrectionNormalizedTime = 0.4f;

        // 急转参数：再次触发延迟从急转动画完成后开始计算。
        [BoxGroup("急转根运动"), SerializeField, Range(0f, 180f), LabelText("急转触发角度（度）")]
        private float sharpTurnAngle = 120f;
        [BoxGroup("急转根运动"), SerializeField, MinValue(0f), LabelText("急转再次触发延迟（秒）"),
         Tooltip("急转动画正常完成并返回普通移动后，需要等待的再次触发时间。"),
         FormerlySerializedAs("sharpTurnCooldown")]
        private float sharpTurnRetriggerDelay = 0.2f;

        // 动画速度匹配：RootReferenceSpeed 是 Move 动画一倍速对应的配置参考速度。
        [InfoBox("动画参考速度表示 Move 动画以 1 倍速播放时对应的期望移动速度。实际目标速度来自角色 GAS Speed，Move 播放速度按当前速度除以动画参考速度计算。", InfoMessageType.Info)]
        [BoxGroup("动画速度匹配"), SerializeField, MinValue(0f), LabelText("动画参考速度（米/秒）")]
        private float rootReferenceSpeed = 2f;
        #endregion

        #region 查询
        /// <summary>获取待机 Transition。</summary>
        public ITransition IdleTransition => idleTransition;
        /// <summary>获取普通 Move Mixer Transition。</summary>
        public ITransition MoveMixerTransition => moveMixerTransition;
        /// <summary>获取 Move Mixer 的移动档位参数键；当前 Walk 档位为 1。</summary>
        public StringAsset MoveParameterX => moveParameterX;
        /// <summary>获取 Move Mixer 旋转参数键。</summary>
        public StringAsset MoveParameterRotation => moveParameterRotation;
        /// <summary>获取 Move Mixer Y 转向参数的指数平滑时间常数。</summary>
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
        /// <summary>获取根运动方向修正的插值响应系数，单位为秒⁻¹（1/s），不是度/秒。</summary>
        public float CorrectionSpeed => correctionSpeed;
        /// <summary>获取非前向起步开始方向修正的归一化时间。</summary>
        public float StartDirectionCorrectionNormalizedTime => startDirectionCorrectionNormalizedTime;
        /// <summary>获取急转向角度阈值。</summary>
        public float SharpTurnAngle => sharpTurnAngle;
        /// <summary>获取急转动画完成后的再次触发延迟。</summary>
        public float SharpTurnRetriggerDelay => sharpTurnRetriggerDelay;
        /// <summary>获取动画一倍速对应的根运动参考速度。</summary>
        public float RootReferenceSpeed => rootReferenceSpeed;

        /// <summary>
        /// 根据角色水平前向与世界空间移动方向选择对应的九方向起步动画。
        /// 这是只读配置查询，不修改资源或运行时状态。
        /// </summary>
        /// <param name="characterForward">角色当前世界空间前向。</param>
        /// <param name="moveDirection">Blackboard 中的世界空间移动方向。</param>
        /// <returns>对应方向已配置的 Transition；输入退化或槽位未配置时返回 null。</returns>
        internal ClipTransition SelectStartTransition(Vector3 characterForward, Vector3 moveDirection)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(characterForward, Vector3.up);
            Vector3 planarMove = Vector3.ProjectOnPlane(moveDirection, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f || planarMove.sqrMagnitude <= 0.0001f)
                return null;

            float angle = Vector3.SignedAngle(
                planarForward.normalized,
                planarMove.normalized,
                Vector3.up);
            if (angle >= -22.5f && angle <= 22.5f) return forwardStart;
            if (angle > 22.5f && angle <= 67.5f) return right45Start;
            if (angle > 67.5f && angle <= 112.5f) return right90Start;
            if (angle > 112.5f && angle <= 157.5f) return right135Start;
            if (angle > 157.5f) return right180Start;
            if (angle < -22.5f && angle >= -67.5f) return left45Start;
            if (angle < -67.5f && angle >= -112.5f) return left90Start;
            if (angle < -112.5f && angle >= -157.5f) return left135Start;
            return left180Start;
        }
        #endregion
    }
}
