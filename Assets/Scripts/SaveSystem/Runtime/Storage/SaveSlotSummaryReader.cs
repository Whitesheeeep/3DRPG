using System.Threading;
using Cysharp.Threading.Tasks;

namespace RPG.SaveSystem
{
    /// <summary>读取容器中的摘要，不解析模块快照或恢复业务状态。</summary>
    internal sealed class SaveSlotSummaryReader
    {
        #region 依赖字段
        private readonly ISaveStorage storage;
        private readonly SaveSerializerRegistry serializers;
        private readonly ISaveBusinessSummaryCodec summaryCodec;
        #endregion

        #region 读取
        /// <summary>接收 SaveManager 持有的同一 Codec。</summary>
        /// <param name="storage">文件容器存储。</param>
        /// <param name="serializers">格式解析表。</param>
        /// <param name="summaryCodec">Manager 创建的摘要转换器。</param>
        public SaveSlotSummaryReader(ISaveStorage storage, SaveSerializerRegistry serializers, ISaveBusinessSummaryCodec summaryCodec)
        {
            this.storage = storage;
            this.serializers = serializers;
            this.summaryCodec = summaryCodec;
        }

        /// <summary>在读取句柄生命周期内解析摘要，并校验槽位身份。</summary>
        /// <param name="slotId">槽位。</param>
        /// <param name="token">Manager 生命周期令牌。</param>
        /// <returns>摘要或结构化读取失败。</returns>
        public async UniTask<SaveResult<SaveSlotSummary>> ReadAsync(SaveSlotId slotId, CancellationToken token)
        {
            SaveResult<ISaveReadHandle> opened = await storage.OpenReadAsync(slotId, token);
            if (!opened.IsSuccess) return SaveResult<SaveSlotSummary>.Failure(opened.ErrorCode, opened.Message, opened.Exception);
            using (ISaveReadHandle handle = opened.Value)
            {
                SaveResult<ISaveSerializer> resolved = serializers.Resolve(handle.FormatId);
                if (!resolved.IsSuccess) return SaveResult<SaveSlotSummary>.Failure(resolved.ErrorCode, resolved.Message, resolved.Exception);
                SaveResult<SaveSlotSummary> result = await resolved.Value.ReadSummaryAsync(handle.Content, summaryCodec, token);
                if (result.IsSuccess && result.Value.SlotId != slotId)
                    return SaveResult<SaveSlotSummary>.Failure(SaveErrorCode.SlotIdMismatch, "[SaveManager] 摘要与文件槽位不一致。");
                return result;
            }
        }
        #endregion
    }
}
