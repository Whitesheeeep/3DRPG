using System.Collections.Generic;
using UnityEngine;
using WS_Modules.LogModule;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using WS_Modules.ResLoadModule;

namespace WS_Modules.Pooling
{
    /// <summary>
    /// 娓告垙瀵硅薄姹犳ā鍧楋細鎻愪緵鍩轰簬棰勫埗浣撶殑瀵硅薄姹犲姛鑳斤紝鏀寔鍚屾鍜屽紓姝ヨ幏鍙栦笌鍥炴敹锛岄鐑姛鑳斤紝浠ュ強姹犲閲忕鐞嗐€?
    /// - 涓嶅叧蹇冩娊灞夌殑瀹炵幇缁嗚妭锛屼笓娉ㄤ簬瀵硅薄姹犵殑鏍稿績鍔熻兘鍜屾帴鍙ｈ璁★紝鏂逛究鍚庣画鏇挎崲鎶藉眽瀹炵幇鎴栨墿灞曞叾浠栫被鍨嬬殑姹犳暟鎹粨鏋勩€?
    /// - 涓嶅叧蹇冭祫婧愬姞杞界殑瀹炵幇缁嗚妭锛岄€氳繃 IResLoad 鎺ュ彛鎶借薄璧勬簮鍔犺浇閫昏緫锛屾柟渚垮悗缁浛鎹㈣祫婧愬姞杞界郴缁熸垨鏀寔涓嶅悓绫诲瀷鐨勮祫婧愬姞杞介渶姹傘€?
    /// - 闇€瑕佽嚜琛屼紶鍏ュ璞＄殑 key, initCount 鍜?maxCapacity 鏉ラ鐑睜瀛愶紝棰勭儹鏃朵細鑷姩鍒涘缓涓€涓柊鐨勬睜瀛愬苟鍔犺浇鎸囧畾鏁伴噺鐨勫璞″疄渚嬪埌姹犱腑锛屾柟渚垮悗缁幏鍙栨椂鐩存帴澶嶇敤銆?
    /// </summary>
    public class GameObjectPoolModule
    {
        // 鏁翠釜瀵硅薄姹犵殑鏍瑰璞?
        private Transform poolRootTransform;
        // 瀛樺偍鎵€鏈夋睜瀛愮殑鏁版嵁缁撴瀯
        private Dictionary<string, GameObjectPoolData> PoolDic = new();
        // 鐢ㄤ簬鍒涘缓瀵硅薄鐨勫伐鍘傦紝閬垮厤鐩存帴渚濊禆璧勬簮鍔犺浇绯荤粺锛屾柟渚垮悗缁浛鎹㈠拰鎵╁睍
        private IResLoad<string> gameObjectResLoader;

        /// <summary>
        /// 鏋勯€犲嚱鏁帮紝鎺ュ彈涓€涓?Transform 浣滀负瀵硅薄姹犵殑鏍硅妭鐐癸紝浠ュ強涓€涓?IResLoad 璧勬簮鍔犺浇鍣ㄦ潵鍔犺浇棰勫埗浣撹祫婧愶紝纭繚瀵硅薄姹犳ā鍧楃殑鐙珛鎬у拰鍙浛鎹㈡€с€?
        /// </summary>
        /// <param name="poolRootTransform">璇ユ睜瀛愭ā鍧楃殑鏍硅妭鐐?/param>
        /// <param name="gameObjectResLoader"></param>
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
        /// 瀵硅薄姹犻鐑細鎻愬墠鍒涘缓涓€瀹氭暟閲忕殑瀵硅薄瀹炰緥骞舵斁鍏ユ睜涓紝鍑忓皯鍚庣画鑾峰彇鏃剁殑鎬ц兘寮€閿€锛岄€傜敤浜庨渶瑕佸湪娓告垙寮€濮嬫椂灏卞噯澶囧ソ涓€瀹氭暟閲忓璞＄殑鎯呭喌锛岄伩鍏嶅湪娓告垙杩囩▼涓绻佸姞杞借祫婧愬拰瀹炰緥鍖栧璞″鑷寸殑鎬ц兘闂銆?
        /// 棰勭儹鏃朵細鑷姩鍒涘缓涓€涓柊鐨勬睜瀛愬苟鍔犺浇鎸囧畾鏁伴噺鐨勫璞″疄渚嬪埌姹犱腑锛屾柟渚垮悗缁幏鍙栨椂鐩存帴澶嶇敤銆傝姹傞鐑殑瀵硅薄鍚嶇О key銆佸垵濮嬫暟閲?initCount 鍜屾渶澶у閲?maxCapacity 鍙傛暟蹇呴』鏈夋晥锛屽惁鍒欓鐑細澶辫触骞惰緭鍑鸿鍛婃棩蹇椼€?
        /// 棰勭儹瀹屾垚鍚庯紝姹犲瓙涓細鏈?initCount 涓璞″疄渚嬪彲渚涜幏鍙栵紝姹犲瓙鐨勬渶澶у閲忎负 maxCapacity锛岃秴杩囧閲忛檺鍒剁殑瀵硅薄鍦ㄥ洖鏀舵椂浼氳涓㈠純鑰屼笉鏄斁鍏ユ睜涓€?
        /// </summary> <param name="key">瀵硅薄鍚嶇О key</param>
        /// <param name="initCount">鍒濆鏁伴噺</param>
        /// <param name="maxCapacity">鏈€澶у閲?/param>
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
        /// 瀵硅薄姹犻鐑殑寮傛鐗堟湰锛岄€傜敤浜庨渶瑕佷粠杩滅▼鎴栧紓姝ヨ祫婧愮郴缁熷姞杞介鍒朵綋鐨勬儏鍐碉紝閬垮厤鍦ㄤ富绾跨▼绛夊緟璧勬簮鍔犺浇瀹屾垚銆?
        /// 鍙互鐩存帴浣跨敤 async/await 鏉ヨ皟鐢ㄨ繖涓柟娉曪紝鎴栬€呬娇鐢ㄥ洖璋冩帴鍙ｆ潵鑾峰彇棰勭儹瀹屾垚鐨勯€氱煡銆?
        /// 涔熷彲浠ョ洿鎺ョ敤 Forget() 瀹炵幇涓€鍙戝嵆寮冪殑棰勭儹璋冪敤锛岄€傜敤浜庝笉鍏冲績棰勭儹瀹屾垚鏃舵満鐨勬儏鍐点€?
        /// </summary>
        /// <param name="key">瀵硅薄鍚嶇О key</param>
        /// <param name="initCount">鍒濆鏁伴噺</param>
        /// <param name="maxCapacity">鏈€澶у閲?/param>
        /// <param name="onComplete">寮傛鐗堟湰棰勭儹瀹屾垚鍚庣殑鍥炶皟锛岃繑鍥炰竴涓猙ool琛ㄧず棰勭儹鏄惁鎴愬姛</param>
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
        /// 璇ユ柟娉曟彁渚涗簡涓€涓畝鍖栫殑鎺ュ彛锛屽厑璁歌皟鐢ㄦ柟鐩存帴閫氳繃绫诲瀷鍙傛暟鏉ヨ幏鍙栧璞″疄渚嬶紝鍐呴儴浼氫娇鐢ㄧ被鍨嬪悕绉颁綔涓?key 鏉ョ鐞嗘睜瀛愶紝閫傜敤浜庢瘡涓被鍨嬪搴斾竴涓鍒朵綋鐨勫父瑙佹儏鍐点€?
        /// 瑕佹眰锛?c>蹇呴』鏄祫婧愪笌瀵瑰簲鐨勭被涓€鑷村悕绉?/c>
        /// </summary>
        public GameObject Get<T>(Transform parent = null) where T : IGameObjectPoolable
        {
            return Get(typeof(T).Name, parent);
        }

        /// <summary>
        /// 鍚屾鍔犺浇瀵硅薄锛屽鏋滄睜涓病鏈夊彲鐢ㄥ璞★紝鍒欏皾璇曚粠椤圭洰璧勬簮绯荤粺鍔犺浇棰勫埗浣撳苟瀹炰緥鍖栵紝杩斿洖瀹炰緥瀵硅薄銆?
        /// </summary>
        /// <param name="key"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        public GameObject Get(string key, Transform parent = null)
        {
            if (!CheckKeyAndResLoadValid(key)) return null;

            if (!PoolDic.TryGetValue(key, out var data))
            {
                // 鑷姩鍒涘缓涓€涓棤闄愬閲忕殑姹犮€?
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
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
                // 鐩翠紶 prefab 鑾峰彇鏃惰嚜鍔ㄥ垱寤烘棤闄愬閲忔睜锛屽悗缁洖鏀跺彲浠ュ鐢ㄣ€?
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable)) return null;
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
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
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
                // 鐩翠紶 prefab 鎵归噺鑾峰彇鏃惰嚜鍔ㄥ垱寤烘棤闄愬閲忔睜锛屽悗缁洖鏀跺彲浠ュ鐢ㄣ€?
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
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
        /// 杩斿洖 UniTask&lt;GameObject&gt;鐗堟湰鐨?Get 鏂规硶锛岄€傜敤浜庨渶瑕佷粠杩滅▼鎴栧紓姝ヨ祫婧愮郴缁熷姞杞介鍒朵綋鐨勬儏鍐碉紝閬垮厤鍦ㄤ富绾跨▼绛夊緟璧勬簮鍔犺浇瀹屾垚銆?
        /// </summary>
        /// <param name="parent"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
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
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
                data = new GameObjectPoolData(poolRootTransform, -1, $"Pool_{key}");
                PoolDic[key] = data;
            }

            if (data.TryGet(out var go, parent))
            {
                if (!TryGetPoolable(go, key, out IGameObjectPoolable poolable)) return null;
                poolable.Spawn();
                return go;
            }

            // 浣跨敤 IResLoad 鐨?LoadAsync 鏂规硶鏉ュ姞杞借祫婧愶紝鍔犺浇瀹屾垚鍚庡疄渚嬪寲骞惰繑鍥?
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
                WSLog.Log("鍒涘缓鏂扮殑瀵硅薄姹? " + key + ", 榛樿鏃犻檺瀹归噺锛屽鏋滈渶瑕佸閲忛檺鍒惰棰勫厛璋冪敤 Prewarm 鏂规硶璁剧疆瀹归噺锛屽悓鏃跺缓璁鐑睜瀛愪互閬垮厤鍚庣画鑾峰彇鏃剁殑鎬ц兘闂");
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

            // 浣跨敤 IResLoad 鐨?LoadAsync 鍥炶皟鎺ュ彛鏉ュ姞杞借祫婧愶紝鍔犺浇瀹屾垚鍚庡湪鍥炶皟涓疄渚嬪寲骞惰繑鍥?
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
        /// 鍥炴敹瀵硅薄鍒板搴旂殑姹犱腑锛屽鏋滄睜涓嶅瓨鍦ㄥ垯浼氬垱寤轰竴涓柊鐨勬睜锛屽鏋滄睜涓嶅瓨鍦ㄥ氨鐩存帴涓㈠純瀵硅薄锛岄€傜敤浜庨渶瑕佹墜鍔ㄦ寚瀹氬洖鏀跺璞℃墍灞炴睜鐨勬儏鍐点€?
        /// </summary>
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

        #region 璇ョ被鐨勫悎鐞嗘€ф楠?
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


