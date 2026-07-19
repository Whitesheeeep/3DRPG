using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;
using WS_Modules.Singleton;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 管理器，对外提供窗口生命周期和窗口栈 API，内部委托给具体服务处理。
    /// </summary>
    public class UIManager : SingletonBase<UIManager>
    {
        private UIManager()
        {
        }

        private Camera uiCamera;
        private Transform uiRoot;
        private WindowConfig windowConfig;
        private UIWindowRegistry windowRegistry;
        private UIWindowLayerService layerService;
        private UIWindowLifecycleService lifecycleService;
        private UIWindowStackService stackService;

        /// <summary>
        /// UI 摄像机。
        /// </summary>
        public Camera Camera => uiCamera;

        /// <summary>
        /// UI 管理器是否已经完成服务初始化。
        /// </summary>
        public bool IsInitialized => lifecycleService != null && stackService != null;

        /// <summary>
        /// 窗口状态变化时触发。
        /// </summary>
        public event Action<UIWindowStateChangedEventArgs> WindowStateChanged;

        /// <summary>
        /// 窗口稳定显示后触发。
        /// </summary>
        public event Action<UIWindowSnapshot> WindowOpened;

        /// <summary>
        /// 窗口稳定隐藏后触发。
        /// </summary>
        public event Action<UIWindowSnapshot> WindowHidden;

        /// <summary>
        /// 窗口销毁后触发。
        /// </summary>
        public event Action<UIWindowSnapshot> WindowDestroyed;

        /// <summary>
        /// 顶层窗口变化时触发。
        /// </summary>
        public event Action<UIWindowTopChangedEventArgs> TopWindowChanged;

        /// <summary>
        /// 使用 FrameSetting 初始化 UI 管理器。
        /// </summary>
        /// <param name="uiManagerSetting">UI 管理配置。</param>
        public void Initialize(UIManagerSetting uiManagerSetting)
        {
            Initialize(uiManagerSetting.windowConfig,
                uiManagerSetting.uiCameraPrefabPath,
                uiManagerSetting.uiRootPath,
                uiManagerSetting.uiEventSystemPrefabPath,
                uiManagerSetting.isSingleMask).Forget();
        }

        /// <summary>
        /// 初始化 UI 根节点、摄像机、事件系统和内部服务。
        /// </summary>
        /// <param name="windowConfig">窗口配置表。</param>
        /// <param name="uiCameraPath">UI 摄像机资源路径。</param>
        /// <param name="uiRootPath">UI 根节点资源路径。</param>
        /// <param name="uiEventSystemPath">UI 事件系统资源路径。</param>
        /// <param name="isSingleMask">是否使用单遮罩。</param>
        public async UniTaskVoid Initialize(
            WindowConfig windowConfig,
            string uiCameraPath = "UICamera",
            string uiRootPath = "UIRoot",
            string uiEventSystemPath = "UIEventSystem",
            bool isSingleMask = false)
        {
            this.windowConfig = windowConfig;
            uiRoot = GameObject.Find("UIRoot")?.transform ??
                     GameObject.Instantiate(ResSystem.Instance.Load<GameObject>(uiRootPath)).transform;
            uiCamera = GameObject.Find("UICamera")?.GetComponent<Camera>() ?? GameObject
                .Instantiate(ResSystem.Instance.Load<GameObject>(uiCameraPath)).GetComponent<Camera>();
            GameObject uiEventSystem = GameObject.Find("UIEventSystem") ??
                                       GameObject.Instantiate(
                                           await ResSystem.Instance.LoadAsync<GameObject>(uiEventSystemPath));
            uiEventSystem.name = "UIEventSystem";
            GameObject.DontDestroyOnLoad(uiRoot);
            GameObject.DontDestroyOnLoad(uiCamera);
            GameObject.DontDestroyOnLoad(uiEventSystem);
            InitializeServices(isSingleMask);
        }

        /// <summary>
        /// 预加载窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PreLoadWindow<T>() where T : WindowBase, new()
        {
            PreLoadWindowAsync<T>().Forget();
        }

        /// <summary>
        /// 异步预加载窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public async UniTask PreLoadWindowAsync<T>() where T : WindowBase, new()
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            await lifecycleService.PreLoadWindowAsync<T>();
        }

        /// <summary>
        /// 弹出窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PopUpWindow<T>() where T : WindowBase, new()
        {
            PopUpWindowAsync<T>().Forget();
        }

        /// <summary>
        /// 弹出窗口并传入本次打开的临时参数。
        /// </summary>
        /// <param name="openContext">本次打开参数。</param>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <typeparam name="TOpenContext">临时打开参数类型。</typeparam>
        public void PopUpWindow<TWindow, TOpenContext>(TOpenContext openContext) where TWindow : WindowBase, new()
        {
            PopUpWindowAsync<TWindow, TOpenContext>(openContext).Forget();
        }

        /// <summary>
        /// 异步弹出窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口对象。</returns>
        public async UniTask<T> PopUpWindowAsync<T>() where T : WindowBase, new()
        {
            if (!EnsureServicesReady())
            {
                return null;
            }

            return await lifecycleService.PopUpWindowAsync<T>();
        }

        /// <summary>
        /// 异步弹出窗口并传入本次打开的临时参数。
        /// </summary>
        /// <param name="openContext">本次打开参数。</param>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <typeparam name="TOpenContext">临时打开参数类型。</typeparam>
        /// <returns>窗口对象。</returns>
        public async UniTask<TWindow> PopUpWindowAsync<TWindow, TOpenContext>(TOpenContext openContext)
            where TWindow : WindowBase, new()
        {
            if (!EnsureServicesReady())
            {
                return null;
            }

            return await lifecycleService.PopUpWindowAsync<TWindow, TOpenContext>(openContext);
        }

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void HideWindow<T>() where T : WindowBase
        {
            HideWindow(typeof(T).Name);
        }

        /// <summary>
        /// 隐藏窗口。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void HideWindow(string windowName)
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            lifecycleService.HideWindow(windowName);
        }

        /// <summary>
        /// 销毁窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void DestroyWindow<T>() where T : WindowBase
        {
            DestroyWindow(typeof(T).Name);
        }

        /// <summary>
        /// 销毁窗口。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void DestroyWindow(string windowName)
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            lifecycleService.DestroyWindow(windowName);
        }

        /// <summary>
        /// 获取已加载窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口对象。</returns>
        public T GetWindow<T>() where T : WindowBase
        {
            if (!EnsureServicesReady())
            {
                return null;
            }

            return lifecycleService.GetWindow<T>();
        }

        public bool TryGetWindow<T>(out T window) where T : WindowBase
        {
            window = null;
            if (!EnsureServicesReady())
            {
                return false;
            }

            window = lifecycleService.GetWindow<T>(false);
            return window != null;
        }

        /// <summary>
        /// 获取所有窗口运行时快照。
        /// </summary>
        /// <returns>所有已注册窗口的只读快照。</returns>
        public IReadOnlyList<UIWindowSnapshot> GetWindowSnapshots()
        {
            if (!EnsureServicesReady())
            {
                return Array.Empty<UIWindowSnapshot>();
            }

            return lifecycleService.GetWindowSnapshots();
        }

        /// <summary>
        /// 获取当前顶层窗口快照。
        /// </summary>
        /// <param name="snapshot">顶层窗口快照。</param>
        /// <returns>存在顶层窗口时返回 true。</returns>
        public bool TryGetTopWindowSnapshot(out UIWindowSnapshot snapshot)
        {
            if (!EnsureServicesReady())
            {
                snapshot = UIWindowSnapshot.Empty;
                return false;
            }

            return lifecycleService.TryGetTopWindowSnapshot(out snapshot);
        }

        /// <summary>
        /// 开始弹出栈内第一个窗口。
        /// </summary>
        public void StartPopFirstStackWindow()
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            stackService.StartPopFirstStackWindow();
        }

        /// <summary>
        /// 压入一个窗口到栈中。
        /// </summary>
        /// <param name="popCallBack">窗口弹出后的回调。</param>
        /// <param name="single">是否只允许存在一个。</param>
        /// <param name="pushToStackTop">是否插入到栈顶优先弹出。</param>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PushWindowToStack<T>(Action<WindowBase> popCallBack = null, bool single = false, bool pushToStackTop = false)
            where T : WindowBase, new()
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            stackService.PushWindowToStack<T>(popCallBack, single, pushToStackTop);
        }

        /// <summary>
        /// 压入窗口并开始弹出。
        /// </summary>
        /// <param name="popCallBack">窗口弹出后的回调。</param>
        /// <param name="single">是否只允许存在一个。</param>
        /// <param name="pushToStackTop">是否插入到栈顶优先弹出。</param>
        /// <typeparam name="T">窗口类型。</typeparam>
        public void PushAndPopStackWindow<T>(Action<WindowBase> popCallBack = null, bool single = false, bool pushToStackTop = false)
            where T : WindowBase, new()
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            stackService.PushAndPopStackWindow<T>(popCallBack, single, pushToStackTop);
        }

        /// <summary>
        /// 弹出栈内下一个窗口。
        /// </summary>
        /// <returns>成功弹出返回 true。</returns>
        public bool PopStackWindow()
        {
            if (!EnsureServicesReady())
            {
                return false;
            }

            return stackService.PopStackWindow();
        }

        /// <summary>
        /// 清空窗口栈。
        /// </summary>
        public void ClearStackWindows()
        {
            if (!EnsureServicesReady())
            {
                return;
            }

            stackService.ClearStackWindows();
        }

        private void InitializeServices(bool isSingleMask)
        {
            UnsubscribeLifecycleEvents();
            windowRegistry = new UIWindowRegistry();
            layerService = new UIWindowLayerService(isSingleMask, true);
            lifecycleService = new UIWindowLifecycleService(windowRegistry, layerService, windowConfig, uiRoot, uiCamera);
            SubscribeLifecycleEvents();
            stackService = new UIWindowStackService(lifecycleService);
        }

        #region 事件转发
        private void SubscribeLifecycleEvents()
        {
            lifecycleService.WindowStateChanged += OnLifecycleWindowStateChanged;
            lifecycleService.WindowOpened += OnLifecycleWindowOpened;
            lifecycleService.WindowHidden += OnLifecycleWindowHidden;
            lifecycleService.WindowDestroyed += OnLifecycleWindowDestroyed;
            lifecycleService.TopWindowChanged += OnLifecycleTopWindowChanged;
        }

        private void UnsubscribeLifecycleEvents()
        {
            if (lifecycleService == null)
            {
                return;
            }

            lifecycleService.WindowStateChanged -= OnLifecycleWindowStateChanged;
            lifecycleService.WindowOpened -= OnLifecycleWindowOpened;
            lifecycleService.WindowHidden -= OnLifecycleWindowHidden;
            lifecycleService.WindowDestroyed -= OnLifecycleWindowDestroyed;
            lifecycleService.TopWindowChanged -= OnLifecycleTopWindowChanged;
        }

        private void OnLifecycleWindowStateChanged(UIWindowStateChangedEventArgs args)
        {
            WindowStateChanged?.Invoke(args);
        }

        private void OnLifecycleWindowOpened(UIWindowSnapshot snapshot)
        {
            WindowOpened?.Invoke(snapshot);
        }

        private void OnLifecycleWindowHidden(UIWindowSnapshot snapshot)
        {
            WindowHidden?.Invoke(snapshot);
        }

        private void OnLifecycleWindowDestroyed(UIWindowSnapshot snapshot)
        {
            WindowDestroyed?.Invoke(snapshot);
        }

        private void OnLifecycleTopWindowChanged(UIWindowTopChangedEventArgs args)
        {
            TopWindowChanged?.Invoke(args);
        }

        private bool EnsureServicesReady()
        {
            if (lifecycleService != null && stackService != null)
            {
                return true;
            }

            WSLog.LogError("UIManager 尚未完成初始化，无法执行窗口操作。");
            return false;
        }
        #endregion
    }
}


