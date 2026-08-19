using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace RPG.SaveSystem
{
    #region 序列化接口

    /// <summary>
    /// 定义完整存档容器与 Stream 之间的可替换序列化边界。
    /// </summary>
    public interface ISaveSerializer
    {
        /// <summary>
        /// 获取长期稳定且区分大小写的序列化格式标识。
        /// </summary>
        string FormatId { get; }

        /// <summary>
        /// 将完整存档容器异步写入目标 Stream 当前 Position，且不释放目标 Stream。
        /// </summary>
        /// <param name="envelope">待序列化的完整存档容器。</param>
        /// <param name="destination">调用方拥有的可写目标 Stream。</param>
        /// <param name="cancellationToken">取消异步序列化的令牌。</param>
        /// <returns>序列化结果。</returns>
        UniTask<SaveResult> SerializeAsync(
            SaveEnvelope envelope,
            Stream destination,
            CancellationToken cancellationToken);

        /// <summary>
        /// 从源 Stream 当前 Position 异步恢复完整存档容器，且不释放源 Stream。
        /// </summary>
        /// <param name="source">调用方拥有的可读源 Stream。</param>
        /// <param name="snapshotTypeResolver">按模块 ID 和版本解析快照实际类型的服务。</param>
        /// <param name="cancellationToken">取消异步反序列化的令牌。</param>
        /// <returns>完整存档容器或结构化失败。</returns>
        UniTask<SaveResult<SaveEnvelope>> DeserializeAsync(
            Stream source,
            ISaveSnapshotTypeResolver snapshotTypeResolver,
            CancellationToken cancellationToken);
    }

    #endregion

    #region 解析接口

    /// <summary>
    /// 根据容器头中的 FormatId 解析对应序列化器。
    /// </summary>
    public interface ISaveSerializerResolver
    {
        /// <summary>
        /// 解析指定格式标识，不对未知格式执行默认回退。
        /// </summary>
        /// <param name="formatId">区分大小写的格式标识。</param>
        /// <returns>对应序列化器或 UnknownSerializerFormat 失败。</returns>
        SaveResult<ISaveSerializer> Resolve(string formatId);
    }

    /// <summary>
    /// 根据模块标识和独立版本解析快照的实际 CLR 类型。
    /// </summary>
    public interface ISaveSnapshotTypeResolver
    {
        /// <summary>
        /// 解析指定模块版本的快照类型。
        /// </summary>
        /// <param name="moduleId">模块标识。</param>
        /// <param name="version">正整数模块版本。</param>
        /// <returns>实现 ISaveModuleSnapshot 的具体类型或结构化失败。</returns>
        SaveResult<Type> Resolve(SaveModuleId moduleId, int version);
    }

    #endregion
}
