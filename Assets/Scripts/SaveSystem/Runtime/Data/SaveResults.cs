using System;

namespace RPG.SaveSystem
{
    #region 错误类型

    /// <summary>
    /// 定义存档边界上可预期失败的稳定分类。
    /// </summary>
    public enum SaveErrorCode
    {
        /// <summary>操作成功，没有错误。</summary>
        None = 0,
        /// <summary>槽位标识无效。</summary>
        InvalidSlotId,
        /// <summary>目标槽位不存在。</summary>
        SlotNotFound,
        /// <summary>总存档格式版本不受支持。</summary>
        UnsupportedFormatVersion,
        /// <summary>序列化格式标识没有对应实现。</summary>
        UnknownSerializerFormat,
        /// <summary>对象序列化失败。</summary>
        SerializationFailed,
        /// <summary>内容反序列化失败。</summary>
        DeserializationFailed,
        /// <summary>存档管理器当前已有另一个操作正在执行。</summary>
        OperationBusy,
        /// <summary>存档包含当前管理器未注册的模块。</summary>
        UnknownModule,
        /// <summary>业务模块采集快照失败。</summary>
        SnapshotCaptureFailed,
        /// <summary>模块快照迁移失败。</summary>
        MigrationFailed,
        /// <summary>存储读取失败。</summary>
        StorageReadFailed,
        /// <summary>存储写入失败。</summary>
        StorageWriteFailed,
        /// <summary>存储删除失败。</summary>
        StorageDeleteFailed,
        /// <summary>存档内出现重复模块。</summary>
        DuplicateModule,
        /// <summary>存档缺少必需模块。</summary>
        MissingModule,
        /// <summary>模块版本不受支持。</summary>
        UnsupportedModuleVersion,
        /// <summary>模块缺少必要迁移步骤。</summary>
        MissingMigration,
        /// <summary>模块快照无效。</summary>
        InvalidSnapshot,
        /// <summary>模块恢复失败。</summary>
        RestoreFailed,
        /// <summary>单文件容器头无效。</summary>
        InvalidContainerHeader,
        /// <summary>单文件容器 Magic 不匹配。</summary>
        InvalidContainerMagic,
        /// <summary>单文件容器版本不受支持。</summary>
        UnsupportedContainerVersion,
        /// <summary>文件名槽位与容器头槽位不匹配。</summary>
        SlotIdMismatch,
        /// <summary>序列化格式标识无效。</summary>
        InvalidFormatId,
        /// <summary>Payload 内容短于容器头声明长度。</summary>
        PayloadTruncated,
        /// <summary>Payload 之后存在未声明的尾部数据。</summary>
        TrailingPayloadData,
        /// <summary>未分类错误。</summary>
        Unknown
    }

    #endregion

    #region 非泛型结果

    /// <summary>
    /// 表示一个不返回业务值的存档操作结果。
    /// </summary>
    public readonly struct SaveResult
    {
        /// <summary>
        /// 获取操作是否成功。
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 获取稳定错误分类。
        /// </summary>
        public SaveErrorCode ErrorCode { get; }

        /// <summary>
        /// 获取供日志或 UI 使用的诊断消息。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 获取可选的原始异常，仅用于诊断。
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// 创建存档操作结果。
        /// </summary>
        /// <param name="isSuccess">操作是否成功。</param>
        /// <param name="errorCode">稳定错误分类。</param>
        /// <param name="message">诊断消息。</param>
        /// <param name="exception">可选原始异常。</param>
        private SaveResult(bool isSuccess, SaveErrorCode errorCode, string message, Exception exception)
        {
            IsSuccess = isSuccess;
            ErrorCode = errorCode;
            Message = message;
            Exception = exception;
        }

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        /// <returns>成功结果。</returns>
        public static SaveResult Success() => new SaveResult(true, SaveErrorCode.None, string.Empty, null);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        /// <param name="errorCode">非 None 错误分类。</param>
        /// <param name="message">诊断消息。</param>
        /// <param name="exception">可选原始异常。</param>
        /// <returns>失败结果。</returns>
        /// <exception cref="ArgumentException">错误分类为 None 时抛出。</exception>
        public static SaveResult Failure(SaveErrorCode errorCode, string message, Exception exception = null)
        {
            if (errorCode == SaveErrorCode.None)
            {
                throw new ArgumentException("失败结果不能使用 SaveErrorCode.None。", nameof(errorCode));
            }

            return new SaveResult(false, errorCode, message ?? string.Empty, exception);
        }
    }

    #endregion

    #region 泛型结果

    /// <summary>
    /// 表示一个携带成功值的存档操作结果。
    /// </summary>
    /// <typeparam name="T">成功时返回的值类型。</typeparam>
    public readonly struct SaveResult<T>
    {
        /// <summary>
        /// 获取操作是否成功。
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 获取成功值；失败时为默认值。
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// 获取稳定错误分类。
        /// </summary>
        public SaveErrorCode ErrorCode { get; }

        /// <summary>
        /// 获取供日志或 UI 使用的诊断消息。
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 获取可选的原始异常，仅用于诊断。
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// 创建带值的存档操作结果。
        /// </summary>
        /// <param name="isSuccess">操作是否成功。</param>
        /// <param name="value">成功值。</param>
        /// <param name="errorCode">稳定错误分类。</param>
        /// <param name="message">诊断消息。</param>
        /// <param name="exception">可选原始异常。</param>
        private SaveResult(bool isSuccess, T value, SaveErrorCode errorCode, string message, Exception exception)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorCode = errorCode;
            Message = message;
            Exception = exception;
        }

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        /// <param name="value">成功值。</param>
        /// <returns>携带指定值的成功结果。</returns>
        public static SaveResult<T> Success(T value) =>
            new SaveResult<T>(true, value, SaveErrorCode.None, string.Empty, null);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        /// <param name="errorCode">非 None 错误分类。</param>
        /// <param name="message">诊断消息。</param>
        /// <param name="exception">可选原始异常。</param>
        /// <returns>失败结果。</returns>
        /// <exception cref="ArgumentException">错误分类为 None 时抛出。</exception>
        public static SaveResult<T> Failure(SaveErrorCode errorCode, string message, Exception exception = null)
        {
            if (errorCode == SaveErrorCode.None)
            {
                throw new ArgumentException("失败结果不能使用 SaveErrorCode.None。", nameof(errorCode));
            }

            return new SaveResult<T>(false, default, errorCode, message ?? string.Empty, exception);
        }
    }

    #endregion
}
