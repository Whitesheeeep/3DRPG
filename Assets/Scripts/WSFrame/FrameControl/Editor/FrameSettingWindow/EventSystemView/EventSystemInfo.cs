using System.Collections.Generic;
using UnityEditor;

namespace WS_Modules
{
    /// <summary>描述一次源码中的事件注册或发布调用。</summary>
    internal sealed class EventCallInfo
    {
        /// <summary>创建事件调用信息。</summary>
        /// <param name="script">调用所在脚本。</param><param name="line">调用起始行。</param>
        /// <param name="source">调用来源。</param><param name="center">事件中心种类。</param><param name="expression">事件表达式。</param>
        public EventCallInfo(MonoScript script, int line, EventCallSource source, EventSourceParser.EventCenterKind center, string expression)
        { Script = script; Line = line; Source = source; Center = center; Expression = expression; }
        /// <summary>调用所在脚本。</summary><value>脚本资源。</value>
        public MonoScript Script { get; }
        /// <summary>调用起始行。</summary><value>源码行号。</value>
        public int Line { get; }
        /// <summary>调用来源。</summary><value>直接事件中心或业务架构。</value>
        public EventCallSource Source { get; }
        /// <summary>事件中心种类。</summary><value>String、Int 或 Type。</value>
        public EventSourceParser.EventCenterKind Center { get; }
        /// <summary>事件类型或键表达式。</summary><value>源码表达式。</value>
        public string Expression { get; }

        /// <summary>说明该位置是否使用了无法落到具体类型的泛型转发。</summary>
        public bool IsGenericForwarding { get; internal set; }
    }

    /// <summary>事件调用来源，用于在面板中区分直接事件中心和业务架构转发。</summary>
    internal enum EventCallSource
    {
        /// <summary>直接调用 WSFrame EventSystem。</summary>
        EventSystem,
        /// <summary>通过 BusinessArchitecture 调用。</summary>
        BusinessArchitecture,
    }

    /// <summary>按事件身份归并的源码注册和发布位置集合。</summary>
    internal sealed class EventSystemInfo
    {
        /// <summary>面板展示的事件名称。</summary>
        public string DisplayName { get; internal set; } = string.Empty;
        /// <summary>事件名称和类别标签的 Tooltip。</summary>
        public string Tooltip { get; internal set; } = string.Empty;
        /// <summary>是否为泛型转发条目。</summary>
        public bool IsGenericForwarding { get; internal set; }
        /// <summary>事件中心类别。</summary>
        public EventSourceParser.EventCenterKind Center { get; internal set; }
        /// <summary>监听调用位置。</summary>
        public readonly List<EventCallInfo> RegisterCalls = new List<EventCallInfo>();
        /// <summary>发布调用位置。</summary>
        public readonly List<EventCallInfo> TriggerCalls = new List<EventCallInfo>();
        /// <summary>添加一条监听调用。</summary><param name="call">调用位置。</param>
        public void AddRegister(EventCallInfo call) { RegisterCalls.Add(call); IsGenericForwarding |= call.IsGenericForwarding; }
        /// <summary>添加一条发布调用。</summary><param name="call">调用位置。</param>
        public void AddTrigger(EventCallInfo call) { TriggerCalls.Add(call); IsGenericForwarding |= call.IsGenericForwarding; }
    }
}
