using RPG.CameraSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules;

namespace RPG.Game.Loading
{
    /// <summary>直接打开游戏场景时补齐 WSFrame 与 GameplayCamera，正常加载时复用常驻实例。</summary>
    [DefaultExecutionOrder(-700), DisallowMultipleComponent]
    [InfoBox("用于直接打开庭院场景时补齐缺失的 WSFrameRoot 与 GameplayCamera；正常游戏从 GameStart 启动后会复用常驻实例。GameplayCameraController 必须与 CinemachineManager 位于同一 Prefab 根节点。")]
    public sealed class GameplaySceneBootstrap : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("框架运行根 Prefab")]
        private GameObject frameRootPrefab;
        [SerializeField, Required, LabelText("常驻 GameplayCamera Prefab")]
        private GameObject gameplayCameraPrefab;

        #endregion

        /// <summary>在场景入口启动前确保框架及唯一游戏相机已常驻。</summary>
        private void Start()
        {
            if (WSFrameRoot.Instance == null)
            {
                if (frameRootPrefab == null)
                    throw new System.InvalidOperationException("[GameplaySceneBootstrap] 缺少 WSFrameRoot Prefab 引用。");
                Instantiate(frameRootPrefab);
                Debug.Log("[GameplaySceneBootstrap] 直接打开游戏场景，已创建 WSFrameRoot。");
            }

            CinemachineManager cameraManager = CinemachineManager.Instance;
            if (cameraManager == null)
            {
                if (gameplayCameraPrefab == null)
                    throw new System.InvalidOperationException("[GameplaySceneBootstrap] 缺少 GameplayCamera Prefab 引用。");
                Instantiate(gameplayCameraPrefab);
                cameraManager = CinemachineManager.Instance;
                Debug.Log("[GameplaySceneBootstrap] 直接打开游戏场景，已创建常驻 GameplayCamera。");
            }

            if (cameraManager == null)
                throw new System.InvalidOperationException("[GameplaySceneBootstrap] GameplayCamera Prefab 实例化后未注册 CinemachineManager。");
            GameplayCameraController cameraController = cameraManager.GetComponent<GameplayCameraController>();
            if (cameraController == null)
                throw new System.InvalidOperationException("[GameplaySceneBootstrap] CinemachineManager 根节点缺少 GameplayCameraController。");
            cameraController.EnterPresentationMode();
        }
    }
}
