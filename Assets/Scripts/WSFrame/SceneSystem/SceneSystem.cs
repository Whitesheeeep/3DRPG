using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using WS_Modules.CustomEventSystem;

namespace WS_Modules.SceneModule
{
    /// <summary>
    /// 基于 Unity SceneManager 和 UniTask 的场景加载系统。
    /// </summary>
    public static class SceneSystem
    {
        private static readonly IEventCenter<SceneSystemEventType> eventModule = new EventCenterModule<SceneSystemEventType>();
        private static readonly Dictionary<string, SceneLoadInfo> loadedAdditiveScenes =
            new Dictionary<string, SceneLoadInfo>(StringComparer.Ordinal);

        /// <summary>
        /// 当前是否正在通过 SceneSystem 加载场景。
        /// </summary>
        public static bool IsLoading { get; private set; }

        /// <summary>
        /// 当前加载目标的名称或 BuildIndex 字符串。
        /// </summary>
        public static string CurrentLoadingTarget { get; private set; }

        private static int nextLoadId;
        private static int nextUnloadId;
        private static SceneLoadInfo currentLoadInfo;

        /// <summary>
        /// 获取当前活动场景
        /// </summary>
        public static Scene CurrentScene => SceneManager.GetActiveScene();

        /// <summary>
        /// 获取当前场景名称
        /// </summary>
        public static string CurrentSceneName => CurrentScene.name;

        /// <summary>
        /// 获取当前场景索引
        /// </summary>
        public static int CurrentSceneIndex => CurrentScene.buildIndex;

        /// <summary>
        /// 判断目标场景是否由 SceneSystem 以 Additive 模式加载并记录。
        /// </summary>
        /// <param name="sceneName">目标场景名称。</param>
        /// <returns>如果目标场景位于 SceneSystem 的 Additive 记录集合中，则返回 true。</returns>
        public static bool IsSceneLoaded(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName) && loadedAdditiveScenes.ContainsKey(sceneName);
        }

        /// <summary>
        /// 获取 SceneSystem 当前记录的 Additive 场景名称快照。
        /// </summary>
        /// <returns>当前记录的 Additive 场景名称数组。</returns>
        public static string[] GetLoadedAdditiveSceneNames()
        {
            var sceneNames = new string[loadedAdditiveScenes.Count];
            loadedAdditiveScenes.Keys.CopyTo(sceneNames, 0);
            return sceneNames;
        }

        #region Sync Load
        // LoadSceneMode: Single（默认）会卸载当前场景并加载新场景；Additive 会在当前场景基础上加载新场景，不会卸载当前场景。
        /// <summary>
        /// 通过场景名称同步加载场景。
        /// </summary>
        /// <param name="sceneName">目标场景名称。</param>
        /// <param name="mode">场景加载模式。</param>
        public static void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            SceneLoadRequest request = CreateSceneNameRequest(sceneName, mode, true);
            try
            {
                BeginValidatedLoad(request);
                SceneManager.LoadScene(sceneName, mode);
                CompleteLoad(request.LoadInfo);
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }

        /// <summary>
        /// 通过 BuildIndex 同步加载场景。
        /// </summary>
        /// <param name="sceneBuildIndex">目标场景 BuildIndex。</param>
        /// <param name="mode">场景加载模式。</param>
        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            SceneLoadRequest request = CreateBuildIndexRequest(sceneBuildIndex, mode, true);
            try
            {
                BeginValidatedLoad(request);
                SceneManager.LoadScene(sceneBuildIndex, mode);
                CompleteLoad(request.LoadInfo);
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }

        // LoadSceneParameters 允许更细粒度的控制加载行为，例如是否允许场景激活、加载时的本地化设置等。
        /// <summary>
        /// 通过场景名称和加载参数同步加载场景。
        /// </summary>
        /// <param name="sceneName">目标场景名称。</param>
        /// <param name="loadSceneParameters">Unity 场景加载参数。</param>
        /// <returns>加载得到的场景。</returns>
        public static Scene LoadScene(string sceneName, LoadSceneParameters loadSceneParameters)
        {
            SceneLoadRequest request = CreateSceneNameRequest(sceneName, loadSceneParameters.loadSceneMode, true);
            try
            {
                BeginValidatedLoad(request);
                Scene scene = SceneManager.LoadScene(sceneName, loadSceneParameters);
                CompleteLoad(request.LoadInfo);
                return scene;
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }

        /// <summary>
        /// 通过 BuildIndex 和加载参数同步加载场景。
        /// </summary>
        /// <param name="sceneBuildIndex">目标场景 BuildIndex。</param>
        /// <param name="loadSceneParameters">Unity 场景加载参数。</param>
        /// <returns>加载得到的场景。</returns>
        public static Scene LoadScene(int sceneBuildIndex, LoadSceneParameters loadSceneParameters)
        {
            SceneLoadRequest request = CreateBuildIndexRequest(
                sceneBuildIndex,
                loadSceneParameters.loadSceneMode,
                true);
            try
            {
                BeginValidatedLoad(request);
                Scene scene = SceneManager.LoadScene(sceneBuildIndex, loadSceneParameters);
                CompleteLoad(request.LoadInfo);
                return scene;
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }
        #endregion

        #region Async Load (UniTask)

        /// <summary>
        /// 异步加载场景 (UniTask)
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="callBack">回调函数,注意：每次进度更新都会调用一次,参数为0-1的进度</param>
        /// <param name="mode">加载模式</param>
        /// <param name="cancellationToken">取消加载等待流程的令牌。</param>
        public static async UniTask LoadSceneAsync(string sceneName, Action<float> callBack = null,
            LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            SceneLoadRequest request = CreateSceneNameRequest(sceneName, mode, true);
            await ExecuteAsyncLoad(request, callBack, cancellationToken);
        }

        /// <summary>
        /// 异步加载场景 (UniTask)
        /// </summary>
        /// <param name="sceneBuildIndex">目标场景 BuildIndex。</param>
        /// <param name="callBack">加载进度回调，参数为 0 到 1。</param>
        /// <param name="mode">场景加载模式。</param>
        /// <param name="cancellationToken">取消加载等待流程的令牌。</param>
        public static async UniTask LoadSceneAsync(int sceneBuildIndex, Action<float> callBack = null,
            LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            SceneLoadRequest request = CreateBuildIndexRequest(sceneBuildIndex, mode, true);
            await ExecuteAsyncLoad(request, callBack, cancellationToken);
        }

        // 等待普通异步场景加载完成，并统一发布去重后的进度。
        private static async UniTask DoLoadSceneAsync(
            SceneLoadInfo loadInfo,
            AsyncOperation ao,
            Action<float> callBack,
            CancellationToken cancellationToken)
        {
            ao.allowSceneActivation = true;
            float lastReportedProgress = -1f;
            while (!ao.isDone)
            {
                // 进度可能会停在 0.9，直到激活场景
                // SceneManager.LoadSceneAsync 如果 allowSceneActivation=true，进度会在加载完变成 1 并 isDone=true
                cancellationToken.ThrowIfCancellationRequested();
                
                float progress = ao.progress < 0.9f ? ao.progress : 1.0f;

                ReportProgress(loadInfo, progress, callBack, ref lastReportedProgress);

                await UniTask.Yield();
            }
            
            ReportProgress(loadInfo, 1.0f, callBack, ref lastReportedProgress);
        }

        /// <summary>
        /// 异步加载场景，加载完成后不立刻切换 (UniTask)
        /// </summary>
        /// <param name="sceneName">场景名称</param>
        /// <param name="activeCallBack">手动激活场景的回调，当场景准备好时调用此回调，传入的 Action 用于执行激活操作</param>
        /// <param name="loadingCallBack">加载时进度回调</param>
        /// <param name="mode">加载模式</param>
        /// <param name="cancellationToken">取消加载等待流程的令牌。</param>
        public static async UniTask LoadSceneAsyncWithoutActive(string sceneName, Action<Action> activeCallBack,
            Action<float> loadingCallBack = null, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default)
        {
            SceneLoadRequest request = CreateSceneNameRequest(sceneName, mode, false);
            await ExecuteAsyncLoadWithoutActivation(request, activeCallBack, loadingCallBack, cancellationToken);
        }

        /// <summary>
        /// 异步加载场景，加载完成后不立刻切换 (UniTask)
        /// </summary>
        /// <param name="sceneIndex">目标场景 BuildIndex。</param>
        /// <param name="activeCallBack">手动激活场景的回调，当场景准备好时调用此回调，传入的 Action 用于执行激活操作。</param>
        /// <param name="loadingCallBack">加载时进度回调。</param>
        /// <param name="mode">场景加载模式。</param>
        /// <param name="cancellationToken">取消加载等待流程的令牌。</param>
        public static async UniTask LoadSceneAsyncWithoutActive(int sceneIndex, Action<Action> activeCallBack,
            Action<float> loadingCallBack = null, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default)
        {
            SceneLoadRequest request = CreateBuildIndexRequest(sceneIndex, mode, false);
            await ExecuteAsyncLoadWithoutActivation(request, activeCallBack, loadingCallBack, cancellationToken);
        }

        
        // 等待手动激活场景加载完成，并处理 0.9 后取消时的 Unity 激活限制。
        private static async UniTask DoLoadSceneAsyncWithoutActive(
            SceneLoadInfo loadInfo,
            AsyncOperation ao,
            Action<Action> activeCallBack, Action<float> loadingCallBack, CancellationToken cancellationToken)
        {
            ao.allowSceneActivation = false;
            float lastReportedProgress = -1f;

            // 当 allowSceneActivation = false 时，progress 最多到 0.9
            while (ao.progress < 0.9f)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ReportProgress(loadInfo, ao.progress, loadingCallBack, ref lastReportedProgress);
                
                await UniTask.Yield();
            }

            // 加载到了 0.9，即使已经 ready，也需要给用户回调去激活
            ReportProgress(loadInfo, 0.9f, loadingCallBack, ref lastReportedProgress);

            // 等待直到 isDone (其实这里不会 isDone 直到 allowSceneActivation = true)
            // 我们告知用户可以激活了
            bool activated = false;
            
            Action activateScene = () =>
            {
                if (activated) return;
                activated = true;
                ao.allowSceneActivation = true;
            };

            // 触发回调，告诉用户“准备好了，调用传入的 Action 来激活”
            activeCallBack?.Invoke(activateScene);

            if (activeCallBack == null)
            {
                activateScene();
            }

            // 等待激活完成
            while (!ao.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    activateScene();
                    await WaitAsyncOperationDone(ao);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // 激活中
                await UniTask.Yield();
            }
            
            // 完成
            ReportProgress(loadInfo, 1.0f, loadingCallBack, ref lastReportedProgress);
        }

        #endregion

        #region Additive Management

        /// <summary>
        /// 卸载由 SceneSystem 以 Additive 模式记录的场景。
        /// </summary>
        /// <param name="sceneName">目标场景名称。</param>
        public static async UniTask UnloadSceneAsync(string sceneName)
        {
            SceneUnloadInfo unloadInfo = CreateSceneNameUnloadInfo(sceneName);
            await ExecuteUnloadAsync(unloadInfo);
        }

        /// <summary>
        /// 卸载由 SceneSystem 以 Additive 模式记录的场景。
        /// </summary>
        /// <param name="sceneBuildIndex">目标场景 BuildIndex。</param>
        public static async UniTask UnloadSceneAsync(int sceneBuildIndex)
        {
            SceneUnloadInfo unloadInfo = CreateBuildIndexUnloadInfo(sceneBuildIndex);
            await ExecuteUnloadAsync(unloadInfo);
        }

        /// <summary>
        /// 将已加载场景设置为 Unity 当前活动场景。
        /// </summary>
        /// <param name="sceneName">目标场景名称。</param>
        public static void SetActiveScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be null, empty, or whitespace.", nameof(sceneName));
            }

            SetActiveLoadedScene(sceneName);
        }

        /// <summary>
        /// 将已加载场景设置为 Unity 当前活动场景。
        /// </summary>
        /// <param name="sceneBuildIndex">目标场景 BuildIndex。</param>
        public static void SetActiveScene(int sceneBuildIndex)
        {
            ValidateBuildIndexIsLoadable(sceneBuildIndex);
            SetActiveLoadedScene(GetSceneNameByBuildIndex(sceneBuildIndex));
        }

        #endregion

        #region Event Register

        /// <summary>
        /// 注册场景加载开始事件。
        /// </summary>
        /// <param name="handler">场景加载开始事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadStarted(Action<SceneLoadStartedEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.Started, handler);
        }

        /// <summary>
        /// 注册场景加载进度变化事件。
        /// </summary>
        /// <param name="handler">场景加载进度变化事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadProgressChanged(Action<SceneLoadProgressEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.ProgressChanged, handler);
        }

        /// <summary>
        /// 注册场景加载成功完成事件。
        /// </summary>
        /// <param name="handler">场景加载成功完成事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadSucceeded(Action<SceneLoadSucceededEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.Succeeded, handler);
        }

        /// <summary>
        /// 注册场景加载失败事件。
        /// </summary>
        /// <param name="handler">场景加载失败事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadFailed(Action<SceneLoadFailedEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.Failed, handler);
        }

        /// <summary>
        /// 注册场景加载取消事件。
        /// </summary>
        /// <param name="handler">场景加载取消事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadCancelled(Action<SceneLoadCancelledEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.Cancelled, handler);
        }

        /// <summary>
        /// 注册 Additive 场景卸载开始事件。
        /// </summary>
        /// <param name="handler">Additive 场景卸载开始事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadStarted(Action<SceneUnloadStartedEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.UnloadStarted, handler);
        }

        /// <summary>
        /// 注册 Additive 场景卸载成功完成事件。
        /// </summary>
        /// <param name="handler">Additive 场景卸载成功完成事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadSucceeded(Action<SceneUnloadSucceededEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.UnloadSucceeded, handler);
        }

        /// <summary>
        /// 注册 Additive 场景卸载失败事件。
        /// </summary>
        /// <param name="handler">Additive 场景卸载失败事件处理函数。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadFailed(Action<SceneUnloadFailedEventArgs> handler)
        {
            return eventModule.Register(SceneSystemEventType.UnloadFailed, handler);
        }

        #endregion

        // 创建基于场景名称的内部加载请求。
        private static SceneLoadRequest CreateSceneNameRequest(
            string sceneName,
            LoadSceneMode mode,
            bool allowSceneActivation)
        {
            var loadInfo = new SceneLoadInfo(
                ++nextLoadId,
                sceneName,
                SceneLoadTargetKind.SceneName,
                sceneName,
                null,
                mode,
                allowSceneActivation);

            return new SceneLoadRequest(loadInfo);
        }

        // 创建基于 BuildIndex 的内部加载请求。
        private static SceneLoadRequest CreateBuildIndexRequest(
            int buildIndex,
            LoadSceneMode mode,
            bool allowSceneActivation)
        {
            string target = buildIndex.ToString();
            var loadInfo = new SceneLoadInfo(
                ++nextLoadId,
                target,
                SceneLoadTargetKind.BuildIndex,
                null,
                buildIndex,
                mode,
                allowSceneActivation);

            return new SceneLoadRequest(loadInfo);
        }

        // 创建基于场景名称的内部卸载请求。
        private static SceneUnloadInfo CreateSceneNameUnloadInfo(string sceneName)
        {
            return new SceneUnloadInfo(
                ++nextUnloadId,
                sceneName,
                SceneLoadTargetKind.SceneName,
                sceneName,
                null);
        }

        // 创建基于 BuildIndex 的内部卸载请求。
        private static SceneUnloadInfo CreateBuildIndexUnloadInfo(int buildIndex)
        {
            return new SceneUnloadInfo(
                ++nextUnloadId,
                buildIndex.ToString(),
                SceneLoadTargetKind.BuildIndex,
                TryGetSceneNameByBuildIndex(buildIndex),
                buildIndex);
        }

        // 执行普通异步加载请求，并统一处理状态、进度、失败和取消。
        private static async UniTask ExecuteAsyncLoad(
            SceneLoadRequest request,
            Action<float> progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateLoadRequest(request);
                BeginLoad(request.LoadInfo);
                AsyncOperation asyncOperation = StartAsyncOperation(request);
                await DoLoadSceneAsync(request.LoadInfo, asyncOperation, progressCallback, cancellationToken);
                CompleteLoad(request.LoadInfo);
            }
            catch (OperationCanceledException)
            {
                CancelLoad(request.LoadInfo);
                throw;
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }

        // 执行手动激活异步加载请求，并统一处理状态、进度、失败和取消。
        private static async UniTask ExecuteAsyncLoadWithoutActivation(
            SceneLoadRequest request,
            Action<Action> activeCallBack,
            Action<float> progressCallback,
            CancellationToken cancellationToken)
        {
            try
            {
                ValidateLoadRequest(request);
                BeginLoad(request.LoadInfo);
                AsyncOperation asyncOperation = StartAsyncOperation(request);
                await DoLoadSceneAsyncWithoutActive(
                    request.LoadInfo,
                    asyncOperation,
                    activeCallBack,
                    progressCallback,
                    cancellationToken);
                CompleteLoad(request.LoadInfo);
            }
            catch (OperationCanceledException)
            {
                CancelLoad(request.LoadInfo);
                throw;
            }
            catch (Exception exception)
            {
                FailLoad(request.LoadInfo, exception);
                throw;
            }
        }

        // 执行 Additive 场景卸载请求，并统一处理卸载事件和失败。
        private static async UniTask ExecuteUnloadAsync(SceneUnloadInfo unloadInfo)
        {
            try
            {
                string sceneName = ValidateUnloadRequest(unloadInfo);
                eventModule.EventTrigger(
                    SceneSystemEventType.UnloadStarted,
                    new SceneUnloadStartedEventArgs(unloadInfo));

                AsyncOperation asyncOperation = SceneManager.UnloadSceneAsync(sceneName);
                if (asyncOperation == null)
                {
                    throw new InvalidOperationException($"Failed to start scene unload: {unloadInfo.Target}");
                }

                await WaitUnloadAsync(asyncOperation);
                loadedAdditiveScenes.Remove(sceneName);
                eventModule.EventTrigger(
                    SceneSystemEventType.UnloadSucceeded,
                    new SceneUnloadSucceededEventArgs(unloadInfo));
            }
            catch (Exception exception)
            {
                eventModule.EventTrigger(
                    SceneSystemEventType.UnloadFailed,
                    new SceneUnloadFailedEventArgs(unloadInfo, exception));
                throw;
            }
        }

        // 等待 Unity 卸载操作完成；卸载一旦开始就不支持框架级取消。
        private static async UniTask WaitUnloadAsync(AsyncOperation asyncOperation)
        {
            while (!asyncOperation.isDone)
            {
                await UniTask.Yield();
            }
        }

        // 启动 Unity 异步加载操作，并把启动失败转换为明确异常。
        private static AsyncOperation StartAsyncOperation(SceneLoadRequest request)
        {
            SceneLoadInfo loadInfo = request.LoadInfo;
            AsyncOperation asyncOperation = loadInfo.TargetKind == SceneLoadTargetKind.SceneName
                ? SceneManager.LoadSceneAsync(loadInfo.SceneName, loadInfo.Mode)
                : SceneManager.LoadSceneAsync(loadInfo.BuildIndex.GetValueOrDefault(), loadInfo.Mode);

            if (asyncOperation == null)
            {
                throw new InvalidOperationException(
                    $"Failed to start async scene load: {loadInfo.Target}");
            }

            return asyncOperation;
        }

        // 校验加载请求，并在通过后进入加载状态。
        private static void BeginValidatedLoad(SceneLoadRequest request)
        {
            ValidateLoadRequest(request);
            BeginLoad(request.LoadInfo);
        }

        // 根据加载目标类型分发到具体的 Build Settings 校验方法。
        private static void ValidateLoadRequest(SceneLoadRequest request)
        {
            SceneLoadInfo loadInfo = request.LoadInfo;
            if (loadInfo.TargetKind == SceneLoadTargetKind.SceneName)
            {
                ValidateSceneNameIsLoadable(loadInfo.SceneName);
                return;
            }

            ValidateBuildIndexIsLoadable(loadInfo.BuildIndex.GetValueOrDefault());
        }

        // 校验卸载请求是否指向 SceneSystem 记录的 Additive 场景。
        private static string ValidateUnloadRequest(SceneUnloadInfo unloadInfo)
        {
            string sceneName;
            if (unloadInfo.TargetKind == SceneLoadTargetKind.SceneName)
            {
                sceneName = unloadInfo.SceneName;
                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    throw new ArgumentException(
                        "Scene name cannot be null, empty, or whitespace.",
                        nameof(unloadInfo));
                }
            }
            else
            {
                int buildIndex = unloadInfo.BuildIndex.GetValueOrDefault();
                ValidateBuildIndexIsLoadable(buildIndex);
                sceneName = GetSceneNameByBuildIndex(buildIndex);
            }

            if (!loadedAdditiveScenes.ContainsKey(sceneName))
            {
                throw new InvalidOperationException(
                    $"Scene '{sceneName}' was not loaded additively by SceneSystem and cannot be unloaded by SceneSystem.");
            }

            return sceneName;
        }

        // 将当前已经加载的场景设置为 Unity 活动场景。
        private static void SetActiveLoadedScene(string sceneName)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException($"Scene '{sceneName}' is not loaded and cannot be set active.");
            }

            if (!SceneManager.SetActiveScene(scene))
            {
                throw new InvalidOperationException($"Failed to set active scene: {sceneName}");
            }
        }

        // 标记加载开始，并发布 Started 事件。
        private static void BeginLoad(SceneLoadInfo loadInfo)
        {
            if (IsLoading)
            {
                throw new InvalidOperationException(
                    $"SceneSystem is already loading '{CurrentLoadingTarget}'. Cannot load '{loadInfo.Target}' at the same time.");
            }

            IsLoading = true;
            CurrentLoadingTarget = loadInfo.Target;
            currentLoadInfo = loadInfo;
            eventModule.EventTrigger(
                SceneSystemEventType.Started,
                new SceneLoadStartedEventArgs(currentLoadInfo));
        }

        // 等待 Unity 异步操作彻底结束，不响应取消。
        private static async UniTask WaitAsyncOperationDone(AsyncOperation asyncOperation)
        {
            while (!asyncOperation.isDone)
            {
                await UniTask.Yield();
            }
        }

        #region 检查是否位于 Build Settings 中
        // 校验场景名称是否存在于启用的 Build Settings 场景中。
        private static void ValidateSceneNameIsLoadable(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name cannot be null, empty, or whitespace.", nameof(sceneName));
            }

            int sceneCount = SceneManager.sceneCountInBuildSettings;
            for (int i = 0; i < sceneCount; i++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                string buildSceneName = Path.GetFileNameWithoutExtension(scenePath);
                if (string.Equals(buildSceneName, sceneName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new ArgumentException(
                $"Scene '{sceneName}' is not enabled in Build Settings and cannot be loaded by SceneSystem.",
                nameof(sceneName));
        }

        // 校验 BuildIndex 是否指向启用的 Build Settings 场景。
        private static void ValidateBuildIndexIsLoadable(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buildIndex),
                    buildIndex,
                    "Scene buildIndex is not enabled in Build Settings and cannot be loaded by SceneSystem.");
            }

            string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            if (string.IsNullOrEmpty(scenePath))
            {
                throw new ArgumentException(
                    $"Scene buildIndex '{buildIndex}' does not resolve to a Build Settings scene.",
                    nameof(buildIndex));
            }
        }
        #endregion

        // 通过 BuildIndex 获取 Build Settings 中的场景名称。
        private static string GetSceneNameByBuildIndex(int buildIndex)
        {
            string sceneName = TryGetSceneNameByBuildIndex(buildIndex);
            if (string.IsNullOrEmpty(sceneName))
            {
                throw new ArgumentException(
                    $"Scene buildIndex '{buildIndex}' does not resolve to a Build Settings scene.",
                    nameof(buildIndex));
            }

            return sceneName;
        }

        // 尝试通过 BuildIndex 解析 Build Settings 中的场景名称。
        private static string TryGetSceneNameByBuildIndex(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return null;
            }

            string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
            return string.IsNullOrEmpty(scenePath) ? null : Path.GetFileNameWithoutExtension(scenePath);
        }

        // 发布去重后的加载进度，并同步调用本次请求的进度回调。
        private static void ReportProgress(
            SceneLoadInfo loadInfo,
            float progress,
            Action<float> progressCallback,
            ref float lastReportedProgress)
        {
            progress = Mathf.Clamp01(progress);
            if (Mathf.Approximately(progress, lastReportedProgress))
            {
                return;
            }

            lastReportedProgress = progress;
            progressCallback?.Invoke(progress);
            eventModule.EventTrigger(
                SceneSystemEventType.ProgressChanged,
                new SceneLoadProgressEventArgs(loadInfo, progress));
        }

        // 标记匹配的加载请求完成，并发布 Succeeded 事件。
        private static void CompleteLoad(SceneLoadInfo loadInfo)
        {
            if (currentLoadInfo.LoadId == loadInfo.LoadId)
            {
                TrackCompletedLoad(loadInfo);
                IsLoading = false;
                CurrentLoadingTarget = null;
                currentLoadInfo = default;
            }

            eventModule.EventTrigger(
                SceneSystemEventType.Succeeded,
                new SceneLoadSucceededEventArgs(loadInfo));
        }

        // 根据加载模式更新 SceneSystem 记录的 Additive 场景集合。
        private static void TrackCompletedLoad(SceneLoadInfo loadInfo)
        {
            if (loadInfo.Mode == LoadSceneMode.Single)
            {
                loadedAdditiveScenes.Clear();
                return;
            }

            string sceneName = GetSceneName(loadInfo);
            if (!string.IsNullOrEmpty(sceneName))
            {
                loadedAdditiveScenes[sceneName] = loadInfo;
            }
        }

        // 获取加载请求对应的场景名称。
        private static string GetSceneName(SceneLoadInfo loadInfo)
        {
            return loadInfo.TargetKind == SceneLoadTargetKind.SceneName
                ? loadInfo.SceneName
                : GetSceneNameByBuildIndex(loadInfo.BuildIndex.GetValueOrDefault());
        }

        // 标记匹配的加载请求失败，并发布 Failed 事件。
        private static void FailLoad(SceneLoadInfo loadInfo, Exception exception)
        {
            if (currentLoadInfo.LoadId == loadInfo.LoadId)
            {
                IsLoading = false;
                CurrentLoadingTarget = null;
                currentLoadInfo = default;
            }

            eventModule.EventTrigger(
                SceneSystemEventType.Failed,
                new SceneLoadFailedEventArgs(loadInfo, exception));
        }

        // 标记匹配的加载请求取消，并发布 Cancelled 事件。
        private static void CancelLoad(SceneLoadInfo loadInfo)
        {
            if (currentLoadInfo.LoadId == loadInfo.LoadId)
            {
                IsLoading = false;
                CurrentLoadingTarget = null;
                currentLoadInfo = default;
            }

            eventModule.EventTrigger(
                SceneSystemEventType.Cancelled,
                new SceneLoadCancelledEventArgs(loadInfo));
        }

        // SceneSystem 内部统一加载管线使用的纯请求数据。
        private readonly struct SceneLoadRequest
        {
            private readonly SceneLoadInfo loadInfo;

            internal SceneLoadRequest(SceneLoadInfo loadInfo)
            {
                this.loadInfo = loadInfo;
            }

            internal SceneLoadInfo LoadInfo => loadInfo;
        }
    }
}
