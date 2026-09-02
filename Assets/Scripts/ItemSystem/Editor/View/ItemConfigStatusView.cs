#if UNITY_EDITOR
using System;
using UnityEngine.UIElements;

namespace RPG.ItemSystem.Editor
{
    /// <summary>物品配置窗口底部状态栏的展示子 View。</summary>
    internal sealed class ItemConfigStatusView : IDisposable
    {
        #region 字段

        private readonly Label statusLabel;
        private bool disposed;

        #endregion

        /// <summary>创建状态栏子 View。</summary>
        /// <param name="statusLabel">状态文本控件。</param>
        internal ItemConfigStatusView(Label statusLabel)
        {
            this.statusLabel = statusLabel ?? throw new ArgumentNullException(nameof(statusLabel));
        }

        /// <summary>显示普通状态并清除错误样式。</summary>
        /// <param name="message">状态文本。</param>
        internal void ShowMessage(string message)
        {
            if (disposed) return;
            statusLabel.text = message ?? string.Empty;
            statusLabel.EnableInClassList("item-editor-status--error", false);
        }

        /// <summary>显示错误状态。</summary>
        /// <param name="message">错误文本。</param>
        internal void ShowError(string message)
        {
            if (disposed) return;
            statusLabel.text = string.IsNullOrEmpty(message) ? "发生未知错误。" : message;
            statusLabel.EnableInClassList("item-editor-status--error", true);
        }

        /// <summary>标记状态栏不再接受刷新。</summary>
        public void Dispose() => disposed = true;
    }
}
#endif
