using System;
using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.SceneModule
{
    /// <summary>为本次任务树执行分配引用位置并汇总叶子任务的加权完成进度。负责汇总所有任务的执行状态和进度信息。</summary>
    public sealed class SceneLoadProgressTracker
    {
        #region 运行状态

        // key：任务树引用路径；value：该引用位置独立的资产、权重和执行状态。
        private readonly Dictionary<string, MutableSceneLoadTaskProgress> taskProgressByReferencePathMap = new();
        private readonly List<string> taskReferencePathOrder = new();
        private readonly SceneLoadConfig config;
        private readonly Guid flowId;
        private float totalLeafWeight;
        private bool targetSceneReady;
        private E_SceneLoadExecutionState executionState = E_SceneLoadExecutionState.Loading;
        private string failureMessage;

        #endregion

        #region 事件

        /// <summary>快照中的任一公开状态变化时触发。</summary>
        public event Action<SceneLoadExecutionSnapshot> SnapshotChanged;
        /// <summary>单个引用位置开始、更新或结束时触发。</summary>
        public event Action<SceneLoadTaskSnapshot> TaskChanged;

        #endregion

        #region 创建与属性

        /// <summary>创建本次流程的进度表，并为共享资产的每个引用位置建立独立记录。</summary>
        /// <param name="config">已校验的场景配置。</param>
        public SceneLoadProgressTracker(SceneLoadConfig config)
        {
            this.config = config;
            flowId = Guid.NewGuid();
            CollectTask(config.RootTask, "root", new HashSet<SceneLoadTask>());
        }

        /// <summary>获取本次流程唯一标识。</summary>
        public Guid FlowId => flowId;
        /// <summary>获取当前完整快照。</summary>
        public SceneLoadExecutionSnapshot Snapshot => CreateSnapshot();

        #endregion

        #region 任务生命周期

        /// <summary>标记指定引用位置已进入执行。</summary>
        /// <param name="referencePath">任务引用路径。</param>
        public void BeginTask(string referencePath)
        {
            MutableSceneLoadTaskProgress taskProgress = taskProgressByReferencePathMap[referencePath];
            taskProgress.State = E_SceneLoadTaskState.Running;
            PublishTask(taskProgress);
        }

        /// <summary>按单调前进规则更新任务局部进度并发布最新快照。</summary>
        /// <param name="referencePath">任务引用路径。</param>
        /// <param name="progress">任务局部进度。</param>
        /// <exception cref="ArgumentOutOfRangeException">进度是 NaN 或无穷值时抛出。</exception>
        public void ReportProgress(string referencePath, float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress))
                throw new ArgumentOutOfRangeException(nameof(progress), "任务进度必须是有限数值。");
            MutableSceneLoadTaskProgress taskProgress = taskProgressByReferencePathMap[referencePath];
            taskProgress.Progress = Mathf.Max(taskProgress.Progress, Mathf.Clamp01(progress));
            PublishTask(taskProgress);
        }

        /// <summary>标记任务成功并把其局部进度补足至 100%。</summary>
        /// <param name="referencePath">任务引用路径。</param>
        public void CompleteTask(string referencePath)
        {
            MutableSceneLoadTaskProgress taskProgress = taskProgressByReferencePathMap[referencePath];
            taskProgress.Progress = 1f;
            taskProgress.State = E_SceneLoadTaskState.Succeeded;
            PublishTask(taskProgress);
        }

        /// <summary>记录任务失败摘要，保留原始异常由执行调用栈传播。</summary>
        /// <param name="referencePath">任务引用路径。</param>
        /// <param name="exception">导致失败的异常。</param>
        public void FailTask(string referencePath, Exception exception)
        {
            MutableSceneLoadTaskProgress taskProgress = taskProgressByReferencePathMap[referencePath];
            taskProgress.State = E_SceneLoadTaskState.Failed;
            taskProgress.FailureMessage = exception.Message;
            failureMessage ??= $"任务引用 '{referencePath}' 失败：{exception.Message}";
            PublishTask(taskProgress);
        }

        /// <summary>标记协作式取消状态。</summary>
        /// <param name="referencePath">被取消的任务引用路径。</param>
        public void CancelTask(string referencePath)
        {
            MutableSceneLoadTaskProgress taskProgress = taskProgressByReferencePathMap[referencePath];
            taskProgress.State = E_SceneLoadTaskState.Cancelled;
            PublishTask(taskProgress);
        }

        /// <summary>更新已经激活并校验的目标场景状态。</summary>
        public void SetTargetSceneReady()
        {
            targetSceneReady = true;
            PublishSnapshot();
        }

        /// <summary>标记根任务与最终场景校验全部成功；成功终态精确显示 100%。</summary>
        public void Succeed()
        {
            executionState = E_SceneLoadExecutionState.Succeeded;
            PublishSnapshot();
        }

        /// <summary>标记完整流程失败。</summary>
        /// <param name="exception">失败原因。</param>
        public void Fail(Exception exception)
        {
            executionState = E_SceneLoadExecutionState.Failed;
            failureMessage ??= exception.Message;
            PublishSnapshot();
        }

        /// <summary>标记完整流程取消。</summary>
        public void Cancel()
        {
            executionState = E_SceneLoadExecutionState.Cancelled;
            PublishSnapshot();
        }

        #endregion

        #region 进度汇总

        /// <summary>按真实树路径递归建立每个任务引用位置的运行记录。</summary>
        /// <param name="task">当前任务资产。</param>
        /// <param name="referencePath">当前引用位置路径。</param>
        /// <param name="ancestorSet">当前递归祖先，用于尽早拒绝无效循环树。</param>
        private void CollectTask(SceneLoadTask task, string referencePath, HashSet<SceneLoadTask> ancestorSet)
        {
            if (!ancestorSet.Add(task))
                throw new InvalidOperationException($"[SceneLoadProgressTracker] 进度树检测到循环引用，path={referencePath}。");

            float weight = task is SequenceSceneLoadTask or ParallelSceneLoadTask
                ? 0f
                : task.ProgressWeight;
            var progress = new MutableSceneLoadTaskProgress(referencePath, task.name, weight);
            taskProgressByReferencePathMap.Add(referencePath, progress);
            taskReferencePathOrder.Add(referencePath);
            totalLeafWeight += weight;

            // Sequence 和 Parallel 任务的子任务引用位置按顺序编号，形成唯一路径。
            // Sequence 和 Parallel 任务的进度权重为 0，只有叶子任务才贡献总体进度。
            IReadOnlyList<SceneLoadTask> children = task switch
            {
                SequenceSceneLoadTask sequence => sequence.Children,
                ParallelSceneLoadTask parallel => parallel.Children,
                _ => null
            };
            if (children != null)
            {
                for (int index = 0; index < children.Count; index++)
                    CollectTask(children[index], $"{referencePath}/{index}", ancestorSet);
            }
            ancestorSet.Remove(task);
        }

        /// <summary>生成一致的不可变流程与全部任务引用快照。</summary>
        /// <returns>当前加载状态快照。</returns>
        private SceneLoadExecutionSnapshot CreateSnapshot()
        {
            var taskSnapshots = new List<SceneLoadTaskSnapshot>(taskReferencePathOrder.Count);
            float weightedProgress = 0f;
            // Sequence 和 Parallel 任务的进度权重为 0，对于总体进度无影响， 只有叶子任务才贡献总体进度。
            for (int index = 0; index < taskReferencePathOrder.Count; index++)
            {
                MutableSceneLoadTaskProgress item = taskProgressByReferencePathMap[taskReferencePathOrder[index]];
                taskSnapshots.Add(item.ToSnapshot());
                weightedProgress += item.Weight * item.Progress;
            }

            float progress = totalLeafWeight <= 0f ? 0f : weightedProgress / totalLeafWeight;
            if (executionState != E_SceneLoadExecutionState.Succeeded)
                progress = Mathf.Min(progress, 0.99f);
            else
                progress = 1f;

            return new SceneLoadExecutionSnapshot(flowId, config.SceneId, config.DisplayName, progress,
                executionState, targetSceneReady, failureMessage, taskSnapshots);
        }

        /// <summary>先发布任务快照，再发布与其相同版本的完整树快照。</summary>
        /// <param name="taskProgress">刚更新的任务运行记录。</param>
        private void PublishTask(MutableSceneLoadTaskProgress taskProgress)
        {
            TaskChanged?.Invoke(taskProgress.ToSnapshot());
            PublishSnapshot();
        }

        /// <summary>向订阅者发布完整流程的新快照。</summary>
        private void PublishSnapshot() => SnapshotChanged?.Invoke(CreateSnapshot());

        /// <summary>保存一个特定引用位置的可变运行状态。</summary>
        private sealed class MutableSceneLoadTaskProgress
        {
            /// <summary>获取引用路径。</summary>
            public string ReferencePath { get; }
            /// <summary>获取任务名称。</summary>
            public string TaskName { get; }
            /// <summary>获取进度权重。</summary>
            public float Weight { get; }
            /// <summary>获取或设置局部进度。</summary>
            public float Progress { get; set; }
            /// <summary>获取或设置任务状态。</summary>
            public E_SceneLoadTaskState State { get; set; }
            /// <summary>获取或设置失败摘要。</summary>
            public string FailureMessage { get; set; }

            /// <summary>创建一个任务执行引用记录。</summary>
            /// <param name="referencePath">树中的具体引用位置。</param>
            /// <param name="taskName">任务资产名称。</param>
            /// <param name="weight">总体进度权重。</param>
            public MutableSceneLoadTaskProgress(string referencePath, string taskName, float weight)
            {
                ReferencePath = referencePath;
                TaskName = taskName;
                Weight = weight;
                State = E_SceneLoadTaskState.Pending;
            }

            /// <summary>复制为不暴露可变状态的任务快照。</summary>
            /// <returns>任务快照。</returns>
            public SceneLoadTaskSnapshot ToSnapshot() =>
                new(ReferencePath, TaskName, Weight, Progress, State, FailureMessage);
        }

        #endregion
    }
}
