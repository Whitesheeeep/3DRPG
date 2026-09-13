using System;
using RPG.Game;
using RPG.Game.UI.Bag;
using RPG.Game.UI.Escape;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 将 PlayerInputController 转发的即时 UI 快捷键转换为类型化 UI 事件或 Command。
    /// 该组件不订阅 InputAction、不创建输入缓存、不管理窗口生命周期；玩法输入仍由 PlayerInputController 的缓冲系统处理。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-800)]
    [InfoBox("依赖同一 Player 根节点上 Inspector 显式绑定的 PlayerInputController；该组件只接收即时 UI 快捷键通知。")]
    public sealed class GameUIShortcutController : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required]
        private PlayerInputController inputController;

        #endregion

        #region Unity 生命周期

        /// <summary>校验 Inspector 显式绑定的玩家输入控制器。</summary>
        private void Awake()
        {
            if (inputController == null)
                throw new InvalidOperationException("[GameUIShortcutController] 未绑定 PlayerInputController。");
        }

        /// <summary>订阅输入控制器的即时 UI 快捷键通知。</summary>
        private void OnEnable()
        {
            inputController.ImmediateInputPerformed += OnImmediateInputPerformed;
            Debug.Log("[GameUIShortcutController] 已订阅即时 UI 快捷键。", this);
        }

        /// <summary>注销输入控制器的即时 UI 快捷键通知。</summary>
        private void OnDisable()
        {
            inputController.ImmediateInputPerformed -= OnImmediateInputPerformed;
            Debug.Log("[GameUIShortcutController] 已注销即时 UI 快捷键。", this);
        }

        #endregion

        #region 快捷键转发

        /// <summary>将输入层提供的即时类型翻译为对应的游戏 UI 意图。</summary>
        /// <param name="inputType">由 PlayerInputController 转发的即时输入类型。</param>
        private void OnImmediateInputPerformed(PlayerInputType inputType)
        {
            switch (inputType)
            {
                case PlayerInputType.BagWindow:
                    PublishBagOpenRequest();
                    break;
                case PlayerInputType.CancelWindow:
                    DispatchCancelCommand();
                    break;
            }
        }

        /// <summary>发布来自键盘或手柄快捷键的 Bag 打开意图。</summary>
        private static void PublishBagOpenRequest()
        {
            Debug.Log("[GameUIShortcutController] 转发 Bag 快捷键。");
            EventSystem.EventTrigger_Type(
                typeof(BagWindowOpenRequestedEventArgs),
                new BagWindowOpenRequestedEventArgs(BagWindowRequestSource.Shortcut));
        }

        /// <summary>将取消快捷键交给 BusinessArchitecture 的 Esc Command 栈。</summary>
        private static void DispatchCancelCommand()
        {
            Debug.Log("[GameUIShortcutController] 转发 Esc 快捷键。");
            GameArchitecture.Interface.SendCommand(new DispatchEscCommand());
        }

        #endregion
    }
}
