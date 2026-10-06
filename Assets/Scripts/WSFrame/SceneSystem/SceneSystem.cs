using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using WS_Modules.CustomEventSystem;

namespace WS_Modules.SceneModule
{
    /// <summary>兼容既有调用方的静态场景 API 门面；实际状态与操作由场景加载模块承接。</summary>
    public static class SceneSystem
    {
        #region 场景状态

        /// <summary>获取当前是否正在通过 SceneSystem 加载场景。</summary>
        public static bool IsLoading => SceneLoadModule.IsLoading;
        /// <summary>获取当前加载目标名称或 BuildIndex 字符串。</summary>
        public static string CurrentLoadingTarget => SceneLoadModule.CurrentLoadingTarget;
        /// <summary>获取 Unity 当前活动场景。</summary>
        public static Scene CurrentScene => SceneLoadModule.CurrentScene;
        /// <summary>获取 Unity 当前活动场景名称。</summary>
        public static string CurrentSceneName => SceneLoadModule.CurrentSceneName;
        /// <summary>获取 Unity 当前活动场景 BuildIndex。</summary>
        public static int CurrentSceneIndex => SceneLoadModule.CurrentSceneIndex;

        /// <summary>判断指定名称的场景是否由本门面以 Additive 模式加载。</summary>
        /// <param name="sceneName">目标场景名称。</param>
        /// <returns>场景处于 Additive 记录中时为 true。</returns>
        public static bool IsSceneLoaded(string sceneName) => SceneLoadModule.IsSceneLoaded(sceneName);

        /// <summary>获取由本门面跟踪的 Additive 场景名称快照。</summary>
        /// <returns>已记录场景名称。</returns>
        public static string[] GetLoadedAdditiveSceneNames() => SceneLoadModule.GetLoadedAdditiveSceneNames();

        #endregion

        #region 同步加载

        /// <summary>按名称同步加载场景。</summary>
        /// <param name="sceneName">Build Settings 场景名称。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        public static void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single) =>
            SceneLoadModule.LoadScene(sceneName, mode);

        /// <summary>按 BuildIndex 同步加载场景。</summary>
        /// <param name="sceneBuildIndex">Build Settings 场景索引。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode = LoadSceneMode.Single) =>
            SceneLoadModule.LoadScene(sceneBuildIndex, mode);

        /// <summary>按名称及详细参数同步加载场景。</summary>
        /// <param name="sceneName">Build Settings 场景名称。</param>
        /// <param name="loadSceneParameters">Unity 场景加载参数。</param>
        /// <returns>加载得到的 Unity Scene。</returns>
        public static Scene LoadScene(string sceneName, LoadSceneParameters loadSceneParameters) =>
            SceneLoadModule.LoadScene(sceneName, loadSceneParameters);

        /// <summary>按 BuildIndex 及详细参数同步加载场景。</summary>
        /// <param name="sceneBuildIndex">Build Settings 场景索引。</param>
        /// <param name="loadSceneParameters">Unity 场景加载参数。</param>
        /// <returns>加载得到的 Unity Scene。</returns>
        public static Scene LoadScene(int sceneBuildIndex, LoadSceneParameters loadSceneParameters) =>
            SceneLoadModule.LoadScene(sceneBuildIndex, loadSceneParameters);

        #endregion

        #region 异步场景操作

        // 旧门面的异步入口继续委托给 SceneManager 模块。
        /// <summary>按名称异步加载内置场景并反馈去重后的进度。</summary>
        /// <param name="sceneName">Build Settings 场景名称。</param>
        /// <param name="callBack">0 至 1 的进度回调。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="cancellationToken">取消等待并向底层加载协作传递的令牌。</param>
        public static UniTask LoadSceneAsync(string sceneName, Action<float> callBack = null,
            LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default) =>
            SceneLoadModule.LoadSceneAsync(sceneName, callBack, mode, cancellationToken);

        /// <summary>按 BuildIndex 异步加载内置场景并反馈去重后的进度。</summary>
        /// <param name="sceneBuildIndex">Build Settings 场景索引。</param>
        /// <param name="callBack">0 至 1 的进度回调。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="cancellationToken">取消等待并向底层加载协作传递的令牌。</param>
        public static UniTask LoadSceneAsync(int sceneBuildIndex, Action<float> callBack = null,
            LoadSceneMode mode = LoadSceneMode.Single, CancellationToken cancellationToken = default) =>
            SceneLoadModule.LoadSceneAsync(sceneBuildIndex, callBack, mode, cancellationToken);

        /// <summary>在调用方显式触发时激活按名称异步加载的场景。</summary>
        /// <param name="sceneName">Build Settings 场景名称。</param>
        /// <param name="activeCallBack">接收场景激活操作的回调。</param>
        /// <param name="callBack">加载进度回调。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="cancellationToken">协作式取消令牌。</param>
        public static UniTask LoadSceneAsyncWithoutActive(string sceneName, Action<Action> activeCallBack,
            Action<float> callBack = null, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default) =>
            SceneLoadModule.LoadSceneAsyncWithoutActive(sceneName, activeCallBack, callBack, mode, cancellationToken);

        /// <summary>在调用方显式触发时激活按 BuildIndex 异步加载的场景。</summary>
        /// <param name="sceneIndex">Build Settings 场景索引。</param>
        /// <param name="activeCallBack">接收场景激活操作的回调。</param>
        /// <param name="callBack">加载进度回调。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="cancellationToken">协作式取消令牌。</param>
        public static UniTask LoadSceneAsyncWithoutActive(int sceneIndex, Action<Action> activeCallBack,
            Action<float> callBack = null, LoadSceneMode mode = LoadSceneMode.Single,
            CancellationToken cancellationToken = default) =>
            SceneLoadModule.LoadSceneAsyncWithoutActive(sceneIndex, activeCallBack, callBack, mode, cancellationToken);

        // 旧门面的 Additive 查询和卸载仍由 SceneLoadModule 持有记录。
        /// <summary>卸载由本门面记录的 Additive 场景。</summary>
        /// <param name="sceneName">目标场景名称。</param>
        public static UniTask UnloadSceneAsync(string sceneName) => SceneLoadModule.UnloadSceneAsync(sceneName);

        /// <summary>按 BuildIndex 卸载由本门面记录的 Additive 场景。</summary>
        /// <param name="sceneBuildIndex">Build Settings 场景索引。</param>
        public static UniTask UnloadSceneAsync(int sceneBuildIndex) => SceneLoadModule.UnloadSceneAsync(sceneBuildIndex);

        /// <summary>将已加载内置场景设为活动场景。</summary>
        /// <param name="sceneName">目标场景名称。</param>
        public static void SetActiveScene(string sceneName) => SceneLoadModule.SetActiveScene(sceneName);

        /// <summary>将已加载内置场景设为活动场景。</summary>
        /// <param name="sceneBuildIndex">Build Settings 场景索引。</param>
        public static void SetActiveScene(int sceneBuildIndex) => SceneLoadModule.SetActiveScene(sceneBuildIndex);

        #endregion

        #region 事件订阅

        /// <summary>订阅内置场景加载开始事件。</summary>
        /// <param name="handler">开始事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadStarted(Action<SceneLoadStartedEventArgs> handler) =>
            SceneLoadModule.RegisterLoadStarted(handler);
        /// <summary>订阅内置场景加载进度事件。</summary>
        /// <param name="handler">进度事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadProgressChanged(Action<SceneLoadProgressEventArgs> handler) =>
            SceneLoadModule.RegisterLoadProgressChanged(handler);
        /// <summary>订阅内置场景加载成功事件。</summary>
        /// <param name="handler">成功事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadSucceeded(Action<SceneLoadSucceededEventArgs> handler) =>
            SceneLoadModule.RegisterLoadSucceeded(handler);
        /// <summary>订阅内置场景加载失败事件。</summary>
        /// <param name="handler">失败事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadFailed(Action<SceneLoadFailedEventArgs> handler) =>
            SceneLoadModule.RegisterLoadFailed(handler);
        /// <summary>订阅内置场景加载取消事件。</summary>
        /// <param name="handler">取消事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterLoadCancelled(Action<SceneLoadCancelledEventArgs> handler) =>
            SceneLoadModule.RegisterLoadCancelled(handler);
        /// <summary>订阅内置 Additive 场景卸载开始事件。</summary>
        /// <param name="handler">开始事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadStarted(Action<SceneUnloadStartedEventArgs> handler) =>
            SceneLoadModule.RegisterUnloadStarted(handler);
        /// <summary>订阅内置 Additive 场景卸载成功事件。</summary>
        /// <param name="handler">成功事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadSucceeded(Action<SceneUnloadSucceededEventArgs> handler) =>
            SceneLoadModule.RegisterUnloadSucceeded(handler);
        /// <summary>订阅内置 Additive 场景卸载失败事件。</summary>
        /// <param name="handler">失败事件处理器。</param>
        /// <returns>注销句柄。</returns>
        public static IUnRegister RegisterUnloadFailed(Action<SceneUnloadFailedEventArgs> handler) =>
            SceneLoadModule.RegisterUnloadFailed(handler);

        #endregion
    }
}
