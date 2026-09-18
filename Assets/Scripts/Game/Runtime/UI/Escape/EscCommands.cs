using Cysharp.Threading.Tasks;
using WS_Modules.BusinessArchitecture;
using WS_Modules.UIModule;

namespace RPG.Game.UI.Escape
{
    /// <summary>把退出 Command 注册到 EscCommandManager。</summary>
    public sealed class RegisterEscCommand : AbstractCommand<EscCommandRegistration>
    {
        private readonly ICommand command;

        /// <summary>创建 Esc 注册 Command。</summary>
        /// <param name="command">要注册的退出 Command。</param>
        public RegisterEscCommand(ICommand command)
        {
            this.command = command;
        }

        /// <summary>执行注册并返回句柄。</summary>
        /// <returns>新注册句柄。</returns>
        protected override EscCommandRegistration OnExecute() => this.GetManager<EscCommandManager>().Register(command);
    }

    /// <summary>从 EscCommandManager 注销一个退出 Command。</summary>
    public sealed class UnregisterEscCommand : AbstractCommand
    {
        private readonly EscCommandRegistration registration;

        /// <summary>创建 Esc 注销 Command。</summary>
        /// <param name="registration">待注销句柄。</param>
        public UnregisterEscCommand(EscCommandRegistration registration)
        {
            this.registration = registration;
        }

        /// <summary>执行精确注销。</summary>
        protected override void OnExecute() => this.GetManager<EscCommandManager>().Unregister(registration);
    }

    /// <summary>执行当前 Esc 栈顶的退出 Command。</summary>
    public sealed class DispatchEscCommand : AbstractCommand<bool>
    {
        /// <summary>执行当前栈顶动作，并把是否存在动作返回给输入层。</summary>
        /// <returns>存在并执行了栈顶 Command 时返回 true。</returns>
        protected override bool OnExecute()
        {
            if (!this.GetManager<EscCommandManager>().TryGetCurrent(out ICommand command)) return false;
            this.SendCommand(command);
            return true;
        }
    }

    /// <summary>请求通过 UIManager 关闭 BagWindow。</summary>
    public sealed class CloseBagWindowCommand : AbstractCommand
    {
        /// <summary>执行 BagWindow 隐藏流程。</summary>
        protected override void OnExecute()
        {
            UIManager.Instance.HideWindowAsync<BagWindow>().Forget();
        }
    }

    /// <summary>请求通过 UIManager 关闭武器培养窗口。</summary>
    public sealed class CloseEquipmentDevelopmentWindowCommand : AbstractCommand
    {
        /// <summary>执行武器培养窗口隐藏流程。</summary>
        protected override void OnExecute()
        {
            UIManager.Instance.HideWindowAsync<EquipmentDevelopmentWindow>().Forget();
        }
    }

    /// <summary>请求收起统一装备培养窗口中的物品选择面板。</summary>
    public sealed class CloseEquipmentSelectionPanelCommand : AbstractCommand
    {
        /// <summary>调用当前培养窗口的面板关闭意图。</summary>
        protected override void OnExecute()
        {
            if (UIManager.Instance.TryGetWindow<EquipmentDevelopmentWindow>(out EquipmentDevelopmentWindow window))
                window.CloseSelectionPanelFromCommand();
        }
    }
}
