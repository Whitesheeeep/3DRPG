using System;
using Cysharp.Threading.Tasks;
using RPG.Game;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Escape;
using RPG.Game.UI.Task;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 任务窗口负责连接 WindowBase 生命周期、任务面板控制器和 Esc 关闭命令。
    /// </summary>
    [InfoBox("依赖同一根节点上的 TaskWindowDataComponent 与 TaskWindowController；窗口对象和条目均由 Prefab 显式绑定。")]
    public sealed class TaskWindow : WindowBase
    {
        #region 依赖字段

        // 依赖字段：DataComponent 保存窗口组件所需的 Prefab 引用；Controller 管理任务浏览和数据同步。
        private TaskWindowDataComponent data;
        private TaskWindowController controller;
        private EscCommandRegistration escCommandRegistration;
        private IArchitecture escapeArchitecture;

        #endregion

        #region 生命周期

        /// <summary>
        /// 校验 Prefab 绑定并初始化 WindowBase 与任务面板控制器。
        /// </summary>
        /// <exception cref="InvalidOperationException">窗口 Prefab 缺少必需组件时抛出。</exception>
        public override void OnAwake()
        {
            data = GameObject.GetComponent<TaskWindowDataComponent>();
            if (data == null)
                throw new InvalidOperationException("[TaskWindow] 根节点缺少 TaskWindowDataComponent。");

            data.ValidateConfiguration();
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();

            controller = GameObject.GetComponent<TaskWindowController>();
            if (controller == null)
                throw new InvalidOperationException("[TaskWindow] 根节点缺少 TaskWindowController。");

            controller.Initialize(data);
            AddButtonClickListener(data.CloseButton, CloseWindow);
            WSLog.Log("[TaskWindow] 初始化完成，窗口绑定与关闭监听已就绪。");
        }

        /// <summary>
        /// 窗口显示时注册 Esc 关闭命令并刷新任务快照。
        /// </summary>
        public override void OnShow()
        {
            base.OnShow();
            UnregisterEscCommand();
            escapeArchitecture = GameArchitecture.Interface;
            escCommandRegistration = escapeArchitecture.SendCommand(
                new RegisterEscCommand(new CloseTaskWindowCommand()));
            controller.OnWindowShown();
        }

        /// <summary>
        /// 窗口隐藏时注销 Esc 命令和后端事件订阅。
        /// </summary>
        public override void OnHide()
        {
            UnregisterEscCommand();
            controller.OnWindowHidden();
            base.OnHide();
        }

        /// <summary>
        /// 窗口销毁时幂等释放 Controller 的按钮与后端订阅。
        /// </summary>
        public override void OnDestroy()
        {
            UnregisterEscCommand();
            controller?.Dispose();
            base.OnDestroy();
        }

        #endregion

        #region 后端绑定与关闭

        /// <summary>
        /// 注入面板数据后端；调用方持有后端实例的生命周期。
        /// </summary>
        /// <param name="backend">提供查询、未读确认和追踪操作的后端。</param>
        /// <exception cref="InvalidOperationException">窗口尚未完成初始化时抛出。</exception>
        public void BindBackend(ITaskWindowBackend backend)
        {
            if (controller == null)
                throw new InvalidOperationException("[TaskWindow] 初始化完成前不能绑定任务后端。");

            controller.BindBackend(backend);
        }

        /// <summary>
        /// 关闭窗口并确保该窗口的 Esc 命令不会残留在栈中。
        /// </summary>
        private void CloseWindow()
        {
            WSLog.Log("[TaskWindow] 收到关闭请求，开始隐藏任务窗口。");
            HideWindow();
        }

        /// <summary>
        /// 注销当前显示周期注册的 Esc Command。
        /// </summary>
        private void UnregisterEscCommand()
        {
            if (!escCommandRegistration.IsValid)
                return;

            escapeArchitecture.SendCommand(new UnregisterEscCommand(escCommandRegistration));
            escCommandRegistration = default;
        }

        #endregion

        #region Esc 命令

        /// <summary>
        /// 将 Esc 请求转交 UIManager 隐藏任务窗口。
        /// </summary>
        private sealed class CloseTaskWindowCommand : AbstractCommand
        {
            /// <summary>
            /// 执行异步隐藏并让 UIManager 完成窗口生命周期回调。
            /// </summary>
            protected override void OnExecute()
            {
                UIManager.Instance.HideWindowAsync<TaskWindow>().Forget();
            }
        }

        #endregion
    }
}
