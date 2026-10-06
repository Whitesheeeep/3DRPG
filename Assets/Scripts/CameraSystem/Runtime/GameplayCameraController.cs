using System;
using System.Threading;
using Cinemachine;
using Cysharp.Threading.Tasks;
using RPG.Character;
using RPG.Character.Combat;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;

namespace RPG.CameraSystem
{
    /// <summary>协调自由与锁定两台 Virtual Camera 的输入、模式衔接和光标生命周期。</summary>
    [DefaultExecutionOrder(50), DisallowMultipleComponent]
    [InfoBox("依赖 Prefab 内绑定的 Main Camera、Brain、自由与锁定 Virtual Camera、CameraPivot、Target Group、构图代理和 CinemachineManager；自由 VCam 必须配置 Third Person Follow Body，镜头高度由 CameraPivot 的局部位置配置并作为世界轴偏移跟随玩家；目标构图范围读取锁定对象子层级 Renderer，排除粒子和拖尾。")]
    public sealed class GameplayCameraController : MonoBehaviour
    {
        #region 镜头依赖与输入配置

        // 依赖字段：Prefab 内部引用定义单一输出链及两套相机的职责边界。
        [Title("镜头组件依赖", "所有对象都来自 GameplayCamera Prefab 内部")]
        [SerializeField, Required, LabelText("主相机（Main Camera）")] private Camera outputCamera;
        [SerializeField, Required, LabelText("镜头大脑（Cinemachine Brain）")] private CinemachineBrain brain;
        [SerializeField, Required, LabelText("自由观察虚拟相机")] private CinemachineVirtualCamera freeLookVirtualCamera;
        [SerializeField, Required, LabelText("锁定虚拟相机")] private CinemachineVirtualCamera lockedVirtualCamera;
        [SerializeField, Required, LabelText("镜头旋转支点")] private Transform cameraPivot;
        [SerializeField, Required, LabelText("目标组（Target Group）")] private CinemachineTargetGroup targetGroup;
        [SerializeField, Required, LabelText("玩家构图节点")] private Transform playerFramingTarget;
        [SerializeField, Required, LabelText("目标构图节点")] private Transform lockedTargetFramingTarget;
        [SerializeField, Required, LabelText("Cinemachine 管理器")] private CinemachineManager cameraManager;

        // 自由观察输入：只配置输入到角度的换算和俯仰范围。
        [Title("自由观察输入", "鼠标按像素、摇杆按时间控制视角")]
        [SerializeField, MinValue(0f), LabelText("鼠标灵敏度（度/像素）")] private float pointerDegreesPerPixel = 0.15f;
        [SerializeField, MinValue(0f), LabelText("摇杆灵敏度（度/秒）")] private float stickDegreesPerSecond = 180f;
        [SerializeField, LabelText("反转垂直视角")] private bool invertLookY;
        [SerializeField, MinValue(-89f), MaxValue(0f), LabelText("自由模式最小俯仰角")] private float freeMinimumPitch = -70f;
        [SerializeField, MinValue(0f), MaxValue(89f), LabelText("自由模式最大俯仰角")] private float freeMaximumPitch = 70f;

        // 自由缩放：Third Person Follow 没有输入缩放与距离 Clamp，因此由控制器处理。
        [Title("自由模式滚轮缩放", "Player Map 开启时复用现有锁定目标滚轮事件")]
        [SerializeField, MinValue(0.1f), LabelText("最小距离（米）")] private float minimumFreeCameraDistance = 1.5f;
        [SerializeField, MinValue(0.1f), LabelText("最大距离（米）")] private float maximumFreeCameraDistance = 8f;
        [SerializeField, MinValue(0.01f), LabelText("每格缩放步长（米）")] private float freeZoomStep = 0.5f;
        [SerializeField, MinValue(0f), LabelText("缩放平滑时间（秒）")] private float freeZoomSmoothTime = 0.15f;

        // 锁定观察方向：构图占比、距离和位置阻尼保留在锁定 VCam。
        [Title("锁定观察方向", "Cinemachine 负责构图距离和位置阻尼")]
        [SerializeField, MinValue(-89f), MaxValue(0f), LabelText("最小俯仰角")] private float lockMinimumPitch = -30f;
        [SerializeField, MinValue(0f), MaxValue(89f), LabelText("最大俯仰角")] private float lockMaximumPitch = 60f;
        [SerializeField, MinValue(0f), LabelText("角度平滑时间（秒）")] private float lockAngleSmoothTime = 0.15f;

        #endregion

        #region 依赖字段与运行时状态

        // 自由 VCam 的 Body 属于其 Cinemachine 管线配置；Awake 从该 VCam 获取后缓存供缩放使用。
        private Cinemachine3rdPersonFollow freeLookFollow;

        // Player、输入单例及锁定系统可能在持久化相机 Prefab 之后才完成初始化。
        private PlayerController boundPlayer;
        private Transform boundPlayerRoot;
        private PlayerInputController boundInputController;
        private LockTargetSystem lockTargetSystem;
        private GameplayAbilitySystemComponent lockedTarget;
        private Transform lockedTargetRoot;
        // 存储锁定目标的所有可见 Renderer，用于对不同体型的目标进行构图；粒子、拖尾和不可见对象不参与构图。
        private Renderer[] lockedTargetRenderers = System.Array.Empty<Renderer>();
        private CancellationTokenSource playerBindingCancellationSource;

        // 镜头跟随：从 Prefab 的 CameraPivot 局部位置缓存固定世界轴偏移，不随角色朝向旋转。
        private Vector3 cameraPivotWorldOffset;

        // 构图节点偏移由 Prefab 配置，并以 CharacterRoot 世界坐标为基准应用。
        private Vector3 playerFramingWorldOffset;
        private Vector3 targetFallbackWorldOffset;
        private float desiredFreeCameraDistance;
        private float freeCameraDistanceVelocity;
        private float yaw;
        private float pitch;
        private float lockYawVelocity;
        private float lockPitchVelocity;
        private bool discardNextLookSample;
        private E_GameplayCameraMode currentMode = E_GameplayCameraMode.FreeLook;

        // 光标状态只在相机首次接管时快照，释放时还原进入玩法前的状态。
        private bool applicationHasFocus = true;
        private bool cursorStateCaptured;
        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;

        #endregion

        #region 状态属性

        /// <summary>获取当前由输入驱动或由锁定构图驱动的镜头模式。</summary>
        public E_GameplayCameraMode CurrentMode => currentMode;

        /// <summary>获取自由观察镜头，供技能编辑器读取稳定的参考 FOV。</summary>
        public CinemachineVirtualCamera FreeLookVirtualCamera => freeLookVirtualCamera;

        #endregion

        #region Unity 生命周期

        /// <summary>校验 Prefab 依赖，缓存支点世界轴偏移，从自由 VCam 获取 Third Person Follow 并初始化镜头状态。</summary>
        /// <exception cref="System.InvalidOperationException">Prefab 缺少输出镜头、构图目标或自由 VCam 的 Third Person Follow 时抛出。</exception>
        private void Awake()
        {
            if (outputCamera == null || brain == null || freeLookVirtualCamera == null ||
                lockedVirtualCamera == null || cameraPivot == null ||
                targetGroup == null || playerFramingTarget == null || lockedTargetFramingTarget == null ||
                cameraManager == null)
            {
                string error = $"[GameplayCameraController] '{name}' 的双 VCam、Brain、构图组或代理引用不完整。";
                Debug.LogError(error, this);
                throw new System.InvalidOperationException(error);
            }

            // Body 配置由自由 VCam 自己持有，避免 Prefab Controller 重复保存同一组件引用。
            freeLookFollow = freeLookVirtualCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
            if (freeLookFollow == null)
            {
                string error = $"[GameplayCameraController] '{name}' 的自由 VCam '{freeLookVirtualCamera.name}' 缺少 Third Person Follow Body。";
                Debug.LogError(error, this);
                throw new System.InvalidOperationException(error);
            }
            Debug.Log($"[GameplayCameraController] 已从自由 VCam 获取 Third Person Follow Body，camera={freeLookVirtualCamera.name}。", this);

            if (brain.gameObject != outputCamera.gameObject)
            {
                string error = $"[GameplayCameraController] '{name}' 的 Brain 与 Main Camera 必须位于同一对象。";
                Debug.LogError(error, this);
                throw new System.InvalidOperationException(error);
            }

            if (targetGroup.m_Targets == null || targetGroup.m_Targets.Length != 2)
            {
                string error = $"[GameplayCameraController] '{name}' 的 Target Group 必须包含玩家与目标两个固定成员。";
                Debug.LogError(error, this);
                throw new System.InvalidOperationException(error);
            }

            // 局部位置是 Prefab 配置入口；后续把该偏移加到角色世界位置，避免随角色转向绕轴偏移。
            cameraPivotWorldOffset = cameraPivot.localPosition;
            playerFramingWorldOffset = playerFramingTarget.localPosition;
            targetFallbackWorldOffset = lockedTargetFramingTarget.localPosition;
            desiredFreeCameraDistance = Mathf.Clamp(
                freeLookFollow.CameraDistance,
                minimumFreeCameraDistance,
                maximumFreeCameraDistance);
            freeLookFollow.CameraDistance = desiredFreeCameraDistance;
            ReadOrientation(freeLookVirtualCamera.transform.rotation, out yaw, out pitch);
            SetCameraPriorities(false);
        }

        /// <summary>确认唯一镜头实例后订阅目标与输入实例变化，并绑定或异步等待 Player。</summary>
        private void OnEnable()
        {
            if (cameraManager != CinemachineManager.Instance)
            {
                Debug.LogWarning($"[GameplayCameraController] 忽略重复 GameplayCamera 实例 '{name}'。", this);
                enabled = false;
                return;
            }

            lockTargetSystem = LockTargetSystem.Instance;
            lockTargetSystem.LockTargetChanged += HandleLockTargetChanged;
            PlayerInputController.InstanceChanged += HandleInputControllerInstanceChanged;
            if (!TryBindPlayer())
                StartPlayerBindingWait();
            Debug.Log($"[GameplayCameraController] 已订阅锁定与输入实例变化，playerBound={boundPlayer != null}。", this);
        }

        /// <summary>在 Brain 更新前同步代理、目标组、输入视角和锁定观察方向。</summary>
        private void LateUpdate()
        {
            if (!TryEnsurePlayerBinding())
            {
                UpdateCursorOwnership(false);
                return;
            }

            PlayerInputController input = boundInputController;
            bool inputAvailable = input != null && input.isActiveAndEnabled && !input.IsGameplayInputBlocked;
            UpdateCursorOwnership(applicationHasFocus && inputAvailable);

            // 先按固定世界轴偏移抬高 Follow 起点，使第三人称避障从身体上方开始检测。
            cameraPivot.position = boundPlayerRoot.position + cameraPivotWorldOffset;
            playerFramingTarget.position = boundPlayerRoot.position + playerFramingWorldOffset;

            bool hasUsableLockedTarget = currentMode == E_GameplayCameraMode.Locked &&
                                         RefreshLockedTargetRootCache();
            UpdateTargetFramingTarget();
            // 先刷新代理成员与组中心，再让更晚执行的 Brain 使用本帧构图数据。
            targetGroup.DoUpdate();

            if (Time.timeScale > 0f)
            {
                if (currentMode == E_GameplayCameraMode.Locked)
                {
                    if (hasUsableLockedTarget)
                        UpdateLockedOrientation();
                }
                else if (applicationHasFocus && inputAvailable)
                    UpdateFreeOrientation(input);
            }

            Quaternion cameraRotation = Quaternion.Euler(-pitch, yaw, 0f);
            cameraPivot.rotation = cameraRotation;
            freeLookVirtualCamera.transform.rotation = cameraRotation;
            lockedVirtualCamera.transform.rotation = cameraRotation;
        }

        /// <summary>失焦时释放光标控制；恢复焦点时由输入控制器丢弃首笔鼠标 Delta。</summary>
        /// <param name="hasFocus">应用是否处于前台。</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (applicationHasFocus == hasFocus) return;
            applicationHasFocus = hasFocus;
            UpdateCursorOwnership(hasFocus && boundPlayer != null && boundInputController != null &&
                                  !boundInputController.IsGameplayInputBlocked);
            Debug.Log($"[GameplayCameraController] 应用焦点变化，hasFocus={hasFocus}。", this);
        }

        /// <summary>取消 Player 等待、对称解除事件和输入订阅，并恢复接管前的光标状态。</summary>
        private void OnDisable()
        {
            if (lockTargetSystem != null)
                lockTargetSystem.LockTargetChanged -= HandleLockTargetChanged;
            PlayerInputController.InstanceChanged -= HandleInputControllerInstanceChanged;
            CancelPlayerBindingWait();
            UnbindInputController();
            boundPlayer = null;
            boundPlayerRoot = null;
            ClearCameraTargetState(true);
            UpdateCursorOwnership(false);
            Debug.Log("[GameplayCameraController] 已取消 Player 等待、解除目标与输入订阅并恢复光标。", this);
        }

        /// <summary>确保对象销毁阶段不会遗留被锁定或隐藏的光标。</summary>
        private void OnDestroy()
        {
            UpdateCursorOwnership(false);
        }

        /// <summary>限制输入参数，并保持自由缩放和俯仰区间有效。</summary>
        private void OnValidate()
        {
            minimumFreeCameraDistance = Mathf.Max(0.1f, minimumFreeCameraDistance);
            maximumFreeCameraDistance = Mathf.Max(minimumFreeCameraDistance, maximumFreeCameraDistance);
            freeZoomStep = Mathf.Max(0.01f, freeZoomStep);
            freeMinimumPitch = Mathf.Clamp(freeMinimumPitch, -89f, 0f);
            freeMaximumPitch = Mathf.Clamp(freeMaximumPitch, 0f, 89f);
            freeMaximumPitch = Mathf.Max(freeMinimumPitch, freeMaximumPitch);
            freeZoomSmoothTime = Mathf.Max(0f, freeZoomSmoothTime);
            lockMinimumPitch = Mathf.Clamp(lockMinimumPitch, -89f, 0f);
            lockMaximumPitch = Mathf.Clamp(lockMaximumPitch, 0f, 89f);
            lockMaximumPitch = Mathf.Max(lockMinimumPitch, lockMaximumPitch);
            lockAngleSmoothTime = Mathf.Max(0f, lockAngleSmoothTime);
        }

        #endregion

        #region Player 与输入绑定

        // Player 查找：持久化镜头可能先于角色队伍创建；每个启用周期只保留一个可取消等待。
        /// <summary>创建不受 timeScale 影响的 Player 绑定重试任务。</summary>
        private void StartPlayerBindingWait()
        {
            if (playerBindingCancellationSource != null) return;

            playerBindingCancellationSource = new CancellationTokenSource();
            WaitForPlayerBindingAsync(playerBindingCancellationSource)
                .Forget(HandlePlayerBindingWaitException);
            Debug.Log("[GameplayCameraController] Player 尚未就绪，已启动实时重试等待。", this);
        }

        /// <summary>每隔 0.25 秒检查 Player 是否已完成初始化，并在绑定或取消后释放本任务令牌。</summary>
        /// <param name="cancellationSource">本次等待独占的取消源。</param>
        private async UniTask WaitForPlayerBindingAsync(CancellationTokenSource cancellationSource)
        {
            CancellationToken cancellationToken = cancellationSource.Token;
            try
            {
                while (isActiveAndEnabled && !cancellationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(0.25f), DelayType.Realtime,
                        PlayerLoopTiming.Update, cancellationToken);
                    if (!isActiveAndEnabled || cancellationToken.IsCancellationRequested)
                        break;
                    if (TryBindPlayer())
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 组件停用属于预期生命周期结束；不把取消记录为异步故障。
            }
            finally
            {
                bool wasCurrentWait = ReferenceEquals(playerBindingCancellationSource, cancellationSource);
                if (wasCurrentWait)
                    playerBindingCancellationSource = null;
                cancellationSource.Dispose();
            }
        }

        /// <summary>取消当前 Player 等待并立即释放槽位，让后续启用周期可创建新任务。</summary>
        private void CancelPlayerBindingWait()
        {
            CancellationTokenSource cancellationSource = playerBindingCancellationSource;
            if (cancellationSource == null) return;

            playerBindingCancellationSource = null;
            cancellationSource.Cancel();
            Debug.Log("[GameplayCameraController] 已请求取消 Player 绑定等待。", this);
        }

        /// <summary>记录 Player 绑定等待中未被生命周期取消的异步异常。</summary>
        /// <param name="exception">绑定流程抛出的异常。</param>
        private void HandlePlayerBindingWaitException(Exception exception)
        {
            Debug.LogException(exception, this);
        }

        /// <summary>绑定当前有效 Player，并从其世界根节点初始化镜头跟随支点。</summary>
        /// <returns>本次已有有效绑定或成功绑定时返回 true。</returns>
        private bool TryBindPlayer()
        {
            if (boundPlayer != null && boundPlayer.isActiveAndEnabled && boundPlayerRoot != null)
                return true;

            PlayerController player = PlayerController.Instance;
            if (player == null || !player.isActiveAndEnabled || player.CharacterRoot == null ||
                player.CharacterManager == null || !player.CharacterManager.IsReady)
                return false;

            boundPlayer = player;
            boundPlayerRoot = player.CharacterRoot;
            cameraPivot.position = boundPlayerRoot.position + cameraPivotWorldOffset;
            playerFramingTarget.position = boundPlayerRoot.position + playerFramingWorldOffset;
            SynchronizeInputController(PlayerInputController.Instance);
            ApplyLockSystemTarget(lockTargetSystem.CurrentTarget);
            Debug.Log($"[GameplayCameraController] 已绑定 Player，player={player.name}。", this);
            return true;
        }

        /// <summary>处理 Player 销毁与重建，清理镜头侧状态并启动后续低频绑定。</summary>
        /// <returns>当前 Player 仍可供镜头跟随时返回 true。</returns>
        private bool TryEnsurePlayerBinding()
        {
            if (boundPlayer != null && boundPlayer.isActiveAndEnabled && boundPlayerRoot != null)
                return true;

            bool hadPlayerBinding = !ReferenceEquals(boundPlayer, null) ||
                                    !ReferenceEquals(boundPlayerRoot, null);
            if (hadPlayerBinding)
            {
                UnbindInputController();
                boundPlayer = null;
                boundPlayerRoot = null;
                ClearCameraTargetState(true);
                UpdateCursorOwnership(false);
                Debug.Log("[GameplayCameraController] Player 已失效，已清理镜头绑定并等待重建。", this);
            }
            StartPlayerBindingWait();
            return false;
        }

        // 输入订阅：PlayerInputController.InstanceChanged 驱动替换，绑定 Player 后才接收滚轮。
        /// <summary>将输入事件订阅同步到当前实例，避免未绑定 Player 时占用滚轮事件。</summary>
        /// <param name="currentInput">当前 PlayerInputController 单例。</param>
        private void SynchronizeInputController(PlayerInputController currentInput)
        {
            bool playerIsUsable = boundPlayer != null && boundPlayer.isActiveAndEnabled && boundPlayerRoot != null;
            if (!playerIsUsable)
            {
                UnbindInputController();
                return;
            }

            if (ReferenceEquals(boundInputController, currentInput)) return;
            UnbindInputController();
            if (currentInput == null || !currentInput.isActiveAndEnabled) return;

            boundInputController = currentInput;
            boundInputController.ImmediateInputPerformed += HandleImmediateInputPerformed;
            Debug.Log("[GameplayCameraController] 已订阅 Player Map 的镜头滚轮事件。", this);
        }

        /// <summary>输入单例变化时更新当前 Player 的滚轮事件订阅。</summary>
        /// <param name="currentInput">新建或销毁后的当前输入单例。</param>
        private void HandleInputControllerInstanceChanged(PlayerInputController currentInput)
        {
            SynchronizeInputController(currentInput);
        }

        /// <summary>注销当前输入实例，避免 Player 或控制器重建后重复处理滚轮。</summary>
        private void UnbindInputController()
        {
            if (ReferenceEquals(boundInputController, null)) return;
            boundInputController.ImmediateInputPerformed -= HandleImmediateInputPerformed;
            boundInputController = null;
            Debug.Log("[GameplayCameraController] 已注销 Player Map 的镜头滚轮事件。", this);
        }

        #endregion

        #region 自由观察与滚轮缩放

        // 自由观察：Pointer Delta 按像素转换，手柄值按时间积分。
        /// <summary>按 Pointer 像素增量或手柄角速度推进自由观察方向。</summary>
        /// <param name="input">已经由现有 Player Map 门禁采样的输入。</param>
        private void UpdateFreeOrientation(PlayerInputController input)
        {
            Vector2 look = discardNextLookSample ? Vector2.zero : input.LookInput;
            discardNextLookSample = false;
            float scale = input.LookInputIsRate
                ? stickDegreesPerSecond * Time.deltaTime
                : pointerDegreesPerPixel;
            // 计算俯仰角时反转 Y 轴输入，Yaw 角度在 -180~180 范围内循环。
            yaw = Mathf.Repeat(yaw + look.x * scale + 180f, 360f) - 180f;
            pitch = Mathf.Clamp(pitch + (invertLookY ? -look.y : look.y) * scale,
                freeMinimumPitch, freeMaximumPitch);

            freeLookFollow.CameraDistance = Mathf.SmoothDamp(
                freeLookFollow.CameraDistance,
                desiredFreeCameraDistance,
                ref freeCameraDistanceVelocity,
                freeZoomSmoothTime,
                Mathf.Infinity,
                Time.deltaTime);
        }

        // 滚轮处理：锁定时只由 PlayerController 切换目标。
        /// <summary>锁定期间滚轮继续由 PlayerController 切换目标；自由模式向上拉近、向下拉远。</summary>
        /// <param name="inputType">Player Map 发出的滚轮输入类型。</param>
        private void HandleImmediateInputPerformed(E_PlayerInputType inputType)
        {
            if (inputType != E_PlayerInputType.LockPrevious && inputType != E_PlayerInputType.LockNext)
                return;
            if (currentMode != E_GameplayCameraMode.FreeLook || Time.timeScale <= 0f)
                return;

            // Camera Distance 越大镜头越远；LockPrevious 代表向下滚轮，需增加距离。
            float direction = inputType == E_PlayerInputType.LockPrevious ? 1f : -1f;
            desiredFreeCameraDistance = Mathf.Clamp(
                desiredFreeCameraDistance + direction * freeZoomStep,
                minimumFreeCameraDistance,
                maximumFreeCameraDistance);
            Debug.Log(
                $"[GameplayCameraController] 自由镜头滚轮缩放，direction={direction}，targetDistance={desiredFreeCameraDistance:0.00}m。",
                this);
        }

        #endregion

        #region 锁定构图与目标缓存

        // 锁定方向：距离与屏幕占比完全由 Framing Transposer 根据 Target Group 求解。
        /// <summary>朝向玩家与目标构图范围中心，并由 Framing Transposer 决定镜头距离。</summary>
        private void UpdateLockedOrientation()
        {
            Vector3 playerFocus = playerFramingTarget.position;
            Vector3 targetFocus = lockedTargetFramingTarget.position;
            Vector3 horizontalOffset = Vector3.ProjectOnPlane(targetFocus - playerFocus, Vector3.up);
            if (horizontalOffset.sqrMagnitude > 0.0001f)
                yaw = Mathf.SmoothDampAngle(yaw,
                    Mathf.Atan2(horizontalOffset.x, horizontalOffset.z) * Mathf.Rad2Deg,
                    ref lockYawVelocity, lockAngleSmoothTime, Mathf.Infinity, Time.deltaTime);

            float horizontalDistance = horizontalOffset.magnitude;
            float targetPitch = Mathf.Clamp(
                Mathf.Atan2(targetFocus.y - playerFocus.y, Mathf.Max(0.01f, horizontalDistance)) * Mathf.Rad2Deg,
                lockMinimumPitch,
                lockMaximumPitch);
            pitch = Mathf.Clamp(
                Mathf.SmoothDamp(pitch, targetPitch, ref lockPitchVelocity,
                    lockAngleSmoothTime, Mathf.Infinity, Time.deltaTime),
                lockMinimumPitch,
                lockMaximumPitch);
        }

        // Target Group 数据：用缓存 Renderer Bounds 更新目标中心和球形半径。
        /// <summary>以缓存的普通 Renderer 更新 Target Group 目标代理，供 Cinemachine 原生构图。</summary>
        private void UpdateTargetFramingTarget()
        {
            CinemachineTargetGroup.Target[] members = targetGroup.m_Targets;
            CinemachineTargetGroup.Target targetMember = members[1];
            targetMember.target = lockedTargetFramingTarget;
            targetMember.weight = lockedTargetRoot != null ? 1f : 0f;

            if (lockedTargetRoot == null)
            {
                lockedTargetFramingTarget.position = boundPlayerRoot.position + targetFallbackWorldOffset;
                targetMember.radius = 0.5f;
                members[1] = targetMember;
                return;
            }

            if (TryGetTargetBounds(out Bounds targetBounds))
            {
                lockedTargetFramingTarget.position = targetBounds.center;
                targetMember.radius = Mathf.Max(0.01f, targetBounds.extents.magnitude);
            }
            else
            {
                lockedTargetFramingTarget.position = lockedTargetRoot.position + targetFallbackWorldOffset;
                targetMember.radius = 0.5f;
            }
            members[1] = targetMember;
        }

        /// <summary>合并锁定目标有效 Renderer 的世界包围盒。</summary>
        /// <param name="bounds">找到 Renderer 时返回合并后的世界空间 Bounds。</param>
        /// <returns>仍有效的普通 Renderer 至少有一个时返回 true。</returns>
        private bool TryGetTargetBounds(out Bounds bounds)
        {
            bool hasBounds = false;
            bounds = default;
            for (int rendererIndex = 0; rendererIndex < lockedTargetRenderers.Length; rendererIndex++)
            {
                Renderer renderer = lockedTargetRenderers[rendererIndex];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        // 锁定目标生命周期：变化时刷新 Renderer 快照，且只在模式边界切换 VCam。
        /// <summary>锁定变化时同步目标组成员、构图缓存、观察方向和 VCam 优先级。</summary>
        /// <param name="previousTarget">变化前的目标 ASC。</param>
        /// <param name="currentTarget">变化后的目标 ASC。</param>
        /// <param name="reason">锁定状态变化原因。</param>
        private void HandleLockTargetChanged(
            GameplayAbilitySystemComponent previousTarget,
            GameplayAbilitySystemComponent currentTarget,
            E_LockTargetChangeReason reason)
        {
            if (boundPlayer == null || !boundPlayer.isActiveAndEnabled || boundPlayerRoot == null)
            {
                Debug.Log($"[GameplayCameraController] 收到锁定变化但 Player 尚未绑定，稍后同步当前目标，reason={reason}。", this);
                return;
            }

            ApplyLockSystemTarget(currentTarget);
            Debug.Log(
                $"[GameplayCameraController] 锁定镜头目标变化，previous={GetTargetName(previousTarget)}，" +
                $"current={GetTargetName(currentTarget)}，reason={reason}。", this);
        }

        /// <summary>应用 LockTargetSystem 的最新目标；相同目标和 Root 的重复同步不会重建缓存或切换镜头。</summary>
        /// <param name="target">新锁定目标，为空时切回自由观察。</param>
        private void ApplyLockSystemTarget(GameplayAbilitySystemComponent target)
        {
            if (!TryGetUsableTargetRoot(target, out Transform targetRoot))
            {
                ClearCameraTargetState(true);
                return;
            }

            if (ReferenceEquals(lockedTarget, target) && ReferenceEquals(lockedTargetRoot, targetRoot))
                return;

            bool enteredLock = currentMode != E_GameplayCameraMode.Locked;
            lockedTarget = target;
            CacheLockedTargetRenderers(targetRoot);

            if (enteredLock)
            {
                SynchronizeOrientationFromBrain();
                freeCameraDistanceVelocity = 0f;
                lockYawVelocity = 0f;
                lockPitchVelocity = 0f;
            }
            currentMode = E_GameplayCameraMode.Locked;
            if (enteredLock) SetCameraPriorities(true);
            Debug.Log(
                $"[GameplayCameraController] 已应用锁定系统目标并缓存构图范围，target={target.name}，rendererCount={lockedTargetRenderers.Length}。",
                this);
        }

        /// <summary>检查当前锁定目标的 Owner Root 是否仍有效，并仅在 Root 替换时刷新构图缓存。</summary>
        /// <returns>目标 Root 可用于本帧构图时返回 true。</returns>
        private bool RefreshLockedTargetRootCache()
        {
            if (!TryGetUsableTargetRoot(lockedTarget, out Transform currentTargetRoot))
            {
                if (!ReferenceEquals(lockedTargetRoot, null) || lockedTargetRenderers.Length > 0)
                {
                    lockedTargetRoot = null;
                    lockedTargetRenderers = Array.Empty<Renderer>();
                    Debug.LogWarning("[GameplayCameraController] 锁定目标 Root 暂不可用，等待 LockTargetSystem 更新状态。", this);
                }
                return false;
            }

            if (ReferenceEquals(lockedTargetRoot, currentTargetRoot))
                return true;

            // Root 替换时刷新构图缓存，避免锁定目标的子层级被替换后仍使用旧 Renderer。
            CacheLockedTargetRenderers(currentTargetRoot);
            Debug.Log($"[GameplayCameraController] 锁定目标 Root 已替换，刷新构图缓存，target={GetTargetName(lockedTarget)}。", this);
            return true;
        }

        /// <summary>为目标 Root 重建普通 Renderer 快照，供逐帧 Bounds 合并使用。</summary>
        /// <param name="targetRoot">锁定 ASC Owner 当前的世界根节点。</param>
        private void CacheLockedTargetRenderers(Transform targetRoot)
        {
            lockedTargetRoot = targetRoot;
            Renderer[] foundRenderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            int validRendererCount = 0;
            for (int rendererIndex = 0; rendererIndex < foundRenderers.Length; rendererIndex++)
            {
                if (foundRenderers[rendererIndex] is ParticleSystemRenderer or TrailRenderer) continue;
                validRendererCount++;
            }

            lockedTargetRenderers = new Renderer[validRendererCount];
            int targetRendererIndex = 0;
            for (int rendererIndex = 0; rendererIndex < foundRenderers.Length; rendererIndex++)
            {
                if (foundRenderers[rendererIndex] is ParticleSystemRenderer or TrailRenderer) continue;
                lockedTargetRenderers[targetRendererIndex++] = foundRenderers[rendererIndex];
            }
        }

        /// <summary>清理镜头侧锁定缓存和 Target Group 状态，不修改 LockTargetSystem 的业务目标。</summary>
        /// <param name="synchronizeOrientation">离开锁定镜头时是否接续 Brain 当前的原始朝向。</param>
        private void ClearCameraTargetState(bool synchronizeOrientation)
        {
            bool wasLocked = currentMode == E_GameplayCameraMode.Locked;
            bool hadTargetState = !ReferenceEquals(lockedTarget, null) ||
                                  !ReferenceEquals(lockedTargetRoot, null) ||
                                  lockedTargetRenderers.Length > 0;
            if (wasLocked && synchronizeOrientation)
                SynchronizeOrientationFromBrain();

            lockedTarget = null;
            lockedTargetRoot = null;
            lockedTargetRenderers = Array.Empty<Renderer>();
            targetGroup.m_Targets[1].weight = 0f;
            SetCameraPriorities(false);

            if (hadTargetState || wasLocked)
            {
                Debug.Log("[GameplayCameraController] 已清理镜头侧锁定目标与构图缓存。", this);
            }
        }

        /// <summary>检查 ASC、Owner 和世界根节点仍可安全用于构图。</summary>
        /// <param name="target">锁定目标 ASC。</param>
        /// <param name="root">有效时返回 ASC Owner 的世界根节点。</param>
        /// <returns>目标对象及层级仍处于活动状态时返回 true。</returns>
        private static bool TryGetUsableTargetRoot(
            GameplayAbilitySystemComponent target,
            out Transform root)
        {
            root = null;
            if (target == null || !target.isActiveAndEnabled || !target.gameObject.activeInHierarchy)
                return false;

            var owner = target.Owner;
            if (owner == null || owner is UnityEngine.Object ownerObject && ownerObject == null)
                return false;
            root = owner.RootTransform;
            return root != null && root.gameObject.activeInHierarchy;
        }

        // 朝向同步：模式切换读取 Brain 原始状态，避免将最终 Shake 作为输入。
        /// <summary>把输出 Brain 当前的原始方向同步到两个 VCam，避免混入最终 Shake 修饰。</summary>
        private void SynchronizeOrientationFromBrain()
        {
            Quaternion rawOrientation = brain.CurrentCameraState.RawOrientation;
            ReadOrientation(rawOrientation, out yaw, out pitch);
            freeLookVirtualCamera.transform.rotation = rawOrientation;
            lockedVirtualCamera.transform.rotation = rawOrientation;
        }

        /// <summary>从四元数提取观察 yaw 和相对水平面的俯仰角。</summary>
        /// <param name="rotation">世界空间观察旋转。</param>
        /// <param name="resolvedYaw">返回水平观察角。</param>
        /// <param name="resolvedPitch">返回相对水平面的观察仰角。</param>
        private static void ReadOrientation(Quaternion rotation, out float resolvedYaw, out float resolvedPitch)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            resolvedYaw = horizontalForward.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(horizontalForward.x, horizontalForward.z) * Mathf.Rad2Deg
                : rotation.eulerAngles.y;
            resolvedPitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>按模式切换两台 VCam 的优先级，交由 Brain 使用 Prefab Blend 过渡。</summary>
        /// <param name="locked">是否让锁定镜头成为当前高优先级镜头。</param>
        private void SetCameraPriorities(bool locked)
        {
            currentMode = locked ? E_GameplayCameraMode.Locked : E_GameplayCameraMode.FreeLook;
            freeLookVirtualCamera.Priority = locked ? 10 : 20;
            lockedVirtualCamera.Priority = locked ? 20 : 10;
        }

        /// <summary>生成锁定事件诊断使用的稳定目标名称。</summary>
        /// <param name="target">目标 ASC，可能为空或已销毁。</param>
        /// <returns>目标名称或 None。</returns>
        private static string GetTargetName(GameplayAbilitySystemComponent target) =>
            target == null ? "None" : target.name;

        #endregion

        #region 光标生命周期

        /// <summary>玩法输入有效时锁定并隐藏光标；窗口、失焦和停用时恢复接管前状态。</summary>
        /// <param name="shouldOwnCursor">当前是否应该接管光标。</param>
        private void UpdateCursorOwnership(bool shouldOwnCursor)
        {
            if (shouldOwnCursor)
            {
                if (!cursorStateCaptured)
                {
                    previousCursorLockState = Cursor.lockState;
                    previousCursorVisible = Cursor.visible;
                    cursorStateCaptured = true;
                    discardNextLookSample = true;
                    Debug.Log(
                        $"[GameplayCameraController] 已接管光标，previousLock={previousCursorLockState}，" +
                        $"previousVisible={previousCursorVisible}。", this);
                }

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            if (!cursorStateCaptured) return;
            Cursor.lockState = previousCursorLockState;
            Cursor.visible = previousCursorVisible;
            cursorStateCaptured = false;
            Debug.Log("[GameplayCameraController] 已恢复接管前的光标状态。", this);
        }

        #endregion
    }
}
