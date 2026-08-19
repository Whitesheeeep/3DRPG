#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.TaskSystem;
using Sirenix.OdinInspector;
using UnityEngine;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystem.Tests
{
    /// <summary>
    /// 通过 Odin Button 验证任务实例订阅、进度更新和取消订阅生命周期。
    /// </summary>
    public sealed class TaskSystemOdinTester : MonoBehaviour
    {
        [ShowInInspector, ReadOnly]
        private string lastStatus = "Idle";

        private TaskRuntime runtime;

        /// <summary>
        /// 运行一次使用测试领域事件驱动目标进度的生命周期测试。
        /// </summary>
        [Button("运行任务实例订阅测试", ButtonSizes.Large)]
        public void RunTaskRuntimeSubscriptionTest()
        {
            try
            {
                TaskDatabase database = TaskDatabase.CreateRuntime(new[]
                {
                    new TaskDefinition(
                        "test.task.runtime",
                        "Test",
                        "运行时测试任务",
                        "验证 TaskRuntime 订阅和取消订阅。",
                        Array.Empty<TaskConditionDefinition>(),
                        new TaskObjectiveDefinition[]
                        {
                            new TestObjectiveDefinition("objective.event", 5)
                        },
                        new TaskRewardDefinition[]
                        {
                            new TestRewardDefinition()
                        })
                });

                TaskManager taskManager = TaskManager.Instance;
                taskManager.ResetForTests(database);
                if (!taskManager.TryCreateActiveRecord(new TaskId("test.task.runtime"), out _))
                {
                    throw new InvalidOperationException("无法创建测试活动任务记录。 ");
                }

                var registry = new TaskObjectiveHandlerRegistry();
                registry.Register(new TestObjectiveHandler());
                runtime = new TaskRuntime(
                    taskManager,
                    database.Definitions[0],
                    GetRequiredRecord(taskManager),
                    registry);

                runtime.StartListening();
                EventSystem.EventTrigger_Type(
                    typeof(TestDomainEvent),
                    new TestDomainEvent(2));
                AssertProgress(taskManager, 2, "第一次事件处理");

                runtime.StopListening();
                EventSystem.EventTrigger_Type(
                    typeof(TestDomainEvent),
                    new TestDomainEvent(2));
                AssertProgress(taskManager, 2, "取消订阅后事件处理");

                lastStatus = "通过：TaskRuntime 订阅、进度更新和取消订阅均符合预期。";
            }
            catch (Exception exception)
            {
                lastStatus = $"失败：{exception.Message}";
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// 组件销毁时停止测试任务运行时，避免测试事件订阅泄漏到下一次运行。
        /// </summary>
        private void OnDestroy()
        {
            runtime?.StopListening();
            runtime = null;
            TaskManager.Instance.ClearRuntimeState();
        }

        /// <summary>
        /// 获取测试任务的活动记录。
        /// </summary>
        /// <param name="taskManager">任务 Manager。</param>
        /// <returns>唯一活动任务记录。</returns>
        private static TaskRecord GetRequiredRecord(TaskManager taskManager)
        {
            if (!taskManager.TryGetActiveRecord(new TaskId("test.task.runtime"), out TaskRecord record))
            {
                throw new InvalidOperationException("测试任务记录不存在。 ");
            }

            return record;
        }

        /// <summary>
        /// 验证测试任务目标当前进度。
        /// </summary>
        /// <param name="taskManager">任务 Manager。</param>
        /// <param name="expected">预期进度。</param>
        /// <param name="phase">测试阶段名称。</param>
        private static void AssertProgress(TaskManager taskManager, int expected, string phase)
        {
            TaskRecord record = GetRequiredRecord(taskManager);
            if (!record.TryGetProgress(new ObjectiveId("objective.event"), out TaskObjectiveProgress progress) ||
                progress.Current != expected)
            {
                throw new InvalidOperationException($"{phase}：目标进度不是 {expected}。 ");
            }
        }

        /// <summary>
        /// 用于测试的事件型目标定义。
        /// </summary>
        [Serializable]
        private sealed class TestObjectiveDefinition : TaskObjectiveDefinition
        {
            /// <summary>
            /// 创建测试目标定义。
            /// </summary>
            /// <param name="objectiveId">目标标识。</param>
            /// <param name="required">目标需求。</param>
            public TestObjectiveDefinition(string objectiveId, int required)
                : base(objectiveId, required)
            {
            }
        }

        /// <summary>
        /// 用于测试的空奖励定义。
        /// </summary>
        [Serializable]
        private sealed class TestRewardDefinition : TaskRewardDefinition
        {
        }

        /// <summary>
        /// 用于验证目标 Handler 生命周期的测试领域事件。
        /// </summary>
        private sealed class TestDomainEvent
        {
            /// <summary>
            /// 创建测试领域事件。
            /// </summary>
            /// <param name="count">本次事件增加量。</param>
            public TestDomainEvent(int count)
            {
                Count = count;
            }

            /// <summary>
            /// 获取本次事件增加量。
            /// </summary>
            public int Count { get; }
        }

        /// <summary>
        /// 将测试目标定义映射为测试目标运行时的 Handler。
        /// </summary>
        private sealed class TestObjectiveHandler : TaskObjectiveHandler<TestObjectiveDefinition>
        {
            /// <summary>
            /// 创建测试目标运行时。
            /// </summary>
            /// <param name="definition">测试目标定义。</param>
            /// <param name="context">目标进度上下文。</param>
            /// <returns>测试目标运行时。</returns>
            public override ITaskObjectiveRuntime CreateRuntime(
                TestObjectiveDefinition definition,
                ITaskObjectiveRuntimeContext context)
            {
                return new TestObjectiveRuntime(context);
            }
        }

        /// <summary>
        /// 订阅测试领域事件并向任务上下文提交累计进度的运行时 Handler。
        /// </summary>
        private sealed class TestObjectiveRuntime : ITaskObjectiveRuntime
        {
            private readonly ITaskObjectiveRuntimeContext context;
            private IUnRegister unregister;

            /// <summary>
            /// 创建测试目标运行时。
            /// </summary>
            /// <param name="context">目标进度上下文。</param>
            public TestObjectiveRuntime(ITaskObjectiveRuntimeContext context)
            {
                this.context = context ?? throw new ArgumentNullException(nameof(context));
            }

            /// <summary>
            /// 注册测试领域事件监听。
            /// </summary>
            public void StartListening()
            {
                if (unregister != null)
                {
                    return;
                }

                unregister = EventSystem.Register_Type<TestDomainEvent>(
                    typeof(TestDomainEvent),
                    OnDomainEvent);
            }

            /// <summary>
            /// 取消测试领域事件监听。
            /// </summary>
            public void StopListening()
            {
                unregister?.UnRegister();
                unregister = null;
            }

            /// <summary>
            /// 处理测试领域事件并累计目标进度。
            /// </summary>
            /// <param name="eventArgs">测试领域事件。</param>
            private void OnDomainEvent(TestDomainEvent eventArgs)
            {
                context.AddProgress(eventArgs.Count);
            }
        }
    }
}
#endif
