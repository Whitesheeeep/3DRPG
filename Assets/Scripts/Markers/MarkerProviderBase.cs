using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPG.Markers
{
    /// <summary>
    /// 为需要通用 Marker Inspector 的 Provider Mono 组件提供统一的编辑器入口。
    /// </summary>
    public abstract class MarkerProviderBase : MonoBehaviour, IMarkerProvider
    {
        #region Provider 契约

        /// <summary>获取当前 Provider 声明为必需的 MarkerKey。</summary>
        public abstract IReadOnlyList<MarkerKey> RequiredMarkerKeys { get; }

        /// <summary>获取最近一次完整重建与必需项校验是否成功。</summary>
        public abstract bool IsValid { get; }

        /// <summary>
        /// 枚举当前 Provider 作用域内的全部 TransformMarker。
        /// </summary>
        /// <param name="results">接收 Marker 的调用方列表。</param>
        /// <exception cref="ArgumentNullException">当调用方传入空列表引用时抛出。</exception>
        public abstract void GetMarkers(List<TransformMarker> results);

        /// <summary>重新收集当前作用域 Marker 并校验必需项。</summary>
        /// <returns>索引与必需项全部有效时返回 true。</returns>
        public abstract bool TryRebuild();

        /// <summary>查询当前 Provider 作用域中指定 MarkerKey 对应的 Transform。</summary>
        /// <param name="key">需要解析的 MarkerKey。</param>
        /// <param name="marker">查询成功时返回当前实例中的实际 Transform。</param>
        /// <returns>挂点键存在且目标 Transform 仍有效时返回 true。</returns>
        public abstract bool TryGetMarker(MarkerKey key, out Transform marker);

        #endregion
    }
}
