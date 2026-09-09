using Animancer;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.TAG;

namespace RPG.Character
{
    /// <summary>集中保存角色 Locomotion FSM 的动画过渡、速度基准与转向参数。</summary>
    [CreateAssetMenu(menuName = "RPG/Character/Player FSM Transition")]
    public sealed class PlayerFSMTransition : ScriptableObject
    {
        #region 动画配置
        // 基础循环动画：角色在待机和普通移动状态下持续使用。
        [FoldoutGroup("基础循环动画"), SerializeField, LabelText("待机动画")]
        private TransitionAsset idleTransition;
        [FoldoutGroup("基础循环动画"), SerializeField, LabelText("移动混合动画")]
        private TransitionAsset moveMixerTransition;
        [FoldoutGroup("基础循环动画"), SerializeField, LabelText("移动速度参数"),
         Tooltip("Move Mixer 的 X 参数。由实际有效移动速度按照 WalkReferenceSpeed 与 RunReferenceSpeed 两个参考点分段映射到 0~2：0 为 Idle，1 为 Walk 参考点，2 为 Run 参考点。")]
        private StringAsset moveParameterX;
        [FoldoutGroup("基础循环动画"), SerializeField, LabelText("移动旋转参数"),
         Tooltip("RotationY 表示角色预计前向与世界 Move 方向的相对角度。角度以弧度限制在 -2 到 2，并乘以有效移动速度相对 RunReferenceSpeed 的权重；达到 RunReferenceSpeed 时使用完整范围。该参数不是角色世界旋转速度。")]
        private StringAsset moveParameterRotation;
        [FoldoutGroup("基础循环动画"), SerializeField, MinValue(0f), LabelText("移动转向参数平滑时间（秒）"),
         Tooltip("只平滑 Move Mixer 的 RotationY 有限标量。目标值来自预计角色前向与世界 Move 方向的相对角度及有效速度权重；不平滑输入、角色世界旋转、实际位移、GAS Speed 或 Mixer X。单位为秒，值越小响应越快，0 表示立即写入。")]
        private float moveParameterSmoothing = 0.1f;
        // 起步根运动：按角色朝向和世界空间 Move 输入的夹角选择一次。
        [InfoBox("角色从 Idle 开始移动时，根据当前朝向与世界移动输入的夹角选择对应起步动画。所有方向槽位都必须配置有效动画，缺失会在角色配置校验阶段直接报告；持续移动中切换角色不会播放起步动画。", InfoMessageType.Info)]
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

        [InfoBox("Run 起步只在 Idle 或 Stop 直接进入奔跑时使用。前向使用当前脚相位选择 L0/R0；起步结束时会把结束姿态的脚相位写入 RunFeet。所有槽位都是必需配置。", InfoMessageType.Info)]
        [FoldoutGroup("Run 起步根运动", Expanded = false), SerializeField, LabelText("左转 180°")]
        private ClipTransition runLeft180Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("左转 135°")]
        private ClipTransition runLeft135Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("左转 90°")]
        private ClipTransition runLeft90Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("左转 45°")]
        private ClipTransition runLeft45Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("前向 / L0")]
        private ClipTransition runForwardLeftStart;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("前向 / R0")]
        private ClipTransition runForwardRightStart;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("右转 45°")]
        private ClipTransition runRight45Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("右转 90°")]
        private ClipTransition runRight90Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("右转 135°")]
        private ClipTransition runRight135Start;
        [FoldoutGroup("Run 起步根运动"), SerializeField, LabelText("右转 180°")]
        private ClipTransition runRight180Start;

        [InfoBox("未配置停止动画时，松开 Move 会直接回到待机。", InfoMessageType.Info)]
        [FoldoutGroup("停止根运动"), SerializeField, LabelText("左脚停止动画")]
        private ClipTransition stopLeft;
        [FoldoutGroup("停止根运动"), SerializeField, LabelText("右脚停止动画")]
        private ClipTransition stopRight;

        [InfoBox("主动跳跃只区分原地起跳和向前起跳；是否向前起跳依据起跳前实际水平速度。外力弹射先播放 Start，仍在上升时再进入 Loop。落地动画根据本次离地高度选择 1h、2h 或 3h。", InfoMessageType.Info)]
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("原地起跳动画")]
        private ClipTransition jumpTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("向前起跳动画")]
        private ClipTransition forwardJumpTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("外力弹射起始动画")]
        private ClipTransition externalLaunchStartTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("外力弹射上升循环动画")]
        private ClipTransition externalLaunchLoopTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("下落动画")]
        private ClipTransition fallTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("1 米落地动画")]
        private TransitionAsset landing1HeightTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("2 米落地动画")]
        private TransitionAsset landing2HeightTransition;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("3 米落地动画")]
        private TransitionAsset landing3HeightTransition;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), MaxValue(1f), LabelText("1 米落地输入开放时间"),
         Tooltip("1 米落地动画播放到该归一化进度后，才允许新的 Jump 或 Move 输入打断；这是动画进度，不是秒数。")]
        private float landing1InputNormalizedTime = 0.35f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), MaxValue(1f), LabelText("2 米落地输入开放时间"),
         Tooltip("2 米落地动画播放到该归一化进度后，才允许新的 Jump 或 Move 输入打断；这是动画进度，不是秒数。")]
        private float landing2InputNormalizedTime = 0.5f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), MaxValue(1f), LabelText("3 米落地输入开放时间"),
         Tooltip("3 米落地动画播放到该归一化进度后，才允许新的 Jump 或 Move 输入打断；这是动画进度，不是秒数。")]
        private float landing3InputNormalizedTime = 0.65f;

        #endregion

        #region 运动配置
        // 代码移动参数：只影响 Walk/Run 的 Update 位移与转向。
        [FoldoutGroup("代码移动"), SerializeField, MinValue(0f), LabelText("移动加速度（米/秒²）")]
        private float movementAcceleration = 12f;
        [FoldoutGroup("代码移动"), SerializeField, MinValue(0f), LabelText("常态转向速度")]
        private float turnSpeed = 1.4f;
        [FoldoutGroup("代码移动"), SerializeField, MinValue(0f), LabelText("Run 速度倍率")]
        private float runSpeedMultiplier = 1.35f;

        // 根运动方向修正：起步按配置的归一化时间分段开始，使用 Slerp 响应系数跟随目标方向。
        [FoldoutGroup("根运动方向修正"), SerializeField, MinValue(0f), LabelText("根运动方向修正速度"),
         Tooltip("起步根运动朝向的插值响应系数，单位为秒⁻¹（1/s），不是度/秒。每次动画求值使用 Clamp01(响应系数 × 本次求值时长) 作为 Slerp 比例，从应用动画根旋转后的朝向靠近目标方向。数值越大修正越快，0 表示不进行代码方向修正但保留动画自身根旋转。前向起步立即修正，其他方向达到起步方向修正开始时间后修正。")]
        private float correctionSpeed = 1.8f;
        [FoldoutGroup("根运动方向修正"), SerializeField, MinValue(0f), MaxValue(1f), LabelText("起步方向修正开始时间")]
        private float startDirectionCorrectionNormalizedTime = 0.4f;


        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("土狼时间（秒）")]
        private float coyoteTime = 0.12f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("外力弹射最低垂直速度")]
        private float externalLaunchMinVerticalSpeed = 0.5f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("主动跳跃初速度")]
        private float jumpInitialSpeed = 5f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("向前起跳最低水平速度（米/秒）"),
         Tooltip("起跳瞬间实际观测到的水平速度达到该值时播放向前起跳，否则播放原地起跳。")]
        private float forwardJumpMinSpeed = 0.2f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("空中水平加速度（米/秒²）"),
         Tooltip("空中有移动输入时，水平速度向当前目标方向和目标速度靠近的最大加速度；无输入时保留当前惯性。")]
        private float airMovementAcceleration = 4f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("空中转向速度（度/秒）"),
         Tooltip("空中角色朝水平运动方向旋转的最大角速度，不改变地面 TurnSpeed。")]
        private float airTurnSpeed = 360f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("2 米落地高度阈值（米）"),
         Tooltip("离地高度达到该值时优先选择 2 米落地动画；应小于 3 米阈值。")]
        private float landing2HeightThreshold = 1.5f;
        [FoldoutGroup("空中与环境"), SerializeField, MinValue(0f), LabelText("3 米落地高度阈值（米）"),
         Tooltip("离地高度达到该值时优先选择 3 米落地动画。")]
        private float landing3HeightThreshold = 2.5f;
        [FoldoutGroup("空中与环境"), SerializeField, LabelText("外力弹射原因 Tag"),
         Tooltip("由跳板、击飞等外力 GameplayEffect/GA 在生效期间维护的业务原因 Tag；它不是 ExternalLaunch 状态 Tag，用于在同帧 Jump 前识别外部上升运动。")]
        private GameplayTag externalLaunchCauseTag = GameplayTag.Empty;

        [InfoBox("状态 Tag 由各叶状态在进入和退出时对称维护；建议使用 State.Locomotion.Grounded.* 与 State.Locomotion.Airborne.* 的精确子 Tag。空值表示当前项目尚未为该状态配置 Tag。", InfoMessageType.Info)]
        [FoldoutGroup("状态门禁", Expanded = false), SerializeField, LabelText("Idle 状态 Tag")]
        private GameplayTag idleStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("WalkStart 状态 Tag")]
        private GameplayTag walkStartStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("RunStart 状态 Tag")]
        private GameplayTag runStartStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Walk 状态 Tag")]
        private GameplayTag walkStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Run 状态 Tag")]
        private GameplayTag runStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Stop 状态 Tag")]
        private GameplayTag stopStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Jump 状态 Tag")]
        private GameplayTag jumpStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("ExternalLaunch 状态 Tag")]
        private GameplayTag externalLaunchStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Fall 状态 Tag")]
        private GameplayTag fallStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("FallLand 状态 Tag")]
        private GameplayTag fallLandStateTag = GameplayTag.Empty;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Idle 进入查询")]
        private GameplayTagQuery idleCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("WalkStart 进入查询")]
        private GameplayTagQuery walkStartCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("RunStart 进入查询")]
        private GameplayTagQuery runStartCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Walk 进入查询")]
        private GameplayTagQuery walkCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Run 进入查询")]
        private GameplayTagQuery runCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Stop 进入查询")]
        private GameplayTagQuery stopCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Jump 进入查询")]
        private GameplayTagQuery jumpCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("ExternalLaunch 进入查询")]
        private GameplayTagQuery externalLaunchCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("Fall 进入查询")]
        private GameplayTagQuery fallCanEnterQuery;
        [FoldoutGroup("状态门禁"), SerializeField, LabelText("FallLand 进入查询")]
        private GameplayTagQuery fallLandCanEnterQuery;

        // 动画速度匹配：两个参考速度分别固定 Mixer 的 Walk/Run 采样点，不修改实际 CharacterController 位移。
        [InfoBox("Walk 动画参考速度对应 Move Mixer 的 X=1，Run 动画参考速度对应 X=2。两者之间按实际有效速度连续混合；只有超过 Run 参考速度时才提高整个 Mixer 的播放倍率。", InfoMessageType.Info)]
        [FoldoutGroup("动画速度匹配"), SerializeField, MinValue(0.01f), LabelText("Walk 动画参考速度（米/秒）"),
         Tooltip("Move Mixer 的 X=1 对应的实际有效水平速度，也是 WalkStart 进入 Walk 时的初始速度。该值必须小于 Run 动画参考速度。")]
        private float walkReferenceSpeed = 0.914f;
        [FoldoutGroup("动画速度匹配"), SerializeField, MinValue(0.01f), LabelText("Run 动画参考速度（米/秒）")]
        private float runReferenceSpeed = 3f;
        [FoldoutGroup("动画速度匹配"), SerializeField, LabelText("RunFeet 参数")]
        private StringAsset runFeetParameter;

        #endregion

        #region 查询
        /// <summary>获取待机 Transition。</summary>
        public ITransition IdleTransition => idleTransition;
        /// <summary>获取普通 Move Mixer Transition。</summary>
        public ITransition MoveMixerTransition => moveMixerTransition;
        /// <summary>获取由有效移动速度驱动的 Move Mixer X 参数键。</summary>
        public StringAsset MoveParameterX => moveParameterX;
        /// <summary>获取 Move Mixer 旋转参数键。</summary>
        public StringAsset MoveParameterRotation => moveParameterRotation;
        /// <summary>获取 Move Mixer Y 转向参数的指数平滑时间常数。</summary>
        public float MoveParameterSmoothing => moveParameterSmoothing;
        /// <summary>获取前向起步 Transition。</summary>
        public ClipTransition ForwardStart => forwardStart;
        /// <summary>获取 Run 起步左转 180 度 Transition。</summary>
        public ClipTransition RunLeft180Start => runLeft180Start;
        /// <summary>获取 Run 起步左转 135 度 Transition。</summary>
        public ClipTransition RunLeft135Start => runLeft135Start;
        /// <summary>获取 Run 起步左转 90 度 Transition。</summary>
        public ClipTransition RunLeft90Start => runLeft90Start;
        /// <summary>获取 Run 起步左转 45 度 Transition。</summary>
        public ClipTransition RunLeft45Start => runLeft45Start;
        /// <summary>获取 Run 起步前向左脚相位 Transition。</summary>
        public ClipTransition RunForwardLeftStart => runForwardLeftStart;
        /// <summary>获取 Run 起步前向右脚相位 Transition。</summary>
        public ClipTransition RunForwardRightStart => runForwardRightStart;
        /// <summary>获取 Run 起步右转 45 度 Transition。</summary>
        public ClipTransition RunRight45Start => runRight45Start;
        /// <summary>获取 Run 起步右转 90 度 Transition。</summary>
        public ClipTransition RunRight90Start => runRight90Start;
        /// <summary>获取 Run 起步右转 135 度 Transition。</summary>
        public ClipTransition RunRight135Start => runRight135Start;
        /// <summary>获取 Run 起步右转 180 度 Transition。</summary>
        public ClipTransition RunRight180Start => runRight180Start;
        /// <summary>获取左脚停止 Transition。</summary>
        public ClipTransition StopLeft => stopLeft;
        /// <summary>获取右脚停止 Transition。</summary>
        public ClipTransition StopRight => stopRight;
        /// <summary>获取代码移动加速度。</summary>
        public float MovementAcceleration => movementAcceleration;
        /// <summary>获取普通移动转向速度。</summary>
        public float TurnSpeed => turnSpeed;
        /// <summary>获取根运动方向修正的插值响应系数，单位为秒⁻¹（1/s），不是度/秒。</summary>
        public float CorrectionSpeed => correctionSpeed;
        /// <summary>获取非前向起步开始方向修正的归一化时间。</summary>
        public float StartDirectionCorrectionNormalizedTime => startDirectionCorrectionNormalizedTime;
        /// <summary>获取 Run 目标速度倍率。</summary>
        public float RunSpeedMultiplier => runSpeedMultiplier;
        /// <summary>获取土狼时间。</summary>
        public float CoyoteTime => coyoteTime;
        /// <summary>获取外力弹射所需的最低观测垂直速度。</summary>
        public float ExternalLaunchMinVerticalSpeed => externalLaunchMinVerticalSpeed;
        /// <summary>获取外力弹射原因 Tag。</summary>
        public GameplayTag ExternalLaunchCauseTag => externalLaunchCauseTag;
        /// <summary>获取 Idle 叶状态 Tag。</summary>
        public GameplayTag IdleStateTag => idleStateTag;
        /// <summary>获取 WalkStart 叶状态 Tag。</summary>
        public GameplayTag WalkStartStateTag => walkStartStateTag;
        /// <summary>获取 RunStart 叶状态 Tag。</summary>
        public GameplayTag RunStartStateTag => runStartStateTag;
        /// <summary>获取 Walk 叶状态 Tag。</summary>
        public GameplayTag WalkStateTag => walkStateTag;
        /// <summary>获取 Run 叶状态 Tag。</summary>
        public GameplayTag RunStateTag => runStateTag;
        /// <summary>获取 Stop 叶状态 Tag。</summary>
        public GameplayTag StopStateTag => stopStateTag;
        /// <summary>获取 Jump 叶状态 Tag。</summary>
        public GameplayTag JumpStateTag => jumpStateTag;
        /// <summary>获取 ExternalLaunch 叶状态 Tag。</summary>
        public GameplayTag ExternalLaunchStateTag => externalLaunchStateTag;
        /// <summary>获取 Fall 叶状态 Tag。</summary>
        public GameplayTag FallStateTag => fallStateTag;
        /// <summary>获取 FallLand 叶状态 Tag。</summary>
        public GameplayTag FallLandStateTag => fallLandStateTag;
        /// <summary>获取 Idle 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery IdleCanEnterQuery => idleCanEnterQuery;
        /// <summary>获取 WalkStart 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery WalkStartCanEnterQuery => walkStartCanEnterQuery;
        /// <summary>获取 RunStart 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery RunStartCanEnterQuery => runStartCanEnterQuery;
        /// <summary>获取 Walk 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery WalkCanEnterQuery => walkCanEnterQuery;
        /// <summary>获取 Run 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery RunCanEnterQuery => runCanEnterQuery;
        /// <summary>获取 Stop 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery StopCanEnterQuery => stopCanEnterQuery;
        /// <summary>获取 Jump 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery JumpCanEnterQuery => jumpCanEnterQuery;
        /// <summary>获取 ExternalLaunch 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery ExternalLaunchCanEnterQuery => externalLaunchCanEnterQuery;
        /// <summary>获取 Fall 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery FallCanEnterQuery => fallCanEnterQuery;
        /// <summary>获取 FallLand 的 ASC Tag 门禁查询。</summary>
        public GameplayTagQuery FallLandCanEnterQuery => fallLandCanEnterQuery;
        /// <summary>获取原地起跳动画。</summary>
        public ClipTransition JumpTransition => jumpTransition;
        /// <summary>获取向前起跳动画。</summary>
        public ClipTransition ForwardJumpTransition => forwardJumpTransition;
        /// <summary>获取外力弹射起始动画。</summary>
        public ClipTransition ExternalLaunchStartTransition => externalLaunchStartTransition;
        /// <summary>获取外力弹射上升循环动画。</summary>
        public ClipTransition ExternalLaunchLoopTransition => externalLaunchLoopTransition;
        /// <summary>获取下落动画。</summary>
        public ClipTransition FallTransition => fallTransition;
        /// <summary>获取 1 米落地动画。</summary>
        public TransitionAsset Landing1HeightTransition => landing1HeightTransition;
        /// <summary>获取 2 米落地动画。</summary>
        public TransitionAsset Landing2HeightTransition => landing2HeightTransition;
        /// <summary>获取 3 米落地动画。</summary>
        public TransitionAsset Landing3HeightTransition => landing3HeightTransition;
        /// <summary>获取 2 米落地动画的高度阈值。</summary>
        public float Landing2HeightThreshold => landing2HeightThreshold;
        /// <summary>获取 3 米落地动画的高度阈值。</summary>
        public float Landing3HeightThreshold => landing3HeightThreshold;
        /// <summary>获取主动跳跃初速度。</summary>
        public float JumpInitialSpeed => jumpInitialSpeed;
        /// <summary>获取选择向前起跳所需的最低实际水平速度。</summary>
        public float ForwardJumpMinSpeed => forwardJumpMinSpeed;
        /// <summary>获取空中水平速度向输入目标靠近的加速度。</summary>
        public float AirMovementAcceleration => airMovementAcceleration;
        /// <summary>获取空中角色朝水平运动方向旋转的最大角速度。</summary>
        public float AirTurnSpeed => airTurnSpeed;
        /// <summary>获取 Run Mixer X=2 对应的有效移动速度。</summary>
        public float RunReferenceSpeed => runReferenceSpeed;
        /// <summary>获取 Move Mixer X=1 对应的 Walk 参考速度。</summary>
        public float WalkReferenceSpeed => walkReferenceSpeed;
        /// <summary>获取 RunAsset 使用的左右脚循环参数。</summary>
        public StringAsset RunFeetParameter => runFeetParameter;
        /// <summary>获取 1 米落地动画的输入开放归一化时间。</summary>
        public float Landing1InputNormalizedTime => landing1InputNormalizedTime;
        /// <summary>获取 2 米落地动画的输入开放归一化时间。</summary>
        public float Landing2InputNormalizedTime => landing2InputNormalizedTime;
        /// <summary>获取 3 米落地动画的输入开放归一化时间。</summary>
        public float Landing3InputNormalizedTime => landing3InputNormalizedTime;

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

        /// <summary>
        /// 根据角色前向、世界移动方向和进入时脚相位选择 Run 起步动画。
        /// </summary>
        /// <param name="characterForward">角色当前世界空间前向。</param>
        /// <param name="moveDirection">Blackboard 中的世界空间移动方向。</param>
        /// <param name="leftFootAhead">进入 RunStart 时左脚是否在前。</param>
        /// <returns>对应方向和前向脚相位的 Run 起步 Transition。</returns>
        internal ClipTransition SelectRunStartTransition(
            Vector3 characterForward,
            Vector3 moveDirection,
            bool leftFootAhead)
        {
            float angle = CalculatePlanarMoveAngle(characterForward, moveDirection);
            if (float.IsNaN(angle))
                return null;

            if (angle >= -22.5f && angle <= 22.5f)
                return leftFootAhead ? runForwardLeftStart : runForwardRightStart;
            if (angle > 22.5f && angle <= 67.5f) return runRight45Start;
            if (angle > 67.5f && angle <= 112.5f) return runRight90Start;
            if (angle > 112.5f && angle <= 157.5f) return runRight135Start;
            if (angle > 157.5f) return runRight180Start;
            if (angle < -22.5f && angle >= -67.5f) return runLeft45Start;
            if (angle < -67.5f && angle >= -112.5f) return runLeft90Start;
            if (angle < -112.5f && angle >= -157.5f) return runLeft135Start;
            return runLeft180Start;
        }

        /// <summary>
        /// 校验正式 Locomotion 所需的速度、参数和起步动画配置。
        /// </summary>
        /// <exception cref="System.InvalidOperationException">任一正式配置缺失或数值关系无效时抛出。</exception>
        public void Validate()
        {
            ValidateTransitionAsset(idleTransition, "待机动画");
            ValidateTransitionAsset(moveMixerTransition, "移动混合动画");
            if (moveParameterX == null)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 未配置 Move Mixer X 参数。 ");
            if (moveParameterRotation == null)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 未配置 Move Mixer Rotation 参数。 ");
            if (walkReferenceSpeed <= 0f)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 的 Walk 动画参考速度必须大于 0。 ");
            if (runReferenceSpeed <= walkReferenceSpeed)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 的 Run 动画参考速度必须大于 Walk 动画参考速度。 ");
            if (runFeetParameter == null)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 未配置 RunFeet 参数。 ");

            ValidateTransition(forwardStart, "Walk 起步/前向");
            ValidateTransition(left45Start, "Walk 起步/左转 45°");
            ValidateTransition(left90Start, "Walk 起步/左转 90°");
            ValidateTransition(left135Start, "Walk 起步/左转 135°");
            ValidateTransition(left180Start, "Walk 起步/左转 180°");
            ValidateTransition(right45Start, "Walk 起步/右转 45°");
            ValidateTransition(right90Start, "Walk 起步/右转 90°");
            ValidateTransition(right135Start, "Walk 起步/右转 135°");
            ValidateTransition(right180Start, "Walk 起步/右转 180°");
            ValidateTransition(runForwardLeftStart, "Run 起步/前向 L0");
            ValidateTransition(runForwardRightStart, "Run 起步/前向 R0");
            ValidateTransition(runLeft45Start, "Run 起步/左转 45°");
            ValidateTransition(runLeft90Start, "Run 起步/左转 90°");
            ValidateTransition(runLeft135Start, "Run 起步/左转 135°");
            ValidateTransition(runLeft180Start, "Run 起步/左转 180°");
            ValidateTransition(runRight45Start, "Run 起步/右转 45°");
            ValidateTransition(runRight90Start, "Run 起步/右转 90°");
            ValidateTransition(runRight135Start, "Run 起步/右转 135°");
            ValidateTransition(runRight180Start, "Run 起步/右转 180°");
        }

        /// <summary>校验单个起步 Transition 的真实动画对象。</summary>
        /// <param name="transition">待校验的 Transition。</param>
        /// <param name="slotName">用于异常上下文的槽位名称。</param>
        private void ValidateTransition(ClipTransition transition, string slotName)
        {
            if (transition == null || !transition.IsValid)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 的 {slotName} 未配置有效动画。 ");
        }

        /// <summary>校验一个 Animancer TransitionAsset 已引用可播放的过渡。</summary>
        /// <param name="transition">待校验的过渡资产。</param>
        /// <param name="fieldName">用于异常上下文的配置字段名称。</param>
        private void ValidateTransitionAsset(TransitionAsset transition, string fieldName)
        {
            if (transition == null || !transition.HasTransition || !transition.IsValid)
                throw new System.InvalidOperationException(
                    $"PlayerFSMTransition '{name}' 的 {fieldName} 未配置有效动画过渡。 ");
        }

        /// <summary>计算水平前向与世界移动方向的有符号角度。</summary>
        /// <param name="characterForward">角色世界空间前向。</param>
        /// <param name="moveDirection">世界空间移动方向。</param>
        /// <returns>有效输入返回角度；方向退化时返回 NaN。</returns>
        private static float CalculatePlanarMoveAngle(Vector3 characterForward, Vector3 moveDirection)
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(characterForward, Vector3.up);
            Vector3 planarMove = Vector3.ProjectOnPlane(moveDirection, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f || planarMove.sqrMagnitude <= 0.0001f)
                return float.NaN;
            return Vector3.SignedAngle(
                planarForward.normalized,
                planarMove.normalized,
                Vector3.up);
        }

        #endregion
    }
}
