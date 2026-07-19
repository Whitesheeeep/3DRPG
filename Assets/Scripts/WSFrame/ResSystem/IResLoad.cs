using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;

namespace WS_Modules.ResLoadModule
{
    /// <summary>
    /// 用于资源加载的接口，提供同步和异步加载方法，以及资源卸载方法，支持泛型和键值对方式，可以适用于不同类型的资源加载需求，例如：预制体、音频、文本等
    /// </summary>
    /// <typeparam name="TKey">该类型用于指定加载资源所使用的 Key 类型</typeparam>
    public interface IResLoad<TKey> where TKey : notnull
    {
        public T Load<T>(TKey key) where T : UnityEngine.Object;
        public void LoadAsync<T>(TKey key, UnityAction<T> callback) where T : UnityEngine.Object;
        public UniTask<T> LoadAsync<T>(TKey key) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载多个资源 (Callback)
        /// </summary>
        public void LoadAssetsAsync<T>(UnityAction<T> callback, params TKey[] keys) where T : UnityEngine.Object;

        /// <summary>
        /// 异步加载多个资源 (UniTask)
        /// </summary>
        public UniTask<IList<T>> LoadAssetsAsync<T>(params TKey[] keys) where T : UnityEngine.Object;

        public void UnLoad<T>(string key, bool deleteImmediately = true) where T : UnityEngine.Object;

        /// <summary>
        /// 适用于使用 Unitask 进行同步加载的资源卸载方法，提供了一个可选的回调参数，当资源卸载完成后会调用该回调函数，参数为卸载的资源对象，可以用于执行一些清理操作或者更新 UI 等逻辑
        /// </summary>
        /// <param name="key"></param>
        /// <param name="deleteImmediately"></param>
        /// <param name="callback"></param>
        /// <typeparam name="T"></typeparam>
        public void UnLoadAsync<T>(string key, UnityAction<T> callback, bool deleteImmediately = true)
            where T : UnityEngine.Object;

        public void UnLoadAssets<T>(bool deleteImmediately = true, params string[] keys) where T : UnityEngine.Object;

        public void UnLoadAssetsAsync<T>(UnityAction<T> callback, bool deleteImmediately = true, params string[] keys)
            where T : UnityEngine.Object;
        
        public void UnLoadAll();
    }
}