using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using Cysharp.Threading.Tasks;
using WS_Modules.LogModule;
using Object = UnityEngine.Object;

namespace WS_Modules.ResLoadModule
{
    public abstract class ResInfoBase
    {
        /// <summary>
        /// 用于判断是否需要直接卸载资源，避免频繁卸载资源
        /// </summary>
        public bool DeleteImmediately { get; set; } = true;
        public int RefCount { get; set; }
        public void AddRef() => RefCount++;
        public void SubRef() => RefCount--;
        public virtual void Clear() => RefCount = 0;
    }

    /// <summary>
    /// Resources 资源加载管理模块
    /// </summary>
    public class ResourcesLoadMgrModule : IResLoad<string>
    {
        private class ResInfo<T> : ResInfoBase where T : Object
        {
            public T Asset { get; set; }
            // 取消令牌源，用于取消异步加载（如果需要）
            public CancellationTokenSource Cts { get; set; }
            public UnityAction<T> Callback { get; set; }

            public void StopLoadingSource()
            {
                if (Cts is not null)
                {
                    Cts.Cancel();
                    Cts.Dispose();
                    Cts = null;
                }
            }

            /// <summary>
            /// 必须确保资源加载完成后调用此方法以执行回调
            /// </summary>
            public void InvokeCallBack()
            {
                Callback?.Invoke(Asset);
                Callback = null; // 确保回调只调用一次
            }

            public override void Clear()
            {
                base.Clear();
                Asset = null;
                StopLoadingSource();
                Callback = null;
            }
        }

        private readonly Dictionary<string, ResInfoBase> _resDic = new();

        #region Load
        public T Load<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 规范化路径，避免 "A/B" 和 "A\B" 被视为不同资源
            path = path.Replace('\\', '/');

            // 通过路径和类型构建唯一标识，确保同一路径不同类型的资源能正确区分
            string resPath = path + '_' + typeof(T).Name;

            ResInfo<T> resInfo;
            // 存在直接返回
            if (_resDic.TryGetValue(resPath, out var info))
            {
                resInfo = info as ResInfo<T> ??
                          throw new NullReferenceException(
                              $"[ResourcesLoadMgr] ResInfo type mismatch for path: {path}");

                // 资源正在加载中，停止异步加载，改为同步加载（强制覆盖）
                if (resInfo is { Asset: null })
                {
                    resInfo.StopLoadingSource();
                    resInfo.Asset = Resources.Load<T>(path);
                    resInfo.InvokeCallBack();
                }

                resInfo.AddRef();

                return resInfo.Asset;
            }

            // 不存在则加载并添加到字典
            var asset = Resources.Load<T>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[ResourcesLoadMgr] Load failed: {path}");
                return null;
            }

            resInfo = new ResInfo<T> { Asset = asset, RefCount = 1 };
            _resDic[resPath] = resInfo;
            return asset;
        }

        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : Object
        {
            if (string.IsNullOrEmpty(path))
            {
                callback?.Invoke(null);
                return;
            }

            path = path.Replace('\\', '/');

            string resPath = path + '_' + typeof(T).Name;
            ResInfo<T> info;

            if (_resDic.TryGetValue(resPath, out var baseInfo))
            {
                info = baseInfo as ResInfo<T> ??
                       throw new NullReferenceException("[ResourcesLoadMgr] ResInfo type mismatch for path: " + path);
                info.AddRef();
                // 资源已加载完成，直接返回并回调
                if (info is { Asset: not null })
                {
                    WSLog.LogSuccess("[ResourcesLoadMgr] 已有资源，直接返回: " + path);
                    callback?.Invoke(info.Asset);
                }
                else
                    // 资源正在加载中，直接等待完成并回调，将回调添加到现有的 LoadingSource 上，避免重复加载
                    info.Callback += callback;
            }
            else // 不存在则创建新的 ResInfo 并开始异步加载
            {
                info = new ResInfo<T>
                {
                    RefCount = 1, Cts = new CancellationTokenSource(), Callback = callback
                };
                _resDic[resPath] = info;
                LoadAssetAsyncInternal<T>(path, resPath, info.Cts.Token).Forget();
            }
        }

        public async UniTask<T> LoadAsync<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return null;

            path = path.Replace('\\', '/');
            string resPath = path + '_' + typeof(T).Name;

            ResInfo<T> info;
            if (_resDic.TryGetValue(resPath, out var baseInfo))
            {
                info = baseInfo as ResInfo<T> ??
                       throw new NullReferenceException("[ResourcesLoadMgr] ResInfo type mismatch for path: " + path);
                info.AddRef();
                if (info is { Asset: not null })
                {
                    WSLog.LogSuccess("[ResourcesLoadMgr] 已有资源，直接返回: " + path);
                    return info.Asset;
                }
                else
                {
                    // 资源正在加载中，等待完成并返回结果
                    while (info.Asset == null)
                    {
                        await UniTask.Yield();
                    }

                    return info.Asset;
                }
            }
            else
            {
                info = new ResInfo<T>
                {
                    RefCount = 1, Cts = new CancellationTokenSource()
                };
                _resDic[resPath] = info;
                await LoadAssetAsyncInternal<T>(path, resPath, info.Cts.Token);
                return info.Asset;
            }
        }

        private async UniTask LoadAssetAsyncInternal<T>(string path, string resPath, CancellationToken token)
            where T : Object
        {
            var request = Resources.LoadAsync<T>(path);

            // 注意：Resources.LoadAsync 无法真正的被 Cancel，底层的加载过程必然会完成。
            // 这里传入 token 只是为了让 UniTask 能够响应 await 结束，但我们必须处理“加载完发现被取消”的情况。
            // 使用 SuppressCancellationThrow 避免抛出异常
            await request.ToUniTask(cancellationToken: token).SuppressCancellationThrow();

            // 模拟加载时间，测试取消功能（实际项目中可以去掉）
            // await UniTask.Delay(1000);

            // 无论是否 Cancel，我们都要检查资源是否加载出来了，因为 Resources 不受 token 控制
            if (_resDic.TryGetValue(resPath, out var baseInfo) && baseInfo is ResInfo<T> info)
            {
                // 情况1：资源加载成功
                if (request.asset != null)
                {
                    // 检查是否在加载过程中被“取消/卸载”了 (通过 RefCount == 0 判断)
                    // 或者 token 被触发了除了 RefCount 用法外的其他 Cancel (虽然目前主要由 RefCount 触发)
                    if (info.DeleteImmediately && (info.RefCount <= 0 || token.IsCancellationRequested))
                    {
                        // 这是一个“孤儿”资源，刚生下来就没人要了，必须负责销毁它
                        WSLog.Log($"[ResourcesLoadMgr] Async load canceled/aborted for: {path}, destroying asset.");

                        // 从字典彻底移除（UnloadInternal 里因为还在加载没敢移除，留给我们现在移除）
                        _resDic.Remove(resPath);

                        // 执行真正的卸载逻辑
                        // 注意：GameObject (Prefab) 不能用 UnloadAsset 卸载，只能任其丢失引用，等待 Resources.UnloadUnusedAssets
                        if (request.asset is not GameObject)
                        {
                            Resources.UnloadAsset(request.asset);
                        }

                        // 确保引用清空
                        info.Asset = null;
                        info.Callback = null;
                        return;
                    }

                    // 正常流程：赋值
                    info.Asset = request.asset as T;
                    info.InvokeCallBack();
                }
                else
                {
                    WSLog.LogWarning($"[ResourcesLoadMgr] Async load failed: {path}");
                    // 加载失败还是要移除字典
                    _resDic.Remove(resPath);
                }

                info.Cts = null;
            }
            else
            {
                // 为了防止多线程或极端情况下的字典丢失，如果字典里都没这个 Info 了，
                // 但资源还是加载出来了，也要卸载掉，防止泄漏
                if (request.asset != null)
                {
                    if (request.asset is not GameObject)
                    {
                        Resources.UnloadAsset(request.asset);
                    }
                }
            }
        }

        /// <summary>
        /// [Resources] 异步加载多个资源 (UniTask)
        /// 原理：循环调用单资源加载，使用 WhenAll 等待所有完成
        /// </summary>
        public async UniTask<IList<T>> LoadAssetsAsync<T>(params string[] keys) where T : UnityEngine.Object
        {
            if (keys == null || keys.Length == 0) return null;

            // 创建一个任务列表
            List<UniTask<T>> tasks = new List<UniTask<T>>(keys.Length);

            foreach (var key in keys)
            {
                // 这里直接复用你已经写好的单资源加载方法 LoadAsync<T>(key)
                // 这样可以复用引用计数、缓存等逻辑
                tasks.Add(LoadAsync<T>(key));
            }

            // 并行等待所有加载完成
            T[] results = await UniTask.WhenAll(tasks);

            return results;
        }

        /// <summary>
        /// [Resources] 异步加载多个资源 (Callback)
        /// 原理：调用上面的 UniTask 版本，完成后执行回调
        /// 尽量不要使用 Resources 的回调版本，最好直接一发即忘，如果需要回调请使用 UniTask 版本并在外部转换为回调，避免在 Resources 模块内部实现复杂的回调逻辑
        /// 该方法只是为了满足接口要求提供的简单封装，实际使用中建议直接使用 UniTask 版本以获得更好的性能和更简洁的代码结构
        /// </summary>
        public async void LoadAssetsAsync<T>(UnityAction<T> callback, params string[] keys)
            where T : UnityEngine.Object
        {
            WSLog.LogWarning("[ResourcesLoadMgr] 尽量不要使用 Resources 的回调版本，最好直接一发即忘，该方法只是为了满足接口要求提供的简单封装");
            if (keys == null || keys.Length == 0)
            {
                callback?.Invoke(null);
                return;
            }

            // 复用上面的 UniTask 实现
            IList<T> results = await LoadAssetsAsync<T>(keys);

            foreach (var item in results)
            {
                callback?.Invoke(item);
            }
        }
        #endregion

        #region Unload
        public void UnLoad<T>(string key, bool deleteImmediately = true) where T : Object
        {
            if (string.IsNullOrEmpty(key)) return;

            key = key.Replace('\\', '/');
            string resPath = key + '_' + typeof(T).Name;
            UnLoadInternal<T>(resPath, deleteImmediately, null);
        }

        public void UnLoadAsync<T>(string key, UnityAction<T> callback, bool deleteImmediately = true) where T : Object
        {
            if (string.IsNullOrEmpty(key)) return;

            key = key.Replace('\\', '/');
            string resPath = key + '_' + typeof(T).Name;
            UnLoadInternal(resPath, deleteImmediately, callback);
        }

        public void UnLoadAssets<T>(bool deleteImmediately = true, params string[] keys) where T : Object
        {
            if (keys == null || keys.Length == 0) return;

            foreach (var key in keys)
            {
                UnLoad<T>(key, deleteImmediately);
            }
        }

        public void UnLoadAssetsAsync<T>(UnityAction<T> callback, bool deleteImmediately = true, params string[] keys)
            where T : Object
        {
            if (keys == null || keys.Length == 0) return;

            foreach (var key in keys)
            {
                UnLoadAsync<T>(key, callback, deleteImmediately);
            }
        }

        /// <summary>
        /// 卸载所有资源，包括引用计数不为 0 的资源（强制卸载）
        /// </summary>
        public void UnLoadAll()
        {
            UnloadAllAsyncInternal().Forget();
        }

        private async UniTaskVoid UnloadAllAsyncInternal(UnityAction callback = null)
        {
            foreach (var info in _resDic.Values)
            {
                // 强制设置为需要立即删除，以便在 Clear 中正确处理可能的逻辑（虽然 Clear 主要是重置数据）
                info.DeleteImmediately = true;
                info.Clear();
            }

            _resDic.Clear();

            await Resources.UnloadUnusedAssets();
            callback?.Invoke();
        }

        public void UnloadUnusedAssets(UnityAction callback = null)
        {
            UnLoadUnusedAssetsAsyncInternal(callback).Forget();
        }

        private async UniTask UnLoadUnusedAssetsAsyncInternal(UnityAction callback)
        {
            // 查找引用计数为 0 的资源，无论 DeleteImmediately 状态如何，只要调用了 UnloadUnusedAssets 就应该清理
            // 或者策略是只清理标记为 DeleteImmediately 的？通常 UnloadUnusedAssets 意味着清理所有不用的。
            // 为了安全起见和符合语义，这里应该清理所有 RefCount == 0 的。

            List<string> keysToRemove = new List<string>();
            foreach (var pair in _resDic)
            {
                if (pair.Value.RefCount <= 0)
                {
                    keysToRemove.Add(pair.Key);
                    // 确保 Asset 被卸载
                    /*
                       注意：Resources.UnloadUnusedAssets() 会自动清理没有引用的资源。
                       这里我们需要做的是从字典里移除 key，断开引用。
                       如果 Asset 是 GameObject (Prefab)，我们移除引用后，Resources.UnloadUnusedAssets() 会处理它。
                       如果 Asset 是 Texture 等，可以手动调用 Resources.UnloadAsset() 来加速释放，
                       但 ResInfo 只有泛型 T，这里基类不知道 T 是什么，需要转换。
                    */
                    // 我们在 ResInfoBase 中无法直接访问 Asset。
                    // 由于调用了 Resources.UnloadUnusedAssets()，只要我们断开引用（从字典移除），
                    // 并且没有其他地方引用该资源，它就会被卸载。
                }
            }

            foreach (var key in keysToRemove) _resDic.Remove(key);

            await Resources.UnloadUnusedAssets();
            callback?.Invoke();
        }

        private void UnLoadInternal<T>(string resPath, bool deleteImmediately, UnityAction<T> callback) where T : Object
        {
            if (_resDic.TryGetValue(resPath, out var baseInfo) && baseInfo is ResInfo<T> info)
            {
                info.SubRef();
                info.DeleteImmediately = deleteImmediately;

                // 只有引用计数 <= 0 并且对象标记为 DeleteImmediately 才是需要卸载的
                if (info.RefCount <= 0 && info.DeleteImmediately)
                {
                    // 如果资源已经加载完毕
                    if (info.Asset != null)
                    {
                        // GameObject 不能使用 UnloadAsset 卸载，只能销毁实例或等待 UnloadUnusedAssets
                        // 但这里存的是 Prefab 引用，不能 Destroy。只能让它在 UnloadUnusedAssets 时被回收。
                        // 对于非 GameObject 资源，可以手动 UnloadAsset
                        if (info.Asset is not GameObject)
                        {
                            Resources.UnloadAsset(info.Asset);
                        }

                        info.Asset = null;
                        _resDic.Remove(resPath); // 只有确认卸载了才移除
                    }
                    else // 资源正在加载中
                    {
                        // 关键修改：
                        // 既然正在加载，我们不能单纯的 Remove，否则 LoadAsyncInternal 回来找不到人，资源就泄漏了。
                        // 我们要做的是：
                        // 1. 取消 Token（通知 LoadAsyncInternal，你回来的时候不用通知回调了，直接销毁资源吧）
                        // 2. 移除上层用户的回调
                        // 3. 字典里保留这个 entry，等待 LoadAsyncInternal 完事后自己清理

                        info.StopLoadingSource(); // 触发 Cts.Cancel()

                        if (callback is not null && info.Callback != null)
                            info.Callback -= callback;

                        // 不在这里 Remove！交给 LoadAssetAsyncInternal 去 Remove
                        // _resDic.Remove(resPath);

                        WSLog.Log($"[ResourcesLoadMgr] Request unload while loading: {resPath}. Marked for deletion.");
                    }
                }
                else
                {
                    // 引用计数 > 0, just remove callback if provided
                    if (callback is not null && info.Callback != null)
                    {
                        info.Callback -= callback;
                    }
                }
            }
        }
        #endregion

        public int GetRefCount<T>(string path) where T : Object
        {
            if (string.IsNullOrEmpty(path)) return 0;
            path = path.Replace('\\', '/');
            string resPath = path + '_' + typeof(T).Name;

            if (_resDic.TryGetValue(resPath, out var baseInfo) && baseInfo is ResInfo<T> info)
            {
                return info.RefCount;
            }

            return 0;
        }
    }
}