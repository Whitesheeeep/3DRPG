using System;
using Newtonsoft.Json;

namespace RPG.SaveSystem
{
    /// <summary>由 SaveManager 创建，以显式类型配置保证业务摘要读写对称。</summary>
    public sealed class SaveBusinessSummaryCodec : ISaveBusinessSummaryCodec
    {
        #region 配置
        /// <summary>创建业务摘要转换器，不启用文件内 CLR 类型解析。</summary>
        /// <param name="summaryType">具有公开无参构造函数的业务 DTO 类型。</param>
        /// <exception cref="ArgumentException">类型不满足摘要 DTO 契约时抛出。</exception>
        public SaveBusinessSummaryCodec(Type summaryType)
        {
            if (summaryType == null || !summaryType.IsClass || summaryType.IsAbstract ||
                !typeof(ISaveBusinessSummary).IsAssignableFrom(summaryType) || summaryType.GetConstructor(Type.EmptyTypes) == null)
                throw new ArgumentException("[SaveManager] 业务摘要必须是实现接口且具有公开无参构造函数的具体类。", nameof(summaryType));
            SummaryType = summaryType;
        }

        /// <summary>获取业务摘要实际类型。</summary>
        public Type SummaryType { get; }
        #endregion

        #region 对称转换
        /// <summary>校验写入类型后使用当前 JSON 配置写入 DTO。</summary>
        /// <param name="writer">调用方写入器。</param>
        /// <param name="summary">业务摘要。</param>
        /// <param name="serializer">当前 JSON 配置。</param>
        public void WriteBusinessSummary(JsonWriter writer, ISaveBusinessSummary summary, JsonSerializer serializer)
        {
            if (summary == null || summary.GetType() != SummaryType)
                throw new JsonSerializationException("[SaveManager] 业务摘要与 Manager 配置的类型不一致。");
            // 转换器只匹配接口声明，显式使用具体 DTO 可避免再次进入自身。
            serializer.Serialize(writer, summary, SummaryType);
        }

        /// <summary>按 Manager 配置的具体 DTO 类型读取，不创建任何业务对象。</summary>
        /// <param name="reader">调用方读取器。</param>
        /// <param name="serializer">当前 JSON 配置。</param>
        /// <returns>非空业务摘要。</returns>
        public ISaveBusinessSummary ReadBusinessSummary(JsonReader reader, JsonSerializer serializer)
        {
            return serializer.Deserialize(reader, SummaryType) as ISaveBusinessSummary
                   ?? throw new JsonSerializationException("[SaveManager] 存档缺少有效业务摘要。");
        }
        #endregion
    }
}
