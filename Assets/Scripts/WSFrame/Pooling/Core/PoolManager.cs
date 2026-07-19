using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;

namespace WS_Modules.Pooling
{
    public class PoolManager : SingletonBase<PoolManager>
    {
        private PoolManager()
        {
        }
        
        private GameObjectPoolModule _gameObjectPoolModule;
        private ClassPoolModule _classPoolModule;
        private readonly GlobalPoolPrewarmProcessor _globalPrewarmProcessor = GlobalPoolPrewarmProcessor.Instance;

        public void Initialize(PoolingSetting poolingSetting, IResLoad<string> resLoader = null, Transform rootParent = null)
        {
            Init(resLoader ?? GetResLoader(poolingSetting), poolingSetting, rootParent);
        }

        private void Init(IResLoad<string> gameObjectResLoader, PoolingSetting poolingSetting, Transform rootParent)
        {
            if (_gameObjectPoolModule != null) return;
            
            var poolRoot = new GameObject("PoolSystemRoot").transform;
            if (rootParent != null)
            {
                poolRoot.SetParent(rootParent);
            }
            
            // 浣跨敤 ResourcesLoadMgr 浣滀负璧勬簮鍔犺浇鍣?
            _gameObjectPoolModule = new GameObjectPoolModule(poolRoot, gameObjectResLoader);
            _classPoolModule = new ClassPoolModule();

            // 搴旂敤鍏ㄥ眬棰勭儹閰嶇疆
            ApplyGlobalPrewarm(poolingSetting);
        }

        #region Prewarm
        public void Prewarm(string key, int initCount, int maxCapacity) => _gameObjectPoolModule.Prewarm(key, initCount, maxCapacity);
        /// <summary>
        /// 浣跨敤宸茬粡鎸佹湁鐨?prefab 棰勭儹 GameObject 姹犮€備紭鍏堜娇鐢?prefab 涓婄殑 PoolObjectIdentity.PoolKey锛岀己澶辨椂浣跨敤 prefab 鍚嶇О锛涜祫婧愯矾寰勬垨 Addressable 鍦烘櫙寤鸿浼樺厛浣跨敤 string key 閲嶈浇銆?
        /// </summary>
        public void Prewarm(GameObject prefab, int initCount, int maxCapacity) => _gameObjectPoolModule.Prewarm(prefab, initCount, maxCapacity);

        public void PrewarmClass<T>(int count, int maxCapacity) where T : class, new() => _classPoolModule.Prewarm<T>(count, maxCapacity);

        public async UniTask PrewarmAsync(string key, int initCount, int maxCapacity, UnityAction<bool> onComplete = null) 
            => await _gameObjectPoolModule.PrewarmAsync(key, initCount, maxCapacity, onComplete);
        #endregion

        #region Get
        public GameObject Get<T>(Transform parent = null) where T : IPoolable => _gameObjectPoolModule.Get<T>(parent);
        public GameObject Get(string key, Transform parent = null) => _gameObjectPoolModule.Get(key, parent);

        /// <summary>
        /// 浣跨敤宸茬粡鎸佹湁鐨?prefab 鑾峰彇 GameObject銆備紭鍏堜娇鐢?prefab 涓婄殑 PoolObjectIdentity.PoolKey锛岀己澶辨椂浣跨敤 prefab 鍚嶇О锛涙睜涓虹┖鏃剁洿鎺ュ疄渚嬪寲璇?prefab锛屼笉璧拌祫婧愬姞杞藉櫒銆?
        /// </summary>
        public GameObject Get(GameObject prefab, Transform parent = null) => _gameObjectPoolModule.Get(prefab, parent);

        public List<GameObject> GetSome(string key, int count, Transform parent = null) => _gameObjectPoolModule.GetSome(key, count, parent);

        /// <summary>
        /// 浣跨敤宸茬粡鎸佹湁鐨?prefab 鎵归噺鑾峰彇 GameObject銆備紭鍏堜娇鐢?prefab 涓婄殑 PoolObjectIdentity.PoolKey锛岀己澶辨椂浣跨敤 prefab 鍚嶇О锛涙睜鏁伴噺涓嶈冻鏃剁洿鎺ュ疄渚嬪寲璇?prefab 琛ヨ冻锛屼笉璧拌祫婧愬姞杞藉櫒銆?
        /// </summary>
        public List<GameObject> GetSome(GameObject prefab, int count, Transform parent = null) => _gameObjectPoolModule.GetSome(prefab, count, parent);

        public async UniTask<GameObject> GetAsync<T>(Transform parent = null) => await _gameObjectPoolModule.GetAsync<T>(parent);
        public async UniTask<GameObject> GetAsync(string key, Transform parent = null) => await _gameObjectPoolModule.GetAsync(key, parent);

        public void GetAsync<T>(Transform parent, UnityAction<GameObject> onComplete) => _gameObjectPoolModule.GetAsync<T>(parent, onComplete);
        public void GetAsync(string key, Transform parent, UnityAction<GameObject> onComplete) => _gameObjectPoolModule.GetAsync(key, parent, onComplete);
        
        /// <summary>
        /// 鑾峰彇鏅€氱被瀵硅薄
        /// </summary>
        public T GetClass<T>() where T : class, new() => _classPoolModule.Get<T>();
        #endregion

        #region Recycle
        public void Recycle(string key, GameObject go) => _gameObjectPoolModule.Recycle(key, go);
        /// <summary>
        /// 鍥炴敹宸茬粡鎸佹湁鐨?GameObject 瀹炰緥銆備紭鍏堜娇鐢ㄥ疄渚嬩笂鐨?PoolObjectIdentity.PoolKey锛岀己澶辨椂浣跨敤瀹炰緥鍚嶇О锛涜祫婧愯矾寰勬垨 Addressable 鍦烘櫙寤鸿纭繚瀹炰緥甯︽湁绋冲畾鐨?PoolObjectIdentity銆?
        /// </summary>
        public void Recycle(GameObject go) => _gameObjectPoolModule.Recycle(go);

        /// <summary>
        /// 鎵归噺鍥炴敹宸茬粡鎸佹湁鐨?GameObject 瀹炰緥銆備娇鐢ㄧ涓€椤圭殑 PoolObjectIdentity.PoolKey 鎴栧悕绉板畾浣嶆睜锛岃姹傚垪琛ㄥ唴瀵硅薄鏉ヨ嚜鍚屼竴涓睜銆?
        /// </summary>
        public void RecycleSome(List<GameObject> gos) => _gameObjectPoolModule.RecycleSome(gos);
        
        /// <summary>
        /// 鍥炴敹鏅€氱被瀵硅薄
        /// </summary>
        public void RecycleClass<T>(T obj) where T : class, new() => _classPoolModule.Recycle(obj);
        #endregion

        #region Clear
        public void ClearPool(string key) => _gameObjectPoolModule.ClearPool(key);
        public void ClearClassPool<T>() => _classPoolModule.Clear<T>();
        
        public void ClearAll()
        {
            _gameObjectPoolModule.ClearAll();
            _classPoolModule.ClearAll();
        }
        #endregion
        
        private IResLoad<string> GetResLoader(PoolingSetting poolingSetting)
        {
            IResLoad<string> resLoader;
            switch (poolingSetting?.ResLoadType ?? E_ResLoadType.Resources)
            {
                case E_ResLoadType.Resources:
                    resLoader = new ResourcesLoadMgrModule();
                    break;
                case E_ResLoadType.Addressable:
                    resLoader = new AddressablesLoadMgrModule();
                    break;
                default:
                    resLoader = new ResourcesLoadMgrModule();
                    break;
            }

            return resLoader;
        }

        private void ApplyGlobalPrewarm(PoolingSetting poolingSetting)
        {
            _globalPrewarmProcessor.SetConfig(poolingSetting?.GlobalPrewarmConfig);
            _globalPrewarmProcessor.Apply(_gameObjectPoolModule, _classPoolModule);
        }
    }
}




