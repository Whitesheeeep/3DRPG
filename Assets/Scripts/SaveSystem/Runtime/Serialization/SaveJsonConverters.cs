using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RPG.SaveSystem
{
    /// <summary>在框架摘要边界显式调用同一个业务 Codec，避免接口属性绕过写入契约。</summary>
    internal sealed class SaveBusinessSummaryJsonConverter : JsonConverter
    {
        #region 依赖字段
        private readonly ISaveBusinessSummaryCodec codec;
        #endregion

        #region 转换
        /// <summary>接收 Manager 创建的业务摘要转换器。</summary>
        /// <param name="codec">完整读写和只读摘要共用的实例。</param>
        public SaveBusinessSummaryJsonConverter(ISaveBusinessSummaryCodec codec) => this.codec = codec;

        /// <summary>仅接管框架摘要，不接管业务具体 DTO，防止递归。</summary>
        /// <param name="objectType">待转换类型。</param>
        /// <returns>是否为框架摘要。</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(SaveSlotSummary);

        /// <summary>写入框架元数据，再交给 Codec 写入组合业务摘要。</summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">框架摘要。</param>
        /// <param name="serializer">本次配置。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var summary = (SaveSlotSummary)value;
            writer.WriteStartObject();
            writer.WritePropertyName("slotId");
            serializer.Serialize(writer, summary.SlotId);
            writer.WritePropertyName("savedAtUtcUnixMilliseconds");
            writer.WriteValue(summary.SavedAtUtcUnixMilliseconds);
            writer.WritePropertyName("formatVersion");
            writer.WriteValue(summary.FormatVersion);
            writer.WritePropertyName("businessSummary");
            codec.WriteBusinessSummary(writer, summary.BusinessSummary, serializer);
            writer.WriteEndObject();
        }

        /// <summary>读取框架字段，通过 Codec 恢复业务摘要，不解析任意文件类型名。</summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="objectType">目标类型。</param>
        /// <param name="existingValue">未使用的已有值。</param>
        /// <param name="serializer">本次配置。</param>
        /// <returns>组合摘要。</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject data = JObject.Load(reader);
            if (data["slotId"] == null || data["savedAtUtcUnixMilliseconds"]?.Type != JTokenType.Integer ||
                data["formatVersion"]?.Type != JTokenType.Integer || data["businessSummary"]?.Type != JTokenType.Object)
                throw new JsonSerializationException("[SaveManager] 存档缺少有效框架摘要或业务摘要。");
            SaveSlotId slotId = data["slotId"].ToObject<SaveSlotId>(serializer);
            long savedAt = data["savedAtUtcUnixMilliseconds"].Value<long>();
            int version = data["formatVersion"].Value<int>();
            if (savedAt < 0 || savedAt > 253402300799999L || version <= 0)
                throw new JsonSerializationException("[SaveManager] 摘要时间或版本无效。");
            using (JsonReader businessReader = data["businessSummary"].CreateReader())
                return new SaveSlotSummary(slotId, codec.ReadBusinessSummary(businessReader, serializer), savedAt, version);
        }
        #endregion
    }

    #region 标识符转换器

    /// <summary>
    /// 将槽位标识以规范 GUID N 格式字符串写入 JSON。
    /// </summary>
    internal sealed class SaveSlotIdJsonConverter : JsonConverter
    {
        /// <summary>
        /// 判断目标类型是否是槽位标识。
        /// </summary>
        /// <param name="objectType">待检查的 CLR 类型。</param>
        /// <returns>目标类型为 SaveSlotId 时返回 true。</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(SaveSlotId);

        /// <summary>
        /// 将槽位标识写为 JSON 字符串。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">槽位标识值。</param>
        /// <param name="serializer">当前序列化器。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var slotId = (SaveSlotId)value;
            if (!slotId.IsValid)
            {
                throw new JsonSerializationException("不能序列化无效 SaveSlotId。");
            }

            writer.WriteValue(slotId.Value);
        }

        /// <summary>
        /// 从 JSON 字符串恢复并规范化槽位标识。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="objectType">目标 CLR 类型。</param>
        /// <param name="existingValue">现有值，本转换器不使用。</param>
        /// <param name="serializer">当前序列化器。</param>
        /// <returns>有效槽位标识。</returns>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String || !(reader.Value is string value) ||
                !SaveSlotId.TryParse(value, out SaveSlotId slotId))
            {
                throw new JsonSerializationException("JSON 中的 slotId 必须是有效非空 GUID 字符串。");
            }

            return slotId;
        }
    }

    /// <summary>
    /// 将模块标识以稳定字符串写入 JSON。
    /// </summary>
    internal sealed class SaveModuleIdJsonConverter : JsonConverter
    {
        /// <summary>
        /// 判断目标类型是否是模块标识。
        /// </summary>
        /// <param name="objectType">待检查的 CLR 类型。</param>
        /// <returns>目标类型为 SaveModuleId 时返回 true。</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(SaveModuleId);

        /// <summary>
        /// 将模块标识写为 JSON 字符串。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">模块标识值。</param>
        /// <param name="serializer">当前序列化器。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var moduleId = (SaveModuleId)value;
            if (!moduleId.IsValid)
            {
                throw new JsonSerializationException("不能序列化无效 SaveModuleId。");
            }

            writer.WriteValue(moduleId.Value);
        }

        /// <summary>
        /// 从 JSON 字符串恢复模块标识。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="objectType">目标 CLR 类型。</param>
        /// <param name="existingValue">现有值，本转换器不使用。</param>
        /// <param name="serializer">当前序列化器。</param>
        /// <returns>有效模块标识。</returns>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String || !(reader.Value is string value) ||
                !SaveModuleId.TryCreate(value, out SaveModuleId moduleId))
            {
                throw new JsonSerializationException("JSON 中的 moduleId 不符合稳定标识规则。");
            }

            return moduleId;
        }
    }

    #endregion

    #region 模块数据转换器

    /// <summary>
    /// 保留快照类型解析器错误码的 JSON 转换异常。
    /// </summary>
    internal sealed class SaveJsonResolutionException : JsonSerializationException
    {
        /// <summary>
        /// 获取快照类型解析错误码。
        /// </summary>
        public SaveErrorCode ErrorCode { get; }

        /// <summary>
        /// 创建携带结构化错误的 JSON 转换异常。
        /// </summary>
        /// <param name="errorCode">快照类型解析错误。</param>
        /// <param name="message">诊断消息。</param>
        public SaveJsonResolutionException(SaveErrorCode errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// 根据 ModuleId 和 Version 将接口快照恢复为已注册的具体类型。
    /// </summary>
    internal sealed class SaveModuleDataJsonConverter : JsonConverter
    {
        private readonly ISaveSnapshotTypeResolver snapshotTypeResolver;

        /// <summary>
        /// 创建模块数据 JSON 转换器。
        /// </summary>
        /// <param name="snapshotTypeResolver">反序列化时使用的快照类型解析器；仅写入时可为空。</param>
        public SaveModuleDataJsonConverter(ISaveSnapshotTypeResolver snapshotTypeResolver)
        {
            this.snapshotTypeResolver = snapshotTypeResolver;
        }

        /// <summary>
        /// 判断目标类型是否是模块数据。
        /// </summary>
        /// <param name="objectType">待检查的 CLR 类型。</param>
        /// <returns>目标类型为 SaveModuleData 时返回 true。</returns>
        public override bool CanConvert(Type objectType) => objectType == typeof(SaveModuleData);

        /// <summary>
        /// 按固定 moduleId、version、snapshot 顺序写入单个模块。
        /// </summary>
        /// <param name="writer">JSON 写入器。</param>
        /// <param name="value">模块数据。</param>
        /// <param name="serializer">当前序列化器。</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var moduleData = value as SaveModuleData;
            if (moduleData == null || moduleData.Snapshot == null || moduleData.Version <= 0)
            {
                throw new JsonSerializationException("模块数据必须包含正版本和非空快照。");
            }

            // 显式写入外层字段，确保类型解析信息不依赖 CLR 类型名。
            writer.WriteStartObject();
            writer.WritePropertyName("moduleId");
            serializer.Serialize(writer, moduleData.ModuleId);
            writer.WritePropertyName("version");
            writer.WriteValue(moduleData.Version);
            writer.WritePropertyName("snapshot");
            serializer.Serialize(writer, moduleData.Snapshot, moduleData.Snapshot.GetType());
            writer.WriteEndObject();
        }

        /// <summary>
        /// 读取单个模块，先解析标识和版本，再恢复具体快照。
        /// </summary>
        /// <param name="reader">JSON 读取器。</param>
        /// <param name="objectType">目标 CLR 类型。</param>
        /// <param name="existingValue">现有值，本转换器不使用。</param>
        /// <param name="serializer">当前序列化器。</param>
        /// <returns>恢复后的模块数据。</returns>
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (snapshotTypeResolver == null)
            {
                throw new JsonSerializationException("反序列化模块快照时必须提供类型解析器。");
            }

            JObject moduleObject = JObject.Load(reader);
            SaveModuleId moduleId = ReadModuleId(moduleObject);
            int version = ReadVersion(moduleObject);
            JToken snapshotToken = moduleObject["snapshot"];
            if (snapshotToken == null || snapshotToken.Type == JTokenType.Null)
            {
                throw new JsonSerializationException($"模块 {moduleId} 缺少非空 snapshot。");
            }

            SaveResult<Type> typeResult = snapshotTypeResolver.Resolve(moduleId, version);
            if (!typeResult.IsSuccess)
            {
                throw new SaveJsonResolutionException(typeResult.ErrorCode, typeResult.Message);
            }

            object snapshot = snapshotToken.ToObject(typeResult.Value, serializer);
            if (!(snapshot is ISaveModuleSnapshot typedSnapshot))
            {
                throw new JsonSerializationException($"模块 {moduleId} 的快照未恢复为 ISaveModuleSnapshot。");
            }

            return new SaveModuleData(moduleId, version, typedSnapshot);
        }

        /// <summary>
        /// 从模块 JSON 对象读取并校验模块标识。
        /// </summary>
        /// <param name="moduleObject">模块 JSON 对象。</param>
        /// <returns>有效模块标识。</returns>
        private static SaveModuleId ReadModuleId(JObject moduleObject)
        {
            JToken token = moduleObject["moduleId"];
            string value = token?.Type == JTokenType.String ? token.Value<string>() : null;
            if (!SaveModuleId.TryCreate(value, out SaveModuleId moduleId))
            {
                throw new JsonSerializationException("模块数据缺少有效 moduleId。");
            }

            return moduleId;
        }

        /// <summary>
        /// 从模块 JSON 对象读取正整数版本。
        /// </summary>
        /// <param name="moduleObject">模块 JSON 对象。</param>
        /// <returns>正整数模块版本。</returns>
        private static int ReadVersion(JObject moduleObject)
        {
            JToken token = moduleObject["version"];
            if (token == null || token.Type != JTokenType.Integer)
            {
                throw new JsonSerializationException("模块数据缺少整数 version。");
            }

            int version = token.Value<int>();
            if (version <= 0)
            {
                throw new JsonSerializationException("模块 version 必须大于零。");
            }

            return version;
        }
    }

    #endregion
}
