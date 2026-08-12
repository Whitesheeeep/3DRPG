using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 对外提供 GameObject 池和普通对象池的统一访问门面。
    /// </summary>
    public class PoolManager : SingletonBase<PoolManager>
    {
        #region 字段

        private GameObjectPoolModule gameObjectPoolModule;
        private ClassPoolModule classPoolModule;
        private readonly GlobalPoolPrewarmProcessor globalPrewarmProcessor = GlobalPoolPrewarmProcessor.Instance;

        #endregion

        #region 生命周期

        /// <summary>
        /// 创建对象池管理器单例实例。
        /// </summary>
        private PoolManager()
        {
        }

        /// <summary>
        /// 初始化对象池模块；重复调用不会重建既有池状态。
        /// </summary>
        /// <param name="poolingSetting">对象池设置。</param>
        /// <param name="resLoader">可选的资源加载器。</param>
        /// <param name="rootParent">对象池根节点的可选父节点。</param>
        public void Initialize(PoolingSetting poolingSetting, IResLoad<string> resLoader = null, Transform rootParent = null)
        {
            Init(resLoader ?? GetResLoader(poolingSetting), poolingSetting, rootParent);
        }

        /// <summary>
        /// 创建具体池模块并应用全局预热配置。
        /// </summary>
        /// <param name="gameObjectResLoader">GameObject 资源加载器。</param>
        /// <param name="poolingSetting">对象池设置。</param>
        /// <param name="rootParent">对象池根节点的可选父节点。</param>
        private void Init(IResLoad<string> gameObjectResLoader, PoolingSetting poolingSetting, Transform rootParent)
        {
            if (gameObjectPoolModule != null) return;

            var poolRoot = new GameObject("PoolSystemRoot").transform;
            if (rootParent != null) poolRoot.SetParent(rootParent);

            // GameObject 池与普通对象池共享同一个门面，但各自维护独立存储。
            gameObjectPoolModule = new GameObjectPoolModule(poolRoot, gameObjectResLoader);
            classPoolModule = new ClassPoolModule();
            ApplyGlobalPrewarm(poolingSetting);
        }

        #endregion

        #region 预热

        /// <summary>按资源 Key 预热 GameObject 池。</summary>
        public void Prewarm(string key, int initCount, int maxCapacity) => gameObjectPoolModule.Prewarm(key, initCount, maxCapacity);

        /// <summary>使用根节点 Identity 的 Key 预热 GameObject 池。</summary>
        public void Prewarm(GameObject prefab, int initCount, int maxCapacity) => gameObjectPoolModule.Prewarm(prefab, initCount, maxCapacity);

        /// <summary>预热指定类型的普通对象池。</summary>
        public void PrewarmClass<T>(int count, int maxCapacity) where T : class, new() => classPoolModule.Prewarm<T>(count, maxCapacity);

        /// <summary>异步加载资源并预热 GameObject 池。</summary>
        public async UniTask PrewarmAsync(string key, int initCount, int maxCapacity, UnityAction<bool> onComplete = null)
            => await gameObjectPoolModule.PrewarmAsync(key, initCount, maxCapacity, onComplete);

        #endregion

        #region 获取

        /// <summary>按池化身份类型名获取 GameObject。</summary>
        public GameObject Get<T>(Transform parent = null) where T : IGameObjectPoolable => gameObjectPoolModule.Get<T>(parent);

        /// <summary>按稳定 Key 获取 GameObject。</summary>
        public GameObject Get(string key, Transform parent = null) => gameObjectPoolModule.Get(key, parent);

        /// <summary>按 Prefab 根节点 Identity 的 Key 获取 GameObject。</summary>
        public GameObject Get(GameObject prefab, Transform parent = null) => gameObjectPoolModule.Get(prefab, parent);

        /// <summary>按稳定 Key 批量获取 GameObject。</summary>
        public List<GameObject> GetSome(string key, int count, Transform parent = null) => gameObjectPoolModule.GetSome(key, count, parent);

        /// <summary>按 Prefab 根节点 Identity 的 Key 批量获取 GameObject。</summary>
        public List<GameObject> GetSome(GameObject prefab, int count, Transform parent = null) => gameObjectPoolModule.GetSome(prefab, count, parent);

        /// <summary>按池化身份类型名异步获取 GameObject。</summary>
        public async UniTask<GameObject> GetAsync<T>(Transform parent = null) where T : IGameObjectPoolable => await gameObjectPoolModule.GetAsync<T>(parent);

        /// <summary>按稳定 Key 异步获取 GameObject。</summary>
        public async UniTask<GameObject> GetAsync(string key, Transform parent = null) => await gameObjectPoolModule.GetAsync(key, parent);

        /// <summary>按池化身份类型名异步获取 GameObject，并通过回调返回结果。</summary>
        public void GetAsync<T>(Transform parent, UnityAction<GameObject> onComplete) where T : IGameObjectPoolable => gameObjectPoolModule.GetAsync<T>(parent, onComplete);

        /// <summary>按稳定 Key 异步获取 GameObject，并通过回调返回结果。</summary>
        public void GetAsync(string key, Transform parent, UnityAction<GameObject> onComplete) => gameObjectPoolModule.GetAsync(key, parent, onComplete);

        /// <summary>获取指定类型的普通对象。</summary>
        public T GetClass<T>() where T : class, new() => classPoolModule.Get<T>();

        #endregion

        #region 回收

        /// <summary>校验对象身份与指定 Key 一致后回收 GameObject。</summary>
        public void Recycle(string key, GameObject go) => gameObjectPoolModule.Recycle(key, go);

        /// <summary>使用实例根节点 Identity 的 Key 回收 GameObject。</summary>
        public void Recycle(GameObject go) => gameObjectPoolModule.Recycle(go);

        /// <summary>按共同 Identity Key 批量回收 GameObject。</summary>
        public void RecycleSome(List<GameObject> gameObjects) => gameObjectPoolModule.RecycleSome(gameObjects);

        /// <summary>回收普通对象。</summary>
        public void RecycleClass<T>(T instance) where T : class, new() => classPoolModule.Recycle(instance);

        #endregion

        #region 清理

        /// <summary>清理指定 Key 对应的 GameObject 池。</summary>
        public void ClearPool(string key) => gameObjectPoolModule.ClearPool(key);

        /// <summary>清理指定类型的普通对象池。</summary>
        public void ClearClassPool<T>() => classPoolModule.Clear<T>();

        /// <summary>清理全部 GameObject 池和普通对象池。</summary>
        public void ClearAll()
        {
            gameObjectPoolModule.ClearAll();
            classPoolModule.ClearAll();
        }

        #endregion

        #region 内部辅助

        /// <summary>
        /// 根据设置创建对应资源加载器。
        /// </summary>
        private IResLoad<string> GetResLoader(PoolingSetting poolingSetting)
        {
            switch (poolingSetting?.ResLoadType ?? E_ResLoadType.Resources)
            {
                case E_ResLoadType.Resources:
                    return new ResourcesLoadMgrModule();
                case E_ResLoadType.Addressable:
                    return new AddressablesLoadMgrModule();
                default:
                    return new ResourcesLoadMgrModule();
            }
        }

        /// <summary>
        /// 将全局预热配置应用到两个具体池模块。
        /// </summary>
        private void ApplyGlobalPrewarm(PoolingSetting poolingSetting)
        {
            globalPrewarmProcessor.SetConfig(poolingSetting?.GlobalPrewarmConfig);
            globalPrewarmProcessor.Apply(gameObjectPoolModule, classPoolModule);
        }

        #endregion
    }
}
