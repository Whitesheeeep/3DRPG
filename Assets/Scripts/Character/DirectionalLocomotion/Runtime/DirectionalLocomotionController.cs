using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using WS_Modules.FSM;

namespace RPG.Character.DirectionalLocomotion
{
    /// <summary>驱动 UnifiedFSM，并提供方向移动状态所需的输入、动画和运动能力。</summary>
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    [InfoBox("临时 Demo 依赖父级 CharacterActor、PlayerController、共享 CharacterController，以及同节点 Animator 与 AnimancerComponent。")]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(AnimancerComponent))]
    public sealed class DirectionalLocomotionController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private DirectionalLocomotionSetting setting;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private InputActionReference moveAction;

        private CharacterController _characterController;
        private CharacterActor _characterActor;
        private IMotionDriver _motionDriver;
        private MotionControlHandle _gravityHandle;
        private Animator _animator;
        private AnimancerComponent _animancer;
        private StateMachine<DirectionalLocomotionStateId, DirectionalLocomotionController> _stateMachine;
        private Vector2 _moveInput;
        private Vector2 _externalInput;
        private Vector2 _lastDirectionInput;
        private Vector3 _targetDirection;
        private float _verticalSpeed;
        private bool _externalInputEnabled;
        private bool _enabledMoveAction;
        private bool _wasMoving;

        public Vector2 MoveInput => _moveInput;
        public Vector3 TargetDirection => _targetDirection;
        public float CurrentSpeed { get; internal set; }
        public Vector3 RootVelocity { get; internal set; }
        public Vector3 WalkVelocity { get; internal set; }
        public float LockedStartAngle { get; internal set; }
        public float SelectedStartAngle { get; internal set; }
        public string SelectedStartClipName { get; internal set; } = string.Empty;
        public bool IsMoving { get; private set; }
        public bool IsStarting => CurrentStateId == DirectionalLocomotionStateId.MoveStart;
        public float StartNormalizedTime { get; internal set; }
        public DirectionalLocomotionStateId CurrentStateId => _stateMachine?.CurrentState != null
            ? _stateMachine.CurrentState.StateId
            : DirectionalLocomotionStateId.Root;
        public string StateTree => _stateMachine != null ? _stateMachine.ToDebugString() : string.Empty;

        [Title("CharacterController 重力诊断")]
        [ShowInInspector, ReadOnly, LabelText("垂直速度")]
        public float VerticalSpeed => _verticalSpeed;

        [ShowInInspector, ReadOnly, LabelText("传入 Move 的 Y")]
        public float GravityMoveY { get; private set; }

        [ShowInInspector, ReadOnly, LabelText("Move 前 Transform Y")]
        public float GravityBeforeY { get; private set; }

        [ShowInInspector, ReadOnly, LabelText("Move 后 Transform Y")]
        public float GravityAfterY { get; private set; }

        [ShowInInspector, ReadOnly, LabelText("实际 Y 变化")]
        public float GravityActualDeltaY { get; private set; }

        [ShowInInspector, ReadOnly, LabelText("是否接地")]
        public bool IsGrounded => _characterController != null && _characterController.isGrounded;

        [Title("CharacterController 水平移动诊断")]
        [ShowInInspector, ReadOnly, LabelText("水平移动向量")]
        public Vector3 HorizontalMovement { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("水平 Move 前 Y")]
        public float HorizontalBeforeY { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("水平 Move 后 Y")]
        public float HorizontalAfterY { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("水平 Move 实际 Y 变化")]
        public float HorizontalActualDeltaY { get; internal set; }

        [Title("MoveStart 根运动诊断")]
        [ShowInInspector, ReadOnly, LabelText("原始 Animator Delta")]
        public Vector3 RawRootDelta { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("应用的水平根位移")]
        public Vector3 AppliedRootMovement { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("根运动 Move 前 Y")]
        public float RootMotionBeforeY { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("根运动 Move 后 Y")]
        public float RootMotionAfterY { get; internal set; }

        [ShowInInspector, ReadOnly, LabelText("根运动实际 Y 变化")]
        public float RootMotionActualDeltaY { get; internal set; }

        internal Animator Animator => _animator;
        internal IMotionDriver MotionDriver => _motionDriver;
        internal DirectionalLocomotionSetting Setting => setting;
        internal AnimancerComponent Animancer => _animancer;

        /// <summary>缓存方向移动依赖，并构建仅负责输入和动画状态的 Locomotion 状态机。</summary>
        private void Awake()
        {
            _characterController = GetComponentInParent<CharacterController>();
            _characterActor = GetComponentInParent<CharacterActor>();
            _motionDriver = GetComponentInParent<PlayerController>().MotionDriver;
            _animator = GetComponent<Animator>();
            _animancer = GetComponent<AnimancerComponent>();
            if (_animancer == null)
                _animancer = gameObject.AddComponent<AnimancerComponent>();
            _animancer.Animator = _animator;

            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = true;
            BuildStateMachine();
        }

        /// <summary>校验 Demo 配置并进入初始状态。</summary>
        private void Start()
        {
            if (setting == null || !setting.IsValid)
            {
                Debug.LogError("[DirectionalLocomotion] 配置无效，无法启动 Demo。", this);
                enabled = false;
                return;
            }
            _gravityHandle = _motionDriver.RequestControl(new MotionControlRequest(
                _characterActor, MotionPriority.Gravity, MotionChannels.Vertical));
            _stateMachine.OnEnter();
        }

        /// <summary>恢复此 Demo 独占的输入动作。</summary>
        private void OnEnable()
        {
            if (moveAction != null && !moveAction.action.enabled)
            {
                moveAction.action.Enable();
                _enabledMoveAction = true;
            }
        }

        /// <summary>停用输入并释放 Demo 持有的重力控制权。</summary>
        private void OnDisable()
        {
            if (_enabledMoveAction && moveAction != null)
                moveAction.action.Disable();
            _enabledMoveAction = false;
            _gravityHandle?.Dispose();
            _gravityHandle = null;
        }

        /// <summary>退出状态树并释放当前状态运动请求。</summary>
        private void OnDestroy()
        {
            _stateMachine?.OnExit();
        }

        /// <summary>读取连续输入并推进 Demo 普通阶段。</summary>
        private void Update()
        {
            if (setting == null) return;
            ReadInput();
            UpdateMoveIntent();
            _stateMachine.OnUpdate();
        }

        /// <summary>在 PlayerController 结算前提交 Demo 的物理运动。</summary>
        private void FixedUpdate()
        {
            UpdateGravity(Time.fixedDeltaTime);
            _stateMachine?.OnFixedUpdate();
        }

        /// <summary>推进 Demo 表现延迟阶段。</summary>
        private void LateUpdate()
        {
            _stateMachine?.OnLateUpdate();
        }

        /// <summary>只推进临时状态动画回调；正式根运动由 CharacterActor 转交 Player。</summary>
        private void OnAnimatorMove()
        {
            _stateMachine?.OnAnimationMove();
        }

        /// <summary>使用外部二维输入覆盖 Input System，供 AI 或 Odin Demo 测试使用。</summary>
        public void SetExternalInput(Vector2 input)
        {
            _externalInputEnabled = true;
            _externalInput = Vector2.ClampMagnitude(input, 1f);
        }

        /// <summary>结束外部输入覆盖，恢复 Input System 输入。</summary>
        public void ClearExternalInput()
        {
            _externalInputEnabled = false;
            _externalInput = Vector2.zero;
        }

        /// <summary>清空外部输入，使状态机返回 Idle。</summary>
        public void Stop()
        {
            _externalInputEnabled = true;
            _externalInput = Vector2.zero;
        }

        /// <summary>建立临时 Demo 的 Idle、起步和行走状态树。</summary>
        private void BuildStateMachine()
        {
            _stateMachine = new StateMachine<DirectionalLocomotionStateId, DirectionalLocomotionController>(
                DirectionalLocomotionStateId.Root,
                this);
            _stateMachine.AddState(new DirectionalIdleState());
            _stateMachine.AddState(new DirectionalMoveStartState());
            _stateMachine.AddState(new DirectionalMoveState());
            _stateMachine.SetDefaultState(DirectionalLocomotionStateId.Idle);
        }

        /// <summary>读取手动覆盖或输入动作并限制方向长度。</summary>
        private void ReadInput()
        {
            _moveInput = _externalInputEnabled
                ? _externalInput
                : moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
            _moveInput = Vector2.ClampMagnitude(_moveInput, 1f);
        }

        /// <summary>将当前输入和 ASC 水平移动许可合并为本帧移动意图。</summary>
        private void UpdateMoveIntent()
        {
            IsMoving = _motionDriver.CanMoveHorizontally &&
                       _moveInput.sqrMagnitude >= setting.inputDeadZone * setting.inputDeadZone;
            if (IsMoving)
            {
                bool inputChanged = (_moveInput - _lastDirectionInput).sqrMagnitude >=
                                    setting.inputDirectionChangeThreshold * setting.inputDirectionChangeThreshold;
                if (!_wasMoving || inputChanged || _targetDirection.sqrMagnitude <= 0f)
                {
                    _targetDirection = CalculateWorldDirection(_moveInput);
                    _lastDirectionInput = _moveInput;
                }
            }
            _wasMoving = IsMoving;
        }

        /// <summary>将平面输入变换为摄像机相对的世界方向。</summary>
        /// <param name="input">二维方向输入。</param>
        /// <returns>归一化世界方向。</returns>
        private Vector3 CalculateWorldDirection(Vector2 input)
        {
            if (cameraTransform == null)
                return new Vector3(input.x, 0f, input.y).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }

        /// <summary>计算重力位移并交给移动驱动应用，使全部冻结 Tag 可以阻止垂直移动。</summary>
        /// <param name="deltaTime">本帧普通更新的秒数。</param>
        private void UpdateGravity(float deltaTime)
        {
            if (!_characterController.enabled)
                return;

            if (_characterController.isGrounded && _verticalSpeed < 0f)
                _verticalSpeed = -2f;
            else
                _verticalSpeed -= setting.gravity * deltaTime;

            GravityMoveY = _verticalSpeed * deltaTime;
            GravityBeforeY = transform.position.y;
            if (_gravityHandle != null)
                _motionDriver.SubmitFixed(_gravityHandle,
                    FixedMotionRequest.TranslationOnly(Vector3.up * GravityMoveY));
            // 最终位移由稍后执行的 PlayerController.ResolveFixedMotion 统一产生。
            GravityAfterY = transform.position.y;
            GravityActualDeltaY = 0f;
        }
    }
}
