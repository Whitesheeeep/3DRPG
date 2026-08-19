using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 在构造函数注入的目录中管理自描述单文件存档容器。
    /// </summary>
    public sealed class LocalFileSaveStorage : ISaveStorage
    {
        #region 存储配置

        private const int CopyBufferSize = 81920;
        private readonly string rootDirectory;

        /// <summary>
        /// 创建不依赖 UnityEngine 路径 API 的本地存储。
        /// </summary>
        /// <param name="rootDirectory">存放 .save 文件的根目录；测试可传入临时目录。</param>
        /// <exception cref="ArgumentException">路径为空或空白时抛出。</exception>
        public LocalFileSaveStorage(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("存档根目录不能为空。", nameof(rootDirectory));
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        #endregion

        #region 公开存储操作

        /// <summary>
        /// 列出所有规范槽位文件，个别损坏容器作为 Corrupted 条目保留。
        /// </summary>
        /// <param name="cancellationToken">在文件枚举边界传播取消的令牌。</param>
        /// <returns>按 SlotId 升序排列的槽位条目或目录枚举失败。</returns>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public UniTask<SaveResult<IReadOnlyList<SaveStorageEntry>>> ListEntriesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new List<SaveStorageEntry>();

            // 根目录不存在时返回空列表，避免在首次运行时创建目录。
            if (!Directory.Exists(rootDirectory))
            {
                return UniTask.FromResult(
                    SaveResult<IReadOnlyList<SaveStorageEntry>>.Success(entries));
            }

            try
            {
                // 只枚举顶层目录，避免递归进入子目录或符号链接。
                foreach (string filePath in Directory.EnumerateFiles(
                             rootDirectory,
                             "*" + SaveContainerFormat.FileExtension,
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryGetCanonicalSlotId(filePath, out SaveSlotId slotId))
                    {
                        continue;
                    }

                    entries.Add(ReadStorageEntry(filePath, slotId));
                }

                entries.Sort((left, right) => left.SlotId.CompareTo(right.SlotId));
                return UniTask.FromResult(
                    SaveResult<IReadOnlyList<SaveStorageEntry>>.Success(entries));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsFileBoundaryException(exception))
            {
                return UniTask.FromResult(
                    SaveResult<IReadOnlyList<SaveStorageEntry>>.Failure(
                        SaveErrorCode.StorageReadFailed,
                        $"枚举存档目录失败：{rootDirectory}",
                        exception));
            }
        }

        /// <summary>
        /// 打开指定槽位并在返回前校验容器头和 Payload 边界。
        /// </summary>
        /// <param name="slotId">待打开槽位。</param>
        /// <param name="cancellationToken">取消打开操作的令牌。</param>
        /// <returns>拥有受限 Payload Stream 的句柄或结构化失败。</returns>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public UniTask<SaveResult<ISaveReadHandle>> OpenReadAsync(
            SaveSlotId slotId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!slotId.IsValid)
            {
                return UniTask.FromResult(
                    SaveResult<ISaveReadHandle>.Failure(SaveErrorCode.InvalidSlotId, "不能读取无效存档槽位。"));
            }

            string filePath = GetSlotPath(slotId);
            if (!File.Exists(filePath))
            {
                return UniTask.FromResult(
                    SaveResult<ISaveReadHandle>.Failure(SaveErrorCode.SlotNotFound, $"存档槽位不存在：{slotId}"));
            }

            FileStream fileStream = null;
            try
            {
                fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    CopyBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                SaveResult<SaveContainerHeader> headerResult =
                    SaveContainerCodec.ReadAndValidateHeader(fileStream, slotId);
                if (!headerResult.IsSuccess)
                {
                    fileStream.Dispose();
                    return UniTask.FromResult(
                        SaveResult<ISaveReadHandle>.Failure(
                            headerResult.ErrorCode,
                            headerResult.Message,
                            headerResult.Exception));
                }

                // 编解码器返回时底层流已停在 Payload 起点，受限流防止向容器外读取。
                var payloadStream = new ReadOnlyPayloadStream(fileStream, headerResult.Value.PayloadLength);
                fileStream = null;
                ISaveReadHandle handle = new SaveReadHandle(headerResult.Value, payloadStream);
                return UniTask.FromResult(SaveResult<ISaveReadHandle>.Success(handle));
            }
            catch (Exception exception) when (IsFileBoundaryException(exception))
            {
                fileStream?.Dispose();
                return UniTask.FromResult(
                    SaveResult<ISaveReadHandle>.Failure(
                        SaveErrorCode.StorageReadFailed,
                        $"打开存档槽位失败：{slotId}",
                        exception));
            }
        }

        /// <summary>
        /// 从 Payload Stream 当前位置读取到末尾，并使用同目录临时文件原子提交容器。
        /// </summary>
        /// <param name="slotId">目标槽位。</param>
        /// <param name="formatId">Payload 的序列化格式标识。</param>
        /// <param name="payload">由调用方拥有且不会被关闭的可读流。</param>
        /// <param name="cancellationToken">取消复制和提交的令牌。</param>
        /// <returns>原子提交结果。</returns>
        /// <exception cref="ArgumentNullException">Payload Stream 为空时抛出。</exception>
        /// <exception cref="ArgumentException">Payload Stream 不可读时抛出。</exception>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public async UniTask<SaveResult> WriteAsync(
            SaveSlotId slotId,
            string formatId,
            Stream payload,
            CancellationToken cancellationToken)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (!payload.CanRead)
            {
                throw new ArgumentException("Payload Stream 必须可读。", nameof(payload));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!slotId.IsValid)
            {
                return SaveResult.Failure(SaveErrorCode.InvalidSlotId, "不能写入无效存档槽位。");
            }

            try
            {
                SaveContainerFormat.GetValidatedFormatIdByteCount(formatId);
            }
            catch (ArgumentException exception)
            {
                return SaveResult.Failure(SaveErrorCode.InvalidFormatId, exception.Message, exception);
            }

            string temporaryPath = null;
            try
            {
                Directory.CreateDirectory(rootDirectory);
                string targetPath = GetSlotPath(slotId);
                temporaryPath = Path.Combine(rootDirectory, $"{slotId.Value}.{Guid.NewGuid():N}.tmp");

                using (var output = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.ReadWrite,
                           FileShare.None,
                           CopyBufferSize,
                           FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    long headerStartPosition = output.Position;
                    SaveContainerCodec.WriteHeader(
                        output,
                        SaveContainerHeader.CreateCurrent(slotId, formatId, 0));

                    // 先流式复制 Payload，再回填长度，因此同时支持不可定位的输入流。
                    long payloadLength = await CopyPayloadAsync(payload, output, cancellationToken);
                    SaveContainerCodec.PatchPayloadLength(output, headerStartPosition, payloadLength);
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Flush(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                CommitTemporaryFile(temporaryPath, targetPath);
                temporaryPath = null;
                return SaveResult.Success();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsFileBoundaryException(exception))
            {
                return SaveResult.Failure(
                    SaveErrorCode.StorageWriteFailed,
                    $"写入存档槽位失败：{slotId}",
                    exception);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    TryDeleteTemporaryFile(temporaryPath);
                }
            }
        }

        /// <summary>
        /// 删除指定槽位的正式容器文件。
        /// </summary>
        /// <param name="slotId">待删除槽位。</param>
        /// <param name="cancellationToken">取消删除操作的令牌。</param>
        /// <returns>删除结果；目标不存在时返回 SlotNotFound。</returns>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public UniTask<SaveResult> DeleteAsync(SaveSlotId slotId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!slotId.IsValid)
            {
                return UniTask.FromResult(
                    SaveResult.Failure(SaveErrorCode.InvalidSlotId, "不能删除无效存档槽位。"));
            }

            string filePath = GetSlotPath(slotId);
            if (!File.Exists(filePath))
            {
                return UniTask.FromResult(
                    SaveResult.Failure(SaveErrorCode.SlotNotFound, $"存档槽位不存在：{slotId}"));
            }

            try
            {
                File.Delete(filePath);
                return UniTask.FromResult(SaveResult.Success());
            }
            catch (Exception exception) when (IsFileBoundaryException(exception))
            {
                return UniTask.FromResult(
                    SaveResult.Failure(
                        SaveErrorCode.StorageDeleteFailed,
                        $"删除存档槽位失败：{slotId}",
                        exception));
            }
        }

        #endregion

        #region 容器读写辅助

        /// <summary>
        /// 读取单个槽位容器头并转换为可用或损坏条目。
        /// </summary>
        /// <param name="filePath">规范槽位文件路径。</param>
        /// <param name="slotId">从文件名解析的槽位标识。</param>
        /// <returns>槽位列表条目。</returns>
        private static SaveStorageEntry ReadStorageEntry(string filePath, SaveSlotId slotId)
        {
            try
            {
                using (var stream = new FileStream(
                           filePath,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read,
                           4096,
                           FileOptions.SequentialScan))
                {
                    SaveResult<SaveContainerHeader> result =
                        SaveContainerCodec.ReadAndValidateHeader(stream, slotId);
                    return result.IsSuccess
                        ? SaveStorageEntry.Available(result.Value)
                        : SaveStorageEntry.Corrupted(slotId, result.ErrorCode, result.Message);
                }
            }
            catch (Exception exception) when (IsFileBoundaryException(exception))
            {
                return SaveStorageEntry.Corrupted(slotId, SaveErrorCode.StorageReadFailed, exception.Message);
            }
        }

        /// <summary>
        /// 将 Payload Stream 当前位置到末尾的内容异步复制到容器临时文件。
        /// </summary>
        /// <param name="source">调用方拥有的 Payload Stream。</param>
        /// <param name="destination">临时容器文件流。</param>
        /// <param name="cancellationToken">取消读写的令牌。</param>
        /// <returns>实际复制的 Payload 字节数。</returns>
        /// <exception cref="OperationCanceledException">复制被取消时抛出。</exception>
        private static async UniTask<long> CopyPayloadAsync(
            Stream source,
            Stream destination,
            CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            long totalBytes = 0;
            try
            {
                while (true)
                {
                    int readCount = await source.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        cancellationToken);
                    if (readCount == 0)
                    {
                        return totalBytes;
                    }

                    await destination.WriteAsync(buffer, 0, readCount, cancellationToken);
                    totalBytes = checked(totalBytes + readCount);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>
        /// 在同目录内提交临时文件，使替换前后的正式文件始终是完整容器。
        /// </summary>
        /// <param name="temporaryPath">已完整写入和刷盘的临时文件。</param>
        /// <param name="targetPath">正式槽位文件。</param>
        private static void CommitTemporaryFile(string temporaryPath, string targetPath)
        {
            if (File.Exists(targetPath))
            {
                File.Replace(temporaryPath, targetPath, null);
                return;
            }

            File.Move(temporaryPath, targetPath);
        }

        #endregion

        #region 路径和失败处理

        /// <summary>
        /// 从文件路径严格解析小写 GUID N 格式槽位文件名。
        /// </summary>
        /// <param name="filePath">待检查文件路径。</param>
        /// <param name="slotId">解析成功时的槽位标识。</param>
        /// <returns>文件名与规范槽位文件名完全一致时返回 true。</returns>
        private static bool TryGetCanonicalSlotId(string filePath, out SaveSlotId slotId)
        {
            string fileName = Path.GetFileName(filePath);
            string value = Path.GetFileNameWithoutExtension(fileName);
            if (SaveSlotId.TryParse(value, out slotId) &&
                string.Equals(fileName, SaveContainerFormat.GetFileName(slotId), StringComparison.Ordinal))
            {
                return true;
            }

            slotId = default;
            return false;
        }

        /// <summary>
        /// 获取槽位在已规范根目录中的唯一正式路径。
        /// </summary>
        /// <param name="slotId">有效槽位标识。</param>
        /// <returns>槽位 .save 文件的绝对路径。</returns>
        private string GetSlotPath(SaveSlotId slotId) =>
            Path.Combine(rootDirectory, SaveContainerFormat.GetFileName(slotId));

        /// <summary>
        /// 在取消或写入失败后尽力删除未提交临时文件，清理失败不覆盖原操作结果。
        /// </summary>
        /// <param name="temporaryPath">待清理的精确临时文件路径。</param>
        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // 正式存档未受影响，临时文件可在下次外部维护时清理。
            }
            catch (UnauthorizedAccessException)
            {
                // 不使用清理异常覆盖更关键的写入或取消结果。
            }
        }

        /// <summary>
        /// 判断异常是否来自文件系统边界并可转换为存档存储错误。
        /// </summary>
        /// <param name="exception">待分类异常。</param>
        /// <returns>是文件边界失败时返回 true。</returns>
        private static bool IsFileBoundaryException(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or NotSupportedException;

        #endregion
    }
}
