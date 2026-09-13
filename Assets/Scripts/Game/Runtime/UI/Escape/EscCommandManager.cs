using System.Collections.Generic;
using WS_Modules.BusinessArchitecture;

namespace RPG.Game.UI.Escape
{
    /// <summary>
    /// 保存当前可响应 Esc 的退出 Command，按后注册先执行的顺序提供栈语义。
    /// </summary>
    public sealed class EscCommandManager : AbstractManager
    {
        #region 状态

        // 列表尾部是当前最上层的退出动作；条目只保存业务 Command 和稳定注册序号。
        private readonly List<RegisteredEscCommand> registeredCommands = new();
        private long nextRegistrationId;

        #endregion

        #region 注册与分发

        /// <summary>注册一个新的 Esc Command。</summary>
        /// <param name="command">按下 Esc 时执行的 Command。</param>
        /// <returns>用于注销本次注册的句柄。</returns>
        public EscCommandRegistration Register(ICommand command)
        {
            if (command == null) throw new System.ArgumentNullException(nameof(command));
            EscCommandRegistration registration = new EscCommandRegistration(++nextRegistrationId);
            registeredCommands.Add(new RegisteredEscCommand(registration, command));
            return registration;
        }

        /// <summary>注销一个已注册的 Esc Command。</summary>
        /// <param name="registration">待注销的注册句柄。</param>
        /// <returns>找到并移除时返回 true。</returns>
        public bool Unregister(EscCommandRegistration registration)
        {
            if (!registration.IsValid) return false;
            for (int index = registeredCommands.Count - 1; index >= 0; index--)
            {
                if (registeredCommands[index].Registration != registration) continue;
                registeredCommands.RemoveAt(index);
                return true;
            }

            return false;
        }

        /// <summary>读取当前栈顶的 Esc Command。</summary>
        /// <param name="command">栈顶 Command。</param>
        /// <returns>存在可执行 Command 时返回 true。</returns>
        public bool TryGetCurrent(out ICommand command)
        {
            if (registeredCommands.Count == 0)
            {
                command = null;
                return false;
            }

            command = registeredCommands[registeredCommands.Count - 1].Command;
            return true;
        }

        #endregion

        #region Manager 生命周期

        /// <summary>初始化 Esc 栈；架构启动时栈应为空。</summary>
        protected override void OnInit()
        {
            registeredCommands.Clear();
            nextRegistrationId = 0;
        }

        /// <summary>架构注销时清理所有窗口退出注册。</summary>
        protected override void OnDeinit()
        {
            registeredCommands.Clear();
        }

        #endregion

        #region 内部类型

        /// <summary>保存注册句柄与退出 Command 的内部条目。</summary>
        private readonly struct RegisteredEscCommand
        {
            /// <summary>创建内部注册条目。</summary>
            /// <param name="registration">注册句柄。</param>
            /// <param name="command">退出 Command。</param>
            public RegisteredEscCommand(EscCommandRegistration registration, ICommand command)
            {
                Registration = registration;
                Command = command;
            }

            /// <summary>注册句柄。</summary>
            public EscCommandRegistration Registration { get; }

            /// <summary>退出 Command。</summary>
            public ICommand Command { get; }
        }

        #endregion
    }
}
