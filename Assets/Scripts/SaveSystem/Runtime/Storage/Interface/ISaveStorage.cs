using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace RPG.SaveSystem
{
    #region 存储接口

    /// <summary>
    /// 定义槽位容器的存储边界；实现负责容器头与原子提交，不理解业务快照。
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>
        /// 异步列出存储后端中可识别的全部槽位，并保留规范文件名对应的损坏条目。
        /// </summary>
        /// <param name="cancellationToken">取消异步枚举的令牌。</param>
        /// <returns>按槽位 ID 升序排列的条目列表，或存储后端无法枚举时的结构化失败。</returns>
        UniTask<SaveResult<IReadOnlyList<SaveStorageEntry>>> ListEntriesAsync(
            CancellationToken cancellationToken);

        /// <summary>
        /// 异步打开指定槽位，并在返回前完成容器头、文件名和 Payload 边界校验。
        /// </summary>
        /// <param name="slotId">待打开的槽位标识。</param>
        /// <param name="cancellationToken">取消异步读取的令牌。</param>
        /// <returns>拥有只读 Payload Stream 的读取句柄或结构化失败。</returns>
        UniTask<SaveResult<ISaveReadHandle>> OpenReadAsync(
            SaveSlotId slotId,
            CancellationToken cancellationToken);

        /// <summary>
        /// 从 Payload Stream 当前 Position 读取到末尾，并原子新建或覆盖指定槽位容器。
        /// </summary>
        /// <param name="slotId">目标槽位标识。</param>
        /// <param name="formatId">Payload 使用的序列化格式标识。</param>
        /// <param name="payload">调用方拥有且由存储器读取但不释放的 Payload Stream。</param>
        /// <param name="cancellationToken">取消异步写入的令牌。</param>
        /// <returns>原子提交结果。</returns>
        UniTask<SaveResult> WriteAsync(
            SaveSlotId slotId,
            string formatId,
            Stream payload,
            CancellationToken cancellationToken);

        /// <summary>
        /// 异步删除指定槽位容器。
        /// </summary>
        /// <param name="slotId">待删除的槽位标识。</param>
        /// <param name="cancellationToken">取消异步删除的令牌。</param>
        /// <returns>删除结果。</returns>
        UniTask<SaveResult> DeleteAsync(
            SaveSlotId slotId,
            CancellationToken cancellationToken);
    }

    #endregion
}
