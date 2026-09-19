using System.Collections.Generic;
using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 对外提供实例级 Socket 查询能力，使调用方无需了解 MarkerProvider 的层级与索引实现。
    /// </summary>
    public interface IMarkerProvider
    {
        /// <summary>获取当前角色配置声明为必需的 MarkerKey。</summary>
        IReadOnlyList<MarkerKey> RequiredMarkerKeys { get; }

        /// <summary>获取最近一次完整重建与必需项校验是否成功。</summary>
        bool IsValid { get; }

        /// <summary>
        /// 获取当前 Provider 作用域内的全部 TransformMarker，供运行时重建和编辑器诊断共用。
        /// </summary>
        /// <param name="results">
        /// 接收 Marker 的调用方列表；实现会先清空该列表，再写入包含未激活节点的当前作用域结果。
        /// </param>
        /// <exception cref="System.ArgumentNullException">当调用方传入空列表引用时抛出。</exception>
        void GetMarkers(List<TransformMarker> results);

        /// <summary>重新收集当前作用域 Marker 并校验必需项。</summary>
        /// <returns>索引与必需项全部有效时返回 true。</returns>
        bool TryRebuild();

        /// <summary>
        /// 查询当前 Provider 作用域中指定语义 Socket 对应的 Transform。
        /// </summary>
        /// <param name="key">需要解析的挂点资产键。</param>
        /// <param name="marker">查询成功时返回当前实例层级中的实际挂点。</param>
        /// <returns>挂点键存在且目标 Transform 仍有效时返回 true。</returns>
        bool TryGetMarker(MarkerKey key, out Transform marker);
    }
}
