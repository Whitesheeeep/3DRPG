using System;
using RPG.Game.UI.Controllers;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;
using WS_Modules.UIModule;
using Sirenix.OdinInspector;

namespace WS_Modules.UIModule
{
    /// <summary>承载场景加载进度、当前任务和失败提示的全屏窗口。</summary>
    [InfoBox("依赖同根 LoadingWindowDataComponent 与 LoadingWindowController；窗口按 TemplateWindow 的 UIMask／UIContent 结构配置，缺少引用时初始化会失败。")]
    public sealed class LoadingWindow : WindowBase
    {
        #region 窗口依赖字段

        // 同根 Prefab 组件分别保存显式 View 引用并投影流程快照。
        private LoadingWindowDataComponent data;
        private LoadingWindowController controller;

        #endregion

        #region 窗口生命周期

        /// <summary>校验 Prefab 视图、初始化窗口层级行为并绑定同根控制器。</summary>
        /// <exception cref="InvalidOperationException">Prefab 缺少必需的数据或控制器组件时抛出。</exception>
        public override void OnAwake()
        {
            data = GameObject.GetComponent<LoadingWindowDataComponent>();
            if (data == null)
                throw new InvalidOperationException("[LoadingWindow] 根节点缺少 LoadingWindowDataComponent。");

            data.ValidateConfiguration();
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();
            controller = GameObject.GetComponent<LoadingWindowController>();
            if (controller == null)
                throw new InvalidOperationException("[LoadingWindow] 根节点缺少 LoadingWindowController。");
            controller.Initialize(data, HandleCloseRequested);
            WSLog.Log("[LoadingWindow] TemplateWindow 结构校验完成，静态进度视图已初始化。");
        }

        /// <summary>窗口销毁时解除控制器与按钮回调。</summary>
        public override void OnDestroy()
        {
            controller?.Dispose();
            controller = null;
            data = null;
            base.OnDestroy();
        }

        #endregion

        #region 快照展示

        /// <summary>刷新场景名称、任务进度、整体百分比或终态错误信息。</summary>
        /// <param name="snapshot">当前不可变加载流程快照。</param>
        public void Present(SceneLoadExecutionSnapshot snapshot)
        {
            controller.Render(snapshot);
        }

        /// <summary>响应终态提示按钮并交由 UIManager 执行窗口隐藏生命周期。</summary>
        private void HandleCloseRequested()
        {
            WSLog.Log($"[LoadingWindow] 用户关闭终态加载提示，sceneId={controller.SceneId}。");
            HideWindow();
        }

        #endregion
    }
}
