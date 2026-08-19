using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RPG.SaveSystem
{
    /// <summary>
    /// 使用 Newtonsoft.Json 将完整存档容器转换为可读 UTF-8 JSON Payload。
    /// </summary>
    public sealed class NewtonsoftJsonSaveSerializer : ISaveSerializer
    {
        #region 格式常量与属性

        /// <summary>
        /// JSON Payload 在存档容器头中的稳定格式标识。
        /// </summary>
        public const string JsonFormatId = "json";

        private static readonly Encoding Utf8Encoding = new UTF8Encoding(false, true);

        /// <summary>
        /// 获取 JSON Payload 的稳定格式标识。
        /// </summary>
        public string FormatId => JsonFormatId;

        #endregion

        #region 序列化操作

        /// <summary>
        /// 将完整存档容器以缩进 UTF-8 JSON 写入目标流，不关闭目标流。
        /// </summary>
        /// <param name="envelope">待序列化的存档容器。</param>
        /// <param name="destination">从当前 Position 开始写入的可写流。</param>
        /// <param name="cancellationToken">在 JSON 转换边界传播取消的令牌。</param>
        /// <returns>序列化结果。</returns>
        /// <exception cref="ArgumentNullException">存档容器或目标流为空时抛出。</exception>
        /// <exception cref="ArgumentException">目标流不可写时抛出。</exception>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public UniTask<SaveResult> SerializeAsync(
            SaveEnvelope envelope,
            Stream destination,
            CancellationToken cancellationToken)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            RequireWritableStream(destination, nameof(destination));
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                JsonSerializer serializer = CreateSerializer(null);
                // StreamWriter 仅拥有文本缓冲区，leaveOpen 保证调用方继续拥有 Payload Stream。
                using (var textWriter = new StreamWriter(destination, Utf8Encoding, 1024, true))
                using (var jsonWriter = new JsonTextWriter(textWriter) { CloseOutput = false })
                {
                    serializer.Serialize(jsonWriter, envelope, typeof(SaveEnvelope));
                    jsonWriter.Flush();
                }

                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(SaveResult.Success());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsSerializationBoundaryException(exception))
            {
                return UniTask.FromResult(
                    SaveResult.Failure(SaveErrorCode.SerializationFailed, "JSON Payload 序列化失败。", exception));
            }
        }

        /// <summary>
        /// 从 UTF-8 JSON Payload 恢复完整存档容器，不关闭源流。
        /// </summary>
        /// <param name="source">从当前 Position 开始读取的可读流。</param>
        /// <param name="snapshotTypeResolver">按模块标识和版本解析快照类型的服务。</param>
        /// <param name="cancellationToken">在 JSON 转换边界传播取消的令牌。</param>
        /// <returns>恢复后的存档容器或结构化失败。</returns>
        /// <exception cref="ArgumentNullException">源流或快照类型解析器为空时抛出。</exception>
        /// <exception cref="ArgumentException">源流不可读时抛出。</exception>
        /// <exception cref="OperationCanceledException">操作被取消时抛出。</exception>
        public UniTask<SaveResult<SaveEnvelope>> DeserializeAsync(
            Stream source,
            ISaveSnapshotTypeResolver snapshotTypeResolver,
            CancellationToken cancellationToken)
        {
            RequireReadableStream(source, nameof(source));
            if (snapshotTypeResolver == null)
            {
                throw new ArgumentNullException(nameof(snapshotTypeResolver));
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                JsonSerializer serializer = CreateSerializer(snapshotTypeResolver);
                SaveEnvelope envelope;
                using (var textReader = new StreamReader(source, Utf8Encoding, true, 1024, true))
                using (var jsonReader = new JsonTextReader(textReader) { CloseInput = false })
                {
                    envelope = serializer.Deserialize<SaveEnvelope>(jsonReader);
                    if (envelope == null)
                    {
                        throw new JsonSerializationException("JSON Payload 不包含 SaveEnvelope。");
                    }

                    // 仅允许根对象之后出现空白，防止静默忽略第二个 JSON 值。
                    if (jsonReader.Read())
                    {
                        throw new JsonSerializationException("JSON Payload 在根对象之后包含额外内容。");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return UniTask.FromResult(SaveResult<SaveEnvelope>.Success(envelope));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SaveJsonResolutionException exception)
            {
                return UniTask.FromResult(
                    SaveResult<SaveEnvelope>.Failure(exception.ErrorCode, exception.Message, exception));
            }
            catch (Exception exception) when (IsSerializationBoundaryException(exception))
            {
                return UniTask.FromResult(
                    SaveResult<SaveEnvelope>.Failure(
                        SaveErrorCode.DeserializationFailed,
                        "JSON Payload 反序列化失败。",
                        exception));
            }
        }

        #endregion

        #region 配置与边界校验

        /// <summary>
        /// 为单次读写创建隔离的 Json.NET 配置，避免受全局 JsonMgr 设置影响。
        /// </summary>
        /// <param name="snapshotTypeResolver">读取时的快照类型解析器；写入时为空。</param>
        /// <returns>使用稳定 JSON 线格式的序列化器。</returns>
        private static JsonSerializer CreateSerializer(ISaveSnapshotTypeResolver snapshotTypeResolver)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Culture = CultureInfo.InvariantCulture,
                Formatting = Formatting.Indented,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None
            };
            settings.Converters.Add(new SaveSlotIdJsonConverter());
            settings.Converters.Add(new SaveModuleIdJsonConverter());
            settings.Converters.Add(new SaveModuleDataJsonConverter(snapshotTypeResolver));
            return JsonSerializer.Create(settings);
        }

        /// <summary>
        /// 校验流可以从当前位置读取。
        /// </summary>
        /// <param name="stream">待校验流。</param>
        /// <param name="parameterName">用于异常的参数名。</param>
        private static void RequireReadableStream(Stream stream, string parameterName)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException("源 Stream 必须可读。", parameterName);
            }
        }

        /// <summary>
        /// 校验流可以从当前位置写入。
        /// </summary>
        /// <param name="stream">待校验流。</param>
        /// <param name="parameterName">用于异常的参数名。</param>
        private static void RequireWritableStream(Stream stream, string parameterName)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!stream.CanWrite)
            {
                throw new ArgumentException("目标 Stream 必须可写。", parameterName);
            }
        }

        /// <summary>
        /// 判断异常是否来自 JSON、UTF-8 或 Stream 序列化边界。
        /// </summary>
        /// <param name="exception">待分类异常。</param>
        /// <returns>应转换为结构化序列化错误时返回 true。</returns>
        private static bool IsSerializationBoundaryException(Exception exception) =>
            exception is JsonException ||
            exception is IOException ||
            exception is DecoderFallbackException ||
            exception is EncoderFallbackException;

        #endregion
    }
}
