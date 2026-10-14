using System;
using Newtonsoft.Json;

namespace RPG.SaveSystem
{
    /// <summary>标记由业务定义、与框架摘要组合的纯数据对象。</summary>
    public interface ISaveBusinessSummary { }

    /// <summary>为完整读档与只读摘要提供同一组业务摘要转换规则。</summary>
    public interface ISaveBusinessSummaryCodec
    {
        /// <summary>获取本 Manager 接受的业务摘要具体类型。</summary>
        Type SummaryType { get; }

        /// <summary>使用本次 JSON 配置写入业务摘要，不关闭调用方拥有的写入器。</summary>
        /// <param name="writer">当前 JSON 写入器。</param>
        /// <param name="summary">业务摘要。</param>
        /// <param name="serializer">本次完整或摘要操作的 JSON 配置。</param>
        void WriteBusinessSummary(JsonWriter writer, ISaveBusinessSummary summary, JsonSerializer serializer);

        /// <summary>使用相同 JSON 配置恢复具体业务摘要，不修改业务运行时。</summary>
        /// <param name="reader">定位在业务摘要对象的读取器。</param>
        /// <param name="serializer">本次 JSON 配置。</param>
        /// <returns>业务摘要对象。</returns>
        ISaveBusinessSummary ReadBusinessSummary(JsonReader reader, JsonSerializer serializer);
    }
}
