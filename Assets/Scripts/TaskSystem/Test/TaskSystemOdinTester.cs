#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using RPG.CurrencySystemNS;
using RPG.Game;
using RPG.TaskSystem;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using WS_Modules.BusinessArchitecture;
using WS_Modules.CustomEventSystem;

namespace RPG.TaskSystem.Tests
{
    /// <summary>
    /// 通过 Odin 按钮验证统一接取、阶段推进、货币领奖和任务快照恢复。
    /// </summary>
    public sealed class TaskSystemOdinTester : MonoBehaviour
    {
        #region 测试依赖字段

        // 依赖字段：临时替换任务配置和编排 System，以隔离本按钮验证的领域事件与奖励钱包。
        private TaskProgressSystem testSystem;
        private TaskDatabase testDatabase;
        private TestCurrencyWallet testWallet;

        #endregion

        #region 测试状态字段

        [ShowInInspector, ReadOnly]
        private string lastStatus = "Idle";

        private TaskDatabase previousDatabase;
        private TaskSaveSnapshot previousSnapshot;
        private TaskProgressSystem architectureTaskSystem;
        private bool restoreManagerAfterTest;

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
                TaskStageId firstStageId = new TaskStageId("stage.first");
                TaskStageId secondStageId = new TaskStageId("stage.second");

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
                        new TaskRewardDefinition[]
                        {
                            new TaskCurrencyRewardDefinition(new[]
                            {
                                new TaskCurrencyRewardEntry(CurrencyId.Mola, 1)
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
                        new TaskRewardDefinition[]
                        {
                            new TaskCurrencyRewardDefinition(new[]
                            {
                                new TaskCurrencyRewardEntry(CurrencyId.Mola, 1)
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
                                    new TestObjectiveDefinition("objective.first", "first", 1)
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
                        new TaskRewardDefinition[]
                        {
                            new TaskCurrencyRewardDefinition(new[]
                            {
                                new TaskCurrencyRewardEntry(CurrencyId.Mola, 8),
                                new TaskCurrencyRewardEntry(CurrencyId.Mola, 12)
                            })
                        })
                });

                TaskManager taskManager = TaskManager.Instance;
                architectureTaskSystem = GameArchitecture.Interface.GetSystem<TaskProgressSystem>();
                previousDatabase = taskManager.IsConfigured ? taskManager.Database : null;
                previousSnapshot = previousDatabase != null ? taskManager.CaptureSnapshot() : null;
                restoreManagerAfterTest = true;
                taskManager.ResetForTests(testDatabase);
                var objectiveHandlerRegistry = new TaskObjectiveHandlerRegistry();
                objectiveHandlerRegistry.RegisterDefault();
                objectiveHandlerRegistry.RegisterDefault();
                objectiveHandlerRegistry.Register(new TestObjectiveHandler());
                var conditionHandlerRegistry = new TaskConditionHandlerRegistry();
                conditionHandlerRegistry.RegisterDefault();
                conditionHandlerRegistry.RegisterDefault();
                testWallet = new TestCurrencyWallet(20);
                testSystem = new TaskProgressSystem(
                    objectiveHandlerRegistry,
                    conditionHandlerRegistry,
                    new TaskCurrencyRewardHandler(testWallet));
                testSystem.InitializeForTests();
                Debug.Log("[TaskSystemOdinTester] 已建立临时任务资产和隔离测试钱包，开始验证任务生命周期。", this);

                Assert(
                    testSystem.GetAvailability(stagedTaskId).Status == TaskAvailabilityStatus.Locked,
                    "未完成前置任务時，阶段任务应为 Locked。");
                Assert(
                    testSystem.GetAvailability(stagedTaskId).Reasons.Count == 1,
                    "Locked 结果应包含一个结构化前置原因。");

                Assert(testSystem.TryAcceptTask(prerequisiteTaskId, TaskAcceptSource.Test).Succeeded,
                    "统一接取入口应接受无前置条件的测试任务。");
                PublishDomainEvent("prerequisite");
                Assert(testSystem.TryClaimReward(prerequisiteTaskId).Succeeded,
                    "前置任务达到待提交状态后应能领奖。");
                Assert(testSystem.GetAvailability(stagedTaskId).Status == TaskAvailabilityStatus.Available,
                    "前置任务完成后阶段任务应变为 Available。");

                Assert(testSystem.TryAcceptTask(parallelTaskId, TaskAcceptSource.Test).Succeeded,
                    "统一入口应允许另一个独立任务并行活动。");
                Assert(testSystem.TryAcceptTask(stagedTaskId, TaskAcceptSource.Test).Succeeded,
                    "前置条件满足后统一接取入口应成功。");
                Assert(!testSystem.TryAcceptTask(stagedTaskId, TaskAcceptSource.Test).Succeeded,
                    "重复接取活动任务必须被拒绝。");
                Assert(taskManager.TrySetTrackedTask(stagedTaskId), "活动任务应能设置为追踪任务。");

                PublishDomainEvent("first");
                Assert(taskManager.TryGetActiveRecord(stagedTaskId, out TaskRecord stagedRecord),
                    "阶段任务活动记录应存在。");
                Assert(stagedRecord.CurrentStageId == secondStageId,
                    "第一阶段完成后必须切换到第二阶段。");
                Assert(stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out TaskObjectiveProgress secondProgress),
                    "新阶段目标应在切换时初始化。");
                Assert(secondProgress.Current == 0, "阶段切换不能带入上一阶段目标进度。");

                Assert(taskManager.TryGetActiveRecord(parallelTaskId, out TaskRecord parallelRecord),
                    "并行任务应保留独立的活动记录。");
                Assert(parallelRecord.TryGetProgress(
                           new ObjectiveId("objective.parallel"),
                           out TaskObjectiveProgress parallelProgress),
                    "并行任务应保留独立的目标进度。");
                Assert(parallelProgress.Current == 0, "另一任务的目标事件不能推进并行任务。");
                Assert(taskManager.TrySetTrackedTask(parallelTaskId), "追踪切换应允许选择另一个活动任务。");
                PublishDomainEvent("parallel");
                Assert(parallelRecord.State == TaskLifecycleState.Claimable,
                    "并行任务应由自己的目标事件完成。");
                Assert(taskManager.TrySetTrackedTask(stagedTaskId), "可领取任务仍可以被显式追踪。");
                Assert(secondProgress.Current == 0, "切换追踪任务不能改变原任务进度。");
                Assert(taskManager.AcknowledgeTask(parallelTaskId), "点击具体任务后应能确认其未读状态。");
                Assert(taskManager.UnreadTaskIds.Count == 1, "确认并行任务后只应保留阶段任务的未读状态。");

                PublishDomainEvent("first");
                Assert(secondProgress.Current == 0, "停止的旧阶段监听不能推进新阶段目标。");

                TaskSaveSnapshot inProgressSnapshot = taskManager.CaptureSnapshot();
                taskManager.RestoreSnapshot(inProgressSnapshot);
                testSystem.RebuildRuntimes();
                Assert(taskManager.TryGetActiveRecord(stagedTaskId, out stagedRecord), "加载后活动任务应恢复。");
                Assert(stagedRecord.CurrentStageId == secondStageId, "加载后应恢复当前阶段 ID。");
                Assert(taskManager.TrackedTaskId == stagedTaskId, "加载后应恢复追踪状态。");
                Assert(taskManager.UnreadTaskIds.Count == 1,
                    "加载后应恢复阶段任务未读状态，同时保留已确认的并行任务状态。");
                Assert(!testSystem.TryGetRuntime(parallelTaskId, out _),
                    "恢复待提交的并行任务时不应重建已完成目标的监听。");

                PublishDomainEvent("first");
                Assert(stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out secondProgress),
                    "恢复后的当前阶段目标应存在。");
                Assert(secondProgress.Current == 0, "恢复后旧阶段事件仍不得推进当前目标。");
                PublishDomainEvent("second");
                Assert(secondProgress.Current == 1 && stagedRecord.State == TaskLifecycleState.InProgress,
                    "第二阶段部分进度应保留为进行中，直到目标全部完成。");

                TaskSaveSnapshot partialProgressSnapshot = taskManager.CaptureSnapshot();
                taskManager.RestoreSnapshot(partialProgressSnapshot);
                testSystem.RebuildRuntimes();
                Assert(taskManager.TryGetActiveRecord(stagedTaskId, out stagedRecord) &&
                       stagedRecord.TryGetProgress(new ObjectiveId("objective.second"), out secondProgress) &&
                       secondProgress.Current == 1,
                    "加载后必须恢复当前阶段非零目标进度。");
                PublishDomainEvent("second");
                Assert(stagedRecord.State == TaskLifecycleState.Claimable,
                    "最后阶段目标完成后任务应进入 Claimable。");

                TaskSaveSnapshot claimableSnapshot = taskManager.CaptureSnapshot();
                taskManager.RestoreSnapshot(claimableSnapshot);
                testSystem.RebuildRuntimes();
                Assert(taskManager.UnreadTaskIds.Count == 1,
                    "待提交快照恢复不能隐式清除未读事实。");
                TaskClaimResult rejectedClaim = testSystem.TryClaimReward(stagedTaskId);
                Assert(!rejectedClaim.Succeeded && rejectedClaim.Failure == TaskCommandFailure.RewardRejected,
                    "货币上限预检失败时应拒绝领奖且保持 Claimable。");
                Assert(taskManager.TryGetActiveRecord(stagedTaskId, out stagedRecord) &&
                       stagedRecord.State == TaskLifecycleState.Claimable,
                    "奖励预检失败不能清理待提交任务。");

                testWallet.SetBalance(CurrencyId.Mola, 0);
                TaskClaimResult nestedClaimResult = null;
                testWallet.BeforeAddCurrencies = () =>
                    nestedClaimResult = testSystem.TryClaimReward(stagedTaskId);
                Assert(testSystem.TryClaimReward(stagedTaskId).Succeeded,
                    "预检条件恢复后同一任务应能重试并成功领奖。");
                testWallet.BeforeAddCurrencies = null;
                Assert(nestedClaimResult != null &&
                       nestedClaimResult.Failure == TaskCommandFailure.RewardClaimInProgress,
                    "货币发放期间重入领奖必须被拒绝。");
                Assert(taskManager.IsTaskCompleted(stagedTaskId), "领奖成功后任务应加入完成 ID 集合。");
                Assert(!taskManager.TrackedTaskId.IsValid, "完成追踪任务后应清除追踪状态。");
                Assert(taskManager.UnreadTaskIds.Count == 0, "成功领奖应清理该任务的未读事实。");
                Assert(testWallet.GetBalance(CurrencyId.Mola) == 20,
                    "奖励应原子到账一次，且不能重复领取。");
                Assert(!testSystem.TryClaimReward(stagedTaskId).Succeeded &&
                       testWallet.GetBalance(CurrencyId.Mola) == 20,
                    "已完成任务再次领奖不能重复增加货币。");
                Assert(!testSystem.TryAcceptTask(stagedTaskId, TaskAcceptSource.Test).Succeeded,
                    "已完成任务不能再次接取。");

                TaskSaveSnapshot completedSnapshot = taskManager.CaptureSnapshot();
                taskManager.RestoreSnapshot(completedSnapshot);
                Assert(taskManager.IsTaskCompleted(stagedTaskId), "存档恢复应保留完成任务 ID。");

                lastStatus = "通过：资格查询、并行追踪、阶段恢复、未读恢复、领奖失败重试和完成存档均符合预期。";
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

            if (restoreManagerAfterTest)
            {
                TaskManager.Instance.RestoreAfterTests(previousDatabase, previousSnapshot);
                if (architectureTaskSystem != null && architectureTaskSystem.IsInitialized)
                {
                    architectureTaskSystem.RebuildRuntimes();
                }
                restoreManagerAfterTest = false;
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
                new TaskRewardDefinition[]
                {
                    new TaskCurrencyRewardDefinition(new[]
                    {
                        new TaskCurrencyRewardEntry(CurrencyId.Mola, 1)
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
