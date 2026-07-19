using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;

namespace WS_Modules.ResLoadModule
{
    /// <summary>
    /// 可寻址资源信息类
    /// 用于记录加载的资源句柄引用计数
    /// </summary>
    public class AddressablesInfo : ResInfoBase
    {
        // 异步操作句柄，用于管理资源的加载状态和释放
        private AsyncOperationHandle _handle;

        // 使用 UnityAction 替代 List 手动管理，利用多播委托特性
        private UnityAction<AsyncOperationHandle> _onCompleted;

        public AsyncOperationHandle Handle => _handle;
        public bool HandleIsValid => _handle.IsValid();
        public bool HandleIsDone => _handle.IsDone;
        public object HandleResult => _handle.IsValid() && _handle.IsDone ? _handle.Result : null;

        public AddressablesInfo(AsyncOperationHandle handle)
        {
            _handle = handle;
            RefCount = 1;

            // 注册到底层 Handle 的完成事件
            // 当底层操作完成时，触发我们管理的所有回调
            _handle.Completed += OnHandleCompleted;
        }

        private void OnHandleCompleted(AsyncOperationHandle op)
        {
            // 执行所有注册的回调
            _onCompleted?.Invoke(op);
            // 执行后清空引用
            _onCompleted = null;
        }

        public void AddCallback(UnityAction<AsyncOperationHandle> callback)
        {
            if (_handle.IsDone)
            {
                // 如果已经完成，直接执行
                callback?.Invoke(_handle);
            }
            else
            {
                // 如果未完成，订阅委托
                _onCompleted += callback;
            }
        }

        public void RemoveCallback(UnityAction<AsyncOperationHandle> callback)
        {
            if (callback is null) return;
            _onCompleted -= callback;
        }

        // 核心功能：在卸载资源时调用此方法，切断所有未执行的回调
        public void ClearCallbacks()
        {
            _onCompleted = null;
        }
    }

    /// <summary>
    /// Addressables 资源加载管理模块
    /// 实现 IResLoad 接口，提供同步/异步加载、资源卸载及缓存管理功能
    /// </summary>
    public class AddressablesLoadMgrModule : IResLoad<string>
    {
        // 资源缓存字典：Key为资源唯一标识(路径+类型等)，Value为资源信息(句柄+引用计数)
        private Dictionary<string, AddressablesInfo> _resDic = new();

        /// <summary>
        /// 同步加载资源
        /// <para>注意：Addressables 本质是异步的，此处使用 WaitForCompletion 阻塞主线程直到加载完成。</para>
        /// <para>警告：可能会导致帧率波动或卡顿，仅在必要时使用。</para>
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="key">资源 Key（Addressable Name/Label/Path）</param>
        /// <returns>加载的资源实例</returns>
        public T Load<T>(string key) where T : UnityEngine.Object
        {
            string resPath = key + '_' + typeof(T).Name;

            if (_resDic.TryGetValue(resPath, out var resInfo))
            {
                if (resInfo.Handle.IsValid())
                {
                    resInfo.AddRef();
                    if (resInfo.Handle.IsDone)
                        return resInfo.Handle.Result as T;
                    
                    // 如果正在加载中，WaitForCompletion 会阻塞直到完成
                    return (T)resInfo.Handle.WaitForCompletion();
                }

                // 如果句柄无效，说明之前加载失败了，移除缓存重新加载
                _resDic.Remove(resPath);
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            var result = handle.WaitForCompletion();

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                var newInfo = new AddressablesInfo(handle);
                _resDic.Add(resPath, newInfo);
                return result;
            }

            // 加载失败
            if (handle.IsValid()) Addressables.Release(handle);
            return null;
        }

        /// <summary>
        /// 异步加载资源 (Callback 方式)
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="key">资源 Key</param>
        /// <param name="callback">加载完成后的回调函，参数为加载的资源</param>
        public void LoadAsync<T>(string key, UnityAction<T> callback) where T : UnityEngine.Object
        {
            string resPath = key + '_' + typeof(T).Name;

            if (_resDic.TryGetValue(resPath, out var resInfo))
            {
                resInfo.AddRef();
                // 如果已经加载完成，直接回调
                if (resInfo.Handle.IsDone && resInfo.Handle.IsValid())
                {
                    callback?.Invoke(resInfo.Handle.Result as T);
                    return;
                }
            }
            else
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                resInfo = new AddressablesInfo(handle);
                _resDic.Add(resPath, resInfo);
            }

            // 第一次加载时，或者正在加载中的资源，都通过 AddressablesInfo 的 AddCallback 来管理回调
            // 注意：这里我们不直接订阅底层 Handle 的 Completed 事件，而是通过 AddressablesInfo 的 AddCallback 来管理回调
            // 使用 AddressablesInfo 的 AddCallback 管理回调
            resInfo.AddCallback((handle) => DecorateCallback(resPath, resInfo, handle, callback));
        }

        /// <summary>
        /// 异步加载资源 (UniTask 方式)
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="key">资源 Key</param>
        /// <returns>包含资源的 UniTask</returns>
        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            string resPath = key + '_' + typeof(T).Name;

            if (_resDic.TryGetValue(resPath, out var resInfo))
            {
                resInfo.AddRef();
                return await resInfo.Handle.Convert<T>().Task;
            }

            var handle = Addressables.LoadAssetAsync<T>(key);
            var newInfo = new AddressablesInfo(handle);
            _resDic.Add(resPath, newInfo);

            try
            {
                var result = await handle.Task;
                // 判断是否进行了卸载（可能在等待过程中调用了 UnLoad 导致引用计数为0并删除了缓存）
                if (!_resDic.ContainsKey(resPath))
                {
                    // 已经被卸载了，立即释放资源
                    if (handle.IsValid()) Addressables.Release(handle);
                    return null;
                }

                return result;
            }
            catch (Exception)
            {
                if (_resDic.TryGetValue(resPath, out var current) && current == newInfo)
                    _resDic.Remove(resPath);
                return null;
            }
        }

        #region 加载多个资源
        /// <summary>
        /// 重载版本：默认使用 MergeMode.Union (并集) 加载同时满足所有 Key 的资源
        /// </summary>
        public void LoadAssetsAsync<T>(UnityAction<T> callback, params string[] keys)
            where T : UnityEngine.Object
        {
            LoadAssetsAsync(callback, Addressables.MergeMode.Union, keys);
        }

        // 重载版本：默认使用 MergeMode.Union (并集)
        public async UniTask<IList<T>> LoadAssetsAsync<T>(params string[] keys) where T : UnityEngine.Object
        {
            return await LoadAssetsAsync<T>(Addressables.MergeMode.Union, keys);
        }

        /// <summary>
        /// 异步加载多个资源 (Callback 方式)
        /// 根据 Labels 或 Keys 加载一组资源
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="callback">加载完成回调，返回资源列表中的每一项</param>
        /// <param name="mode">合并模式 (Union:并集, Intersection:交集, UseFirst:使用第一个)</param>
        /// <param name="keys">资源 Key 列表</param>
        public void LoadAssetsAsync<T>(UnityAction<T> callback, Addressables.MergeMode mode,
            params string[] keys) where T : UnityEngine.Object
        {
            if (keys == null || keys.Length == 0) return;
            string combinedKey = string.Join("_", keys) + "_" + typeof(T).Name + "_" + mode;

            AsyncOperationHandle<IList<T>> handle;
            if (_resDic.TryGetValue(combinedKey, out var resInfo))
            {
                // 已经在加载或已加载，增加引用计数并等待完成
                resInfo.AddRef();
                handle = resInfo.Handle.Convert<IList<T>>();
                if (handle.IsDone && handle.IsValid())
                {
                    foreach (var item in handle.Result)
                    {
                        callback?.Invoke(item);
                    }

                    return;
                }
            }
            // 没有加载过，开始加载
            else
            {
                var keysList = new List<string>(keys);
                handle = Addressables.LoadAssetsAsync<T>(keysList, null, mode);
                resInfo = new AddressablesInfo(handle);
                _resDic.Add(combinedKey, resInfo);
            }

            resInfo.AddCallback((resHandle) => DecorateCallback(combinedKey, resInfo, resHandle, callback, true));
        }

        /// <summary>
        /// 异步加载多个资源 (UniTask 方式)
        /// </summary>
        /// <typeparam name="T">资源类型</typeparam>
        /// <param name="mode">合并模式</param>
        /// <param name="keys">资源 Key 列表</param>
        /// <returns>资列表的 UniTask</returns>
        public async UniTask<IList<T>> LoadAssetsAsync<T>(Addressables.MergeMode mode, params string[] keys)
            where T : UnityEngine.Object
        {
            if (keys == null || keys.Length == 0) return null;
            string combinedKey = string.Join("_", keys) + "_" + typeof(T).Name + "_" + mode;

            if (_resDic.TryGetValue(combinedKey, out var resInfo))
            {
                resInfo.AddRef();
                return await resInfo.Handle.Convert<IList<T>>().Task;
            }

            var keysList = new List<string>(keys);
            var handle = Addressables.LoadAssetsAsync<T>(keysList, null, mode);
            var newInfo = new AddressablesInfo(handle);
            _resDic.Add(combinedKey, newInfo);

            try
            {
                var result = await handle.Task;

                // 检查加载期间是否被卸载
                if (!_resDic.ContainsKey(combinedKey))
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                    return null;
                }

                return result;
            }
            catch (Exception)
            {
                if (_resDic.TryGetValue(combinedKey, out var current) && current == newInfo)
                    _resDic.Remove(combinedKey);
                return null;
            }
        }
        #endregion

        /// <summary>
        /// 卸载资源
        /// 根据 Key 和 类型 减少引用计数，计数为 0 时真正释放 Addressable handle
        /// </summary>
        public void UnLoad<T>(string key, bool deleteImmediately = true) where T : Object
        {
            string resPath = key + "_" + typeof(T).Name;

            UnloadInternal<T>(resPath, deleteImmediately);
        }

        public void UnLoadAsync<T>(string key, UnityAction<T> callback, bool deleteImmediately = true) where T : Object
        {
            string resPath = key + "_" + typeof(T).Name;

            UnloadInternal(resPath, deleteImmediately, callback);
        }

        /// <summary>
        /// 卸载通过 LoadAssetsAsync 加载的资源组
        /// </summary>
        public void UnLoadAssets<T>(Addressables.MergeMode mode, params string[] keys) where T : UnityEngine.Object
        {
            UnLoadAssetsAsync<T>(mode, true, null, keys);
        }

        public void UnLoadAssets<T>(Addressables.MergeMode mode, bool deleteImmediately, params string[] keys)
            where T : UnityEngine.Object
        {
            UnLoadAssetsAsync<T>(mode, deleteImmediately, null, keys);
        }

        public void UnLoadAssets<T>(bool deleteImmediately = true, params string[] keys) where T : Object
        {
            UnLoadAssetsAsync<T>(Addressables.MergeMode.Union, deleteImmediately, null, keys);
        }


        /// <summary>
        /// 卸载多个资源 (Callback 方式)，默认使用 MergeMode.Union (并集) 卸载同时满足所有 Key 的资源
        /// </summary>
        public void UnLoadAssetsAsync<T>(UnityAction<T> callback, bool deleteImmediately = true, params string[] keys)
            where T : Object
        {
            UnLoadAssetsAsync(Addressables.MergeMode.Union, deleteImmediately, callback, keys);
        }

        public void UnLoadAll()
        {
            foreach (var kvp in _resDic)
            {
                // 清空回调
                kvp.Value.ClearCallbacks();

                if (kvp.Value.HandleIsValid)
                    Addressables.Release(kvp.Value.Handle);
            }

            _resDic.Clear();
        }

        /// <summary>
        /// 因为可能使用的 Unitask 加载，因此卸载时可以不提供 回调函数，直接根据 Key 和类型 减少引用计数，计数为 0 时真正释放 Addressable handle
        /// </summary>
        public void UnLoadAssetsAsync<T>(Addressables.MergeMode mode, bool deleteImmediately,
            UnityAction<T> callback = null,
            params string[] keys)
            where T : UnityEngine.Object
        {
            if (keys == null || keys.Length == 0) return;
            string combinedKey = string.Join("_", keys) + "_" + typeof(T).Name + "_" + mode;
            UnloadInternal(combinedKey, deleteImmediately, callback, true);
        }

        /// <summary>
        /// 内部卸载逻辑，处理引用计数和 Handle 释放，因为可能加载的时候是同步加载，也可能是异步加载，所以需要一个统一的卸载逻辑来处理两种情况
        /// </summary>
        private void UnloadInternal<T>(string key, bool deleteImmediately, UnityAction<T> callback = null,
            bool isMulti = false)
            where T : Object
        {
            if (_resDic.TryGetValue(key, out var info))
            {
                info.SubRef();
                info.DeleteImmediately = deleteImmediately;
                if (callback is not null)
                    info.RemoveCallback(resHandle => DecorateCallback(key, info, resHandle, callback, isMulti));

                if (info.RefCount <= 0 && info.DeleteImmediately)
                {
                    // 卸载前清空所有等待的回调
                    info.ClearCallbacks();

                    if (info.HandleIsValid)
                        Addressables.Release(info.Handle);
                    _resDic.Remove(key);
                }
            }
        }

        /// <summary>
        /// 卸载所有未使用的资源 (引用计数为0)
        /// </summary>
        public void UnloadUnusedAssets()
        {
            List<string> toRemove = new List<string>();
            foreach (var kvp in _resDic)
            {
                if (kvp.Value.RefCount <= 0)
                {
                    // 卸载前清空回调
                    kvp.Value.ClearCallbacks();

                    if (kvp.Value.HandleIsValid)
                        Addressables.Release(kvp.Value.Handle);
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var key in toRemove)
                _resDic.Remove(key);

            Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// 清空所有资源
        /// 强制释放所有缓存的 Handle，并清理未使用的资源
        /// </summary>
        public void Clear()
        {
            foreach (var kvp in _resDic)
            {
                // 清空回调
                kvp.Value.ClearCallbacks();

                if (kvp.Value.HandleIsValid)
                    Addressables.Release(kvp.Value.Handle);
            }

            _resDic.Clear();
        }

        /// <summary>
        /// 用于装饰回调函数，处理加载结果并根据成功或失败执行相应的逻辑
        /// </summary>
        /// 用于装饰回调函数：处理加载结果并根据成功或失败执行相应的逻辑 /// </summary>
        /// <param name="resKey">资源唯一标识（Key\+Type 或组合 Key）</param>
        /// <param name="info">资源缓存信息</param>
        /// <param name="handle">异步操作句柄</param>
        /// <param name="callback">加载完成回调</param>
        /// <param name="isMulti">是否是 LoadAssets 的回调</param>
        /// <typeparam name="T"></typeparam>
        private void DecorateCallback<T>(string resKey, AddressablesInfo info, AsyncOperationHandle handle,
            UnityAction<T> callback, bool isMulti = false) where T : UnityEngine.Object
        {
            if (handle.Status == AsyncOperationStatus.Succeeded && handle.IsValid())
            {
                if (!isMulti)
                    callback?.Invoke(handle.Convert<T>().Result);
                else
                {
                    foreach (var item in handle.Convert<IList<T>>().Result)
                    {
                        callback?.Invoke(item);
                    }
                }
            }
            // 加载失败移除
            else
            {
                if (_resDic.TryGetValue(resKey, out var current) && current == info)
                    _resDic.Remove(resKey);
                callback?.Invoke(null);
            }
        }
    }
}


