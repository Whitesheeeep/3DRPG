using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WS_Modules.SceneModule
{
    /// <summary>描述一次完整场景准备流程的运行状态。</summary>
    public enum E_SceneLoadExecutionState
    {
        /// <summary>场景或至少一个后续准备任务仍在运行。</summary>
        Loading,
        /// <summary>根任务以及目标场景校验均已完成。</summary>
        Succeeded,
        /// <summary>任一任务或界面准备阶段失败。</summary>
        Failed,
        /// <summary>流程等待被协作式取消。</summary>
        Cancelled
    }

    /// <summary>描述任务树中一个具体引用位置的运行状态。</summary>
    public enum E_SceneLoadTaskState
    {
        /// <summary>任务还未启动。</summary>
        Pending,
        /// <summary>任务当前正在执行。</summary>
        Running,
        /// <summary>任务已完整执行成功。</summary>
        Succeeded,
        /// <summary>任务发生失败。</summary>
        Failed,
        /// <summary>当前任务等待被取消。</summary>
        Cancelled
    }

    /// <summary>提供不暴露运行时可变状态的任务进度快照。</summary>
    public sealed class SceneLoadTaskSnapshot
    {
        /// <summary>获取任务在本次树执行中的稳定引用位置。</summary>
        public string ReferencePath { get; }
        /// <summary>获取任务在资产中的显示名称。</summary>
        public string TaskName { get; }
        /// <summary>获取此叶子任务在总体进度中的权重。</summary>
        public float Weight { get; }
        /// <summary>获取任务局部进度。</summary>
        public float Progress { get; }
        /// <summary>获取任务当前运行状态。</summary>
        public E_SceneLoadTaskState State { get; }
        /// <summary>获取可供 UI 显示的错误摘要。</summary>
        public string FailureMessage { get; }

        /// <summary>创建一个不可变任务进度快照。</summary>
        /// <param name="referencePath">本次执行中的任务引用位置。</param>
        /// <param name="taskName">任务资产名称。</param>
        /// <param name="weight">总体进度权重。</param>
        /// <param name="progress">局部进度。</param>
        /// <param name="state">任务运行状态。</param>
        /// <param name="failureMessage">失败摘要。</param>
        public SceneLoadTaskSnapshot(string referencePath, string taskName, float weight, float progress,
            E_SceneLoadTaskState state, string failureMessage)
        {
            ReferencePath = referencePath;
            TaskName = taskName;
            Weight = weight;
            Progress = progress;
            State = state;
            FailureMessage = failureMessage;
        }
    }

    /// <summary>承载展示实现、进度订阅者和调试界面共用的一致流程快照。</summary>
    public sealed class SceneLoadExecutionSnapshot
    {
        /// <summary>获取流程唯一标识，用于忽略上一轮流程的迟到回调。</summary>
        public Guid FlowId { get; }
        /// <summary>获取稳定场景 ID。</summary>
        public string SceneId { get; }
        /// <summary>获取场景友好显示名称。</summary>
        public string DisplayName { get; }
        /// <summary>获取任务树加权完成比例。</summary>
        public float Progress { get; }
        /// <summary>获取流程状态。</summary>
        public E_SceneLoadExecutionState State { get; }
        /// <summary>获取是否已经激活并校验目标场景。</summary>
        public bool TargetSceneReady { get; }
        /// <summary>获取最后失败任务或流程的可读错误摘要。</summary>
        public string FailureMessage { get; }
        /// <summary>获取任务树中各引用位置的不可变快照。</summary>
        public IReadOnlyList<SceneLoadTaskSnapshot> Tasks { get; }

        /// <summary>创建不可变的完整流程快照。</summary>
        /// <param name="flowId">流程唯一标识。</param>
        /// <param name="sceneId">稳定场景 ID。</param>
        /// <param name="displayName">场景友好名称。</param>
        /// <param name="progress">总体加权进度。</param>
        /// <param name="state">流程状态。</param>
        /// <param name="targetSceneReady">目标场景是否已就绪。</param>
        /// <param name="failureMessage">流程失败摘要。</param>
        /// <param name="tasks">任务引用位置快照。</param>
        public SceneLoadExecutionSnapshot(Guid flowId, string sceneId, string displayName, float progress,
            E_SceneLoadExecutionState state, bool targetSceneReady, string failureMessage,
            IList<SceneLoadTaskSnapshot> tasks)
        {
            FlowId = flowId;
            SceneId = sceneId;
            DisplayName = displayName;
            Progress = progress;
            State = state;
            TargetSceneReady = targetSceneReady;
            FailureMessage = failureMessage;
            Tasks = new ReadOnlyCollection<SceneLoadTaskSnapshot>(new List<SceneLoadTaskSnapshot>(tasks));
        }
    }
}
