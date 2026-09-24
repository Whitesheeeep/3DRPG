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
        /// <summary>开始指定输入手势，并快照本次手势的全部时间配置。</summary>
        /// <param name="inputType">输入类型。</param>
        /// <param name="pressBufferDuration">Press 缓冲秒数。</param>
        /// <param name="releaseBufferDuration">Release 缓冲秒数。</param>
        /// <param name="clickMaxHeldDuration">Click 与长按的分界秒数。</param>
        /// <param name="clickBufferDuration">Click 缓冲秒数。</param>
        void NotifyPerformed(PlayerInputType inputType, float pressBufferDuration,
            float releaseBufferDuration, float clickMaxHeldDuration, float clickBufferDuration);
        /// <summary>为指定输入的现有手势创建 Release 阶段，并按快照阈值判断 Click。</summary>
        /// <param name="inputType">输入类型。</param>
        /// <returns>存在未释放的当前手势并成功处理松开时返回 true。</returns>
        bool NotifyCanceled(PlayerInputType inputType);
        /// <summary>确认一个来源句柄对应的阶段已被业务消费。</summary>
        bool TryConfirmConsumed(InputRequestHandle handle);
        /// <summary>清除全部请求。</summary>
        void Clear();
    }
}
