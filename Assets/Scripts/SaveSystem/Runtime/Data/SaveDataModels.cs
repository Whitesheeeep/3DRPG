using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    #region 槽位摘要

    /// <summary>
    /// 保存选档展示与存档格式识别所需的最小摘要数据。
    /// </summary>
    [Serializable]
    public sealed class SaveSlotSummary
    {
        /// <summary>
        /// 获取或设置槽位标识。
        /// </summary>
        public SaveSlotId SlotId { get; set; }

        /// <summary>
        /// 获取或设置角色展示名称。
        /// </summary>
        public string CharacterName { get; set; }

        /// <summary>
        /// 获取或设置保存时刻的 UTC Unix 毫秒数。
        /// </summary>
        public long SavedAtUtcUnixMilliseconds { get; set; }

        /// <summary>
        /// 获取或设置总存档格式版本。
        /// </summary>
        public int FormatVersion { get; set; }

        /// <summary>
        /// 创建供序列化器使用的空摘要。
        /// </summary>
        public SaveSlotSummary()
        {
        }

        /// <summary>
        /// 创建完整槽位摘要。
        /// </summary>
        /// <param name="slotId">槽位标识。</param>
        /// <param name="characterName">角色展示名称。</param>
        /// <param name="savedAtUtcUnixMilliseconds">UTC Unix 毫秒保存时间。</param>
        /// <param name="formatVersion">正整数总存档格式版本。</param>
        public SaveSlotSummary(
            SaveSlotId slotId,
            string characterName,
            long savedAtUtcUnixMilliseconds,
            int formatVersion)
        {
            SlotId = slotId;
            CharacterName = characterName;
            SavedAtUtcUnixMilliseconds = savedAtUtcUnixMilliseconds;
            FormatVersion = formatVersion;
        }
    }

    #endregion

    #region 模块数据

    /// <summary>
    /// 保存单个业务模块的版本和强类型快照对象。
    /// </summary>
    [Serializable]
    public sealed class SaveModuleData
    {
        /// <summary>
        /// 获取或设置模块标识。
        /// </summary>
        public SaveModuleId ModuleId { get; set; }

        /// <summary>
        /// 获取或设置模块独立版本。
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// 获取或设置模块快照。
        /// </summary>
        public ISaveModuleSnapshot Snapshot { get; set; }

        /// <summary>
        /// 创建供序列化器使用的空模块数据。
        /// </summary>
        public SaveModuleData()
        {
        }

        /// <summary>
        /// 创建模块数据。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="version">正整数模块版本。</param>
        /// <param name="snapshot">与模块和版本匹配的快照。</param>
        public SaveModuleData(SaveModuleId moduleId, int version, ISaveModuleSnapshot snapshot)
        {
            ModuleId = moduleId;
            Version = version;
            Snapshot = snapshot;
        }
    }

    /// <summary>
    /// 表示一次完整角色存档的根数据对象。
    /// </summary>
    [Serializable]
    public sealed class SaveEnvelope
    {
        /// <summary>
        /// 获取或设置槽位摘要。
        /// </summary>
        public SaveSlotSummary Summary { get; set; }

        /// <summary>
        /// 获取或设置按模块标识升序保存的模块数据列表。
        /// </summary>
        public List<SaveModuleData> Modules { get; set; } = new List<SaveModuleData>();

        /// <summary>
        /// 创建供序列化器使用的空存档容器。
        /// </summary>
        public SaveEnvelope()
        {
        }

        /// <summary>
        /// 创建完整存档容器。
        /// </summary>
        /// <param name="summary">槽位摘要。</param>
        /// <param name="modules">按模块标识升序排列的模块数据。</param>
        public SaveEnvelope(SaveSlotSummary summary, List<SaveModuleData> modules)
        {
            Summary = summary;
            Modules = modules;
        }
    }

    #endregion

    #region 存储条目

    /// <summary>
    /// 表示本地存档容器是否可以正常打开。
    /// </summary>
    public enum SaveStorageEntryState
    {
        /// <summary>容器头和 Payload 边界已通过校验。</summary>
        Available = 0,
        /// <summary>文件名可识别为槽位，但容器内容已损坏或不受支持。</summary>
        Corrupted = 1
    }

    /// <summary>
    /// 表示存储后端无需反序列化 Payload 即可读取的槽位状态和元数据。
    /// </summary>
    public readonly struct SaveStorageEntry
    {
        /// <summary>
        /// 获取槽位标识。
        /// </summary>
        public SaveSlotId SlotId { get; }

        /// <summary>
        /// 获取容器是否可用。
        /// </summary>
        public SaveStorageEntryState State { get; }

        /// <summary>
        /// 获取当前条目是否通过容器校验。
        /// </summary>
        public bool IsAvailable => State == SaveStorageEntryState.Available;

        /// <summary>
        /// 获取序列化格式标识。
        /// </summary>
        public string FormatId { get; }

        /// <summary>
        /// 获取单文件容器版本。
        /// </summary>
        public ushort ContainerVersion { get; }

        /// <summary>
        /// 获取容器中 Payload 的字节长度。
        /// </summary>
        public long PayloadLength { get; }

        /// <summary>
        /// 获取损坏条目的错误分类；可用条目为 None。
        /// </summary>
        public SaveErrorCode ErrorCode { get; }

        /// <summary>
        /// 获取损坏条目的诊断消息。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 创建存储条目元数据。
        /// </summary>
        /// <param name="slotId">槽位标识。</param>
        /// <param name="state">容器可用状态。</param>
        /// <param name="formatId">序列化格式标识。</param>
        /// <param name="containerVersion">容器版本。</param>
        /// <param name="payloadLength">Payload 字节长度。</param>
        /// <param name="errorCode">损坏原因；可用条目为 None。</param>
        /// <param name="message">损坏诊断消息。</param>
        private SaveStorageEntry(
            SaveSlotId slotId,
            SaveStorageEntryState state,
            string formatId,
            ushort containerVersion,
            long payloadLength,
            SaveErrorCode errorCode,
            string message)
        {
            SlotId = slotId;
            State = state;
            FormatId = formatId;
            ContainerVersion = containerVersion;
            PayloadLength = payloadLength;
            ErrorCode = errorCode;
            Message = message ?? string.Empty;
        }

        /// <summary>
        /// 使用已校验的容器头创建可用槽位条目。
        /// </summary>
        /// <param name="header">已通过文件名和边界校验的容器头。</param>
        /// <returns>可用的槽位条目。</returns>
        public static SaveStorageEntry Available(SaveContainerHeader header) =>
            new SaveStorageEntry(
                header.SlotId,
                SaveStorageEntryState.Available,
                header.FormatId,
                header.ContainerVersion,
                header.PayloadLength,
                SaveErrorCode.None,
                string.Empty);

        /// <summary>
        /// 使用规范文件名中的槽位和容器错误创建损坏条目。
        /// </summary>
        /// <param name="slotId">从文件名解析的有效槽位标识。</param>
        /// <param name="errorCode">非 None 容器错误分类。</param>
        /// <param name="message">诊断消息。</param>
        /// <returns>损坏的槽位条目。</returns>
        /// <exception cref="ArgumentException">槽位无效或错误分类为 None 时抛出。</exception>
        public static SaveStorageEntry Corrupted(
            SaveSlotId slotId,
            SaveErrorCode errorCode,
            string message)
        {
            if (!slotId.IsValid)
            {
                throw new ArgumentException("损坏条目必须对应规范槽位文件名。", nameof(slotId));
            }

            if (errorCode == SaveErrorCode.None)
            {
                throw new ArgumentException("损坏条目不能使用 SaveErrorCode.None。", nameof(errorCode));
            }

            return new SaveStorageEntry(
                slotId,
                SaveStorageEntryState.Corrupted,
                string.Empty,
                0,
                0,
                errorCode,
                message);
        }
    }

    #endregion
}
