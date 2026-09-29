using System;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 静态任务面板窗口，负责按 WindowBase 生命周期初始化绑定并处理关闭操作。
    /// </summary>
    [InfoBox("依赖同一根节点上的 TaskWindowDataComponent；窗口结构遵循 TemplateWindow 的 UIMask 与 UIContent 约定。")]
    public sealed class TaskWindow : WindowBase
    {
        #region 依赖字段

        // 序列化 UI 引用由同根节点 DataComponent 提供，避免运行时按层级名称搜索。
        private TaskWindowDataComponent data;

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
            AddButtonClickListener(data.CloseButton, CloseWindow);
            WSLog.Log("[TaskWindow] 初始化完成，关闭按钮与任务列表模板已绑定。");
        }

        #endregion

        #region 窗口操作

        /// <summary>响应关闭按钮并请求 UIManager 隐藏任务窗口。</summary>
        private void CloseWindow()
        {
            WSLog.Log("[TaskWindow] 收到关闭请求，开始隐藏任务窗口。");
            HideWindow();
        }

        #endregion
    }
}
