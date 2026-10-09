using System;
using System.Collections.Generic;
using RPG.Character;
using RPG.Game;
using RPG.Game.UI.Views.HUD;
using RPG.SaveSystem;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>把 TaskSystem 的追踪事实投影到 HUD，并管理可选导航目标标记。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 Prefab 显式绑定的 HUDTaskTrackerView 与 HUDTaskWorldMarkerView；任务数据从 GameArchitecture 查询，玩家和 MainCamera 在 HUD 显示期间缓存。")]
    public sealed class HUDTaskController : MonoBehaviour
    {
        #region 依赖字段
        [SerializeField, Required] private HUDTaskTrackerView trackerView;
        [SerializeField, Required] private HUDTaskWorldMarkerView worldMarkerView;
        private TaskSystem taskSystem;
        private SaveManager saveManager;
        private PlayerController playerController;
        private CharacterManager characterManager;
        private Camera gameplayCamera;
        private readonly List<IUnRegister> taskEventUnregisterList = new List<IUnRegister>();
        #endregion

        #region HUD 与导航状态
        private string lastDistanceLabel = string.Empty;
        private float nextCameraResolveTime;
        private bool initialized;
        private bool visible;
        private bool refreshPending;
        private bool hasLoggedMissingCamera;
        #endregion

        #region 生命周期
        /// <summary>连接任务、存档事实和 Prefab View；调用发生在 HUD 主线程。</summary>
        /// <exception cref="InvalidOperationException">架构依赖或 Prefab 引用缺失时抛出。</exception>
        public void Initialize()
        {
            if (initialized)
                return;

            if (trackerView == null || worldMarkerView == null)
                throw new InvalidOperationException("[HUDTaskController] HUD Prefab 未绑定任务摘要或世界标记 View。");

            trackerView.ValidateConfiguration();
            worldMarkerView.ValidateConfiguration();
            taskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
            saveManager = GameArchitecture.Interface.GetManager<SaveManager>();
            initialized = true;
            Debug.Log("[HUDTaskController] 已连接 TaskSystem、SaveManager 和 HUD 任务 View。", this);
        }

        /// <summary>HUD 显示时订阅任务事实并读取当前追踪任务快照。</summary>
        public void OnWindowShown()
        {
            if (!initialized || visible)
                return;

            visible = true;
            RegisterTaskEvents();
            saveManager.OperationCompleted += HandleSaveOperationCompleted;
            BindPlayerSources();
            ResolveGameplayCamera();
            RefreshNow();
            Debug.Log("[HUDTaskController] HUD 显示，已绑定任务事件并刷新追踪摘要。", this);
        }

        /// <summary>HUD 隐藏时注销事件并隐藏摘要与世界目标标记。</summary>
        public void OnWindowHidden()
        {
            if (!visible)
                return;

            visible = false;
            UnregisterTaskEvents();
            saveManager.OperationCompleted -= HandleSaveOperationCompleted;
            UnbindPlayerSources();
            trackerView.Hide();
            worldMarkerView.Hide();
            lastDistanceLabel = string.Empty;
            Debug.Log("[HUDTaskController] HUD 隐藏，已解除任务订阅并隐藏追踪显示。", this);
        }

        /// <summary>销毁控制器时释放显示期间的订阅和导航引用。</summary>
        public void Dispose()
        {
            if (!initialized)
                return;

            OnWindowHidden();
            taskSystem = null;
            saveManager = null;
            trackerView = null;
            worldMarkerView = null;
            initialized = false;
            Debug.Log("[HUDTaskController] HUD 任务控制器已释放。", this);
        }

        /// <summary>合并同一帧内到达的任务变化，随后在晚帧刷新目标投影。</summary>
        private void LateUpdate()
        {
            if (!visible)
                return;

            if (refreshPending)
                RefreshNow();

            if (playerController == null || characterManager == null)
                BindPlayerSources();
            if (gameplayCamera == null && Time.unscaledTime >= nextCameraResolveTime)
                ResolveGameplayCamera();
            RefreshNavigationProjection();
        }
        #endregion

        #region 任务事件绑定
        /// <summary>订阅所有会改变摘要事实的任务事件。</summary>
        private void RegisterTaskEvents()
        {
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskAcceptedEventArgs>(
                typeof(TaskAcceptedEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskObjectiveProgressChangedEventArgs>(
                typeof(TaskObjectiveProgressChangedEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskStageChangedEventArgs>(
                typeof(TaskStageChangedEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskStateChangedEventArgs>(
                typeof(TaskStateChangedEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskRewardClaimableEventArgs>(
                typeof(TaskRewardClaimableEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskCompletedEventArgs>(
                typeof(TaskCompletedEventArgs), HandleTaskChanged));
            taskEventUnregisterList.Add(EventSystem.Register_Type<TaskTrackedChangedEventArgs>(
                typeof(TaskTrackedChangedEventArgs), HandleTaskChanged));
            WSLog.Log("[HUDTaskController] 已订阅接取、进度、阶段、状态、领奖资格、完成和追踪任务事件。");
        }

        /// <summary>注销 HUD 显示期间登记的所有任务事件。</summary>
        private void UnregisterTaskEvents()
        {
            for (int index = 0; index < taskEventUnregisterList.Count; index++)
                taskEventUnregisterList[index]?.UnRegister();
            taskEventUnregisterList.Clear();
            WSLog.Log("[HUDTaskController] 已解除全部任务事实事件订阅。");
        }

        /// <summary>任务变化只安排刷新，由 HUD 晚帧统一重读完整事实。</summary>
        /// <typeparam name="TEvent">任务事实事件类型。</typeparam>
        /// <param name="eventArgs">已提交的任务事实。</param>
        private void HandleTaskChanged<TEvent>(TEvent eventArgs)
        {
            MarkRefreshPending();
        }

        /// <summary>仅在成功加载存档后安排当前任务事实重读。</summary>
        /// <param name="completion">完成的存档操作结果。</param>
        private void HandleSaveOperationCompleted(SaveOperationCompleted completion)
        {
            if (completion.Kind == SaveOperationKind.Load && completion.IsSuccess)
                MarkRefreshPending();
        }

        /// <summary>标记任务展示投影在晚帧合并刷新。</summary>
        private void MarkRefreshPending()
        {
            refreshPending = true;
        }
        #endregion

        #region 任务摘要刷新
        /// <summary>将 TaskSystem 当前追踪任务及其当前阶段目标写入 HUD View。</summary>
        private void RefreshNow()
        {
            refreshPending = false;
            lastDistanceLabel = string.Empty;
            if (!taskSystem.TrackedTaskId.IsValid ||
                !taskSystem.TryGetActiveRecord(taskSystem.TrackedTaskId, out TaskRecord record))
            {
                trackerView.Hide();
                worldMarkerView.Hide();
                return;
            }

            TaskDefinition definition = TaskConfigManager.Instance.GetRequiredDefinition(record.TaskId);
            if (!definition.TryGetStage(record.CurrentStageId, out TaskStageDefinition stage, out _))
                throw new InvalidOperationException($"[HUDTaskController] 追踪任务 {record.TaskId} 的当前阶段不存在。");

            trackerView.RenderHeader(
                definition.Title,
                definition.CategoryId,
                record.State == E_TaskLifecycleState.Claimable);
            trackerView.SetDistanceLabel(string.Empty);
            trackerView.SetObjectiveCount(stage.Objectives.Count);
            for (int index = 0; index < stage.Objectives.Count; index++)
            {
                TaskObjectiveDefinition objective = stage.Objectives[index];
                if (!record.TryGetProgress(objective.ObjectiveId, out TaskObjectiveProgress progress))
                    throw new InvalidOperationException(
                        $"[HUDTaskController] 任务 {record.TaskId} 阶段 {record.CurrentStageId} 缺少目标进度 {objective.ObjectiveId}。");

                string description = !string.IsNullOrWhiteSpace(objective.DisplayDescription)
                    ? objective.DisplayDescription
                    : !string.IsNullOrWhiteSpace(stage.Title) ? stage.Title : "完成目标";
                trackerView.GetObjectiveRowView(index).Bind(
                    description,
                    progress.Current,
                    progress.Required,
                    progress.IsComplete);
            }
        }
        #endregion

        #region 玩家与目标投影
        /// <summary>在 HUD 显示期间缓存稳定 PlayerController 并订阅当前角色切换。</summary>
        private void BindPlayerSources()
        {
            if (playerController != null)
                return;

            playerController = PlayerController.Instance;
            if (playerController == null)
                return;

            characterManager = playerController.CharacterManager;
            if (characterManager != null)
                characterManager.ActiveCharacterChanged += HandleActiveCharacterChanged;
            Debug.Log("[HUDTaskController] 已绑定玩家角色来源。", this);
        }

        /// <summary>注销当前玩家角色切换通知并释放场景对象引用。</summary>
        private void UnbindPlayerSources()
        {
            if (characterManager != null)
                characterManager.ActiveCharacterChanged -= HandleActiveCharacterChanged;
            playerController = null;
            characterManager = null;
            gameplayCamera = null;
        }

        /// <summary>缓存当前 MainCamera；相机暂缺时限制为低频重试。</summary>
        private void ResolveGameplayCamera()
        {
            nextCameraResolveTime = Time.unscaledTime + 1f;
            gameplayCamera = Camera.main;
            if (gameplayCamera != null)
            {
                if (hasLoggedMissingCamera)
                    Debug.Log("[HUDTaskController] MainCamera 已恢复，导航投影重新启用。", this);
                hasLoggedMissingCamera = false;
                return;
            }

            if (!hasLoggedMissingCamera)
            {
                Debug.LogWarning("[HUDTaskController] 当前没有 MainCamera，暂时隐藏 HUD 世界目标标记。", this);
                hasLoggedMissingCamera = true;
            }
        }

        /// <summary>角色切换后由晚帧使用新的活动角色位置投影导航目标。</summary>
        /// <param name="previous">切换前角色。</param>
        /// <param name="current">切换后角色。</param>
        private void HandleActiveCharacterChanged(CharacterActor previous, CharacterActor current)
        {
            MarkRefreshPending();
        }

        /// <summary>读取任务层选出的 Transform 并逐帧更新屏幕坐标与距离。</summary>
        private void RefreshNavigationProjection()
        {
            if (!taskSystem.TryGetTrackedNavigationTarget(out Transform navigationTarget, out Vector3 offset) ||
                navigationTarget == null ||
                characterManager == null || !characterManager.IsReady ||
                characterManager.ActiveCharacter == null || gameplayCamera == null)
            {
                worldMarkerView.Hide();
                SetTrackerDistance(string.Empty);
                return;
            }

            Transform playerTransform = characterManager.ActiveCharacter.transform;
            int distance = worldMarkerView.Render(gameplayCamera, playerTransform, navigationTarget, offset);
            SetTrackerDistance(distance >= 0 ? $"{distance}m" : string.Empty);
        }

        /// <summary>仅在距离整数变化时更新任务摘要行，避免每帧重复布局。</summary>
        /// <param name="distanceLabel">需要展示的整数米距离。</param>
        private void SetTrackerDistance(string distanceLabel)
        {
            if (string.Equals(lastDistanceLabel, distanceLabel, StringComparison.Ordinal))
                return;

            lastDistanceLabel = distanceLabel;
            trackerView.SetDistanceLabel(distanceLabel);
        }
        #endregion
    }
}
