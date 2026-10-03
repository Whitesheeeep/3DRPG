using System;
using RPG.Game.UI.Controllers;
using RPG.Game;
using RPG.Game.UI.Escape;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 静态任务面板窗口，负责按 WindowBase 生命周期初始化绑定并处理关闭操作。
    /// </summary>
    [InfoBox("依赖同一根节点上的 TaskWindowDataComponent 和 TaskWindowController；窗口结构遵循 TemplateWindow 的 UIMask 与 UIContent 约定。缺少任一组件时窗口初始化会失败。")]
    public sealed class TaskWindow : WindowBase
    {
        #region 依赖字段

        // 序列化 UI 引用由同根节点 DataComponent 提供，避免运行时按层级名称搜索。
        private TaskWindowDataComponent data;
        private TaskWindowController controller;
        private EscCommandRegistration escCommandRegistration;
        private IArchitecture escapeArchitecture;

        #endregion

        #region 生命周期

        /// <summary>校验窗口绑定，设置 WindowBase 行为并注册关闭按钮监听。</summary>
        /// <exception cref="InvalidOperationException">根节点缺少任务窗口 DataComponent 时抛出。</exception>
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
            WSLog.Log("[TaskWindow] 初始化完成，关闭按钮与任务业务 Controller 已绑定。");
        }

        /// <summary>窗口显示后刷新当前任务快照并订阅后续任务变化。</summary>
        public override void OnShow()
        {
            base.OnShow();
            controller.OnWindowShown();
            UnregisterEscCommand();
            escapeArchitecture = GameArchitecture.Interface;
            escCommandRegistration = escapeArchitecture.SendCommand(
                new RegisterEscCommand(new CloseTaskWindowCommand()));
        }

        /// <summary>窗口隐藏时释放任务事件订阅。</summary>
        public override void OnHide()
        {
            controller.OnWindowHidden();
            UnregisterEscCommand();
            base.OnHide();
        }

        /// <summary>窗口销毁时释放 Controller 创建的列表条目和按钮回调。</summary>
        public override void OnDestroy()
        {
            controller?.Dispose();
            UnregisterEscCommand();
            base.OnDestroy();
        }

        #endregion

        #region 窗口操作

        /// <summary>响应关闭按钮并请求 UIManager 隐藏任务窗口。</summary>
        private void CloseWindow()
        {
            WSLog.Log("[TaskWindow] 收到关闭请求，开始隐藏任务窗口。");
            HideWindow();
        }

        /// <summary>移除 TaskWindow 可见期间登记的 Esc 关闭操作。</summary>
        private void UnregisterEscCommand()
        {
            if (!escCommandRegistration.IsValid) return;
            escapeArchitecture?.SendCommand(new UnregisterEscCommand(escCommandRegistration));
            escCommandRegistration = default;
        }

        #endregion
    }
}
