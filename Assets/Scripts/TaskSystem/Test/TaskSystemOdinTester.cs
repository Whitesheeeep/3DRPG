#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.Character.Animation;
using RPG.CurrencySystemNS;
using RPG.DialogueSystemModule;
using RPG.Game;
using RPG.RedDotSystemNS;
using RPG.RewardSystemNS;
using RPG.TaskSystemNS;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystemNS.Tests
{
    /// <summary>
    /// 通过 Odin 按钮验证任务生命周期、奖励提交、存档恢复和对话完成目标。
    /// </summary>
    public sealed class TaskSystemOdinTester : MonoBehaviour
    {
        #region 测试依赖字段

        // 依赖字段：临时替换任务配置和业务 System，以隔离领域事件与奖励钱包。
        private TaskSystem testSystem;
        private TaskDatabase testDatabase;
        private TestCurrencyWallet testWallet;

        #endregion

        #region 临时对话测试资源

        // 临时资源列表按创建顺序记录；清理时逆序销毁，先释放节点和参与者再释放主资产。
        private readonly List<UnityEngine.Object> temporaryDialogueObjects =
            new List<UnityEngine.Object>();

        #endregion

        #region 测试状态字段

        [ShowInInspector, ReadOnly]
        private string lastStatus = "Idle";

        private TaskDatabase previousDatabase;
        private TaskSaveSnapshot previousSnapshot;
        private TaskSystem architectureTaskSystem;
        private bool restoreTaskSystemAfterTest;

        #endregion

        #region 测试流程

        /// <summary>
        /// 执行接取资格、阶段切换、恢复、领奖失败和成功领奖的闭环验证。
        /// </summary>
        [Button("运行任务生命周期闭环测试", ButtonSizes.Large)]
        public void RunTaskLifecycleTest()
        {
            if (!Application.isPlaying)
            {
                lastStatus = "请在 Play Mode 且 GameArchitecture 已启动后运行手动测试。";
                Debug.LogWarning($"[TaskSystemOdinTester] {lastStatus}", this);
                return;
            }

            CleanupTestSystem();
            try
            {
                VerifyTaskCategoryConfiguration();

                TaskId prerequisiteTaskId = new TaskId("test.task.prerequisite");
                TaskId parallelTaskId = new TaskId("test.task.parallel");
                TaskId stagedTaskId = new TaskId("test.task.staged");
                TaskId dialogueRepeatedTaskId = new TaskId("test.task.dialogue.repeated");
                TaskId dialogueParallelTaskId = new TaskId("test.task.dialogue.parallel");
                TaskId dialogueParallelOtherTaskId = new TaskId("test.task.dialogue.parallel.other");
                TaskId dialogueStagedTaskId = new TaskId("test.task.dialogue.staged");
                TaskId dialogueRestoreTaskId = new TaskId("test.task.dialogue.restore");
                TaskStageId firstStageId = new TaskStageId("stage.first");
                TaskStageId secondStageId = new TaskStageId("stage.second");
                TestDialogueGraph targetDialogueGraph = CreateTestDialogueGraph("TaskTest_TargetDialogue");
                TestDialogueGraph otherDialogueGraph = CreateTestDialogueGraph("TaskTest_OtherDialogue");

                testDatabase = TaskDatabase.CreateRuntime(new[]
                {
                    TaskDefinition.CreateRuntime(
                        prerequisiteTaskId.Value,
                        TaskCategoryCatalog.SideIdValue,
                        "测试前置任务",
                        "用于解锁阶段测试任务。",
                        Array.Empty<TaskConditionDefinition>(),
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.prerequisite",
                                "完成前置条件",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TestObjectiveDefinition("objective.prerequisite", "prerequisite", 1)
                                })
                        },
                        new RewardDefinition[]
                        {
                            new CurrencyRewardDefinition(new[]
                            {
                                new CurrencyRewardEntry(CurrencyId.Mola, 1)
                            })
                        }),
                    TaskDefinition.CreateRuntime(
                        parallelTaskId.Value,
                        TaskCategoryCatalog.SideIdValue,
                        "并行测试任务",
                        "验证追踪切换不会改变其他活动任务进度。",
                        Array.Empty<TaskConditionDefinition>(),
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.parallel",
                                "并行阶段",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TestObjectiveDefinition("objective.parallel", "parallel", 1)
                                })
                        },
                        new RewardDefinition[]
                        {
                            new CurrencyRewardDefinition(new[]
                            {
                                new CurrencyRewardEntry(CurrencyId.Mola, 1)
                            })
                        }),
                    TaskDefinition.CreateRuntime(
                        stagedTaskId.Value,
                        TaskCategoryCatalog.SideIdValue,
                        "两阶段测试任务",
                        "验证当前阶段边界、存档恢复和可重试领奖。",
                        new TaskConditionDefinition[]
                        {
                            new TaskPrerequisiteCompletedConditionDefinition(prerequisiteTaskId.Value)
                        },
                        new[]
                        {
                            new TaskStageDefinition(
                                firstStageId.Value,
                                "第一阶段",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TestObjectiveDefinition("objective.first", "first", 1),
                                    new TestObjectiveDefinition("objective.first.secondary", "first.secondary", 1)
                                }),
                            new TaskStageDefinition(
                                secondStageId.Value,
                                "第二阶段",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TestObjectiveDefinition("objective.second", "second", 2)
                                })
                        },
                        new RewardDefinition[]
                        {
                            new CurrencyRewardDefinition(new[]
                            {
                                new CurrencyRewardEntry(CurrencyId.Mola, 8),
                                new CurrencyRewardEntry(CurrencyId.Mola, 12)
                            })
                        }),
                    CreateDialogueTaskDefinition(
                        dialogueRepeatedTaskId.Value,
                        "重复完成对话任务",
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.dialogue.repeated",
                                "重复完成指定对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.repeated",
                                        targetDialogueGraph.Asset,
                                        2)
                                })
                        }),
                    CreateDialogueTaskDefinition(
                        dialogueParallelTaskId.Value,
                        "并行对话目标任务",
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.dialogue.parallel",
                                "完成目标对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.parallel",
                                        targetDialogueGraph.Asset)
                                })
                        }),
                    CreateDialogueTaskDefinition(
                        dialogueParallelOtherTaskId.Value,
                        "其他对话目标任务",
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.dialogue.parallel.other",
                                "完成另一段对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.parallel.other",
                                        otherDialogueGraph.Asset)
                                })
                        }),
                    CreateDialogueTaskDefinition(
                        dialogueStagedTaskId.Value,
                        "同资源分阶段对话任务",
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.dialogue.first",
                                "第一次对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.first",
                                        targetDialogueGraph.Asset)
                                }),
                            new TaskStageDefinition(
                                "stage.dialogue.second",
                                "第二次对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.second",
                                        targetDialogueGraph.Asset)
                                })
                        }),
                    CreateDialogueTaskDefinition(
                        dialogueRestoreTaskId.Value,
                        "恢复对话目标任务",
                        new[]
                        {
                            new TaskStageDefinition(
                                "stage.dialogue.restore",
                                "恢复并完成对话",
                                string.Empty,
                                new TaskObjectiveDefinition[]
                                {
                                    new TaskDialogueCompletedObjectiveDefinition(
                                        "objective.dialogue.restore",
                                        targetDialogueGraph.Asset,
                                        2)
                                })
                        })
                });

                architectureTaskSystem = GameArchitecture.Interface.GetSystem<TaskSystem>();
                previousDatabase = TaskConfigManager.Instance.IsConfigured
                    ? TaskConfigManager.Instance.Database
                    : null;
                previousSnapshot = previousDatabase != null ? architectureTaskSystem.CaptureSnapshot() : null;
                restoreTaskSystemAfterTest = true;
                architectureTaskSystem.ClearPlayerTaskState();
                TaskConfigManager.ResetForTests(testDatabase);
                var objectiveHandlerRegistry = new TaskObjectiveHandlerRegistry();
                objectiveHandlerRegistry.RegisterDefault();
                objectiveHandlerRegistry.RegisterDefault();
                Assert(
                    objectiveHandlerRegistry.Resolve(new TaskDialogueCompletedObjectiveDefinition(
                        "objective.dialogue.registry",
                        targetDialogueGraph.Asset)) is TaskDialogueCompletedObjectiveHandler,
                    "默认目标注册表应显式登记对话完成 Handler，重复默认注册保持幂等。");
                objectiveHandlerRegistry.Register(new TestObjectiveHandler());
                var conditionHandlerRegistry = new TaskConditionHandlerRegistry();
                conditionHandlerRegistry.RegisterDefault();
                conditionHandlerRegistry.RegisterDefault();
                testWallet = new TestCurrencyWallet(20);
                var rewardHandlerRegistry = new RewardHandlerRegistry();
                var testCurrencyRewardHandler = new TestCurrencyRewardHandler(testWallet);
                rewardHandlerRegistry.RegisterDefault(testCurrencyRewardHandler);
                rewardHandlerRegistry.RegisterDefault(testCurrencyRewardHandler);
                Assert(rewardHandlerRegistry.Count == 1, "奖励默认 Handler 重复调用不能重复登记。");
                var testRewardSystem = new RewardSystem(rewardHandlerRegistry);
                testSystem = new TaskSystem(
                    objectiveHandlerRegistry,
                    conditionHandlerRegistry,
                    testRewardSystem,
                    GameArchitecture.Interface.GetSystem<RedDotSystem>(),
                    RedDotBusinessConfigProvider.GetConfig<TaskRedDotConfig>());
                testSystem.InitializeForTests();
                RedDotSystem taskRedDotSystem = GameArchitecture.Interface.GetSystem<RedDotSystem>();
                TaskRedDotConfig taskRedDotConfig = RedDotBusinessConfigProvider.GetConfig<TaskRedDotConfig>();
                Debug.Log("[TaskSystemOdinTester] 已建立临时任务资产和隔离测试钱包，开始验证任务生命周期。", this);

                Assert(
                    testSystem.GetAvailability(stagedTaskId).Status == TaskAvailabilityStatus.Locked,
                    "未完成前置任务時，阶段任务应为 Locked。");
                Assert(
                    testSystem.GetAvailability(stagedTaskId).Reasons.Count == 1,
                    "Locked 结果应包含一个结构化前置原因。");

                Assert(testSystem.TryAcceptTask(prerequisiteTaskId, E_TaskAcceptSource.Test).Succeeded,
                    "统一接取入口应接受无前置条件的测试任务。");
                Assert(taskRedDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey) == 1,
                    "接取支线任务后应立即更新支线未读红点自身值。");
                PublishDomainEvent("prerequisite");
                Assert(testSystem.TryClaimReward(prerequisiteTaskId).Succeeded,
                    "前置任务达到待提交状态后应能领奖。");
                Assert(taskRedDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey) == 0,
                    "领奖完成后应清除已完成支线任务的未读红点计数。");
                Assert(testSystem.GetAvailability(stagedTaskId).Status == TaskAvailabilityStatus.Available,
                    "前置任务完成后阶段任务应变为 Available。");

                Assert(testSystem.TryAcceptTask(parallelTaskId, E_TaskAcceptSource.Test).Succeeded,
                    "统一入口应允许另一个独立任务并行活动。");
                bool acceptanceCallbackSawListeningRuntime = false;
                IUnRegister acceptedListener = EventSystem.Register_Type<TaskAcceptedEventArgs>(
                    typeof(TaskAcceptedEventArgs),
                    acceptedEvent =>
                    {
                        if (acceptedEvent.TaskId == stagedTaskId &&
                            testSystem.TryGetRuntime(stagedTaskId, out TaskRuntime callbackRuntime))
                        {
                            acceptanceCallbackSawListeningRuntime = callbackRuntime.IsListening;
                        }
                    });
                TaskAcceptResult stagedAcceptResult;
                try
                {
                    stagedAcceptResult = testSystem.TryAcceptTask(stagedTaskId, E_TaskAcceptSource.Test);
                }
                finally
                {
                    acceptedListener.UnRegister();
                }

                Assert(stagedAcceptResult.Succeeded,
                    "前置条件满足后统一接取入口应成功。");
                Assert(acceptanceCallbackSawListeningRuntime,
                    "接取事件回调应能查询到已加入集合且监听已启动的任务实例。");
                Assert(!testSystem.TryAcceptTask(stagedTaskId, E_TaskAcceptSource.Test).Succeeded,
                    "重复接取活动任务必须被拒绝。");
                Assert(testSystem.TrySetTrackedTask(stagedTaskId), "活动任务应能设置为追踪任务。");
                Assert(testSystem.TryGetRuntime(stagedTaskId, out TaskRuntime stagedRuntime),
                    "接取后应存在完整任务运行时。");
                TaskRecord runtimeOwnedRecord = stagedRuntime.Record;
                TaskStageRuntime firstStageRuntime = stagedRuntime.CurrentStageRuntime;

                PublishDomainEvent("first");
                Assert(testSystem.TryGetActiveRecord(stagedTaskId, out TaskRecord stagedRecord),
                    "阶段任务活动记录应存在。");
                Assert(stagedRecord.CurrentStageId == firstStageId,
                    "同一阶段仍有未完成目标时不能提前切换。");
                Assert(stagedRecord.TryGetProgress(new ObjectiveId("objective.first"), out TaskObjectiveProgress firstProgress) &&
                       firstProgress.Current == 1,
                    "第一阶段的首个目标事件应只推进对应目标。");
                Assert(ReferenceEquals(runtimeOwnedRecord, stagedRecord) &&
                       ReferenceEquals(firstStageRuntime, stagedRuntime.CurrentStageRuntime),
                    "同一阶段尚未完成时，TaskRuntime、Record 和 StageRuntime 应保持不变。");

                bool stageChangedCallbackSawListeningRuntime = false;
                IUnRegister stageChangedListener = EventSystem.Register_Type<TaskStageChangedEventArgs>(
                    typeof(TaskStageChangedEventArgs),
                    stageChangedEvent =>
                    {
                        if (stageChangedEvent.TaskId == stagedTaskId &&
                            stageChangedEvent.CurrentStageId == secondStageId &&
                            testSystem.TryGetRuntime(stagedTaskId, out TaskRuntime callbackRuntime))
                        {
                            stageChangedCallbackSawListeningRuntime =
                                callbackRuntime.CurrentStageRuntime != null &&
                                callbackRuntime.CurrentStageRuntime.IsListening;
                        }
                    });
                try
                {
                    PublishDomainEvent("first.secondary");
                }
                finally
                {
                    stageChangedListener.UnRegister();
                }

                Assert(stageChangedCallbackSawListeningRuntime,
                    "阶段切换事件回调应能观察到已启动监听的新阶段运行时。");
                Assert(stagedRecord.CurrentStageId == secondStageId,
                    "第一阶段完成后必须切换到第二阶段。");
                Assert(ReferenceEquals(runtimeOwnedRecord, stagedRecord) &&
                       testSystem.TryGetRuntime(stagedTaskId, out TaskRuntime currentRuntime) &&
                       ReferenceEquals(currentRuntime, stagedRuntime),
                    "阶段切换必须保留 TaskRuntime 和其唯一 Record。");
                Assert(!ReferenceEquals(firstStageRuntime, stagedRuntime.CurrentStageRuntime),
                    "阶段切换必须替换 TaskStageRuntime。");
                Assert(stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out TaskObjectiveProgress secondProgress),
                    "新阶段目标应在切换时初始化。");
                Assert(secondProgress.Current == 0, "阶段切换不能带入上一阶段目标进度。");

                Assert(testSystem.TryGetActiveRecord(parallelTaskId, out TaskRecord parallelRecord),
                    "并行任务应保留独立的活动记录。");
                Assert(parallelRecord.TryGetProgress(
                           new ObjectiveId("objective.parallel"),
                           out TaskObjectiveProgress parallelProgress),
                    "并行任务应保留独立的目标进度。");
                Assert(parallelProgress.Current == 0, "另一任务的目标事件不能推进并行任务。");
                Assert(testSystem.TrySetTrackedTask(parallelTaskId), "追踪切换应允许选择另一个活动任务。");
                PublishDomainEvent("parallel");
                Assert(parallelRecord.State == E_TaskLifecycleState.Claimable,
                    "并行任务应由自己的目标事件完成。");
                Assert(testSystem.TrySetTrackedTask(stagedTaskId), "可领取任务仍可以被显式追踪。");
                Assert(secondProgress.Current == 0, "切换追踪任务不能改变原任务进度。");
                Assert(testSystem.AcknowledgeTask(parallelTaskId), "点击具体任务后应能确认其未读状态。");
                Assert(testSystem.UnreadTaskIds.Count == 1, "确认并行任务后只应保留阶段任务的未读状态。");
                Assert(taskRedDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey) == 1,
                    "确认查看一个任务后只应减去该任务对应的未读红点计数。");

                PublishDomainEvent("first");
                Assert(secondProgress.Current == 0, "停止的旧阶段监听不能推进新阶段目标。");

                TaskSaveSnapshot inProgressSnapshot = testSystem.CaptureSnapshot();
                testSystem.RestoreSnapshot(inProgressSnapshot);
                testSystem.RebuildRuntimes();
                Assert(testSystem.TryGetActiveRecord(stagedTaskId, out stagedRecord), "加载后活动任务应恢复。");
                Assert(stagedRecord.CurrentStageId == secondStageId, "加载后应恢复当前阶段 ID。");
                Assert(testSystem.TrackedTaskId == stagedTaskId, "加载后应恢复追踪状态。");
                Assert(testSystem.UnreadTaskIds.Count == 1,
                    "加载后应恢复阶段任务未读状态，同时保留已确认的并行任务状态。");
                Assert(taskRedDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey) == 1,
                    "快照恢复后应从恢复的未读集合重建红点自身值。");
                Assert(testSystem.TryGetRuntime(parallelTaskId, out TaskRuntime claimableRuntime) &&
                       claimableRuntime.Record.State == E_TaskLifecycleState.Claimable &&
                       claimableRuntime.CurrentStageRuntime == null,
                    "待提交任务应保留任务实例和 Record，同时不重建阶段监听。");

                PublishDomainEvent("first");
                Assert(stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out secondProgress),
                    "恢复后的当前阶段目标应存在。");
                Assert(secondProgress.Current == 0, "恢复后旧阶段事件仍不得推进当前目标。");
                PublishDomainEvent("second");
                Assert(secondProgress.Current == 1 && stagedRecord.State == E_TaskLifecycleState.InProgress,
                    "第二阶段部分进度应保留为进行中，直到目标全部完成。");

                TaskSaveSnapshot partialProgressSnapshot = testSystem.CaptureSnapshot();
                testSystem.RestoreSnapshot(partialProgressSnapshot);
                testSystem.RebuildRuntimes();
                Assert(testSystem.TryGetActiveRecord(stagedTaskId, out stagedRecord) &&
                       stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out secondProgress) &&
                       secondProgress.Current == 1,
                    "加载后必须恢复当前阶段非零目标进度。");
                PublishDomainEvent("second");
                Assert(stagedRecord.State == E_TaskLifecycleState.Claimable,
                    "最后阶段目标完成后任务应进入 Claimable。");

                TaskSaveSnapshot claimableSnapshot = testSystem.CaptureSnapshot();
                testSystem.RestoreSnapshot(claimableSnapshot);
                testSystem.RebuildRuntimes();
                Assert(testSystem.UnreadTaskIds.Count == 1,
                    "待提交快照恢复不能隐式清除未读事实。");
                TaskClaimResult rejectedClaim = testSystem.TryClaimReward(stagedTaskId);
                Assert(!rejectedClaim.Succeeded && rejectedClaim.Failure == TaskCommandFailure.RewardRejected,
                    "货币上限预检失败时应拒绝领奖且保持 Claimable。");
                Assert(testSystem.TryGetActiveRecord(stagedTaskId, out stagedRecord) &&
                       stagedRecord.State == E_TaskLifecycleState.Claimable,
                    "奖励预检失败不能清理待提交任务。");

                testWallet.SetBalance(CurrencyId.Mola, 0);
                TaskClaimResult nestedClaimResult = null;
                testWallet.BeforeAddCurrencies = () =>
                    nestedClaimResult = testSystem.TryClaimReward(stagedTaskId);
                IUnRegister throwingCompletionListener = EventSystem.Register_Type<TaskCompletedEventArgs>(
                    typeof(TaskCompletedEventArgs),
                    _ => throw new InvalidOperationException("手动测试用的任务完成订阅异常。"));
                TaskClaimResult completedClaim;
                try
                {
                    completedClaim = testSystem.TryClaimReward(stagedTaskId);
                }
                finally
                {
                    throwingCompletionListener.UnRegister();
                }
                Assert(completedClaim.Succeeded,
                    "预检条件恢复后同一任务应能重试并成功领奖。");
                testWallet.BeforeAddCurrencies = null;
                Assert(nestedClaimResult != null &&
                       nestedClaimResult.Failure == TaskCommandFailure.RewardClaimInProgress,
                    "货币发放期间重入领奖必须被拒绝。");
                Assert(testSystem.IsTaskCompleted(stagedTaskId), "领奖成功后任务应加入完成 ID 集合。");
                Assert(!testSystem.TrackedTaskId.IsValid, "完成追踪任务后应清除追踪状态。");
                Assert(testSystem.UnreadTaskIds.Count == 0, "成功领奖应清理该任务的未读事实。");
                Assert(taskRedDotSystem.GetSelfValue(taskRedDotConfig.SideUnreadKey) == 0,
                    "任务全部领奖完成后支线未读红点应归零。");
                Assert(testWallet.GetBalance(CurrencyId.Mola) == 20,
                    "奖励应原子到账一次，且不能重复领取。");
                Assert(!testSystem.TryClaimReward(stagedTaskId).Succeeded &&
                       testWallet.GetBalance(CurrencyId.Mola) == 20,
                    "已完成任务再次领奖不能重复增加货币。");
                Assert(!testSystem.TryAcceptTask(stagedTaskId, E_TaskAcceptSource.Test).Succeeded,
                    "已完成任务不能再次接取。");

                TaskSaveSnapshot completedSnapshot = testSystem.CaptureSnapshot();
                testSystem.RestoreSnapshot(completedSnapshot);
                Assert(testSystem.IsTaskCompleted(stagedTaskId), "存档恢复应保留完成任务 ID。");

                VerifyDialogueCompletedObjectiveLifecycle(
                    dialogueRepeatedTaskId,
                    dialogueParallelTaskId,
                    dialogueParallelOtherTaskId,
                    dialogueStagedTaskId,
                    dialogueRestoreTaskId,
                    targetDialogueGraph,
                    otherDialogueGraph);

                lastStatus = "通过：任务生命周期、奖励提交、快照恢复及对话完成目标的资源匹配、阶段监听和读档均符合预期。";
                Debug.Log($"[TaskSystemOdinTester] {lastStatus}", this);
            }
            catch (Exception exception)
            {
                lastStatus = $"失败：{exception.Message}";
                Debug.LogException(exception, this);
            }
            finally
            {
                CleanupTestSystem();
            }
        }

        /// <summary>
        /// 通过独立 Odin 按钮运行包含真实对话推进与任务目标断言的完整手动验证。
        /// </summary>
        [Button("验证指定对话完成任务目标", ButtonSizes.Large)]
        public void RunDialogueCompletedObjectiveTest()
        {
            RunTaskLifecycleTest();
        }

        /// <summary>
        /// 组件销毁时停止手动测试创建的目标监听并清理任务状态。
        /// </summary>
        private void OnDestroy()
        {
            CleanupTestSystem();
        }

        /// <summary>
        /// 停止测试 System，防止测试领域事件监听残留到下一次运行。
        /// </summary>
        private void CleanupTestSystem()
        {
            testSystem?.DeinitializeForTests();
            testSystem = null;

            if (restoreTaskSystemAfterTest)
            {
                TaskConfigManager.RestoreAfterTests(previousDatabase);
                if (architectureTaskSystem != null && architectureTaskSystem.IsInitialized)
                {
                    if (previousSnapshot != null)
                    {
                        architectureTaskSystem.RestoreSnapshot(previousSnapshot);
                    }

                    architectureTaskSystem.RebuildRuntimes();
                }
                restoreTaskSystemAfterTest = false;
                previousDatabase = null;
                previousSnapshot = null;
                architectureTaskSystem = null;
            }

            if (testDatabase != null)
            {
                IReadOnlyList<TaskDefinition> definitions = testDatabase.Definitions;
                for (int index = 0; index < definitions.Count; index++)
                {
                    if (definitions[index] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(definitions[index]);
                    }
                }

                UnityEngine.Object.DestroyImmediate(testDatabase);
                testDatabase = null;
                testWallet = null;
                Debug.Log("[TaskSystemOdinTester] 已销毁临时任务资产并恢复测试前的任务状态。", this);
            }

            // 任务资产销毁后再逆序释放临时对话节点、Speaker 与 DialogueAsset。
            for (int index = temporaryDialogueObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object dialogueObject = temporaryDialogueObjects[index];
                if (dialogueObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(dialogueObject);
                }
            }

            if (temporaryDialogueObjects.Count > 0)
            {
                Debug.Log(
                    $"[TaskSystemOdinTester] 已释放临时对话测试资源，objectCount={temporaryDialogueObjects.Count}。",
                    this);
                temporaryDialogueObjects.Clear();
            }
        }

        #endregion

        #region 对话完成目标生命周期验证

        /// <summary>
        /// 使用真实 DialogueSystem 入口验证资源匹配、正常结束、并行阶段和恢复监听。
        /// </summary>
        /// <param name="repeatedTaskId">需要完成两次同一对话的任务。</param>
        /// <param name="parallelTaskId">监听目标对话的并行任务。</param>
        /// <param name="parallelOtherTaskId">监听另一资源的并行任务。</param>
        /// <param name="stagedTaskId">两个阶段引用同一资源的任务。</param>
        /// <param name="restoreTaskId">验证进行中和待领奖恢复的任务。</param>
        /// <param name="targetDialogueGraph">任务要求完成的对话图。</param>
        /// <param name="otherDialogueGraph">用于验证引用不匹配的对话图。</param>
        /// <exception cref="InvalidOperationException">对话 API 或任务生命周期与预期不符时抛出。</exception>
        private void VerifyDialogueCompletedObjectiveLifecycle(
            TaskId repeatedTaskId,
            TaskId parallelTaskId,
            TaskId parallelOtherTaskId,
            TaskId stagedTaskId,
            TaskId restoreTaskId,
            TestDialogueGraph targetDialogueGraph,
            TestDialogueGraph otherDialogueGraph)
        {
            var missingAssetDefinition = new TaskDialogueCompletedObjectiveDefinition(
                "objective.dialogue.missing-asset",
                null);
            bool missingAssetRejected = false;
            try
            {
                missingAssetDefinition.Validate();
            }
            catch (ArgumentException)
            {
                missingAssetRejected = true;
            }
            Assert(missingAssetRejected, "缺失 DialogueAsset 的目标定义必须在配置校验时明确失败。");

            DialogueSystem dialogueSystem = GameArchitecture.Interface.GetSystem<DialogueSystem>();
            Assert(dialogueSystem.CurrentSession == null, "手动目标验证开始前不能存在未结束的对话会话。");

            Assert(testSystem.TryAcceptTask(repeatedTaskId, E_TaskAcceptSource.Test).Succeeded,
                "指定对话目标应能通过任务统一接取入口启动监听。");
            Assert(GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == 0,
                "接取任务不能补记监听开始前的对话历史。");

            using (StartTestDialogue(dialogueSystem, otherDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == 0,
                "完成不同 DialogueAsset 不能推进当前任务目标。");

            using (TestDialogueExecution failedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                Assert(GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == 0,
                    "仅启动目标对话不能增加进度。");
                failedDialogue.Session.End("手动验证失败结束不会推进任务。", DialogueEndStatus.Failed);
                Assert(failedDialogue.Session.EndStatus == DialogueEndStatus.Failed,
                    "失败对话路径应产生 Failed 结束状态。");
            }
            Assert(GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == 0,
                "Failed 结束不能推进指定对话目标。");

            for (int completionIndex = 0; completionIndex < 2; completionIndex++)
            {
                using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
                {
                    dialogueSystem.Advance();
                    Assert(completedDialogue.Session.EndStatus == DialogueEndStatus.Completed,
                        "Advance 进入 EndNode 后必须报告 Completed。");
                }
                Assert(
                    GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == completionIndex + 1,
                    "每次匹配的正常对话结束事件只能累计一次。");
            }
            Assert(GetDialogueProgress(repeatedTaskId, "objective.dialogue.repeated") == 2,
                "同一任务要求两次时，两次正常完成应分别累计一次。");
            Assert(GetDialogueRecord(repeatedTaskId).State == E_TaskLifecycleState.Claimable,
                "满足 Required 的指定对话目标应使最后阶段进入 Claimable。");

            Assert(testSystem.TryAcceptTask(parallelTaskId, E_TaskAcceptSource.Test).Succeeded &&
                   testSystem.TryAcceptTask(parallelOtherTaskId, E_TaskAcceptSource.Test).Succeeded,
                "监听不同对话资源的两个任务应能并行接取。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
                Assert(completedDialogue.Session.EndStatus == DialogueEndStatus.Completed,
                    "并行监听验证的目标对话应正常结束。");
            }
            Assert(GetDialogueRecord(parallelTaskId).State == E_TaskLifecycleState.Claimable,
                "目标资源的完成事件应推进匹配的并行任务。");
            Assert(GetDialogueProgress(parallelOtherTaskId, "objective.dialogue.parallel.other") == 0,
                "一个任务的对话事件不能越过资产引用匹配推进另一个任务。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, otherDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(GetDialogueRecord(parallelOtherTaskId).State == E_TaskLifecycleState.Claimable,
                "完成另一任务配置的资源后，该任务应独立达到 Claimable。");

            Assert(testSystem.TryAcceptTask(stagedTaskId, E_TaskAcceptSource.Test).Succeeded,
                "同一对话资源配置在不同阶段的任务应能接取。");
            Assert(testSystem.TryGetRuntime(stagedTaskId, out TaskRuntime stagedRuntime),
                "分阶段对话任务应创建完整 TaskRuntime。");
            TaskRecord stagedRecord = stagedRuntime.Record;
            TaskStageRuntime firstStageRuntime = stagedRuntime.CurrentStageRuntime;
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(ReferenceEquals(stagedRuntime.Record, stagedRecord) &&
                   ReferenceEquals(stagedRuntime, GetDialogueRuntime(stagedTaskId)),
                "对话任务切换阶段时 TaskRuntime 与其唯一 Record 必须保持不变。");
            Assert(stagedRecord.CurrentStageId == new TaskStageId("stage.dialogue.second") &&
                   !ReferenceEquals(firstStageRuntime, stagedRuntime.CurrentStageRuntime) &&
                   !firstStageRuntime.IsListening,
                "一次对话完成只应推进当前阶段、停止旧监听并替换 StageRuntime。");
            Assert(GetDialogueProgress(stagedTaskId, "objective.dialogue.second") == 0 &&
                   stagedRuntime.IsListening,
                "新阶段目标从零开始监听，不能消费触发切换的同一结束事件。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(stagedRecord.State == E_TaskLifecycleState.Claimable,
                "新阶段只有收到下一次目标对话完成事件后才可领奖。");

            Assert(testSystem.TryAcceptTask(restoreTaskId, E_TaskAcceptSource.Test).Succeeded,
                "读档验证任务应能接取并开始监听。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(GetDialogueProgress(restoreTaskId, "objective.dialogue.restore") == 1,
                "保存前的单次对话完成应保留为部分进度。");

            TaskSaveSnapshot inProgressSnapshot = testSystem.CaptureSnapshot();
            testSystem.RestoreSnapshot(inProgressSnapshot);
            testSystem.RebuildRuntimes();
            Assert(GetDialogueProgress(restoreTaskId, "objective.dialogue.restore") == 1 &&
                   GetDialogueRuntime(restoreTaskId).IsListening,
                "恢复进行中快照应保留进度并重建当前阶段监听，不补记事件。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(GetDialogueRecord(restoreTaskId).State == E_TaskLifecycleState.Claimable,
                "读档后新发生的指定对话完成事件应继续推进目标。");

            TaskSaveSnapshot claimableSnapshot = testSystem.CaptureSnapshot();
            testSystem.RestoreSnapshot(claimableSnapshot);
            testSystem.RebuildRuntimes();
            Assert(GetDialogueRecord(restoreTaskId).State == E_TaskLifecycleState.Claimable &&
                   !GetDialogueRuntime(restoreTaskId).IsListening,
                "恢复 Claimable 快照应保留 TaskRuntime，但不启动阶段监听。");
            using (TestDialogueExecution completedDialogue = StartTestDialogue(dialogueSystem, targetDialogueGraph))
            {
                dialogueSystem.Advance();
            }
            Assert(GetDialogueProgress(restoreTaskId, "objective.dialogue.restore") == 2 &&
                   GetDialogueRecord(restoreTaskId).State == E_TaskLifecycleState.Claimable,
                "恢复 Claimable 任务后再次完成对话不能重放进度或改变领奖状态。");
        }

        /// <summary>
        /// 获取活动对话测试任务的目标进度。
        /// </summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <param name="objectiveId">当前阶段目标标识。</param>
        /// <returns>目标当前累计数量。</returns>
        /// <exception cref="InvalidOperationException">活动 Record 或目标进度不存在时抛出。</exception>
        private int GetDialogueProgress(TaskId taskId, string objectiveId)
        {
            Assert(testSystem.TryGetActiveRecord(taskId, out TaskRecord record),
                $"对话测试任务 {taskId} 应保留活动 Record。");
            Assert(record.TryGetProgress(new ObjectiveId(objectiveId), out TaskObjectiveProgress progress),
                $"对话测试任务 {taskId} 当前阶段应包含目标 {objectiveId}。");
            return progress.Current;
        }

        /// <summary>
        /// 获取活动对话测试任务的唯一 Record。
        /// </summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>任务实例持有的可存档状态。</returns>
        /// <exception cref="InvalidOperationException">活动任务 Record 不存在时抛出。</exception>
        private TaskRecord GetDialogueRecord(TaskId taskId)
        {
            Assert(testSystem.TryGetActiveRecord(taskId, out TaskRecord record),
                $"对话测试任务 {taskId} 应保留活动 Record。");
            return record;
        }

        /// <summary>
        /// 获取活动对话测试任务的完整运行时实例。
        /// </summary>
        /// <param name="taskId">活动任务标识。</param>
        /// <returns>任务实例及其当前阶段监听。</returns>
        /// <exception cref="InvalidOperationException">活动任务运行时不存在时抛出。</exception>
        private TaskRuntime GetDialogueRuntime(TaskId taskId)
        {
            Assert(testSystem.TryGetRuntime(taskId, out TaskRuntime runtime),
                $"对话测试任务 {taskId} 应保留 TaskRuntime。");
            return runtime;
        }

        /// <summary>
        /// 创建带有单一货币奖励的临时对话目标任务资产。
        /// </summary>
        /// <param name="taskId">稳定任务标识。</param>
        /// <param name="title">任务显示名称。</param>
        /// <param name="stages">按执行顺序排列的阶段定义。</param>
        /// <returns>已经校验的临时任务资产。</returns>
        private static TaskDefinition CreateDialogueTaskDefinition(
            string taskId,
            string title,
            IEnumerable<TaskStageDefinition> stages)
        {
            return TaskDefinition.CreateRuntime(
                taskId,
                TaskCategoryCatalog.SideIdValue,
                title,
                "由指定 DialogueAsset 正常结束事件推进的手动验证任务。",
                Array.Empty<TaskConditionDefinition>(),
                stages,
                new RewardDefinition[]
                {
                    new CurrencyRewardDefinition(new[]
                    {
                        new CurrencyRewardEntry(CurrencyId.Mola, 1)
                    })
                });
        }

        /// <summary>
        /// 创建无需写入 AssetDatabase 的线性 DialogueAsset 测试图。
        /// </summary>
        /// <param name="assetName">用于日志与参与者显示的临时资源名称。</param>
        /// <returns>包含资产与对白 Speaker 的临时图。</returns>
        private TestDialogueGraph CreateTestDialogueGraph(string assetName)
        {
            DialogueSpeaker speaker = TrackDialogueObject(ScriptableObject.CreateInstance<DialogueSpeaker>());
            speaker.name = $"{assetName}_Speaker";
            DialogueAsset asset = TrackDialogueObject(ScriptableObject.CreateInstance<DialogueAsset>());
            asset.name = assetName;
            DialogueEntryNode entry = TrackDialogueObject(ScriptableObject.CreateInstance<DialogueEntryNode>());
            DialogueSpeechNode speech = TrackDialogueObject(ScriptableObject.CreateInstance<DialogueSpeechNode>());
            DialogueEndNode end = TrackDialogueObject(ScriptableObject.CreateInstance<DialogueEndNode>());

            speech.Configure(speaker, "任务系统手动验证对白。", null, 0f);
            speech.SetNextNode(end);
            asset.SetEntryNode(entry);
            asset.AddNode(speech);
            asset.AddNode(end);
            entry.SetFirstSpeechNode(speech);
            asset.EnsureStableIds();
            return new TestDialogueGraph(asset, speaker);
        }

        /// <summary>
        /// 登记一个无需保存的 Unity 测试对象，以便 Odin 测试结束后统一销毁。
        /// </summary>
        /// <typeparam name="TObject">Unity 对象类型。</typeparam>
        /// <param name="temporaryObject">待登记的临时对象。</param>
        /// <returns>已登记的原对象。</returns>
        private TObject TrackDialogueObject<TObject>(TObject temporaryObject)
            where TObject : UnityEngine.Object
        {
            temporaryObject.hideFlags = HideFlags.DontSave;
            temporaryDialogueObjects.Add(temporaryObject);
            return temporaryObject;
        }

        /// <summary>
        /// 通过实际 DialogueSystem API 启动一段临时对话。
        /// </summary>
        /// <param name="dialogueSystem">由 GameArchitecture 持有的对话 System。</param>
        /// <param name="graph">请求使用的对话图。</param>
        /// <returns>供测试推进或结束并负责清理参与者的会话包装。</returns>
        /// <exception cref="InvalidOperationException">对话 System 拒绝或未能启动临时会话时抛出。</exception>
        private static TestDialogueExecution StartTestDialogue(
            DialogueSystem dialogueSystem,
            TestDialogueGraph graph)
        {
            var participantObject = new GameObject("TaskSystemOdinTester_DialogueParticipant");
            var initiator = new TestDialogueParticipant(participantObject, graph.Speaker);
            var request = new DialogueRequest(
                graph.Asset,
                initiator,
                Array.Empty<IDialogueParticipantContext>());

            DialogueStartResult startResult;
            try
            {
                startResult = dialogueSystem.TryStartDialogue(request);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(participantObject);
                throw;
            }

            if (!startResult.Succeeded)
            {
                UnityEngine.Object.DestroyImmediate(participantObject);
                throw new InvalidOperationException(
                    $"临时对话启动失败，status={startResult.Status}, message={startResult.Message}");
            }

            return new TestDialogueExecution(startResult.Session, participantObject);
        }

        #endregion

        #region 对话目标测试夹具

        /// <summary>
        /// 保存一个临时对话资产及其首句使用的 Speaker。
        /// </summary>
        private sealed class TestDialogueGraph
        {
            /// <summary>
            /// 创建图测试夹具。
            /// </summary>
            /// <param name="asset">线性对话图资产。</param>
            /// <param name="speaker">首句 Speaker。</param>
            public TestDialogueGraph(DialogueAsset asset, DialogueSpeaker speaker)
            {
                Asset = asset;
                Speaker = speaker;
            }

            /// <summary>获取临时对话图资产。</summary>
            public DialogueAsset Asset { get; }

            /// <summary>获取对话节点引用的 Speaker 资产。</summary>
            public DialogueSpeaker Speaker { get; }
        }

        /// <summary>
        /// 为公开 DialogueRequest 提供测试参与者身份及场景对象。
        /// </summary>
        private sealed class TestDialogueParticipant : IDialogueParticipantContext
        {
            #region 依赖字段

            // 依赖字段：测试请求只需要身份匹配和发起者场景对象，不启动语音或动画表现。
            private readonly GameObject participantObject;
            private readonly DialogueSpeaker speaker;

            #endregion

            /// <summary>
            /// 创建无语音和动画表现的临时对话参与者。
            /// </summary>
            /// <param name="participantObject">请求所需的非空场景对象。</param>
            /// <param name="speaker">与首句 SpeechNode 匹配的资产。</param>
            public TestDialogueParticipant(GameObject participantObject, DialogueSpeaker speaker)
            {
                this.participantObject = participantObject;
                this.speaker = speaker;
            }

            /// <summary>获取对白使用的 Speaker。</summary>
            public DialogueSpeaker Speaker => speaker;

            /// <summary>获取发起者场景对象。</summary>
            public GameObject ParticipantObject => participantObject;

            /// <summary>测试对白不创建语音 AudioSource。</summary>
            public AudioSource VoiceAudioSource => null;

            /// <summary>测试对白不启动动画播放器。</summary>
            public IAnimationPlayer AnimationPlayer => null;
        }

        /// <summary>
        /// 管理真实测试会话及其临时发起者对象的生命周期。
        /// </summary>
        private sealed class TestDialogueExecution : IDisposable
        {
            #region 依赖字段

            // 依赖字段：会话结束后销毁 DialogueRequest 持有的参与者对象。
            private readonly GameObject participantObject;

            #endregion

            /// <summary>
            /// 创建一次已成功启动的对话测试执行。
            /// </summary>
            /// <param name="session">实际启动的对话会话。</param>
            /// <param name="participantObject">请求中发起者的场景对象。</param>
            public TestDialogueExecution(
                DialogueSession session,
                GameObject participantObject)
            {
                Session = session;
                this.participantObject = participantObject;
            }

            /// <summary>获取实际 DialogueSystem 创建的会话。</summary>
            public DialogueSession Session { get; }

            /// <summary>
            /// 确保测试结束时没有活动对话和临时场景对象残留。
            /// </summary>
            /// <exception cref="Exception">结束事件订阅者在清理会话时抛出异常。</exception>
            public void Dispose()
            {
                if (!Session.IsEnded)
                {
                    Session.End("手动任务目标验证已清理未结束的对话会话。");
                }

                if (participantObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(participantObject);
                }
            }
        }

        #endregion

        #region 分类配置校验

        /// <summary>
        /// 确认静态分类表接受已登记分类，并拒绝任务资产中的未知分类。
        /// </summary>
        private static void VerifyTaskCategoryConfiguration()
        {
            Assert(TaskCategoryCatalog.IsDefined(TaskCategoryCatalog.MainIdValue),
                "静态分类表应登记主线任务。");
            Assert(TaskCategoryCatalog.IsDefined(TaskCategoryCatalog.SideIdValue),
                "静态分类表应登记支线任务。");

            TaskDefinition invalidCategoryDefinition = TaskDefinition.CreateRuntime(
                "test.task.invalid-category",
                TaskCategoryCatalog.SideIdValue,
                "分类校验任务",
                string.Empty,
                Array.Empty<TaskConditionDefinition>(),
                new[]
                {
                    new TaskStageDefinition(
                        "stage.category-validation",
                        "分类校验阶段",
                        string.Empty,
                        new TaskObjectiveDefinition[]
                        {
                            new TestObjectiveDefinition("objective.category-validation", "category-validation", 1)
                        })
                },
                new RewardDefinition[]
                {
                    new CurrencyRewardDefinition(new[]
                    {
                        new CurrencyRewardEntry(CurrencyId.Mola, 1)
                    })
                });

            try
            {
                // 经 SerializedObject 修改临时资产，模拟旧资产中残留的未登记 ID。
                var serializedDefinition = new SerializedObject(invalidCategoryDefinition);
                serializedDefinition.FindProperty("categoryId").stringValue = "unknown.category";
                serializedDefinition.ApplyModifiedProperties();

                bool rejectedUnknownCategory = false;
                try
                {
                    invalidCategoryDefinition.Validate();
                }
                catch (ArgumentException exception)
                {
                    rejectedUnknownCategory = exception.Message.Contains("unknown.category");
                }

                Assert(rejectedUnknownCategory, "任务定义校验应拒绝并指出未知分类 ID。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(invalidCategoryDefinition);
            }
        }

        #endregion

        #region 测试领域事件与断言

        /// <summary>
        /// 发布用于手动验收的测试领域事件。
        /// </summary>
        /// <param name="eventKey">阶段目标监听的事件键。</param>
        private static void PublishDomainEvent(string eventKey)
        {
            Debug.Log("[TaskSystemOdinTester] 触发 TestDomainEvent 事件。");
            EventSystem.EventTrigger_Type(typeof(TestDomainEvent), new TestDomainEvent(eventKey));
        }

        /// <summary>
        /// 在手动测试失败时尽早指出不满足的业务期望。
        /// </summary>
        /// <param name="condition">应满足的期望。</param>
        /// <param name="message">失败说明。</param>
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        #endregion

        #region 测试目标定义与运行时

        /// <summary>
        /// 用于 Odin 场景的事件型目标定义。
        /// </summary>
        [Serializable]
        private sealed class TestObjectiveDefinition : TaskObjectiveDefinition
        {
            [SerializeField] private string eventKey = string.Empty;

            /// <summary>
            /// 创建测试目标定义。
            /// </summary>
            /// <param name="objectiveId">目标标识。</param>
            /// <param name="eventKey">推进该目标的领域事件键。</param>
            /// <param name="required">目标需求值。</param>
            public TestObjectiveDefinition(string objectiveId, string eventKey, int required)
                : base(objectiveId, required)
            {
                this.eventKey = eventKey;
            }

            /// <summary>
            /// 获取测试目标监听的事件键。
            /// </summary>
            public string EventKey => eventKey;
        }

        /// <summary>
        /// 用于驱动测试目标进度的领域事件。
        /// </summary>
        private sealed class TestDomainEvent
        {
            /// <summary>
            /// 创建测试领域事件。
            /// </summary>
            /// <param name="eventKey">事件键。</param>
            public TestDomainEvent(string eventKey)
            {
                EventKey = eventKey;
            }

            /// <summary>
            /// 获取事件键。
            /// </summary>
            public string EventKey { get; }
        }

        /// <summary>
        /// 将测试目标定义映射为可订阅测试事件的 Handler。
        /// </summary>
        private sealed class TestObjectiveHandler : TaskObjectiveHandler<TestObjectiveDefinition>
        {
            /// <summary>
            /// 创建测试目标运行时。
            /// </summary>
            /// <param name="definition">测试目标定义。</param>
            /// <param name="context">目标进度上下文。</param>
            /// <returns>可订阅测试事件的运行时。</returns>
            public override ITaskObjectiveRuntime CreateRuntime(
                TestObjectiveDefinition definition,
                ITaskObjectiveRuntimeContext context)
            {
                return new TestObjectiveRuntime(definition.EventKey, context);
            }
        }

        /// <summary>
        /// 订阅当前阶段事件并在目标停止时释放订阅句柄。
        /// </summary>
        private sealed class TestObjectiveRuntime : ITaskObjectiveRuntime
        {
            #region 依赖字段

            // 依赖字段：只按定义的事件键累计归属该任务和阶段的目标进度。
            private readonly string eventKey;
            private readonly ITaskObjectiveRuntimeContext context;

            #endregion

            private IUnRegister unregister;

            /// <summary>
            /// 创建测试目标运行时。
            /// </summary>
            /// <param name="eventKey">目标事件键。</param>
            /// <param name="context">目标进度上下文。</param>
            public TestObjectiveRuntime(string eventKey, ITaskObjectiveRuntimeContext context)
            {
                this.eventKey = eventKey;
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
            /// 仅将匹配当前目标键的事件计入当前阶段。
            /// </summary>
            /// <param name="eventArgs">测试领域事件。</param>
            private void OnDomainEvent(TestDomainEvent eventArgs)
            {
                if (string.Equals(eventArgs.EventKey, eventKey, StringComparison.Ordinal))
                {
                    context.AddProgress(1);
                }
            }
        }

        /// <summary>
        /// 将货币奖励定义接入生命周期测试的隔离钱包。
        /// </summary>
        private sealed class TestCurrencyRewardHandler : IRewardHandler
        {
            #region 依赖字段

            private readonly TestCurrencyWallet wallet;

            #endregion

            #region 构造与奖励准备

            /// <summary>创建连接隔离测试钱包的货币 Handler。</summary>
            /// <param name="wallet">手动测试钱包。</param>
            public TestCurrencyRewardHandler(TestCurrencyWallet wallet)
            {
                this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            }

            /// <summary>获取精确处理的奖励定义类型。</summary>
            public Type DefinitionType => typeof(CurrencyRewardDefinition);

            /// <summary>合并测试货币配置并准备钱包操作。</summary>
            /// <param name="definitions">货币奖励配置。</param>
            /// <param name="preparedParts">成功时返回隔离钱包批次。</param>
            /// <param name="result">奖励准备结果。</param>
            /// <returns>准备成功时返回 true。</returns>
            public bool TryPrepare(
                IReadOnlyList<RewardDefinition> definitions,
                out IReadOnlyList<IPreparedRewardPart> preparedParts,
                out RewardGrantResult result)
            {
                var amountByCurrencyIdMap = new Dictionary<CurrencyId, int>();
                var currencyOrder = new List<CurrencyId>();
                for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
                {
                    var definition = (CurrencyRewardDefinition)definitions[definitionIndex];
                    for (int entryIndex = 0; entryIndex < definition.Amounts.Count; entryIndex++)
                    {
                        CurrencyRewardEntry entry = definition.Amounts[entryIndex];
                        if (!amountByCurrencyIdMap.TryGetValue(entry.CurrencyId, out int current))
                        {
                            amountByCurrencyIdMap.Add(entry.CurrencyId, entry.Amount);
                            currencyOrder.Add(entry.CurrencyId);
                        }
                        else
                        {
                            amountByCurrencyIdMap[entry.CurrencyId] = checked(current + entry.Amount);
                        }
                    }
                }

                var amounts = new List<CurrencyAmount>(currencyOrder.Count);
                for (int index = 0; index < currencyOrder.Count; index++)
                {
                    CurrencyId currencyId = currencyOrder[index];
                    amounts.Add(new CurrencyAmount(currencyId, amountByCurrencyIdMap[currencyId]));
                }

                CurrencyOperationResult walletResult = wallet.CanAddCurrencies(amounts);
                if (!walletResult.Succeeded)
                {
                    preparedParts = Array.Empty<IPreparedRewardPart>();
                    result = new RewardGrantResult(
                        RewardGrantFailureDomain.Currency,
                        walletResult.Status,
                        currencyId: walletResult.CurrencyId);
                    return false;
                }

                preparedParts = new IPreparedRewardPart[] { new TestCurrencyRewardPart(wallet, amounts) };
                result = RewardGrantResult.Success();
                return true;
            }

            #endregion
        }

        /// <summary>隔离测试钱包的准备状态提交操作。</summary>
        private sealed class TestCurrencyRewardPart : IPreparedRewardPart
        {
            #region 依赖字段

            private readonly TestCurrencyWallet wallet;
            private readonly IReadOnlyList<CurrencyAmount> amounts;

            #endregion

            #region 构造与操作

            /// <summary>创建隔离钱包奖励批次。</summary>
            /// <param name="wallet">测试钱包。</param>
            /// <param name="amounts">已经合并的货币金额。</param>
            public TestCurrencyRewardPart(TestCurrencyWallet wallet, IReadOnlyList<CurrencyAmount> amounts)
            {
                this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
                this.amounts = amounts ?? throw new ArgumentNullException(nameof(amounts));
            }

            /// <summary>确认测试钱包仍能接收本批次金额。</summary>
            public bool CanCommit => wallet.CanAddCurrencies(amounts).Succeeded;

            /// <summary>提交测试钱包金额，并触发重入保护验证回调。</summary>
            public void CommitState()
            {
                CurrencyOperationResult result = wallet.AddCurrencies(amounts);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"测试钱包提交失败：{result.Status}。");
            }

            /// <summary>测试钱包不发布独立领域通知。</summary>
            public void PublishNotifications() { }

            #endregion
        }

        /// <summary>
        /// 仅供生命周期测试使用的可控货币钱包，支持模拟上限拒绝。
        /// </summary>
        private sealed class TestCurrencyWallet : ICurrencyWallet
        {
            // key：货币 ID；value：测试期间的钱包余额。
            private readonly Dictionary<CurrencyId, int> balanceByCurrencyIdMap =
                new Dictionary<CurrencyId, int>();
            private readonly int maxBalance;

            /// <summary>
            /// 在批量增加执行前触发的重入测试回调。
            /// </summary>
            public Action BeforeAddCurrencies { get; set; }

            /// <summary>
            /// 创建测试钱包并设置统一余额上限。
            /// </summary>
            /// <param name="maxBalance">所有测试货币的余额上限。</param>
            public TestCurrencyWallet(int maxBalance)
            {
                this.maxBalance = maxBalance;
                balanceByCurrencyIdMap.Add(CurrencyId.Mola, 0);
                balanceByCurrencyIdMap.Add(CurrencyId.YuanShi, 0);
            }

            /// <summary>
            /// 获取指定货币余额。
            /// </summary>
            /// <param name="currencyId">货币标识。</param>
            /// <returns>测试余额。</returns>
            public int GetBalance(CurrencyId currencyId) => balanceByCurrencyIdMap[currencyId];

            /// <summary>判断测试钱包是否能支付全部成本。</summary>
            /// <param name="costs">货币成本。</param>
            /// <returns>余额足够时返回 true。</returns>
            public bool CanAfford(IReadOnlyList<CurrencyAmount> costs)
            {
                for (int index = 0; index < costs.Count; index++)
                {
                    CurrencyAmount cost = costs[index];
                    if (!balanceByCurrencyIdMap.ContainsKey(cost.CurrencyId) ||
                        balanceByCurrencyIdMap[cost.CurrencyId] < cost.Amount)
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// 无副作用检查奖励是否符合钱包和余额上限。
            /// </summary>
            /// <param name="amounts">待增加金额。</param>
            /// <returns>校验状态。</returns>
            public CurrencyOperationResult CanAddCurrencies(IReadOnlyList<CurrencyAmount> amounts)
            {
                for (int index = 0; index < amounts.Count; index++)
                {
                    CurrencyAmount amount = amounts[index];
                    if (!balanceByCurrencyIdMap.ContainsKey(amount.CurrencyId))
                        return new CurrencyOperationResult(CurrencyOperationStatus.InvalidCurrency, amount.CurrencyId);
                    if (amount.Amount <= 0)
                        return new CurrencyOperationResult(CurrencyOperationStatus.InvalidAmount, amount.CurrencyId);
                    if ((long)balanceByCurrencyIdMap[amount.CurrencyId] + amount.Amount > maxBalance)
                        return new CurrencyOperationResult(CurrencyOperationStatus.BalanceLimitExceeded, amount.CurrencyId);
                }

                return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, CurrencyId.None);
            }

            /// <summary>
            /// 原子增加测试钱包中的全部货币。
            /// </summary>
            /// <param name="amounts">待增加金额。</param>
            /// <returns>操作结果。</returns>
            public CurrencyOperationResult AddCurrencies(IReadOnlyList<CurrencyAmount> amounts)
            {
                CurrencyOperationResult check = CanAddCurrencies(amounts);
                if (!check.Succeeded)
                {
                    return check;
                }

                // 模拟钱包发放事件同步调用任务领奖，确认同任务领奖保护覆盖提交窗口。
                BeforeAddCurrencies?.Invoke();
                for (int index = 0; index < amounts.Count; index++)
                {
                    CurrencyAmount amount = amounts[index];
                    balanceByCurrencyIdMap[amount.CurrencyId] += amount.Amount;
                }

                return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, CurrencyId.None);
            }

            /// <summary>
            /// 消耗测试钱包中的全部货币。
            /// </summary>
            /// <param name="costs">待消耗金额。</param>
            /// <returns>操作结果。</returns>
            public CurrencyOperationResult ConsumeCurrencies(IReadOnlyList<CurrencyAmount> costs)
            {
                if (!CanAfford(costs))
                    return new CurrencyOperationResult(CurrencyOperationStatus.InsufficientBalance, CurrencyId.None);

                for (int index = 0; index < costs.Count; index++)
                {
                    CurrencyAmount cost = costs[index];
                    balanceByCurrencyIdMap[cost.CurrencyId] -= cost.Amount;
                }

                return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, CurrencyId.None);
            }

            /// <summary>
            /// 应用测试钱包的有符号货币变化。
            /// </summary>
            /// <param name="changes">待应用变化。</param>
            /// <returns>操作结果。</returns>
            public CurrencyOperationResult ApplyChanges(IReadOnlyList<CurrencyDelta> changes)
            {
                var positiveAmounts = new List<CurrencyAmount>();
                for (int index = 0; index < changes.Count; index++)
                {
                    CurrencyDelta change = changes[index];
                    if (change.Delta > 0)
                    {
                        positiveAmounts.Add(new CurrencyAmount(change.CurrencyId, change.Delta));
                    }
                    else if (!balanceByCurrencyIdMap.ContainsKey(change.CurrencyId) ||
                             balanceByCurrencyIdMap[change.CurrencyId] < -change.Delta)
                    {
                        return new CurrencyOperationResult(CurrencyOperationStatus.InsufficientBalance, change.CurrencyId);
                    }
                }

                CurrencyOperationResult additionCheck = CanAddCurrencies(positiveAmounts);
                if (positiveAmounts.Count > 0 && !additionCheck.Succeeded)
                {
                    return additionCheck;
                }

                for (int index = 0; index < changes.Count; index++)
                {
                    CurrencyDelta change = changes[index];
                    balanceByCurrencyIdMap[change.CurrencyId] += change.Delta;
                }

                return new CurrencyOperationResult(CurrencyOperationStatus.Succeeded, CurrencyId.None);
            }

            /// <summary>
            /// 设置测试钱包余额，模拟外部消费释放余额上限。
            /// </summary>
            /// <param name="currencyId">货币标识。</param>
            /// <param name="balance">新的非负余额。</param>
            internal void SetBalance(CurrencyId currencyId, int balance)
            {
                balanceByCurrencyIdMap[currencyId] = balance;
            }
        }

        #endregion
    }
}
#endif
