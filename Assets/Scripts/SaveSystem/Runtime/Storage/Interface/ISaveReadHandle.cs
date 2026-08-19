using System;
using System.IO;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 表示一个已经完成容器头校验的槽位读取会话，并拥有底层 Payload Stream 生命周期。
    /// </summary>
    public interface ISaveReadHandle : IDisposable
    {
        /// <summary>
        /// 获取已经与容器头核对的槽位标识。
        /// </summary>
        SaveSlotId SlotId { get; }

        /// <summary>
        /// 获取容器头声明的序列化格式标识。
        /// </summary>
        string FormatId { get; }

        /// <summary>
        /// 获取受容器头 PayloadLength 限制的只读内容流。
        /// </summary>
        Stream Content { get; }
    }
}