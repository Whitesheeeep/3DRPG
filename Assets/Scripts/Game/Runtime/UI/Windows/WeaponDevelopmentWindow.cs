using System;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Escape;
using RPG.Game.UI.WeaponDevelopment;
using WS_Modules.BusinessArchitecture;

namespace WS_Modules.UIModule
{
    /// <summary>覆盖在 BagWindow 上方的武器成长与精炼窗口。</summary>
    public sealed class WeaponDevelopmentWindow : WindowBase, IWindowWithOpenContext<WeaponDevelopmentOpenContext>
    {
        #region 依赖与状态

        private WeaponDevelopmentWindowDataComponent data;
        private WeaponDevelopmentWindowController controller;
        private EscCommandRegistration escCommandRegistration;
        private IArchitecture escapeArchitecture;

        #endregion

        #region 生命周期

        /// <summary>读取模板化 Window DataComponent 并初始化 Controller。</summary>
        public override void OnAwake()
        {
            data = GameObject.GetComponent<WeaponDevelopmentWindowDataComponent>();
            if (data == null) throw new InvalidOperationException("[WeaponDevelopmentWindow] 根节点缺少 WeaponDevelopmentWindowDataComponent。");
            data.ValidateConfiguration();
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();
            controller = GameObject.GetComponent<WeaponDevelopmentWindowController>();
            if (controller == null) throw new InvalidOperationException("[WeaponDevelopmentWindow] 根节点缺少 WeaponDevelopmentWindowController。");
            controller.Initialize(data);
        }

        /// <summary>应用本次打开目标并注册培养窗口 Esc Command。</summary>
        /// <param name="context">武器实例打开上下文。</param>
        public void ApplyOpenContext(WeaponDevelopmentOpenContext context)
        {
            controller.SetTarget(context.WeaponInstanceId);
        }

        /// <summary>窗口稳定显示时注册退出动作并刷新页面。</summary>
        public override void OnShow()
        {
            base.OnShow();
            // OpenContext 重复打开可见窗口时同样会调用 OnShow，先移除旧句柄保证 Esc 栈只有一层。
            UnregisterEscCommand();
            escapeArchitecture = RPG.Game.GameArchitecture.Interface;
            escCommandRegistration = escapeArchitecture.SendCommand(
                new RegisterEscCommand(new CloseWeaponDevelopmentWindowCommand()));
            controller.OnWindowShown();
        }

        /// <summary>窗口稳定隐藏时注销退出动作并释放 Controller 资源。</summary>
        public override void OnHide()
        {
            UnregisterEscCommand();
            controller.OnWindowHidden();
            base.OnHide();
        }

        /// <summary>窗口销毁时释放退出动作和 Controller。</summary>
        public override void OnDestroy()
        {
            UnregisterEscCommand();
            controller?.Dispose();
            base.OnDestroy();
        }

        #endregion

        #region 选择面板

        /// <summary>供 Esc Command 收起当前升级或精炼选择面板。</summary>
        public void CloseSelectionPanelFromCommand() => controller.CloseSelectionPanelFromCommand();

        /// <summary>注销当前窗口的 Esc Command。</summary>
        private void UnregisterEscCommand()
        {
            if (!escCommandRegistration.IsValid) return;
            escapeArchitecture?.SendCommand(new UnregisterEscCommand(escCommandRegistration));
            escCommandRegistration = default;
        }

        #endregion
    }
}
