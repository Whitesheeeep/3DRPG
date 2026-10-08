using System;
using RPG.Game.UI.Controllers;
using Sirenix.OdinInspector;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI
{
    /// <summary>按 TemplateWindow 结构显示开始游戏入口并响应用户点击。</summary>
    [InfoBox("依赖同根 GameStartWindowDataComponent 和 GameStartWindowController；Prefab 必须包含同级全屏 UIMask 与 UIContent。缺少引用时初始化失败。")]
    public sealed class GameStartWindow : WindowBase
    {
        #region 窗口依赖字段

        private GameStartWindowDataComponent data;
        private GameStartWindowController controller;

        #endregion

        #region 生命周期

        /// <summary>校验 TemplateWindow 控件绑定并初始化开始按钮回调。</summary>
        /// <exception cref="InvalidOperationException">Prefab 缺少必需窗口组件或控件配置时抛出。</exception>
        public override void OnAwake()
        {
            data = GameObject.GetComponent<GameStartWindowDataComponent>();
            controller = GameObject.GetComponent<GameStartWindowController>();
            if (data == null || controller == null)
                throw new InvalidOperationException("[GameStartWindow] 根节点缺少 DataComponent 或 Controller。");

            data.ValidateConfiguration();
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();
            controller.Initialize(data);
            AddButtonClickListener(data.StartButton, controller.RequestStart);
            WSLog.Log("[GameStartWindow] TemplateWindow 结构校验完成，开始按钮已绑定。");
        }

        /// <summary>窗口重新显示时清理上一次请求留下的按钮禁用状态和提示。</summary>
        public override void OnShow()
        {
            base.OnShow();
            controller?.ResetForOpen();
        }

        /// <summary>释放按钮回调和 Controller 对 DataComponent 的引用。</summary>
        public override void OnDestroy()
        {
            controller?.Dispose();
            controller = null;
            data = null;
            base.OnDestroy();
        }

        #endregion
    }
}
