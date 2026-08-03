using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 对外提供实例挂点查询能力，使调用方无需了解实例层级结构。
    /// </summary>
    public interface IMarkerProvider
    {
        /// <summary>
        /// 查询指定语义挂点当前对应的 Transform。
        /// </summary>
        /// <param name="key">需要解析的挂点资产键。</param>
        /// <param name="marker">查询成功时返回当前实例层级中的实际挂点。</param>
        /// <returns>挂点键存在且目标 Transform 仍有效时返回 true。</returns>
        bool TryGetMarker(MarkerKey key, out Transform marker);
    }
}
