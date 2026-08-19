using System;
using System.Text;

namespace RPG.SaveSystem
{
    #region 容器格式

    /// <summary>
    /// 集中定义第一版自描述单文件存档容器的固定协议常量和校验规则。
    /// </summary>
    public static class SaveContainerFormat
    {
        /// <summary>
        /// 单文件存档使用的固定扩展名；扩展名不参与序列化格式判断。
        /// </summary>
        public const string FileExtension = ".save";

        /// <summary>
        /// 八字节 ASCII Magic，其中最后一个字符为空字符。
        /// </summary>
        public const string MagicText = "RPGSAVE\0";

        /// <summary>
        /// Magic 固定字节数量。
        /// </summary>
        public const int MagicByteCount = 8;

        /// <summary>
        /// 当前单文件容器版本。
        /// </summary>
        public const ushort CurrentVersion = 1;

        /// <summary>
        /// 规范化 SlotId 在容器头中的 ASCII 字节数量。
        /// </summary>
        public const int SlotIdByteCount = 32;

        /// <summary>
        /// FormatId UTF-8 编码允许的最大字节数量。
        /// </summary>
        public const int MaxFormatIdByteCount = 128;

        /// <summary>
        /// 不含可变 FormatId 的固定容器头字节数量。
        /// </summary>
        public const int FixedHeaderByteCount = MagicByteCount + sizeof(ushort) + SlotIdByteCount +
                                                sizeof(ushort) + sizeof(long);

        /// <summary>
        /// PayloadLength 字段相对容器起始位置的固定字节偏移。
        /// </summary>
        public const int PayloadLengthOffset = MagicByteCount + sizeof(ushort) + SlotIdByteCount + sizeof(ushort);

        /// <summary>
        /// 获取指定槽位对应的标准单文件名。
        /// </summary>
        /// <param name="slotId">有效槽位标识。</param>
        /// <returns>由规范 GUID 和 .save 扩展名组成的文件名。</returns>
        /// <exception cref="ArgumentException">槽位标识无效时抛出。</exception>
        public static string GetFileName(SaveSlotId slotId)
        {
            if (!slotId.IsValid)
            {
                throw new ArgumentException("不能为无效槽位 ID 创建存档文件名。", nameof(slotId));
            }

            return slotId.Value + FileExtension;
        }

        /// <summary>
        /// 获取 FormatId 的 UTF-8 字节数量并验证第一版长度约束。
        /// </summary>
        /// <param name="formatId">序列化格式标识。</param>
        /// <returns>UTF-8 编码后的字节数量。</returns>
        /// <exception cref="ArgumentException">FormatId 为空或编码后超过上限时抛出。</exception>
        public static int GetValidatedFormatIdByteCount(string formatId)
        {
            if (string.IsNullOrWhiteSpace(formatId))
            {
                throw new ArgumentException("序列化 FormatId 不能为空。", nameof(formatId));
            }

            int byteCount = Encoding.UTF8.GetByteCount(formatId);
            if (byteCount < 1 || byteCount > MaxFormatIdByteCount)
            {
                throw new ArgumentException(
                    $"序列化 FormatId 的 UTF-8 长度必须在 1 到 {MaxFormatIdByteCount} 字节之间。",
                    nameof(formatId));
            }

            return byteCount;
        }
    }

    #endregion

    #region 容器头

    /// <summary>
    /// 表示已从单文件存档固定头读取或准备写入的容器元数据。
    /// </summary>
    public readonly struct SaveContainerHeader
    {
        /// <summary>
        /// 获取容器协议版本。
        /// </summary>
        public ushort ContainerVersion { get; }

        /// <summary>
        /// 获取容器内容所属槽位。
        /// </summary>
        public SaveSlotId SlotId { get; }

        /// <summary>
        /// 获取 Payload 使用的序列化格式标识。
        /// </summary>
        public string FormatId { get; }

        /// <summary>
        /// 获取 Payload 的精确字节长度。
        /// </summary>
        public long PayloadLength { get; }

        /// <summary>
        /// 创建容器头元数据。
        /// </summary>
        /// <param name="containerVersion">正整数容器版本。</param>
        /// <param name="slotId">有效槽位标识。</param>
        /// <param name="formatId">有效序列化格式标识。</param>
        /// <param name="payloadLength">非负 Payload 字节长度。</param>
        /// <exception cref="ArgumentOutOfRangeException">版本为零或 Payload 长度为负时抛出。</exception>
        /// <exception cref="ArgumentException">槽位或 FormatId 无效时抛出。</exception>
        public SaveContainerHeader(
            ushort containerVersion,
            SaveSlotId slotId,
            string formatId,
            long payloadLength)
        {
            if (containerVersion == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(containerVersion), "容器版本必须大于零。");
            }

            if (!slotId.IsValid)
            {
                throw new ArgumentException("容器头必须包含有效槽位 ID。", nameof(slotId));
            }

            SaveContainerFormat.GetValidatedFormatIdByteCount(formatId);

            if (payloadLength < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "Payload 长度不能小于零。");
            }

            ContainerVersion = containerVersion;
            SlotId = slotId;
            FormatId = formatId;
            PayloadLength = payloadLength;
        }

        /// <summary>
        /// 使用当前容器版本创建容器头。
        /// </summary>
        /// <param name="slotId">有效槽位标识。</param>
        /// <param name="formatId">有效序列化格式标识。</param>
        /// <param name="payloadLength">非负 Payload 字节长度。</param>
        /// <returns>当前版本容器头。</returns>
        public static SaveContainerHeader CreateCurrent(
            SaveSlotId slotId,
            string formatId,
            long payloadLength) =>
            new SaveContainerHeader(SaveContainerFormat.CurrentVersion, slotId, formatId, payloadLength);
    }

    #endregion
}
