using System;
using System.Collections.Generic;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.Generated;

namespace RPG.DialogueSystemModule
{
    /// <summary>
    /// 作为 BusinessArchitecture 系统入口，编排同步对话会话与 Handler 注册表。
    /// </summary>
    public sealed class DialogueSystem : AbstractSystem
    {
        #region 字段与构造

        private readonly Dictionary<Type, IDialogueConditionHandler> conditionHandlers =
            new Dictionary<Type, IDialogueConditionHandler>();
        private readonly Dictionary<Type, IDialogueActionHandler> actionHandlers =
            new Dictionary<Type, IDialogueActionHandler>();

        /// <summary>
        /// 创建一个空的对话系统；业务层随后显式注册 Handler。
        /// </summary>
        public DialogueSystem()
        {
        }

        #endregion

        #region Architecture 生命周期

        /// <summary>
        /// 在业务架构初始化时保留显式 Handler 注册入口，不自动扫描业务类型。
        /// </summary>
        protected override void OnInit()
        {
        }

        /// <summary>
        /// 在业务架构注销时结束活动会话并清理 Handler 注册，避免跨架构持有运行时对象。
        /// </summary>
        protected override void OnDeinit()
        {
            if (CurrentSession is { IsEnded: false })
                CurrentSession.End("DialogueSystem 所属架构已注销。");

            conditionHandlers.Clear();
            actionHandlers.Clear();
        }

        #endregion

        #region 当前会话

        /// <summary>获取当前唯一活动会话。</summary>
        public DialogueSession CurrentSession { get; private set; }

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

        #region Handler 注册

        /// <summary>
        /// 注册一个按定义类型匹配的 Condition Handler；重复类型直接替换。
        /// </summary>
        /// <param name="handler">待注册 Handler。</param>
        public void RegisterConditionHandler(IDialogueConditionHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ValidateDefinitionType<DialogueCondition>(handler.DefinitionType, "Condition");
            conditionHandlers[handler.DefinitionType] = handler;
        }

        /// <summary>
        /// 注册一个按定义类型匹配的 Action Handler；重复类型直接替换。
        /// </summary>
        /// <param name="handler">待注册 Handler。</param>
        public void RegisterActionHandler(IDialogueActionHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ValidateDefinitionType<DialogueAction>(handler.DefinitionType, "Action");
            actionHandlers[handler.DefinitionType] = handler;
        }

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
            PublishLooseGameplayTagRequests(session, LooseGameplayTagChangeOperation.Add);
            session.EnterSpeech(request.Asset.EntryNode.FirstSpeechNode);
            Started?.Invoke(new DialogueStartedEvent(session));
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
        /// <param name="choiceId">当前 SpeechNode 内的稳定 ChoiceId。</param>
        /// <returns>选择处理结果。</returns>
        public DialogueStepResult SelectChoice(string choiceId)
        {
            if (CurrentSession == null || CurrentSession.IsEnded)
                return CreateStepResult(DialogueStepStatus.NotRunning, "当前没有运行中的对话。", null);
            if (CurrentSession.State != DialogueSessionState.WaitingForChoice)
                return CreateStepResult(DialogueStepStatus.InvalidChoice, "当前对白没有可选择的 Choice。", CurrentSession);

            DialogueChoiceNode choice = FindChoice(CurrentSession.CurrentChoices, choiceId);
            if (choice == null)
                return CreateStepResult(DialogueStepStatus.InvalidChoice, $"不存在 Choice：{choiceId}。", CurrentSession);

            DialogueStepStatus conditionStatus = EvaluateConditions(choice, out string conditionMessage);
            if (conditionStatus != DialogueStepStatus.Advanced)
            {
                if (conditionStatus == DialogueStepStatus.MissingHandler)
                {
                    DialogueSession failedSession = CurrentSession;
                    failedSession.End(conditionMessage);
                    return CreateStepResult(conditionStatus, conditionMessage, failedSession);
                }
                return CreateStepResult(conditionStatus, conditionMessage, CurrentSession);
            }

            DialogueSession session = CurrentSession;
            DialogueStepStatus actionStatus = ExecuteActions(choice, out string actionMessage);
            if (actionStatus != DialogueStepStatus.Advanced)
            {
                if (actionStatus == DialogueStepStatus.Failed || actionStatus == DialogueStepStatus.MissingHandler)
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
            session.ChoicePresented += OnChoicePresented;
            session.Ended += OnSessionEnded;
        }

        /// <summary>转发 SpeechNode 展示事实。</summary>
        /// <param name="eventArgs">对白展示事件。</param>
        private void OnSpeechPresented(DialogueSpeechPresentedEvent eventArgs) => SpeechPresented?.Invoke(eventArgs);

        /// <summary>转发 Choice 展示事实。</summary>
        /// <param name="eventArgs">选项展示事件。</param>
        private void OnChoicePresented(DialogueChoicePresentedEvent eventArgs) => ChoicePresented?.Invoke(eventArgs);

        /// <summary>
        /// 转发结束事实并解除 Session 事件订阅，避免窗口关闭后保留旧会话引用。
        /// </summary>
        /// <param name="eventArgs">会话结束事件。</param>
        private void OnSessionEnded(DialogueEndedEvent eventArgs)
        {
            DialogueSession session = eventArgs.Session;
            session.SpeechPresented -= OnSpeechPresented;
            session.ChoicePresented -= OnChoicePresented;
            session.Ended -= OnSessionEnded;
            PublishLooseGameplayTagRequests(session, LooseGameplayTagChangeOperation.Remove);
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
                CurrentSession.EnterSpeech(speech);
                return CreateStepResult(DialogueStepStatus.Advanced, "已进入下一个 SpeechNode。", CurrentSession);
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

        /// <summary>在当前选项集合中按稳定 ChoiceId 查找选项。</summary>
        /// <param name="choices">当前 SpeechNode 的选项。</param>
        /// <param name="choiceId">待查找标识。</param>
        /// <returns>匹配选项；不存在时为空。</returns>
        private static DialogueChoiceNode FindChoice(IReadOnlyList<DialogueChoiceNode> choices, string choiceId)
        {
            if (choices == null) return null;
            for (int index = 0; index < choices.Count; index++)
            {
                DialogueChoiceNode choice = choices[index];
                if (choice != null && string.Equals(choice.ChoiceId, choiceId, StringComparison.Ordinal)) return choice;
            }

            return null;
        }

        #endregion

        #region Handler 执行

        /// <summary>
        /// 按 AND 规则执行当前 Choice 的全部 Condition。
        /// </summary>
        /// <param name="choice">待检查的选项。</param>
        /// <param name="message">失败或缺失 Handler 说明。</param>
        /// <returns>条件通过时返回 Advanced。</returns>
        private DialogueStepStatus EvaluateConditions(DialogueChoiceNode choice, out string message)
        {
            DialogueConditionContext context = new DialogueConditionContext(CurrentSession);
            for (int index = 0; index < choice.Conditions.Count; index++)
            {
                DialogueCondition definition = choice.Conditions[index];
                if (definition == null ||
                    !conditionHandlers.TryGetValue(definition.GetType(), out IDialogueConditionHandler handler))
                {
                    message = $"缺少 Condition Handler：{definition?.GetType().Name ?? "None"}。";
                    return DialogueStepStatus.MissingHandler;
                }

                if (!handler.Evaluate(context, definition, out message))
                    return DialogueStepStatus.ConditionFailed;
            }

            message = string.Empty;
            return DialogueStepStatus.Advanced;
        }

        /// <summary>
        /// 按资产顺序触发当前 Choice 的全部 Action；动作不参与节点跳转判断。
        /// </summary>
        /// <param name="choice">待执行的选项。</param>
        /// <param name="message">失败或缺失 Handler 说明。</param>
        /// <returns>全部动作已经触发时返回 Advanced。</returns>
        private DialogueStepStatus ExecuteActions(DialogueChoiceNode choice, out string message)
        {
            DialogueActionContext context = new DialogueActionContext(CurrentSession);
            for (int index = 0; index < choice.Actions.Count; index++)
            {
                DialogueAction definition = choice.Actions[index];
                if (definition == null ||
                    !actionHandlers.TryGetValue(definition.GetType(), out IDialogueActionHandler handler))
                {
                    message = $"缺少 Action Handler：{definition?.GetType().Name ?? "None"}。";
                    return DialogueStepStatus.MissingHandler;
                }

                try
                {
                    handler.Execute(context, definition);
                }
                catch (Exception exception)
                {
                    // Action 是外部业务边界；异常结束当前会话，避免副作用失败后继续沿图推进。
                    Debug.LogException(exception);
                    message = $"Action Handler 执行失败：{exception.Message}";
                    return DialogueStepStatus.Failed;
                }
            }

            message = string.Empty;
            return DialogueStepStatus.Advanced;
        }

        #endregion

        #region 请求与结果辅助

        /// <summary>校验 Handler 声明的定义类型属于指定基类。</summary>
        /// <typeparam name="TDefinition">允许注册的定义基类。</typeparam>
        /// <param name="definitionType">Handler 声明的定义类型。</param>
        /// <param name="handlerName">Handler 所属类别名称。</param>
        private static void ValidateDefinitionType<TDefinition>(Type definitionType, string handlerName)
            where TDefinition : class
        {
            if (definitionType == null || !typeof(TDefinition).IsAssignableFrom(definitionType) ||
                definitionType.IsAbstract)
                throw new ArgumentException(
                    $"{handlerName} Handler 必须声明可实例化的 {typeof(TDefinition).Name} 类型。",
                    nameof(definitionType));
        }

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
