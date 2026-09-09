using System;
using RPG.Game.UI.Bag;
using RPG.PlayerInputSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.Game.UI.Controllers
{
    /// <summary>
    /// 将玩家输入缓冲中的窗口请求转换为类型化 UI 事件。
    /// 该组件只负责一次性消费 Press，不参与窗口显隐、资源加载或业务操作。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-800)]
    [InfoBox("依赖同一 Player 根节点上的 PlayerInputController；缺失时无法提交窗口命令。")]
    public sealed class GameUIInputCommandController : MonoBehaviour
    {
        #region 依赖字段

        [SerializeField, Required]
        private PlayerInputController inputController;

        #endregion

        #region Unity 生命周期

        /// <summary>解析同一玩家对象上的输入控制器依赖。</summary>
        private void Awake()
        {
            if (inputController == null)
                inputController = GetComponent<PlayerInputController>();
            if (inputController == null)
                throw new InvalidOperationException("[GameUIInputCommandController] Player 上缺少 PlayerInputController。");
        }

        /// <summary>每帧把尚未消费的背包和取消 Press 转发为 UI 命令。</summary>
        private void Update()
        {
            TryPublishBagToggle();
            TryPublishCancel();
        }

        #endregion

        #region 输入命令

        /// <summary>提交一次背包切换命令，并在事件提交后消费输入句柄。</summary>
        private void TryPublishBagToggle()
        {
            if (!inputController.TryGetRequest(PlayerInputType.BagWindow,
                    out IReadOnlyPlayerInputRequest request) || !request.HasBufferedPress)
                return;

            EventSystem.EventTrigger_Type(
                typeof(BagWindowToggleRequestedEventArgs),
                new BagWindowToggleRequestedEventArgs(BagWindowRequestSource.Shortcut));
            inputController.TryConfirmConsumed(request.PressHandle);
        }

        /// <summary>提交一次取消当前窗口命令，并在事件提交后消费输入句柄。</summary>
        private void TryPublishCancel()
        {
            if (!inputController.TryGetRequest(PlayerInputType.CancelWindow,
                    out IReadOnlyPlayerInputRequest request) || !request.HasBufferedPress)
                return;

            EventSystem.EventTrigger_Type(
                typeof(GameWindowCancelRequestedEventArgs),
                new GameWindowCancelRequestedEventArgs(BagWindowRequestSource.CancelShortcut));
            inputController.TryConfirmConsumed(request.PressHandle);
        }

        #endregion
    }
}
