using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 将底层文件流限制在已校验 Payload 长度内，并在释放时关闭底层流。
    /// </summary>
    internal sealed class ReadOnlyPayloadStream : Stream
    {
        #region 状态与能力

        private readonly Stream innerStream;
        private readonly long payloadLength;
        private long position;
        private bool disposed;

        /// <summary>
        /// 创建从底层流当前位置开始的只读 Payload 视图。
        /// </summary>
        /// <param name="innerStream">已定位到 Payload 起点的可读流。</param>
        /// <param name="payloadLength">允许读取的精确字节数。</param>
        /// <exception cref="ArgumentNullException">底层流为空时抛出。</exception>
        /// <exception cref="ArgumentException">底层流不可读时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">Payload 长度为负数时抛出。</exception>
        public ReadOnlyPayloadStream(Stream innerStream, long payloadLength)
        {
            this.innerStream = innerStream ?? throw new ArgumentNullException(nameof(innerStream));
            if (!innerStream.CanRead)
            {
                throw new ArgumentException("底层 Payload Stream 必须可读。", nameof(innerStream));
            }

            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }

            this.payloadLength = payloadLength;
        }

        /// <summary>获取当前流是否可读。</summary>
        public override bool CanRead => !disposed && innerStream.CanRead;

        /// <summary>获取当前流是否可定位；Payload 视图禁止定位。</summary>
        public override bool CanSeek => false;

        /// <summary>获取当前流是否可写；Payload 视图始终只读。</summary>
        public override bool CanWrite => false;

        /// <summary>获取容器头声明的 Payload 长度。</summary>
        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return payloadLength;
            }
        }

        /// <summary>获取 Payload 内的相对读取位置；不允许修改。</summary>
        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return position;
            }
            set => throw new NotSupportedException("Payload Stream 不支持定位。");
        }

        #endregion

        #region 读取操作

        /// <summary>
        /// 只读视图没有自身写入缓冲，因此 Flush 仅校验生命周期。
        /// </summary>
        public override void Flush()
        {
            ThrowIfDisposed();
        }

        /// <summary>
        /// 读取不超过 Payload 剩余边界的字节。
        /// </summary>
        /// <param name="buffer">接收数据的缓冲区。</param>
        /// <param name="offset">写入缓冲区的起始偏移。</param>
        /// <param name="count">期望读取的最大字节数。</param>
        /// <returns>实际读取字节数。</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            int boundedCount = GetBoundedCount(count);
            if (boundedCount == 0)
            {
                return 0;
            }

            int readCount = innerStream.Read(buffer, offset, boundedCount);
            position += readCount;
            return readCount;
        }

        /// <summary>
        /// 异步读取不超过 Payload 剩余边界的字节。
        /// </summary>
        /// <param name="buffer">接收数据的缓冲区。</param>
        /// <param name="offset">写入缓冲区的起始偏移。</param>
        /// <param name="count">期望读取的最大字节数。</param>
        /// <param name="cancellationToken">取消底层读取的令牌。</param>
        /// <returns>实际读取字节数。</returns>
        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            int boundedCount = GetBoundedCount(count);
            if (boundedCount == 0)
            {
                return 0;
            }

            int readCount = await innerStream.ReadAsync(buffer, offset, boundedCount, cancellationToken);
            position += readCount;
            return readCount;
        }

        /// <summary>
        /// 读取单个 Payload 字节，达到边界时返回 -1。
        /// </summary>
        /// <returns>读取的字节，或 Payload 结束时的 -1。</returns>
        public override int ReadByte()
        {
            ThrowIfDisposed();
            if (position >= payloadLength)
            {
                return -1;
            }

            int value = innerStream.ReadByte();
            if (value >= 0)
            {
                position++;
            }

            return value;
        }

        #endregion

        #region 不支持操作

        /// <summary>
        /// Payload 视图不允许定位。
        /// </summary>
        /// <param name="offset">定位偏移。</param>
        /// <param name="origin">定位原点。</param>
        /// <returns>此方法不会返回。</returns>
        /// <exception cref="NotSupportedException">始终抛出。</exception>
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("Payload Stream 不支持定位。");

        /// <summary>
        /// Payload 视图不允许修改长度。
        /// </summary>
        /// <param name="value">目标长度。</param>
        /// <exception cref="NotSupportedException">始终抛出。</exception>
        public override void SetLength(long value) =>
            throw new NotSupportedException("Payload Stream 不支持修改长度。");

        /// <summary>
        /// Payload 视图不允许写入。
        /// </summary>
        /// <param name="buffer">待写入缓冲区。</param>
        /// <param name="offset">缓冲区起始偏移。</param>
        /// <param name="count">待写入字节数。</param>
        /// <exception cref="NotSupportedException">始终抛出。</exception>
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Payload Stream 不支持写入。");

        #endregion

        #region 生命周期与边界辅助

        /// <summary>
        /// 释放 Payload 视图时同时释放其拥有的底层文件流。
        /// </summary>
        /// <param name="disposing">是否由 Dispose 主动释放托管资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
                if (disposing)
                {
                    innerStream.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// 将请求数量限制到 Payload 剩余长度。
        /// </summary>
        /// <param name="requestedCount">调用方请求的最大字节数。</param>
        /// <returns>不越界的读取数量。</returns>
        private int GetBoundedCount(int requestedCount)
        {
            long remaining = payloadLength - position;
            return (int)Math.Min(requestedCount, remaining);
        }

        /// <summary>
        /// 阻止在 Handle 释放后继续访问底层文件流。
        /// </summary>
        /// <exception cref="ObjectDisposedException">当前 Payload 视图已释放时抛出。</exception>
        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ReadOnlyPayloadStream));
            }
        }

        #endregion
    }
}
