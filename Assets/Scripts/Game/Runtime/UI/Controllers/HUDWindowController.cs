using System;
using RPG.Game.UI.Bag;
using UnityEngine;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// HUD 面板的 MVC 控制器，只接收 HUD 自身按钮意图并发布业务事件。
    /// HUD 的显隐策略、Game UI Lock 和跨窗口过渡由 HudWindowLockCoordinator 负责。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HUDWindowController : MonoBehaviour
    {
        #region 状态字段

        private bool initialized;

        #endregion

        #region 生命周期

        /// <summary>建立 HUD 面板 Controller 的一次性初始化状态。</summary>
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            WSLog.Log("[HUDWindowController] HUD 面板 MVC 初始化完成。");
        }

        #endregion

        #region 用户意图

        /// <summary>将 HUD Bag 按钮意图发布为统一的背包打开事件。</summary>
        /// <exception cref="InvalidOperationException">HUD Controller 尚未由 HUDWindow 初始化时抛出。</exception>
        public void HandleBagButtonClicked()
        {
            if (!initialized)
                throw new InvalidOperationException("[HUDWindowController] 尚未初始化，无法处理 Bag 按钮。");

            WSLog.Log("[HUDWindowController] 点击 HUD Bag 按钮，发布背包打开请求。");
            EventSystem.EventTrigger_Type(
                typeof(BagWindowOpenRequestedEventArgs),
                new BagWindowOpenRequestedEventArgs(BagWindowRequestSource.HudButton));
        }

        #endregion
    }
}
