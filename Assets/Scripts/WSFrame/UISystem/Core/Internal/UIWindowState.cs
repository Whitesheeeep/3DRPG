namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口运行时状态，用于 UIManager 内部管理窗口生命周期。
    /// </summary>
    public enum UIWindowState
    {
        /// <summary>
        /// 窗口正在异步加载、实例化或绑定。
        /// </summary>
        Loading,

        /// <summary>
        /// 窗口实例存在，但当前不可见。
        /// </summary>
        Hidden,

        /// <summary>
        /// 窗口正在进入显示流程，后续动画系统会在该状态等待显示动画完成。
        /// </summary>
        Showing,

        /// <summary>
        /// 窗口已经稳定显示。
        /// </summary>
        Visible,

        /// <summary>
        /// 窗口正在进入隐藏流程，后续动画系统会在该状态等待隐藏动画完成。
        /// </summary>
        Hiding
    }
}
