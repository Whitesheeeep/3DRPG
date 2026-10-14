namespace RPG.SaveSystem
{
    /// <summary>组合容器、摘要及摘要读取结果，不复制框架或业务摘要字段。</summary>
    public sealed class SaveSlotDetails
    {
        /// <summary>创建一个槽位的只读展示结果。</summary>
        /// <param name="entry">容器枚举条目。</param>
        /// <param name="summaryResult">只读摘要结果。</param>
        public SaveSlotDetails(SaveStorageEntry entry, SaveResult<SaveSlotSummary> summaryResult)
        {
            Entry = entry;
            SummaryResult = summaryResult;
        }
        /// <summary>获取文件容器状态。</summary>
        public SaveStorageEntry Entry { get; }
        /// <summary>获取摘要或读取失败原因。</summary>
        public SaveResult<SaveSlotSummary> SummaryResult { get; }
    }
}
