using System;
using System.IO;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 拥有已校验 Payload Stream 生命周期的本地存档读取句柄。
    /// </summary>
    internal sealed class SaveReadHandle : ISaveReadHandle
    {
        private bool disposed;

        /// <summary>
        /// 创建已通过容器校验的读取句柄。
        /// </summary>
        /// <param name="header">已校验容器头。</param>
        /// <param name="content">从 Payload 起点开始且受长度限制的流。</param>
        /// <exception cref="ArgumentNullException">Payload Stream 为空时抛出。</exception>
        public SaveReadHandle(SaveContainerHeader header, Stream content)
        {
            SlotId = header.SlotId;
            FormatId = header.FormatId;
            Content = content ?? throw new ArgumentNullException(nameof(content));
        }

        /// <summary>获取已与文件名核对的槽位标识。</summary>
        public SaveSlotId SlotId { get; }

        /// <summary>获取 Payload 序列化格式标识。</summary>
        public string FormatId { get; }

        /// <summary>获取受容器头长度限制的只读 Payload Stream。</summary>
        public Stream Content { get; }

        /// <summary>
        /// 释放读取句柄及其拥有的底层文件流。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Content.Dispose();
        }
    }
}
