using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using WS_Modules.LogModule;

namespace WS_Modules.SceneModule
{
    /// <summary>在一个完整加载请求内共享配置、当前场景和执行路径的运行时上下文。</summary>
    public sealed class SceneLoadContext
    {
        #region 运行时上下文状态

        // 每个并行分支拥有独立路径列表，避免异步完成顺序覆盖诊断路径。
        private readonly List<SceneLoadTask> activeTaskPath;
        // 目标场景与就绪状态在所有分支之间共享。
        private readonly SharedSceneLoadState sharedState;
        private readonly SceneLoadProgressTracker progressTracker;
        // 当前任务引用在本次树执行中的独立位置标识，便于在失败时追踪。
        private readonly string referencePath;
        private readonly SceneLoadTask currentTask;

        #endregion

        #region 属性

        /// <summary>获取本次请求的目标配置。</summary>
        public SceneLoadConfig Config { get; }
        /// <summary>获取共享 Addressables 场景句柄模块。</summary>
        public AddressableSceneLoadModule SceneModule { get; }
        /// <summary>获取此次初始化是否已处于目标场景。</summary>
        public bool InitializesCurrentScene { get; }
        /// <summary>获取目标 Scene 是否已经激活并通过名称校验。</summary>
        public bool TargetSceneReady => sharedState.TargetSceneReady;
        /// <summary>获取当前已验证的目标 Scene；目标未就绪时返回无效场景。</summary>
        public Scene ActiveScene => sharedState.ActiveScene;
        /// <summary>获取当前任务及其组合祖先的诊断路径。</summary>
        public string TaskPath => string.Join(" / ", activeTaskPath.ConvertAll(task => task.name));
        /// <summary>获取当前任务引用在本次树执行中的独立位置标识。</summary>
        public string ReferencePath => referencePath;

        #endregion

        #region 上下文任务路径

        // 初始化此流程唯一的场景状态容器。
        /// <summary>创建一次场景加载流程的运行时上下文。</summary>
        /// <param name="config">目标场景配置。</param>
        /// <param name="sceneModule">负责场景加载与句柄生命周期的模块。</param>
        /// <param name="initializesCurrentScene">是否只初始化已经加载的当前场景。</param>
        public SceneLoadContext(
            SceneLoadConfig config,
            AddressableSceneLoadModule sceneModule,
            bool initializesCurrentScene)
            : this(config, sceneModule, initializesCurrentScene, new SceneLoadProgressTracker(config),
                new SharedSceneLoadState(), string.Empty, null, new List<SceneLoadTask>())
        {
        }

        /// <summary>创建复用统一流程进度跟踪器的场景上下文。</summary>
        /// <param name="config">目标场景配置。</param>
        /// <param name="sceneModule">负责场景加载与句柄生命周期的模块。</param>
        /// <param name="initializesCurrentScene">是否只初始化已经加载的当前场景。</param>
        /// <param name="progressTracker">本次流程唯一的运行时进度跟踪器。</param>
        public SceneLoadContext(
            SceneLoadConfig config,
            AddressableSceneLoadModule sceneModule,
            bool initializesCurrentScene,
            SceneLoadProgressTracker progressTracker)
            : this(config, sceneModule, initializesCurrentScene, progressTracker,
                new SharedSceneLoadState(), string.Empty, null, new List<SceneLoadTask>())
        {
        }

        // 异步执行每个子任务前记录路径，失败时将路径与原始异常一起传播。
        /// <summary>启动一个子任务，并在失败时保留该分支的完整引用路径。</summary>
        /// <param name="task">要执行的任务资产。</param>
        /// <param name="cancellationToken">本次流程取消令牌。</param>
        /// <returns>任务执行结果。</returns>
        public UniTask ExecuteRootTaskAsync(SceneLoadTask task, CancellationToken cancellationToken) =>
            ExecuteTaskAsync(task, -1, cancellationToken);

        /// <summary>执行任务并发布其独立引用位置的进度、生命周期和失败摘要。</summary>
        /// <param name="task">要执行的任务资产。</param>
        /// <param name="childIndex">父组合中的实际列表索引；根任务使用负一。</param>
        /// <param name="cancellationToken">本次流程取消令牌。</param>
        /// <returns>任务执行与终态发布完成后的异步操作。</returns>
        internal async UniTask ExecuteTaskAsync(SceneLoadTask task, int childIndex, CancellationToken cancellationToken)
        {
            string taskReferencePath = childIndex < 0
                ? "root"
                : $"{referencePath}/{childIndex}";
            var taskPath = new List<SceneLoadTask>(activeTaskPath) { task };
            SceneLoadContext childContext = new SceneLoadContext(Config, SceneModule, InitializesCurrentScene,
                progressTracker, sharedState, taskReferencePath, task, taskPath);
            progressTracker.BeginTask(taskReferencePath);
            WSLog.Log($"[SceneLoadContext] 开始执行场景任务，sceneId={Config.SceneId}，taskPath={childContext.TaskPath}。");
            try
            {
                await task.ExecuteAsync(childContext, cancellationToken);
                progressTracker.CompleteTask(taskReferencePath);
                WSLog.Log($"[SceneLoadContext] 场景任务完成，sceneId={Config.SceneId}，taskPath={childContext.TaskPath}。");
            }
            catch (OperationCanceledException)
            {
                progressTracker.CancelTask(taskReferencePath);
                throw;
            }
            catch (System.Exception exception)
            {
                progressTracker.FailTask(taskReferencePath, exception);
                WSLog.LogError($"[SceneLoadContext] 场景任务失败，sceneId={Config.SceneId}，taskPath={childContext.TaskPath}，exception={exception}");
                throw new System.InvalidOperationException(
                    $"[SceneLoadContext] 场景任务失败，sceneId={Config.SceneId}，taskPath={childContext.TaskPath}。", exception);
            }
        }

        /// <summary>报告当前叶子任务已完成的工作比例；同一次执行的比例只允许单调前进。</summary>
        /// <param name="progress">当前任务的局部进度，范围为零到一。</param>
        /// <exception cref="InvalidOperationException">上下文不对应正在执行的任务时抛出。</exception>
        public void ReportProgress(float progress)
        {
            if (currentTask == null)
                throw new InvalidOperationException("[SceneLoadContext] 只能由正在执行的任务上下文报告进度。");
            progressTracker.ReportProgress(referencePath, progress);
        }

        // 场景验证完成后同步发布目标 Scene。
        /// <summary>记录经过名称校验、已经激活的目标场景。</summary>
        /// <param name="scene">已激活的目标场景。</param>
        public void SetTargetScene(Scene scene)
        {
            sharedState.ActiveScene = scene;
            sharedState.TargetSceneReady = true;
            progressTracker.SetTargetSceneReady();
        }

        /// <summary>创建共享场景状态、使用分支局部路径的轻量任务上下文视图。</summary>
        /// <param name="config">当前加载目标配置。</param>
        /// <param name="sceneModule">持有本次 Addressables 场景句柄的模块。</param>
        /// <param name="initializesCurrentScene">是否复用已经激活的当前场景。</param>
        /// <param name="progressTracker">为真实树引用位置汇总进度的跟踪器。</param>
        /// <param name="sharedState">所有并行子上下文共同读写的场景状态。</param>
        /// <param name="referencePath">当前任务引用在任务树中的路径。</param>
        /// <param name="currentTask">当前上下文对应的任务资产。</param>
        /// <param name="activeTaskPath">从根到当前任务的分支局部路径。</param>
        private SceneLoadContext(
            SceneLoadConfig config,
            AddressableSceneLoadModule sceneModule,
            bool initializesCurrentScene,
            SceneLoadProgressTracker progressTracker,
            SharedSceneLoadState sharedState,
            string referencePath,
            SceneLoadTask currentTask,
            List<SceneLoadTask> activeTaskPath)
        {
            Config = config;
            SceneModule = sceneModule;
            InitializesCurrentScene = initializesCurrentScene;
            this.sharedState = sharedState;
            this.progressTracker = progressTracker;
            this.referencePath = referencePath;
            this.currentTask = currentTask;
            this.activeTaskPath = activeTaskPath;
        }

        /// <summary>保存并行分支共同读取和写入的目标场景就绪状态。</summary>
        private sealed class SharedSceneLoadState
        {
            /// <summary>获取或设置目标场景是否已经就绪。</summary>
            public bool TargetSceneReady { get; set; }
            /// <summary>获取或设置通过配置验证的活动场景。</summary>
            public Scene ActiveScene { get; set; }
        }

        #endregion
    }
}
