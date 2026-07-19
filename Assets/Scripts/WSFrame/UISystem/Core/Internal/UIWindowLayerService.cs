using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.LogModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口层级服务，负责遮罩归属和智能显隐策略。
    /// </summary>
    internal sealed class UIWindowLayerService
    {
        private readonly bool isSingleMask;
        private readonly bool smartShowHide;

        /// <summary>
        /// 创建窗口层级服务。
        /// </summary>
        /// <param name="isSingleMask">是否使用单遮罩模式。</param>
        /// <param name="smartShowHide">是否启用智能显隐。</param>
        public UIWindowLayerService(bool isSingleMask, bool smartShowHide)
        {
            this.isSingleMask = isSingleMask;
            this.smartShowHide = smartShowHide;
        }

        /// <summary>
        /// 窗口显示后刷新遮罩和智能显隐状态。
        /// </summary>
        /// <param name="shownWindow">刚显示的窗口。</param>
        /// <param name="visibleWindows">当前可见窗口列表。</param>
        public void OnWindowShown(WindowBase shownWindow, IReadOnlyList<WindowBase> visibleWindows)
        {
            RefreshMask(visibleWindows);
            ApplySmartShow(shownWindow, visibleWindows, false);
        }

        /// <summary>
        /// 窗口隐藏后刷新遮罩和智能显隐状态。
        /// </summary>
        /// <param name="hiddenWindow">刚隐藏的窗口。</param>
        /// <param name="visibleWindows">当前可见窗口列表。</param>
        public void OnWindowHidden(WindowBase hiddenWindow, IReadOnlyList<WindowBase> visibleWindows)
        {
            RefreshMask(visibleWindows);
            ApplySmartHide(hiddenWindow, visibleWindows, true);
        }

        private void RefreshMask(IReadOnlyList<WindowBase> visibleWindows)
        {
            if (!isSingleMask || visibleWindows == null || visibleWindows.Count == 0)
            {
                return;
            }

            WSLog.Log("设置窗口遮罩显示状态，当前可见窗口数量:" + visibleWindows.Count);
            WindowBase maxOrderWindow = null;
            int maxOrder = 0;
            int maxIndex = 0;

            foreach (WindowBase window in visibleWindows)
            {
                if (window?.GameObject == null) continue;

                window.SetMaskVisible(false);
                if (maxOrderWindow == null)
                {
                    maxOrderWindow = window;
                    maxOrder = window.Canvas.sortingOrder;
                    maxIndex = window.Transform.GetSiblingIndex();
                    continue;
                }

                if (maxOrder < window.Canvas.sortingOrder)
                {
                    maxOrderWindow = window;
                    maxOrder = window.Canvas.sortingOrder;
                    maxIndex = window.Transform.GetSiblingIndex();
                }
                else if (maxOrder == window.Canvas.sortingOrder && maxIndex < window.Transform.GetSiblingIndex())
                {
                    maxOrderWindow = window;
                    maxIndex = window.Transform.GetSiblingIndex();
                }
            }

            maxOrderWindow?.SetMaskVisible(true);
        }

        private void ApplySmartShow(WindowBase window, IReadOnlyList<WindowBase> visibleWindows, bool canInteract)
        {
            if (!smartShowHide || window == null || !window.FullScreenWindow || visibleWindows == null)
            {
                return;
            }

            try
            {
                if (visibleWindows.Count > 1)
                {
                    WindowBase previousTopWindow = visibleWindows[^2];
                    if (!previousTopWindow.FullScreenWindow && window.Canvas.sortingOrder < previousTopWindow.Canvas.sortingOrder)
                    {
                        return;
                    }
                }

                for (int i = visibleWindows.Count - 1; i >= 0; i--)
                {
                    WindowBase item = visibleWindows[i];
                    if (item != null && item.Name != window.Name)
                    {
                        item.PseudoHidden(canInteract);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("Error:" + exception);
            }
        }

        private void ApplySmartHide(WindowBase window, IReadOnlyList<WindowBase> visibleWindows, bool canInteract)
        {
            if (!smartShowHide || window == null || !window.FullScreenWindow || visibleWindows == null)
            {
                return;
            }

            for (int i = visibleWindows.Count - 1; i >= 0; i--)
            {
                WindowBase item = visibleWindows[i];
                if (item == null || item == window)
                {
                    continue;
                }

                item.PseudoHidden(canInteract);
                if (item.FullScreenWindow)
                {
                    break;
                }
            }
        }
    }
}
