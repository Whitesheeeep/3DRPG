using System;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 顶层窗口变化事件参数。
    /// </summary>
    public sealed class UIWindowTopChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 创建顶层窗口变化事件参数。
        /// </summary>
        /// <param name="oldTop">变化前的顶层窗口快照。</param>
        /// <param name="hasOldTop">变化前是否存在顶层窗口。</param>
        /// <param name="newTop">变化后的顶层窗口快照。</param>
        /// <param name="hasNewTop">变化后是否存在顶层窗口。</param>
        public UIWindowTopChangedEventArgs(
            UIWindowSnapshot oldTop,
            bool hasOldTop,
            UIWindowSnapshot newTop,
            bool hasNewTop)
        {
            OldTop = oldTop;
            HasOldTop = hasOldTop;
            NewTop = newTop;
            HasNewTop = hasNewTop;
        }

        /// <summary>
        /// 变化前的顶层窗口快照。
        /// </summary>
        public UIWindowSnapshot OldTop { get; }

        /// <summary>
        /// 变化前是否存在顶层窗口。
        /// </summary>
        public bool HasOldTop { get; }

        /// <summary>
        /// 变化后的顶层窗口快照。
        /// </summary>
        public UIWindowSnapshot NewTop { get; }

        /// <summary>
        /// 变化后是否存在顶层窗口。
        /// </summary>
        public bool HasNewTop { get; }
    }
}
