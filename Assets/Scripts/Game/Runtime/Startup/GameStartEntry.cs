using Cysharp.Threading.Tasks;
using RPG.CameraSystem;
using RPG.Game.UI;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.Startup
{
    /// <summary>等待 UIManager 就绪后显示开始窗口，并使常驻相机进入界面模式。</summary>
    [DisallowMultipleComponent]
    [InfoBox("依赖 GameStart 场景中显式绑定的 GameplayCameraController；UIRoot、UICamera 与 UIEventSystem 必须以原名放在同一场景供 UIManager 复用。")]
    public sealed class GameStartEntry : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required, LabelText("常驻 GameplayCamera 控制器")]
        private GameplayCameraController gameplayCameraController;

        #endregion

        #region 启动

        /// <summary>开始等待 UI 服务，成功后由 UIManager 创建并显示游戏开始窗口。</summary>
        private void Start()
        {
            OpenStartWindowAsync().Forget(HandleStartupException);
        }

        #endregion

        #region 窗口接入

        /// <summary>保持界面镜头模式并等待框架 UI 完成场景对象复用。</summary>
        /// <returns>开始窗口完成显示后的异步任务。</returns>
        private async UniTask OpenStartWindowAsync()
        {
            gameplayCameraController.EnterPresentationMode();
            await UniTask.WaitUntil(() => UIManager.Instance != null && UIManager.Instance.IsInitialized);
            GameStartWindow startWindow = await UIManager.Instance.PopUpWindowAsync<GameStartWindow>();
            if (startWindow == null)
                throw new System.InvalidOperationException("[GameStartEntry] UIManager 未能打开 GameStartWindow。");
            WSLog.Log("[GameStartEntry] 开始游戏窗口已显示。");
        }

        /// <summary>记录开始窗口启动失败，避免 Forget 产生未观察异常。</summary>
        /// <param name="exception">窗口启动异常。</param>
        private void HandleStartupException(System.Exception exception)
        {
            WSLog.LogError($"[GameStartEntry] 开始界面启动失败，exception={exception}");
        }

        #endregion
    }
}
