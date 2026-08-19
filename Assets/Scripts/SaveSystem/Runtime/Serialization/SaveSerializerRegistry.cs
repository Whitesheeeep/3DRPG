using System;
using System.Collections.Generic;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 使用 Ordinal 格式标识保存一组不可变存档序列化器。
    /// </summary>
    public sealed class SaveSerializerRegistry : ISaveSerializerResolver
    {
        private readonly Dictionary<string, ISaveSerializer> serializers;

        /// <summary>
        /// 创建序列化器注册表并阻止重复 FormatId。
        /// </summary>
        /// <param name="serializers">应用启动时已组装的序列化器集合。</param>
        /// <exception cref="ArgumentNullException">集合或其中的序列化器为空时抛出。</exception>
        /// <exception cref="ArgumentException">FormatId 无效或重复时抛出。</exception>
        public SaveSerializerRegistry(IEnumerable<ISaveSerializer> serializers)
        {
            if (serializers == null)
            {
                throw new ArgumentNullException(nameof(serializers));
            }

            this.serializers = new Dictionary<string, ISaveSerializer>(StringComparer.Ordinal);
            foreach (ISaveSerializer serializer in serializers)
            {
                if (serializer == null)
                {
                    throw new ArgumentNullException(nameof(serializers), "序列化器集合不能包含空项。");
                }

                SaveContainerFormat.GetValidatedFormatIdByteCount(serializer.FormatId);
                if (!this.serializers.TryAdd(serializer.FormatId, serializer))
                {
                    throw new ArgumentException($"存在重复序列化 FormatId：{serializer.FormatId}", nameof(serializers));
                }
            }
        }

        /// <summary>
        /// 根据区分大小写的 FormatId 解析序列化器，未知格式不回退。
        /// </summary>
        /// <param name="formatId">容器头中的序列化格式标识。</param>
        /// <returns>已注册序列化器或 UnknownSerializerFormat 失败。</returns>
        public SaveResult<ISaveSerializer> Resolve(string formatId)
        {
            if (!string.IsNullOrEmpty(formatId) && serializers.TryGetValue(formatId, out ISaveSerializer serializer))
            {
                return SaveResult<ISaveSerializer>.Success(serializer);
            }

            return SaveResult<ISaveSerializer>.Failure(
                SaveErrorCode.UnknownSerializerFormat,
                $"未注册存档序列化格式：{formatId ?? string.Empty}");
        }
    }
}
