using System;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口状态变化事件参数。
    /// </summary>
    public sealed class UIWindowStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 创建窗口状态变化事件参数。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="oldState">变化前状态。</param>
        /// <param name="newState">变化后状态。</param>
        /// <param name="snapshot">变化后的窗口快照。</param>
        public UIWindowStateChangedEventArgs(
            string windowName,
            UIWindowState oldState,
            UIWindowState newState,
            UIWindowSnapshot snapshot)
        {
            WindowName = windowName;
            OldState = oldState;
            NewState = newState;
            Snapshot = snapshot;
        }

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { get; }

        /// <summary>
        /// 变化前状态。
        /// </summary>
        public UIWindowState OldState { get; }

        /// <summary>
        /// 变化后状态。
        /// </summary>
        public UIWindowState NewState { get; }

        /// <summary>
        /// 变化后的窗口快照。
        /// </summary>
        public UIWindowSnapshot Snapshot { get; }
    }
}
