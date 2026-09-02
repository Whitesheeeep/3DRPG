using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using RPG.Game.UI.Events;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 作为 BusinessArchitecture 系统入口，编排同步对话会话与命令式 Condition/Action。
    /// </summary>
    public sealed class DialogueSystem : AbstractSystem
    {
        #region 字段与构造

        // 该列表只保存当前 Choice 的展示快照，不保存命令或运行时服务，避免 UI 反向持有领域对象。
        private readonly List<DialogueChoiceSnapShot> currentChoicePresentations = new();
        private readonly ReadOnlyCollection<DialogueChoiceSnapShot> readOnlyChoicePresentations;

        /// <summary>
        /// 创建一个由资产命令自身执行、无需额外注册入口的对话系统。
        /// </summary>
        public DialogueSystem()
        {
            readOnlyChoicePresentations = currentChoicePresentations.AsReadOnly();
        }

        #endregion

        #region Architecture 生命周期

        /// <summary>
        /// 在业务架构初始化时保持空闲；具体命令依赖在每次执行时通过 Context 解析。
        /// </summary>
        protected override void OnInit()
        {
        }

        /// <summary>
        /// 在业务架构注销时结束活动会话并清理展示状态，避免跨架构持有运行时对象。
        /// </summary>
        protected override void OnDeinit()
        {
            if (CurrentSession is { IsEnded: false })
                CurrentSession.End("DialogueSystem 所属架构已注销。");

            currentChoicePresentations.Clear();
            // 架构注销后解除所有外部订阅，避免窗口或场景对象继续持有已失效的系统实例。
            Started = null;
            SpeechPresented = null;
            ChoicePresented = null;
            Ended = null;
        }

        #endregion

        #region 当前会话

        /// <summary>获取当前唯一活动会话。</summary>
        public DialogueSession CurrentSession { get; private set; }

        /// <summary>获取当前 Choice 的只读展示快照。</summary>
        public IReadOnlyList<DialogueChoiceSnapShot> CurrentChoicePresentations =>
            readOnlyChoicePresentations;

        #endregion

        #region 事实事件

        /// <summary>对话成功开始后的事实事件。</summary>
        public event Action<DialogueStartedEvent> Started;

        /// <summary>SpeechNode 展示后的事实事件。</summary>
        public event Action<DialogueSpeechPresentedEvent> SpeechPresented;

        /// <summary>Choice 展示后的事实事件。</summary>
        public event Action<DialogueChoicePresentedEvent> ChoicePresented;

        /// <summary>对话结束后的事实事件。</summary>
        public event Action<DialogueEndedEvent> Ended;

        #endregion

        #region 会话入口

        /// <summary>
        /// 校验请求并从 EntryNode 同步进入首个 SpeechNode。
        /// </summary>
        /// <param name="request">待启动的强类型请求。</param>
        /// <returns>启动处理结果。</returns>
        public DialogueStartResult TryStartDialogue(DialogueRequest request)
        {
            if (CurrentSession != null && !CurrentSession.IsEnded)
                return new DialogueStartResult(DialogueStartStatus.Busy, "已有对话会话正在运行。", null);

            if (!IsValidRequest(request, out string requestMessage))
                return new DialogueStartResult(DialogueStartStatus.InvalidRequest, requestMessage, null);

            DialogueSession session = new DialogueSession(request);
            CurrentSession = session;
            SubscribeSession(session);
            try
            {
                // 先发布阻断请求，再进入首句；任一外部接收器异常都由同一清理路径收口。
                PublishLooseGameplayTagRequests(session, LooseGameplayTagChangeOperation.Add);
                PublishGameUILockRequest(session, GameUILockOperation.Acquire);
                session.EnterSpeech(request.Asset.EntryNode.FirstSpeechNode);
            }
            catch (Exception exception)
            {
                // Participant 表现和锁定事件都属于外部边界；异常必须对称结束会话并移除来源。
                Debug.LogException(exception);
                session.End($"对话初始化或首个 SpeechNode 执行失败：{exception.Message}");
            }
            if (session.IsEnded)
                return new DialogueStartResult(DialogueStartStatus.Failed,
                    "对话初始化或首个 SpeechNode 执行失败。", null);
            try
            {
                // Started 属于外部 UI 表现边界；订阅者异常也必须结束会话并释放独占来源。
                Started?.Invoke(new DialogueStartedEvent(session));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                session.End($"对话开始表现失败：{exception.Message}");
                return new DialogueStartResult(DialogueStartStatus.Failed,
                    "对话开始表现失败。", null);
            }
            return new DialogueStartResult(DialogueStartStatus.Started, "对话已开始。", session);
        }

        /// <summary>
        /// 推进当前没有 Choice 的 SpeechNode。
        /// </summary>
        /// <returns>推进处理结果。</returns>
        public DialogueStepResult Advance()
        {
            if (CurrentSession == null || CurrentSession.IsEnded)
                return CreateStepResult(DialogueStepStatus.NotRunning, "当前没有运行中的对话。", null);
            if (CurrentSession.CurrentChoices.Count > 0 ||
                CurrentSession.State == DialogueSessionState.WaitingForChoice)
                return CreateStepResult(DialogueStepStatus.ChoiceRequired, "当前对白需要先选择 Choice。", CurrentSession);

            DialogueNode nextNode = CurrentSession.CurrentSpeech?.NextNode;
            return EnterTarget(nextNode);
        }

        /// <summary>
        /// 校验并选择当前 SpeechNode 的一个 Choice，触发 Action 后直接进入 TargetNode。
        /// </summary>
        /// <param name="choiceNodeId">当前 SpeechNode 内 ChoiceNode 的稳定 NodeId。</param>
        /// <returns>选择处理结果。</returns>
        public DialogueStepResult SelectChoice(string choiceNodeId)
        {
            if (CurrentSession == null || CurrentSession.IsEnded)
                return CreateStepResult(DialogueStepStatus.NotRunning, "当前没有运行中的对话。", null);
            if (CurrentSession.State != DialogueSessionState.WaitingForChoice)
                return CreateStepResult(DialogueStepStatus.InvalidChoice, "当前对白没有可选择的 Choice。", CurrentSession);

            DialogueChoiceNode choice = FindChoice(CurrentSession.CurrentChoices, choiceNodeId);
            if (choice == null)
                return CreateStepResult(DialogueStepStatus.InvalidChoice, $"不存在 ChoiceNode：{choiceNodeId}。", CurrentSession);

            DialogueSession session = CurrentSession;
            DialogueStepStatus conditionStatus = EvaluateConditions(choice, out string conditionMessage);
            if (conditionStatus != DialogueStepStatus.Advanced)
            {
                if (conditionStatus == DialogueStepStatus.Failed)
                    session.End(conditionMessage);
                else
                {
                    if (!RefreshChoicePresentationsAfterConditionFailure(out string refreshMessage))
                        return CreateStepResult(DialogueStepStatus.Failed, refreshMessage, session);
                }
                return CreateStepResult(conditionStatus, conditionMessage, session);
            }

            DialogueStepStatus actionStatus = ExecuteActions(choice, out string actionMessage);
            if (actionStatus != DialogueStepStatus.Advanced)
            {
                if (actionStatus == DialogueStepStatus.Failed)
                    session.End(actionMessage);
                return CreateStepResult(actionStatus, actionMessage, session);
            }

            // Action 只产生副作用，不返回成功状态；所有动作完成后立即沿 Choice 直接引用跳转。
            return EnterTarget(choice.TargetNode);
        }

        #endregion

        #region 会话事件

        /// <summary>
        /// 把 Session 事实事件转发给系统订阅者，并在结束后释放当前会话引用。
        /// </summary>
        /// <param name="session">新建的会话。</param>
        private void SubscribeSession(DialogueSession session)
        {
            session.SpeechPresented += OnSpeechPresented;
            session.Ended += OnSessionEnded;
        }

        /// <summary>转发 SpeechNode 展示事实，并计算当前 Choice 展示快照。</summary>
        /// <param name="eventArgs">对白展示事件。</param>
        private void OnSpeechPresented(DialogueSpeechPresentedEvent eventArgs)
        {
            currentChoicePresentations.Clear();
            SpeechPresented?.Invoke(eventArgs);
            if (eventArgs.Session.IsEnded || eventArgs.Session.CurrentChoices.Count == 0) return;

            if (!TryBuildChoicePresentations(eventArgs.Session, eventArgs.Speech,
                    out string failureMessage))
            {
                eventArgs.Session.End(failureMessage);
                return;
            }

            ChoicePresented?.Invoke(new DialogueChoicePresentedEvent(
                eventArgs.Session, eventArgs.Speech, currentChoicePresentations));
        }

        /// <summary>
        /// 转发结束事实并解除 Session 事件订阅，避免窗口关闭后保留旧会话引用。
        /// </summary>
        /// <param name="eventArgs">会话结束事件。</param>
        private void OnSessionEnded(DialogueEndedEvent eventArgs)
        {
            DialogueSession session = eventArgs.Session;
            session.SpeechPresented -= OnSpeechPresented;
            session.Ended -= OnSessionEnded;
            PublishLooseGameplayTagRequests(session, LooseGameplayTagChangeOperation.Remove);
            PublishGameUILockRequest(session, GameUILockOperation.Release);
            currentChoicePresentations.Clear();
            if (ReferenceEquals(CurrentSession, session)) CurrentSession = null;
            Ended?.Invoke(eventArgs);
        }

        #endregion

        #region 节点推进

        /// <summary>
        /// 进入一个可运行的目标节点或结束节点。
        /// </summary>
        /// <param name="targetNode">直接 ScriptableObject 目标引用。</param>
        /// <returns>进入结果。</returns>
        private DialogueStepResult EnterTarget(DialogueNode targetNode)
        {
            if (targetNode is DialogueSpeechNode speech)
            {
                DialogueSession session = CurrentSession;
                try
                {
                    session.EnterSpeech(speech);
                }
                catch (Exception exception)
                {
                    // 表现组件异常不能让对话系统跳过清理；End 会发布失败事件并移除会话来源 Tag。
                    Debug.LogException(exception);
                    session.End($"进入 SpeechNode 时执行失败：{exception.Message}");
                }
                // EnterSpeech 可能因命令异常立即结束会话，不能把失败伪装成 Advanced。
                if (session.IsEnded)
                    return CreateStepResult(DialogueStepStatus.Failed, "进入 SpeechNode 时执行失败。", session);
                return CreateStepResult(DialogueStepStatus.Advanced, "已进入下一个 SpeechNode。", session);
            }

            if (targetNode is DialogueEndNode endNode)
            {
                DialogueSession session = CurrentSession;
                session.End("已进入结束节点。", DialogueEndStatus.Completed);
                return CreateStepResult(DialogueStepStatus.Ended, "对话已结束。", session);
            }

            DialogueSession failedSession = CurrentSession;
            failedSession.End("对话目标节点无效。");
            return CreateStepResult(DialogueStepStatus.Failed, "对话目标节点无效。", failedSession);
        }

        /// <summary>在当前选项集合中按 ChoiceNode 的稳定 NodeId 查找选项。</summary>
        /// <param name="choices">当前 SpeechNode 的选项。</param>
        /// <param name="choiceNodeId">待查找的 ChoiceNode NodeId。</param>
        /// <returns>匹配选项；不存在时为空。</returns>
        private static DialogueChoiceNode FindChoice(IReadOnlyList<DialogueChoiceNode> choices, string choiceNodeId)
        {
            if (choices == null) return null;
            for (int index = 0; index < choices.Count; index++)
            {
                DialogueChoiceNode choice = choices[index];
                if (choice != null && string.Equals(choice.NodeId, choiceNodeId, StringComparison.Ordinal)) return choice;
            }

            return null;
        }

        #endregion

        #region Choice 展示

        /// <summary>计算当前 Speech 的全部 Choice 展示状态。</summary>
        /// <param name="session">当前会话。</param>
        /// <param name="speech">当前 SpeechNode。</param>
        /// <param name="failureMessage">命令异常或全部不可用时的失败说明。</param>
        /// <returns>成功生成展示快照时返回 true。</returns>
        private bool TryBuildChoicePresentations(DialogueSession session, DialogueSpeechNode speech,
            out string failureMessage)
        {
            currentChoicePresentations.Clear();
            bool hasAvailableChoice = false;
            for (int index = 0; index < speech.Choices.Count; index++)
            {
                DialogueChoiceNode choice = speech.Choices[index];
                if (choice == null)
                {
                    failureMessage = $"SpeechNode '{speech.NodeId}' 的 Choices[{index}] 为空。";
                    return false;
                }

                DialogueStepStatus status = EvaluateConditionsForPresentation(
                    session, choice, out string reason);
                if (status == DialogueStepStatus.Failed)
                {
                    failureMessage = reason;
                    return false;
                }

                bool available = status == DialogueStepStatus.Advanced;
                hasAvailableChoice |= available;
                currentChoicePresentations.Add(new DialogueChoiceSnapShot(
                    choice.NodeId, choice.Text, available, available ? string.Empty : reason));
            }

            if (!hasAvailableChoice)
            {
                failureMessage = $"SpeechNode '{speech.NodeId}' 的全部 Choice 当前不可用。";
                return false;
            }

            failureMessage = string.Empty;
            return true;
        }

        /// <summary>为展示快照执行一次不产生副作用的 Condition 判断。</summary>
        /// <param name="session">当前会话。</param>
        /// <param name="choice">待计算的 Choice。</param>
        /// <param name="message">不满足原因或异常说明。</param>
        /// <returns>全部条件满足时返回 Advanced，正常不满足时返回 ConditionFailed。</returns>
        private DialogueStepStatus EvaluateConditionsForPresentation(DialogueSession session,
            DialogueChoiceNode choice, out string message)
        {
            DialogueCommandContext context = new DialogueCommandContext(
                session, choice, ((IBelongToArchitecture)this).GetArchitecture());
            for (int index = 0; index < choice.Conditions.Count; index++)
            {
                DialogueCondition condition = choice.Conditions[index];
                if (condition == null)
                {
                    message = $"ChoiceNode '{choice.NodeId}' 的 Condition[{index}] 为空。";
                    return DialogueStepStatus.Failed;
                }

                try
                {
                    DialogueConditionResult result = condition.Evaluate(context);
                    if (result.IsMet) continue;
                    message = string.IsNullOrWhiteSpace(result.FailureReason)
                        ? $"Condition 不满足：{condition.GetType().Name}。"
                        : result.FailureReason;
                    return DialogueStepStatus.ConditionFailed;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    message = $"Condition 执行失败：{exception.Message}";
                    return DialogueStepStatus.Failed;
                }
            }

            message = string.Empty;
            return DialogueStepStatus.Advanced;
        }

        /// <summary>条件在选择瞬间失败后重新生成当前展示状态。</summary>
        /// <param name="failureMessage">刷新失败或会话结束时的诊断信息。</param>
        /// <returns>成功重新发布 Choice 展示时返回 true。</returns>
        private bool RefreshChoicePresentationsAfterConditionFailure(out string failureMessage)
        {
            DialogueSession session = CurrentSession;
            if (session == null || session.IsEnded || session.CurrentSpeech == null)
            {
                failureMessage = "当前对话会话已结束，无法刷新 Choice。";
                return false;
            }
            if (!TryBuildChoicePresentations(session, session.CurrentSpeech, out failureMessage))
            {
                session.End(failureMessage);
                return false;
            }

            ChoicePresented?.Invoke(new DialogueChoicePresentedEvent(
                session, session.CurrentSpeech, currentChoicePresentations));
            failureMessage = string.Empty;
            return true;
        }

        #endregion

        #region 命令执行

        /// <summary>
        /// 按 AND 规则执行当前 Choice 的全部 Condition 命令。
        /// </summary>
        /// <param name="choice">待检查的选项。</param>
        /// <param name="message">失败或异常说明。</param>
        /// <returns>条件通过时返回 Advanced。</returns>
        private DialogueStepStatus EvaluateConditions(DialogueChoiceNode choice, out string message)
        {
            DialogueCommandContext context = CreateCommandContext(choice);
            for (int index = 0; index < choice.Conditions.Count; index++)
            {
                DialogueCondition definition = choice.Conditions[index];
                if (definition == null)
                {
                    message = $"ChoiceNode '{choice.NodeId}' 的 Condition[{index}] 为空。";
                    return DialogueStepStatus.Failed;
                }

                try
                {
                    DialogueConditionResult result = definition.Evaluate(context);
                    if (!result.IsMet)
                    {
                        message = string.IsNullOrWhiteSpace(result.FailureReason)
                            ? $"Condition 不满足：{definition.GetType().Name}。"
                            : result.FailureReason;
                        return DialogueStepStatus.ConditionFailed;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    message = $"Condition 执行失败：{exception.Message}";
                    return DialogueStepStatus.Failed;
                }
            }

            message = string.Empty;
            return DialogueStepStatus.Advanced;
        }

        /// <summary>
        /// 按资产顺序执行当前 Choice 的全部 Action 命令；动作不参与节点跳转判断。
        /// </summary>
        /// <param name="choice">待执行的选项。</param>
        /// <param name="message">失败或异常说明。</param>
        /// <returns>全部动作已经触发时返回 Advanced。</returns>
        private DialogueStepStatus ExecuteActions(DialogueChoiceNode choice, out string message)
        {
            DialogueCommandContext context = CreateCommandContext(choice);
            for (int index = 0; index < choice.Actions.Count; index++)
            {
                DialogueAction definition = choice.Actions[index];
                if (definition == null)
                {
                    message = $"ChoiceNode '{choice.NodeId}' 的 Action[{index}] 为空。";
                    return DialogueStepStatus.Failed;
                }

                try
                {
                    definition.Execute(context);
                }
                catch (Exception exception)
                {
                    // Action 是外部业务边界；异常结束当前会话，避免副作用失败后继续沿图推进。
                    Debug.LogException(exception);
                    message = $"Action 执行失败：{exception.Message}";
                    return DialogueStepStatus.Failed;
                }
            }

            message = string.Empty;
            return DialogueStepStatus.Advanced;
        }

        #endregion

        #region 请求与结果辅助

        /// <summary>为指定 Choice 创建一次性的命令上下文。</summary>
        /// <param name="choice">当前判断或执行的 Choice。</param>
        /// <returns>绑定当前 Session 和架构的命令上下文。</returns>
        private DialogueCommandContext CreateCommandContext(DialogueChoiceNode choice) =>
            new DialogueCommandContext(CurrentSession, choice,
                ((IBelongToArchitecture)this).GetArchitecture());

        /// <summary>校验启动请求的外部输入和 EntryNode 关键引用。</summary>
        /// <param name="request">待校验请求。</param>
        /// <param name="message">校验失败说明。</param>
        /// <returns>请求满足启动边界时返回 true。</returns>
        private static bool IsValidRequest(DialogueRequest request, out string message)
        {
            if (request == null)
            {
                message = "DialogueRequest 不能为空。";
                return false;
            }

            if (request.Initiator == null || request.Initiator.ParticipantObject == null)
            {
                message = "DialogueRequest 必须配置发起者 Participant。";
                return false;
            }

            if (request.Asset == null || request.Asset.EntryNode == null || request.Asset.EntryNode.FirstSpeechNode == null)
            {
                message = "DialogueAsset 必须配置 EntryNode 和首个 SpeechNode。";
                return false;
            }

            message = string.Empty;
            return true;
        }

        /// <summary>
        /// 为发起者发布通用移动和 Ability 阻断 Tag 请求；目标 ASC 自行桥接事件。
        /// </summary>
        /// <param name="session">当前对话会话。</param>
        /// <param name="operation">增加或移除操作。</param>
        private void PublishLooseGameplayTagRequests(
            DialogueSession session,
            LooseGameplayTagChangeOperation operation)
        {
            GameObject target = session.Request.Initiator?.ParticipantObject;
            if (target == null) return;

            this.SendEvent(new LooseGameplayTagChangeRequestedEventArgs(
                target,
                session.SessionId,
                GameplayTags.Tag_State_Block_Movement,
                operation));
            this.SendEvent(new LooseGameplayTagChangeRequestedEventArgs(
                target,
                session.SessionId,
                GameplayTags.Tag_State_Block_AbilityActivation,
                operation));
        }

        /// <summary>
        /// 通过 GameUILock 事件通知交互与 HUD 消费者进入或退出对话独占状态。
        /// </summary>
        /// <param name="session">当前对话会话。</param>
        /// <param name="operation">申请或释放操作。</param>
        private void PublishGameUILockRequest(
            DialogueSession session,
            GameUILockOperation operation)
        {
            this.SendEvent(new GameUILockChangeRequestedEventArgs(
                $"Dialogue:{session.SessionId}", operation));
        }

        /// <summary>创建与当前系统会话关联的推进结果。</summary>
        /// <param name="status">推进状态。</param>
        /// <param name="message">结果说明。</param>
        /// <param name="session">关联会话。</param>
        /// <returns>新的推进结果。</returns>
        private static DialogueStepResult CreateStepResult(DialogueStepStatus status, string message,
            DialogueSession session) => new DialogueStepResult(status, message, session);

        #endregion
    }
}
