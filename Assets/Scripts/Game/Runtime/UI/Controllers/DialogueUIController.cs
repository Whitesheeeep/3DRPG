using System;
using System.Collections.Generic;
using RPG.DialogueSystemModule;
using WS_Modules.LogModule;
using WS_Modules.UIModule;

namespace RPG.Game.UI
{
    /// <summary>连接 DialogueSystem 与 DialogueWindow 内两个 View 的窗口级 MVC Controller。</summary>
    public sealed class DialogueUIController : IDisposable
    {
        #region 依赖与状态

        // Controller 不查找业务或控件依赖；所有依赖由 DialogueWindow 组合根注入。
        private readonly DialogueWindow window;
        private readonly DialogueSystem dialogueSystem;
        private readonly DialogueSpeechView speechView;
        private readonly DialogueChoiceView choiceView;
        private readonly List<DialogueChoiceSnapShot> pendingChoices = new();
        private DialogueSpeechNode displayedSpeech;
        private bool disposed;

        #endregion

        #region 构造

        /// <summary>创建并绑定 DialogueWindow 的领域事件与 View 意图。</summary>
        /// <param name="window">所属 DialogueWindow。</param>
        /// <param name="dialogueSystem">同步对话系统。</param>
        /// <param name="speechView">对白 View。</param>
        /// <param name="choiceView">选项 View。</param>
        public DialogueUIController(DialogueWindow window, DialogueSystem dialogueSystem,
            DialogueSpeechView speechView, DialogueChoiceView choiceView)
        {
            this.window = window ?? throw new ArgumentNullException(nameof(window));
            this.dialogueSystem = dialogueSystem ?? throw new ArgumentNullException(nameof(dialogueSystem));
            this.speechView = speechView ?? throw new ArgumentNullException(nameof(speechView));
            this.choiceView = choiceView ?? throw new ArgumentNullException(nameof(choiceView));

            dialogueSystem.Started += OnStarted;
            dialogueSystem.SpeechPresented += OnSpeechPresented;
            dialogueSystem.ChoicePresented += OnChoicePresented;
            dialogueSystem.Ended += OnEnded;
            speechView.AdvanceRequested += OnAdvanceRequested;
            speechView.RevealCompleted += OnRevealCompleted;
            choiceView.ChoiceRequested += OnChoiceRequested;

            // 预加载或脚本域重建期间可能已经存在会话，绑定完成后立即恢复当前画面。
            if (dialogueSystem.CurrentSession != null && !dialogueSystem.CurrentSession.IsEnded)
                RefreshCurrentState(dialogueSystem.CurrentSession);
        }

        #endregion

        #region 领域事件

        /// <summary>收到 Started 后打开窗口；若首句事件尚未到达则恢复当前首屏。</summary>
        /// <param name="eventArgs">对话启动事件。</param>
        private void OnStarted(DialogueStartedEvent eventArgs)
        {
            if (disposed) return;
            // DialogueSystem 的首句顺序是 SpeechPresented、ChoicePresented、Started；同一句已刷新时不能重启打字机。
            if (!ReferenceEquals(displayedSpeech, eventArgs.Session.CurrentSpeech))
                RefreshCurrentState(eventArgs.Session);
            if (!window.Visible) UIManager.Instance.PopUpWindow<DialogueWindow>();
        }

        /// <summary>收到 Speech 展示事实后刷新正文并清理旧选项。</summary>
        /// <param name="eventArgs">对白展示事件。</param>
        private void OnSpeechPresented(DialogueSpeechPresentedEvent eventArgs)
        {
            if (disposed) return;
            displayedSpeech = eventArgs.Speech;
            pendingChoices.Clear();
            choiceView.Clear();
            RefreshSpeech(eventArgs.Speech);
            // 有 Choice 的句子在打字期间也需要保留按钮用于 Skip；显示完成后才会由 OnRevealCompleted 禁用。
            speechView.SetAdvanceEnabled(true);
            speechView.FocusAdvance();
        }

        /// <summary>收到 Choice 展示事实后缓存选项；正文完成后才显示选项区域。</summary>
        /// <param name="eventArgs">选项展示事件。</param>
        private void OnChoicePresented(DialogueChoicePresentedEvent eventArgs)
        {
            if (disposed) return;
            pendingChoices.Clear();
            if (eventArgs.Choices != null)
            {
                for (int index = 0; index < eventArgs.Choices.Count; index++)
                    pendingChoices.Add(eventArgs.Choices[index]);
            }

            if (speechView.IsRevealing)
            {
                // 选项暂存期间按钮继续承担 Skip，不能让 Advance 穿透到 DialogueSystem。
                choiceView.Clear();
                speechView.SetAdvanceEnabled(true);
                speechView.FocusAdvance();
                return;
            }

            ShowPendingChoices();
        }

        /// <summary>收到结束事实后清理 View 并隐藏 DialogueWindow。</summary>
        /// <param name="eventArgs">结束事件。</param>
        private void OnEnded(DialogueEndedEvent eventArgs)
        {
            if (disposed) return;
            displayedSpeech = null;
            pendingChoices.Clear();
            choiceView.Clear();
            speechView.Clear();
            if (window.Visible) UIManager.Instance.HideWindow<DialogueWindow>();
            if (eventArgs.Status == DialogueEndStatus.Failed)
                UnityEngine.Debug.LogError($"对话结束失败：{eventArgs.Message}");
        }

        #endregion

        #region View 意图

        /// <summary>把对白 View 的推进意图交给 DialogueSystem。</summary>
        private void OnAdvanceRequested()
        {
            if (disposed) return;
            DialogueStepResult result = dialogueSystem.Advance();
            LogUnexpectedStepResult(result, "Advance");
        }

        /// <summary>把选项 View 的稳定 NodeId 交给 DialogueSystem。</summary>
        /// <param name="nodeId">ChoiceNode 稳定 NodeId。</param>
        private void OnChoiceRequested(string nodeId)
        {
            if (disposed) return;
            DialogueStepResult result = dialogueSystem.SelectChoice(nodeId);
            LogUnexpectedStepResult(result, "SelectChoice");
        }

        #endregion

        #region 状态刷新与释放

        /// <summary>从当前会话恢复正文和 Choice 展示，兼容事件绑定晚于首句的情况。</summary>
        /// <param name="session">当前对话会话。</param>
        private void RefreshCurrentState(DialogueSession session)
        {
            if (session == null || session.IsEnded || session.CurrentSpeech == null) return;
            displayedSpeech = session.CurrentSpeech;
            pendingChoices.Clear();
            choiceView.Clear();
            RefreshSpeech(session.CurrentSpeech);
            if (session.CurrentChoices.Count > 0)
            {
                CacheCurrentChoices();
                if (speechView.IsRevealing)
                {
                    speechView.SetAdvanceEnabled(true);
                    speechView.FocusAdvance();
                }
                else
                {
                    ShowPendingChoices();
                }
            }
            else
            {
                speechView.SetAdvanceEnabled(true);
                speechView.FocusAdvance();
            }
        }

        /// <summary>刷新对白 View 的说话人和正文。</summary>
        /// <param name="speech">当前对白节点。</param>
        private void RefreshSpeech(DialogueSpeechNode speech)
        {
            string speakerName = speech.Speaker != null ? speech.Speaker.SpeakerName : "<empty>";
            speechView.RefreshSpeech(speakerName, speech.Text);
        }

        /// <summary>缓存当前 DialogueSystem 的 Choice 展示快照，等待正文完成。</summary>
        private void CacheCurrentChoices()
        {
            pendingChoices.Clear();
            IReadOnlyList<DialogueChoiceSnapShot> choices = dialogueSystem.CurrentChoicePresentations;
            if (choices == null) return;
            for (int index = 0; index < choices.Count; index++)
                pendingChoices.Add(choices[index]);
        }

        /// <summary>响应正文完成事件，在存在待展示 Choice 时切换输入焦点到选项区域。</summary>
        private void OnRevealCompleted()
        {
            if (disposed) return;
            if (pendingChoices.Count == 0)
            {
                speechView.SetAdvanceEnabled(true);
                speechView.FocusAdvance();
                return;
            }

            ShowPendingChoices();
        }

        /// <summary>刷新并显示已缓存的 Choice，同时关闭背景推进按钮。</summary>
        private void ShowPendingChoices()
        {
            if (pendingChoices.Count == 0) return;
            speechView.SetAdvanceEnabled(false);
            choiceView.RefreshChoices(pendingChoices);
            choiceView.SetVisible(true);
            pendingChoices.Clear();
        }

        /// <summary>记录不应由当前 UI 意图触发的 DialogueStep 状态。</summary>
        /// <param name="result">DialogueSystem 返回结果。</param>
        /// <param name="operation">操作名称。</param>
        private static void LogUnexpectedStepResult(DialogueStepResult result, string operation)
        {
            if (result.Status == DialogueStepStatus.Advanced || result.Status == DialogueStepStatus.Ended)
                return;
            WSLog.LogWarning($"Dialogue UI {operation} 未执行：{result.Status}，{result.Message}");
        }

        /// <summary>解除领域事件和 View 意图订阅；不释放外部注入对象。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            dialogueSystem.Started -= OnStarted;
            dialogueSystem.SpeechPresented -= OnSpeechPresented;
            dialogueSystem.ChoicePresented -= OnChoicePresented;
            dialogueSystem.Ended -= OnEnded;
            speechView.AdvanceRequested -= OnAdvanceRequested;
            speechView.RevealCompleted -= OnRevealCompleted;
            choiceView.ChoiceRequested -= OnChoiceRequested;
            pendingChoices.Clear();
            displayedSpeech = null;
        }

        #endregion
    }
}
