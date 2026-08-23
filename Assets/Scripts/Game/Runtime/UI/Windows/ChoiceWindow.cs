// WSFrame WindowCode 生成规则：
// 1. 本文件首次由生成器创建，创建后作为手写窗口逻辑入口。
// 2. 后续重新生成不会整体覆盖本文件。
// 3. 生命周期方法、API 方法、MVVM 绑定和业务逻辑不会被生成器修改。
using Cysharp.Threading.Tasks;
using RPG.InteractionSystem.UI;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// 交互选项窗口的组合根，负责组装 ChoiceWindowView 和 InteractionUIController。
    /// </summary>
    public partial class ChoiceWindow : WindowBase
    {
        #region 组合状态

        // Window 持有 MVC 子对象，保证 WSFrame 销毁窗口时能按逆序释放 UI 资源和事件。
        private ChoiceWindowView view;
        private InteractionUIController controller;
        private UniTask viewInitializationTask;

        #endregion

        #region 生命周期

        /// <summary>
        /// 绑定生成组件、初始化 WindowBase，并组装 ChoiceWindowView 与交互 Controller。
        /// </summary>
        public override void OnAwake()
        {
            BindGeneratedComponents();
            base.OnAwake();

            view = new ChoiceWindowView(
                dataCompt.ChoiceRootTransform,
                dataCompt.OptionPrefabPath,
                dataCompt.initOptionPrefabCount);
            controller = new InteractionUIController(this, view);

            // 保存可重复等待的初始化任务，预加载服务据此确认三个行实例已经创建。
            viewInitializationTask = view.InitializeAsync().Preserve();
        }

        /// <summary>
        /// 窗口显示时激活 Controller，使窗口先于玩家创建时也能在玩家出现后同步状态。
        /// </summary>
        public override void OnShow()
        {
            base.OnShow();
            controller.Activate();
        }

        /// <summary>
        /// 窗口隐藏时保留 View、Controller 和玩家事件监听，等待下一次列表刷新。
        /// </summary>
        public override void OnHide()
        {
            base.OnHide();
        }

        /// <summary>
        /// 按 Controller、View、WindowBase 的顺序释放窗口资源和事件连接。
        /// </summary>
        public override void OnDestroy()
        {
            controller?.Dispose();
            controller = null;

            view?.Dispose();
            view = null;

            base.OnDestroy();
        }

        #endregion

        #region 初始化状态

        /// <summary>
        /// 等待选项 View 完成 prefab 加载和初始行创建。
        /// </summary>
        /// <returns>View 初始化任务。</returns>
        public UniTask WaitUntilReadyAsync() => viewInitializationTask;

        /// <summary>
        /// 在窗口已预加载但尚未显示时激活交互 Controller，并保持当前窗口隐藏。
        /// </summary>
        internal void ActivateInteractionController() => controller.Activate(false);

        #endregion
    }
}
