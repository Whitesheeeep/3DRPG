using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;

namespace RPG.Game.UI.Services
{
    /// <summary>
    /// 管理窗口动态图集的加载去重、成功缓存、失败重试和延迟释放。
    /// </summary>
    internal sealed class WindowSpriteAtlasLeaseService : IDisposable
    {
        #region 配置字段

        // key：SpriteAtlas Address；value：本 Service 当前持有的配置地址。
        private readonly IReadOnlyList<string> atlasAddresses;
        private readonly float releaseDelaySeconds;

        #endregion

        #region 状态字段

        // key：SpriteAtlas Address；value：当前成功加载并持有引用的 Atlas。
        private readonly Dictionary<string, SpriteAtlas> atlasByAddressMap = new();
        // key：SpriteAtlas Address；value：该地址当前唯一的底层加载完成源。
        private readonly Dictionary<string, UniTaskCompletionSource<AtlasLoadResult>> loadCompletionByAddressMap = new();
        private readonly CancellationTokenSource lifetimeCancellationSource = new();
        private CancellationTokenSource releaseCancellationSource;
        private int leaseVersion;
        private bool disposed;

        #endregion

        #region 属性与事件

        /// <summary>
        /// 创建动态图集租约服务。
        /// </summary>
        /// <param name="configuredAddresses">本窗口需要准备的动态图集地址。</param>
        /// <param name="releaseDelaySecondsValue">隐藏后的延迟释放秒数。</param>
        public WindowSpriteAtlasLeaseService(
            IReadOnlyList<string> configuredAddresses,
            float releaseDelaySecondsValue)
        {
            atlasAddresses = configuredAddresses ?? throw new ArgumentNullException(nameof(configuredAddresses));
            releaseDelaySeconds = Mathf.Max(0f, releaseDelaySecondsValue);
        }

        /// <summary>
        /// 获取本 Service 配置的动态图集地址。
        /// </summary>
        public IReadOnlyList<string> AtlasAddresses => atlasAddresses;

        /// <summary>
        /// 获取隐藏后的延迟释放秒数。
        /// </summary>
        public float ReleaseDelaySeconds => releaseDelaySeconds;

        /// <summary>
        /// 动态图集实际释放后通知 Controller 清理仍持有 Sprite 引用的 View。
        /// </summary>
        public event Action Released;

        #endregion

        #region 加载与查询

        /// <summary>
        /// 开始本轮配置地址的加载；成功缓存和进行中的请求会复用，失败地址会重新请求。
        /// </summary>
        /// <returns>本轮所有未缓存地址完成尝试后返回是否全部成功。</returns>
        public async UniTask<bool> BeginLoadConfiguredAtlasesAsync()
        {
            if (disposed) return false;
            CancelRelease();
            int requestVersion = leaseVersion;
            List<string> addresses = GetUniqueAddresses();
            var loadTasks = new List<UniTask<AtlasLoadResult>>(addresses.Count);
            for (int index = 0; index < addresses.Count; index++)
            {
                string address = addresses[index];
                if (atlasByAddressMap.ContainsKey(address))
                {
                    WSLog.Log($"[BagWindow][Atlas] 复用缓存地址：{address}");
                    continue;
                }

                if (loadCompletionByAddressMap.TryGetValue(address,
                        out UniTaskCompletionSource<AtlasLoadResult> existingLoad))
                {
                    WSLog.Log($"[BagWindow][Atlas] 复用进行中的加载地址：{address}");
                    loadTasks.Add(existingLoad.Task);
                    continue;
                }

                WSLog.Log($"[BagWindow][Atlas] 开始加载地址：{address}");
                var completionSource = new UniTaskCompletionSource<AtlasLoadResult>();
                loadCompletionByAddressMap.Add(address, completionSource);
                loadTasks.Add(completionSource.Task);
                CompleteLoadAsync(address, requestVersion, completionSource).Forget(HandleLoadException);
            }

            AtlasLoadResult[] results = loadTasks.Count == 0
                ? Array.Empty<AtlasLoadResult>()
                : await UniTask.WhenAll(loadTasks);
            int failedCount = 0;
            for (int index = 0; index < results.Length; index++)
                if (results[index].Atlas == null) failedCount++;

            WSLog.Log($"[BagWindow][Atlas] 本轮加载完成：成功 {results.Length - failedCount}，失败 {failedCount}，缓存 {atlasByAddressMap.Count}。");
            return failedCount == 0;
        }

        /// <summary>
        /// 按 Address 和 SpriteName 查询已经成功加载的 Sprite。
        /// </summary>
        /// <param name="address">SpriteAtlas Address。</param>
        /// <param name="spriteName">Sprite 名称。</param>
        /// <param name="sprite">查询到的 Sprite。</param>
        /// <returns>Sprite 已加载且查询成功时返回 true；否则返回 false。</returns>
        public bool TryGetSprite(string address, string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(spriteName) ||
                !atlasByAddressMap.TryGetValue(address, out SpriteAtlas atlas) || atlas == null)
                return false;

            sprite = atlas.GetSprite(spriteName);
            return sprite != null;
        }

        #endregion

        #region 延迟释放与销毁

        /// <summary>
        /// 启动隐藏后的真实时间延迟释放。
        /// </summary>
        public void ScheduleRelease()
        {
            if (disposed) return;
            CancelRelease();
            releaseCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeCancellationSource.Token);
            ReleaseAfterDelayAsync(releaseCancellationSource.Token).Forget(HandleReleaseException);
            WSLog.Log($"[BagWindow][Atlas] Hide 释放倒计时开始：{releaseDelaySeconds:F1} 秒。");
        }

        /// <summary>
        /// 取消尚未执行的释放倒计时并保留成功租约。
        /// </summary>
        public void CancelRelease()
        {
            if (releaseCancellationSource == null) return;
            releaseCancellationSource.Cancel();
            releaseCancellationSource.Dispose();
            releaseCancellationSource = null;
            if (!disposed) WSLog.Log("[BagWindow][Atlas] 重新打开取消释放，复用成功缓存。");
        }

        /// <summary>
        /// 立即释放当前持有的所有动态图集引用并通知 Controller 清理 Sprite。
        /// </summary>
        public void ReleaseImmediately()
        {
            if (disposed) return;
            ++leaseVersion;
            foreach (KeyValuePair<string, SpriteAtlas> pair in atlasByAddressMap)
            {
                if (pair.Value == null) continue;
                ResSystem.Instance.UnLoad<SpriteAtlas>(pair.Key);
                WSLog.Log($"[BagWindow][Atlas] 立即释放地址：{pair.Key}");
            }

            atlasByAddressMap.Clear();
            loadCompletionByAddressMap.Clear();
            // 先清理租约表再通知 View，避免订阅方继续读取已释放的 Atlas。
            Released?.Invoke();
        }

        /// <summary>
        /// 幂等释放延迟任务、Atlas 租约和生命周期资源。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            releaseCancellationSource?.Cancel();
            releaseCancellationSource?.Dispose();
            releaseCancellationSource = null;
            lifetimeCancellationSource.Cancel();
            loadCompletionByAddressMap.Clear();
            ReleaseImmediately();
            disposed = true;
            lifetimeCancellationSource.Dispose();
        }

        /// <summary>
        /// 使用不受 timeScale 影响的真实时间等待延迟释放。
        /// </summary>
        /// <param name="cancellationToken">释放取消令牌。</param>
        private async UniTask ReleaseAfterDelayAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(releaseDelaySeconds),
                ignoreTimeScale: true, cancellationToken: cancellationToken);
            if (disposed || cancellationToken.IsCancellationRequested) return;
            WSLog.Log("[BagWindow][Atlas] 延迟释放实际执行。");
            ReleaseImmediately();
        }

        /// <summary>
        /// 记录延迟释放任务中的非取消异常。
        /// </summary>
        /// <param name="exception">异步异常。</param>
        private static void HandleReleaseException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception);
        }

        #endregion

        #region 异步加载内部实现

        /// <summary>
        /// 加载单个地址，并在租约版本过期时对称释放迟到结果。
        /// </summary>
        /// <param name="address">Atlas Address。</param>
        /// <param name="requestVersion">发起加载时的租约版本。</param>
        /// <returns>单地址加载结果。</returns>
        private async UniTask<AtlasLoadResult> LoadOneAsync(string address, int requestVersion)
        {
            try
            {
                SpriteAtlas atlas = await ResSystem.Instance.LoadAsync<SpriteAtlas>(address);
                if (atlas == null)
                {
                    WSLog.LogWarning($"[BagWindow][Atlas] 地址加载为空：{address}");
                    return new AtlasLoadResult(address, null);
                }

                if (disposed || requestVersion != leaseVersion)
                {
                    ResSystem.Instance.UnLoad<SpriteAtlas>(address);
                    WSLog.Log($"[BagWindow][Atlas] 过期异步结果被丢弃并释放：{address}");
                    return new AtlasLoadResult(address, null);
                }

                atlasByAddressMap[address] = atlas;
                WSLog.Log($"[BagWindow][Atlas] 单地址加载成功：{address}");
                return new AtlasLoadResult(address, atlas);
            }
            catch (Exception exception)
            {
                WSLog.LogWarning($"[BagWindow][Atlas] 单地址加载失败：{address}，{exception.Message}");
                return new AtlasLoadResult(address, null);
            }
        }

        /// <summary>
        /// 完成单地址任务并移除进行中索引，保证同地址请求只共享一次底层加载。
        /// </summary>
        /// <param name="address">Atlas Address。</param>
        /// <param name="requestVersion">发起加载时的租约版本。</param>
        /// <param name="completionSource">该地址的完成源。</param>
        private async UniTask CompleteLoadAsync(string address, int requestVersion,
            UniTaskCompletionSource<AtlasLoadResult> completionSource)
        {
            AtlasLoadResult result = await LoadOneAsync(address, requestVersion);
            if (loadCompletionByAddressMap.TryGetValue(address,
                    out UniTaskCompletionSource<AtlasLoadResult> currentSource) &&
                ReferenceEquals(currentSource, completionSource))
                loadCompletionByAddressMap.Remove(address);
            completionSource.TrySetResult(result);
        }

        /// <summary>
        /// 记录单地址加载任务中的非取消异常。
        /// </summary>
        /// <param name="exception">异步异常。</param>
        private static void HandleLoadException(Exception exception)
        {
            if (!(exception is OperationCanceledException)) Debug.LogException(exception);
        }

        /// <summary>
        /// 复制配置并按顺序去除空地址和重复地址。
        /// </summary>
        /// <returns>本轮需要处理的唯一地址列表。</returns>
        private List<string> GetUniqueAddresses()
        {
            var result = new List<string>(atlasAddresses.Count);
            var addressSet = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < atlasAddresses.Count; index++)
            {
                string address = atlasAddresses[index]?.Trim();
                if (string.IsNullOrWhiteSpace(address) || !addressSet.Add(address)) continue;
                result.Add(address);
            }

            return result;
        }

        #endregion

        #region 嵌套类型

        /// <summary>
        /// 保存单个 Atlas 地址的加载结果。
        /// </summary>
        private readonly struct AtlasLoadResult
        {
            /// <summary>
            /// 创建单地址加载结果。
            /// </summary>
            /// <param name="address">Atlas Address。</param>
            /// <param name="atlas">成功加载的 Atlas；失败时为空。</param>
            public AtlasLoadResult(string address, SpriteAtlas atlas)
            {
                Address = address;
                Atlas = atlas;
            }

            /// <summary>获取 Atlas Address。</summary>
            public string Address { get; }

            /// <summary>获取成功加载的 Atlas。</summary>
            public SpriteAtlas Atlas { get; }
        }

        #endregion
    }
}
