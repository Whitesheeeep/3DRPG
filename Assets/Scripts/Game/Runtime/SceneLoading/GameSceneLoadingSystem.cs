using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;
using WS_Modules.LogModule;
using WS_Modules.SceneModule;

namespace RPG.Game.Loading
{
    /// <summary>执行唯一组合式场景流程，串起验证、Addressables 加载、准备任务和最终状态。</summary>
    public sealed class GameSceneLoadingSystem : AbstractSystem
    {
        #region 当前流程状态

        // 依赖字段：同一模块持有 Additive 加载句柄，提供成对卸载。
        private readonly AddressableSceneLoadModule addressableSceneLoadModule = new();
        // 依赖字段：加载 UI 通过接口展示状态，不让运行系统持有具体窗口对象。
        private readonly ISceneLoadingPresentation sceneLoadingPresentation;
        // 事件字段：同一流程类型的对外通知使用具名枚举键和可配对注销句柄。
        private readonly EventCenterModule<int> sceneLoadingEventCenter = new();
        // 请求状态覆盖场景加载叶子和全部后置准备任务的生命周期。
        private bool isLoading;
        private string currentSceneId;

        #endregion

        #region 流程状态

        /// <summary>获取完整场景流程是否正在执行。</summary>
        public bool IsLoading => isLoading;
        /// <summary>获取当前流程对应的目标 SceneId。</summary>
        public string CurrentSceneId => currentSceneId;
        /// <summary>获取当前运行或最近结束流程的不可变快照。</summary>
        public SceneLoadExecutionSnapshot CurrentSnapshot { get; private set; }

        /// <summary>判断场景入口是否已经属于目标配置的统一流程加载实例。</summary>
        /// <param name="sceneId">新入口配置的稳定 ID。</param>
        /// <param name="scene">运行此入口的 Unity 场景实例。</param>
        /// <returns>当前流程已开始加载此场景时返回 true。</returns>
        public bool IsSceneJoiningCurrentFlow(string sceneId, Scene scene) =>
            (isLoading && string.Equals(sceneId, currentSceneId, StringComparison.Ordinal)) ||
            addressableSceneLoadModule.OwnsScene(sceneId, scene);

        #endregion

        #region 构造

        /// <summary>创建适用于未注入界面场景的加载执行器。</summary>
        public GameSceneLoadingSystem()
        {
        }

        /// <summary>创建由 RPG 层加载窗口适配器驱动显示的场景系统。</summary>
        /// <param name="sceneLoadingPresentation">负责窗口展示生命周期的适配器。</param>
        public GameSceneLoadingSystem(ISceneLoadingPresentation sceneLoadingPresentation)
        {
            this.sceneLoadingPresentation = sceneLoadingPresentation;
        }

        #endregion

        #region 对外事件订阅

        /// <summary>订阅完整进度快照以及流程状态变化。</summary>
        /// <param name="handler">接收场景加载状态的回调。</param>
        /// <returns>用于配对注销的事件句柄。</returns>
        public IUnRegister RegisterSnapshotChanged(Action<SceneLoadingEventArgs> handler) =>
            sceneLoadingEventCenter.Register((int)E_SceneLoadingEventType.SnapshotChanged, handler);

        /// <summary>订阅任务树各引用位置的进度或生命周期变化。</summary>
        /// <param name="handler">接收任务状态的回调。</param>
        /// <returns>用于配对注销的事件句柄。</returns>
        public IUnRegister RegisterTaskChanged(Action<SceneLoadingTaskEventArgs> handler) =>
            sceneLoadingEventCenter.Register((int)E_SceneLoadingEventType.TaskChanged, handler);

        /// <summary>订阅完整加载流程成功通知。</summary>
        /// <param name="handler">接收终态快照的回调。</param>
        /// <returns>用于配对注销的事件句柄。</returns>
        public IUnRegister RegisterCompleted(Action<SceneLoadingEventArgs> handler) =>
            sceneLoadingEventCenter.Register((int)E_SceneLoadingEventType.Completed, handler);

        /// <summary>订阅完整加载流程失败通知。</summary>
        /// <param name="handler">接收失败终态快照的回调。</param>
        /// <returns>用于配对注销的事件句柄。</returns>
        public IUnRegister RegisterFailed(Action<SceneLoadingEventArgs> handler) =>
            sceneLoadingEventCenter.Register((int)E_SceneLoadingEventType.Failed, handler);

        /// <summary>订阅完整加载流程取消通知。</summary>
        /// <param name="handler">接收取消终态快照的回调。</param>
        /// <returns>用于配对注销的事件句柄。</returns>
        public IUnRegister RegisterCancelled(Action<SceneLoadingEventArgs> handler) =>
            sceneLoadingEventCenter.Register((int)E_SceneLoadingEventType.Cancelled, handler);

        #endregion

        #region 系统生命周期

        /// <summary>清除新一轮架构初始化时残留的运行标记。</summary>
        protected override void OnInit()
        {
            isLoading = false;
            currentSceneId = null;
            CurrentSnapshot = null;
            WSLog.Log("[GameSceneLoadingSystem] 场景加载系统已初始化。");
        }

        #endregion

        #region 场景流程入口

        /// <summary>按稳定 ID 加载目标 Addressables 场景并执行其任务树。</summary>
        /// <param name="sceneId">目标配置稳定 ID。</param>
        /// <param name="cancellationToken">请求方协作式取消令牌。</param>
        /// <returns>所有任务成功后已激活的目标场景。</returns>
        public UniTask<Scene> LoadAsync(string sceneId, CancellationToken cancellationToken = default)
        {
            SceneLoadConfig config = GetSceneConfig(sceneId);
            return ExecuteAsync(config, initializesCurrentScene: false, default, cancellationToken);
        }

        /// <summary>为直接打开的当前场景执行同一任务树，并使场景加载节点验证而不重复加载。</summary>
        /// <param name="sceneId">当前场景配置稳定 ID。</param>
        /// <param name="cancellationToken">调用方协作式取消令牌。</param>
        /// <returns>所有任务成功后继续活动的当前场景。</returns>
        public UniTask<Scene> InitializeCurrentSceneAsync(
            string sceneId,
            CancellationToken cancellationToken = default)
        {
            SceneLoadConfig config = GetSceneConfig(sceneId);
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !string.Equals(scene.name, config.SceneName, StringComparison.Ordinal))
            {
                var exception = new InvalidOperationException(
                    $"[GameSceneLoadingSystem] 当前场景与配置不匹配，sceneId={sceneId}，configured={config.SceneName}，current={scene.name}。");
                WSLog.LogError(exception.Message);
                throw exception;
            }
            return ExecuteAsync(config, initializesCurrentScene: true, scene, cancellationToken);
        }

        /// <summary>卸载此流程加载模块持有的 Additive 场景并释放其 Addressables 句柄。</summary>
        /// <param name="scene">此前由统一场景流程加载的 Scene 实例。</param>
        /// <param name="cancellationToken">在卸载开始前检查的协作式取消令牌。</param>
        /// <returns>Unity 场景卸载及 Addressables 句柄释放全部完成后的任务。</returns>
        public UniTask UnloadSceneAsync(Scene scene, CancellationToken cancellationToken = default) =>
            addressableSceneLoadModule.UnloadAsync(scene, cancellationToken);

        #endregion

        #region 流程执行校验

        // 执行顺序：互斥检查、结构校验、任务树执行和场景就绪确认。
        /// <summary>执行配置根节点并附加当前任务路径处理异常。</summary>
        /// <param name="config">要执行的场景配置。</param>
        /// <param name="initializesCurrentScene">是否复用已经激活的当前场景。</param>
        /// <param name="currentScene">初始化当前场景模式下传入的 Scene。</param>
        /// <param name="cancellationToken">流程协作式取消令牌。</param>
        /// <returns>根节点成功后的目标 Scene。</returns>
        private async UniTask<Scene> ExecuteAsync(
            SceneLoadConfig config,
            bool initializesCurrentScene,
            Scene currentScene,
            CancellationToken cancellationToken)
        {
            if (isLoading)
            {
                string message = $"[GameSceneLoadingSystem] 正在加载 '{currentSceneId}'，拒绝同时开始 '{config.SceneId}'。";
                WSLog.LogWarning(message);
                throw new InvalidOperationException(message);
            }

            SceneLoadValidationResult validation = SceneLoadValidator.Validate(
                config, initializesCurrentScene, GetDatabase());
            if (!validation.IsValid)
            {
                string validationMessage = string.Join("；", validation.Issues);
                WSLog.LogError($"[GameSceneLoadingSystem] 场景配置校验失败，sceneId={config.SceneId}，issues={validationMessage}。");
                throw new InvalidOperationException(
                    $"[GameSceneLoadingSystem] 场景配置无效，sceneId={config.SceneId}：{validationMessage}");
            }

            isLoading = true;
            currentSceneId = config.SceneId;
            var progressTracker = new SceneLoadProgressTracker(config);
            progressTracker.SnapshotChanged += HandleSnapshotChanged;
            progressTracker.TaskChanged += HandleTaskChanged;
            var context = new SceneLoadContext(config, addressableSceneLoadModule,
                initializesCurrentScene, progressTracker);
            WSLog.Log($"[GameSceneLoadingSystem] 开始场景流程，sceneId={config.SceneId}，currentScene={initializesCurrentScene}。");
            try
            {
                // 当前场景初始化也在受管异常范围内发布就绪事件，避免外部订阅异常遗留互斥状态。
                if (initializesCurrentScene) context.SetTargetScene(currentScene);
                HandleSnapshotChanged(progressTracker.Snapshot);
                if (sceneLoadingPresentation != null)
                {
                    await sceneLoadingPresentation.PrepareAsync(progressTracker.Snapshot, cancellationToken);
                }

                WSLog.Log($"[GameSceneLoadingSystem] 开始根任务，sceneId={config.SceneId}，taskPath={config.RootTask.name}。");
                await context.ExecuteRootTaskAsync(config.RootTask, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!context.TargetSceneReady)
                    throw new InvalidOperationException(
                        $"[GameSceneLoadingSystem] 任务结束但场景未就绪，sceneId={config.SceneId}。");

                progressTracker.Succeed();
                WSLog.Log($"[GameSceneLoadingSystem] 根任务完成，sceneId={config.SceneId}。");
                WSLog.Log($"[GameSceneLoadingSystem] 完整场景流程成功，sceneId={config.SceneId}，scene={context.ActiveScene.name}。");
                if (sceneLoadingPresentation != null)
                {
                    try
                    {
                        await sceneLoadingPresentation.CompleteAsync(progressTracker.Snapshot);
                    }
                    catch (Exception presentationException)
                    {
                        // 场景与准备任务已成功；窗口动画错误不能把已成功的加载伪报为失败。
                        WSLog.LogError($"[GameSceneLoadingSystem] 场景已成功但加载窗口收尾失败，sceneId={config.SceneId}，exception={presentationException}");
                    }
                }
                return context.ActiveScene;
            }
            catch (OperationCanceledException)
            {
                progressTracker.Cancel();
                PresentTerminalState(progressTracker.Snapshot, isCancellation: true);
                WSLog.LogWarning($"[GameSceneLoadingSystem] 场景流程已取消，sceneId={config.SceneId}。");
                throw;
            }
            catch (Exception exception)
            {
                progressTracker.Fail(exception);
                PresentTerminalState(progressTracker.Snapshot, isCancellation: false);
                WSLog.LogError($"[GameSceneLoadingSystem] 场景流程失败，sceneId={config.SceneId}，exception={exception}");
                throw;
            }
            finally
            {
                progressTracker.SnapshotChanged -= HandleSnapshotChanged;
                progressTracker.TaskChanged -= HandleTaskChanged;
                currentSceneId = null;
                isLoading = false;
            }
        }

        // 校验依赖：数据库只由 ConfigInstaller 负责注入。
        /// <summary>按稳定 ID 读取场景配置，并在配置查找边界补充业务上下文。</summary>
        /// <param name="sceneId">目标场景稳定标识。</param>
        /// <returns>数据库中的场景配置。</returns>
        private static SceneLoadConfig GetSceneConfig(string sceneId)
        {
            try
            {
                return GetDatabase().GetConfig(sceneId);
            }
            catch (Exception exception)
            {
                WSLog.LogError($"[GameSceneLoadingSystem] 查询场景配置失败，sceneId={sceneId}，exception={exception}");
                throw;
            }
        }

        /// <summary>读取 ConfigInstaller 已注册的场景数据库。</summary>
        /// <returns>唯一已注册的场景数据库。</returns>
        private static SceneLoadDatabase GetDatabase() => SceneLoadDatabaseRegistry.Database ??
            throw new InvalidOperationException("[GameSceneLoadingSystem] SceneLoadDatabaseConfigProvider 尚未注册 SceneLoadDatabase。");

        /// <summary>保留并广播最新快照，再将其应用于已打开的展示层。</summary>
        /// <param name="snapshot">当前流程快照。</param>
        private void HandleSnapshotChanged(SceneLoadExecutionSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            var eventArgs = new SceneLoadingEventArgs(snapshot);
            PublishEventSafely(E_SceneLoadingEventType.SnapshotChanged, eventArgs);
            if (snapshot.State != E_SceneLoadExecutionState.Loading)
            {
                E_SceneLoadingEventType terminalEvent = snapshot.State switch
                {
                    E_SceneLoadExecutionState.Succeeded => E_SceneLoadingEventType.Completed,
                    E_SceneLoadExecutionState.Failed => E_SceneLoadingEventType.Failed,
                    _ => E_SceneLoadingEventType.Cancelled
                };
                PublishEventSafely(terminalEvent, eventArgs);
            }
            try
            {
                sceneLoadingPresentation?.Present(snapshot);
            }
            catch (Exception presentationException)
            {
                // 显示刷新由 UI 适配层隔离；渲染问题不能中止已经启动的资源流程。
                WSLog.LogError($"[GameSceneLoadingSystem] 进度快照无法更新加载窗口，sceneId={snapshot.SceneId}，exception={presentationException}");
            }
        }

        /// <summary>发布任务树中一个执行引用位置的进度或状态变化。</summary>
        /// <param name="task">任务状态快照。</param>
        private void HandleTaskChanged(SceneLoadTaskSnapshot task)
        {
            try
            {
                sceneLoadingEventCenter.EventTrigger(
                    (int)E_SceneLoadingEventType.TaskChanged,
                    new SceneLoadingTaskEventArgs(task));
            }
            catch (Exception listenerException)
            {
                WSLog.LogError($"[GameSceneLoadingSystem] 任务进度订阅者发生异常，taskPath={task.ReferencePath}，exception={listenerException}");
            }
        }

        /// <summary>发布公开流程事件并隔离订阅者异常，保证加载任务不被观察者中断。</summary>
        /// <param name="eventType">具名流程事件类型。</param>
        /// <param name="eventArgs">包含一致快照的事件数据。</param>
        private void PublishEventSafely(E_SceneLoadingEventType eventType, SceneLoadingEventArgs eventArgs)
        {
            try
            {
                sceneLoadingEventCenter.EventTrigger((int)eventType, eventArgs);
            }
            catch (Exception listenerException)
            {
                WSLog.LogError($"[GameSceneLoadingSystem] 场景状态订阅者发生异常，sceneId={eventArgs.Snapshot.SceneId}，event={eventType}，exception={listenerException}");
            }
        }

        /// <summary>隔离失败或取消界面自身异常，保证原始加载结果继续传播。</summary>
        /// <param name="snapshot">流程终态快照。</param>
        /// <param name="isCancellation">是否为协作式取消。</param>
        private void PresentTerminalState(SceneLoadExecutionSnapshot snapshot, bool isCancellation)
        {
            try
            {
                if (isCancellation) sceneLoadingPresentation?.PresentCancellation(snapshot);
                else sceneLoadingPresentation?.PresentFailure(snapshot);
            }
            catch (Exception presentationException)
            {
                WSLog.LogError($"[GameSceneLoadingSystem] 终态加载界面更新失败，sceneId={snapshot.SceneId}，state={snapshot.State}，exception={presentationException}");
            }
        }

        #endregion
    }
}
