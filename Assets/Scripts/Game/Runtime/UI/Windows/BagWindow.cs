using RPG.Game;
using RPG.Game.UI.Controllers;
using RPG.Game.UI.Escape;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.UIModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 背包运行时窗口。
    /// WindowBase 负责窗口生命周期，本类只把生命周期转发给同根节点 Controller。
    /// </summary>
    [InfoBox("依赖同一根节点上的 BagWindowController 和 BagWindowDataComponent。")]
    public sealed class BagWindow : WindowBase
    {
        #region 依赖字段

        private BagWindowController controller;
        private EscCommandRegistration escCommandRegistration;
        private IArchitecture escapeArchitecture;

        #endregion

        #region 生命周期

        /// <summary>初始化序列化数据组件和同根节点 Controller。</summary>
        public override void OnAwake()
        {
            BagWindowDataComponent data = GameObject.GetComponent<BagWindowDataComponent>();
            if (data == null) throw new System.InvalidOperationException("[BagWindow] 根节点缺少 BagWindowDataComponent。");
            data.ValidateConfiguration();
            // 与其它 WSFrame Window 的生成绑定保持一致：先设置层级和动画策略，再初始化 WindowBase。
            FullScreenWindow = data.IsFullWindow;
            SetDoAnimation(data.DoAnimation);
            base.OnAwake();
            controller = GameObject.GetComponent<BagWindowController>();
            if (controller == null) throw new System.InvalidOperationException("[BagWindow] 根节点缺少 BagWindowController。");
            controller.Initialize(data);
        }

        /// <summary>窗口显示时立即刷新动态区域，并确保动态图集准备已经启动。</summary>
        public override void OnShow()
        {
            base.OnShow();
            // UIManager 对已可见窗口的重复打开也会调用 OnShow；先注销旧句柄，避免 Esc 栈重复堆积。
            UnregisterEscCommand();
            escapeArchitecture = GameArchitecture.Interface;
            escCommandRegistration = escapeArchitecture.SendCommand(
                new RegisterEscCommand(new CloseBagWindowCommand()));
            controller.OnWindowShown();
        }

        /// <summary>
        /// 在窗口真正显示前启动动态图集准备；该方法不改变窗口可见性。
        /// </summary>
        public void PrepareOpen()
        {
            controller.PrepareOpen();
        }

        /// <summary>等待背包配置图集完成本轮加载尝试，供 Bag 打开协调流程使用。</summary>
        /// <returns>图集准备任务；部分地址失败时任务仍会在所有尝试完成后成功结束。</returns>
        public Cysharp.Threading.Tasks.UniTask PrepareOpenAsync()
        {
            return controller.PrepareOpenAsync();
        }

        /// <summary>窗口隐藏完成时启动动态图集延迟释放。</summary>
        public override void OnHide()
        {
            UnregisterEscCommand();
            controller.OnWindowHidden();
            base.OnHide();
        }

        /// <summary>销毁窗口时幂等释放 Controller 和动态图集租约。</summary>
        public override void OnDestroy()
        {
            UnregisterEscCommand();
            controller?.Dispose();
            base.OnDestroy();
        }

        /// <summary>注销 BagWindow 当前显示期间注册的 Esc Command。</summary>
        private void UnregisterEscCommand()
        {
            if (!escCommandRegistration.IsValid) return;
            escapeArchitecture?.SendCommand(new UnregisterEscCommand(escCommandRegistration));
            escCommandRegistration = default;
        }

        #endregion

    }
}
