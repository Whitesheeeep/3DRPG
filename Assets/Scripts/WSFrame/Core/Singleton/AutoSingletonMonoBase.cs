using UnityEngine;

namespace WS_Modules.Singleton
{
    public class AutoSingletonMonoBase<T> : MonoBehaviour
        where T : AutoSingletonMonoBase<T>
    {
        private enum InitState { None, Initializing, Done }
        private static InitState _initState = InitState.None;
        private static bool _isQuitting; // 加上 quit 检查避免 quit 调用导致 quit 的时候再次访问 Instance 触发创建新实例的情况
        private static readonly object _instanceLock = new object(); // 锁对象，确保线程安全

        private static T _instance;
        public static bool IsCreated => _instance != null && !_isQuitting;

        public static T Instance
        {
            get
            {
                // 1. 快速退出检查（非锁区）
                if (_isQuitting) return null;

                // 2. 第一层检查：如果已经初始化，直接返回，避开 lock 开销
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        // 3. 第二层检查：确保在排队进入 lock 期间没被其他线程初始化
                        if (_instance == null && !_isQuitting)
                        {
                            _instance = Object.FindFirstObjectByType<T>();

                            if (_instance == null)
                            {
                                // 注意：此处在子线程访问会抛出 Unity 异常，这是引擎底层限制
                                var obj = new GameObject(typeof(T).Name + " (Singleton)");
                                _instance = obj.AddComponent<T>();
                            }
                            if (Application.isPlaying)
                                DontDestroyOnLoad(_instance.gameObject);
                        }
                    }
                }

                // 4. 确保初始化逻辑只运行一次
                if (_instance != null && _initState == InitState.None)
                {
                    lock (_instanceLock)
                    {
                        EnsureInited();
                    }
                }

                return _instance;
            }
        }

        protected virtual void Awake()
        {
            lock (_instanceLock)
            {
                if (_instance == null)
                {
                    _instance = this as T;
                    DontDestroyOnLoad(gameObject);
                    EnsureInited();
                }
                else if (_instance != this)
                {
                    // 已经有实例了，销毁多余的
                    Destroy(gameObject);
                }
            }
        }

        private static void EnsureInited()
        {
            if (_instance == null || _isQuitting || _initState != InitState.None) return;

            _initState = InitState.Initializing;
            try
            {
                _instance.Init();
                _initState = InitState.Done;
            }
            catch (System.Exception e)
            {
                _initState = InitState.None;
                Debug.LogError($"[Singleton] {typeof(T).Name} Init Failed: {e}");
            }
        }

        protected virtual void OnApplicationQuit()
        {
            // 静态变量标志位，防止销毁阶段再次被触发生成
            _isQuitting = true;
        }

        protected virtual void OnDestroy()
        {
            lock (_instanceLock)
            {
                if (_instance == this)
                {
                    _instance = null;
                    _initState = InitState.None;
                }
            }
        }

        public virtual void Init() { }
    }
}
