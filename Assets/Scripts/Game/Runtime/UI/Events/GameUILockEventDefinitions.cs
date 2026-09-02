using System;

namespace RPG.Game.UI.Events
{
    /// <summary>
    /// 表示一个独占游戏 UI 流程对普通 Gameplay UI 和世界交互的占用变更。
    /// </summary>
    public enum GameUILockOperation
    {
        /// <summary>申请独占游戏 UI。</summary>
        Acquire,

        /// <summary>释放独占游戏 UI。</summary>
        Release
    }

    /// <summary>
    /// 请求按来源增加或释放一次 Game UI 独占占用。
    /// </summary>
    public readonly struct GameUILockChangeRequestedEventArgs
    {
        /// <summary>
        /// 创建 Game UI 独占占用请求。
        /// </summary>
        /// <param name="sourceId">本次占用的稳定来源 ID。</param>
        /// <param name="operation">申请或释放操作。</param>
        /// <exception cref="ArgumentException">来源 ID 为空时抛出。</exception>
        public GameUILockChangeRequestedEventArgs(string sourceId, GameUILockOperation operation)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("GameUILock 来源 ID 不能为空。", nameof(sourceId));

            SourceId = sourceId;
            Operation = operation;
        }

        /// <summary>获取本次占用的稳定来源 ID。</summary>
        public string SourceId { get; }

        /// <summary>获取本次占用的申请或释放操作。</summary>
        public GameUILockOperation Operation { get; }
    }
}
