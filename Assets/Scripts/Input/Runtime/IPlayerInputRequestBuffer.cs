using System.Collections.Generic;

namespace RPG.PlayerInputSystem
{
    /// <summary>定义输入适配器、仲裁者与请求缓冲区之间的稳定契约。</summary>
    public interface IPlayerInputRequestBuffer
    {
        /// <summary>获取当前所有请求的只读顺序视图。</summary>
        IReadOnlyList<IReadOnlyPlayerInputRequest> Requests { get; }
        /// <summary>开始或刷新指定输入的 Press 阶段。</summary>
        void NotifyPerformed(PlayerInputType inputType, float pressBufferDuration);
        /// <summary>为指定输入的现有手势创建 Release 阶段。</summary>
        bool NotifyCanceled(PlayerInputType inputType, float releaseBufferDuration);
        /// <summary>确认一个来源句柄对应的阶段已被业务消费。</summary>
        bool TryConfirmConsumed(InputRequestHandle handle);
        /// <summary>清除全部请求。</summary>
        void Clear();
    }
}
