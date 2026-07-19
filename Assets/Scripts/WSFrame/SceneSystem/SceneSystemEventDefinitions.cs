using System;
using UnityEngine.SceneManagement;

namespace WS_Modules.SceneModule
{
    /// <summary>
    /// SceneSystem 内部事件模块使用的事件类型。
    /// </summary>
    public enum SceneSystemEventType
    {
        /// <summary>
        /// 场景加载开始。
        /// </summary>
        Started,

        /// <summary>
        /// 场景加载进度发生变化。
        /// </summary>
        ProgressChanged,

        /// <summary>
        /// 场景加载成功完成。
        /// </summary>
        Succeeded,

        /// <summary>
        /// 场景加载失败。
        /// </summary>
        Failed,

        /// <summary>
        /// 场景加载被取消。
        /// </summary>
        Cancelled,

        /// <summary>
        /// Additive 场景卸载开始。
        /// </summary>
        UnloadStarted,

        /// <summary>
        /// Additive 场景卸载成功完成。
        /// </summary>
        UnloadSucceeded,

        /// <summary>
        /// Additive 场景卸载失败。
        /// </summary>
        UnloadFailed
    }

    /// <summary>
    /// 场景加载目标的输入类型。
    /// </summary>
    public enum SceneLoadTargetKind
    {
        /// <summary>
        /// 通过场景名称加载。
        /// </summary>
        SceneName,

        /// <summary>
        /// 通过 BuildIndex 加载。
        /// </summary>
        BuildIndex
    }

    /// <summary>
    /// 描述一次 SceneSystem 场景加载请求的基础信息。
    /// </summary>
    public readonly struct SceneLoadInfo
    {
        /// <summary>
        /// 创建一次场景加载请求的基础信息。
        /// </summary>
        /// <param name="loadId">本次加载请求的递增编号。</param>
        /// <param name="target">用于显示的目标场景名称或 BuildIndex 字符串。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="allowSceneActivation">是否允许加载完成后自动激活场景。</param>
        public SceneLoadInfo(int loadId, string target, LoadSceneMode mode, bool allowSceneActivation)
        {
            bool isBuildIndex = int.TryParse(target, out int buildIndex);

            LoadId = loadId;
            Target = target;
            TargetKind = isBuildIndex ? SceneLoadTargetKind.BuildIndex : SceneLoadTargetKind.SceneName;
            SceneName = isBuildIndex ? null : target;
            BuildIndex = isBuildIndex ? buildIndex : (int?)null;
            Mode = mode;
            AllowSceneActivation = allowSceneActivation;
        }

        /// <summary>
        /// 创建一次场景加载请求的基础信息。
        /// </summary>
        /// <param name="loadId">本次加载请求的递增编号。</param>
        /// <param name="target">用于显示的目标场景名称或 BuildIndex 字符串。</param>
        /// <param name="targetKind">场景加载目标的输入类型。</param>
        /// <param name="sceneName">目标场景名称；通过 BuildIndex 加载时为空。</param>
        /// <param name="buildIndex">目标场景 BuildIndex；通过场景名称加载时为空。</param>
        /// <param name="mode">Unity 场景加载模式。</param>
        /// <param name="allowSceneActivation">是否允许加载完成后自动激活场景。</param>
        public SceneLoadInfo(
            int loadId,
            string target,
            SceneLoadTargetKind targetKind,
            string sceneName,
            int? buildIndex,
            LoadSceneMode mode,
            bool allowSceneActivation)
        {
            LoadId = loadId;
            Target = target;
            TargetKind = targetKind;
            SceneName = sceneName;
            BuildIndex = buildIndex;
            Mode = mode;
            AllowSceneActivation = allowSceneActivation;
        }

        /// <summary>
        /// 本次加载请求的递增编号。
        /// </summary>
        public int LoadId { get; }

        /// <summary>
        /// 用于显示的目标场景名称或 BuildIndex 字符串。
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// 场景加载目标的输入类型。
        /// </summary>
        public SceneLoadTargetKind TargetKind { get; }

        /// <summary>
        /// 目标场景名称；通过 BuildIndex 加载时为空。
        /// </summary>
        public string SceneName { get; }

        /// <summary>
        /// 目标场景 BuildIndex；通过场景名称加载时为空。
        /// </summary>
        public int? BuildIndex { get; }

        /// <summary>
        /// Unity 场景加载模式。
        /// </summary>
        public LoadSceneMode Mode { get; }

        /// <summary>
        /// 是否允许加载完成后自动激活场景。
        /// </summary>
        public bool AllowSceneActivation { get; }
    }

    /// <summary>
    /// 场景加载开始事件参数。
    /// </summary>
    public readonly struct SceneLoadStartedEventArgs
    {
        /// <summary>
        /// 创建场景加载开始事件参数。
        /// </summary>
        /// <param name="loadInfo">本次加载请求信息。</param>
        public SceneLoadStartedEventArgs(SceneLoadInfo loadInfo)
        {
            LoadInfo = loadInfo;
        }

        /// <summary>
        /// 本次加载请求信息。
        /// </summary>
        public SceneLoadInfo LoadInfo { get; }
    }

    /// <summary>
    /// 场景加载进度变化事件参数。
    /// </summary>
    public readonly struct SceneLoadProgressEventArgs
    {
        /// <summary>
        /// 创建场景加载进度变化事件参数。
        /// </summary>
        /// <param name="loadInfo">本次加载请求信息。</param>
        /// <param name="progress">当前加载进度，范围为 0 到 1。</param>
        public SceneLoadProgressEventArgs(SceneLoadInfo loadInfo, float progress)
        {
            LoadInfo = loadInfo;
            Progress = progress;
        }

        /// <summary>
        /// 本次加载请求信息。
        /// </summary>
        public SceneLoadInfo LoadInfo { get; }

        /// <summary>
        /// 当前加载进度，范围为 0 到 1。
        /// </summary>
        public float Progress { get; }
    }

    /// <summary>
    /// 场景加载成功完成事件参数。
    /// </summary>
    public readonly struct SceneLoadSucceededEventArgs
    {
        /// <summary>
        /// 创建场景加载成功完成事件参数。
        /// </summary>
        /// <param name="loadInfo">本次加载请求信息。</param>
        public SceneLoadSucceededEventArgs(SceneLoadInfo loadInfo)
        {
            LoadInfo = loadInfo;
        }

        /// <summary>
        /// 本次加载请求信息。
        /// </summary>
        public SceneLoadInfo LoadInfo { get; }
    }

    /// <summary>
    /// 场景加载失败事件参数。
    /// </summary>
    public readonly struct SceneLoadFailedEventArgs
    {
        /// <summary>
        /// 创建场景加载失败事件参数。
        /// </summary>
        /// <param name="loadInfo">本次加载请求信息。</param>
        /// <param name="exception">加载失败时捕获到的异常。</param>
        public SceneLoadFailedEventArgs(SceneLoadInfo loadInfo, Exception exception)
        {
            LoadInfo = loadInfo;
            Exception = exception;
        }

        /// <summary>
        /// 本次加载请求信息。
        /// </summary>
        public SceneLoadInfo LoadInfo { get; }

        /// <summary>
        /// 加载失败时捕获到的异常。
        /// </summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// 场景加载取消事件参数。
    /// </summary>
    public readonly struct SceneLoadCancelledEventArgs
    {
        /// <summary>
        /// 创建场景加载取消事件参数。
        /// </summary>
        /// <param name="loadInfo">本次加载请求信息。</param>
        public SceneLoadCancelledEventArgs(SceneLoadInfo loadInfo)
        {
            LoadInfo = loadInfo;
        }

        /// <summary>
        /// 本次加载请求信息。
        /// </summary>
        public SceneLoadInfo LoadInfo { get; }
    }

    /// <summary>
    /// 描述一次 SceneSystem Additive 场景卸载请求的基础信息。
    /// </summary>
    public readonly struct SceneUnloadInfo
    {
        /// <summary>
        /// 创建一次 Additive 场景卸载请求的基础信息。
        /// </summary>
        /// <param name="unloadId">本次卸载请求的递增编号。</param>
        /// <param name="target">用于显示的目标场景名称或 BuildIndex 字符串。</param>
        /// <param name="targetKind">场景卸载目标的输入类型。</param>
        /// <param name="sceneName">目标场景名称；通过 BuildIndex 卸载且解析失败时为空。</param>
        /// <param name="buildIndex">目标场景 BuildIndex；通过场景名称卸载时为空。</param>
        public SceneUnloadInfo(
            int unloadId,
            string target,
            SceneLoadTargetKind targetKind,
            string sceneName,
            int? buildIndex)
        {
            UnloadId = unloadId;
            Target = target;
            TargetKind = targetKind;
            SceneName = sceneName;
            BuildIndex = buildIndex;
        }

        /// <summary>
        /// 本次卸载请求的递增编号。
        /// </summary>
        public int UnloadId { get; }

        /// <summary>
        /// 用于显示的目标场景名称或 BuildIndex 字符串。
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// 场景卸载目标的输入类型。
        /// </summary>
        public SceneLoadTargetKind TargetKind { get; }

        /// <summary>
        /// 目标场景名称；通过 BuildIndex 卸载且解析失败时为空。
        /// </summary>
        public string SceneName { get; }

        /// <summary>
        /// 目标场景 BuildIndex；通过场景名称卸载时为空。
        /// </summary>
        public int? BuildIndex { get; }
    }

    /// <summary>
    /// Additive 场景卸载开始事件参数。
    /// </summary>
    public readonly struct SceneUnloadStartedEventArgs
    {
        /// <summary>
        /// 创建 Additive 场景卸载开始事件参数。
        /// </summary>
        /// <param name="unloadInfo">本次卸载请求信息。</param>
        public SceneUnloadStartedEventArgs(SceneUnloadInfo unloadInfo)
        {
            UnloadInfo = unloadInfo;
        }

        /// <summary>
        /// 本次卸载请求信息。
        /// </summary>
        public SceneUnloadInfo UnloadInfo { get; }
    }

    /// <summary>
    /// Additive 场景卸载成功完成事件参数。
    /// </summary>
    public readonly struct SceneUnloadSucceededEventArgs
    {
        /// <summary>
        /// 创建 Additive 场景卸载成功完成事件参数。
        /// </summary>
        /// <param name="unloadInfo">本次卸载请求信息。</param>
        public SceneUnloadSucceededEventArgs(SceneUnloadInfo unloadInfo)
        {
            UnloadInfo = unloadInfo;
        }

        /// <summary>
        /// 本次卸载请求信息。
        /// </summary>
        public SceneUnloadInfo UnloadInfo { get; }
    }

    /// <summary>
    /// Additive 场景卸载失败事件参数。
    /// </summary>
    public readonly struct SceneUnloadFailedEventArgs
    {
        /// <summary>
        /// 创建 Additive 场景卸载失败事件参数。
        /// </summary>
        /// <param name="unloadInfo">本次卸载请求信息。</param>
        /// <param name="exception">卸载失败时捕获到的异常。</param>
        public SceneUnloadFailedEventArgs(SceneUnloadInfo unloadInfo, Exception exception)
        {
            UnloadInfo = unloadInfo;
            Exception = exception;
        }

        /// <summary>
        /// 本次卸载请求信息。
        /// </summary>
        public SceneUnloadInfo UnloadInfo { get; }

        /// <summary>
        /// 卸载失败时捕获到的异常。
        /// </summary>
        public Exception Exception { get; }
    }
}
