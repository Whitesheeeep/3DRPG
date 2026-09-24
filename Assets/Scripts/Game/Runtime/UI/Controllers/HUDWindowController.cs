using System;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Character;
using UnityEngine;
using UnityEngine.UI;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

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
        private Button characterButton;

        #endregion

        #region 生命周期

        /// <summary>建立 HUD 面板 Controller 的一次性初始化状态。</summary>
        public void Initialize()
        {
            if (initialized) return;
            HUDWindowDataComponent data = GetComponent<HUDWindowDataComponent>();
            if (data == null)
                throw new InvalidOperationException("[HUDWindowController] HUD 根节点缺少 HUDWindowDataComponent。");
            if (data.BagButton == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindowDataComponent 未绑定 BagButton。");
            if (data.DocumentUIPanelDocumentUIPanel == null ||
                data.DocumentUIPanelDocumentUIPanel.CharacterButton == null)
                throw new InvalidOperationException("[HUDWindowController] HUDWindowDataComponent 未绑定 CharacterButton。");

            // 红点徽标已经作为 HUD Prefab 的静态子节点绑定，Controller 只校验自身窗口数据。
            characterButton = data.DocumentUIPanelDocumentUIPanel.CharacterButton;
            characterButton.onClick.AddListener(HandleCharacterButtonClicked);
            initialized = true;
            WSLog.Log("[HUDWindowController] HUD 面板 MVC 初始化完成。");
        }

        /// <summary>注销 HUD 角色按钮监听，避免 HUD 窗口销毁后发布过期请求。</summary>
        public void Dispose()
        {
            if (!initialized) return;
            characterButton?.onClick.RemoveListener(HandleCharacterButtonClicked);
            characterButton = null;
            initialized = false;
            WSLog.Log("[HUDWindowController] HUD 面板 MVC 已释放。");
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

        /// <summary>将 HUD 角色按钮意图发布为统一角色窗口打开事件。</summary>
        public void HandleCharacterButtonClicked()
        {
            if (!initialized)
                throw new InvalidOperationException("[HUDWindowController] 尚未初始化，无法处理 Character 按钮。");
            WSLog.Log("[HUDWindowController] 点击 HUD Character 按钮，发布角色窗口打开请求。");
            EventSystem.EventTrigger_Type(
                typeof(CharacterWindowOpenRequestedEventArgs),
                new CharacterWindowOpenRequestedEventArgs(CharacterWindowOpenSource.HudButton));
        }

        #endregion
    }
}
