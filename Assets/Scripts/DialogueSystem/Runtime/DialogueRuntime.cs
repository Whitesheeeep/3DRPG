using System;
using System.Collections.Generic;
using RPG.InteractionSystem;
using WS_Modules.BusinessArchitecture;

namespace RPG.DialogueSystemModule
{
    #region 运行状态与结果

    /// <summary>
    /// 表示当前对话会话是否正在等待用户推进或选择。
    /// </summary>
    public enum DialogueSessionState
    {
        /// <summary>对话尚未等待 Choice，可以执行 Advance。</summary>
        Running,

        /// <summary>当前 SpeechNode 存在选项，只能执行 SelectChoice。</summary>
        WaitingForChoice,

        /// <summary>会话已经完成或失败。</summary>
        Ended
    }

    /// <summary>
    /// 表示同步启动请求的处理结果。
    /// </summary>
    public enum DialogueStartStatus
    {
        /// <summary>对话已经成功创建并进入首个 SpeechNode。</summary>
        Started,

        /// <summary>已有其他会话正在运行。</summary>
        Busy,

        /// <summary>请求、资产或参与者无效。</summary>
        InvalidRequest,

        /// <summary>对话图校验失败。</summary>
        InvalidGraph,

        /// <summary>首个对白执行命令时发生运行错误。</summary>
        Failed
    }

    /// <summary>
    /// 表示同步推进或选择操作的处理结果。
    /// </summary>
    public enum DialogueStepStatus
    {
        /// <summary>操作成功并进入新的 SpeechNode。</summary>
        Advanced,

        /// <summary>操作成功并结束会话。</summary>
        Ended,

        /// <summary>当前没有运行中的会话。</summary>
        NotRunning,

        /// <summary>当前 SpeechNode 必须先选择 Choice。</summary>
        ChoiceRequired,

        /// <summary>选择标识不存在。</summary>
        InvalidChoice,

        /// <summary>选择条件不满足。</summary>
        ConditionFailed,

        /// <summary>会话或图在推进时失效。</summary>
        Failed
    }

    /// <summary>
    /// 表示一次同步启动操作的不可变结果。
    /// </summary>
    public readonly struct DialogueStartResult
    {
        /// <summary>创建启动结果。</summary>
        /// <param name="status">启动状态。</param>
        /// <param name="message">面向日志或编辑器的补充说明。</param>
        /// <param name="session">成功时创建的会话。</param>
        public DialogueStartResult(DialogueStartStatus status, string message, DialogueSession session)
        {
            Status = status;
            Message = message ?? string.Empty;
            Session = session;
        }

        /// <summary>获取启动状态。</summary>
        public DialogueStartStatus Status { get; }

        /// <summary>获取补充说明。</summary>
        public string Message { get; }

        /// <summary>获取成功创建的会话；失败时为空。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取当前启动是否成功。</summary>
        public bool Succeeded => Status == DialogueStartStatus.Started;
    }

    /// <summary>
    /// 表示一次同步推进或选择操作的不可变结果。
    /// </summary>
    public readonly struct DialogueStepResult
    {
        /// <summary>创建推进结果。</summary>
        /// <param name="status">推进状态。</param>
        /// <param name="message">面向日志或 UI 的补充说明。</param>
        /// <param name="session">关联会话；没有会话时为空。</param>
        public DialogueStepResult(DialogueStepStatus status, string message, DialogueSession session)
        {
            Status = status;
            Message = message ?? string.Empty;
            Session = session;
        }

        /// <summary>获取推进状态。</summary>
        public DialogueStepStatus Status { get; }

        /// <summary>获取补充说明。</summary>
        public string Message { get; }

        /// <summary>获取关联会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取当前操作是否成功。</summary>
        public bool Succeeded => Status == DialogueStepStatus.Advanced || Status == DialogueStepStatus.Ended;
    }

    /// <summary>
    /// 表示会话结束的实际原因；Failed 不对应图中的 EndNode。
    /// </summary>
    public enum DialogueEndStatus
    {
        /// <summary>进入 Completed EndNode。</summary>
        Completed,

        /// <summary>参与者、资产或架构生命周期异常导致会话失败。</summary>
        Failed
    }

    #endregion

    #region 请求与参与者

    /// <summary>
    /// 表示一次对话所需的强类型运行时请求。
    /// </summary>
    public sealed class DialogueRequest
    {
        /// <summary>
        /// 创建对话请求并复制参与者集合，避免会话期间外部修改绑定列表。
        /// </summary>
        /// <param name="asset">待运行的对话资产。</param>
        /// <param name="initiator">发起对话的参与者 Context。</param>
        /// <param name="participants">当前场景的其他参与者 Context。</param>
        /// <param name="target">触发本次对话的 IInteractable Provider，可为空。</param>
        public DialogueRequest(
            DialogueAsset asset,
            IDialogueParticipantContext initiator,
            IEnumerable<IDialogueParticipantContext> participants,
            IInteractable target = null)
        {
            Asset = asset;
            Initiator = initiator;
            Target = target;
            List<IDialogueParticipantContext> contexts = new List<IDialogueParticipantContext>();
            if (initiator != null) contexts.Add(initiator);
            if (participants != null)
                foreach (IDialogueParticipantContext participant in participants)
                    if (participant != null && !contexts.Contains(participant)) contexts.Add(participant);
            Participants = contexts;
        }

        /// <summary>获取对话图资产。</summary>
        public DialogueAsset Asset { get; }

        /// <summary>获取发起对话的参与者 Context。</summary>
        public IDialogueParticipantContext Initiator { get; }

        /// <summary>获取触发本次对话的 IInteractable Provider。</summary>
        public IInteractable Target { get; }

        /// <summary>获取按稳定顺序复制的参与者 Context 集合。</summary>
        public IReadOnlyList<IDialogueParticipantContext> Participants { get; }

        /// <summary>
        /// 按 Speaker 资产引用查找参与者 Context。
        /// </summary>
        /// <param name="speaker">SpeechNode 使用的 Speaker 资产。</param>
        /// <returns>匹配的 Context；找不到时为空。</returns>
        public IDialogueParticipantContext FindParticipant(DialogueSpeaker speaker)
        {
            if (speaker == null) return null;
            for (int index = 0; index < Participants.Count; index++)
            {
                IDialogueParticipantContext participant = Participants[index];
                // UnityEngine.Object 的 == 同时处理资产对象身份和已销毁对象，避免比较托管包装器地址。
                if (participant != null && participant.Speaker == speaker)
                    return participant;
            }

            return null;
        }
    }

    #endregion

    #region Dialogue 命令契约

    /// <summary>表示一次 Condition 命令的结构化判断结果。</summary>
    public readonly struct DialogueConditionResult
    {
        /// <summary>创建条件判断结果。</summary>
        /// <param name="isMet">条件是否满足。</param>
        /// <param name="failureReason">条件不满足时的诊断原因。</param>
        public DialogueConditionResult(bool isMet, string failureReason = null)
        {
            IsMet = isMet;
            FailureReason = failureReason ?? string.Empty;
        }

        /// <summary>获取条件是否满足。</summary>
        public bool IsMet { get; }

        /// <summary>获取条件不满足时的诊断原因。</summary>
        public string FailureReason { get; }

        /// <summary>创建满足结果。</summary>
        /// <returns>表示条件满足的结果。</returns>
        public static DialogueConditionResult Met() => new DialogueConditionResult(true);

        /// <summary>创建不满足结果。</summary>
        /// <param name="failureReason">面向诊断或 UI 的失败原因。</param>
        /// <returns>表示条件不满足的结果。</returns>
        public static DialogueConditionResult NotMet(string failureReason) =>
            new DialogueConditionResult(false, failureReason);
    }

    /// <summary>为 Dialogue Condition 和 Action 提供当前会话、Choice 与 IOC 入口。</summary>
    public sealed class DialogueCommandContext
    {
        /// <summary>创建一次命令执行上下文；仅由 DialogueSystem 在运行时创建。</summary>
        /// <param name="session">当前对话会话。</param>
        /// <param name="choice">当前判断或执行的 Choice。</param>
        /// <param name="architecture">当前 DialogueSystem 所属的 BusinessArchitecture。</param>
        internal DialogueCommandContext(DialogueSession session, DialogueChoiceNode choice,
            IArchitecture architecture)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Choice = choice;
            Architecture = architecture ?? throw new ArgumentNullException(nameof(architecture));
        }

        /// <summary>获取当前对话会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取本次对话请求。</summary>
        public DialogueRequest Request => Session.Request;

        /// <summary>获取当前正在判断或执行的 Choice。</summary>
        public DialogueChoiceNode Choice { get; }

        /// <summary>获取当前系统实际所属的 IOC 架构。</summary>
        public IArchitecture Architecture { get; }
    }

    /// <summary>表示一个自身封装判断行为的对话 Condition 命令。</summary>
    [Serializable]
    public abstract class DialogueCondition
    {
        /// <summary>使用当前上下文判断条件是否满足。</summary>
        /// <param name="context">当前会话命令上下文。</param>
        /// <returns>结构化条件结果。</returns>
        public abstract DialogueConditionResult Evaluate(DialogueCommandContext context);

        /// <summary>校验序列化字段；不得访问运行时 IOC。</summary>
        public virtual void Validate()
        {
        }
    }

    /// <summary>表示一个自身封装副作用行为的对话 Action 命令。</summary>
    [Serializable]
    public abstract class DialogueAction
    {
        /// <summary>在当前上下文中同步执行动作。</summary>
        /// <param name="context">当前会话命令上下文。</param>
        public abstract void Execute(DialogueCommandContext context);

        /// <summary>校验序列化字段；不得访问运行时 IOC。</summary>
        public virtual void Validate()
        {
        }
    }

    /// <summary>表示一个供 UI 展示的当前 Choice 快照。</summary>
    public readonly struct DialogueChoiceSnapShot
    {
        /// <summary>创建 Choice 展示快照。</summary>
        /// <param name="nodeId">ChoiceNode 稳定 NodeId。</param>
        /// <param name="text">展示文本。</param>
        /// <param name="isAvailable">当前是否可选。</param>
        /// <param name="unavailableReason">不可选诊断原因。</param>
        public DialogueChoiceSnapShot(string nodeId, string text, bool isAvailable,
            string unavailableReason = null)
        {
            NodeId = nodeId ?? string.Empty;
            Text = text ?? string.Empty;
            IsAvailable = isAvailable;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        /// <summary>获取 ChoiceNode 稳定 ID。</summary>
        public string NodeId { get; }

        /// <summary>获取 Choice 文本。</summary>
        public string Text { get; }

        /// <summary>获取当前是否可用。</summary>
        public bool IsAvailable { get; }

        /// <summary>获取不可用原因。</summary>
        public string UnavailableReason { get; }
    }

    #endregion

    #region 事实事件

    /// <summary>表示一个对话会话已经开始。</summary>
    public sealed class DialogueStartedEvent
    {
        /// <summary>创建开始事实事件。</summary>
        /// <param name="session">已经开始的会话。</param>
        public DialogueStartedEvent(DialogueSession session) => Session = session ?? throw new ArgumentNullException(nameof(session));

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }
    }

    /// <summary>表示一个 SpeechNode 已经展示给 UI。</summary>
    public sealed class DialogueSpeechPresentedEvent
    {
        /// <summary>创建对白展示事实事件。</summary>
        /// <param name="session">当前会话。</param>
        /// <param name="speech">已经进入的 SpeechNode。</param>
        public DialogueSpeechPresentedEvent(DialogueSession session, DialogueSpeechNode speech)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Speech = speech ?? throw new ArgumentNullException(nameof(speech));
        }

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取当前对白节点。</summary>
        public DialogueSpeechNode Speech { get; }
    }

    /// <summary>表示当前 SpeechNode 的 Choice 已经展示给 UI。</summary>
    public sealed class DialogueChoicePresentedEvent
    {
        /// <summary>创建选项展示事实事件。</summary>
        /// <param name="session">当前会话。</param>
        /// <param name="speech">拥有这些选项的 SpeechNode。</param>
        /// <param name="choices">已经计算完成的选项展示快照。</param>
        public DialogueChoicePresentedEvent(DialogueSession session, DialogueSpeechNode speech,
            IReadOnlyList<DialogueChoiceSnapShot> choices)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Speech = speech ?? throw new ArgumentNullException(nameof(speech));
            // 事件保存独立快照，避免 DialogueSystem 下一次计算时清空内部复用列表影响订阅者。
            Choices = choices == null
                ? throw new ArgumentNullException(nameof(choices))
                : new List<DialogueChoiceSnapShot>(choices);
        }

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取包含选项的对白节点。</summary>
        public DialogueSpeechNode Speech { get; }

        /// <summary>获取按资产顺序排列的选项展示快照。</summary>
        public IReadOnlyList<DialogueChoiceSnapShot> Choices { get; }
    }

    /// <summary>表示一个对话会话已经结束。</summary>
    public sealed class DialogueEndedEvent
    {
        /// <summary>创建结束事实事件。</summary>
        /// <param name="session">已经结束的会话。</param>
        /// <param name="message">结束补充说明。</param>
        public DialogueEndedEvent(DialogueSession session, string message)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Message = message ?? string.Empty;
        }

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取会话结束原因。</summary>
        public DialogueEndStatus Status => Session.EndStatus;

        /// <summary>获取结束说明。</summary>
        public string Message { get; }
    }

    #endregion
}
