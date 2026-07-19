using UnityEngine;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口运行时记录，只保存管理层状态，Unity 对象引用从 WindowBase 转发。
    /// </summary>
    internal sealed class UIWindowRecord
    {
        /// <summary>
        /// 创建窗口运行时记录。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        /// <param name="window">窗口逻辑对象。</param>
        /// <param name="state">窗口初始状态。</param>
        public UIWindowRecord(string windowName, WindowBase window, UIWindowState state)
        {
            WindowName = windowName;
            Window = window;
            State = state;
        }

        /// <summary>
        /// 窗口名称。
        /// </summary>
        public string WindowName { get; }

        /// <summary>
        /// 窗口逻辑对象。
        /// </summary>
        public WindowBase Window { get; }

        /// <summary>
        /// 当前窗口状态。
        /// </summary>
        public UIWindowState State { get; private set; }

        /// <summary>
        /// 当前窗口物体，从 WindowBase 转发。
        /// </summary>
        public GameObject GameObject => Window?.GameObject;

        /// <summary>
        /// 当前窗口 Transform，从 WindowBase 转发。
        /// </summary>
        public Transform Transform => Window?.Transform;

        /// <summary>
        /// 当前窗口 Canvas，从 WindowBase 转发。
        /// </summary>
        public Canvas Canvas => Window?.Canvas;

        /// <summary>
        /// 设置窗口状态。
        /// </summary>
        /// <param name="state">目标状态。</param>
        public void SetState(UIWindowState state)
        {
            State = state;
        }
    }
}
