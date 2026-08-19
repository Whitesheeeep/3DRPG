using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace RPG.SaveSystem
{
    #region 管理器接口与选项

    /// <summary>
    /// 定义面向业务调用方的统一存档管理入口。
    /// </summary>
    public interface ISaveManager
    {
        /// <summary>
        /// 获取当前是否有存档操作正在执行。
        /// </summary>
        bool IsBusy { get; }

        /// <summary>
        /// 在存档操作完成后发布轻量通知；通知不替代返回结果。
        /// </summary>
        event Action<SaveOperationCompleted> OperationCompleted;

        /// <summary>
        /// 列出全部可识别槽位，不反序列化业务 Payload。
        /// </summary>
        /// <returns>槽位元数据或存储错误。</returns>
        UniTask<SaveResult<IReadOnlyList<SaveStorageEntry>>> ListSlotsAsync();

        /// <summary>
        /// 采集全部模块快照并保存到指定槽位。
        /// </summary>
        /// <param name="request">保存请求。</param>
        /// <returns>保存结果。</returns>
        UniTask<SaveResult> SaveAsync(SaveRequest request);

        /// <summary>
        /// 读取、迁移、校验并恢复指定槽位。
        /// </summary>
        /// <param name="slotId">待加载槽位。</param>
        /// <returns>加载摘要和迁移报告，或结构化失败。</returns>
        UniTask<SaveResult<SaveLoadResult>> LoadAsync(SaveSlotId slotId);

        /// <summary>
        /// 删除指定槽位的正式存档文件。
        /// </summary>
        /// <param name="slotId">待删除槽位。</param>
        /// <returns>删除结果。</returns>
        UniTask<SaveResult> DeleteAsync(SaveSlotId slotId);

        /// <summary>
        /// 注册一个业务存档模块，使其从下一次操作开始参与存档。
        /// </summary>
        /// <param name="module">待注册的存档模块。</param>
        void RegisterModule(ISaveModule module);

        /// <summary>
        /// 注册一个相邻版本迁移，使其从下一次加载操作开始生效。
        /// </summary>
        /// <param name="migration">待注册的迁移。</param>
        void RegisterMigration(ISaveMigration migration);
    }

    /// <summary>
    /// 保存管理器的固定运行配置。
    /// </summary>
    public sealed class SaveManagerOptions
    {
        /// <summary>
        /// 创建存档管理器配置。
        /// </summary>
        /// <param name="defaultFormatId">新存档使用的序列化格式标识。</param>
        /// <param name="currentSaveFormatVersion">当前完整存档格式版本。</param>
        /// <exception cref="ArgumentException">格式标识无效时抛出。</exception>
        /// <exception cref="ArgumentOutOfRangeException">存档格式版本不是正整数时抛出。</exception>
        public SaveManagerOptions(string defaultFormatId, int currentSaveFormatVersion)
        {
            SaveContainerFormat.GetValidatedFormatIdByteCount(defaultFormatId);
            if (currentSaveFormatVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(currentSaveFormatVersion),
                    "存档格式版本必须大于零。");
            }

            DefaultFormatId = defaultFormatId;
            CurrentSaveFormatVersion = currentSaveFormatVersion;
        }

        /// <summary>
        /// 获取新存档使用的序列化格式标识。
        /// </summary>
        public string DefaultFormatId { get; }

        /// <summary>
        /// 获取当前完整存档格式版本。
        /// </summary>
        public int CurrentSaveFormatVersion { get; }
    }

    #endregion

    #region 请求与结果

    /// <summary>
    /// 描述一次新建或覆盖存档请求。
    /// </summary>
    public sealed class SaveRequest
    {
        /// <summary>
        /// 创建保存请求。
        /// </summary>
        /// <param name="slotId">目标槽位。</param>
        /// <param name="characterName">写入槽位摘要的角色展示名称。</param>
        /// <exception cref="ArgumentException">槽位标识无效时抛出。</exception>
        public SaveRequest(SaveSlotId slotId, string characterName)
        {
            if (!slotId.IsValid)
            {
                throw new ArgumentException("保存请求必须使用有效槽位 ID。", nameof(slotId));
            }

            SlotId = slotId;
            CharacterName = characterName ?? string.Empty;
        }

        /// <summary>
        /// 获取目标槽位。
        /// </summary>
        public SaveSlotId SlotId { get; }

        /// <summary>
        /// 获取角色展示名称。
        /// </summary>
        public string CharacterName { get; }
    }

    /// <summary>
    /// 描述一次加载完成后的高层结果，不向业务暴露原始 Envelope。
    /// </summary>
    public sealed class SaveLoadResult
    {
        /// <summary>
        /// 创建加载结果。
        /// </summary>
        /// <param name="summary">恢复后的槽位摘要。</param>
        /// <param name="migratedModules">本次执行过迁移的模块 ID。</param>
        /// <exception cref="ArgumentNullException">摘要或迁移列表为空时抛出。</exception>
        internal SaveLoadResult(SaveSlotSummary summary, IReadOnlyList<SaveModuleId> migratedModules)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            if (migratedModules == null)
            {
                throw new ArgumentNullException(nameof(migratedModules));
            }

            MigratedModules = new List<SaveModuleId>(migratedModules).AsReadOnly();
        }

        /// <summary>
        /// 获取恢复后的槽位摘要。
        /// </summary>
        public SaveSlotSummary Summary { get; }

        /// <summary>
        /// 获取本次执行过迁移的模块 ID 列表。
        /// </summary>
        public IReadOnlyList<SaveModuleId> MigratedModules { get; }
    }

    /// <summary>
    /// 标识存档管理器完成的操作类型。
    /// </summary>
    public enum SaveOperationKind
    {
        /// <summary>列举槽位。</summary>
        List = 0,
        /// <summary>保存槽位。</summary>
        Save,
        /// <summary>加载槽位。</summary>
        Load,
        /// <summary>删除槽位。</summary>
        Delete
    }

    /// <summary>
    /// 存档管理器一次操作完成后的通知数据。
    /// </summary>
    public sealed class SaveOperationCompleted
    {
        /// <summary>
        /// 创建操作完成通知。
        /// </summary>
        /// <param name="kind">操作类型。</param>
        /// <param name="slotId">相关槽位；列举操作使用默认无效槽位。</param>
        /// <param name="result">操作结果。</param>
        internal SaveOperationCompleted(SaveOperationKind kind, SaveSlotId slotId, SaveResult result)
        {
            Kind = kind;
            SlotId = slotId;
            IsSuccess = result.IsSuccess;
            ErrorCode = result.ErrorCode;
            Message = result.Message;
        }

        /// <summary>
        /// 获取完成的操作类型。
        /// </summary>
        public SaveOperationKind Kind { get; }

        /// <summary>
        /// 获取相关槽位。
        /// </summary>
        public SaveSlotId SlotId { get; }

        /// <summary>
        /// 获取操作是否成功。
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 获取稳定错误码。
        /// </summary>
        public SaveErrorCode ErrorCode { get; }

        /// <summary>
        /// 获取诊断消息。
        /// </summary>
        public string Message { get; }
    }

    #endregion
}
