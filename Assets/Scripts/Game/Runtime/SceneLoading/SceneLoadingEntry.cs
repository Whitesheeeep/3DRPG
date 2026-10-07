using System;
using Cysharp.Threading.Tasks;
using RPG.Game;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading
{
    /// <summary>直接运行场景时启动准备任务；通过 Addressables 切场景时加入同一加载流程。</summary>
    [DisallowMultipleComponent]
    public sealed class SceneLoadingEntry : MonoBehaviour
    {
        #region 场景配置

        [SerializeField, Tooltip("在 SceneLoadDatabase 中配置的稳定场景 ID。")]
        private string sceneId;

        #endregion

        #region 生命周期

        /// <summary>在业务架构启动后初始化当前场景，或识别并加入已有切场景流程。</summary>
        private void Start()
        {
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new InvalidOperationException($"[SceneLoadingEntry] '{name}' 未配置 SceneId。");

            SceneLoadingSystem loadingSystem = GameArchitecture.Interface.GetSystem<SceneLoadingSystem>();
            if (loadingSystem.IsSceneJoiningCurrentFlow(sceneId, gameObject.scene))
            {
                WSLog.Log($"[SceneLoadingEntry] 场景入口识别统一流程加载实例，sceneId={sceneId}，scene={gameObject.scene.name}，gameObject={name}。");
                return;
            }

            WSLog.Log($"[SceneLoadingEntry] 开始当前场景准备，sceneId={sceneId}，scene={gameObject.scene.name}。");
            loadingSystem.InitializeCurrentSceneAsync(sceneId).Forget(HandleInitializationException);
        }

        #endregion

        #region 异常处理

        /// <summary>记录直接进入场景初始化失败及异常上下文。</summary>
        /// <param name="exception">场景准备异常。</param>
        private void HandleInitializationException(Exception exception) =>
            WSLog.LogError($"[SceneLoadingEntry] 当前场景初始化失败，sceneId={sceneId}，gameObject={name}，exception={exception}");

        #endregion
    }
}
