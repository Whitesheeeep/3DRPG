using System;
using System.IO;
using System.Text;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 按第一版小端协议读写并校验自描述存档容器头。
    /// </summary>
    public static class SaveContainerCodec
    {
        #region 编码常量
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(SaveContainerFormat.MagicText);
        #endregion

        #region 容器头读写
        /// <summary>
        /// 从目标流当前位置写入完整容器头，不关闭目标流。
        /// </summary>
        /// <param name="destination">可写且可定位的容器流。</param>
        /// <param name="header">待写入容器头。</param>
        /// <exception cref="ArgumentNullException">目标流为空时抛出。</exception>
        /// <exception cref="ArgumentException">目标流不可写时抛出。</exception>
        public static void WriteHeader(Stream destination, SaveContainerHeader header)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!destination.CanWrite)
            {
                throw new ArgumentException("容器目标 Stream 必须可写。", nameof(destination));
            }

            byte[] slotIdBytes = Encoding.ASCII.GetBytes(header.SlotId.Value);
            byte[] formatIdBytes = StrictUtf8.GetBytes(header.FormatId);
            using (var writer = new BinaryWriter(destination, StrictUtf8, true))
            {
                // BinaryWriter 的整数写入顺序为小端，与容器协议一致。
                writer.Write(MagicBytes);
                writer.Write(header.ContainerVersion);
                writer.Write(slotIdBytes);
                writer.Write((ushort)formatIdBytes.Length);
                writer.Write(header.PayloadLength);
                writer.Write(formatIdBytes);
            }
        }

        /// <summary>
        /// 从当前位置读取容器头，并根据文件槽位和流剩余长度验证 Payload 边界。
        /// </summary>
        /// <param name="source">可读且可定位的容器流。</param>
        /// <param name="expectedSlotId">从规范文件名解析的槽位标识。</param>
        /// <returns>已校验容器头，或精确的容器协议错误。</returns>
        /// <exception cref="ArgumentNullException">源流为空时抛出。</exception>
        /// <exception cref="ArgumentException">源流不可读、不可定位或期望槽位无效时抛出。</exception>
        public static SaveResult<SaveContainerHeader> ReadAndValidateHeader(
            Stream source,
            SaveSlotId expectedSlotId)
        {
            RequireReadableSeekableStream(source);
            if (!expectedSlotId.IsValid)
            {
                throw new ArgumentException("容器校验必须提供有效文件槽位 ID。", nameof(expectedSlotId));
            }

            try
            {
                using (var reader = new BinaryReader(source, StrictUtf8, true))
                {
                    byte[] magic = ReadExact(reader, SaveContainerFormat.MagicByteCount);

                    // 文件格式魔术校验：
                    // 1. MagicBytes 长度固定，且 ASCII 编码的 MagicText 不包含 BOM。
                    // 2. MagicBytes 与 BinaryReader.ReadBytes 读取的字节顺序一致。
                    /*从流里按 SaveContainerFormat.MagicByteCount 读取魔数字节到 magic（ReadExact）。
                    - 用 BytesEqual 把它和预定义的 MagicBytes（由 SaveContainerFormat.MagicText 得来）逐字节比较。
                    - 若不相等，立即返回一个失败的 SaveResult（错误码 InvalidContainerMagic），中止后续解析。*/
                    if (!BytesEqual(magic, MagicBytes))
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidContainerMagic,
                            "存档容器 Magic 不匹配。");
                    }

                    // BinaryReader 的整数读取顺序为小端，与容器协议一致。
                    ushort containerVersion = reader.ReadUInt16();
                    if (containerVersion != SaveContainerFormat.CurrentVersion)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.UnsupportedContainerVersion,
                            $"不支持存档容器版本：{containerVersion}");
                    }

                    string slotText = Encoding.ASCII.GetString(ReadExact(reader, SaveContainerFormat.SlotIdByteCount));
                    if (!SaveSlotId.TryParse(slotText, out SaveSlotId headerSlotId))
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidContainerHeader,
                            "存档容器头包含无效 SlotId。");
                    }

                    if (headerSlotId != expectedSlotId)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.SlotIdMismatch,
                            $"文件槽位 {expectedSlotId} 与容器头槽位 {headerSlotId} 不一致。");
                    }

                    ushort formatIdLength = reader.ReadUInt16();
                    if (formatIdLength < 1 || formatIdLength > SaveContainerFormat.MaxFormatIdByteCount)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidFormatId,
                            $"存档容器 FormatId 长度无效：{formatIdLength}");
                    }

                    long payloadLength = reader.ReadInt64();
                    if (payloadLength < 0)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidContainerHeader,
                            "存档容器 PayloadLength 不能为负数。");
                    }

                    string formatId;
                    try
                    {
                        formatId = StrictUtf8.GetString(ReadExact(reader, formatIdLength));
                        SaveContainerFormat.GetValidatedFormatIdByteCount(formatId);
                    }
                    catch (DecoderFallbackException exception)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidFormatId,
                            "存档容器 FormatId 不是有效 UTF-8。",
                            exception);
                    }
                    catch (ArgumentException exception)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.InvalidFormatId,
                            exception.Message,
                            exception);
                    }

                    long remainingLength = source.Length - source.Position;
                    if (remainingLength < payloadLength)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.PayloadTruncated,
                            $"Payload 声明 {payloadLength} 字节，实际仅剩余 {remainingLength} 字节。");
                    }

                    if (remainingLength > payloadLength)
                    {
                        return SaveResult<SaveContainerHeader>.Failure(
                            SaveErrorCode.TrailingPayloadData,
                            $"Payload 之后存在 {remainingLength - payloadLength} 字节未声明数据。");
                    }

                    return SaveResult<SaveContainerHeader>.Success(
                        new SaveContainerHeader(containerVersion, headerSlotId, formatId, payloadLength));
                }
            }
            catch (EndOfStreamException exception)
            {
                return SaveResult<SaveContainerHeader>.Failure(
                    SaveErrorCode.InvalidContainerHeader,
                    "存档容器头被截断。",
                    exception);
            }
            catch (IOException exception)
            {
                return SaveResult<SaveContainerHeader>.Failure(
                    SaveErrorCode.StorageReadFailed,
                    "读取存档容器头失败。",
                    exception);
            }
        }

        /// <summary>
        /// 回填已写入容器头的 PayloadLength，并恢复调用前流位置。
        /// </summary>
        /// <param name="destination">包含容器头的可写可定位流。</param>
        /// <param name="headerStartPosition">容器头起始位置。</param>
        /// <param name="payloadLength">已写入 Payload 的精确字节长度。</param>
        /// <exception cref="ArgumentNullException">目标流为空时抛出。</exception>
        /// <exception cref="ArgumentException">流不可写或不可定位时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">起始位置或 Payload 长度为负数时抛出。</exception>
        public static void PatchPayloadLength(Stream destination, long headerStartPosition, long payloadLength)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!destination.CanWrite || !destination.CanSeek)
            {
                throw new ArgumentException("回填 PayloadLength 需要可写且可定位 Stream。", nameof(destination));
            }

            if (headerStartPosition < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(headerStartPosition));
            }

            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength));
            }

            long returnPosition = destination.Position;
            destination.Position = headerStartPosition + SaveContainerFormat.PayloadLengthOffset;
            using (var writer = new BinaryWriter(destination, StrictUtf8, true))
            {
                writer.Write(payloadLength);
            }

            destination.Position = returnPosition;
        }
        #endregion

        #region 校验与读取辅助
        /// <summary>
        /// 校验容器读取需要的 Stream 能力。
        /// </summary>
        /// <param name="source">待校验源流。</param>
        private static void RequireReadableSeekableStream(Stream source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!source.CanRead || !source.CanSeek)
            {
                throw new ArgumentException("容器源 Stream 必须可读且可定位。", nameof(source));
            }
        }

        /// <summary>
        /// 读取固定数量字节，并将不足数量统一表示为容器头截断。
        /// </summary>
        /// <param name="reader">二进制读取器。</param>
        /// <param name="count">必须读取的字节数。</param>
        /// <returns>精确长度的字节数组。</returns>
        /// <exception cref="EndOfStreamException">源流数据不足时抛出。</exception>
        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count)
            {
                throw new EndOfStreamException();
            }

            return bytes;
        }

        /// <summary>
        /// 不依赖平台的字节序列精确比较。
        /// </summary>
        /// <param name="left">左侧字节序列。</param>
        /// <param name="right">右侧字节序列。</param>
        /// <returns>长度和每个字节都相同时返回 true。</returns>
        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }

            return true;
        }
        #endregion
    }
}