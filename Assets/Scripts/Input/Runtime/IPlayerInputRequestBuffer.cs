using System.Collections.Generic;

namespace RPG.PlayerInputSystem
{
    /// <summary>定义业务消费者、仲裁者与输入请求缓冲区之间的稳定契约。</summary>
    public interface IPlayerInputRequestBuffer
    {
        /// <summary>获取当前所有请求的只读顺序视图。</summary>
        IReadOnlyList<IReadOnlyPlayerInputRequest> Requests { get; }
        /// <summary>按输入类型查询当前请求，供业务或仲裁者在不遍历列表的情况下读取。</summary>
        /// <param name="inputType">需要查询的输入类型。</param>
        /// <param name="request">找到时返回该输入类型的只读请求。</param>
        /// <returns>当前存在该输入类型请求时返回 true。</returns>
        bool TryGetRequest(PlayerInputType inputType, out IReadOnlyPlayerInputRequest request);
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
