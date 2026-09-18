using System;
using UnityEngine;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 游戏窗口流程的组合根，只负责创建、注册和释放各个纯 C# 子协调器。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-790)]
    public sealed class GameWindowFlowCoordinator : MonoBehaviour
    {
        #region 状态字段

        // 子协调器按固定顺序注册，并在销毁时按反向顺序释放。
        private IGameWindowSubCoordinator[] subCoordinators;
        private bool disposed;

        #endregion

        #region 生命周期

        /// <summary>创建并注册 HUD 锁和 Bag 跨窗口两个子协调器。</summary>
        private void Awake()
        {
            if (subCoordinators != null) return;

            subCoordinators = new IGameWindowSubCoordinator[]
            {
                new HudWindowLockCoordinator(),
                new BagWindowFlowCoordinator()
            };
            for (int index = 0; index < subCoordinators.Length; index++)
                subCoordinators[index].Register();

            WSLog.Log("[GameWindowFlowCoordinator] 已注册 HUD 锁与 Bag 流程子协调器。");
        }

        /// <summary>按注册逆序释放所有子协调器和其事件订阅。</summary>
        private void OnDestroy()
        {
            if (disposed) return;
            disposed = true;
            if (subCoordinators == null) return;

            for (int index = subCoordinators.Length - 1; index >= 0; index--)
                subCoordinators[index].Dispose();
            subCoordinators = null;
            WSLog.Log("[GameWindowFlowCoordinator] 已释放所有窗口流程子协调器。");
        }

        #endregion
    }

    /// <summary>
    /// 窗口流程子协调器的生命周期契约；实现类不依赖 MonoBehaviour。
    /// </summary>
    internal interface IGameWindowSubCoordinator : IDisposable
    {
        /// <summary>注册该子协调器负责的事件和窗口生命周期通知。</summary>
        void Register();
    }
}
