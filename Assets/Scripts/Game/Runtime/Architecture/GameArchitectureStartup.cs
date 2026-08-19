using UnityEngine;
using WS_Modules.Singleton;

namespace RPG.Game
{
    /// <summary>
    /// Unity 场景中的业务架构启动层，负责初始化和注销 GameArchitecture。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class GameArchitectureStartup : SingletonMonoBase<GameArchitectureStartup>
    {
        private bool architectureStarted;
        private bool applicationQuitting;

        /// <summary>
        /// 保持唯一启动组件并在 WSFrameRoot 之后启动业务架构。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            // DefaultExecutionOrder 确保 WSFrameRoot 已先完成基础设施初始化。
            GameArchitecture.InitArchitecture();
            architectureStarted = true;
        }

        /// <summary>
        /// 应用退出时先注销业务架构，触发 Manager/System 的生命周期清理。
        /// </summary>
        private void OnApplicationQuit()
        {
            applicationQuitting = true;
            DeinitializeArchitecture();
        }

        /// <summary>
        /// 启动组件被销毁时注销业务架构；应用退出路径已处理时不重复注销。
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (!applicationQuitting)
            {
                DeinitializeArchitecture();
            }
        }

        /// <summary>
        /// 在架构已启动时执行一次安全注销。
        /// </summary>
        private void DeinitializeArchitecture()
        {
            if (!architectureStarted)
            {
                return;
            }

            GameArchitecture.Interface.Deinit();
            architectureStarted = false;
        }
    }
}
