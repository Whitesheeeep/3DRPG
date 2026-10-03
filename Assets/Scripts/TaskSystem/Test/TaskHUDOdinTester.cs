#if UNITY_EDITOR
using RPG.Game;
using RPG.SaveSystem;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.UIModule;

namespace RPG.TaskSystemNS.Test
{
    /// <summary>把 Cube (1) 的测试位置自动绑定到当前追踪的测试任务，并提供多目标接取入口。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 Inspector 显式指定两阶段任务、多目标任务与 Cube (1) Transform；追踪任一测试任务时自动提供临时 HUD 目标位置。")]
    public sealed class TaskHUDOdinTester : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private TaskDefinition testTaskDefinition;
        [SerializeField, Required] private TaskDefinition multiObjectiveTaskDefinition;
        [SerializeField, Required] private Transform targetTransform;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.8f, 0f);

        // 依赖字段：测试期间读取任务、存档与 HUD 窗口事实，所有事件回调均在 Unity 主线程。
        private TaskSystem taskSystem;
        private SaveManager saveManager;
        private UIManager uiManager;
        private IUnRegister trackedTaskUnregister;
        private IUnRegister taskStateUnregister;
        private HUDWindow boundHudWindow;
        #endregion

        #region 生命周期状态
        private TaskId navigationTaskId;
        private bool hasStarted;
        private bool isSubscribed;
        #endregion

        #region Odin 测试操作
        /// <summary>将已接取的两阶段 Cube (1) 对话任务设为追踪任务。</summary>
        [Button("追踪两阶段测试任务")]
        private void TrackTestTask()
        {
            if (!TryGetTaskSystem(out TaskSystem system) || !ValidateTaskReference(testTaskDefinition, "两阶段"))
                return;

            if (!system.TrySetTrackedTask(testTaskDefinition.TaskId))
            {
                Debug.LogWarning(
                    $"[TaskHUDOdinTester] 两阶段测试任务尚未接取，无法追踪；taskId={testTaskDefinition.TaskId}。",
                    this);
                return;
            }

            Debug.Log($"[TaskHUDOdinTester] 已追踪两阶段测试任务，taskId={testTaskDefinition.TaskId}。", this);
        }

        /// <summary>通过真实任务 API 接取未完成的多目标任务并设置为追踪。</summary>
        [Button("接取并追踪多目标任务", ButtonSizes.Large)]
        private void AcceptAndTrackMultiObjectiveTask()
        {
            if (!TryGetTaskSystem(out TaskSystem system) ||
                !ValidateTaskReference(multiObjectiveTaskDefinition, "多目标"))
                return;

            TaskId taskId = multiObjectiveTaskDefinition.TaskId;
            if (system.IsTaskCompleted(taskId))
            {
                Debug.LogWarning(
                    $"[TaskHUDOdinTester] 多目标任务已完成；测试入口不会重置玩家任务数据，taskId={taskId}。",
                    this);
                return;
            }

            if (!system.TryGetActiveRecord(taskId, out _))
            {
                TaskAvailabilityResult availability = system.GetAvailability(taskId);
                if (availability.Status != TaskAvailabilityStatus.Available)
                {
                    string reason = availability.Reasons.Count > 0
                        ? availability.Reasons[0].Message
                        : availability.Status.ToString();
                    Debug.LogWarning(
                        $"[TaskHUDOdinTester] 多目标任务当前不可接取，taskId={taskId}, reason={reason}。",
                        this);
                    return;
                }

                TaskAcceptResult acceptResult = system.TryAcceptTask(taskId, E_TaskAcceptSource.Test);
                if (!acceptResult.Succeeded)
                {
                    Debug.LogWarning(
                        $"[TaskHUDOdinTester] 多目标任务接取失败，taskId={taskId}, failure={acceptResult.Failure}。",
                        this);
                    return;
                }
            }

            if (!system.TrySetTrackedTask(taskId))
                throw new System.InvalidOperationException($"[TaskHUDOdinTester] 已接取任务无法设置追踪：{taskId}。");

            Debug.Log($"[TaskHUDOdinTester] 多目标任务已接取并追踪，taskId={taskId}。", this);
        }
        #endregion

        #region Unity 生命周期
        /// <summary>在测试场景开始运行后连接现有任务、存档与窗口事件。</summary>
        private void Start()
        {
            if (!Application.isPlaying)
                return;

            taskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
            saveManager = GameArchitecture.Interface.GetManager<SaveManager>();
            uiManager = UIManager.Instance;
            hasStarted = true;
            SubscribeToRuntimeEvents();
            SynchronizeNavigationTarget();
        }

        /// <summary>组件在 Play Mode 中重新启用时恢复事件监听并重读当前追踪事实。</summary>
        private void OnEnable()
        {
            if (Application.isPlaying && hasStarted)
            {
                SubscribeToRuntimeEvents();
                SynchronizeNavigationTarget();
            }
        }

        /// <summary>组件禁用或场景卸载时解除监听并移除本测试组件提供的导航目标。</summary>
        private void OnDisable()
        {
            UnsubscribeFromRuntimeEvents();
            ClearBoundNavigationTarget();
        }
        #endregion

        #region 事件订阅与追踪同步
        /// <summary>订阅任务追踪、生命周期、HUD 打开及成功读档事实。</summary>
        private void SubscribeToRuntimeEvents()
        {
            if (isSubscribed)
                return;

            trackedTaskUnregister = EventSystem.Register_Type<TaskTrackedChangedEventArgs>(
                typeof(TaskTrackedChangedEventArgs), HandleTaskTrackedChanged);
            taskStateUnregister = EventSystem.Register_Type<TaskStateChangedEventArgs>(
                typeof(TaskStateChangedEventArgs), HandleTaskStateChanged);
            uiManager.WindowOpened += HandleWindowOpened;
            saveManager.OperationCompleted += HandleSaveOperationCompleted;
            isSubscribed = true;
            Debug.Log("[TaskHUDOdinTester] 已订阅追踪、任务状态、HUD 打开和读档事件。", this);
        }

        /// <summary>释放测试期间登记的任务、窗口和存档事件订阅。</summary>
        private void UnsubscribeFromRuntimeEvents()
        {
            if (!isSubscribed)
                return;

            trackedTaskUnregister?.UnRegister();
            taskStateUnregister?.UnRegister();
            uiManager.WindowOpened -= HandleWindowOpened;
            saveManager.OperationCompleted -= HandleSaveOperationCompleted;
            trackedTaskUnregister = null;
            taskStateUnregister = null;
            isSubscribed = false;
            Debug.Log("[TaskHUDOdinTester] 已解除测试任务导航同步订阅。", this);
        }

        /// <summary>追踪任务变化后同步 Cube (1) 的临时 HUD 导航目标。</summary>
        /// <param name="eventArgs">任务追踪变化事实。</param>
        private void HandleTaskTrackedChanged(TaskTrackedChangedEventArgs eventArgs)
        {
            SynchronizeNavigationTarget();
        }

        /// <summary>任务进入待领奖状态时移除测试世界标记。</summary>
        /// <param name="eventArgs">任务状态变化事实。</param>
        private void HandleTaskStateChanged(TaskStateChangedEventArgs eventArgs)
        {
            SynchronizeNavigationTarget();
        }

        /// <summary>HUD 稳定打开后将仍然有效的追踪测试目标重新绑定到新窗口实例。</summary>
        /// <param name="snapshot">稳定打开的窗口快照。</param>
        private void HandleWindowOpened(UIWindowSnapshot snapshot)
        {
            if (snapshot.WindowName == nameof(HUDWindow) && snapshot.Visible)
                SynchronizeNavigationTarget();
        }

        /// <summary>全部模块读档成功后根据恢复的追踪任务重设测试导航目标。</summary>
        /// <param name="completion">完成的存档操作结果。</param>
        private void HandleSaveOperationCompleted(SaveOperationCompleted completion)
        {
            if (completion.Kind == SaveOperationKind.Load && completion.IsSuccess)
                SynchronizeNavigationTarget();
        }

        /// <summary>只为受支持且进行中的测试任务设置 Cube (1) 目标。</summary>
        private void SynchronizeNavigationTarget()
        {
            TaskId nextNavigationTaskId = default;
            TaskId trackedTaskId = taskSystem.TrackedTaskId;
            if (IsSupportedTestTask(trackedTaskId) &&
                taskSystem.TryGetActiveRecord(trackedTaskId, out TaskRecord record) &&
                record.State == E_TaskLifecycleState.InProgress)
            {
                nextNavigationTaskId = trackedTaskId;
            }

            if (navigationTaskId != nextNavigationTaskId)
            {
                ClearBoundNavigationTarget();
                navigationTaskId = nextNavigationTaskId;
            }

            if (!navigationTaskId.IsValid || targetTransform == null ||
                !uiManager.TryGetWindow<HUDWindow>(out HUDWindow hudWindow))
                return;

            if (ReferenceEquals(boundHudWindow, hudWindow))
                return;

            hudWindow.SetTaskNavigationTarget(navigationTaskId, targetTransform, targetOffset);
            boundHudWindow = hudWindow;
            Debug.Log(
                $"[TaskHUDOdinTester] 已把 Cube (1) 导航目标绑定到追踪任务，taskId={navigationTaskId}。",
                this);
        }

        /// <summary>先解除缓存的 HUD 窗口引用，再清理本组件提供的导航输入。</summary>
        private void ClearBoundNavigationTarget()
        {
            HUDWindow windowToClear = boundHudWindow;
            boundHudWindow = null;
            if (windowToClear == null)
                return;

            // Unity 关窗与场景对象禁用可能在同一轮生命周期中交错，先释放本地引用可避免回调重入后重复清理。
            windowToClear.ClearTaskNavigationTarget();
            Debug.Log($"[TaskHUDOdinTester] 已清除测试导航目标，taskId={navigationTaskId}。", this);
        }

        /// <summary>判断追踪任务是否属于当前场景提供 Cube (1) 位置的测试任务。</summary>
        /// <param name="taskId">当前追踪任务。</param>
        /// <returns>任务定义匹配两阶段或多目标测试任务时返回 true。</returns>
        private bool IsSupportedTestTask(TaskId taskId)
        {
            return (testTaskDefinition != null && testTaskDefinition.TaskId == taskId) ||
                   (multiObjectiveTaskDefinition != null && multiObjectiveTaskDefinition.TaskId == taskId);
        }

        /// <summary>校验测试任务资产是否已在当前 TaskDatabase 中注册。</summary>
        /// <param name="definition">待验证任务资产。</param>
        /// <param name="label">日志中的测试任务类型说明。</param>
        /// <returns>资产已由当前配置管理器注册时返回 true。</returns>
        private bool ValidateTaskReference(TaskDefinition definition, string label)
        {
            if (definition == null)
                throw new System.InvalidOperationException($"[TaskHUDOdinTester] Inspector 未配置{label}任务资产。");

            if (TaskConfigManager.Instance.TryGetDefinition(definition.TaskId, out TaskDefinition registered) &&
                ReferenceEquals(registered, definition))
                return true;

            Debug.LogError(
                $"[TaskHUDOdinTester] {label}任务未加入当前 TaskDatabase，taskId={definition.TaskId}。",
                this);
            return false;
        }

        /// <summary>确认调用 Odin 按钮时处于 Play Mode 且任务系统已初始化。</summary>
        /// <param name="system">已注册的任务系统。</param>
        /// <returns>可以调用任务 API 时返回 true。</returns>
        private bool TryGetTaskSystem(out TaskSystem system)
        {
            system = taskSystem;
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[TaskHUDOdinTester] 请先进入 Play Mode，再使用任务测试按钮。", this);
                return false;
            }

            if (system == null)
                system = GameArchitecture.Interface.GetSystem<TaskSystem>();
            return true;
        }
        #endregion
    }
}
#endif
