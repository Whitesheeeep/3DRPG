using System;
using System.Collections.Generic;
using RPG.Character.Animation;
using RPG.InteractionSystem;
using UnityEngine;
using WS_Modules.GAS.AbilitySystemComponent;
using WS_Modules.GAS.TAG;

namespace RPG.DialogueSystem
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

        /// <summary>会话已经完成、取消或失败。</summary>
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
        InvalidGraph
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

        /// <summary>Condition 或 Action Handler 未注册。</summary>
        MissingHandler,

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

        /// <summary>进入 Canceled EndNode。</summary>
        Canceled,

        /// <summary>参与者、资产或架构生命周期异常导致会话失败。</summary>
        Failed
    }

    #endregion

    #region 请求与参与者

    /// <summary>
    /// 将场景中的固定 SpeakerId 绑定到 3D 参与者和动画播放接口。
    /// </summary>
    public sealed class DialogueParticipantBinding
    {
        /// <summary>
        /// 创建参与者绑定；动画接口为空时尝试从 GameObject 获取。
        /// </summary>
        /// <param name="speakerId">与 SpeechNode 匹配的静态 SpeakerId。</param>
        /// <param name="participant">场景中的 3D 参与者对象。</param>
        /// <param name="animationPlayer">可选的动画播放接口。</param>
        public DialogueParticipantBinding(string speakerId, GameObject participant, IAnimationPlayer animationPlayer = null)
        {
            SpeakerId = speakerId ?? string.Empty;
            Participant = participant;
            AnimationPlayer = animationPlayer ?? participant?.GetComponent(typeof(IAnimationPlayer)) as IAnimationPlayer;
        }

        /// <summary>获取静态 SpeakerId。</summary>
        public string SpeakerId { get; }

        /// <summary>获取场景参与者对象。</summary>
        public GameObject Participant { get; }

        /// <summary>获取参与者的动画播放接口。</summary>
        public IAnimationPlayer AnimationPlayer { get; }
    }

    /// <summary>
    /// 表示一次对话所需的强类型运行时请求。
    /// </summary>
    public sealed class DialogueRequest
    {
        /// <summary>
        /// 创建对话请求并复制参与者集合，避免会话期间外部修改绑定列表。
        /// </summary>
        /// <param name="asset">待运行的对话资产。</param>
        /// <param name="player">发起对话的玩家对象。</param>
        /// <param name="playerAbilitySystem">玩家 ASC，可为空。</param>
        /// <param name="participants">当前场景的 SpeakerId 参与者绑定。</param>
        /// <param name="target">触发本次对话的 IInteractable Provider，可为空。</param>
        /// <param name="dialogueControlTag">可选的已烘焙站桩控制 Tag。</param>
        public DialogueRequest(
            DialogueAsset asset,
            GameObject player,
            GameplayAbilitySystemComponent playerAbilitySystem,
            IEnumerable<DialogueParticipantBinding> participants,
            IInteractable target = null,
            GameplayTag dialogueControlTag = default)
        {
            Asset = asset;
            Player = player;
            PlayerAbilitySystem = playerAbilitySystem;
            Target = target;
            Participants = participants == null
                ? Array.Empty<DialogueParticipantBinding>()
                : new List<DialogueParticipantBinding>(participants);
            DialogueControlTag = dialogueControlTag;
        }

        /// <summary>获取对话图资产。</summary>
        public DialogueAsset Asset { get; }

        /// <summary>获取发起对话的玩家对象。</summary>
        public GameObject Player { get; }

        /// <summary>获取玩家 ASC。</summary>
        public GameplayAbilitySystemComponent PlayerAbilitySystem { get; }

        /// <summary>获取触发本次对话的 IInteractable Provider。</summary>
        public IInteractable Target { get; }

        /// <summary>获取不可变参与者绑定集合。</summary>
        public IReadOnlyList<DialogueParticipantBinding> Participants { get; }

        /// <summary>获取本次会话使用的站桩控制 Tag。</summary>
        public GameplayTag DialogueControlTag { get; }

        /// <summary>
        /// 按 SpeakerId 查找参与者绑定。
        /// </summary>
        /// <param name="speakerId">SpeechNode 使用的 SpeakerId。</param>
        /// <returns>匹配的绑定；找不到时为空。</returns>
        public DialogueParticipantBinding FindParticipant(string speakerId)
        {
            for (int index = 0; index < Participants.Count; index++)
            {
                DialogueParticipantBinding participant = Participants[index];
                if (participant != null && string.Equals(participant.SpeakerId, speakerId, StringComparison.Ordinal))
                    return participant;
            }

            return null;
        }
    }

    #endregion

    #region Handler 契约

    /// <summary>
    /// 为 Condition Handler 提供当前会话和请求上下文。
    /// </summary>
    public sealed class DialogueConditionContext
    {
        /// <summary>创建 Condition 上下文。</summary>
        /// <param name="session">当前会话。</param>
        public DialogueConditionContext(DialogueSession session) => Session = session ?? throw new ArgumentNullException(nameof(session));

        /// <summary>获取当前会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取本次请求。</summary>
        public DialogueRequest Request => Session.Request;
    }

    /// <summary>
    /// 为 Action Handler 提供当前会话和请求上下文。
    /// </summary>
    public sealed class DialogueActionContext
    {
        /// <summary>创建 Action 上下文。</summary>
        /// <param name="session">当前会话。</param>
        public DialogueActionContext(DialogueSession session) => Session = session ?? throw new ArgumentNullException(nameof(session));

        /// <summary>获取当前会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取本次请求。</summary>
        public DialogueRequest Request => Session.Request;
    }

    /// <summary>
    /// 定义一个按 Condition 配置具体类型判断 Choice 是否可用的 Handler。
    /// </summary>
    public interface IDialogueConditionHandler
    {
        /// <summary>获取该 Handler 支持的 Condition 定义类型。</summary>
        Type DefinitionType { get; }

        /// <summary>
        /// 计算条件并返回失败原因，不修改游戏状态。
        /// </summary>
        /// <param name="context">当前会话上下文。</param>
        /// <param name="definition">资产中的条件参数。</param>
        /// <param name="failureReason">条件失败时的展示原因。</param>
        /// <returns>条件满足时返回 true。</returns>
        bool Evaluate(DialogueConditionContext context, DialogueCondition definition,
            out string failureReason);
    }

    /// <summary>
    /// 定义一个按 Action 配置具体类型触发 Choice 副作用的 Handler。
    /// </summary>
    public interface IDialogueActionHandler
    {
        /// <summary>获取该 Handler 支持的 Action 定义类型。</summary>
        Type DefinitionType { get; }

        /// <summary>
        /// 触发动作；实现者负责处理其业务异常，不向 DialogueSystem 抛出异常。
        /// </summary>
        /// <param name="context">当前会话上下文。</param>
        /// <param name="definition">资产中的动作参数。</param>
        void Execute(DialogueActionContext context, DialogueAction definition);
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
        public DialogueChoicePresentedEvent(DialogueSession session, DialogueSpeechNode speech)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Speech = speech ?? throw new ArgumentNullException(nameof(speech));
        }

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取包含选项的对白节点。</summary>
        public DialogueSpeechNode Speech { get; }
    }

    /// <summary>表示一个对话会话已经结束。</summary>
    public sealed class DialogueEndedEvent
    {
        /// <summary>创建结束事实事件。</summary>
        /// <param name="session">已经结束的会话。</param>
        /// <param name="status">结束原因。</param>
        /// <param name="message">结束补充说明。</param>
        public DialogueEndedEvent(DialogueSession session, string message)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Message = message ?? string.Empty;
        }

        /// <summary>获取会话。</summary>
        public DialogueSession Session { get; }

        /// <summary>获取结束说明。</summary>
        public string Message { get; }
    }

    #endregion
}
