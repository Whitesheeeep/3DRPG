#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RPG.Game.UI.Task;
using RPG.TaskSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.UIModule;

namespace RPG.Game.Tests
{
    /// <summary>
    /// 通过 Odin 按钮向任务窗口注入临时预览快照，手动检查分类、详情和追踪交互。
    /// </summary>
    [DisallowMultipleComponent]
    [InfoBox("仅用于 Unity Editor 手动验证；按钮会通过 UIManager 打开任务窗口并注入临时数据。")]
    public sealed class TaskWindowOdinTester : MonoBehaviour
    {
        #region 状态

        private readonly PreviewTaskWindowBackend previewBackend = new PreviewTaskWindowBackend();

        #endregion

        #region 手动操作

        /// <summary>
        /// 打开任务窗口并注入包含主线、支线、未读和追踪状态的预览数据。
        /// </summary>
        [Button("打开任务面板预览")]
        public void OpenPreview()
        {
            previewBackend.SetSnapshot(CreatePreviewSnapshot());
            OpenWindowAndBindPreview().Forget();
        }

        /// <summary>
        /// 打开任务窗口并注入空列表，用于检查详情清空和空态显示。
        /// </summary>
        [Button("预览空任务列表")]
        public void OpenEmptyPreview()
        {
            previewBackend.SetSnapshot(new TaskWindowSnapshot(
                Array.Empty<TaskWindowTaskViewData>(), null));
            OpenWindowAndBindPreview().Forget();
        }

        /// <summary>
        /// 异步打开任务窗口并将预览后端注入窗口实例。
        /// </summary>
        /// <returns>等待窗口加载和绑定结束的 UniTask。</returns>
        private async UniTaskVoid OpenWindowAndBindPreview()
        {
            if (!UIManager.Instance.IsInitialized)
            {
                Debug.LogError("[TaskWindowOdinTester] UIManager 尚未初始化，无法打开任务面板。", this);
                return;
            }

            TaskWindow window = await UIManager.Instance.PopUpWindowAsync<TaskWindow>();
            if (window == null)
            {
                Debug.LogError("[TaskWindowOdinTester] UIManager 未能加载 TaskWindow。", this);
                return;
            }

            window.BindBackend(previewBackend);
            Debug.Log("[TaskWindowOdinTester] 预览后端已绑定到任务窗口。", this);
        }

        /// <summary>创建主线、支线、未读与追踪状态都可见的预览任务集。</summary>
        /// <returns>供预览后端显示的不可变快照。</returns>
        private static TaskWindowSnapshot CreatePreviewSnapshot()
        {
            TaskCategoryId mainCategory = new TaskCategoryId(TaskCategoryCatalog.MainIdValue);
            TaskCategoryId sideCategory = new TaskCategoryId(TaskCategoryCatalog.SideIdValue);
            TaskWindowTaskViewData mainTask = new TaskWindowTaskViewData(
                new TaskId("preview.main.story"), mainCategory, "遗迹中的古老回响",
                "根据风蚀石碑的线索，查明遗迹深处传来的异响。",
                "前往遗迹入口", "守卫已经离开，可以从南侧通道进入。",
                new[]
                {
                    new TaskWindowObjectiveViewData("抵达风蚀遗迹", 0, 1),
                    new TaskWindowObjectiveViewData("调查石碑上的刻痕", 0, 3)
                },
                new[]
                {
                    new TaskWindowRewardViewData("摩拉", 12000),
                    new TaskWindowRewardViewData("大英雄的经验", 3)
                },
                TaskLifecycleState.InProgress, true);

            TaskWindowTaskViewData secondMainTask = new TaskWindowTaskViewData(
                new TaskId("preview.main.report"), mainCategory, "向巡林官报告",
                "把遗迹入口附近的发现告知巡林官。",
                "返回营地", string.Empty,
                new[] { new TaskWindowObjectiveViewData("与巡林官交谈", 0, 1) },
                new[] { new TaskWindowRewardViewData("摩拉", 8000) },
                TaskLifecycleState.Claimable, false);

            TaskWindowTaskViewData sideTask = new TaskWindowTaskViewData(
                new TaskId("preview.side.lost_goods"), sideCategory, "商人的遗失货物",
                "商人请你找回被野兽叼走的货箱。",
                "搜寻附近的足迹", "货箱可能被带到了溪流旁。",
                new[] { new TaskWindowObjectiveViewData("找到遗失的货箱", 0, 1) },
                new[] { new TaskWindowRewardViewData("摩拉", 5000) },
                TaskLifecycleState.InProgress, true);

            return new TaskWindowSnapshot(
                new[] { mainTask, secondMainTask, sideTask }, mainTask.TaskId);
        }

        #endregion

        #region 预览后端

        /// <summary>
        /// 提供内存中的任务快照和追踪操作，不连接正式 TaskSystem 状态。
        /// </summary>
        private sealed class PreviewTaskWindowBackend : ITaskWindowBackend
        {
            private TaskWindowSnapshot snapshot = new TaskWindowSnapshot(
                Array.Empty<TaskWindowTaskViewData>(), null);

            /// <summary>
            /// 预览快照发生变化时通知窗口刷新。
            /// </summary>
            public event Action Changed;

            /// <summary>
            /// 返回当前临时预览快照。
            /// </summary>
            /// <returns>只读预览快照。</returns>
            public TaskWindowSnapshot GetSnapshot() => snapshot;

            /// <summary>
            /// 将一条预览任务标记为已读并通知窗口刷新。
            /// </summary>
            /// <param name="taskId">已查看的任务 ID。</param>
            public void AcknowledgeTask(TaskId taskId)
            {
                List<TaskWindowTaskViewData> tasks = new List<TaskWindowTaskViewData>(snapshot.Tasks.Count);
                bool changed = false;
                for (int index = 0; index < snapshot.Tasks.Count; index++)
                {
                    TaskWindowTaskViewData task = snapshot.Tasks[index];
                    if (task.TaskId == taskId && task.IsUnread)
                    {
                        task = task.WithUnread(false);
                        changed = true;
                    }
                    tasks.Add(task);
                }

                if (changed)
                    ReplaceSnapshot(tasks, snapshot.TrackedTaskId);
            }

            /// <summary>
            /// 设置预览任务追踪状态并通知窗口刷新。
            /// </summary>
            /// <param name="taskId">要追踪的任务。</param>
            /// <returns>快照中存在该任务时返回 true。</returns>
            public bool TrySetTrackedTask(TaskId taskId)
            {
                bool taskExists = false;
                for (int index = 0; index < snapshot.Tasks.Count; index++)
                {
                    if (snapshot.Tasks[index].TaskId == taskId)
                    {
                        taskExists = true;
                        break;
                    }
                }

                if (!taskExists)
                    return false;
                if (snapshot.TrackedTaskId == taskId)
                    return true;

                ReplaceSnapshot(snapshot.Tasks, taskId);
                return true;
            }

            /// <summary>
            /// 清除预览追踪任务并通知窗口刷新。
            /// </summary>
            public void ClearTrackedTask()
            {
                if (snapshot.TrackedTaskId.HasValue)
                    ReplaceSnapshot(snapshot.Tasks, null);
            }

            /// <summary>
            /// 替换临时预览数据并触发一次快照变更通知。
            /// </summary>
            /// <param name="nextSnapshot">新的预览快照。</param>
            public void SetSnapshot(TaskWindowSnapshot nextSnapshot)
            {
                snapshot = nextSnapshot;
                Changed?.Invoke();
            }

            /// <summary>保存不可变的新预览快照并通知绑定窗口。</summary>
            /// <param name="tasks">替换后的预览任务。</param>
            /// <param name="trackedTaskId">替换后的追踪任务 ID。</param>
            private void ReplaceSnapshot(IEnumerable<TaskWindowTaskViewData> tasks, TaskId? trackedTaskId)
            {
                snapshot = new TaskWindowSnapshot(tasks, trackedTaskId);
                Changed?.Invoke();
            }
        }

        #endregion
    }
}
#endif
