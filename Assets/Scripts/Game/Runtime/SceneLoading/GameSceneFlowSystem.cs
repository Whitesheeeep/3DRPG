using System;
using Cysharp.Threading.Tasks;
using RPG.CameraSystem;
using RPG.Character;
using RPG.Game;
using RPG.Game.UI;
using UnityEngine.SceneManagement;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;
using WS_Modules.UIModule;

namespace RPG.Game.Loading
{
    /// <summary>按加载快照暂停或恢复 RPG 场景状态，并在成功后显示已预热的 HUD。</summary>
    public sealed class GameSceneFlowSystem : AbstractSystem
    {
        #region 依赖字段

        // 架构注销时配对释放，避免重建架构后重复接收完成通知。
        private IUnRegister completedRegistration;
        private IUnRegister snapshotRegistration;
        private PlayerController preparedPlayer;
        private Scene preparationSourceScene;
        private bool preparationStarted;

        #endregion

        #region 架构生命周期

        /// <summary>在场景加载系统初始化后订阅其成功完成通知。</summary>
        protected override void OnInit()
        {
            completedRegistration = GameArchitecture.Interface.GetSystem<SceneLoadingSystem>()
                .RegisterCompleted(HandleCompleted);
            snapshotRegistration = GameArchitecture.Interface.GetSystem<SceneLoadingSystem>()
                .RegisterSnapshotChanged(HandleSnapshotChanged);
            WSLog.Log("[GameSceneFlowSystem] 已订阅加载快照与完成事件。");
        }

        /// <summary>注销完成通知，避免架构销毁后保留业务收尾回调。</summary>
        protected override void OnDeinit()
        {
            completedRegistration?.UnRegister();
            completedRegistration = null;
            snapshotRegistration?.UnRegister();
            snapshotRegistration = null;
            preparedPlayer = null;
            preparationStarted = false;
            WSLog.Log("[GameSceneFlowSystem] 已注销加载快照与完成事件。");
        }

        #endregion

        #region 完成事件处理

        /// <summary>在新流程首个 Loading 快照到达时暂停当前玩家与相机。</summary>
        /// <param name="eventArgs">当前加载流程的快照。</param>
        private void HandleSnapshotChanged(SceneLoadingEventArgs eventArgs)
        {
            SceneLoadExecutionSnapshot snapshot = eventArgs.Snapshot;
            if (snapshot.State == E_SceneLoadExecutionState.Loading)
            {
                if (preparationStarted) return;

                preparationStarted = true;
                preparationSourceScene = SceneManager.GetActiveScene();
                preparedPlayer = PlayerController.Instance;
                // 当前可能仍处于 GameStart，允许没有 Player；真正游戏场景中的单例立即进入准备门禁。
                preparedPlayer?.BeginScenePreparation();
                if (CinemachineManager.Instance != null &&
                    CinemachineManager.Instance.TryGetComponent(out GameplayCameraController cameraController))
                    cameraController.EnterPresentationMode();
                WSLog.Log($"[GameSceneFlowSystem] 已暂停当前游戏表现，sceneId={snapshot.SceneId}，sourceScene={preparationSourceScene.name}。");
                return;
            }

            if (snapshot.State is E_SceneLoadExecutionState.Failed or E_SceneLoadExecutionState.Cancelled)
                RestoreSourceGameplayWhenAvailable(snapshot);
        }

        /// <summary>接收加载完成通知并启动 RPG 侧异步界面收尾。</summary>
        /// <param name="eventArgs">成功场景流程的最终快照。</param>
        private void HandleCompleted(SceneLoadingEventArgs eventArgs)
        {
            CompleteGameplayAsync(eventArgs.Snapshot).Forget(
                exception => HandleCompletionFailure(eventArgs.Snapshot, exception));
            preparationStarted = false;
            preparedPlayer = null;
        }

        /// <summary>关闭开始窗口、恢复玩家和相机朝向，最后显示已经预热的 HUD。</summary>
        /// <param name="snapshot">完成的场景流程快照。</param>
        /// <returns>RPG 场景收尾与 HUD 显示请求提交完成后的异步任务。</returns>
        private async UniTask CompleteGameplayAsync(SceneLoadExecutionSnapshot snapshot)
        {
            if (UIManager.Instance.TryGetWindow<GameStartWindow>(out GameStartWindow startWindow) && startWindow.Visible)
                await UIManager.Instance.HideWindowAsync<GameStartWindow>();

            PlayerController playerController = PlayerController.Instance;
            if (playerController == null)
                throw new InvalidOperationException("[GameSceneFlowSystem] 流程成功后找不到 PlayerController。");
            playerController.CompleteScenePreparation();

            CinemachineManager cameraManager = CinemachineManager.Instance;
            if (cameraManager == null)
                throw new InvalidOperationException("[GameSceneFlowSystem] 流程成功后找不到常驻 CinemachineManager。");
            GameplayCameraController cameraController = cameraManager.GetComponent<GameplayCameraController>();
            if (cameraController == null)
                throw new InvalidOperationException("[GameSceneFlowSystem] CinemachineManager 根节点缺少 GameplayCameraController。");
            cameraController.AlignToPlayerAndEnterGameplayMode();

            await UIManager.Instance.PopUpWindowAsync<HUDWindow>();
            WSLog.Log($"[GameSceneFlowSystem] 正式游戏状态已恢复并显示 HUD，sceneId={snapshot.SceneId}。");
        }

        /// <summary>记录完成通知之后的 RPG 收尾异常，不反向改变已成功的场景加载结果。</summary>
        /// <param name="snapshot">已成功的场景流程快照。</param>
        /// <param name="exception">RPG 收尾期间发生的异常。</param>
        private static void HandleCompletionFailure(SceneLoadExecutionSnapshot snapshot, Exception exception)
        {
            WSLog.LogError($"[GameSceneFlowSystem] 场景已成功但 RPG 收尾失败，sceneId={snapshot.SceneId}，exception={exception}");
        }

        /// <summary>仅在 Single 切换尚未离开来源场景时恢复原玩家和游戏相机。</summary>
        /// <param name="snapshot">失败或取消的流程快照。</param>
        private void RestoreSourceGameplayWhenAvailable(SceneLoadExecutionSnapshot snapshot)
        {
            if (!preparationStarted) return;
            bool sourceSceneIsStillActive = preparationSourceScene.IsValid() &&
                                            preparationSourceScene.isLoaded &&
                                            SceneManager.GetActiveScene() == preparationSourceScene;
            if (sourceSceneIsStillActive && preparedPlayer != null && preparedPlayer.IsReady)
            {
                preparedPlayer.CompleteScenePreparation();
                if (CinemachineManager.Instance != null &&
                    CinemachineManager.Instance.TryGetComponent(out GameplayCameraController cameraController))
                    cameraController.EnterGameplayMode();
                WSLog.Log($"[GameSceneFlowSystem] 加载失败或取消，已恢复来源场景控制，sceneId={snapshot.SceneId}，sourceScene={preparationSourceScene.name}。");
            }
            else
            {
                WSLog.LogWarning($"[GameSceneFlowSystem] 加载失败或取消，来源场景已不可恢复，保留加载提示，sceneId={snapshot.SceneId}。");
            }

            preparationStarted = false;
            preparedPlayer = null;
        }

        #endregion
    }
}
