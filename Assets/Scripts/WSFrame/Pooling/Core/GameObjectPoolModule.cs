using System.Collections.Generic;
using UnityEngine;
using WS_Modules.LogModule;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using WS_Modules.ResLoadModule;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 管理基于 Prefab 的 GameObject 对象池，支持同步与异步获取、回收、预热和容量限制。
    /// 资源加载由 IResLoad 抽象提供，池数据与加载实现彼此独立。
    /// </summary>
    public class GameObjectPoolModule
    {
        // 全部池抽屉共享的根节点。
        private Transform poolRootTransform;
        // 按稳定 Key 保存各 GameObject 池的数据。
        private Dictionary<string, GameObjectPoolData> PoolDic = new();
        // 通过资源加载接口取得 Key 对应的 Prefab，避免绑定具体资源系统。
        private IResLoad<string> gameObjectResLoader;

        /// <summary>
        /// 创建 GameObject 对象池模块，并配置池根节点与 Prefab 资源加载器。
        /// </summary>
        /// <param name="poolRootTransform">全部池抽屉的根节点；为空时自动创建。</param>
        /// <param name="gameObjectResLoader">按字符串 Key 加载 Prefab 的资源加载器。</param>
        public GameObjectPoolModule(Transform poolRootTransform, IResLoad<string> gameObjectResLoader)
        {
            this.poolRootTransform = poolRootTransform ?? new GameObject("ObjectPoolRoot").transform;
            this.gameObjectResLoader = gameObjectResLoader;
        }

        /// <summary>
        /// 使用 Prefab 根节点 IGameObjectPoolable.Key 预热 GameObject 池；身份缺失或 Key 非法时拒绝预热。
        /// </summary>
        public void Prewarm(GameObject prefab, int initCount, int maxCapacity, bool usePrefabAsFirst = false)
        {
            if (prefab == null)
            {
                WSLog.LogWarning("Prewarm: prefab is null.");
                return;
            }

            string key = ResolvePoolKey(prefab);
            if (!CheckPrewarmValid(key, initCount, maxCapacity, false)) return;

            var poolData = GetOrCreatePrewarmPool(key, maxCapacity);
            int needed = initCount - poolData.Count;
            if (needed <= 0) return;

            PrewarmObjects(poolData, key, prefab, needed, usePrefabAsFirst);
        }

        /// <summary>
        /// 按资源 Key 同步加载 Prefab，并预先创建指定数量的可用对象。
        /// </summary>
        /// <param name="key">Prefab 资源 Key，同时也是对象池的稳定 Key。</param>
        /// <param name="initCount">预热后池内至少应有的可用对象数量。</param>
        /// <param name="maxCapacity">池允许保留的最大对象数量。</param>
        public void Prewarm(string key, int initCount, int maxCapacity)
        {
            if (!CheckPrewarmValid(key, initCount, maxCapacity)) return;

            var poolData = GetOrCreatePrewarmPool(key, maxCapacity);
            int needed = initCount - poolData.Count;
            if (needed <= 0) return;

            var prefab = gameObjectResLoader.Load<GameObject>(key);
            if (prefab == null)
            {
                WSLog.LogWarning($"Prewarm: no prefab found for key '{key}'.");
                return;
            }
            if (!TryGetPoolable(prefab, key, out _)) return;

            PrewarmObjects(poolData, key, prefab, needed, false);
        }

        /// <summary>
        /// 异步加载资源并预热对象池；完成后通过回调报告本次预热是否成功。
        /// </summary>
        /// <param name="key">Prefab 资源 Key，同时也是对象池的稳定 Key。</param>
        /// <param name="initCount">预热后池内至少应有的可用对象数量。</param>
        /// <param name="maxCapacity">池允许保留的最大对象数量。</param>
        /// <param name="onComplete">预热结束回调，参数表示本次操作是否成功。</param>
        public async UniTask PrewarmAsync(string key, int initCount, int maxCapacity,
            UnityAction<bool> onComplete = null)
        {
            if (!CheckPrewarmValid(key, initCount, maxCapacity))
            {
                onComplete?.Invoke(false);
                return;
            }

            var data = GetOrCreatePrewarmPool(key, maxCapacity);
            int needed = initCount - data.Count;
            if (needed <= 0)
            {
                onComplete?.Invoke(true);
                return;
            }

            var prefab = await gameObjectResLoader.LoadAsync<GameObject>(key);
            if (prefab == null)
            {
                WSLog.LogWarning($"PrewarmAsync: no prefab found for key '{key}'.");
                onComplete?.Invoke(false);
                return;
            }
            if (!TryGetPoolable(prefab, key, out _))
            {
                onComplete?.Invoke(false);
                return;
            }

            PrewarmObjects(data, key, prefab, needed, false);

            onComplete?.Invoke(true);
        }


        /// <summary>
        /// 使用类型名作为资源 Key 同步获取对象；资源 Key 必须与类型名一致。
        /// </summary>
        /// <typeparam name="T">提供资源 Key 的池化对象类型。</typeparam>
        /// <param name="parent">对象生成后的父节点。</param>
        /// <returns>取得或新建的对象；校验或加载失败时返回 null。</returns>
        public GameObject Get<T>(Transform parent = null) where T : IGameObjectPoolable
        {
            return Get(typeof(T).Name, parent);
        }

        /// <summary>
        /// 按稳定 Key 同步获取对象；池为空时加载并实例化对应 Prefab。
        /// </summary>
        /// <param name="key">Prefab 资源 Key，同时也是对象池的稳定 Key。</param>
        /// <param name="parent">对象生成后的父节点。</param>
        /// <returns>取得或新建的对象；校验或加载失败时返回 null。</returns>
        public GameObject Get(string key, Transform parent = null)
        {
            if (!CheckKeyAndResLoadValid(key)) return null;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                // 首次直接获取会创建无限容量池；容量限制必须在首次获取前通过预热确定。
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable)) return null;
                poolable.Spawn();
                return go;
            }

            var prefab = gameObjectResLoader.Load<GameObject>(key);
            if (prefab == null)
            {
                WSLog.LogWarning($"Get: no prefab found for key '{key}' and pool is empty.");
                return null;
            }
            if (!TryGetPoolable(prefab, key, out _)) return null;

            var inst = GameObject.Instantiate(prefab, parent, false);
            if (!TryGetPoolable(inst, key, out IGameObjectPoolable instancePoolable))
            {
                GameObject.Destroy(inst);
                return null;
            }
            instancePoolable.Spawn();
            inst.name = prefab.name;
            return inst;
        }

        /// <summary>
        /// 使用 Prefab 根节点 IGameObjectPoolable.Key 获取 GameObject；池为空时直接实例化该 Prefab。
        /// </summary>
        public GameObject Get(GameObject prefab, Transform parent = null)
        {
            if (prefab == null)
            {
                WSLog.LogWarning("Get: prefab is null.");
                return null;
            }

            string key = ResolvePoolKey(prefab);
            if (!CheckKeyValid(key)) return null;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                // 直接传入 Prefab 时也建立可供后续回收复用的无限容量池。
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable))
                {
                    WSLog.LogWarning($"Get: pooled object for key '{key}' is missing IGameObjectPoolable component.");
                    return null;
                }
                poolable.Spawn();
                return go;
            }

            var inst = GameObject.Instantiate(prefab, parent, false);
            if (!TryGetPoolable(inst, key, out IGameObjectPoolable instancePoolable))
            {
                GameObject.Destroy(inst);
                return null;
            }
            instancePoolable.Spawn();
            inst.name = prefab.name;
            return inst;
        }

        /// <summary>按稳定 Key 批量获取并统一 Spawn 指定数量的对象。</summary>
        public List<GameObject> GetSome(string key, int count, Transform parent = null)
        {
            if (!CheckKeyAndResLoadValid(key)) return null;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGetSome(count, out var gos, parent))
            {
                if (!TryGetPoolables(gos, key, out List<IGameObjectPoolable> poolables)) return null;
                SpawnAll(poolables);
                return gos;
            }

            var prefab = gameObjectResLoader.Load<GameObject>(key);
            if (prefab == null)
            {
                WSLog.LogWarning($"Get(count): no prefab found for key '{key}' and pool is empty.");
                return null;
            }
            if (!TryGetPoolable(prefab, key, out _)) return null;

            var instList = new List<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                var inst = GameObject.Instantiate(prefab, parent, false);
                if (!TryGetPoolable(inst, key, out IGameObjectPoolable poolable))
                {
                    GameObject.Destroy(inst);
                    return null;
                }
                poolable.Spawn();
                inst.name = prefab.name;
                instList.Add(inst);
            }

            return instList;
        }
        
        /// <summary>
        /// 使用 Prefab 根节点 IGameObjectPoolable.Key 批量获取 GameObject；池数量不足时直接实例化该 Prefab。
        /// </summary>
        public List<GameObject> GetSome(GameObject prefab, int count, Transform parent = null)
        {
            if (prefab == null)
            {
                WSLog.LogWarning("GetSome: prefab is null.");
                return null;
            }

            if (count <= 0)
            {
                WSLog.LogWarning("GetSome: count must be greater than 0.");
                return new List<GameObject>();
            }

            string key = ResolvePoolKey(prefab);
            if (!CheckKeyValid(key)) return null;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                // 直接传入 Prefab 批量获取时也建立可供后续回收复用的无限容量池。
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGetSome(count, out var gos, parent))
            {
                if (!TryGetPoolables(gos, key, out List<IGameObjectPoolable> poolables)) return null;
                SpawnAll(poolables);
                return gos;
            }

            var instList = new List<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                var inst = GameObject.Instantiate(prefab, parent, false);
                if (!TryGetPoolable(inst, key, out IGameObjectPoolable poolable))
                {
                    GameObject.Destroy(inst);
                    return null;
                }
                poolable.Spawn();
                inst.name = prefab.name;
                instList.Add(inst);
            }

            return instList;
        }
        /// <summary>
        /// 使用类型名作为资源 Key 异步获取对象。
        /// </summary>
        /// <typeparam name="T">提供资源 Key 的池化对象类型。</typeparam>
        /// <param name="parent">对象生成后的父节点。</param>
        /// <returns>异步返回取得或新建的对象；校验或加载失败时返回 null。</returns>
        public async UniTask<GameObject> GetAsync<T>(Transform parent = null)
            where T : IGameObjectPoolable
        {
            return await GetAsync(typeof(T).Name, parent);
        }

        /// <summary>异步加载稳定 Key 对应资源，并在身份校验后 Spawn 对象。</summary>
        public async UniTask<GameObject> GetAsync(string key, Transform parent = null)
        {
            if (!CheckKeyAndResLoadValid(key))
            {
                return null;
            }

            if (!PoolDic.TryGetValue(key, out var data))
            {
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable)) return null;
                poolable.Spawn();
                return go;
            }

            // 等待资源加载完成后再实例化，保持异步加载过程不阻塞调用方。
            var prefab = await gameObjectResLoader.LoadAsync<GameObject>(key);

            if (prefab is null)
            {
                WSLog.LogWarning($"GetAsync: no prefab found for key '{key}' and pool is empty.");
                return null;
            }
            if (!TryGetPoolable(prefab, key, out _)) return null;

            var inst = GameObject.Instantiate(prefab, parent, false);
            if (!TryGetPoolable(inst, key, out IGameObjectPoolable instancePoolable))
            {
                GameObject.Destroy(inst);
                return null;
            }
            instancePoolable.Spawn();
            inst.name = prefab.name;
            return inst;
        }

        /// <summary>按类型名异步获取对象，并在资源与身份准备完成后回调。</summary>
        public void GetAsync<T>(Transform parent, UnityAction<GameObject> onComplete)
            where T : IGameObjectPoolable
        {
            GetAsync(typeof(T).Name, parent, onComplete);
        }

        /// <summary>按稳定 Key 异步获取对象，并在身份准备完成后回调。</summary>
        public void GetAsync(string key, Transform parent, UnityAction<GameObject> onComplete)
        {
            if (!CheckKeyAndResLoadValid(key))
            {
                onComplete?.Invoke(null);
                return;
            }

            if (!PoolDic.TryGetValue(key, out var data))
            {
                WSLog.Log("创建新的对象池 " + key + "，默认容量无限；如需限制容量，请先调用 Prewarm，并建议预热以减少首次加载和实例化开销。");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable))
                {
                    onComplete?.Invoke(null);
                    return;
                }
                poolable.Spawn();
                onComplete?.Invoke(go);
                return;
            }

            // 资源加载完成后在回调中完成身份校验、实例化与 Spawn，再通知调用方。
            gameObjectResLoader.LoadAsync<GameObject>(key, prefab =>
            {
                if (prefab == null)
                {
                    WSLog.LogWarning($"GetAsync(callback): no prefab found for key '{key}' and pool is empty.");
                    onComplete?.Invoke(null);
                    return;
                }
                if (!TryGetPoolable(prefab, key, out _))
                {
                    onComplete?.Invoke(null);
                    return;
                }

                var inst = GameObject.Instantiate(prefab, parent, false);
                if (!TryGetPoolable(inst, key, out IGameObjectPoolable poolable))
                {
                    GameObject.Destroy(inst);
                    onComplete?.Invoke(null);
                    return;
                }
                poolable.Spawn();
                inst.name = prefab.name;
                onComplete?.Invoke(inst);
            });
        }

        /// <summary>
        /// 校验对象身份后回收到指定 Key 的已有池；池不存在时记录错误并拒绝回收。
        /// </summary>
        /// <param name="key">目标对象池的稳定 Key。</param>
        /// <param name="go">需要回收的池化对象。</param>
        public void Recycle(string key, GameObject go)
        {
            if (string.IsNullOrEmpty(key) || go == null) return;

            if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable)) return;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                WSLog.LogError($"Recycle: Pool '{key}' does not exist for GameObject '{go.name}'.");
                return;
            }

            poolable.Despawn();
            data.PushObj(go);
        }

        /// <summary>
        /// 使用实例根节点 IGameObjectPoolable.Key 回收 GameObject，不使用实例名称推断池身份。
        /// </summary>
        public void Recycle(GameObject go)
        {
            if (go == null) return;

            string key = ResolvePoolKey(go);
            Recycle(key, go);
        }

        /// <summary>
        /// 使用第一项的 IGameObjectPoolable.Key 定位池，并要求列表内全部对象拥有相同身份。
        /// </summary>
        public void RecycleSome(List<GameObject> gos)
        {
            if (gos is not { Count: > 0 }) return;

            string key = ResolvePoolKey(gos[0]);
            if (!CheckKeyValid(key)) return;
            if (!TryGetPoolables(gos, key, out List<IGameObjectPoolable> poolables)) return;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                WSLog.LogError($"RecycleSome: Pool '{key}' does not exist.");
                return;
            }
            
            DespawnAll(poolables);
            data.PushObjs(gos);
        }

        /// <summary>销毁指定 Key 池内当前已 Despawn 的对象并移除池数据。</summary>
        public void ClearPool(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (PoolDic.TryGetValue(key, out var data))
            {
                data.ClearPool();
                PoolDic.Remove(key);
            }
        }

        /// <summary>销毁全部池内当前已 Despawn 的对象并清空池数据。</summary>
        public void ClearAll()
        {
            foreach (var p in PoolDic.Values)
            {
                p.ClearPool();
            }

            PoolDic.Clear();
        }

        /// <summary>保留 Editor 选择清理扩展点，运行时无需处理。</summary>
        private static void ClearEditorSelectionIfNeeded(GameObject root)
        {
        }
        /// <summary>取得预热目标池，不存在时创建，存在时只允许扩大容量。</summary>
        private GameObjectPoolData GetOrCreatePrewarmPool(string key, int maxCapacity)
        {
            if (!PoolDic.TryGetValue(key, out var poolData))
            {
                poolData = new GameObjectPoolData(poolRootTransform, maxCapacity, $"Pool_{key}");
                PoolDic[key] = poolData;
                return poolData;
            }

            poolData.EnsureMaxCapacity(maxCapacity);
            return poolData;
        }

        /// <summary>实例化指定数量对象，并通过 Despawn 入口放入初始池状态。</summary>
        private void PrewarmObjects(
            GameObjectPoolData poolData,
            string key,
            GameObject prefab,
            int count,
            bool usePrefabAsFirst)
        {
            if (poolData == null || prefab == null || count <= 0) return;

            int startIndex = 0;
            if (usePrefabAsFirst)
            {
                if (!TryGetPoolable(prefab, key, out IGameObjectPoolable poolable)) return;
                poolable.Despawn();
                poolData.PushObj(prefab);
                startIndex = 1;
            }

            for (int i = startIndex; i < count; i++)
            {
                var inst = GameObject.Instantiate(prefab, poolRootTransform, false);
                inst.name = prefab.name;
                if (!TryGetPoolable(inst, key, out IGameObjectPoolable poolable))
                {
                    GameObject.Destroy(inst);
                    return;
                }
                poolable.Despawn();
                poolData.PushObj(inst);
            }
        }

        #region 参数与身份校验
        /// <summary>校验预热 Key、资源加载器和容量参数。</summary>
        private bool CheckPrewarmValid(string key, int initCount, int maxCapacity, bool requireResLoader = true)
        {
            if (!CheckKeyValid(key)) return false;
            if (requireResLoader && !CheckResLoadValid()) return false;

            if (initCount <= 0 || (initCount > maxCapacity && maxCapacity != -1))
            {
                WSLog.LogError(
                    $"InitCount is inValid: {initCount} or Prewarm: initCount {initCount} exceeds maxCapacity {maxCapacity} for key '{key}'.");
                return false;
            }

            return true;
        }

        /// <summary>校验 Key 与资源加载器均可用。</summary>
        private bool CheckKeyAndResLoadValid(string key)
        {
            return CheckKeyValid(key) && CheckResLoadValid();
        }

        /// <summary>校验对象池 Key 不为空。</summary>
        private bool CheckKeyValid(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                return true;
            }

            WSLog.LogError($"Prewarm: invalid parameters for key '{key}'.");
            return false;
        }

        /// <summary>校验当前对象池拥有资源加载器。</summary>
        private bool CheckResLoadValid()
        {
            if (gameObjectResLoader != null)
            {
                return true;
            }

            WSLog.LogError("Prewarm: gameObjectResLoader is null.");
            return false;
        }
        #endregion

        #region 杈呭姪鍑芥暟
        /// <summary>从池化根对象的强制身份组件读取唯一 Key。</summary>
        /// <param name="go">待读取身份的根对象。</param>
        /// <returns>有效 Key；身份缺失时返回 null。</returns>
        private string ResolvePoolKey(GameObject go)
        {
            if (go == null) return null;
            return TryGetPoolable(go, expectedKey: null, out IGameObjectPoolable poolable)
                ? poolable.Key
                : null;
        }

        /// <summary>校验强制池身份，并确认对象 Key 与当前池一致。</summary>
        /// <param name="go">待校验的池化根对象。</param>
        /// <param name="expectedKey">当前对象池 Key；为空时只验证身份与 Key 有效性。</param>
        /// <param name="poolable">成功时返回池化身份接口。</param>
        /// <returns>身份存在、Key 有效且与当前池一致时返回 true。</returns>
        private bool TryGetPoolable(
            GameObject go,
            string expectedKey,
            out IGameObjectPoolable poolable)
        {
            poolable = null;
            if (go == null) return false;
            poolable = go.GetComponent<IGameObjectPoolable>();
            if (poolable == null)
            {
                WSLog.LogError($"Pool GameObject '{go.name}' must implement IGameObjectPoolable on its root.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(poolable.Key))
            {
                WSLog.LogError($"Pool GameObject '{go.name}' has an empty IGameObjectPoolable.Key.");
                return false;
            }

            if (!string.IsNullOrEmpty(expectedKey) && poolable.Key != expectedKey)
            {
                WSLog.LogError(
                    $"Pool GameObject '{go.name}' Key '{poolable.Key}' does not match requested Key '{expectedKey}'.");
                return false;
            }

            return true;
        }

        /// <summary>批量校验全部对象身份，避免生命周期只提交一部分。</summary>
        private bool TryGetPoolables(
            List<GameObject> gameObjects,
            string key,
            out List<IGameObjectPoolable> poolables)
        {
            poolables = new List<IGameObjectPoolable>(gameObjects.Count);
            for (int i = 0; i < gameObjects.Count; i++)
            {
                if (!TryGetPoolable(gameObjects[i], key, out IGameObjectPoolable poolable))
                    return false;
                poolables.Add(poolable);
            }
            return true;
        }

        /// <summary>在全部对象通过校验后统一发送 Spawn。</summary>
        private static void SpawnAll(List<IGameObjectPoolable> poolables)
        {
            for (int i = 0; i < poolables.Count; i++) poolables[i].Spawn();
        }

        /// <summary>在全部对象通过校验后统一发送 Despawn。</summary>
        private static void DespawnAll(List<IGameObjectPoolable> poolables)
        {
            for (int i = 0; i < poolables.Count; i++) poolables[i].Despawn();
        }
        #endregion
    }
}


