using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;
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
        private bool isShuttingDown;
        private int initializationVersion;

        /// <summary>
        /// UI 摄像机。
        /// </summary>
        public Camera Camera => uiCamera;

        /// <summary>
        /// UI 管理器是否已经完成服务初始化。
        /// </summary>
        public bool IsInitialized => !isShuttingDown && lifecycleService != null && stackService != null;

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
            int currentInitializationVersion = BeginInitialization();
            this.windowConfig = windowConfig;
            uiRoot = GameObject.Find("UIRoot")?.transform ??
                     GameObject.Instantiate(ResSystem.Instance.Load<GameObject>(uiRootPath)).transform;
            uiCamera = GameObject.Find("UICamera")?.GetComponent<Camera>() ?? GameObject
                .Instantiate(ResSystem.Instance.Load<GameObject>(uiCameraPath)).GetComponent<Camera>();
            GameObject uiEventSystem = GameObject.Find("UIEventSystem") ??
                                       GameObject.Instantiate(
                                       await ResSystem.Instance.LoadAsync<GameObject>(uiEventSystemPath));

            if (!IsInitializationCurrent(currentInitializationVersion))
            {
                return;
            }

            // 实在没有，最后通过 Resources 加载
            if (uiRoot == null) uiRoot = GameObject.Instantiate(Resources.Load<GameObject>("UIRoot")).transform;
            if (uiCamera == null) uiCamera = GameObject.Instantiate(Resources.Load<GameObject>("UICamera")).GetComponent<Camera>();
            if (uiEventSystem == null) uiEventSystem = GameObject.Instantiate(Resources.Load<GameObject>("UIEventSystem"));

            ConfigureCameraStack();
            uiEventSystem.name = "UIEventSystem";
            GameObject.DontDestroyOnLoad(uiRoot);
            GameObject.DontDestroyOnLoad(uiCamera);
            GameObject.DontDestroyOnLoad(uiEventSystem);
            InitializeServices(isSingleMask);
        }

        /// <summary>
        /// 关闭 UI 管理器并释放所有窗口；该操作可重复调用，且不会触发常规窗口栈事件。
        /// </summary>
        public void Shutdown()
        {
            if (isShuttingDown && lifecycleService == null && stackService == null)
            {
                return;
            }

            // 先标记关闭并使旧的异步初始化失效，防止停止运行时继续创建窗口服务。
            isShuttingDown = true;
            initializationVersion++;

            stackService?.Shutdown();
            UnsubscribeLifecycleEvents();
            lifecycleService?.Shutdown();

            stackService = null;
            lifecycleService = null;
            layerService = null;
            windowRegistry = null;
            uiRoot = null;
            uiCamera = null;
            windowConfig = null;

            // 管理器即将进入不可用状态，清理外部订阅，避免禁用域重载时保留旧对象引用。
            WindowStateChanged = null;
            WindowOpened = null;
            WindowHidden = null;
            WindowDestroyed = null;
            TopWindowChanged = null;
        }

        /// <summary>
        /// 将 UI Overlay 摄像机加入常驻 Base 摄像机的渲染栈。
        /// </summary>
        /// <exception cref="InvalidOperationException">主摄像机、摄像机类型或 URP Stack 配置不符合约定时抛出。</exception>
        private void ConfigureCameraStack()
        {
            // 主摄像机是游戏唯一的最终输出，UI 摄像机只能作为 Overlay 追加渲染，不能独立清除画面。
            Camera baseCamera = Camera.main;
            if (baseCamera == null)
            {
                throw new InvalidOperationException("UIManager 初始化失败：找不到带有 MainCamera Tag 的常驻 Base Camera。");
            }

            if (uiCamera == null)
            {
                throw new InvalidOperationException("UIManager 初始化失败：UICamera 预制体缺少 Camera 组件。");
            }

            UniversalAdditionalCameraData baseCameraData = baseCamera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData uiCameraData = uiCamera.GetUniversalAdditionalCameraData();
            if (baseCameraData.renderType != CameraRenderType.Base)
            {
                throw new InvalidOperationException("UIManager 初始化失败：MainCamera 必须设置为 URP Base。");
            }

            if (uiCameraData.renderType != CameraRenderType.Overlay)
            {
                throw new InvalidOperationException("UIManager 初始化失败：UICamera 必须设置为 URP Overlay。");
            }

            List<Camera> cameraStack = baseCameraData.cameraStack;
            if (cameraStack == null)
            {
                throw new InvalidOperationException("UIManager 初始化失败：MainCamera 当前 Renderer 不支持 Camera Stack。");
            }

            // 只清理 UICamera 自身的重复引用，保留其他 Overlay 摄像机及其顺序。
            for (int index = cameraStack.Count - 1; index >= 0; index--)
            {
                if (cameraStack[index] == uiCamera)
                {
                    cameraStack.RemoveAt(index);
                }
            }

            // 初始化可重复调用，但同一 UICamera 在 Stack 中始终只保留一个末尾引用。
            cameraStack.Add(uiCamera);
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

        /// <summary>
        /// 创建当前 UI 代次使用的注册表、层级、生命周期和窗口栈服务。
        /// </summary>
        /// <param name="isSingleMask">是否启用单遮罩策略。</param>
        private void InitializeServices(bool isSingleMask)
        {
            UnsubscribeLifecycleEvents();
            windowRegistry = new UIWindowRegistry();
            layerService = new UIWindowLayerService(isSingleMask, true);
            lifecycleService = new UIWindowLifecycleService(windowRegistry, layerService, windowConfig, uiRoot, uiCamera);
            SubscribeLifecycleEvents();
            stackService = new UIWindowStackService(lifecycleService);
        }

        /// <summary>
        /// 开始一次新的 UI 初始化，并关闭可能残留的上一代服务。
        /// </summary>
        /// <returns>本次初始化的代次标识。</returns>
        private int BeginInitialization()
        {
            Shutdown();
            isShuttingDown = false;
            return ++initializationVersion;
        }

        /// <summary>
        /// 判断异步初始化回调是否仍属于当前有效的 UI 管理器代次。
        /// </summary>
        /// <param name="version">初始化开始时保存的代次。</param>
        /// <returns>仍可继续初始化时返回 true。</returns>
        private bool IsInitializationCurrent(int version)
        {
            return !isShuttingDown && initializationVersion == version;
        }

        #region 事件转发
        /// <summary>
        /// 建立生命周期服务到 UIManager 公共事件的转发订阅。
        /// </summary>
        private void SubscribeLifecycleEvents()
        {
            lifecycleService.WindowStateChanged += OnLifecycleWindowStateChanged;
            lifecycleService.WindowOpened += OnLifecycleWindowOpened;
            lifecycleService.WindowHidden += OnLifecycleWindowHidden;
            lifecycleService.WindowDestroyed += OnLifecycleWindowDestroyed;
            lifecycleService.TopWindowChanged += OnLifecycleTopWindowChanged;
        }

        /// <summary>
        /// 解除当前生命周期服务到 UIManager 公共事件的转发订阅。
        /// </summary>
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

        /// <summary>
        /// 判断 UI 服务是否可用；关闭期间静默拒绝请求，初始化未完成时输出诊断日志。
        /// </summary>
        /// <returns>服务可用时返回 true。</returns>
        private bool EnsureServicesReady()
        {
            if (isShuttingDown)
            {
                return false;
            }

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


