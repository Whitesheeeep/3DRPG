using System;
using System.Threading;

namespace RPG.Game.UI.Flow
{
    /// <summary>标识一次需要关联 HUD 显隐结果的请求。</summary>
    public readonly struct GameWindowTransitionRequestId : IEquatable<GameWindowTransitionRequestId>
    {
        private static long nextId;

        /// <summary>使用序号创建请求标识。</summary>
        /// <param name="id">单调递增的请求序号。</param>
        private GameWindowTransitionRequestId(long id) => Id = id;

        /// <summary>创建进程内唯一的请求标识。</summary>
        /// <returns>新的请求标识。</returns>
        public static GameWindowTransitionRequestId Create() =>
            new GameWindowTransitionRequestId(Interlocked.Increment(ref nextId));

        /// <summary>获取请求序号。</summary>
        public long Id { get; }

        /// <summary>判断请求标识是否相等。</summary>
        /// <param name="other">另一个请求标识。</param>
        /// <returns>相等时返回 true。</returns>
        public bool Equals(GameWindowTransitionRequestId other) => Id == other.Id;

        /// <summary>判断对象是否为相同请求标识。</summary>
        /// <param name="obj">待比较对象。</param>
        /// <returns>相等时返回 true。</returns>
        public override bool Equals(object obj) => obj is GameWindowTransitionRequestId other && Equals(other);

        /// <summary>获取请求标识哈希值。</summary>
        /// <returns>哈希值。</returns>
        public override int GetHashCode() => Id.GetHashCode();

        /// <summary>获取请求标识文本。</summary>
        /// <returns>请求序号文本。</returns>
        public override string ToString() => Id.ToString();

        /// <summary>判断两个请求标识是否相同。</summary>
        /// <param name="left">左侧请求标识。</param>
        /// <param name="right">右侧请求标识。</param>
        /// <returns>相同时返回 true。</returns>
        public static bool operator ==(GameWindowTransitionRequestId left, GameWindowTransitionRequestId right) => left.Equals(right);

        /// <summary>判断两个请求标识是否不同。</summary>
        /// <param name="left">左侧请求标识。</param>
        /// <param name="right">右侧请求标识。</param>
        /// <returns>不同时返回 true。</returns>
        public static bool operator !=(GameWindowTransitionRequestId left, GameWindowTransitionRequestId right) => !left.Equals(right);
    }

    /// <summary>请求 HUD 显示或隐藏；具体窗口通过 UIManager 生命周期判断完成时机。</summary>
    public readonly struct HudVisibilityChangeRequestedEventArgs
    {
        /// <summary>创建 HUD 显隐请求。</summary>
        /// <param name="requestId">关联的请求标识。</param>
        /// <param name="visible">HUD 目标可见状态。</param>
        public HudVisibilityChangeRequestedEventArgs(GameWindowTransitionRequestId requestId, bool visible)
        {
            RequestId = requestId;
            Visible = visible;
        }

        /// <summary>关联的请求标识。</summary>
        public GameWindowTransitionRequestId RequestId { get; }

        /// <summary>HUD 目标可见状态。</summary>
        public bool Visible { get; }
    }

    /// <summary>报告 HUD 无法执行某次显隐请求。</summary>
    public readonly struct HudVisibilityChangeRejectedEventArgs
    {
        /// <summary>创建 HUD 显隐拒绝通知。</summary>
        /// <param name="requestId">关联的请求标识。</param>
        /// <param name="reason">拒绝原因。</param>
        public HudVisibilityChangeRejectedEventArgs(GameWindowTransitionRequestId requestId, string reason)
        {
            RequestId = requestId;
            Reason = reason ?? string.Empty;
        }

        /// <summary>关联的请求标识。</summary>
        public GameWindowTransitionRequestId RequestId { get; }

        /// <summary>拒绝原因。</summary>
        public string Reason { get; }
    }
}
