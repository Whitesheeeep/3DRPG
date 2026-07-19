using System;
using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks; // 假设使用了 UniTask，如果是原生 Task 请改为 Task
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.Singleton;
using Object = UnityEngine.Object;

namespace WS_Modules.ResLoadModule
{
    /// <summary>
    /// 资源加载系统的单例门面。
    /// 它不直接实现加载逻辑，而是将请求转发给注入的 resLoadModule。
    /// </summary>
    public class ResSystem : SingletonBase<ResSystem>, IResLoad<string>
    {
        // 具体的资源加载实现（例如 ResourcesLoader, AddressablesLoader 等）
        private IResLoad<string> resLoadModule;
        /// <summary>
        /// 可通过 ResLoadModule 属性访问当前注入的资源加载模块，提供了对底层加载实现的直接访问权限。
        /// 可以在运行时切换不同的加载策略，例如从 Resources 切换到 Addressables，而不需要修改 ResSystem 的接口或调用代码。
        /// 或者直接调用 ResSystem.(ResLoadModule as 具体类).Load &lt;T&gt; (key) 来使用底层加载模块的功能，适用于需要访问特定加载模块功能的场景。
        /// </summary>
        public IResLoad<string> ResLoadModule => resLoadModule;

        /// <summary>
        /// 初始化资源系统，必须在使用前调用以注入具体的加载策略
        /// </summary>
        /// <param name="module">具体的加载模块实现</param>
        public void Initialize(IResLoad<string> module)
        {
            this.resLoadModule = module;
        }

        // SingletonBase 通常会自动创建实例，构造函数保持私有或受保护
        // 如果你的 SingletonBase 支持带参数构造，请保留原构造函数，
        // 否则建议使用 Init 方法进行依赖注入。
        private ResSystem()
        {
        }

        #region IResLoad<string> Implementation (Delegation)

        public T Load<T>(string key) where T : Object
        {
            CheckModule();
            return resLoadModule.Load<T>(key);
        }

        public void LoadAsync<T>(string key, UnityAction<T> callback) where T : Object
        {
            CheckModule();
            resLoadModule.LoadAsync(key, callback);
        }

        public async UniTask<T> LoadAsync<T>(string key) where T : Object
        {
            CheckModule();
            return await resLoadModule.LoadAsync<T>(key);
        }

        public void LoadAssetsAsync<T>(UnityAction<T> callback, params string[] keys) where T : Object
        {
            CheckModule();
            resLoadModule.LoadAssetsAsync(callback, keys);
        }

        public async UniTask<System.Collections.Generic.IList<T>> LoadAssetsAsync<T>(params string[] keys) where T : Object
        {
            CheckModule();
            return await resLoadModule.LoadAssetsAsync<T>(keys);
        }

        public void UnLoad<T>(string key, bool deleteImmediately = true) where T : Object
        {
            CheckModule();
            resLoadModule.UnLoad<T>(key, deleteImmediately);
        }

        public void UnLoadAsync<T>(string key, UnityAction<T> callback, bool deleteImmediately = true) where T : Object
        {
            CheckModule();
            resLoadModule.UnLoadAsync(key, callback, deleteImmediately);
        }

        public void UnLoadAssets<T>(bool deleteImmediately = true, params string[] keys) where T : Object
        {
            CheckModule();
            resLoadModule.UnLoadAssets<T>(deleteImmediately, keys);
        }

        public void UnLoadAssetsAsync<T>(UnityAction<T> callback, bool deleteImmediately = true, params string[] keys) where T : Object
        {
            CheckModule();
            resLoadModule.UnLoadAssetsAsync(callback, deleteImmediately, keys);
        }

        public void UnLoadAll()
        {
            CheckModule();
            resLoadModule.UnLoadAll();
        }

        // 扩展功能：加载并实例化
        public GameObject Instantiate(string key, Transform parent = null)
        {
            CheckModule();
            var prefab = resLoadModule.Load<GameObject>(key);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, parent);
        }

        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null)
        {
            CheckModule();
            var prefab = await resLoadModule.LoadAsync<GameObject>(key);
            if (prefab == null) return null;
            return Object.Instantiate(prefab, parent);
        }
        
        #endregion

        private void CheckModule()
        {
            if (resLoadModule == null)
            {
                throw new InvalidOperationException("[ResSystem] ResLoadModule is not initialized. Call Init() first.");
            }
        }
    }
}
