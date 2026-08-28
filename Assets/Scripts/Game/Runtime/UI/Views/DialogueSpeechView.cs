using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WS_Modules.UIModule
{
    /// <summary>DialogueWindow 的对白 View，负责文本显示和推进按钮意图。</summary>
    public sealed class DialogueSpeechView : IDisposable
    {
        #region 字段与依赖

        // View 只持有窗口组合根注入的控件，不主动查找业务系统或其他窗口。
        private readonly Button advanceButton;
        private readonly TMP_Text speakerNameText;
        private readonly TMP_Text speechContentText;
        private readonly TMProTypeWriter typeWriter;
        private int revealVersion;
        private bool revealPending;
        private bool disposed;

        #endregion

        #region 事件

        /// <summary>用户点击或通过 EventSystem Submit 推进对白时触发。</summary>
        public event Action AdvanceRequested;

        /// <summary>当前对白文本自然显示完成或成功跳过后触发。</summary>
        public event Action RevealCompleted;

        /// <summary>获取当前正文是否仍在打字或淡入显示。</summary>
        public bool IsRevealing => !disposed && revealPending;

        #endregion

        #region 构造与生命周期

        /// <summary>创建绑定 DialogueWindow 控件的对白 View。</summary>
        /// <param name="advanceButton">承接点击和 Unity UI Submit 的按钮。</param>
        /// <param name="speakerNameText">说话人文本。</param>
        /// <param name="speechContentText">对白正文文本。</param>
        /// <param name="typeWriter">绑定正文 TMP 的打字机组件。</param>
        public DialogueSpeechView(Button advanceButton, TMP_Text speakerNameText,
            TMP_Text speechContentText, TMProTypeWriter typeWriter)
        {
            this.advanceButton = advanceButton ?? throw new ArgumentNullException(nameof(advanceButton));
            this.speakerNameText = speakerNameText ?? throw new ArgumentNullException(nameof(speakerNameText));
            this.speechContentText = speechContentText ??
                throw new ArgumentNullException(nameof(speechContentText));
            this.typeWriter = typeWriter ?? throw new ArgumentNullException(nameof(typeWriter));
            advanceButton.onClick.AddListener(HandleAdvanceClicked);
        }

        #endregion

        #region 状态刷新

        /// <summary>刷新当前说话人并启动正文显示效果。</summary>
        /// <param name="speakerName">说话人名称。</param>
        /// <param name="content">对白正文。</param>
        public void RefreshSpeech(string speakerName, string content)
        {
            ThrowIfDisposed();
            speakerNameText.text = speakerName ?? string.Empty;
            string text = content ?? string.Empty;
            int version = ++revealVersion;
            revealPending = true;
            RunRevealAsync(text, version).Forget();
        }

        /// <summary>设置推进按钮是否可交互；存在 Choice 时由 Controller 传入 false。</summary>
        /// <param name="enabled">按钮是否可点击和可被 UI Submit。</param>
        public void SetAdvanceEnabled(bool enabled)
        {
            ThrowIfDisposed();
            advanceButton.interactable = enabled;
        }

        /// <summary>将 EventSystem 焦点放到推进按钮，供键盘或手柄 Submit 使用。</summary>
        public void FocusAdvance()
        {
            ThrowIfDisposed();
            if (!advanceButton.interactable || EventSystem.current == null) return;
            EventSystem.current.SetSelectedGameObject(advanceButton.gameObject);
        }

        /// <summary>清空文本并取消属于本 View 的 UI 焦点。</summary>
        public void Clear()
        {
            ThrowIfDisposed();
            revealVersion++;
            revealPending = false;
            typeWriter.StopReveal(true);
            speakerNameText.text = string.Empty;
            speechContentText.text = string.Empty;
            SetAdvanceEnabled(false);
            ClearSelectionIfOwned();
        }

        #endregion

        #region 释放

        /// <summary>移除按钮监听并释放 View 事件，控件生命周期仍由 Unity Window 管理。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            revealVersion++;
            revealPending = false;
            typeWriter.StopReveal(true);
            advanceButton.onClick.RemoveListener(HandleAdvanceClicked);
            AdvanceRequested = null;
            RevealCompleted = null;
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 处理背景按钮输入：显示未完成时只跳过文字，显示完成后才转发推进意图。
        /// </summary>
        private void HandleAdvanceClicked()
        {
            if (IsRevealing)
            {
                // TMProTypeWriter 自己处理 noSkipDuration；无论是否成功跳过，本次输入都不能推进节点。
                typeWriter.Skip();
                return;
            }

            AdvanceRequested?.Invoke();
        }

        /// <summary>等待 TypeWriter 完成当前文本，并过滤被新句或清理流程取消的旧任务。</summary>
        /// <param name="text">待显示的对白正文。</param>
        /// <param name="version">本次显示版本。</param>
        private async UniTask RunRevealAsync(string text, int version)
        {
            await typeWriter.ShowText(text);
            if (disposed || version != revealVersion)
                return;

            revealPending = false;
            RevealCompleted?.Invoke();
        }

        /// <summary>仅清理当前选中对象属于推进按钮时的 EventSystem 焦点。</summary>
        private void ClearSelectionIfOwned()
        {
            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == advanceButton.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>拒绝在 View 释放后继续刷新控件。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(DialogueSpeechView));
        }

        #endregion
    }
}
