using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WS_Modules.Extensions;
using WS_Modules.LogModule;
using WS_Modules.ResLoadModule;

namespace WS_Modules.UIModule
{
    /// <summary>
    /// UI 窗口生命周期服务，负责窗口加载、预加载、显示、隐藏和销毁。
    /// </summary>
    internal sealed class UIWindowLifecycleService
    {
        private readonly UIWindowRegistry registry;
        private readonly UIWindowLayerService layerService;
        private readonly Dictionary<string, UniTaskCompletionSource<WindowBase>> loadingWindows = new();
        private readonly WindowConfig windowConfig;
        private readonly Transform uiRoot;
        private readonly Camera uiCamera;
        private string currentTopWindowName = string.Empty;

        /// <summary>
        /// 窗口被隐藏或销毁后触发，用于驱动窗口栈弹出下一个窗口。
        /// </summary>
        internal event Action<WindowBase> WindowClosed;

        /// <summary>
        /// 窗口状态变化时触发。
        /// </summary>
        internal event Action<UIWindowStateChangedEventArgs> WindowStateChanged;

        /// <summary>
        /// 窗口稳定显示后触发。
        /// </summary>
        internal event Action<UIWindowSnapshot> WindowOpened;

        /// <summary>
        /// 窗口稳定隐藏后触发。
        /// </summary>
        internal event Action<UIWindowSnapshot> WindowHidden;

        /// <summary>
        /// 窗口销毁后触发。
        /// </summary>
        internal event Action<UIWindowSnapshot> WindowDestroyed;

        /// <summary>
        /// 顶层窗口变化时触发。
        /// </summary>
        internal event Action<UIWindowTopChangedEventArgs> TopWindowChanged;

        /// <summary>
        /// 创建窗口生命周期服务。
        /// </summary>
        /// <param name="registry">窗口注册表。</param>
        /// <param name="layerService">窗口层级服务。</param>
        /// <param name="windowConfig">窗口配置表。</param>
        /// <param name="uiRoot">UI 根节点。</param>
        /// <param name="uiCamera">UI 摄像机。</param>
        public UIWindowLifecycleService(
            UIWindowRegistry registry,
            UIWindowLayerService layerService,
            WindowConfig windowConfig,
            Transform uiRoot,
            Camera uiCamera)
        {
            this.registry = registry;
            this.layerService = layerService;
            this.windowConfig = windowConfig;
            this.uiRoot = uiRoot;
            this.uiCamera = uiCamera;
        }

        /// <summary>
        /// 预加载窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        public async UniTask PreLoadWindowAsync<T>() where T : WindowBase, new()
        {
            string windowName = typeof(T).Name;
            if (loadingWindows.TryGetValue(windowName, out UniTaskCompletionSource<WindowBase> loadingSource))
            {
                WSLog.LogWarning("窗口正在加载中，预加载已复用加载任务，窗口名称:" + windowName);
                await loadingSource.Task;
                return;
            }

            if (registry.Contains(windowName))
            {
                WSLog.Log("窗口已存在，跳过预加载，窗口名称:" + windowName);
                return;
            }

            WindowBase window = await InitializeWindowWithLoading(windowName, new T(), false);
            if (window != null)
            {
                WSLog.Log("预加载窗口完成，窗口名称:" + windowName);
            }
        }

        /// <summary>
        /// 弹出指定类型窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>窗口对象。</returns>
        public async UniTask<T> PopUpWindowAsync<T>() where T : WindowBase, new()
        {
            string windowName = typeof(T).Name;
            if (loadingWindows.TryGetValue(windowName, out UniTaskCompletionSource<WindowBase> loadingSource))
            {
                WSLog.LogWarning("窗口正在加载中，已复用加载任务，窗口名称:" + windowName);
                WindowBase loadingWindow = await loadingSource.Task;
                if (loadingWindow is { Visible: false } &&
                    registry.TryGetRecord(windowName, out UIWindowRecord loadingRecord))
                {
                    loadingWindow = ShowExistingWindow(loadingRecord);
                }

                return loadingWindow as T;
            }

            if (registry.TryGetRecord(windowName, out UIWindowRecord record))
            {
                return ShowExistingWindow(record) as T;
            }

            WSLog.Log("弹出窗口，窗口名称:" + windowName);
            WindowBase window = await InitializeWindowWithLoading(windowName, new T(), true);
            if (window is { Visible: false } && registry.TryGetRecord(windowName, out record))
            {
                window = ShowExistingWindow(record);
            }

            return window as T;
        }

        /// <summary>
        /// 弹出指定类型窗口，并传入本次打开的临时参数。
        /// </summary>
        /// <param name="openContext">本次打开参数。</param>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <typeparam name="TOpenContext">临时打开参数类型。</typeparam>
        /// <returns>窗口对象。</returns>
        public async UniTask<TWindow> PopUpWindowAsync<TWindow, TOpenContext>(TOpenContext openContext)
            where TWindow : WindowBase, new()
        {
            string windowName = typeof(TWindow).Name;
            if (loadingWindows.TryGetValue(windowName, out UniTaskCompletionSource<WindowBase> loadingSource))
            {
                WSLog.LogWarning("窗口正在加载中，已复用加载任务，窗口名称:" + windowName);
                WindowBase loadingWindow = await loadingSource.Task;
                if (loadingWindow != null && registry.TryGetRecord(windowName, out UIWindowRecord loadingRecord))
                {
                    loadingWindow = ShowExistingWindow(loadingRecord, openContext);
                }

                return loadingWindow as TWindow;
            }

            if (registry.TryGetRecord(windowName, out UIWindowRecord record))
            {
                return ShowExistingWindow(record, openContext) as TWindow;
            }

            WSLog.Log("弹出窗口，窗口名称:" + windowName);
            WindowBase window = await InitializeWindowWithLoading(windowName, new TWindow(), false);
            if (window != null && registry.TryGetRecord(windowName, out record))
            {
                window = ShowExistingWindow(record, openContext);
            }

            return window as TWindow;
        }

        /// <summary>
        /// 弹出已有窗口对象，主要用于窗口栈。
        /// </summary>
        /// <param name="window">窗口对象。</param>
        /// <returns>窗口对象。</returns>
        public async UniTask<WindowBase> PopUpWindowAsync(WindowBase window)
        {
            string windowName = string.IsNullOrEmpty(window.Name) ? window.GetType().Name : window.Name;
            if (loadingWindows.TryGetValue(windowName, out UniTaskCompletionSource<WindowBase> loadingSource))
            {
                WSLog.LogWarning("窗口正在加载中，已复用加载任务，窗口名称:" + windowName);
                WindowBase loadingWindow = await loadingSource.Task;
                if (loadingWindow is { Visible: false } &&
                    registry.TryGetRecord(windowName, out UIWindowRecord loadingRecord))
                {
                    loadingWindow = ShowExistingWindow(loadingRecord);
                }

                return loadingWindow;
            }

            if (registry.TryGetRecord(windowName, out UIWindowRecord record))
            {
                return ShowExistingWindow(record);
            }

            WSLog.Log("弹出窗口，窗口名称:" + windowName);
            WindowBase initializedWindow = await InitializeWindowWithLoading(windowName, window, true);
            if (initializedWindow is { Visible: false } && registry.TryGetRecord(windowName, out record))
            {
                initializedWindow = ShowExistingWindow(record);
            }

            return initializedWindow;
        }

        /// <summary>
        /// 隐藏指定窗口。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void HideWindow(string windowName)
        {
            if (registry.TryGetRecord(windowName, out UIWindowRecord record))
            {
                HideWindow(record, true);
            }
        }

        /// <summary>
        /// 销毁指定窗口。
        /// </summary>
        /// <param name="windowName">窗口名称。</param>
        public void DestroyWindow(string windowName)
        {
            if (loadingWindows.ContainsKey(windowName))
            {
                WSLog.LogWarning("窗口正在加载中，跳过本次销毁请求，窗口名称:" + windowName);
                return;
            }

            if (!registry.TryGetRecord(windowName, out var record))
            {
                return;
            }

            if (record.State == UIWindowState.Loading || record.GameObject == null)
            {
                WSLog.LogWarning("窗口尚未完成加载，跳过本次销毁请求，窗口名称:" + windowName);
                return;
            }

            if (record.Window.Visible)
            {
                HideWindow(record, false);
            }

            UIWindowSnapshot destroyedSnapshot = registry.CreateSnapshot(record);
            registry.Unregister(windowName);
            record.Window.OnDestroy();
            WindowDestroyed?.Invoke(destroyedSnapshot);
            PublishTopWindowChanged();
            WindowClosed?.Invoke(record.Window);

            WindowConfigData windowData = windowConfig != null ? windowConfig.GetWindowData(windowName) : null;
            if (windowData != null)
            {
                ResSystem.Instance.UnLoadAsync<GameObject>(windowData.windowPrefabPath, null);
            }

            GameObject.Destroy(record.GameObject);
        }

        /// <summary>
        /// 获取指定类型窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <param name="logWhenMissing">缺失时是否输出错误日志。</param>
        /// <returns>窗口对象。</returns>
        public T GetWindow<T>(bool logWhenMissing = true) where T : WindowBase
        {
            T window = registry.GetWindow<T>();
            if (window == null && logWhenMissing)
            {
                WSLog.LogError("窗口字典中不存在窗口，无法获取窗口，窗口名称:" + typeof(T).Name);
            }

            return window;
        }

        /// <summary>
        /// 获取所有窗口运行时快照。
        /// </summary>
        /// <returns>所有已注册窗口的只读快照。</returns>
        public IReadOnlyList<UIWindowSnapshot> GetWindowSnapshots()
        {
            return registry.GetWindowSnapshots();
        }

        /// <summary>
        /// 获取当前顶层窗口快照。
        /// </summary>
        /// <param name="snapshot">顶层窗口快照。</param>
        /// <returns>存在顶层窗口时返回 true。</returns>
        public bool TryGetTopWindowSnapshot(out UIWindowSnapshot snapshot)
        {
            return registry.TryGetTopWindowSnapshot(out snapshot);
        }

        private async UniTask<WindowBase> InitializeWindowWithLoading<T>(string windowName, T windowBase,
            bool isVisible)
            where T : WindowBase
        {
            if (loadingWindows.TryGetValue(windowName, out UniTaskCompletionSource<WindowBase> loadingSource))
            {
                WSLog.LogWarning("窗口正在加载中，已复用加载任务，窗口名称:" + windowName);
                return await loadingSource.Task;
            }

            UniTaskCompletionSource<WindowBase> source = new UniTaskCompletionSource<WindowBase>();
            loadingWindows.Add(windowName, source);

            try
            {
                WindowBase initializedWindow = await InitializeWindow(windowName, windowBase, isVisible);
                source.TrySetResult(initializedWindow);
                return initializedWindow;
            }
            catch (Exception exception)
            {
                source.TrySetException(exception);
                throw;
            }
            finally
            {
                loadingWindows.Remove(windowName);
            }
        }

        private async UniTask<WindowBase> InitializeWindow<T>(string windowName, T windowBase, bool isVisible)
            where T : WindowBase
        {
            UIWindowRecord record = registry.Register(windowName, windowBase, UIWindowState.Loading);
            WindowStateChanged?.Invoke(new UIWindowStateChangedEventArgs(
                record.WindowName,
                UIWindowState.Hidden,
                UIWindowState.Loading,
                registry.CreateSnapshot(record)));
            GameObject windowObject = await LoadWindow(windowName);
            if (windowObject == null)
            {
                registry.Unregister(windowName);
                WSLog.LogError("弹出窗口失败，无法加载窗口预制体，窗口名称:" + windowName);
                return null;
            }

            if (!registry.TryGetRecord(windowName, out UIWindowRecord currentRecord) ||
                !ReferenceEquals(currentRecord, record))
            {
                GameObject.Destroy(windowObject);
                WSLog.LogWarning("窗口加载完成时记录已失效，已丢弃加载结果，窗口名称:" + windowName);
                return null;
            }

            BindWindowBase(windowBase, windowObject);
            if (isVisible)
            {
                ShowInitializedWindow(record);
            }
            else
            {
                windowBase.SetVisible(false);
                SetRecordState(record, UIWindowState.Hidden);
            }

            return windowBase;
        }

        private void ShowInitializedWindow(UIWindowRecord record)
        {
            SetRecordState(record, UIWindowState.Showing);
            registry.MarkShown(record.WindowName);
            record.Window.SetVisible(true);
            record.Window.OnShow();
            SetRecordState(record, UIWindowState.Visible);
            layerService.OnWindowShown(record.Window, registry.GetVisibleWindows());
            WindowOpened?.Invoke(registry.CreateSnapshot(record));
            PublishTopWindowChanged();
        }

        private WindowBase ShowExistingWindow(UIWindowRecord record)
        {
            WindowBase window = record.Window;
            WSLog.Log("显示窗口，窗口名称:" + record.WindowName);
            if (window is { GameObject: not null, Visible: false })
            {
                SetRecordState(record, UIWindowState.Showing);
                registry.MarkShown(record.WindowName);
                window.Transform.SetAsLastSibling();
                window.SetVisible(true);
                window.OnShow();
                SetRecordState(record, UIWindowState.Visible);
                layerService.OnWindowShown(window, registry.GetVisibleWindows());
                WindowOpened?.Invoke(registry.CreateSnapshot(record));
                PublishTopWindowChanged();
                WSLog.Log("窗口显示成功，窗口名称:" + record.WindowName);
            }
            else if (window is { GameObject: not null, Visible: true })
            {
                window.OnShow();
            }

            return window;
        }

        private WindowBase ShowExistingWindow<TOpenContext>(UIWindowRecord record, TOpenContext openContext)
        {
            WindowBase window = record.Window;
            WSLog.Log("显示窗口，窗口名称:" + record.WindowName);
            if (window is { GameObject: not null, Visible: false })
            {
                SetRecordState(record, UIWindowState.Showing);
                registry.MarkShown(record.WindowName);
                window.Transform.SetAsLastSibling();
                ApplyOpenContext(window, openContext);
                window.SetVisible(true);
                window.OnShow();
                SetRecordState(record, UIWindowState.Visible);
                layerService.OnWindowShown(window, registry.GetVisibleWindows());
                WindowOpened?.Invoke(registry.CreateSnapshot(record));
                PublishTopWindowChanged();
                WSLog.Log("窗口显示成功，窗口名称:" + record.WindowName);
            }
            else if (window is { GameObject: not null, Visible: true })
            {
                ApplyOpenContext(window, openContext);
                window.OnShow();
            }

            return window;
        }

        private void HideWindow(UIWindowRecord record, bool notifyClosed)
        {
            WindowBase window = record.Window;
            if (window is not { GameObject: not null, Visible: true })
            {
                return;
            }

            SetRecordState(record, UIWindowState.Hiding);
            window.SetVisible(false);
            window.OnHide();
            SetRecordState(record, UIWindowState.Hidden);
            registry.MarkHidden(record.WindowName);
            layerService.OnWindowHidden(window, registry.GetVisibleWindows());
            WindowHidden?.Invoke(registry.CreateSnapshot(record));
            PublishTopWindowChanged();
            if (notifyClosed)
            {
                WindowClosed?.Invoke(window);
            }
        }

        private void BindWindowBase<T>(T windowBase, GameObject windowObject) where T : WindowBase
        {
            windowBase.OnAwake(windowObject, uiCamera);
            RectTransform rectTransform = windowObject.GetComponent<RectTransform>();
            rectTransform.SetFullStretch();
        }

        private async UniTask<GameObject> LoadWindow(string windowName)
        {
            if (windowConfig == null)
            {
                WSLog.LogError("窗口配置表未设置，无法加载窗口预制体，窗口名称:" + windowName);
                return null;
            }

            WindowConfigData windowData = windowConfig.GetWindowData(windowName);
            if (windowData == null)
            {
                WSLog.LogError("窗口配置表中不存在窗口数据，无法加载窗口预制体，窗口名称:" + windowName);
                return null;
            }

            GameObject windowPrefab = await ResSystem.Instance.LoadAsync<GameObject>(windowData.windowPrefabPath);
            if (windowPrefab == null)
            {
                WSLog.LogError("窗口预制体加载失败，窗口名称:" + windowName + "，路径:" + windowData.windowPrefabPath);
                return null;
            }

            GameObject windowObject = GameObject.Instantiate(windowPrefab, uiRoot, true);
            windowObject.transform.Reset();
            windowObject.name = windowName;
            return windowObject;
        }

        private void SetRecordState(UIWindowRecord record, UIWindowState state)
        {
            UIWindowState oldState = record.State;
            if (oldState == state)
            {
                return;
            }

            record.SetState(state);
            UIWindowSnapshot snapshot = registry.CreateSnapshot(record);
            WindowStateChanged?.Invoke(new UIWindowStateChangedEventArgs(record.WindowName, oldState, state, snapshot));
        }

        private void ApplyOpenContext<TOpenContext>(WindowBase window, TOpenContext openContext)
        {
            if (window is IWindowWithOpenContext<TOpenContext> windowWithOpenContext)
            {
                windowWithOpenContext.ApplyOpenContext(openContext);
                return;
            }

            WSLog.LogWarning($"窗口未实现匹配的 OpenContext 接口，窗口名称:{window?.Name}, OpenContext类型:{typeof(TOpenContext).Name}");
        }

        private void PublishTopWindowChanged()
        {
            bool hasNewTop = registry.TryGetTopWindowSnapshot(out UIWindowSnapshot newTop);
            string newTopName = hasNewTop ? newTop.WindowName : string.Empty;
            if (currentTopWindowName == newTopName)
            {
                return;
            }

            UIWindowSnapshot oldTop = UIWindowSnapshot.Empty;
            bool hasOldTop = false;
            if (!string.IsNullOrEmpty(currentTopWindowName) &&
                registry.TryGetRecord(currentTopWindowName, out UIWindowRecord oldRecord) &&
                oldRecord.GameObject != null)
            {
                oldTop = registry.CreateSnapshot(oldRecord);
                hasOldTop = true;
            }

            currentTopWindowName = newTopName;
            TopWindowChanged?.Invoke(new UIWindowTopChangedEventArgs(oldTop, hasOldTop, newTop, hasNewTop));
        }
    }
}
