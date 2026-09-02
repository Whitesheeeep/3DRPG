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
