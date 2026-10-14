using System;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Escape;
using Sirenix.OdinInspector;
using WS_Modules.UIModule;

namespace RPG.Game.UI
{
    /// <summary>按需打开的设定窗口，关闭完成后销毁，不参与预热。</summary>
    [InfoBox("依赖同根 SettingsWindowDataComponent 与 SettingsWindowController；控件由 Prefab 显式绑定。")]
    public sealed class SettingsWindow : WindowBase
    {
        #region 依赖字段
        private SettingsWindowController controller;
        private EscCommandRegistration escRegistration;
        #endregion

        #region 生命周期
        /// <summary>取得同根组件并连接 Controller。</summary>
        public override void OnAwake()
        {
            var data = GameObject.GetComponent<SettingsWindowDataComponent>();
            controller = GameObject.GetComponent<SettingsWindowController>();
            if (data == null || controller == null) throw new InvalidOperationException("[SettingsWindow] Prefab 缺少依赖组件。");
            FullScreenWindow = true;
            SetDoAnimation(false);
            base.OnAwake();
            controller.Initialize(data);
        }

        /// <summary>显示时注册关闭 Command，并读取当前游戏与全局偏好。</summary>
        public override void OnShow()
        {
            base.OnShow();
            escRegistration = GameArchitecture.Interface.SendCommand(new RegisterEscCommand(new CloseSettingsWindowCommand()));
            controller.HandleShown();
        }

        /// <summary>隐藏完成后解除本窗口的 Esc 注册。</summary>
        public override void OnHide()
        {
            UnregisterEscape();
            base.OnHide();
        }

        /// <summary>销毁时释放控件绑定与头像租约。</summary>
        public override void OnDestroy()
        {
            UnregisterEscape();
            controller?.Dispose();
            base.OnDestroy();
        }

        /// <summary>由按钮与 Esc 共用同一个关闭请求。</summary>
        public void RequestClose() => controller.RequestClose();

        /// <summary>仅注销本窗口拥有的 Esc 句柄。</summary>
        private void UnregisterEscape()
        {
            if (!escRegistration.IsValid) return;
            GameArchitecture.Interface.SendCommand(new UnregisterEscCommand(escRegistration));
            escRegistration = default;
        }
        #endregion
    }
}
