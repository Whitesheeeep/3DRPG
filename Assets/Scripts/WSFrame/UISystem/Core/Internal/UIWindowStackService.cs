using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口栈服务，负责编排连续弹窗顺序。
    /// </summary>
    internal sealed class UIWindowStackService
    {
        private readonly UIWindowLifecycleService lifecycleService;
        private readonly List<WindowBase> windowStack = new List<WindowBase>();
        private bool startPopStackWindowStatus;

        /// <summary>
        /// 创建窗口栈服务。
        /// </summary>
        /// <param name="lifecycleService">窗口生命周期服务。</param>
        public UIWindowStackService(UIWindowLifecycleService lifecycleService)
        {
            this.lifecycleService = lifecycleService;
            this.lifecycleService.WindowClosed += PopNextStackWindow;
        }

        /// <summary>
        /// 开始弹出栈内第一个窗口。
        /// </summary>
        public void StartPopFirstStackWindow()
        {
            if (startPopStackWindowStatus)
            {
                return;
            }

            startPopStackWindowStatus = true;
            PopStackWindow();
        }

        /// <summary>
        /// 压入一个窗口到栈中。
        /// </summary>
        /// <param name="popCallBack">窗口弹出后的回调。</param>
        /// <param name="single">是否只允许栈内或已显示窗口存在一个。</param>
        /// <param name="pushToStackTop">是否插入到栈顶优先弹出。</param>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PushWindowToStack<T>(Action<WindowBase> popCallBack = null, bool single = false, bool pushToStackTop = false)
            where T : WindowBase, new()
        {
            string windowName = typeof(T).Name;
            if (single)
            {
                foreach (WindowBase item in windowStack)
                {
                    if (item.Name.Equals(windowName))
                    {
                        return;
                    }
                }

                WindowBase visibleWindow = lifecycleService.GetWindow<T>(false);
                if (visibleWindow != null)
                {
                    Debug.Log($"{windowName} 弹窗已显示，single模式不处理压栈");
                    visibleWindow.OnShow();
                    return;
                }
            }

            Debug.Log($"Stack Window Push :{windowName}");
            T window = new T { PopStackListener = popCallBack, Name = windowName };
            if (pushToStackTop)
            {
                windowStack.Insert(0, window);
                return;
            }

            windowStack.Add(window);
        }

        /// <summary>
        /// 压入窗口并开始弹出。
        /// </summary>
        /// <param name="popCallBack">窗口弹出后的回调。</param>
        /// <param name="single">是否只允许存在一个。</param>
        /// <param name="pushToStackTop">是否插入到栈顶优先弹出。</param>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PushAndPopStackWindow<T>(Action<WindowBase> popCallBack = null, bool single = false, bool pushToStackTop = false)
            where T : WindowBase, new()
        {
            PushWindowToStack<T>(popCallBack, single, pushToStackTop);
            StartPopFirstStackWindow();
        }

        /// <summary>
        /// 弹出栈内下一个窗口。
        /// </summary>
        /// <returns>成功弹出返回 true。</returns>
        public bool PopStackWindow()
        {
            if (windowStack.Count <= 0)
            {
                startPopStackWindowStatus = false;
                return false;
            }

            WindowBase window = windowStack[0];
            windowStack.RemoveAt(0);
            PopStackWindowAsync(window).Forget();
            return true;
        }

        /// <summary>
        /// 清空窗口栈。
        /// </summary>
        public void ClearStackWindows()
        {
            windowStack.Clear();
        }

        private void PopNextStackWindow(WindowBase windowBase)
        {
            if (windowBase != null && startPopStackWindowStatus && windowBase.PopStack)
            {
                windowBase.PopStack = false;
                PopStackWindow();
            }
        }

        private async UniTaskVoid PopStackWindowAsync(WindowBase window)
        {
            WindowBase popWindow = await lifecycleService.PopUpWindowAsync(window);
            if (popWindow == null)
            {
                PopStackWindow();
                return;
            }

            popWindow.PopStackListener = window.PopStackListener;
            popWindow.PopStack = true;
            popWindow.PopStackListener?.Invoke(popWindow);
            popWindow.PopStackListener = null;
        }
    }
}
